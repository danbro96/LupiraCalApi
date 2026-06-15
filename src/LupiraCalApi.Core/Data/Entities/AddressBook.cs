namespace LupiraCalApi.Data.Entities;

public class AddressBook
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Slug { get; set; } = null!;
    public string? DisplayName { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
