using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Infrastructure.Repositories
{
    public class ConversationRepository : IConversationRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly ILogger<ConversationRepository> _logger;

        public ConversationRepository(
            IDbConnectionFactory dbConnectionFactory,
            ILogger<ConversationRepository> logger)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _logger = logger;
        }

        public async Task<List<ConversationSummaryDto>> GetUserConversationsAsync(
            string userId, 
            string? tenantId, 
            CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            string query = @"
                SELECT ""Id"", ""Title"", ""CreatedAt"", ""UpdatedAt"" 
                FROM ""UserConversations"" 
                WHERE ""UserId"" = @UserId 
                  AND (@TenantId IS NULL OR ""PharmacyOwnerId"" = @TenantId)
                ORDER BY ""UpdatedAt"" DESC 
                LIMIT 50;";

            IEnumerable<ConversationSummaryDto> results = await connection.QueryAsync<ConversationSummaryDto>(
                new CommandDefinition(query, new { UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));

            return results.ToList();
        }

        public async Task<ConversationDetailDto?> GetConversationDetailsAsync(
            Guid conversationId, 
            string userId, 
            string? tenantId, 
            CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            string headerQuery = @"
                SELECT ""Id"", ""Title"", ""CreatedAt"" 
                FROM ""UserConversations"" 
                WHERE ""Id"" = @ConversationId 
                  AND ""UserId"" = @UserId 
                  AND (@TenantId IS NULL OR ""PharmacyOwnerId"" = @TenantId);";

            dynamic? header = await connection.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(headerQuery, new { ConversationId = conversationId, UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));

            if (header == null) return null;

            string messagesQuery = @"
                SELECT ""Content"", (""Role"" = 'user') AS ""IsUser"", ""CreatedAt"" 
                FROM ""ConversationMessages"" 
                WHERE ""ConversationId"" = @ConversationId 
                ORDER BY ""CreatedAt"" ASC;";

            IEnumerable<ChatMessageDto> messages = await connection.QueryAsync<ChatMessageDto>(
                new CommandDefinition(messagesQuery, new { ConversationId = conversationId }, cancellationToken: cancellationToken));

            return new ConversationDetailDto(
                Id: (Guid)header.Id,
                Title: (string)header.Title,
                Messages: messages.ToList(),
                CreatedAt: (DateTime)header.CreatedAt
            );
        }

        public async Task<Guid> CreateConversationAsync(
            string userId, 
            string? tenantId, 
            string title, 
            CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);
            Guid newId = Guid.NewGuid();

            string insertSql = @"
                INSERT INTO ""UserConversations"" (""Id"", ""UserId"", ""PharmacyOwnerId"", ""Title"", ""CreatedAt"", ""UpdatedAt"")
                VALUES (@Id, @UserId, @TenantId, @Title, NOW(), NOW());";

            await connection.ExecuteAsync(new CommandDefinition(
                insertSql, 
                new { Id = newId, UserId = userId, TenantId = tenantId, Title = title }, 
                cancellationToken: cancellationToken));

            return newId;
        }

        public async Task SaveMessageAsync(
            Guid conversationId, 
            string role, 
            string content, 
            CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            string insertMessageSql = @"
                INSERT INTO ""ConversationMessages"" (""Id"", ""ConversationId"", ""Role"", ""Content"", ""CreatedAt"")
                VALUES (@Id, @ConversationId, @Role, @Content, NOW());

                UPDATE ""UserConversations"" 
                SET ""UpdatedAt"" = NOW() 
                WHERE ""Id"" = @ConversationId;";

            await connection.ExecuteAsync(new CommandDefinition(
                insertMessageSql, 
                new { Id = Guid.NewGuid(), ConversationId = conversationId, Role = role, Content = content }, 
                cancellationToken: cancellationToken));
        }

        public async Task DeleteConversationAsync(
            Guid conversationId, 
            string userId, 
            string? tenantId, 
            CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            string deleteSql = @"
                DELETE FROM ""UserConversations"" 
                WHERE ""Id"" = @ConversationId 
                  AND ""UserId"" = @UserId 
                  AND (@TenantId IS NULL OR ""PharmacyOwnerId"" = @TenantId);";

            await connection.ExecuteAsync(new CommandDefinition(
                deleteSql, 
                new { ConversationId = conversationId, UserId = userId, TenantId = tenantId }, 
                cancellationToken: cancellationToken));
        }
    }
}
