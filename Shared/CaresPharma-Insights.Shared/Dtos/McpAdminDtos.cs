namespace CaresPharma_Insights.Shared.Dtos
{
    public sealed record GenerateTenantMcpKeyRequest(
        string? ClientName,
        int? ExpiryDays
    );

    public sealed record ToggleMcpKeyStatusRequest(
        bool IsActive
    );
}
