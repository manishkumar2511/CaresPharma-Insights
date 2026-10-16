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

                CREATE TABLE IF NOT EXISTS ""TenantMcpClients"" (
                    ""Id"" UUID PRIMARY KEY,
                    ""PharmacyOwnerId"" VARCHAR(100) NOT NULL,
                    ""ClientId"" VARCHAR(100) NOT NULL UNIQUE,
                    ""ClientSecretHash"" VARCHAR(256) NOT NULL,
                    ""ClientSecretSalt"" VARCHAR(128) NOT NULL,
                    ""ClientName"" VARCHAR(150) NOT NULL,
                    ""IsActive"" BOOLEAN NOT NULL DEFAULT TRUE,
                    ""CreatedAtUtc"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                    ""ExpiresAtUtc"" TIMESTAMP WITH TIME ZONE NULL,
                    ""LastUsedAtUtc"" TIMESTAMP WITH TIME ZONE NULL,
                    ""CreatedByAdminId"" VARCHAR(100) NULL
                );

                CREATE INDEX IF NOT EXISTS ""IX_TenantMcpClients_Owner"" ON ""TenantMcpClients"" (""PharmacyOwnerId"", ""IsActive"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TenantMcpClients_ClientId"" ON ""TenantMcpClients"" (""ClientId"");

                CREATE TABLE IF NOT EXISTS ""TenantMcpActivityLogs"" (
                    ""Id"" UUID PRIMARY KEY,
                    ""PharmacyOwnerId"" VARCHAR(100) NOT NULL,
                    ""ClientId"" VARCHAR(100) NOT NULL,
                    ""EventType"" VARCHAR(50) NOT NULL,
                    ""ToolName"" VARCHAR(100) NULL,
                    ""ClientIpAddress"" VARCHAR(50) NULL,
                    ""UserAgent"" VARCHAR(255) NULL,
                    ""ExecutionDurationMs"" INT NOT NULL DEFAULT 0,
                    ""IsSuccess"" BOOLEAN NOT NULL DEFAULT TRUE,
                    ""ErrorMessage"" TEXT NULL,
                    ""TimestampUtc"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS ""IX_TenantMcpActivityLogs_Owner_Time"" ON ""TenantMcpActivityLogs"" (""PharmacyOwnerId"", ""TimestampUtc"" DESC);
            ";

            await connection.ExecuteAsync(new CommandDefinition(initSchemaSql, cancellationToken: cancellationToken));
            _logger.LogInformation("Database tables for Chat Conversations, MCP Credentials and Activity Logs initialized.");
        }
    }
}
