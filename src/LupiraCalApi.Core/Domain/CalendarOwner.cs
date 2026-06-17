namespace LupiraCalApi.Domain;

/// <summary>A principal's access grant on a calendar (plain membership document; multi-owner). Composite identity (calendar:principal).</summary>
public sealed class CalendarOwner
{
    public string Id { get; set; } = "";
    public Guid CalendarId { get; set; }
    public Guid PrincipalId { get; set; }
    public Access Access { get; set; }

    public static string MakeId(Guid calendarId, Guid principalId) => $"{calendarId:N}:{principalId:N}";
}
