using Microsoft.Extensions.DependencyInjection;
using CaresPharma_Insights.Application.Mcp.Services;
using CaresPharma_Insights.Application.Mcp.Tools;

namespace CaresPharma_Insights.Api.Extensions
{
    public static class McpServiceCollectionExtensions
    {
        public static IServiceCollection AddMcpServerServices(this IServiceCollection services)
        {
            // Register singleton session manager to manage active client SSE channels
            services.AddSingleton<IMcpSessionManager, McpSessionManager>();
            services.AddSingleton<IMcpHandshakeTokenService, McpHandshakeTokenService>();
            services.AddScoped<ITenantMcpAuthService, TenantMcpAuthService>();

            // Register modular isolated MCP Tools (Separation of Concerns / OCP)
            services.AddScoped<IMcpTool, GetCurrentInventoryStockTool>();
            services.AddScoped<IMcpTool, GetExpiringMedicinesTool>();
            services.AddScoped<IMcpTool, GetSalesAndRevenueSummaryTool>();
            services.AddScoped<IMcpTool, GetLowStockMedicinesTool>();
            services.AddScoped<IMcpTool, GetCustomerCountMetricsTool>();
            services.AddScoped<IMcpTool, GetPharmacySchemaTool>();
            services.AddScoped<IMcpTool, QueryPharmacyInsightsTool>();

            // Register MCP domain tool registry & protocol dispatcher
            services.AddScoped<IMcpToolHandler, McpToolHandler>();
            services.AddScoped<IMcpProtocolService, McpProtocolService>();

            return services;
        }
    }
}
