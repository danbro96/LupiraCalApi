using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Event-replay behavior of the <see cref="ContactGroup"/> aggregate: creation, rename, soft-delete,
/// and idempotent membership add/remove.</summary>
public class ContactGroupTests
{
    static ContactGroup Created(Guid gid, ContactGroupKind kind = ContactGroupKind.Organization, string name = "Acme")
    {
        var g = new ContactGroup();
        g.Apply(new ContactGroupCreated(gid, Guid.NewGuid(), kind, name, null));
        return g;
    }

    [Fact]
    public void Organization_membership_add_and_remove()
    {
        var gid = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var g = Created(gid);
        Assert.Equal(ContactGroupKind.Organization, g.Kind);

        g.Apply(new ContactAddedToGroup(gid, contact, DateTimeOffset.UtcNow));
        Assert.Contains(contact, g.MemberContactIds);

        g.Apply(new ContactRemovedFromGroup(gid, contact, DateTimeOffset.UtcNow));
        Assert.DoesNotContain(contact, g.MemberContactIds);
    }

    [Fact]
    public void Personal_group_kind_is_preserved()
    {
        var g = Created(Guid.NewGuid(), ContactGroupKind.Group, "Family");
        Assert.Equal(ContactGroupKind.Group, g.Kind);
        Assert.Equal("Family", g.Name);
    }

    [Fact]
    public void Renamed_changes_the_name()
    {
        var gid = Guid.NewGuid();
        var g = Created(gid);
        g.Apply(new ContactGroupRenamed(gid, "Acme Corp"));
        Assert.Equal("Acme Corp", g.Name);
    }

    [Fact]
    public void Deleted_tombstones_the_group()
    {
        var gid = Guid.NewGuid();
        var g = Created(gid);
        g.Apply(new ContactGroupDeleted(gid));
        Assert.NotNull(g.DeletedAt);
    }

    [Fact]
    public void Duplicate_add_is_idempotent()
    {
        var gid = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var g = Created(gid);
        g.Apply(new ContactAddedToGroup(gid, contact, DateTimeOffset.UtcNow));
        g.Apply(new ContactAddedToGroup(gid, contact, DateTimeOffset.UtcNow));
        Assert.Single(g.MemberContactIds);
    }

    [Fact]
    public void Removing_a_non_member_is_a_no_op()
    {
        var gid = Guid.NewGuid();
        var g = Created(gid);
        g.Apply(new ContactRemovedFromGroup(gid, Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Empty(g.MemberContactIds);
    }
}
