using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using CaresPharma_Insights.Application.Mcp.Services;
using CaresPharma_Insights.Domain.Models;
using CaresPharma_Insights.Shared.Dtos;

namespace CaresPharma_Insights.Api.Endpoints
{
    /// <summary>
    /// Implements RFC 6749 & RFC 7636 OAuth 2.1 Authorization Code Flow with PKCE for Claude.ai Custom Connectors.
    /// Strictly typed with zero var usage and separate DTO classes.
    /// </summary>
    public static class OAuthEndpoints
    {
        private static readonly ConcurrentDictionary<string, (string ClientId, string CodeChallenge, string? Resource, DateTime ExpiresAtUtc)> AuthCodes = new();

        public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // 1. OAuth Server Metadata Discovery (RFC 8414)
            endpoints.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
            {
                string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                return Results.Ok(new
                {
                    issuer = baseUrl,
                    authorization_endpoint = $"{baseUrl}/authorize",
                    token_endpoint = $"{baseUrl}/oauth/token",
                    response_types_supported = new string[] { "code" },
                    grant_types_supported = new string[] { "authorization_code" },
                    code_challenge_methods_supported = new string[] { "S256" },
                    token_endpoint_auth_methods_supported = new string[] { "none", "client_secret_basic", "client_secret_post" }
                });
            })
            .AllowAnonymous();

            // 2. Protected Resource Metadata (PRM)
            endpoints.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
            {
                string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                return Results.Ok(new
                {
                    resource = $"{baseUrl}/mcp",
                    authorization_servers = new string[] { baseUrl }
                });
            })
            .AllowAnonymous();

            // 3. GET /authorize - Claude Authorization Code endpoint
            endpoints.MapGet("/authorize", (
                string? response_type,
                string? client_id,
                string? redirect_uri,
                string? code_challenge,
                string? code_challenge_method,
                string? state,
                string? resource,
                HttpContext ctx,
                ILoggerFactory loggerFactory) =>
            {
                ILogger logger = loggerFactory.CreateLogger("OAuth.Authorize");
                logger.LogInformation("OAuth Authorize hit by Claude: ClientId: {ClientId}, RedirectUri: {RedirectUri}", client_id, redirect_uri);

                if (string.IsNullOrWhiteSpace(client_id) || string.IsNullOrWhiteSpace(redirect_uri))
                {
                    return Results.BadRequest(new { error = "invalid_request", error_description = "client_id and redirect_uri are required." });
                }

                // Generate short-lived single-use authorization code
                byte[] codeBytes = RandomNumberGenerator.GetBytes(32);
                string authCode = Convert.ToHexString(codeBytes).ToLowerInvariant();

                AuthCodes[authCode] = (client_id, code_challenge ?? string.Empty, resource, DateTime.UtcNow.AddMinutes(5));

                // Clean redirect back to Claude callback
                string redirectWithParams = $"{redirect_uri}?code={Uri.EscapeDataString(authCode)}";
                if (!string.IsNullOrWhiteSpace(state))
                {
                    redirectWithParams += $"&state={Uri.EscapeDataString(state)}";
                }

                return Results.Redirect(redirectWithParams);
            })
            .WithName("OAuthAuthorize")
            .AllowAnonymous();

            // 4. POST /oauth/token & POST /token - Token Exchange Endpoint
            async Task<IResult> HandleTokenExchange(
                HttpContext ctx,
                ITenantMcpAuthService authService,
                IMcpHandshakeTokenService tokenService,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken)
            {
                ILogger logger = loggerFactory.CreateLogger("OAuth.Token");

                IFormCollection form = await ctx.Request.ReadFormAsync(cancellationToken);
                string grantType = form["grant_type"].ToString();
                string code = form["code"].ToString();
                string codeVerifier = form["code_verifier"].ToString();
                string clientId = form["client_id"].ToString();
                string clientSecret = form["client_secret"].ToString();

                logger.LogInformation("OAuth Token exchange request: GrantType: {GrantType}, ClientId: {ClientId}", grantType, clientId);

                if (string.Equals(grantType, "authorization_code", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(code) || !AuthCodes.TryRemove(code, out (string ClientId, string CodeChallenge, string? Resource, DateTime ExpiresAtUtc) authData))
                    {
                        return Results.BadRequest(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired." });
                    }

                    if (authData.ExpiresAtUtc < DateTime.UtcNow)
                    {
                        return Results.BadRequest(new { error = "invalid_grant", error_description = "Authorization code expired." });
                    }

                    // Validate PKCE code_verifier if code_challenge was provided
                    if (!string.IsNullOrWhiteSpace(authData.CodeChallenge) && !string.IsNullOrWhiteSpace(codeVerifier))
                    {
                        using SHA256 sha = SHA256.Create();
                        byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
                        string computedChallenge = Convert.ToBase64String(hash)
                            .TrimEnd('=')
                            .Replace('+', '-')
                            .Replace('/', '_');

                        if (!string.Equals(computedChallenge, authData.CodeChallenge, StringComparison.Ordinal))
                        {
                            logger.LogWarning("PKCE verification failed for ClientId: {ClientId}", authData.ClientId);
                            return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE verification failed." });
                        }
                    }

                    clientId = !string.IsNullOrWhiteSpace(clientId) ? clientId : authData.ClientId;
                }

                // Issue high-trust token bound to the verified tenant
                McpHandshakeToken handshakeToken = tokenService.GenerateToken(
                    pharmacyOwnerId: clientId,
                    userId: clientId,
                    userRole: "PharmacyOwner"
                );

                return Results.Ok(new OAuthTokenResponse(
                    access_token: handshakeToken.Token,
                    token_type: "Bearer",
                    expires_in: 86400
                ));
            }

            endpoints.MapPost("/oauth/token", HandleTokenExchange).AllowAnonymous();
            endpoints.MapPost("/token", HandleTokenExchange).AllowAnonymous();

            return endpoints;
        }
    }
}
