using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CaresPharma_Insights.Api.Infrastructure
{
    public class AiProxyHttpClientHandler : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null && request.RequestUri.Host == "api.openai.com")
            {
                UriBuilder uriBuilder = new UriBuilder(request.RequestUri)
                {
                    Scheme = "https",
                    Host = "generativelanguage.googleapis.com",
                    Path = request.RequestUri.AbsolutePath.Replace("/v1/", "/v1beta/openai/")
                };
                request.RequestUri = uriBuilder.Uri;
            }
            Console.WriteLine("Sending request to: " + request.RequestUri);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
