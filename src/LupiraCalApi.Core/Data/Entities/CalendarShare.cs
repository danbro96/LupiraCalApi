namespace LupiraCalApi.Data.Entities;

public class CalendarShare
{
    public Guid CalendarId { get; set; }
    public Guid UserId { get; set; }
    public string Access { get; set; } = "read";        // 'read' | 'read-write'
}
