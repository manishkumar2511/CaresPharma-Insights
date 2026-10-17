using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Npgsql;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Shared.Configurations;

namespace CaresPharma_Insights.Infrastructure.Data
{
    public class DataSyncWorker : BackgroundService
    {
        private readonly ILogger<DataSyncWorker> _logger;
        private readonly IDbConnectionFactory _pgConnectionFactory;
        private readonly string _sqlServerConnectionString;
        private readonly TimeSpan _syncInterval;
        private readonly SyncSettings _syncSettings;
        private readonly SemaphoreSlim _syncLock = new(1, 1);

        public DataSyncWorker(
            ILogger<DataSyncWorker> logger, 
            IDbConnectionFactory pgConnectionFactory,
            IConfiguration configuration,
            IOptions<SyncSettings> syncSettingsOptions)
        {
            _logger = logger;
            _pgConnectionFactory = pgConnectionFactory;
            _sqlServerConnectionString = configuration.GetConnectionString("SqlServerSource") ?? string.Empty;
            _syncSettings = syncSettingsOptions.Value;
            _syncInterval = TimeSpan.FromMinutes(_syncSettings.SyncIntervalMinutes > 0 ? _syncSettings.SyncIntervalMinutes : 2);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dynamic Data Sync Worker initialized with {Interval} minutes interval.", _syncInterval.TotalMinutes);
            
            try 
            {
                // Warmup delay before first sync cycle
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!string.IsNullOrEmpty(_sqlServerConnectionString))
                    {
                        await SafeSyncAllTablesAsync(stoppingToken);
                    }
                    
                    await Task.Delay(_syncInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Data Sync Worker canceled gracefully.");
            }
        }

        private async Task SafeSyncAllTablesAsync(CancellationToken cancellationToken)
        {
            // Concurrency guard: Ensure multiple runs do not execute simultaneously
            if (!await _syncLock.WaitAsync(0, cancellationToken))
            {
                _logger.LogWarning("Previous sync is still in progress. Skipping cycle.");
                return;
            }

            try
            {
                _logger.LogInformation("Beginning dynamic database synchronization...");

                using SqlConnection sqlConnection = new SqlConnection(_sqlServerConnectionString);
                await sqlConnection.OpenAsync(cancellationToken);

                using IDbConnection pgDbConnection = await _pgConnectionFactory.CreateConnectionAsync(cancellationToken);
                NpgsqlConnection pgConnection = (NpgsqlConnection)pgDbConnection;

                // Retrieve base tables from SQL Server
                IEnumerable<string> rawTables = await sqlConnection.QueryAsync<string>(
                    new CommandDefinition(
                        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'",
                        cancellationToken: cancellationToken));

                List<string> tables = rawTables.ToList();

                foreach (string table in tables)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (_syncSettings.ExcludedTables.Contains(table, StringComparer.OrdinalIgnoreCase)) continue;

                    try
                    {
                        await SyncTableAtomicallyAsync(table, sqlConnection, pgConnection, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to sync table '{TableName}'. Continuing with next table.", table);
                    }
                }

                _logger.LogInformation("Dynamic synchronization cycle finished successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error encountered during database synchronization.");
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private async Task SyncTableAtomicallyAsync(
            string tableName, 
            SqlConnection sqlConn, 
            NpgsqlConnection pgConn, 
            CancellationToken ct)
        {
            _logger.LogInformation("Syncing table: {TableName}", tableName);

            // 1. Fetch source column definitions
            IEnumerable<ColumnSchema> rawColumns = await sqlConn.QueryAsync<ColumnSchema>(
                new CommandDefinition(@"
                    SELECT COLUMN_NAME as ColumnName, DATA_TYPE as DataType 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = @TableName", 
                    new { TableName = tableName },
                    cancellationToken: ct));

            List<ColumnSchema> columns = rawColumns.ToList();

            // 2. Filter sensitive column fields using strongly-typed settings
            List<ColumnSchema> safeColumns = columns.Where(c => 
                !_syncSettings.ExcludedColumnKeywords.Any(keyword => c.ColumnName.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();

            if (safeColumns.Count == 0) return;

            // 3. Create destination table in PostgreSQL if not exists
            IEnumerable<string> colDefs = safeColumns.Select(c => $"\"{c.ColumnName}\" {MapSqlTypeToPgType(c.DataType)}");
            string createTableSql = $"CREATE TABLE IF NOT EXISTS \"{tableName}\" ({string.Join(", ", colDefs)});";
            await pgConn.ExecuteAsync(new CommandDefinition(createTableSql, cancellationToken: ct));

            // 4. Create an unlogged temporary staging table with identical schema
            string stagingTable = $"staging_{tableName.ToLower()}";
            await pgConn.ExecuteAsync(new CommandDefinition(
                $"DROP TABLE IF EXISTS \"{stagingTable}\"; CREATE TEMP TABLE \"{stagingTable}\" AS SELECT * FROM \"{tableName}\" WITH NO DATA;",
                cancellationToken: ct));

            // 5. Stream from SQL Server directly into PostgreSQL staging table via Binary Importer
            string sqlColsSelect = string.Join(", ", safeColumns.Select(c => $"[{c.ColumnName}]"));
            string pgColsList = string.Join(", ", safeColumns.Select(c => $"\"{c.ColumnName}\""));

            using SqlCommand sqlCmd = new SqlCommand($"SELECT {sqlColsSelect} FROM [{tableName}]", sqlConn);
            using SqlDataReader reader = await sqlCmd.ExecuteReaderAsync(ct);

            int totalRows = 0;
            if (reader.HasRows)
            {
                using NpgsqlBinaryImporter writer = await pgConn.BeginBinaryImportAsync(
                    $"COPY \"{stagingTable}\" ({pgColsList}) FROM BINARY;", 
                    ct);

                while (await reader.ReadAsync(ct))
                {
                    await writer.StartRowAsync(ct);
                    for (int i = 0; i < safeColumns.Count; i++)
                    {
                        if (reader.IsDBNull(i))
                        {
                            await writer.WriteNullAsync(ct);
                        }
                        else
                        {
                            object val = reader.GetValue(i);
                            await writer.WriteAsync(val, ct);
                        }
                    }
                    totalRows++;
                }

                await writer.CompleteAsync(ct);
            }

            // 6. Zero-Downtime Atomic Swap: Truncate and transfer from staging inside a single transaction
            using (DbTransaction tx = await pgConn.BeginTransactionAsync(ct))
            {
                await pgConn.ExecuteAsync(new CommandDefinition(
                    $"TRUNCATE TABLE \"{tableName}\"; INSERT INTO \"{tableName}\" ({pgColsList}) SELECT {pgColsList} FROM \"{stagingTable}\";",
                    transaction: tx,
                    cancellationToken: ct));
                await tx.CommitAsync(ct);
            }

            _logger.LogInformation("Successfully synced {Count} rows for table '{TableName}'.", totalRows, tableName);
        }

        private static string MapSqlTypeToPgType(string sqlType)
        {
            return sqlType.ToLower() switch
            {
                "int" => "INTEGER",
                "bigint" => "BIGINT",
                "smallint" => "SMALLINT",
                "tinyint" => "SMALLINT",
                "bit" => "BOOLEAN",
                "decimal" or "numeric" or "money" or "smallmoney" => "NUMERIC",
                "float" => "DOUBLE PRECISION",
                "real" => "REAL",
                "datetime" or "datetime2" or "smalldatetime" => "TIMESTAMP",
                "date" => "DATE",
                "time" => "TIME",
                "uniqueidentifier" => "UUID",
                _ => "TEXT"
            };
        }

        private class ColumnSchema
        {
            public string ColumnName { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
        }
    }
}
