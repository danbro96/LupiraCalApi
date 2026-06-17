namespace LupiraCalApi.Domain;

/// <summary>A principal's access grant on an address book (plain membership document; multi-owner). Composite identity (book:principal).</summary>
public sealed class AddressBookOwner
{
    public string Id { get; set; } = "";
    public Guid AddressBookId { get; set; }
    public Guid PrincipalId { get; set; }
    public Access Access { get; set; }

    public static string MakeId(Guid addressBookId, Guid principalId) => $"{addressBookId:N}:{principalId:N}";
}
