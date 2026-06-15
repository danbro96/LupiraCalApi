namespace LupiraCalApi.Data.Entities;

public class AddressBookShare
{
    public Guid AddressBookId { get; set; }
    public Guid UserId { get; set; }
    public string Access { get; set; } = "read";
}
