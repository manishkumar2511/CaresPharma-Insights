using System;
using CaresPharma_Insights.Domain.Enums;

namespace CaresPharma_Insights.Domain.Models
{
    /// <summary>
    /// Telemetry & Audit trail record capturing every MCP connection and tool execution per tenant.
    /// Strictly typed with domain enums.
    /// </summary>
    public sealed class TenantMcpActivityLog
    {
        public Guid Id { get; set; }
        public string PharmacyOwnerId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public McpActivityEventType EventType { get; set; } = McpActivityEventType.Handshake;
        public string? ToolName { get; set; }
        public string? ClientIpAddress { get; set; }
        public string? UserAgent { get; set; }
        public int ExecutionDurationMs { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
