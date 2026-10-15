using System;

namespace CaresPharma_Insights.Domain.Models
{
    /// <summary>
    /// Represents an active tenant & user context for cross-cutting security enforcement.
    /// </summary>
    public sealed record TenantSecurityContext(
        string? PharmacyOwnerId,
        string? UserId,
        string UserRole,
        string AuthSource
    )
    {
        public bool IsSuperAdmin => string.Equals(UserRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

        public static TenantSecurityContext Anonymous =>
            new(PharmacyOwnerId: null, UserId: null, UserRole: "Anonymous", AuthSource: "None");

        public static TenantSecurityContext SuperAdmin(string? userId = null, string authSource = "System") =>
            new(PharmacyOwnerId: null, UserId: userId ?? "superadmin", UserRole: "SuperAdmin", AuthSource: authSource);
    }
}
