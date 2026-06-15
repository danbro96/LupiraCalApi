using System.Text.Json;
using LupiraCalApi.Auth;
using LupiraCalApi.Data;
using LupiraCalApi.Data.Entities;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Mappers;
using LupiraCalApi.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LupiraCalApi.Application;

/// <summary>Contact core shared by REST, DAV, and MCP. Mirrors EventService's revision/change-log bookkeeping per address book.</summary>
public sealed class ContactService(CalDbContext db, AccessResolver access)
{
    public async Task<OpResult<ContactDto>> CreateAsync(Guid userId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(userId, r.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this address book.");

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
        return OpResult<ContactDto>.Ok(c.ToResponse());
    }

    public async Task<OpResult<List<ContactDto>>> QueryAsync(Guid userId, string? query, Guid? addressBookId, CancellationToken ct = default)
    {
        var ids = await access.AccessibleAddressBooks(userId).Select(a => a.Id).ToListAsync(ct);
        if (addressBookId is { } abid)
        {
            if (!ids.Contains(abid)) return OpResult<List<ContactDto>>.Forbidden("No access to this address book.");
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
        return OpResult<List<ContactDto>>.Ok(list.Select(ContactMapper.ToResponse).ToList());
    }

    public async Task<OpResult<ContactDto>> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (c is null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(userId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to this contact.");
        return OpResult<ContactDto>.Ok(c.ToResponse());
    }

    public async Task<OpResult> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (c is null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(userId, c.AddressBookId, ct)) return OpResult.Forbidden("No write access to this contact.");
        c.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(c.AddressBookId, c.VcardUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    public async Task<OpResult<DavWriteResult>> PutVcfAsync(
        Guid userId, Guid addressBookId, string vcardUid, string rawVcard, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(userId, addressBookId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this address book.");

        var existing = await db.Contacts.FirstOrDefaultAsync(c => c.AddressBookId == addressBookId && c.VcardUid == vcardUid, ct);
        var live = existing is { DeletedAt: null };
        if (ifNoneMatchStar && live) return OpResult<DavWriteResult>.Conflict("Resource already exists.");
        if (ifMatch is not null && (!live || existing!.ContentHash != ifMatch)) return OpResult<DavWriteResult>.Conflict("ETag mismatch.");

        var p = VCardSerializer.ParseVCard(rawVcard);
        var c = existing ?? new Contact { Id = Guid.NewGuid(), AddressBookId = addressBookId, VcardUid = vcardUid, Metadata = "{}" };
        c.FullName = p.FullName; c.GivenName = p.GivenName; c.FamilyName = p.FamilyName; c.Organization = p.Organization;
        c.Emails = p.Emails is null ? null : JsonSerializer.Serialize(p.Emails);
        c.Phones = p.Phones is null ? null : JsonSerializer.Serialize(p.Phones);
        c.Birthday = p.Birthday;
        c.SourceVcard = rawVcard;
        c.ContentHash = ContentHash.Of(rawVcard);
        c.DeletedAt = null;

        if (existing is null) db.Contacts.Add(c);
        else c.UpdatedAt = DateTimeOffset.UtcNow;

        await BumpAndLogAsync(addressBookId, vcardUid, "saved", c.ContentHash, ct);
        await db.SaveChangesAsync(ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, c.ContentHash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid userId, Guid addressBookId, string vcardUid, string? ifMatch, CancellationToken ct = default)
    {
        var c = await db.Contacts.FirstOrDefaultAsync(x => x.AddressBookId == addressBookId && x.VcardUid == vcardUid && x.DeletedAt == null, ct);
        if (c is null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(userId, addressBookId, ct)) return OpResult.Forbidden("No write access to this address book.");
        if (ifMatch is not null && c.ContentHash != ifMatch) return OpResult.Conflict("ETag mismatch.");

        c.DeletedAt = DateTimeOffset.UtcNow;
        await BumpAndLogAsync(addressBookId, vcardUid, "deleted", null, ct);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
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
}
