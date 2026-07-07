using LupiraCalApi.Domain;

namespace LupiraCalApi.Scheduling;

/// <summary>The calendar a fire is delivered under: one calendar drives the stamped <c>calendar_id</c>, the
/// <c>expire_after</c> kind, and the owning principal — so the three can never disagree.</summary>
public sealed record FireContext(Guid CalendarId, CalendarKind? Kind, Guid? PrincipalId);
