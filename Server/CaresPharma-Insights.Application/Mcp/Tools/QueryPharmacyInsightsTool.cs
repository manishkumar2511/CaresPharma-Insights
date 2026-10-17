using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Tools
{
    /// <summary>
    /// Executes dynamic ad-hoc read-only SQL queries with tenant boundaries,
    /// read-only AST safety validation, and bounded result sets.
    /// </summary>
    public sealed class QueryPharmacyInsightsTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public QueryPharmacyInsightsTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "query_pharmacy_insights",
            Description: "Executes an ad-hoc strictly READ-ONLY SQL SELECT query against CaresPharma database. NOTE: For current stock, expiring items, or sales summaries, PREFER using the dedicated tools get_current_inventory_stock, get_expiring_medicines, get_sales_and_revenue_summary.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    sqlQuery = new
                    {
                        type = "string",
                        description = "The read-only SQL SELECT query to execute against the analytics database."
                    }
                },
                required = new[] { "sqlQuery" }
            }
        );

        public async Task<McpCallToolResult> ExecuteAsync(
            JsonElement? arguments, 
            TenantSecurityContext? tenantContext, 
            CancellationToken cancellationToken)
        {
            if (arguments == null || !arguments.Value.TryGetProperty("sqlQuery", out JsonElement sqlProp))
            {
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", "Error: 'sqlQuery' parameter is required.") }, 
                    IsError: true);
            }

            string? sql = sqlProp.GetString();
            if (string.IsNullOrWhiteSpace(sql))
            {
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", "Error: 'sqlQuery' cannot be empty.") }, 
                    IsError: true);
            }

            string? tenantId = tenantContext?.PharmacyOwnerId;
            string? userRole = tenantContext?.UserRole;

            string resultJson = await _dataRepository.ExecuteReadOnlyQueryAsync(
                sql, 
                tenantId: tenantId, 
                userRole: userRole, 
                maxRows: 50, 
                cancellationToken: cancellationToken);

            return new McpCallToolResult(new[] { new McpContentItem("text", resultJson) });
        }
    }
}
