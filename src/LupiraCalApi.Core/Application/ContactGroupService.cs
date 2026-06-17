using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Contact groups (personal groupings + organizations) and their membership history. Authorized against the owning address book.</summary>
public sealed class ContactGroupService(IDocumentSession session, AccessResolver access)
{
    public async Task<OpResult<ContactGroupDto>> CreateAsync(Guid principalId, Guid addressBookId, string? kind, string name, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult<ContactGroupDto>.Forbidden("No write access to this address book.");
        var id = Guid.NewGuid();
        session.Events.StartStream<ContactGroup>(id, new ContactGroupCreated(id, addressBookId, ParseKind(kind), name, null));
        await session.SaveChangesAsync(ct);
        var g = await session.LoadAsync<ContactGroup>(id, ct);
        return OpResult<ContactGroupDto>.Ok(ToDto(g!));
    }

    public async Task<OpResult<List<ContactGroupDto>>> ListAsync(Guid principalId, Guid addressBookId, CancellationToken ct = default)
    {
        if (!await access.CanReadAddressBookAsync(principalId, addressBookId, ct)) return OpResult<List<ContactGroupDto>>.Forbidden("No access to this address book.");
        var groups = await session.Query<ContactGroup>().Where(g => g.AddressBookId == addressBookId && g.DeletedAt == null).ToListAsync(ct);
        return OpResult<List<ContactGroupDto>>.Ok(groups.Select(ToDto).ToList());
    }

    public Task<OpResult<ContactGroupDto>> RenameAsync(Guid principalId, Guid groupId, string name, CancellationToken ct = default) =>
        MutateAsync(principalId, groupId, new ContactGroupRenamed(groupId, name), ct);

    public Task<OpResult<ContactGroupDto>> AddMemberAsync(Guid principalId, Guid groupId, Guid contactId, CancellationToken ct = default) =>
        MutateAsync(principalId, groupId, new ContactAddedToGroup(groupId, contactId, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<ContactGroupDto>> RemoveMemberAsync(Guid principalId, Guid groupId, Guid contactId, CancellationToken ct = default) =>
        MutateAsync(principalId, groupId, new ContactRemovedFromGroup(groupId, contactId, DateTimeOffset.UtcNow), ct);

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid groupId, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<ContactGroup>(groupId, ct);
        var g = stream.Aggregate;
        if (g is null || g.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, g.AddressBookId, ct)) return OpResult.Forbidden("No write access to this group.");
        stream.AppendOne(new ContactGroupDeleted(groupId));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task<OpResult<ContactGroupDto>> MutateAsync(Guid principalId, Guid groupId, object @event, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<ContactGroup>(groupId, ct);
        var g = stream.Aggregate;
        if (g is null || g.DeletedAt is not null) return OpResult<ContactGroupDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, g.AddressBookId, ct)) return OpResult<ContactGroupDto>.Forbidden("No write access to this group.");
        stream.AppendOne(@event);
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<ContactGroup>(groupId, ct);
        return OpResult<ContactGroupDto>.Ok(ToDto(updated!));
    }

    private static ContactGroupDto ToDto(ContactGroup g) => new(g.Id, g.AddressBookId, g.Kind.ToString(), g.Name, g.MemberContactIds);
    private static ContactGroupKind ParseKind(string? s) => Enum.TryParse<ContactGroupKind>(s, true, out var v) ? v : ContactGroupKind.Group;
}
