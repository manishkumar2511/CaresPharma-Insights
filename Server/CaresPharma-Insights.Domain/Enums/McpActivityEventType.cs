namespace CaresPharma_Insights.Domain.Enums
{
    /// <summary>
    /// Strongly-typed event classifications for tenant MCP telemetry and audit logs.
    /// Eliminates hardcoded magic strings across the system.
    /// </summary>
    public enum McpActivityEventType
    {
        Handshake = 1,
        ToolCall = 2,
        AuthFailed = 3,
        Revoked = 4,
        StatusToggled = 5
    }
}
