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
    /// Aggregates sales volume, invoice metrics, collected revenue, and outstanding balance dues
    /// strictly isolated to the caller's pharmacy tenant.
    /// </summary>
    public sealed class GetSalesAndRevenueSummaryTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetSalesAndRevenueSummaryTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_sales_and_revenue_summary",
            Description: "Gets the total sales, invoices count, received revenue, and balance due for the past N days (default 30 days) exclusively for the authenticated pharmacy owner.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    days = new
                    {
                        type = "integer",
                        description = "Past period in days to aggregate sales (e.g. 7, 30, 90). Defaults to 30."
                    }
                },
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

            int days = 30;
            if (arguments.HasValue && arguments.Value.TryGetProperty("days", out JsonElement daysElem) && daysElem.TryGetInt32(out int parsedDays))
            {
                days = Math.Max(1, parsedDays);
            }

            string salesJson = await _dataRepository.GetTenantSalesSummaryAsync(tenantId, days, cancellationToken);
            return new McpCallToolResult(new[] { new McpContentItem("text", salesJson) });
        }
    }
}
