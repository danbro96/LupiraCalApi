using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>A DAV-only member (never touches REST) is bootstrapped with the standard container set on first contact,
/// so DAVx5 discovers the agenda calendars and the personal address book straight after account setup.</summary>
public sealed class DavSelfProvisionTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "dave@x.test";

    [Fact]
    public async Task First_dav_contact_seeds_the_standard_set_idempotently()
    {
        var dav = Factory.DavClient(Email);

        // Discovery exactly as a DAV client does it: root → current-user-principal → calendar home.
        var root = await ReadXml(await SendDav(dav, "PROPFIND", "/dav/", depth: "0"));
        var principalHref = root.Descendants(D + "current-user-principal").Descendants(D + "href").Single().Value;
        var uid = Guid.Parse(principalHref.TrimEnd('/').Split('/')[^1]);

        var cal = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/", depth: "1"));
        var calHrefs = cal.Descendants(D + "href").Select(h => h.Value).Where(h => !h.EndsWith("/cal/")).ToList();
        Assert.Equal(4, calHrefs.Count);   // agenda set only; the 4 system calendars stay hidden
        var names = cal.Descendants(D + "displayname").Select(n => n.Value).ToList();
        foreach (var name in new[] { "Personal", "Group", "Birthdays", "Availability" })
            Assert.Contains(name, names);

        var card = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/card/", depth: "1"));
        Assert.Single(card.Descendants(CR + "addressbook"));

        // Second contact must not reseed (idempotent on Kind).
        var again = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/", depth: "1"));
        Assert.Equal(4, again.Descendants(D + "href").Count(h => !h.Value.EndsWith("/cal/")));
    }
}
