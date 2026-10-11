using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Infrastructure.Data
{
    public class DbInitializer
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<DbInitializer> _logger;

        public DbInitializer(IDbConnectionFactory connectionFactory, ILogger<DbInitializer> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

            string initSchemaSql = @"
                CREATE TABLE IF NOT EXISTS ""UserConversations"" (
                    ""Id"" UUID PRIMARY KEY,
                    ""UserId"" VARCHAR(100) NOT NULL,
                    ""PharmacyOwnerId"" VARCHAR(100) NULL,
                    ""Title"" VARCHAR(250) NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                    ""UpdatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS ""IX_UserConversations_UserId"" ON ""UserConversations"" (""UserId"", ""PharmacyOwnerId"");

                CREATE TABLE IF NOT EXISTS ""ConversationMessages"" (
                    ""Id"" UUID PRIMARY KEY,
                    ""ConversationId"" UUID NOT NULL REFERENCES ""UserConversations""(""Id"") ON DELETE CASCADE,
                    ""Role"" VARCHAR(20) NOT NULL,
                    ""Content"" TEXT NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS ""IX_ConversationMessages_ConversationId"" ON ""ConversationMessages"" (""ConversationId"", ""CreatedAt"");
            ";

            await connection.ExecuteAsync(new CommandDefinition(initSchemaSql, cancellationToken: cancellationToken));
            _logger.LogInformation("Database tables for Chat Conversations and Messages verified and initialized.");
        }
    }
}
