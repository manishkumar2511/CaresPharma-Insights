using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaresPharma_Insights.Application.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpProtocolService
    {
        Task<McpJsonRpcResponse?> HandleMessageAsync(McpJsonRpcRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Core JSON-RPC 2.0 dispatcher implementing the Model Context Protocol (MCP) specification.
    /// Handles initialize, tools/list, and tools/call seamlessly.
    /// </summary>
    public sealed class McpProtocolService : IMcpProtocolService
    {
        private readonly IMcpToolHandler _toolHandler;
        private readonly ILogger<McpProtocolService> _logger;

        private const string ProtocolVersion = "2024-11-05";

        public McpProtocolService(IMcpToolHandler toolHandler, ILogger<McpProtocolService> logger)
        {
            _toolHandler = toolHandler;
            _logger = logger;
        }

        public async Task<McpJsonRpcResponse?> HandleMessageAsync(McpJsonRpcRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return McpJsonRpcResponse.Fail(null, -32600, "Invalid Request");
            }

            _logger.LogInformation("Processing MCP JSON-RPC method: {Method}, ID: {Id}", request.Method, request.Id);

            switch (request.Method)
            {
                case "initialize":
                {
                    var initResult = new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new
                        {
                            tools = new { listChanged = false }
                        },
                        serverInfo = new
                        {
                            name = "CaresPharma-Insights",
                            version = "1.0.0"
                        }
                    };

                    return McpJsonRpcResponse.Success(request.Id, initResult);
                }

                case "notifications/initialized":
                {
                    // Client handshake acknowledgement notification - no response required in JSON-RPC
                    return null;
                }

                case "ping":
                {
                    return McpJsonRpcResponse.Success(request.Id, new { });
                }

                case "tools/list":
                {
                    var tools = _toolHandler.GetToolDefinitions();
                    return McpJsonRpcResponse.Success(request.Id, new { tools });
                }

                case "tools/call":
                {
                    if (request.Params == null || !request.Params.Value.TryGetProperty("name", out var nameProp))
                    {
                        return McpJsonRpcResponse.Fail(request.Id, -32602, "Invalid params: 'name' is required.");
                    }

                    string toolName = nameProp.GetString() ?? string.Empty;
                    JsonElement? arguments = null;

                    if (request.Params.Value.TryGetProperty("arguments", out var argsProp))
                    {
                        arguments = argsProp;
                    }

                    McpCallToolResult callResult = await _toolHandler.ExecuteToolAsync(toolName, arguments, cancellationToken);
                    return McpJsonRpcResponse.Success(request.Id, callResult);
                }

                default:
                {
                    _logger.LogWarning("Method not found in MCP dispatcher: {Method}", request.Method);
                    return McpJsonRpcResponse.Fail(request.Id, -32601, $"Method '{request.Method}' not found.");
                }
            }
        }
    }
}
