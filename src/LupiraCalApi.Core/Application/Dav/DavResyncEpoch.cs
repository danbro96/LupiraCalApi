namespace LupiraCalApi.Core.Application.Dav;

/// <summary>Advanced when stored ETags change without new events (a projection rebuild): sync tokens minted under an
/// earlier epoch stop parsing, so clients take the full listing and re-fetch whatever ETags changed.</summary>
public sealed class DavResyncEpoch
{
    public const string SingletonId = "calendar-items";

    public string Id { get; set; } = SingletonId;

    public int Value { get; set; }
}
