using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CaresPharma_Insights.Api.Infrastructure
{
    public class OpenRouterHttpClientHandler : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null && request.RequestUri.Host == "api.openai.com")
            {
                var uriBuilder = new UriBuilder(request.RequestUri)
                {
                    Scheme = "https",
                    Host = "openrouter.ai",
                    Path = "/api" + request.RequestUri.AbsolutePath
                };
                request.RequestUri = uriBuilder.Uri;
                
                request.Headers.Add("HTTP-Referer", "http://localhost:5500");
                request.Headers.Add("X-Title", "CaresPharma-Insights");
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
