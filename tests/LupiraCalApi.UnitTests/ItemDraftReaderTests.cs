using Lupira.Primitives;
using Lupira.Results;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class ItemDraftReaderTests
{
    private static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private const string Head = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n";

    [Fact]
    public void Drafts_carry_the_event_identity_and_an_iana_zone()
    {
        const string file = Head + "BEGIN:VEVENT\r\nUID: k@x \r\nDTSTART;TZID=W. Europe Standard Time:20260701T090000\r\nSUMMARY:Möte\r\n" +
            "STATUS:TENTATIVE\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var r = ItemDraftReader.Read(file, Alice);

        var d = Assert.Single(r.Value!);
        Assert.Equal("import-" + ContentHash.Of($"{Alice}\nk@x"), d.SourceKey);
        Assert.Equal("Möte", d.Title);
        Assert.Equal(ItemStatus.Tentative, d.Status);
        Assert.Equal("Europe/Berlin", d.StartTimezone);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero), d.StartsAt);
    }

    [Fact]
    public void An_event_without_identity_gets_a_stable_key()
    {
        const string file = Head + "BEGIN:VEVENT\r\nDTSTART:20260701T090000Z\r\nSUMMARY:Bare\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var first = Assert.Single(ItemDraftReader.Read(file, Alice).Value!).SourceKey;

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, Assert.Single(ItemDraftReader.Read(file, Alice).Value!).SourceKey);
    }

    [Fact]
    public void Floating_times_read_in_the_given_zone_which_becomes_the_start_zone()
    {
        const string file = Head + "BEGIN:VEVENT\r\nUID:f@x\r\nDTSTART:20260701T100000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var d = Assert.Single(ItemDraftReader.Read(file, Alice, "Europe/Stockholm").Value!);

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), d.StartsAt);
        Assert.Equal("Europe/Stockholm", d.StartTimezone);
    }

    [Fact]
    public void Unknown_zone_is_invalid() =>
        Assert.Equal(OpStatus.Invalid, ItemDraftReader.Read(Head + "END:VCALENDAR\r\n", Alice, "Mars/Olympus").Status);

    [Fact]
    public void Each_caller_gets_their_own_key_for_the_same_event()
    {
        const string file = Head + "BEGIN:VEVENT\r\nUID:k@x\r\nDTSTART:20260701T090000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        Assert.NotEqual(Assert.Single(ItemDraftReader.Read(file, Alice).Value!).SourceKey, Assert.Single(ItemDraftReader.Read(file, Bob).Value!).SourceKey);
    }

    [Fact]
    public void Unreadable_file_is_invalid() =>
        Assert.Equal(OpStatus.Invalid, ItemDraftReader.Read("not a calendar", Alice).Status);
}
