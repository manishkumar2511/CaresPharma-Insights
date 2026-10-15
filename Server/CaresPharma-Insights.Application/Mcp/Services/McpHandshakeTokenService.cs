using System;
using System.Collections.Concurrent;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpHandshakeTokenService
    {
        McpHandshakeToken GenerateToken(string pharmacyOwnerId, string userId, string userRole);
        TenantSecurityContext? ValidateAndClaimToken(string token);
    }

    /// <summary>
    /// Thread-safe service for issuing and validating short-lived 1-Click Claude handshake tokens.
    /// In production, can be backed by Redis or DB; built here with high-performance concurrent memory storage.
    /// </summary>
    public sealed class McpHandshakeTokenService : IMcpHandshakeTokenService
    {
        private static readonly TimeSpan DefaultTokenValidity = TimeSpan.FromMinutes(10);
        private readonly ConcurrentDictionary<string, McpHandshakeToken> _tokens = new();

        public McpHandshakeToken GenerateToken(string pharmacyOwnerId, string userId, string userRole)
        {
            var tokenRecord = new McpHandshakeToken(pharmacyOwnerId, userId, userRole, DefaultTokenValidity);
            _tokens[tokenRecord.Token] = tokenRecord;
            return tokenRecord;
        }

        public TenantSecurityContext? ValidateAndClaimToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            if (_tokens.TryGetValue(token, out var record) && record.IsValid)
            {
                record.MarkAsClaimed();
                return new TenantSecurityContext(
                    PharmacyOwnerId: record.PharmacyOwnerId,
                    UserId: record.UserId,
                    UserRole: record.UserRole,
                    AuthSource: "Claude1ClickHandshake"
                );
            }

            return null;
        }
    }
}
