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
            Name("Dr", "Jane", "Q", "Smith", "Jr", null), "VCF", "h"));
        Assert.Equal("Dr Jane Q Smith Jr", c.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_to_nickname()
    {
        var c = new Contact();
        c.Apply(new ContactCreated(Guid.NewGuid(), Guid.NewGuid(), "u@x",
            Name(null, null, null, null, null, "Mom"), "VCF", "h"));
        Assert.Equal("Mom", c.DisplayName);
    }

    [Fact]
    public void Deleted_then_restored_clears_the_tombstone()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "VCF", "h"));
        c.Apply(new ContactDeleted(id));
        Assert.NotNull(c.DeletedAt);
        c.Apply(new ContactRestored(id, "VCF2", "h2"));
        Assert.Null(c.DeletedAt);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void VcardPut_resurrects_a_soft_deleted_contact()
    {
        var id = Guid.NewGuid();
        var book = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, book, "u@x", Name(null, "A", null, "B", null, null), "VCF", "h1"));
        c.Apply(new ContactDeleted(id));

        c.Apply(new ContactVcardPut(id, book, "u@x", Name(null, "A", null, "B", null, null), "VCF2", "h2"));
        Assert.Null(c.DeletedAt);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void Revised_updates_the_name_and_hash()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "Bob", null, "Jones", null, null), "VCF", "h1"));
        c.Apply(new ContactRevised(id, Name(null, "Robert", null, "Jones", null, null), "VCF2", "h2"));

        Assert.Equal("Robert Jones", c.DisplayName);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void Addresses_replaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "VCF", "h"));

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
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "VCF", "h"));

        c.Apply(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "mastodon", Handle = "@a" }]));
        c.Apply(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "github", Handle = "b", Url = "https://github.com/b" }]));

        var only = Assert.Single(c.Profiles);
        Assert.Equal("github", only.Service);
        Assert.Equal("https://github.com/b", only.Url);
    }
}
