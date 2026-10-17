using System;
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
    /// Retrieves the list of customers derived from invoices or customer records
    /// strictly isolated to the authenticated pharmacy owner's store.
    /// </summary>
    public sealed class GetCustomerListTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetCustomerListTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_customer_list",
            Description: "Gets the list of customers (name, contact number, invoice count, total billed, outstanding balance, last purchase date) for the authenticated pharmacy owner's store.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    limit = new
                    {
                        type = "integer",
                        description = "Maximum number of customers to return (default 50)."
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
                    new[] { new McpContentItem("text", "Security Error: Pharmacy tenant context could not be resolved.") },
                    IsError: true);
            }

            int limit = 50;
            if (arguments.HasValue && arguments.Value.TryGetProperty("limit", out JsonElement limitProp) && limitProp.TryGetInt32(out int parsedLimit))
            {
                limit = Math.Clamp(parsedLimit, 1, 200);
            }

            // Derive customer profiles with financial summaries strictly filtered by PharmacyOwnerId
            string sql = $@"
                SELECT 
                    COALESCE(""CustomerName"", 'Walk-in Customer') AS ""CustomerName"",
                    COALESCE(""CustomerContact"", 'N/A') AS ""Contact"",
                    COUNT(*) AS ""InvoicesCount"",
                    ROUND(COALESCE(SUM(""GrandTotal""), 0), 2) AS ""TotalBilled"",
                    ROUND(COALESCE(SUM(""DueAmount""), 0), 2) AS ""BalanceDue"",
                    MAX(""InvoiceDate"") AS ""LastPurchaseDate""
                FROM ""Invoices""
                WHERE ""PharmacyOwnerId"" = '{tenantId}'::uuid
                GROUP BY ""CustomerName"", ""CustomerContact""
                ORDER BY ""LastPurchaseDate"" DESC
                LIMIT {limit};
            ";

            try
            {
                string result = await _dataRepository.ExecuteReadOnlyQueryAsync(
                    sql,
                    tenantId: tenantId,
                    userRole: tenantContext?.UserRole,
                    maxRows: limit,
                    cancellationToken: cancellationToken);

                return new McpCallToolResult(new[] { new McpContentItem("text", result) });
            }
            catch (Exception ex)
            {
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", $"Error retrieving customer list: {ex.Message}") },
                    IsError: true);
            }
        }
    }
}
