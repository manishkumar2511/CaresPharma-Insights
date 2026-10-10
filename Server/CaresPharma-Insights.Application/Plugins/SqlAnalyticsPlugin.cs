using System.ComponentModel;
using System.Data;
using Microsoft.SemanticKernel;
using Dapper;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Application.Plugins
{
    public class SqlAnalyticsPlugin
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public SqlAnalyticsPlugin(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        [KernelFunction("get_database_schema")]
        [Description("Gets the schema of all tables and columns available in the insights database. Use this to understand what data you can query.")]
        public async Task<string> GetDatabaseSchemaAsync()
        {
            using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync();
            
            // Get all tables and columns for PostgreSQL
            string schemaSql = @"
                SELECT table_name, column_name, data_type 
                FROM information_schema.columns 
                WHERE table_schema = 'public'
                ORDER BY table_name, ordinal_position;
            ";

            IEnumerable<dynamic> columns = await connection.QueryAsync<dynamic>(schemaSql);
            
            System.Text.StringBuilder schemaBuilder = new System.Text.StringBuilder();
            schemaBuilder.AppendLine("Database Schema:");
            
            string currentTable = "";
            foreach (dynamic col in columns)
            {
                if (currentTable != (string)col.table_name)
                {
                    currentTable = (string)col.table_name;
                    schemaBuilder.AppendLine($"\nTable: {currentTable}");
                }
                schemaBuilder.AppendLine($"  - {col.column_name} ({col.data_type})");
            }

            return schemaBuilder.ToString();
        }

        [KernelFunction("execute_sql_query")]
        [Description("Executes a READ-ONLY SQL query (SELECT only) against the PostgreSQL insights database and returns the data as a JSON string. ALWAYS call get_database_schema first if you are unsure of the tables.")]
        public async Task<string> ExecuteSqlQueryAsync([Description("The SQL SELECT query to execute")] string sqlQuery)
        {
            if (string.IsNullOrWhiteSpace(sqlQuery)) return "Error: Query is empty.";
            
            // Basic safety check (though DB role should enforce this in prod)
            if (sqlQuery.Trim().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) || 
                sqlQuery.Trim().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || 
                sqlQuery.Trim().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) || 
                sqlQuery.Trim().StartsWith("DROP", StringComparison.OrdinalIgnoreCase))
            {
                return "Error: Only SELECT queries are allowed.";
            }

            try
            {
                using IDbConnection connection = await _dbConnectionFactory.CreateConnectionAsync();
                IEnumerable<dynamic> results = await connection.QueryAsync<dynamic>(sqlQuery);
                
                // Convert to JSON
                return System.Text.Json.JsonSerializer.Serialize(results);
            }
            catch (Exception ex)
            {
                return $"Error executing query: {ex.Message}";
            }
        }
    }
}
