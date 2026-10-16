using System;

namespace CaresPharma_Insights.Domain.Models
{
    /// <summary>
    /// Represents a persistent tenant-scoped MCP client credential issued by Super Admin.
    /// Provides cryptographic authentication for external LLMs (Claude.ai Custom Connectors).
    /// </summary>
    public sealed class TenantMcpClient
    {
        public Guid Id { get; private set; }
        public string PharmacyOwnerId { get; private set; } = string.Empty;
        public string ClientId { get; private set; } = string.Empty;
        public string ClientSecretHash { get; private set; } = string.Empty;
        public string ClientSecretSalt { get; private set; } = string.Empty;
        public string ClientName { get; private set; } = "Claude AI Connector";
        public bool IsActive { get; private set; } = true;
        public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
        public DateTime? ExpiresAtUtc { get; private set; }
        public DateTime? LastUsedAtUtc { get; private set; }
        public string? CreatedByAdminId { get; private set; }

        private TenantMcpClient() { }

        public TenantMcpClient(
            Guid id,
            string pharmacyOwnerId,
            string clientId,
            string clientSecretHash,
            string clientSecretSalt,
            string clientName,
            DateTime? expiresAtUtc,
            string? createdByAdminId)
        {
            if (string.IsNullOrWhiteSpace(pharmacyOwnerId))
                throw new ArgumentException("PharmacyOwnerId cannot be empty.", nameof(pharmacyOwnerId));

            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("ClientId cannot be empty.", nameof(clientId));

            if (string.IsNullOrWhiteSpace(clientSecretHash))
                throw new ArgumentException("ClientSecretHash cannot be empty.", nameof(clientSecretHash));

            Id = id;
            PharmacyOwnerId = pharmacyOwnerId;
            ClientId = clientId;
            ClientSecretHash = clientSecretHash;
            ClientSecretSalt = clientSecretSalt;
            ClientName = string.IsNullOrWhiteSpace(clientName) ? "Claude AI Connector" : clientName;
            IsActive = true;
            CreatedAtUtc = DateTime.UtcNow;
            ExpiresAtUtc = expiresAtUtc;
            CreatedByAdminId = createdByAdminId;
        }

        public bool IsExpired => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value < DateTime.UtcNow;

        public bool CanAuthenticate => IsActive && !IsExpired;

        public void Deactivate()
        {
            IsActive = false;
        }

        public void Activate()
        {
            IsActive = true;
        }

        public void RecordUsage()
        {
            LastUsedAtUtc = DateTime.UtcNow;
        }

        public void UpdateExpiry(DateTime? newExpiryUtc)
        {
            ExpiresAtUtc = newExpiryUtc;
        }
    }
}
