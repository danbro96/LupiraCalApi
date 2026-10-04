namespace LupiraCalApi.Core.Dtos.Me;

/// <summary><c>DefaultTimezone</c> (an IANA zone id) is given to the calendars bootstrap creates; omitted = the server
/// default. Calendars that already exist keep their zone.</summary>
public sealed class BootstrapRequest
{
    public string? DefaultTimezone { get; set; }
}
