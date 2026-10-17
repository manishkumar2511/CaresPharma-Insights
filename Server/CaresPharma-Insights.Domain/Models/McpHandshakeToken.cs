using System;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace CaresPharma_Insights.Domain.Models
{
    /// <summary>
    /// Represents a short-lived, single-use handshake token generated when a Pharmacy Owner
    /// clicks "[Connect with Claude]" inside the CaresPharma Web Portal.
    /// Eliminates manual API keys while guaranteeing tenant isolation.
    /// </summary>
    public sealed class McpHandshakeToken
    {
        [JsonPropertyName("token")]
        public string Token { get; private set; }

        [JsonPropertyName("pharmacyOwnerId")]
        public string PharmacyOwnerId { get; private set; }

        [JsonPropertyName("userId")]
        public string UserId { get; private set; }

        [JsonPropertyName("userRole")]
        public string UserRole { get; private set; }

        [JsonPropertyName("createdAtUtc")]
        public DateTime CreatedAtUtc { get; private set; }

        [JsonPropertyName("expiresAtUtc")]
        public DateTime ExpiresAtUtc { get; private set; }

        [JsonPropertyName("isClaimed")]
        public bool IsClaimed { get; private set; }

        [JsonPropertyName("isReusable")]
        public bool IsReusable { get; private set; }

        private McpHandshakeToken()
        {
            Token = string.Empty;
            PharmacyOwnerId = string.Empty;
            UserId = string.Empty;
            UserRole = string.Empty;
        }

        public McpHandshakeToken(string pharmacyOwnerId, string userId, string userRole, TimeSpan validityDuration, bool isReusable = false)
        {
            if (string.IsNullOrWhiteSpace(pharmacyOwnerId))
            {
                throw new ArgumentException("PharmacyOwnerId cannot be empty.", nameof(pharmacyOwnerId));
            }

            Token = GenerateCryptographicSecureToken();
            PharmacyOwnerId = pharmacyOwnerId;
            UserId = userId;
            UserRole = userRole;
            CreatedAtUtc = DateTime.UtcNow;
            ExpiresAtUtc = DateTime.UtcNow.Add(validityDuration);
            IsClaimed = false;
            IsReusable = isReusable;
        }

        public bool IsValid => (IsReusable || !IsClaimed) && DateTime.UtcNow <= ExpiresAtUtc;

        public void MarkAsClaimed()
        {
            IsClaimed = true;
        }

        private static string GenerateCryptographicSecureToken()
        {
            byte[] bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
