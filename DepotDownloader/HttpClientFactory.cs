using System.Net.Http;
using System.Net.Http.Headers;

namespace DepotDownloader;

internal class HttpClientFactory
{
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();

        var assemblyVersion = typeof(HttpClientFactory).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DepotDownloader", assemblyVersion));

        return client;
    }
}