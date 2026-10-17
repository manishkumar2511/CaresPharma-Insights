using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Tools
{
    /// <summary>
    /// Retrieves total registered customer count or staff count metrics.
    /// </summary>
    public sealed class GetCustomerCountMetricsTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetCustomerCountMetricsTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_customer_count_metrics",
            Description: "Gets the total registered customer count and customer metrics for the authenticated pharmacy owner's store.",
            InputSchema: new
            {
                type = "object",
                properties = new { },
                additionalProperties = false
            }
        );

        public async Task<McpCallToolResult> ExecuteAsync(
            JsonElement? arguments, 
            TenantSecurityContext? tenantContext, 
            CancellationToken cancellationToken)
        {
            string? tenantId = tenantContext?.PharmacyOwnerId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", "Error: Pharmacy tenant context could not be resolved.") },
                    IsError: true);
            }

            string sql = @"
                SELECT 
                    COUNT(*) AS ""TotalCustomers"",
                    COUNT(CASE WHEN ""CreatedAt"" >= CURRENT_DATE - INTERVAL '30 days' THEN 1 END) AS ""NewCustomersLast30Days""
                FROM ""Customers""
                WHERE ""PharmacyOwnerId"" = @TenantId::uuid;
            ";

            try
            {
                string result = await _dataRepository.ExecuteReadOnlyQueryAsync(
                    sql.Replace("@TenantId::uuid", $"'{tenantId}'::uuid"),
                    tenantId: tenantId,
                    userRole: tenantContext?.UserRole,
                    maxRows: 1,
                    cancellationToken: cancellationToken);

                return new McpCallToolResult(new[] { new McpContentItem("text", result) });
            }
            catch
            {
                // Fallback to singular table name if table is Customer
                string fallbackSql = @"
                    SELECT 
                        COUNT(*) AS ""TotalCustomers""
                    FROM ""Customer""
                    WHERE ""PharmacyOwnerId"" = @TenantId::uuid;
                ";
                string result = await _dataRepository.ExecuteReadOnlyQueryAsync(
                    fallbackSql.Replace("@TenantId::uuid", $"'{tenantId}'::uuid"),
                    tenantId: tenantId,
                    userRole: tenantContext?.UserRole,
                    maxRows: 1,
                    cancellationToken: cancellationToken);

                return new McpCallToolResult(new[] { new McpContentItem("text", result) });
            }
        }
    }
}
