using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Mappers;
using LupiraCalApi.Serialization;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Contact core shared by REST, CardDAV, and MCP. Event-sourced like <see cref="CalendarItemService"/>; a contact belongs to one address book.</summary>
public sealed class ContactService(IDocumentSession session, AccessResolver access)
{
    public async Task<OpResult<ContactDto>> CreateAsync(Guid principalId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, r.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this address book.");

        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var fields = new ContactFields(r.NamePrefix, r.GivenName, r.MiddleName, r.FamilyName, r.NameSuffix, r.Nickname, r.Emails, r.Phones, r.Birthday, r.Tags);
        var vcard = VCardSerializer.Build(uid, ComposeName(fields), r.GivenName, r.FamilyName, null, r.Emails, r.Phones, r.Birthday);
        var hash = ContentHash.Of(vcard);

        session.Events.StartStream<Contact>(id, new ContactCreated(id, r.AddressBookId, uid, fields, vcard, hash));
        await session.SaveChangesAsync(ct);
        var c = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(c!.ToResponse());
    }

    public async Task<OpResult<List<ContactDto>>> QueryAsync(Guid principalId, string? query, Guid? addressBookId, CancellationToken ct = default)
    {
        var bookIds = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        if (addressBookId is { } abid)
        {
            if (!bookIds.Contains(abid)) return OpResult<List<ContactDto>>.Forbidden("No access to this address book.");
            bookIds = [abid];
        }

        var candidates = await session.Query<Contact>().Where(c => c.DeletedAt == null).ToListAsync(ct);
        IEnumerable<Contact> contacts = candidates.Where(c => bookIds.Contains(c.AddressBookId));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            contacts = contacts.Where(c => c.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return OpResult<List<ContactDto>>.Ok(contacts.OrderBy(c => c.DisplayName).Select(c => c.ToResponse()).ToList());
    }

    public async Task<OpResult<ContactDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to this contact.");
        return OpResult<ContactDto>.Ok(c.ToResponse());
    }

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult.Forbidden("No write access to this contact.");
        stream.AppendOne(new ContactDeleted(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    // ---- CardDAV write path ----

    public async Task<OpResult<DavWriteResult>> PutVcfAsync(
        Guid principalId, Guid addressBookId, string vcardUid, string rawVcard, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this address book.");

        var id = DeterministicGuid.From(vcardUid);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var existing = stream.Aggregate;
        // Streams are keyed by vCard UID alone, so a UID can resolve to a contact in another address book; applying
        // a PUT would move it. Refuse unless the caller can also write the book the contact currently lives in.
        if (existing is not null && existing.AddressBookId != addressBookId && !await access.CanWriteAddressBookAsync(principalId, existing.AddressBookId, ct))
            return OpResult<DavWriteResult>.Forbidden("This resource belongs to another collection.");
        var live = existing is { DeletedAt: null };
        if (ifNoneMatchStar && live) return OpResult<DavWriteResult>.Conflict("Resource already exists.");
        if (ifMatch is not null && (!live || existing!.ContentHash != ifMatch)) return OpResult<DavWriteResult>.Conflict("ETag mismatch.");

        var p = VCardSerializer.ParseVCard(rawVcard);
        var fields = new ContactFields(null, p.GivenName, null, p.FamilyName, null, null, p.Emails, p.Phones, p.Birthday, null);
        var hash = ContentHash.Of(rawVcard);
        stream.AppendOne(new ContactVcardPut(id, addressBookId, vcardUid, fields, rawVcard, hash));   // also clears soft-delete
        await session.SaveChangesAsync(ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, hash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid principalId, Guid addressBookId, string vcardUid, string? ifMatch, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult.Forbidden("No write access to this address book.");
        var id = DeterministicGuid.From(vcardUid);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        // c.AddressBookId != addressBookId guards the UID-collision case (the contact lives in another book).
        if (c is null || c.DeletedAt is not null || c.AddressBookId != addressBookId) return OpResult.NotFound();
        if (ifMatch is not null && c.ContentHash != ifMatch) return OpResult.Conflict("ETag mismatch.");
        stream.AppendOne(new ContactDeleted(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private static string ComposeName(ContactFields f)
    {
        var name = string.Join(' ', new[] { f.NamePrefix, f.GivenName, f.MiddleName, f.FamilyName, f.NameSuffix }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length > 0 ? name : (f.Nickname ?? "");
    }
}
