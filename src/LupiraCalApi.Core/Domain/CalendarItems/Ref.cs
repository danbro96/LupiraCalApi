namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A reference the fired payload acts on. <c>Id</c> for Event/Contact/Task; <c>Url</c> for External.</summary>
public sealed record Ref(RefKind Kind, Guid? Id, string? Url);
