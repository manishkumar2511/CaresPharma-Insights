namespace CaresPharma_Insights.Shared.Dtos
{
    /// <summary>
    /// RFC 6749 compliant token response payload.
    /// </summary>
    public sealed record OAuthTokenResponse(
        string access_token,
        string token_type,
        int expires_in
    );
}
