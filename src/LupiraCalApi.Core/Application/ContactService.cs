using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Mappers;
using LupiraCalApi.Serialization;
using Marten;
using JasperFx.Events;

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
    /// Requires write on this contact's book and read on the target's; the DAV import path is deliberately laxer (no target check).
    /// Kinship invariant: siblinghood is expressed as shared parentage, so a <c>Sibling</c> add between contacts where either
    /// side already has a parent instead assigns that parent to the other (no explicit edge stored); and adding a
    /// <c>Parent</c>/<c>Child</c> dissolves the newly-parented contact's explicit sibling edges into shared parentage.</summary>
    public async Task<OpResult<ContactDto>> AddRelationAsync(Guid principalId, Guid id, AddContactRelationRequest r, CancellationToken ct = default)
    {
        var writer = new RelationWriter(session);
        var c = await writer.LoadAsync(id, ct);
        if (c is null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (r.ToContactId == id) return OpResult<ContactDto>.Invalid("A contact cannot relate to itself.");

        var target = await session.LoadAsync<Contact>(r.ToContactId, ct);
        if (target is null || target.DeletedAt is not null) return OpResult<ContactDto>.Invalid("Related contact not found.");
        if (!await access.CanReadAddressBookAsync(principalId, target.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to the related contact.");

        var label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim();

        // Sibling where a parent is already recorded on either side → express it as shared parentage instead of an edge.
        if (r.Kind == ContactRelationKind.Sibling)
        {
            var fromParents = await ParentIdsAsync(id, c.Relations, ct);
            var toParents = await ParentIdsAsync(r.ToContactId, target.Relations, ct);
            if (fromParents.Count > 0 || toParents.Count > 0)
            {
                if (fromParents.Count > 0 && !await access.CanWriteAddressBookAsync(principalId, target.AddressBookId, ct))
                    return OpResult<ContactDto>.Forbidden("No write access to the related contact to assign its parent.");
                foreach (var p in fromParents) await writer.AddParentAsync(r.ToContactId, p, ct);
                foreach (var p in toParents) await writer.AddParentAsync(id, p, ct);
                await session.SaveChangesAsync(ct);
                return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
            }
        }

        if (c.Relations.Any(x => x.ToContactId == r.ToContactId && x.Kind == r.Kind && x.Label == label))
            return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // identical edge: no event, no ETag churn

        await writer.UpsertAsync(id, r.ToContactId, r.Kind, label, ct);

        // Gaining a parent dissolves the newly-parented contact's explicit sibling edges into shared parentage.
        if (r.Kind == ContactRelationKind.Parent)
            await DissolveSiblingsAsync(writer, principalId, id, await ParentIdsAsync(id, writer.WorkingRelations(id), ct), ct);
        else if (r.Kind == ContactRelationKind.Child)
        {
            var childParents = await ParentIdsAsync(r.ToContactId, target.Relations, ct);
            childParents.Add(id);
            await DissolveSiblingsAsync(writer, principalId, r.ToContactId, childParents, ct);
        }

        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
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
    public async Task<OpResult<List<ContactRelationEntryDto>>> ListRelationsAsync(Guid principalId, Guid id, bool includeInferred = false, CancellationToken ct = default)
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
                entries.Add(new ContactRelationEntryDto { ContactId = t.Id, DisplayName = t.DisplayName, Kind = r.Kind.AsKinship(), Label = r.Label, Direction = ContactRelationDirection.Outgoing });

        var sources = await session.Query<Contact>()
            .Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == id)).ToListAsync(ct);
        foreach (var s in sources.Where(s => s.Id != id && books.Contains(s.AddressBookId)).OrderBy(s => s.DisplayName))
            foreach (var edge in s.Relations.Where(r => r.ToContactId == id))
                entries.Add(new ContactRelationEntryDto { ContactId = s.Id, DisplayName = s.DisplayName, Kind = edge.Kind.Inverse().AsKinship(), Label = null, Direction = ContactRelationDirection.Incoming });

        if (includeInferred)
        {
            // Kinship derives from the parent/child graph, which can span address books — resolve over all readable contacts.
            var all = (await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct))
                .Where(x => books.Contains(x.AddressBookId)).ToList();
            var byId = all.ToDictionary(x => x.Id);
            foreach (var kin in KinshipInference.Infer(id, all))
                if (byId.TryGetValue(kin.ContactId, out var k))
                    entries.Add(new ContactRelationEntryDto { ContactId = k.Id, DisplayName = k.DisplayName, Kind = kin.Kind, Label = null, Direction = ContactRelationDirection.Incoming, Provenance = RelationProvenance.Inferred });
        }

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

    // ---- Kinship invariant + sweep ----

    /// <summary>One-time (idempotent) cleanup that converts every explicit Sibling edge whose endpoints have a recorded
    /// parent into shared parentage, so siblinghood is uniformly derived. Scoped to the caller's writable books.</summary>
    public async Task<OpResult<int>> NormalizeSiblingsAsync(Guid principalId, Guid? addressBookId, CancellationToken ct = default)
    {
        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        if (addressBookId is { } abid)
        {
            if (!books.Contains(abid)) return OpResult<int>.Forbidden("No access to this address book.");
            books = [abid];
        }

        var total = 0;
        // Fixed point: a pass may parent a contact whose own siblings only convert on the next pass; converges since each
        // conversion strictly removes a (parent ∧ sibling-edge) violation. The pass count is bounded by the contact count.
        for (var pass = 0; ; pass++)
        {
            var all = (await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct))
                .Where(x => books.Contains(x.AddressBookId)).ToList();
            var byId = all.ToDictionary(x => x.Id);
            var writer = new RelationWriter(session);
            var converted = 0;

            foreach (var c in all)
            {
                var (parents, siblings) = KinshipInference.Normalize(c.Id, all);
                if (parents.Count == 0 || siblings.Count == 0) continue;
                foreach (var sib in siblings)
                {
                    if (!byId.TryGetValue(sib, out var sc) || !await access.CanWriteAddressBookAsync(principalId, sc.AddressBookId, ct)) continue;
                    foreach (var p in parents) await writer.AddParentAsync(sib, p, ct);
                    if (c.Relations.Any(r => r.ToContactId == sib && r.Kind == ContactRelationKind.Sibling)) await writer.RemoveSiblingAsync(c.Id, sib, ct);
                    if (sc.Relations.Any(r => r.ToContactId == c.Id && r.Kind == ContactRelationKind.Sibling)) await writer.RemoveSiblingAsync(sib, c.Id, ct);
                    converted++;
                }
            }

            await session.SaveChangesAsync(ct);
            total += converted;
            if (converted == 0 || pass >= all.Count) break;
        }
        return OpResult<int>.Ok(total);
    }

    // Parents of a contact = its outgoing Parent edges (from the supplied relation list) ∪ contacts holding a Child edge to it.
    private async Task<HashSet<Guid>> ParentIdsAsync(Guid id, IReadOnlyList<ContactRelation> relations, CancellationToken ct)
    {
        var parents = relations.Where(r => r.Kind == ContactRelationKind.Parent).Select(r => r.ToContactId).ToHashSet();
        var incoming = await session.Query<Contact>().Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == id)).ToListAsync(ct);
        foreach (var x in incoming)
            if (x.Id != id && x.Relations.Any(r => r.ToContactId == id && r.Kind == ContactRelationKind.Child)) parents.Add(x.Id);
        return parents;
    }

    // Give each explicit sibling of the newly-parented contact (in a writable book) that contact's parents, then drop the
    // Sibling edge on whichever side stored it. childParents already reflects the just-added parent.
    private async Task DissolveSiblingsAsync(RelationWriter writer, Guid principalId, Guid childId, IReadOnlyCollection<Guid> childParents, CancellationToken ct)
    {
        if (childParents.Count == 0) return;
        if (await writer.LoadAsync(childId, ct) is null) return;

        var outgoing = writer.WorkingRelations(childId).Where(r => r.Kind == ContactRelationKind.Sibling).Select(r => r.ToContactId).ToHashSet();
        var incoming = await session.Query<Contact>().Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == childId)).ToListAsync(ct);
        var incomingSibs = incoming.Where(x => x.Relations.Any(r => r.ToContactId == childId && r.Kind == ContactRelationKind.Sibling)).ToDictionary(x => x.Id);

        foreach (var sib in outgoing.Union(incomingSibs.Keys).ToList())
        {
            var sc = incomingSibs.TryGetValue(sib, out var found) ? found : await session.LoadAsync<Contact>(sib, ct);
            if (sc is null || sc.DeletedAt is not null || !await access.CanWriteAddressBookAsync(principalId, sc.AddressBookId, ct)) continue;
            foreach (var p in childParents) await writer.AddParentAsync(sib, p, ct);
            if (outgoing.Contains(sib)) await writer.RemoveSiblingAsync(childId, sib, ct);
            if (incomingSibs.ContainsKey(sib)) await writer.RemoveSiblingAsync(sib, childId, ct);
        }
    }

    /// <summary>Batches relation-edge writes across several contact streams in one session, tracking each contact's evolving
    /// edge list so multiple appends to the same contact carry correct incremental content hashes.</summary>
    private sealed class RelationWriter(IDocumentSession session)
    {
        private sealed record Entry(IEventStream<Contact> Stream, Contact Contact, List<ContactRelation> Relations);
        private readonly Dictionary<Guid, Entry?> _entries = new();

        /// <summary>Current (pre-append) aggregate, or null if missing/deleted; caches the writable stream + working edge list.</summary>
        public async Task<Contact?> LoadAsync(Guid id, CancellationToken ct) => (await GetAsync(id, ct))?.Contact;

        public IReadOnlyList<ContactRelation> WorkingRelations(Guid id) =>
            _entries.TryGetValue(id, out var e) && e is not null ? e.Relations : [];

        public async Task UpsertAsync(Guid id, Guid toId, ContactRelationKind kind, string? label, CancellationToken ct)
        {
            if (await GetAsync(id, ct) is not { } e) return;
            e.Relations.RemoveAll(r => r.ToContactId == toId && r.Kind == kind);
            e.Relations.Add(new ContactRelation { ToContactId = toId, Kind = kind, Label = label });
            e.Stream.AppendOne(new ContactRelationAdded(id, toId, kind, label, HashOf(e)));
        }

        public async Task AddParentAsync(Guid childId, Guid parentId, CancellationToken ct)
        {
            if (await GetAsync(childId, ct) is not { } e) return;
            if (e.Relations.Any(r => r.ToContactId == parentId && r.Kind == ContactRelationKind.Parent)) return;
            e.Relations.Add(new ContactRelation { ToContactId = parentId, Kind = ContactRelationKind.Parent, Label = null });
            e.Stream.AppendOne(new ContactRelationAdded(childId, parentId, ContactRelationKind.Parent, null, HashOf(e)));
        }

        public async Task RemoveSiblingAsync(Guid id, Guid toId, CancellationToken ct)
        {
            if (await GetAsync(id, ct) is not { } e) return;
            if (e.Relations.RemoveAll(r => r.ToContactId == toId && r.Kind == ContactRelationKind.Sibling) == 0) return;
            e.Stream.AppendOne(new ContactRelationRemoved(id, toId, ContactRelationKind.Sibling, HashOf(e)));
        }

        private async Task<Entry?> GetAsync(Guid id, CancellationToken ct)
        {
            if (_entries.TryGetValue(id, out var cached)) return cached;
            var stream = await session.Events.FetchForWriting<Contact>(id, ct);
            var c = stream.Aggregate;
            var entry = c is null || c.DeletedAt is not null ? null : new Entry(stream, c, [.. c.Relations]);
            _entries[id] = entry;
            return entry;
        }

        private static string HashOf(Entry e) => ContentHash.Of(CanonicalVcard(e.Contact.ExternalId, FieldsOf(e.Contact), e.Relations));
    }
}
