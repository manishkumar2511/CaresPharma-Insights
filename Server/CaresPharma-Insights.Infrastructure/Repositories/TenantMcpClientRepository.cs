using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Infrastructure.Repositories
{
    public class TenantMcpClientRepository : ITenantMcpClientRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public TenantMcpClientRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<TenantMcpClient?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""ClientSecretHash"", 
                    ""ClientSecretSalt"", ""ClientName"", ""IsActive"", ""CreatedAtUtc"", 
                    ""ExpiresAtUtc"", ""LastUsedAtUtc"", ""CreatedByAdminId""
                FROM ""TenantMcpClients""
                WHERE ""ClientId"" = @ClientId
                LIMIT 1;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return await connection.QueryFirstOrDefaultAsync<TenantMcpClient>(
                new CommandDefinition(sql, new { ClientId = clientId }, cancellationToken: cancellationToken));
        }

        public async Task<TenantMcpClient?> GetActiveByOwnerIdAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""ClientSecretHash"", 
                    ""ClientSecretSalt"", ""ClientName"", ""IsActive"", ""CreatedAtUtc"", 
                    ""ExpiresAtUtc"", ""LastUsedAtUtc"", ""CreatedByAdminId""
                FROM ""TenantMcpClients""
                WHERE ""PharmacyOwnerId"" = @PharmacyOwnerId AND ""IsActive"" = TRUE
                ORDER BY ""CreatedAtUtc"" DESC
                LIMIT 1;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return await connection.QueryFirstOrDefaultAsync<TenantMcpClient>(
                new CommandDefinition(sql, new { PharmacyOwnerId = pharmacyOwnerId }, cancellationToken: cancellationToken));
        }

        public async Task<IReadOnlyList<TenantMcpClient>> GetAllByOwnerIdAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""ClientSecretHash"", 
                    ""ClientSecretSalt"", ""ClientName"", ""IsActive"", ""CreatedAtUtc"", 
                    ""ExpiresAtUtc"", ""LastUsedAtUtc"", ""CreatedByAdminId""
                FROM ""TenantMcpClients""
                WHERE ""PharmacyOwnerId"" = @PharmacyOwnerId
                ORDER BY ""CreatedAtUtc"" DESC;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            IEnumerable<TenantMcpClient> results = await connection.QueryAsync<TenantMcpClient>(
                new CommandDefinition(sql, new { PharmacyOwnerId = pharmacyOwnerId }, cancellationToken: cancellationToken));
            return results.AsList();
        }

        public async Task<IReadOnlyList<TenantMcpClient>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""ClientSecretHash"", 
                    ""ClientSecretSalt"", ""ClientName"", ""IsActive"", ""CreatedAtUtc"", 
                    ""ExpiresAtUtc"", ""LastUsedAtUtc"", ""CreatedByAdminId""
                FROM ""TenantMcpClients""
                ORDER BY ""CreatedAtUtc"" DESC;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            IEnumerable<TenantMcpClient> results = await connection.QueryAsync<TenantMcpClient>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.AsList();
        }

        public async Task InsertAsync(TenantMcpClient client, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO ""TenantMcpClients"" (
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""ClientSecretHash"", 
                    ""ClientSecretSalt"", ""ClientName"", ""IsActive"", ""CreatedAtUtc"", 
                    ""ExpiresAtUtc"", ""LastUsedAtUtc"", ""CreatedByAdminId""
                ) VALUES (
                    @Id, @PharmacyOwnerId, @ClientId, @ClientSecretHash, 
                    @ClientSecretSalt, @ClientName, @IsActive, @CreatedAtUtc, 
                    @ExpiresAtUtc, @LastUsedAtUtc, @CreatedByAdminId
                );";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, client, cancellationToken: cancellationToken));
        }

        public async Task UpdateStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE ""TenantMcpClients""
                SET ""IsActive"" = @IsActive
                WHERE ""Id"" = @Id;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, IsActive = isActive }, cancellationToken: cancellationToken));
        }

        public async Task UpdateLastUsedAsync(Guid id, DateTime lastUsedAtUtc, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE ""TenantMcpClients""
                SET ""LastUsedAtUtc"" = @LastUsedAtUtc
                WHERE ""Id"" = @Id;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, LastUsedAtUtc = lastUsedAtUtc }, cancellationToken: cancellationToken));
        }

        public async Task RevokeByOwnerIdAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE ""TenantMcpClients""
                SET ""IsActive"" = FALSE
                WHERE ""PharmacyOwnerId"" = @PharmacyOwnerId;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, new { PharmacyOwnerId = pharmacyOwnerId }, cancellationToken: cancellationToken));
        }

        public async Task LogActivityAsync(TenantMcpActivityLog activityLog, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                INSERT INTO ""TenantMcpActivityLogs"" (
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""EventType"", 
                    ""ToolName"", ""ClientIpAddress"", ""UserAgent"", ""ExecutionDurationMs"", 
                    ""IsSuccess"", ""ErrorMessage"", ""TimestampUtc""
                ) VALUES (
                    @Id, @PharmacyOwnerId, @ClientId, @EventType, 
                    @ToolName, @ClientIpAddress, @UserAgent, @ExecutionDurationMs, 
                    @IsSuccess, @ErrorMessage, @TimestampUtc
                );";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, activityLog, cancellationToken: cancellationToken));
        }

        public async Task<IReadOnlyList<TenantMcpActivityLog>> GetRecentActivityLogsAsync(int count = 100, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT 
                    ""Id"", ""PharmacyOwnerId"", ""ClientId"", ""EventType"", 
                    ""ToolName"", ""ClientIpAddress"", ""UserAgent"", ""ExecutionDurationMs"", 
                    ""IsSuccess"", ""ErrorMessage"", ""TimestampUtc""
                FROM ""TenantMcpActivityLogs""
                ORDER BY ""TimestampUtc"" DESC
                LIMIT @Count;";

            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            IEnumerable<TenantMcpActivityLog> results = await connection.QueryAsync<TenantMcpActivityLog>(
                new CommandDefinition(sql, new { Count = Math.Clamp(count, 1, 500) }, cancellationToken: cancellationToken));
            return results.AsList();
        }
    }
}
