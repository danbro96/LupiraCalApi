namespace LupiraCalApi.Domain;

/// <summary>An address book collection (plain document). Access is via <see cref="AddressBookOwner"/>; it contains <see cref="Contact"/>s and <see cref="ContactGroup"/>s.</summary>
public sealed class AddressBook
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string? DisplayName { get; set; }
}
