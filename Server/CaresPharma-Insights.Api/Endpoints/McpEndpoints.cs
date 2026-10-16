using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Application.Mcp.Services;

namespace CaresPharma_Insights.Api.Endpoints
{
    public static class McpEndpoints
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // 0. POST /api/mcp/connect-token - Generates short-lived 1-Click Connect Token for Portal Users
            endpoints.MapPost("/api/mcp/connect-token", (
                HttpContext httpContext,
                IMcpHandshakeTokenService tokenService) =>
            {
                var user = httpContext.User;
                string? pharmacyOwnerId = user.FindFirst("pharmacyOwnerId")?.Value 
                    ?? user.FindFirst("ownerId")?.Value;
                string userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                    ?? user.FindFirst("sub")?.Value 
                    ?? "anonymous-user";
                string role = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "PharmacyOwner";

                if (string.IsNullOrWhiteSpace(pharmacyOwnerId) && !string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Unable to resolve pharmacy tenant identity from current user context." });
                }

                // If SuperAdmin, pharmacyOwnerId can be null
                var handshakeToken = tokenService.GenerateToken(pharmacyOwnerId ?? "superadmin", userId, role);
                string connectUrl = $"https://dev-carespharma-insights.runasp.net/mcp?token={handshakeToken.Token}";

                return Results.Ok(new
                {
                    token = handshakeToken.Token,
                    expiresAtUtc = handshakeToken.ExpiresAtUtc,
                    connectUrl = connectUrl,
                    claudeDeepLink = $"https://claude.ai/customize/connectors?url={Uri.EscapeDataString(connectUrl)}"
                });
            })
            .WithName("GenerateMcpConnectToken")
            .WithSummary("Issues a secure 1-Click handshake token for the authenticated pharmacy owner.")
            .RequireAuthorization();

            // 1. GET /mcp - Server-Sent Events (SSE) Stream
            endpoints.MapGet("/mcp", async (
                HttpContext httpContext,
                IMcpSessionManager sessionManager,
                IMcpHandshakeTokenService tokenService,
                ITenantMcpAuthService authService,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("McpEndpoints.SSE");
                string sessionId = Guid.NewGuid().ToString("N");

                // 1. Check for Persistent Client Credentials (ClientId & ClientSecret)
                string? clientId = httpContext.Request.Query["clientId"].FirstOrDefault() 
                    ?? httpContext.Request.Query["client_id"].FirstOrDefault()
                    ?? httpContext.Request.Headers["X-Client-Id"].FirstOrDefault();

                string? clientSecret = httpContext.Request.Query["clientSecret"].FirstOrDefault() 
                    ?? httpContext.Request.Query["client_secret"].FirstOrDefault()
                    ?? httpContext.Request.Headers["X-Client-Secret"].FirstOrDefault();

                // Also inspect standard HTTP Basic Authorization header (often sent by OAuth/MCP clients)
                string? authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string credentials = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authHeader[6..].Trim()));
                        int colonIndex = credentials.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            clientId = credentials[..colonIndex];
                            clientSecret = credentials[(colonIndex + 1)..];
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug("Failed parsing Basic Auth header: {Msg}", ex.Message);
                    }
                }

                string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
                string? userAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault();

                CaresPharma_Insights.Domain.Models.TenantSecurityContext? tenantContext = null;

                if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                {
                    tenantContext = await authService.ValidateCredentialsAsync(
                        clientId, clientSecret, ipAddress, userAgent, cancellationToken);

                    if (tenantContext != null)
                    {
                        logger.LogInformation("Enterprise Client Credentials validated for Tenant: {TenantId}", tenantContext.PharmacyOwnerId);
                    }
                    else
                    {
                        logger.LogWarning("Invalid MCP Client Credentials rejected for ClientId: {ClientId}", clientId);
                        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await httpContext.Response.WriteAsync("{\"error\":\"Invalid or revoked MCP Client Credentials.\"}", cancellationToken);
                        return;
                    }
                }
                else
                {
                    // 2. Fallback to 1-Click Handshake Token if provided
                    string? token = httpContext.Request.Query["token"].FirstOrDefault() 
                        ?? httpContext.Request.Query["sessionToken"].FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        tenantContext = tokenService.ValidateAndClaimToken(token);
                        if (tenantContext != null)
                        {
                            logger.LogInformation("1-Click Claude handshake validated successfully. Tenant: {TenantId}, Role: {Role}", 
                                tenantContext.PharmacyOwnerId, tenantContext.UserRole);
                        }
                        else
                        {
                            logger.LogWarning("Invalid or expired 1-Click token provided: {Token}", token);
                        }
                    }
                }

                httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
                httpContext.Response.Headers.Append("Cache-Control", "no-cache, no-transform");
                httpContext.Response.Headers.Append("Connection", "keep-alive");
                httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

                var reader = sessionManager.CreateSession(sessionId, tenantContext);
                logger.LogInformation("New MCP SSE client connected. SessionId: {SessionId}", sessionId);

                try
                {
                    // Announce the POST message callback endpoint to Claude / MCP client
                    string endpointUrl = $"/mcp/message?sessionId={sessionId}";
                    await httpContext.Response.WriteAsync($"event: endpoint\r\ndata: {endpointUrl}\r\n\r\n", cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);

                    // Maintain SSE stream with heartbeats and dispatched events
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                        try
                        {
                            if (await reader.WaitToReadAsync(linkedCts.Token))
                            {
                                while (reader.TryRead(out var message))
                                {
                                    await httpContext.Response.WriteAsync($"event: message\r\ndata: {message}\r\n\r\n", cancellationToken);
                                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                                }
                            }
                        }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                        {
                            // Send standard SSE comment heartbeat to prevent gateway or proxy timeouts
                            await httpContext.Response.WriteAsync(": heartbeat\r\n\r\n", cancellationToken);
                            await httpContext.Response.Body.FlushAsync(cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("MCP SSE client disconnected. SessionId: {SessionId}", sessionId);
                }
                finally
                {
                    sessionManager.RemoveSession(sessionId);
                }
            })
            .WithName("McpSseStream")
            .WithSummary("Server-Sent Events endpoint for Claude and other MCP clients.")
            .AllowAnonymous();

            // 2. POST /mcp & POST /mcp/message - JSON-RPC Ingestion
            async Task<IResult> HandleMcpPost(
                HttpContext httpContext,
                IMcpSessionManager sessionManager,
                IMcpProtocolService protocolService,
                CancellationToken cancellationToken)
            {
                string? sessionId = httpContext.Request.Query["sessionId"];

                using var streamReader = new StreamReader(httpContext.Request.Body);
                string rawBody = await streamReader.ReadToEndAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(rawBody))
                {
                    return Results.BadRequest(McpJsonRpcResponse.Fail(null, -32600, "Empty payload"));
                }

                McpJsonRpcRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<McpJsonRpcRequest>(rawBody, JsonOptions);
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(McpJsonRpcResponse.Fail(null, -32700, $"Parse error: {ex.Message}"));
                }

                if (request == null)
                {
                    return Results.BadRequest(McpJsonRpcResponse.Fail(null, -32600, "Invalid JSON-RPC request"));
                }

                // Resolve tenant context bound to this active session
                CaresPharma_Insights.Domain.Models.TenantSecurityContext? tenantContext = 
                    !string.IsNullOrEmpty(sessionId) ? sessionManager.GetSessionTenantContext(sessionId) : null;

                McpJsonRpcResponse? response = await protocolService.HandleMessageAsync(request, tenantContext, cancellationToken);

                if (response != null)
                {
                    string serializedResponse = JsonSerializer.Serialize(response, JsonOptions);

                    // If a valid active SSE session exists, send response through SSE stream
                    if (!string.IsNullOrEmpty(sessionId) && sessionManager.TryPostMessage(sessionId, serializedResponse))
                    {
                        return Results.Accepted();
                    }

                    // Direct HTTP response fallback (Streamable HTTP / standard RPC)
                    return Results.Content(serializedResponse, "application/json");
                }

                return Results.Accepted();
            }

            endpoints.MapPost("/mcp", HandleMcpPost)
                .WithName("McpPostRoot")
                .WithSummary("JSON-RPC message receiver endpoint directly on /mcp.")
                .AllowAnonymous();

            endpoints.MapPost("/mcp/message", HandleMcpPost)
                .WithName("McpPostMessage")
                .WithSummary("JSON-RPC message receiver endpoint for Claude MCP interactions.")
                .AllowAnonymous();

            return endpoints;
        }
    }
}
