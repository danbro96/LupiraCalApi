namespace LupiraCalApi.Domain;

/// <summary>Created via REST/MCP from structured fields (service built the vCard + hash).</summary>
public record ContactCreated(Guid ContactId, Guid AddressBookId, string VcardUid, ContactFields Fields, string SourceVcard, string ContentHash);

/// <summary>Created or replaced from a CardDAV raw vCard PUT (blob authoritative; <c>Parsed</c> is a projection aid).</summary>
public record ContactVcardPut(Guid ContactId, Guid AddressBookId, string VcardUid, ContactFields Parsed, string SourceVcard, string ContentHash);

public record ContactRevised(Guid ContactId, ContactFields Fields, string SourceVcard, string ContentHash);
public record ContactDeleted(Guid ContactId);
public record ContactRestored(Guid ContactId, string SourceVcard, string ContentHash);

/// <summary>Replaces the contact's postal addresses (each → an <c>address</c>-kind <see cref="Place"/>).</summary>
public record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses);

/// <summary>Replaces the contact's social/IM handles (vCard <c>IMPP</c>/<c>X-SOCIALPROFILE</c>).</summary>
public record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles);
