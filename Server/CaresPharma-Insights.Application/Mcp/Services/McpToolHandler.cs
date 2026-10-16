using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpToolHandler
    {
        IReadOnlyList<McpToolDefinition> GetToolDefinitions();
        Task<McpCallToolResult> ExecuteToolAsync(
            string toolName, 
            JsonElement? arguments, 
            TenantSecurityContext? tenantContext, 
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Enterprise MCP Tool Handler exposing CaresPharma analytics and schema introspection.
    /// Strictly leverages existing repository interfaces with read-only execution guardrails and tenant boundaries.
    /// </summary>
    public sealed class McpToolHandler : IMcpToolHandler
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public McpToolHandler(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public IReadOnlyList<McpToolDefinition> GetToolDefinitions()
        {
            return new List<McpToolDefinition>
            {
                new(
                    Name: "get_current_inventory_stock",
                    Description: "Gets the current active medicine inventory and batch stock exclusively for the authenticated pharmacy owner's store. Returns medicine name, category, rack number, batch number, remaining quantity, purchase/selling price, and expiry date. NON-TECH OWNERS ASKING FOR STOCK OR MEDICINE LIST SHOULD ALWAYS USE THIS TOOL.",
                    InputSchema: new
                    {
                        type = "object",
                        properties = new { },
                        additionalProperties = false
                    }
                ),
                new(
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
                ),
                new(
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
                ),
                new(
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
                ),
                new(
                    Name: "get_pharmacy_schema",
                    Description: "Inspects and returns the cached database schema of CaresPharma (available tables, column names, and analytical views). Call this if you need to know what custom tables are available.",
                    InputSchema: new
                    {
                        type = "object",
                        properties = new { },
                        additionalProperties = false
                    }
                ),
                new(
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
                )
            };
        }

        public async Task<McpCallToolResult> ExecuteToolAsync(
            string toolName, 
            JsonElement? arguments, 
            TenantSecurityContext? tenantContext,
            CancellationToken cancellationToken)
        {
            try
            {
                string? tenantId = tenantContext?.PharmacyOwnerId;

                switch (toolName)
                {
                    case "get_current_inventory_stock":
                    {
                        if (string.IsNullOrWhiteSpace(tenantId))
                        {
                            return new McpCallToolResult(
                                new[] { new McpContentItem("text", "Error: Pharmacy tenant context could not be resolved. Please verify credentials.") },
                                IsError: true);
                        }

                        string stockJson = await _dataRepository.GetTenantCurrentStockAsync(tenantId, cancellationToken);
                        return new McpCallToolResult(new[] { new McpContentItem("text", stockJson) });
                    }

                    case "get_expiring_medicines":
                    {
                        if (string.IsNullOrWhiteSpace(tenantId))
                        {
                            return new McpCallToolResult(
                                new[] { new McpContentItem("text", "Error: Pharmacy tenant context could not be resolved.") },
                                IsError: true);
                        }

                        int days = 90;
                        if (arguments.HasValue && arguments.Value.TryGetProperty("days", out JsonElement daysElem) && daysElem.TryGetInt32(out int parsedDays))
                        {
                            days = parsedDays;
                        }

                        string expiryJson = await _dataRepository.GetTenantExpiringMedicinesAsync(tenantId, days, cancellationToken);
                        return new McpCallToolResult(new[] { new McpContentItem("text", expiryJson) });
                    }

                    case "get_sales_and_revenue_summary":
                    {
                        if (string.IsNullOrWhiteSpace(tenantId))
                        {
                            return new McpCallToolResult(
                                new[] { new McpContentItem("text", "Error: Pharmacy tenant context could not be resolved.") },
                                IsError: true);
                        }

                        int days = 30;
                        if (arguments.HasValue && arguments.Value.TryGetProperty("days", out JsonElement daysElem) && daysElem.TryGetInt32(out int parsedDays))
                        {
                            days = parsedDays;
                        }

                        string salesJson = await _dataRepository.GetTenantSalesSummaryAsync(tenantId, days, cancellationToken);
                        return new McpCallToolResult(new[] { new McpContentItem("text", salesJson) });
                    }

                    case "get_low_stock_medicines":
                    {
                        if (string.IsNullOrWhiteSpace(tenantId))
                        {
                            return new McpCallToolResult(
                                new[] { new McpContentItem("text", "Error: Pharmacy tenant context could not be resolved.") },
                                IsError: true);
                        }

                        int threshold = 10;
                        if (arguments.HasValue && arguments.Value.TryGetProperty("threshold", out JsonElement threshElem) && threshElem.TryGetInt32(out int parsedThresh))
                        {
                            threshold = parsedThresh;
                        }

                        string lowStockJson = await _dataRepository.GetTenantLowStockMedicinesAsync(tenantId, threshold, cancellationToken);
                        return new McpCallToolResult(new[] { new McpContentItem("text", lowStockJson) });
                    }

                    case "get_pharmacy_schema":
                    {
                        string schema = await _dataRepository.GetCachedDatabaseSchemaAsync(cancellationToken);
                        return new McpCallToolResult(new[] { new McpContentItem("text", schema) });
                    }

                    case "query_pharmacy_insights":
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

                        // Execute query with tenant safety boundary enforcement
                        string? userRole = tenantContext?.UserRole;

                        string resultJson = await _dataRepository.ExecuteReadOnlyQueryAsync(
                            sql, 
                            tenantId: tenantId, 
                            userRole: userRole, 
                            maxRows: 50, 
                            cancellationToken: cancellationToken);

                        return new McpCallToolResult(new[] { new McpContentItem("text", resultJson) });
                    }

                    default:
                        return new McpCallToolResult(
                            new[] { new McpContentItem("text", $"Unknown tool: '{toolName}'.") }, 
                            IsError: true);
                }
            }
            catch (System.Exception ex)
            {
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", $"Database query execution error: {ex.Message}") }, 
                    IsError: true);
            }
        }
    }
}
