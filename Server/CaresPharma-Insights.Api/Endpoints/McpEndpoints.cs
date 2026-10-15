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
            // 1. GET /mcp - Server-Sent Events (SSE) Stream
            endpoints.MapGet("/mcp", async (
                HttpContext httpContext,
                IMcpSessionManager sessionManager,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("McpEndpoints.SSE");
                string sessionId = Guid.NewGuid().ToString("N");

                httpContext.Response.Headers.Append("Content-Type", "text/event-stream");
                httpContext.Response.Headers.Append("Cache-Control", "no-cache, no-transform");
                httpContext.Response.Headers.Append("Connection", "keep-alive");
                httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

                var reader = sessionManager.CreateSession(sessionId);
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

            // 2. POST /mcp/message - JSON-RPC Ingestion
            endpoints.MapPost("/mcp/message", async (
                HttpContext httpContext,
                IMcpSessionManager sessionManager,
                IMcpProtocolService protocolService,
                CancellationToken cancellationToken) =>
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

                McpJsonRpcResponse? response = await protocolService.HandleMessageAsync(request, cancellationToken);

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
            })
            .WithName("McpPostMessage")
            .WithSummary("JSON-RPC message receiver endpoint for Claude MCP interactions.")
            .AllowAnonymous();

            return endpoints;
        }
    }
}
