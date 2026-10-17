using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Tools
{
    /// <summary>
    /// Inspects and returns cached analytical database schema information.
    /// </summary>
    public sealed class GetPharmacySchemaTool : IMcpTool
    {
        private readonly IAnalyticsDataRepository _dataRepository;

        public GetPharmacySchemaTool(IAnalyticsDataRepository dataRepository)
        {
            _dataRepository = dataRepository;
        }

        public McpToolDefinition Definition => new(
            Name: "get_pharmacy_schema",
            Description: "Inspects and returns the cached database schema of CaresPharma (available tables, column names, and analytical views). Call this if you need to know what custom tables are available.",
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
            string schema = await _dataRepository.GetCachedDatabaseSchemaAsync(cancellationToken);
            return new McpCallToolResult(new[] { new McpContentItem("text", schema) });
        }
    }
}
