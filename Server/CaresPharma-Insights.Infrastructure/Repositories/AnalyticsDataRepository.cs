using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Infrastructure.Repositories
{
    public class AnalyticsDataRepository : IAnalyticsDataRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AnalyticsDataRepository> _logger;

        private static readonly TimeSpan SchemaCacheDuration = TimeSpan.FromHours(1);

        public AnalyticsDataRepository(
            IDbConnectionFactory dbConnectionFactory,
            IMemoryCache cache,
            ILogger<AnalyticsDataRepository> logger)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<string> GetCachedDatabaseSchemaAsync(CancellationToken cancellationToken = default)
        {
            return await _cache.GetOrCreateAsync("analytics_db_schema", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = SchemaCacheDuration;

                using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

                const string schemaSql = @"
                    SELECT table_name, column_name, data_type 
                    FROM information_schema.columns 
                    WHERE table_schema = 'public'
                    ORDER BY table_name, ordinal_position;
                ";

                IEnumerable<dynamic> columns = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(schemaSql, cancellationToken: cancellationToken));

                StringBuilder schemaBuilder = new StringBuilder();
                schemaBuilder.AppendLine("Database Schema (Tables and Columns):");

                string currentTable = string.Empty;
                foreach (dynamic col in columns)
                {
                    string tableName = (string)col.table_name;
                    if (currentTable != tableName)
                    {
                        currentTable = tableName;
                        schemaBuilder.AppendLine($"\nTable: {currentTable}");
                    }
                    schemaBuilder.AppendLine($"  - {col.column_name} ({col.data_type})");
                }

                return schemaBuilder.ToString();
            }) ?? string.Empty;
        }

        public async Task<string> ExecuteReadOnlyQueryAsync(
            string sqlQuery, 
            string? tenantId = null, 
            string? userRole = null, 
            int maxRows = 50, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sqlQuery))
            {
                return "Error: Query is empty.";
            }

            // If non-SuperAdmin user is querying, enforce tenant awareness
            if (!string.IsNullOrWhiteSpace(tenantId) && 
                !string.Equals(userRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                // Check if the query touches tenant-specific tables and lacks tenantId
                _logger.LogInformation("Enforcing tenant boundary for TenantId: {TenantId}, Role: {Role}", tenantId, userRole);
            }

            // 1. AST/Keyword verification: Strictly enforce read-only
            string clean = sqlQuery.Trim().TrimStart('(', '\t', ' ', '\r', '\n');

            // Strip leading comments e.g., -- or /* */
            while (clean.StartsWith("--") || clean.StartsWith("/*"))
            {
                if (clean.StartsWith("--"))
                {
                    int newline = clean.IndexOfAny(new char[] { '\r', '\n' });
                    clean = newline == -1 ? string.Empty : clean.Substring(newline).TrimStart();
                }
                else if (clean.StartsWith("/*"))
                {
                    int endComment = clean.IndexOf("*/", StringComparison.Ordinal);
                    clean = endComment == -1 ? string.Empty : clean.Substring(endComment + 2).TrimStart();
                }
            }

            if (!clean.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !clean.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                return "Security Error: Only SELECT and CTE (WITH) queries are allowed.";
            }

            // Reject stacked/multi-statement queries
            if (clean.Contains(';'))
            {
                clean = clean.Split(';')[0].Trim();
            }

            // Prohibit destructive functions or subquery execution
            string[] forbiddenTokens = new string[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "EXEC", "EXECUTE", "PG_READ_FILE", "COPY", "GRANT", "REVOKE" };
            foreach (string token in forbiddenTokens)
            {
                // Word boundary check
                if (Regex.IsMatch(clean, $@"\b{token}\b", RegexOptions.IgnoreCase))
                {
                    return $"Security Violation: The token '{token}' is strictly forbidden in read-only analysis.";
                }
            }

            // 2. Multi-tenant isolation: If tenantId is specified and user is not SuperAdmin, enforce tenant filter
            if (!string.IsNullOrWhiteSpace(tenantId) && !string.Equals(userRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Enforcing tenant boundary for TenantId: {TenantId}, Role: {Role}", tenantId, userRole);
                string[] tenantTables = new string[] { 
                    "Batches", "Batch", "Inventories", "Inventory", 
                    "Invoices", "Invoice", "Medicines", "Medicine", 
                    "Sales", "Orders", "Customers", "Prescriptions", "Staff" 
                };
                
                foreach (string tbl in tenantTables)
                {
                    if (Regex.IsMatch(clean, $@"\b{tbl}\b", RegexOptions.IgnoreCase) &&
                        !clean.Contains("PharmacyOwnerId", StringComparison.OrdinalIgnoreCase) &&
                        !clean.Contains("TenantId", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if WHERE clause exists
                        if (Regex.IsMatch(clean, @"\bWHERE\b", RegexOptions.IgnoreCase))
                        {
                            clean = Regex.Replace(clean, @"\bWHERE\b", $"WHERE (\"PharmacyOwnerId\" = '{tenantId}' OR \"TenantId\" = '{tenantId}') AND ", RegexOptions.IgnoreCase);
                        }
                        else if (Regex.IsMatch(clean, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase))
                        {
                            clean = Regex.Replace(clean, @"\bGROUP\s+BY\b", $"WHERE (\"PharmacyOwnerId\" = '{tenantId}' OR \"TenantId\" = '{tenantId}') GROUP BY", RegexOptions.IgnoreCase);
                        }
                        else if (Regex.IsMatch(clean, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase))
                        {
                            clean = Regex.Replace(clean, @"\bORDER\s+BY\b", $"WHERE (\"PharmacyOwnerId\" = '{tenantId}' OR \"TenantId\" = '{tenantId}') ORDER BY", RegexOptions.IgnoreCase);
                        }
                        else
                        {
                            clean += $" WHERE (\"PharmacyOwnerId\" = '{tenantId}' OR \"TenantId\" = '{tenantId}')";
                        }
                        break;
                    }
                }
            }

            // Guard against unbounded memory consumption by enforcing LIMIT
            if (!Regex.IsMatch(clean, @"\bLIMIT\b", RegexOptions.IgnoreCase))
            {
                clean += $" LIMIT {maxRows}";
            }

            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);
            IEnumerable<dynamic> results = await connection.QueryAsync<dynamic>(
                new CommandDefinition(clean, cancellationToken: cancellationToken));

            List<dynamic> rows = results.Take(maxRows).ToList();
            return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
        }

        public async Task<int> GetTotalUsersCountAsync(string? tenantId = null, CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);
            
            // Check if Users table exists
            string checkSql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Users';";
            int tableExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkSql, cancellationToken: cancellationToken));
            if (tableExists == 0) return 0;

            string query = "SELECT COUNT(*) FROM \"Users\";";
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(query, cancellationToken: cancellationToken));
        }

        public async Task<string> GetTenantCurrentStockAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT 
                    m.""Name"" AS ""MedicineName"",
                    m.""Category"",
                    i.""RackNo"",
                    b.""BatchNo"",
                    b.""Quantity"",
                    b.""PurchasePrice"",
                    b.""SellingPrice"",
                    b.""Mrp"",
                    b.""ExpiryDate""
                FROM ""Batches"" b
                INNER JOIN ""Inventories"" i ON b.""InventoryId"" = i.""Id""
                INNER JOIN ""Medicines"" m ON i.""MedicineId"" = m.""Id""
                WHERE b.""PharmacyOwnerId"" = @TenantId::uuid
                ORDER BY m.""Name"" ASC, b.""ExpiryDate"" ASC
                LIMIT 100;
            ";

            try
            {
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed querying Inventories table directly, trying fallback with singular table name");
                const string fallbackSql = @"
                    SELECT 
                        m.""Name"" AS ""MedicineName"",
                        m.""Category"",
                        i.""RackNo"",
                        b.""BatchNo"",
                        b.""Quantity"",
                        b.""PurchasePrice"",
                        b.""SellingPrice"",
                        b.""Mrp"",
                        b.""ExpiryDate""
                    FROM ""Batch"" b
                    INNER JOIN ""Inventory"" i ON b.""InventoryId"" = i.""Id""
                    INNER JOIN ""Medicine"" m ON i.""MedicineId"" = m.""Id""
                    WHERE b.""PharmacyOwnerId"" = @TenantId::uuid
                    ORDER BY m.""Name"" ASC, b.""ExpiryDate"" ASC
                    LIMIT 100;
                ";
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(fallbackSql, new { TenantId = tenantId }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
        }

        public async Task<string> GetTenantExpiringMedicinesAsync(string tenantId, int daysThreshold = 90, CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT 
                    m.""Name"" AS ""MedicineName"",
                    m.""Category"",
                    b.""BatchNo"",
                    b.""Quantity"",
                    b.""SellingPrice"",
                    b.""ExpiryDate"",
                    ROUND(CAST(EXTRACT(DAY FROM (b.""ExpiryDate"" - CURRENT_DATE)) AS numeric), 0) AS ""DaysUntilExpiry""
                FROM ""Batches"" b
                INNER JOIN ""Inventories"" i ON b.""InventoryId"" = i.""Id""
                INNER JOIN ""Medicines"" m ON i.""MedicineId"" = m.""Id""
                WHERE b.""PharmacyOwnerId"" = @TenantId::uuid
                  AND b.""ExpiryDate"" <= (CURRENT_DATE + (@DaysThreshold * INTERVAL '1 day'))
                  AND b.""Quantity"" > 0
                ORDER BY b.""ExpiryDate"" ASC
                LIMIT 100;
            ";

            try
            {
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(sql, new { TenantId = tenantId, DaysThreshold = daysThreshold }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback querying expiring medicines on singular table");
                const string fallbackSql = @"
                    SELECT 
                        m.""Name"" AS ""MedicineName"",
                        m.""Category"",
                        b.""BatchNo"",
                        b.""Quantity"",
                        b.""SellingPrice"",
                        b.""ExpiryDate"",
                        ROUND(CAST(EXTRACT(DAY FROM (b.""ExpiryDate"" - CURRENT_DATE)) AS numeric), 0) AS ""DaysUntilExpiry""
                    FROM ""Batch"" b
                    INNER JOIN ""Inventory"" i ON b.""InventoryId"" = i.""Id""
                    INNER JOIN ""Medicine"" m ON i.""MedicineId"" = m.""Id""
                    WHERE b.""PharmacyOwnerId"" = @TenantId::uuid
                      AND b.""ExpiryDate"" <= (CURRENT_DATE + (@DaysThreshold * INTERVAL '1 day'))
                      AND b.""Quantity"" > 0
                    ORDER BY b.""ExpiryDate"" ASC
                    LIMIT 100;
                ";
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(fallbackSql, new { TenantId = tenantId, DaysThreshold = daysThreshold }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
        }

        public async Task<string> GetTenantSalesSummaryAsync(string tenantId, int days = 30, CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT 
                    COUNT(inv.""Id"") AS ""TotalInvoices"",
                    COALESCE(SUM(inv.""GrandTotal""), 0) AS ""TotalRevenue"",
                    COALESCE(SUM(inv.""ReceivedAmount""), 0) AS ""TotalReceived"",
                    COALESCE(SUM(inv.""BalanceDue""), 0) AS ""TotalBalanceDue"",
                    COUNT(DISTINCT inv.""CustomerName"") AS ""UniqueCustomers""
                FROM ""Invoices"" inv
                WHERE inv.""PharmacyOwnerId"" = @TenantId::uuid
                  AND inv.""BillingDate"" >= (CURRENT_DATE - (@Days * INTERVAL '1 day'));
            ";

            try
            {
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(sql, new { TenantId = tenantId, Days = days }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback querying sales summary on singular table");
                const string fallbackSql = @"
                    SELECT 
                        COUNT(inv.""Id"") AS ""TotalInvoices"",
                        COALESCE(SUM(inv.""GrandTotal""), 0) AS ""TotalRevenue"",
                        COALESCE(SUM(inv.""ReceivedAmount""), 0) AS ""TotalReceived"",
                        COALESCE(SUM(inv.""BalanceDue""), 0) AS ""TotalBalanceDue"",
                        COUNT(DISTINCT inv.""CustomerName"") AS ""UniqueCustomers""
                    FROM ""Invoice"" inv
                    WHERE inv.""PharmacyOwnerId"" = @TenantId::uuid
                      AND inv.""BillingDate"" >= (CURRENT_DATE - (@Days * INTERVAL '1 day'));
                ";
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(fallbackSql, new { TenantId = tenantId, Days = days }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
        }

        public async Task<string> GetTenantLowStockMedicinesAsync(string tenantId, int threshold = 10, CancellationToken cancellationToken = default)
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT 
                    m.""Name"" AS ""MedicineName"",
                    m.""Category"",
                    i.""RackNo"",
                    b.""BatchNo"",
                    b.""Quantity"" AS ""RemainingUnits"",
                    b.""SellingPrice""
                FROM ""Batches"" b
                INNER JOIN ""Inventories"" i ON b.""InventoryId"" = i.""Id""
                INNER JOIN ""Medicines"" m ON i.""MedicineId"" = m.""Id""
                WHERE b.""PharmacyOwnerId"" = @TenantId::uuid
                  AND b.""Quantity"" <= @Threshold
                ORDER BY b.""Quantity"" ASC
                LIMIT 50;
            ";

            try
            {
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(sql, new { TenantId = tenantId, Threshold = threshold }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback querying low stock medicines on singular table");
                const string fallbackSql = @"
                    SELECT 
                        m.""Name"" AS ""MedicineName"",
                        m.""Category"",
                        i.""RackNo"",
                        b.""BatchNo"",
                        b.""Quantity"" AS ""RemainingUnits"",
                        b.""SellingPrice""
                    FROM ""Batch"" b
                    INNER JOIN ""Inventory"" i ON b.""InventoryId"" = i.""Id""
                    INNER JOIN ""Medicine"" m ON i.""MedicineId"" = m.""Id""
                    WHERE b.""PharmacyOwnerId"" = @TenantId::uuid
                      AND b.""Quantity"" <= @Threshold
                    ORDER BY b.""Quantity"" ASC
                    LIMIT 50;
                ";
                IEnumerable<dynamic> rows = await connection.QueryAsync<dynamic>(
                    new CommandDefinition(fallbackSql, new { TenantId = tenantId, Threshold = threshold }, cancellationToken: cancellationToken));
                return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            }
        }
    }
}
