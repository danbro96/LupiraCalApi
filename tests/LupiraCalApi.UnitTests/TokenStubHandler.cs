using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace LupiraCalApi.UnitTests;

/// <summary>Records every token-endpoint request and answers with <see cref="Respond"/>.</summary>
internal sealed class TokenStubHandler : HttpMessageHandler
{
    public List<Dictionary<string, string>> Forms { get; } = [];

    public Func<Dictionary<string, string>, (HttpStatusCode Status, string Body)> Respond { get; set; } =
        form => (HttpStatusCode.OK, $$"""{"access_token":"tok-{{form.GetValueOrDefault("audience") ?? form["client_id"]}}","expires_in":300}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct)).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        lock (Forms) Forms.Add(form);
        var (status, body) = Respond(form);
        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}
