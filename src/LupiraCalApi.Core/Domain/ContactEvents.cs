namespace LupiraCalApi.Domain;

/// <summary>Created via REST/MCP from structured fields (hash derived from the canonical vCard).</summary>
public record ContactCreated(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Fields, string ContentHash);

/// <summary>Created or replaced from a CardDAV PUT — parsed into structured fields (no blob retained); hash derived from the canonical form.</summary>
public record ContactImported(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Parsed, string ContentHash);

public record ContactRevised(Guid ContactId, ContactFields Fields, string ContentHash);
public record ContactDeleted(Guid ContactId);
public record ContactRestored(Guid ContactId, string ContentHash);

/// <summary>Replaces the contact's postal addresses (each an optional geo place id + formatted address).</summary>
public record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses);

/// <summary>Replaces the contact's social/IM handles (vCard <c>IMPP</c>/<c>X-SOCIALPROFILE</c>).</summary>
public record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles);

/// <summary>Upserts one directed relation edge keyed by (ToContactId, Kind). Relations appear in the canonical vCard, so the event carries the new hash.</summary>
public record ContactRelationAdded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string? Label, string ContentHash);

public record ContactRelationRemoved(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string ContentHash);

/// <summary>Wholesale replace from a CardDAV PUT (mirrors <see cref="ContactAddressesReplaced"/>). No hash: only ever
/// appended alongside a <see cref="ContactImported"/> whose hash already covers the final fields + relations.</summary>
public record ContactRelationsReplaced(Guid ContactId, IReadOnlyList<ContactRelation> Relations);
