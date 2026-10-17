using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Tools
{
    /// <summary>
    /// Contract for isolated MCP Tools following the Open-Closed Principle (OCP)
    /// and Single Responsibility Principle (SRP).
    /// </summary>
    public interface IMcpTool
    {
        /// <summary>
        /// Gets the metadata and JSON Schema definition for this tool.
        /// </summary>
        McpToolDefinition Definition { get; }

        /// <summary>
        /// Executes the tool with strict tenant isolation and error safety boundaries.
        /// </summary>
        Task<McpCallToolResult> ExecuteAsync(
            JsonElement? arguments, 
            TenantSecurityContext? tenantContext, 
            CancellationToken cancellationToken);
    }
}
