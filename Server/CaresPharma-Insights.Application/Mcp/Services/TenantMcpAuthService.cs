using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CaresPharma_Insights.Application.Mcp.Models;
using CaresPharma_Insights.Domain.Enums;
using CaresPharma_Insights.Domain.Interfaces;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    /// <summary>
    /// Enterprise cryptographic authentication service for multi-tenant MCP access.
    /// Strictly typed with zero var usage, constant-time HMAC-SHA256 comparison, and instant kill-switch integration.
    /// </summary>
    public sealed class TenantMcpAuthService : ITenantMcpAuthService
    {
        private readonly ITenantMcpClientRepository _repository;
        private readonly IMcpSessionManager _sessionManager;
        private readonly ILogger<TenantMcpAuthService> _logger;

        public TenantMcpAuthService(
            ITenantMcpClientRepository repository,
            IMcpSessionManager sessionManager,
            ILogger<TenantMcpAuthService> logger)
        {
            _repository = repository;
            _sessionManager = sessionManager;
            _logger = logger;
        }

        public async Task<GeneratedMcpCredentials> GenerateCredentialsAsync(
            string pharmacyOwnerId,
            string? clientName = null,
            int? expiryDays = null,
            string? adminId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pharmacyOwnerId))
            {
                throw new ArgumentException("PharmacyOwnerId cannot be empty.", nameof(pharmacyOwnerId));
            }

            // High-entropy 128-bit Client ID and 256-bit Client Secret
            string clientId = $"cp_live_cid_{GenerateRandomHex(16)}";
            string clientSecret = $"cp_sec_{GenerateRandomHex(32)}";
            string salt = GenerateRandomHex(16);

            string secretHash = ComputeHmacSha256(clientSecret, salt);

            DateTime? expiresAtUtc = expiryDays.HasValue && expiryDays.Value > 0
                ? DateTime.UtcNow.AddDays(expiryDays.Value)
                : null;

            TenantMcpClient client = new TenantMcpClient(
                id: Guid.NewGuid(),
                pharmacyOwnerId: pharmacyOwnerId.Trim(),
                clientId: clientId,
                clientSecretHash: secretHash,
                clientSecretSalt: salt,
                clientName: clientName ?? "Claude AI Connector",
                expiresAtUtc: expiresAtUtc,
                createdByAdminId: adminId
            );

            await _repository.InsertAsync(client, cancellationToken);

            _logger.LogInformation("New MCP Client Credentials issued for Tenant: {TenantId}, ClientId: {ClientId}", 
                pharmacyOwnerId, clientId);

            return new GeneratedMcpCredentials(clientId, clientSecret, pharmacyOwnerId, expiresAtUtc);
        }

        public async Task<TenantSecurityContext?> ValidateCredentialsAsync(
            string clientId,
            string clientSecret,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return null;
            }

            TenantMcpClient? client = await _repository.GetByClientIdAsync(clientId.Trim(), cancellationToken);
            if (client == null)
            {
                _logger.LogWarning("MCP Auth Failed: ClientId {ClientId} not found.", clientId);
                return null;
            }

            // Verify active status and expiration boundary
            if (!client.CanAuthenticate)
            {
                _logger.LogWarning("MCP Auth Failed: ClientId {ClientId} is inactive or expired. Tenant: {TenantId}", 
                    clientId, client.PharmacyOwnerId);
                
                await _repository.LogActivityAsync(new TenantMcpActivityLog
                {
                    Id = Guid.NewGuid(),
                    PharmacyOwnerId = client.PharmacyOwnerId,
                    ClientId = clientId,
                    EventType = McpActivityEventType.AuthFailed,
                    ClientIpAddress = ipAddress,
                    UserAgent = userAgent,
                    IsSuccess = false,
                    ErrorMessage = client.IsExpired ? "Credentials expired" : "Credentials deactivated",
                    TimestampUtc = DateTime.UtcNow
                }, cancellationToken);

                return null;
            }

            // Constant-time hash verification prevents side-channel timing attacks
            string computedHash = ComputeHmacSha256(clientSecret.Trim(), client.ClientSecretSalt);
            byte[] computedBytes = Encoding.UTF8.GetBytes(computedHash);
            byte[] storedBytes = Encoding.UTF8.GetBytes(client.ClientSecretHash);

            if (!CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes))
            {
                _logger.LogWarning("MCP Auth Failed: Secret mismatch for ClientId {ClientId}", clientId);

                await _repository.LogActivityAsync(new TenantMcpActivityLog
                {
                    Id = Guid.NewGuid(),
                    PharmacyOwnerId = client.PharmacyOwnerId,
                    ClientId = clientId,
                    EventType = McpActivityEventType.AuthFailed,
                    ClientIpAddress = ipAddress,
                    UserAgent = userAgent,
                    IsSuccess = false,
                    ErrorMessage = "Invalid client secret",
                    TimestampUtc = DateTime.UtcNow
                }, cancellationToken);

                return null;
            }

            // Update telemetry and activity timestamps
            await _repository.UpdateLastUsedAsync(client.Id, DateTime.UtcNow, cancellationToken);

            await _repository.LogActivityAsync(new TenantMcpActivityLog
            {
                Id = Guid.NewGuid(),
                PharmacyOwnerId = client.PharmacyOwnerId,
                ClientId = clientId,
                EventType = McpActivityEventType.Handshake,
                ClientIpAddress = ipAddress,
                UserAgent = userAgent,
                IsSuccess = true,
                TimestampUtc = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation("MCP Client authenticated successfully for Tenant: {TenantId}", client.PharmacyOwnerId);

            return new TenantSecurityContext(
                PharmacyOwnerId: client.PharmacyOwnerId,
                UserId: client.PharmacyOwnerId,
                UserRole: "PharmacyOwner",
                AuthSource: "McpClientCredentials"
            );
        }

        public async Task<bool> RevokeCredentialsAsync(string pharmacyOwnerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pharmacyOwnerId))
            {
                return false;
            }

            await _repository.RevokeByOwnerIdAsync(pharmacyOwnerId, cancellationToken);

            // Execute instant kill-switch termination for any active SSE channels
            int closedSessionsCount = _sessionManager.TerminateSessionsForTenant(pharmacyOwnerId);
            _logger.LogInformation("MCP Credentials revoked for Tenant: {TenantId}. Terminated {Closed} active sessions.", 
                pharmacyOwnerId, closedSessionsCount);

            return true;
        }

        public async Task<bool> ToggleStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
        {
            await _repository.UpdateStatusAsync(id, isActive, cancellationToken);
            return true;
        }

        private static string GenerateRandomHex(int byteLength)
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(byteLength);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string ComputeHmacSha256(string content, string salt)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(salt);
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);

            using HMACSHA256 hmac = new HMACSHA256(keyBytes);
            byte[] hashBytes = hmac.ComputeHash(contentBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
