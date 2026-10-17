using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Application.Mcp.Tools;
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
    /// Senior Architect Grade MCP Tool Orchestrator.
    /// Follows the Strategy & Registry patterns to isolate individual tool concerns.
    /// New tools are auto-discovered via Dependency Injection without modifying this orchestrator.
    /// </summary>
    public sealed class McpToolHandler : IMcpToolHandler
    {
        private readonly ConcurrentDictionary<string, IMcpTool> _tools;
        private readonly ILogger<McpToolHandler> _logger;

        public McpToolHandler(
            IEnumerable<IMcpTool> tools,
            ILogger<McpToolHandler> logger)
        {
            _logger = logger;
            _tools = new ConcurrentDictionary<string, IMcpTool>(
                tools.ToDictionary(t => t.Definition.Name, t => t, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Initialized McpToolHandler with {Count} isolated MCP tools: [{Tools}]",
                _tools.Count, string.Join(", ", _tools.Keys));
        }

        public IReadOnlyList<McpToolDefinition> GetToolDefinitions()
        {
            return _tools.Values.Select(t => t.Definition).ToList();
        }

        public async Task<McpCallToolResult> ExecuteToolAsync(
            string toolName, 
            JsonElement? arguments, 
            TenantSecurityContext? tenantContext,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", "Error: Tool name cannot be null or empty.") },
                    IsError: true);
            }

            if (!_tools.TryGetValue(toolName, out IMcpTool? tool))
            {
                _logger.LogWarning("Requested tool '{ToolName}' not recognized by MCP registry.", toolName);
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", $"Unknown tool: '{toolName}'. Available tools: {string.Join(", ", _tools.Keys)}") },
                    IsError: true);
            }

            try
            {
                _logger.LogInformation("Executing isolated MCP tool '{ToolName}' for Tenant: {TenantId}, Role: {Role}",
                    toolName, tenantContext?.PharmacyOwnerId ?? "None", tenantContext?.UserRole ?? "Anonymous");

                return await tool.ExecuteAsync(arguments, tenantContext, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during execution of MCP tool '{ToolName}'", toolName);
                return new McpCallToolResult(
                    new[] { new McpContentItem("text", $"Execution error in '{toolName}': {ex.Message}") },
                    IsError: true);
            }
        }
    }
}
