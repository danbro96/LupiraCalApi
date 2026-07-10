using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Mappers;
using LupiraCalApi.Serialization;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Contact core shared by REST, CardDAV, and MCP. Event-sourced like <see cref="CalendarItemService"/>; a contact belongs to one address book.</summary>
public sealed class ContactService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness)
{
    public async Task<OpResult<ContactDto>> CreateAsync(Guid principalId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, r.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this address book.");

        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var fields = new ContactFields(r.NamePrefix, r.GivenName, r.MiddleName, r.FamilyName, r.NameSuffix, r.Nickname, r.Emails, r.Phones, r.Birthday, r.Tags);
        var hash = ContentHash.Of(CanonicalVcard(uid, fields, []));

        session.Events.StartStream<Contact>(id, new ContactCreated(id, r.AddressBookId, uid, fields, hash));
        await session.SaveChangesAsync(ct);
        var c = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(c!, ct));
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
        var ordered = contacts.OrderBy(c => c.DisplayName).ToList();
        var scores = await completeness.ScoreContactsAsync(ordered, ct);
        return OpResult<List<ContactDto>>.Ok([.. ordered.Select(c => c.ToResponse(scores[c.Id]))]);
    }

    public async Task<OpResult<ContactDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to this contact.");
        return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
    }

    /// <summary>Merge-update an existing contact: provided scalars overwrite, provided email/phone/tag arrays
    /// union onto the existing values (deduped), null fields are kept. Never wipes unmentioned fields.</summary>
    public async Task<OpResult<ContactDto>> ReviseAsync(Guid principalId, Guid id, ReviseContactRequest r, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var merged = new ContactFields(
            r.NamePrefix ?? c.NamePrefix,
            r.GivenName ?? c.GivenName,
            r.MiddleName ?? c.MiddleName,
            r.FamilyName ?? c.FamilyName,
            r.NameSuffix ?? c.NameSuffix,
            r.Nickname ?? c.Nickname,
            MergeDistinct(c.Emails, r.Emails),
            MergeDistinct(c.Phones, r.Phones),
            r.Birthday ?? c.Birthday,
            MergeDistinct(c.Tags, r.Tags));
        var hash = ContentHash.Of(CanonicalVcard(c.ExternalId, merged, c.Relations));

        stream.AppendOne(new ContactRevised(id, merged, hash));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    // Union an incoming multi-value field onto the existing one (case-insensitive dedupe); a null/empty
    // incoming keeps the existing values (enrichment adds, never clears).
    private static string[]? MergeDistinct(string[]? existing, string[]? incoming)
    {
        if (incoming is null || incoming.Length == 0) return existing;
        if (existing is null || existing.Length == 0) return incoming;
        return [.. existing.Concat(incoming).Distinct(StringComparer.OrdinalIgnoreCase)];
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

    // ---- Relations (directed edges on the from-contact's stream; see ContactRelation) ----

    /// <summary>Upserts "<c>r.ToContactId</c> is this contact's <c>r.Kind</c>" (re-adding the same key revises the label).
    /// Requires write on this contact's book and read on the target's; the DAV import path is deliberately laxer (no target check).</summary>
    public async Task<OpResult<ContactDto>> AddRelationAsync(Guid principalId, Guid id, AddContactRelationRequest r, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (r.ToContactId == id) return OpResult<ContactDto>.Invalid("A contact cannot relate to itself.");

        var target = await session.LoadAsync<Contact>(r.ToContactId, ct);
        if (target is null || target.DeletedAt is not null) return OpResult<ContactDto>.Invalid("Related contact not found.");
        if (!await access.CanReadAddressBookAsync(principalId, target.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to the related contact.");

        var label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim();
        if (c.Relations.Any(x => x.ToContactId == r.ToContactId && x.Kind == r.Kind && x.Label == label))
            return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // identical edge: no event, no ETag churn

        var next = c.Relations.Where(x => x.ToContactId != r.ToContactId || x.Kind != r.Kind)
            .Append(new ContactRelation { ToContactId = r.ToContactId, Kind = r.Kind, Label = label }).ToList();
        var hash = ContentHash.Of(CanonicalVcard(c.ExternalId, FieldsOf(c), next));
        stream.AppendOne(new ContactRelationAdded(id, r.ToContactId, r.Kind, label, hash));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    public async Task<OpResult<ContactDto>> RemoveRelationAsync(Guid principalId, Guid id, Guid toContactId, ContactRelationKind kind, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (!c.Relations.Any(x => x.ToContactId == toContactId && x.Kind == kind)) return OpResult<ContactDto>.NotFound();

        var next = c.Relations.Where(x => x.ToContactId != toContactId || x.Kind != kind).ToList();
        var hash = ContentHash.Of(CanonicalVcard(c.ExternalId, FieldsOf(c), next));
        stream.AppendOne(new ContactRelationRemoved(id, toContactId, kind, hash));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    /// <summary>Resolved two-way view: outgoing edges (snapshot order) then incoming ones (by display name), each entry's
    /// Kind being the other contact's role relative to the viewed one (incoming = derived inverse). Edges whose other side
    /// is deleted, dangling, or outside the caller's readable books are filtered out.</summary>
    public async Task<OpResult<List<ContactRelationEntryDto>>> ListRelationsAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<List<ContactRelationEntryDto>>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<List<ContactRelationEntryDto>>.Forbidden("No access to this contact.");

        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var entries = new List<ContactRelationEntryDto>();

        var targets = (await session.LoadManyAsync<Contact>(ct, c.Relations.Select(r => r.ToContactId).Distinct().ToArray()))
            .Where(t => t.DeletedAt is null && books.Contains(t.AddressBookId)).ToDictionary(t => t.Id);
        foreach (var r in c.Relations)
            if (targets.TryGetValue(r.ToContactId, out var t))
                entries.Add(new ContactRelationEntryDto { ContactId = t.Id, DisplayName = t.DisplayName, Kind = r.Kind, Label = r.Label, Direction = ContactRelationDirection.Outgoing });

        var sources = await session.Query<Contact>()
            .Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == id)).ToListAsync(ct);
        foreach (var s in sources.Where(s => s.Id != id && books.Contains(s.AddressBookId)).OrderBy(s => s.DisplayName))
            foreach (var edge in s.Relations.Where(r => r.ToContactId == id))
                entries.Add(new ContactRelationEntryDto { ContactId = s.Id, DisplayName = s.DisplayName, Kind = edge.Kind.Inverse(), Label = null, Direction = ContactRelationDirection.Incoming });

        return OpResult<List<ContactRelationEntryDto>>.Ok(entries);
    }

    // Order-sensitive: edge order affects the canonical vCard bytes, so a reorder is a real change.
    private static bool RelationsEqual(IReadOnlyList<ContactRelation>? a, IReadOnlyList<ContactRelation> b) =>
        (a ?? []).Select(r => (r.ToContactId, r.Kind, r.Label)).SequenceEqual(b.Select(r => (r.ToContactId, r.Kind, r.Label)));

    // ---- CardDAV write path ----

    public async Task<OpResult<DavWriteResult>> PutVcfAsync(
        Guid principalId, Guid addressBookId, string externalId, string rawVcard, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this address book.");

        var id = DeterministicGuid.From(externalId);
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
        // RELATED lines are authoritative wholesale-replace (a PUT without them clears relations). Unresolvable target
        // uuids are stored as-is — the target may sync in later or be unreadable to this caller; resolved reads filter.
        var relations = (p.Relations ?? [])
            .Where(r => r.ToContactId != id)
            .DistinctBy(r => (r.ToContactId, r.Kind))
            .ToList();
        // PUT parses into structured fields; the ETag is the hash of the canonical vCard regenerated from them (matching CardDAV GET), not the raw blob.
        var hash = ContentHash.Of(CanonicalVcard(externalId, fields, relations));
        stream.AppendOne(new ContactImported(id, addressBookId, externalId, fields, hash));   // also clears soft-delete
        if (!RelationsEqual(existing?.Relations, relations)) stream.AppendOne(new ContactRelationsReplaced(id, relations));
        await session.SaveChangesAsync(ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, hash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid principalId, Guid addressBookId, string externalId, string? ifMatch, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult.Forbidden("No write access to this address book.");
        var id = DeterministicGuid.From(externalId);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        // c.AddressBookId != addressBookId guards the UID-collision case (the contact lives in another book).
        if (c is null || c.DeletedAt is not null || c.AddressBookId != addressBookId) return OpResult.NotFound();
        if (ifMatch is not null && c.ContentHash != ifMatch) return OpResult.Conflict("ETag mismatch.");
        stream.AppendOne(new ContactDeleted(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task<ContactDto> ToDtoAsync(Contact c, CancellationToken ct) =>
        c.ToResponse(await completeness.ScoreContactAsync(c, ct));

    // The canonical vCard for a contact's fields + relation edges — identical to what CardDAV GET regenerates from the snapshot, so the ETag matches.
    private static string CanonicalVcard(string uid, ContactFields f, IReadOnlyList<ContactRelation> relations) =>
        VCardSerializer.Build(uid, VCardSerializer.ComposeFullName(f.NamePrefix, f.GivenName, f.MiddleName, f.FamilyName, f.NameSuffix, f.Nickname),
            f.GivenName, f.FamilyName, null, f.Emails, f.Phones, f.Birthday, relations);

    private static ContactFields FieldsOf(Contact c) =>
        new(c.NamePrefix, c.GivenName, c.MiddleName, c.FamilyName, c.NameSuffix, c.Nickname, c.Emails, c.Phones, c.Birthday, c.Tags);
}
