namespace LupiraCalApi.Domain;

/// <summary>Created via REST/MCP from structured fields (hash derived from the canonical vCard).</summary>
public record ContactCreated(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Fields, string ContentHash);

/// <summary>Created or replaced from a CardDAV PUT — parsed into structured fields (no blob retained); hash derived from the canonical form.</summary>
public record ContactImported(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Parsed, string ContentHash);

public record ContactRevised(Guid ContactId, ContactFields Fields, string ContentHash);
public record ContactDeleted(Guid ContactId);
public record ContactRestored(Guid ContactId, string ContentHash);

/// <summary>Replaces the contact's postal addresses (each → an <c>address</c>-kind <see cref="Place"/>).</summary>
public record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses);

/// <summary>Replaces the contact's social/IM handles (vCard <c>IMPP</c>/<c>X-SOCIALPROFILE</c>).</summary>
public record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles);
