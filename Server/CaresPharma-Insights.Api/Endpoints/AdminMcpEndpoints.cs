using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Application.Mcp.Services;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Api.Endpoints
{
    public static class AdminMcpEndpoints
    {
        public static IEndpointRouteBuilder MapAdminMcpEndpoints(this IEndpointRouteBuilder endpoints)
        {
            RouteGroupBuilder group = endpoints.MapGroup("/api/admin/mcp")
                .WithTags("SuperAdmin - MCP Governance")
                .RequireAuthorization();

            // 1. Generate client credentials for pharmacy owner
            group.MapPost("/tenants/{ownerId}/generate", async (
                string ownerId,
                GenerateTenantMcpKeyRequest? request,
                HttpContext httpContext,
                ITenantMcpAuthService authService,
                CancellationToken cancellationToken) =>
            {
                if (!IsSuperAdmin(httpContext.User))
                {
                    return Results.Forbid();
                }

                string adminId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    ?? httpContext.User.FindFirst("sub")?.Value 
                    ?? "superadmin";

                GeneratedMcpCredentials credentials = await authService.GenerateCredentialsAsync(
                    pharmacyOwnerId: ownerId,
                    clientName: request?.ClientName,
                    expiryDays: request?.ExpiryDays ?? 180,
                    adminId: adminId,
                    cancellationToken: cancellationToken);

                return Results.Ok(new
                {
                    clientId = credentials.ClientId,
                    clientSecret = credentials.ClientSecret,
                    pharmacyOwnerId = credentials.PharmacyOwnerId,
                    expiresAtUtc = credentials.ExpiresAtUtc,
                    mcpServerUrl = "https://dev-carespharma-insights.runasp.net/mcp",
                    warning = "Please securely copy the clientSecret now. It is hashed and cannot be retrieved again."
                });
            })
            .WithName("AdminGenerateMcpCredentials")
            .WithSummary("Issues a new Client ID & Secret for a pharmacy owner (SuperAdmin only).");

            // 2. Get all credentials for a specific tenant
            group.MapGet("/tenants/{ownerId}", async (
                string ownerId,
                HttpContext httpContext,
                ITenantMcpClientRepository repository,
                CancellationToken cancellationToken) =>
            {
                if (!IsSuperAdmin(httpContext.User))
                {
                    return Results.Forbid();
                }

                IReadOnlyList<TenantMcpClient> clients = await repository.GetAllByOwnerIdAsync(ownerId, cancellationToken);
                return Results.Ok(clients);
            })
            .WithName("AdminGetTenantMcpKeys")
            .WithSummary("Gets all MCP keys and statuses for a pharmacy owner.");

            // 3. Instant Kill-Switch: Revoke all credentials for tenant & force disconnect active SSE sessions
            group.MapPost("/tenants/{ownerId}/revoke", async (
                string ownerId,
                HttpContext httpContext,
                ITenantMcpAuthService authService,
                CancellationToken cancellationToken) =>
            {
                if (!IsSuperAdmin(httpContext.User))
                {
                    return Results.Forbid();
                }

                bool success = await authService.RevokeCredentialsAsync(ownerId, cancellationToken);
                return Results.Ok(new
                {
                    success = success,
                    message = $"MCP credentials revoked and active sessions forcefully terminated for pharmacy: {ownerId}"
                });
            })
            .WithName("AdminRevokeTenantMcp")
            .WithSummary("Revokes credentials and disconnects active SSE sessions instantly for a tenant.");

            // 4. Toggle specific key status (Enable / Disable)
            group.MapPut("/keys/{keyId:guid}/status", async (
                Guid keyId,
                ToggleMcpKeyStatusRequest request,
                HttpContext httpContext,
                ITenantMcpAuthService authService,
                CancellationToken cancellationToken) =>
            {
                if (!IsSuperAdmin(httpContext.User))
                {
                    return Results.Forbid();
                }

                bool success = await authService.ToggleStatusAsync(keyId, request.IsActive, cancellationToken);
                return Results.Ok(new { success = success, isActive = request.IsActive });
            })
            .WithName("AdminToggleMcpKeyStatus")
            .WithSummary("Enables or disables an MCP key.");

            // 5. Live Telemetry & Active Sessions Monitoring
            group.MapGet("/active-sessions", (
                HttpContext httpContext,
                IMcpSessionManager sessionManager) =>
            {
                if (!IsSuperAdmin(httpContext.User))
                {
                    return Results.Forbid();
                }

                int totalActive = sessionManager.GetActiveSessionCount();
                IReadOnlyDictionary<string, int> activeTenants = sessionManager.GetActiveTenantSessionCounts();

                return Results.Ok(new
                {
                    totalActiveSessions = totalActive,
                    activeTenants = activeTenants,
                    timestampUtc = DateTime.UtcNow
                });
            })
            .WithName("AdminGetActiveMcpSessions")
            .WithSummary("Returns real-time active MCP SSE connections per tenant.");

            // 6. Audit Logs
            group.MapGet("/activity-logs", async (
                int? count,
                HttpContext httpContext,
                ITenantMcpClientRepository repository,
                CancellationToken cancellationToken) =>
            {
                if (!IsSuperAdmin(httpContext.User))
                {
                    return Results.Forbid();
                }

                IReadOnlyList<TenantMcpActivityLog> logs = await repository.GetRecentActivityLogsAsync(count ?? 100, cancellationToken);
                return Results.Ok(logs);
            })
            .WithName("AdminGetMcpActivityLogs")
            .WithSummary("Retrieves recent MCP telemetry and audit logs.");

            return endpoints;
        }

        private static bool IsSuperAdmin(ClaimsPrincipal user)
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                return false;
            }

            string role = user.FindFirst(ClaimTypes.Role)?.Value 
                ?? user.FindFirst("role")?.Value 
                ?? user.FindFirst("UserRole")?.Value 
                ?? string.Empty;

            return string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase) || user.IsInRole("SuperAdmin");
        }
    }
}
