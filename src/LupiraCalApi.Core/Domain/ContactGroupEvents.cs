namespace LupiraCalApi.Domain;

public record ContactGroupCreated(Guid GroupId, Guid AddressBookId, ContactGroupKind Kind, string Name, string? ExternalId);
public record ContactGroupRenamed(Guid GroupId, string Name);
public record ContactAddedToGroup(Guid GroupId, Guid ContactId, DateTimeOffset At);
public record ContactRemovedFromGroup(Guid GroupId, Guid ContactId, DateTimeOffset At);
public record ContactGroupDeleted(Guid GroupId);
