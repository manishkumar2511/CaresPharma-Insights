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
    /// Identifies batches and medicines falling below safe replenishment thresholds
    /// strictly isolated to the caller's pharmacy tenant.
    /// </summary>
    public sealed class GetLowStockMedicinesTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetLowStockMedicinesTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_low_stock_medicines",
            Description: "Gets inventory items and batches where remaining quantity has fallen below a threshold (default 10 units) for re-ordering.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    threshold = new
                    {
                        type = "integer",
                        description = "Minimum quantity threshold. Defaults to 10."
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

            int threshold = 10;
            if (arguments.HasValue && arguments.Value.TryGetProperty("threshold", out JsonElement threshElem) && threshElem.TryGetInt32(out int parsedThresh))
            {
                threshold = Math.Max(0, parsedThresh);
            }

            string lowStockJson = await _dataRepository.GetTenantLowStockMedicinesAsync(tenantId, threshold, cancellationToken);
            return new McpCallToolResult(new[] { new McpContentItem("text", lowStockJson) });
        }
    }
}
