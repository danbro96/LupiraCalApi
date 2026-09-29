namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>Reads a participation role or RSVP in either spelling clients send: the iCalendar token
/// (<c>opt-participant</c>, <c>needs-action</c>, any case) or the enum name (<c>OptionalParticipant</c>).</summary>
public static class ParticipationTokens
{
    public const string RoleTokens = "chair|req-participant|opt-participant|non-participant";
    public const string StatusTokens = "needs-action|accepted|declined|tentative|delegated";

    private static readonly Dictionary<string, ParticipationRole> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chair"] = ParticipationRole.Chair,
        ["req-participant"] = ParticipationRole.RequiredParticipant,
        ["opt-participant"] = ParticipationRole.OptionalParticipant,
        ["non-participant"] = ParticipationRole.NonParticipant,
    };

    public static bool TryParseRole(string? value, out ParticipationRole role) =>
        Roles.TryGetValue(value?.Trim() ?? string.Empty, out role) || TryParseName(value, out role);

    public static bool TryParseStatus(string? value, out ParticipationStatus status) =>
        TryParseName(value?.Replace("-", string.Empty, StringComparison.Ordinal), out status);

    // IsDefined rejects numeric strings ("99") that Enum.TryParse would otherwise accept.
    private static bool TryParseName<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}
