using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Infrastructure.Data
{
    public class DataSyncWorker : BackgroundService
    {
        private readonly ILogger<DataSyncWorker> _logger;
        private readonly IDbConnectionFactory _pgConnectionFactory;
        private readonly string _sqlServerConnectionString;
        private readonly TimeSpan _syncInterval = TimeSpan.FromHours(1);

        // Configuration-driven keywords and tables
        private readonly string[] _sensitiveKeywords;
        private readonly string[] _excludedTables;

        public DataSyncWorker(
            ILogger<DataSyncWorker> logger, 
            IDbConnectionFactory pgConnectionFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _pgConnectionFactory = pgConnectionFactory;
            _sqlServerConnectionString = configuration.GetConnectionString("SqlServerSource") ?? "";
            
            // Read from appsettings.json
            _sensitiveKeywords = configuration.GetSection("SyncSettings:ExcludedColumnKeywords").Get<string[]>() ?? Array.Empty<string>();
            _excludedTables = configuration.GetSection("SyncSettings:ExcludedTables").Get<string[]>() ?? Array.Empty<string>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dynamic Data Sync Worker started.");
            
            try 
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!string.IsNullOrEmpty(_sqlServerConnectionString))
                    {
                        await SyncAllTablesAsync(stoppingToken);
                    }
                    
                    await Task.Delay(_syncInterval, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Dynamic Data Sync Worker is stopping.");
            }
        }

        private async Task SyncAllTablesAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting dynamic schema & data sync...");

            using SqlConnection sqlConnection = new SqlConnection(_sqlServerConnectionString);
            await sqlConnection.OpenAsync(cancellationToken);

            using IDbConnection pgConnection = await _pgConnectionFactory.CreateConnectionAsync();

            // 1. Get all tables from SQL Server
            IEnumerable<string> tables = await sqlConnection.QueryAsync<string>(
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'");

            foreach (string table in tables)
            {
                if (_excludedTables.Contains(table)) continue;
                
                await SyncTableAsync(table, sqlConnection, pgConnection);
            }

            _logger.LogInformation("Dynamic sync completed.");
        }

        private async Task SyncTableAsync(string tableName, IDbConnection sqlConn, IDbConnection pgConn)
        {
            _logger.LogInformation($"Syncing table: {tableName}");

            // 2. Get Columns
            IEnumerable<ColumnSchema> columns = await sqlConn.QueryAsync<ColumnSchema>(@"
                SELECT COLUMN_NAME as ColumnName, DATA_TYPE as DataType 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName", new { TableName = tableName });

            // Filter sensitive columns
            List<ColumnSchema> safeColumns = columns.Where(c => 
                !_sensitiveKeywords.Any(k => c.ColumnName.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();

            if (!safeColumns.Any()) return;

            // 3. Create Table in PostgreSQL if not exists
            string pgCreateTableSql = $"CREATE TABLE IF NOT EXISTS \"{tableName}\" (";
            List<string> colDefs = new List<string>();
            foreach(ColumnSchema col in safeColumns)
            {
                string pgType = MapSqlTypeToPgType(col.DataType);
                colDefs.Add($"\"{col.ColumnName}\" {pgType}");
            }
            pgCreateTableSql += string.Join(", ", colDefs) + ");";
            
            await pgConn.ExecuteAsync(pgCreateTableSql);

            // 4. Fetch Data from SQL Server
            string columnsCsv = string.Join(", ", safeColumns.Select(c => $"[{c.ColumnName}]"));
            List<dynamic> data = (await sqlConn.QueryAsync($"SELECT {columnsCsv} FROM [{tableName}]")).ToList();

            if (!data.Any()) return;

            // 5. Truncate & Load (for simple sync, since we don't dynamically know primary keys easily here, truncation is safer for full sync. For prod, we can optimize to Upsert)
            await pgConn.ExecuteAsync($"TRUNCATE TABLE \"{tableName}\"");

            // Build dynamic insert
            string insertCols = string.Join(", ", safeColumns.Select(c => $"\"{c.ColumnName}\""));
            string insertParams = string.Join(", ", safeColumns.Select(c => $"@{c.ColumnName}"));
            string insertSql = $"INSERT INTO \"{tableName}\" ({insertCols}) VALUES ({insertParams})";

            // Insert rows (Using transaction for performance)
            using IDbTransaction tx = pgConn.BeginTransaction();
            foreach(dynamic row in data)
            {
                await pgConn.ExecuteAsync(insertSql, (object)row, transaction: tx);
            }
            tx.Commit();
        }

        private string MapSqlTypeToPgType(string sqlType)
        {
            return sqlType.ToLower() switch
            {
                "int" => "INTEGER",
                "bigint" => "BIGINT",
                "smallint" => "SMALLINT",
                "tinyint" => "SMALLINT",
                "bit" => "BOOLEAN",
                "decimal" or "numeric" or "money" => "NUMERIC",
                "float" => "DOUBLE PRECISION",
                "real" => "REAL",
                "datetime" or "datetime2" or "smalldatetime" => "TIMESTAMP",
                "date" => "DATE",
                "uniqueidentifier" => "UUID",
                _ => "TEXT" // varchar, nvarchar, char, etc.
            };
        }

        private class ColumnSchema
        {
            public string ColumnName { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
        }
    }
}
