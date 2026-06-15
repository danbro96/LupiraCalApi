using System.Text.Json;
using System.Text.Json.Nodes;
using LupiraCalApi.Api;
using LupiraCalApi.Data;
using LupiraCalApi.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Domain;

/// <summary>Contact core shared by REST and MCP. Mirrors EventService's revision/change-log bookkeeping per address book.</summary>
public sealed class ContactService(CalDbContext db, AccessService access)
{
    public async Task<ContactDto> CreateAsync(Guid userId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(userId, r.AddressBookId, ct)) throw new AccessDeniedException();

        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var c = new Contact
        {
            Id = Guid.NewGuid(), AddressBookId = r.AddressBookId, VcardUid = uid,
            FullName = r.FullName, GivenName = r.GivenName, FamilyName = r.FamilyName, Organization = r.Organization,
            Emails = r.Emails is null ? null : JsonSerializer.Serialize(r.Emails),
            Phones = r.Phones is null ? null : JsonSerializer.Serialize(r.Phones),
            Birthday = r.Birthday, Tags = r.Tags, Metadata = "{}",
        };
        c.SourceVcard = VCardSerializer.Build(uid, r.FullName, r.GivenName, r.FamilyName, r.Organization, r.Emails, r.Phones, r.Birthday);
        c.ContentHash = ContentHash.Of(c.SourceVcard);

        db.Contacts.Add(c);
        await BumpAndLogAsync(r.AddressBookId, uid, "saved", c.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return Map(c);
    }

    public async Task<List<ContactDto>> QueryAsync(Guid userId, string? query, Guid? addressBookId, CancellationToken ct = default)
    {
        var ids = await access.AccessibleAddressBooks(userId).Select(a => a.Id).ToListAsync(ct);
        if (addressBookId is { } abid)
        {
            if (!ids.Contains(abid)) throw new AccessDeniedException();
            ids = [abid];
        }

        var q = db.Contacts.Where(c => ids.Contains(c.AddressBookId) && c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(c => c.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("simple", term))
                || EF.Functions.TrigramsWordSimilarity(term, c.FullName ?? "") >= 0.3);
        }
        var list = await q.OrderBy(c => c.FullName).ToListAsync(ct);
        return list.Select(Map).ToList();
    }

    public async Task<ContactDto?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (c is null) return null;
        if (!await access.CanReadAddressBookAsync(userId, c.AddressBookId, ct)) throw new AccessDeniedException();
        return Map(c);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Contact not found.");
        if (!await access.CanWriteAddressBookAsync(userId, c.AddressBookId, ct)) throw new AccessDeniedException();

        c.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(c.AddressBookId, c.VcardUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
    }

    // ---- CardDAV write path (Phase 4) ----

    public async Task<(bool Created, string Etag)> PutVcfAsync(
        Guid userId, Guid addressBookId, string vcardUid, string rawVcard, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(userId, addressBookId, ct)) throw new AccessDeniedException();

        var existing = await db.Contacts.FirstOrDefaultAsync(c => c.AddressBookId == addressBookId && c.VcardUid == vcardUid, ct);
        var live = existing is { DeletedAt: null };
        if (ifNoneMatchStar && live) throw new DavPreconditionException("Resource already exists.");
        if (ifMatch is not null && (!live || existing!.ContentHash != ifMatch)) throw new DavPreconditionException("ETag mismatch.");

        var p = VCardSerializer.ParseVCard(rawVcard);
        var c = existing ?? new Contact { Id = Guid.NewGuid(), AddressBookId = addressBookId, VcardUid = vcardUid, Metadata = "{}" };
        c.FullName = p.FullName; c.GivenName = p.GivenName; c.FamilyName = p.FamilyName; c.Organization = p.Organization;
        c.Emails = p.Emails is null ? null : JsonSerializer.Serialize(p.Emails);
        c.Phones = p.Phones is null ? null : JsonSerializer.Serialize(p.Phones);
        c.Birthday = p.Birthday;
        c.SourceVcard = rawVcard;
        c.ContentHash = ContentHash.Of(rawVcard);
        c.DeletedAt = null;   // resurrect if previously deleted

        if (existing is null) db.Contacts.Add(c);
        else c.UpdatedAt = DateTimeOffset.UtcNow;

        await BumpAndLogAsync(addressBookId, vcardUid, "saved", c.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return (!live, c.ContentHash);
    }

    public async Task DeleteByUidAsync(Guid userId, Guid addressBookId, string vcardUid, string? ifMatch, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(
            x => x.AddressBookId == addressBookId && x.VcardUid == vcardUid && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("Contact not found.");
        if (!await access.CanWriteAddressBookAsync(userId, addressBookId, ct)) throw new AccessDeniedException();
        if (ifMatch is not null && c.ContentHash != ifMatch) throw new DavPreconditionException("ETag mismatch.");

        c.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(addressBookId, vcardUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task BumpAndLogAsync(Guid addressBookId, string vcardUid, string changeType, string? hash, CancellationToken ct)
    {
        var book = await db.AddressBooks.FirstAsync(a => a.Id == addressBookId, ct);
        book.Revision += 1;
        book.UpdatedAt = DateTimeOffset.UtcNow;
        db.ContactChanges.Add(new ContactChange
        {
            AddressBookId = addressBookId, Revision = book.Revision,
            ItemVcardUid = vcardUid, ChangeType = changeType, ContentHash = hash,
        });
    }

    private static ContactDto Map(Contact c) => new(
        c.Id, c.AddressBookId, c.VcardUid, c.FullName, c.Organization, c.Birthday, c.Tags,
        JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata), c.ContentHash);
}
