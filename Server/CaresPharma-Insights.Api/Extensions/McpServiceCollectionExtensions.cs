using Microsoft.Extensions.DependencyInjection;
using CaresPharma_Insights.Application.Mcp.Services;

namespace CaresPharma_Insights.Api.Extensions
{
    public static class McpServiceCollectionExtensions
    {
        public static IServiceCollection AddMcpServerServices(this IServiceCollection services)
        {
            // Register singleton session manager to manage active client SSE channels
            services.AddSingleton<IMcpSessionManager, McpSessionManager>();

            // Register MCP domain tool handlers and protocol processors
            services.AddScoped<IMcpToolHandler, McpToolHandler>();
            services.AddScoped<IMcpProtocolService, McpProtocolService>();

            return services;
        }
    }
}
