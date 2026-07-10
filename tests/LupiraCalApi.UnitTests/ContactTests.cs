using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Event-replay behavior of the <see cref="Contact"/> aggregate: composed display name, soft-delete +
/// resurrection, structured revision, and wholesale replacement of postal addresses / social profiles.</summary>
public class ContactTests
{
    static ContactFields Name(string? prefix, string? given, string? middle, string? family, string? suffix, string? nickname) =>
        new(prefix, given, middle, family, suffix, nickname, null, null, null, null);

    [Fact]
    public void DisplayName_is_composed_from_name_parts()
    {
        var c = new Contact();
        c.Apply(new ContactCreated(Guid.NewGuid(), Guid.NewGuid(), "u@x",
            Name("Dr", "Jane", "Q", "Smith", "Jr", null), "h"));
        Assert.Equal("Dr Jane Q Smith Jr", c.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_to_nickname()
    {
        var c = new Contact();
        c.Apply(new ContactCreated(Guid.NewGuid(), Guid.NewGuid(), "u@x",
            Name(null, null, null, null, null, "Mom"), "h"));
        Assert.Equal("Mom", c.DisplayName);
    }

    [Fact]
    public void Deleted_then_restored_clears_the_tombstone()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h"));
        c.Apply(new ContactDeleted(id));
        Assert.NotNull(c.DeletedAt);
        c.Apply(new ContactRestored(id, "h2"));
        Assert.Null(c.DeletedAt);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void VcardPut_resurrects_a_soft_deleted_contact()
    {
        var id = Guid.NewGuid();
        var book = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, book, "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactDeleted(id));

        c.Apply(new ContactImported(id, book, "u@x", Name(null, "A", null, "B", null, null), "h2"));
        Assert.Null(c.DeletedAt);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void Revised_updates_the_name_and_hash()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "Bob", null, "Jones", null, null), "h1"));
        c.Apply(new ContactRevised(id, Name(null, "Robert", null, "Jones", null, null), "h2"));

        Assert.Equal("Robert Jones", c.DisplayName);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void Addresses_replaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h"));

        c.Apply(new ContactAddressesReplaced(id, [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home }]));
        Assert.Single(c.Addresses);

        var work = Guid.NewGuid();
        c.Apply(new ContactAddressesReplaced(id, [new ContactPostalAddress { PlaceId = work, Type = ContactAddressType.Work }]));
        var only = Assert.Single(c.Addresses);            // replaced, not appended
        Assert.Equal(work, only.PlaceId);
        Assert.Equal(ContactAddressType.Work, only.Type);
    }

    [Fact]
    public void Profiles_replaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h"));

        c.Apply(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "mastodon", Handle = "@a" }]));
        c.Apply(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "github", Handle = "b", Url = "https://github.com/b" }]));

        var only = Assert.Single(c.Profiles);
        Assert.Equal("github", only.Service);
        Assert.Equal("https://github.com/b", only.Url);
    }

    [Fact]
    public void RelationAdded_appends_the_edge_and_updates_the_hash()
    {
        var id = Guid.NewGuid();
        var dad = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactRelationAdded(id, dad, ContactRelationKind.Parent, "dad", "h2"));

        var edge = Assert.Single(c.Relations);
        Assert.Equal(dad, edge.ToContactId);
        Assert.Equal(ContactRelationKind.Parent, edge.Kind);
        Assert.Equal("dad", edge.Label);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void RelationAdded_upserts_on_target_and_kind_but_keeps_other_kinds()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Parent, "dad", "h2"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Friend, null, "h3"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Parent, "father", "h4"));

        Assert.Equal(2, c.Relations.Count);
        Assert.Equal("father", c.Relations.Single(r => r.Kind == ContactRelationKind.Parent).Label);
        Assert.Null(c.Relations.Single(r => r.Kind == ContactRelationKind.Friend).Label);
        Assert.Equal("h4", c.ContentHash);
    }

    [Fact]
    public void RelationRemoved_deletes_only_the_matching_kind()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Friend, null, "h2"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Colleague, null, "h3"));

        c.Apply(new ContactRelationRemoved(id, other, ContactRelationKind.Friend, "h4"));

        var edge = Assert.Single(c.Relations);
        Assert.Equal(ContactRelationKind.Colleague, edge.Kind);
        Assert.Equal("h4", c.ContentHash);
    }

    [Fact]
    public void RelationsReplaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactRelationAdded(id, Guid.NewGuid(), ContactRelationKind.Parent, "dad", "h2"));

        var sis = Guid.NewGuid();
        c.Apply(new ContactRelationsReplaced(id, [new ContactRelation { ToContactId = sis, Kind = ContactRelationKind.Sibling }]));

        var edge = Assert.Single(c.Relations);
        Assert.Equal(sis, edge.ToContactId);
        Assert.Equal(ContactRelationKind.Sibling, edge.Kind);
    }

    [Theory]
    [InlineData(ContactRelationKind.Parent, ContactRelationKind.Child)]
    [InlineData(ContactRelationKind.Child, ContactRelationKind.Parent)]
    [InlineData(ContactRelationKind.Emergency, ContactRelationKind.Other)]   // no true inverse
    [InlineData(ContactRelationKind.Sibling, ContactRelationKind.Sibling)]
    [InlineData(ContactRelationKind.Spouse, ContactRelationKind.Spouse)]
    [InlineData(ContactRelationKind.Partner, ContactRelationKind.Partner)]
    [InlineData(ContactRelationKind.Friend, ContactRelationKind.Friend)]
    [InlineData(ContactRelationKind.Colleague, ContactRelationKind.Colleague)]
    [InlineData(ContactRelationKind.Neighbor, ContactRelationKind.Neighbor)]
    [InlineData(ContactRelationKind.Other, ContactRelationKind.Other)]
    public void Relation_kind_inverse_matrix(ContactRelationKind kind, ContactRelationKind expected) =>
        Assert.Equal(expected, kind.Inverse());
}
