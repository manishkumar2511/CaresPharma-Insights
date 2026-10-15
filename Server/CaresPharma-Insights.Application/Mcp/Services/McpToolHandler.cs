using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Interfaces;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpToolHandler
    {
        IReadOnlyList<McpToolDefinition> GetToolDefinitions();
        Task<McpCallToolResult> ExecuteToolAsync(string toolName, JsonElement? arguments, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Enterprise MCP Tool Handler exposing CaresPharma analytics and schema introspection.
    /// Strictly leverages existing repository interfaces with read-only execution guardrails.
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
                    Name: "get_pharmacy_schema",
                    Description: "Inspects and returns the cached database schema of CaresPharma (available tables, column names, and analytical views). Call this first if you need to know what data is queryable.",
                    InputSchema: new
                    {
                        type = "object",
                        properties = new { },
                        additionalProperties = false
                    }
                ),
                new(
                    Name: "query_pharmacy_insights",
                    Description: "Executes a strictly READ-ONLY SQL SELECT query against CaresPharma database for sales, inventory, orders, revenue, customer insights, or medicine metrics. Returns JSON rows.",
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
            CancellationToken cancellationToken)
        {
            try
            {
                switch (toolName)
                {
                    case "get_pharmacy_schema":
                    {
                        string schema = await _dataRepository.GetCachedDatabaseSchemaAsync(cancellationToken);
                        return new McpCallToolResult(new[] { new McpContentItem("text", schema) });
                    }

                    case "query_pharmacy_insights":
                    {
                        if (arguments == null || !arguments.Value.TryGetProperty("sqlQuery", out var sqlProp))
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

                        // Execute query with safety enforcement
                        string resultJson = await _dataRepository.ExecuteReadOnlyQueryAsync(
                            sql, 
                            tenantId: null, 
                            userRole: null, 
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
