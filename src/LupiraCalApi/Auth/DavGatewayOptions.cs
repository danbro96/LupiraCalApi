namespace LupiraCalApi.Auth;

/// <summary>Binds <c>DavGateway</c> — the DAV gateway's client id, carried as <c>azp</c> on its service tokens.</summary>
public sealed class DavGatewayOptions
{
    public const string SectionName = "DavGateway";

    public string? ClientId { get; set; }
}
