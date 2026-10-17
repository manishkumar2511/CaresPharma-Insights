using System;
using System.Collections.Concurrent;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpHandshakeTokenService
    {
        McpHandshakeToken GenerateToken(string pharmacyOwnerId, string userId, string userRole);
        McpHandshakeToken GenerateSessionToken(string pharmacyOwnerId, string userId, string userRole, TimeSpan validityDuration);
        TenantSecurityContext? ValidateAndClaimToken(string token);
    }

    /// <summary>
    /// Thread-safe service for issuing and validating short-lived 1-Click Claude handshake tokens
    /// and persistent bearer session tokens for OAuth flows.
    /// </summary>
    public sealed class McpHandshakeTokenService : IMcpHandshakeTokenService
    {
        private static readonly TimeSpan DefaultTokenValidity = TimeSpan.FromMinutes(10);
        private readonly ConcurrentDictionary<string, McpHandshakeToken> _tokens = new();

        public McpHandshakeToken GenerateToken(string pharmacyOwnerId, string userId, string userRole)
        {
            McpHandshakeToken tokenRecord = new McpHandshakeToken(pharmacyOwnerId, userId, userRole, DefaultTokenValidity, isReusable: false);
            _tokens[tokenRecord.Token] = tokenRecord;
            return tokenRecord;
        }

        public McpHandshakeToken GenerateSessionToken(string pharmacyOwnerId, string userId, string userRole, TimeSpan validityDuration)
        {
            McpHandshakeToken tokenRecord = new McpHandshakeToken(pharmacyOwnerId, userId, userRole, validityDuration, isReusable: true);
            _tokens[tokenRecord.Token] = tokenRecord;
            return tokenRecord;
        }

        public TenantSecurityContext? ValidateAndClaimToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            if (_tokens.TryGetValue(token, out McpHandshakeToken? record) && record.IsValid)
            {
                if (!record.IsReusable)
                {
                    record.MarkAsClaimed();
                }

                return new TenantSecurityContext(
                    PharmacyOwnerId: record.PharmacyOwnerId,
                    UserId: record.UserId,
                    UserRole: record.UserRole,
                    AuthSource: record.IsReusable ? "ClaudeOAuthSession" : "Claude1ClickHandshake"
                );
            }

            return null;
        }
    }
}
