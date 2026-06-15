namespace LupiraCalApi.Data.Entities;

public class Calendar
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Slug { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Color { get; set; }
    public string? DefaultTimezone { get; set; }        // IANA zone, e.g. 'Europe/Stockholm'
    public long Revision { get; set; }                  // bumped on any child change; DAV derives ctag + sync-token from this
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
