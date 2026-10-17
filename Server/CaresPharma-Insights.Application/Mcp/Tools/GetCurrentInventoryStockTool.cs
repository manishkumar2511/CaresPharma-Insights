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
    /// Retrieves current active medicine stock, batch numbers, rack locations, and pricing
    /// strictly isolated to the caller's pharmacy tenant.
    /// </summary>
    public sealed class GetCurrentInventoryStockTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetCurrentInventoryStockTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_current_inventory_stock",
            Description: "Gets the current active medicine inventory and batch stock exclusively for the authenticated pharmacy owner's store. Returns medicine name, category, rack number, batch number, remaining quantity, purchase/selling price, and expiry date. NON-TECH OWNERS ASKING FOR STOCK OR MEDICINE LIST SHOULD ALWAYS USE THIS TOOL.",
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
                    new[] { new McpContentItem("text", "Error: Pharmacy tenant context could not be resolved. Please verify credentials.") },
                    IsError: true);
            }

            string stockJson = await _dataRepository.GetTenantCurrentStockAsync(tenantId, cancellationToken);
            return new McpCallToolResult(new[] { new McpContentItem("text", stockJson) });
        }
    }
}
