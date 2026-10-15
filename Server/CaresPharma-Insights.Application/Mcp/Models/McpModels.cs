using System.Text.Json.Serialization;

namespace CaresPharma_Insights.Application.Mcp.Models
{
    /// <summary>
    /// Standard JSON-RPC 2.0 Request payload received from MCP clients like Claude.
    /// </summary>
    public sealed record McpJsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = "2.0";

        [JsonPropertyName("id")]
        public object? Id { get; init; }

        [JsonPropertyName("method")]
        public string Method { get; init; } = string.Empty;

        [JsonPropertyName("params")]
        public System.Text.Json.JsonElement? Params { get; init; }
    }

    /// <summary>
    /// Standard JSON-RPC 2.0 Response payload returned to MCP clients.
    /// </summary>
    public sealed record McpJsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = "2.0";

        [JsonPropertyName("id")]
        public object? Id { get; init; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Result { get; init; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public McpJsonRpcError? Error { get; init; }

        public static McpJsonRpcResponse Success(object? id, object result) =>
            new() { Id = id, Result = result };

        public static McpJsonRpcResponse Fail(object? id, int code, string message, object? data = null) =>
            new() { Id = id, Error = new McpJsonRpcError(code, message, data) };
    }

    public sealed record McpJsonRpcError(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("data")] object? Data = null
    );

    /// <summary>
    /// Definition of an MCP tool advertised to Claude during tools/list.
    /// </summary>
    public sealed record McpToolDefinition(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("inputSchema")] object InputSchema
    );

    /// <summary>
    /// Content element inside an MCP tool call result.
    /// </summary>
    public sealed record McpContentItem(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text
    );

    /// <summary>
    /// Response payload for an MCP tools/call request.
    /// </summary>
    public sealed record McpCallToolResult(
        [property: JsonPropertyName("content")] IReadOnlyList<McpContentItem> Content,
        [property: JsonPropertyName("isError")] bool IsError = false
    );
}
