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
    /// Retrieves medicines and batches nearing expiration within an adjustable day window
    /// strictly isolated to the caller's pharmacy tenant.
    /// </summary>
    public sealed class GetExpiringMedicinesTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetExpiringMedicinesTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_expiring_medicines",
            Description: "Gets medicines and batches approaching expiry within a given number of days (default 90 days) exclusively for the authenticated pharmacy owner.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    days = new
                    {
                        type = "integer",
                        description = "Threshold in days to check for expiration (e.g. 30, 60, 90). Defaults to 90 if omitted."
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

            int days = 90;
            if (arguments.HasValue && arguments.Value.TryGetProperty("days", out JsonElement daysElem) && daysElem.TryGetInt32(out int parsedDays))
            {
                days = Math.Max(1, parsedDays);
            }

            string expiryJson = await _dataRepository.GetTenantExpiringMedicinesAsync(tenantId, days, cancellationToken);
            return new McpCallToolResult(new[] { new McpContentItem("text", expiryJson) });
        }
    }
}
