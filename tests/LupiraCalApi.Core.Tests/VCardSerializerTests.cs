using LupiraCalApi.Serialization;
using Xunit;

namespace LupiraCalApi.Core.Tests;

/// <summary>vCard 3.0 build + line-based parse: round-trip fidelity, escape/unescape ordering, FN fallback,
/// the two BDAY formats, multi-valued EMAIL/TEL, ORG segmentation, and folded-line skipping.</summary>
public class VCardSerializerTests
{
    [Fact]
    public void Build_then_parse_preserves_the_core_fields()
    {
        var vcf = VCardSerializer.Build("uid@x", "Jane Smith", "Jane", "Smith", "Acme",
            ["jane@x.com", "j@y.com"], ["+4612345"], new DateOnly(1990, 2, 15));

        var p = VCardSerializer.ParseVCard(vcf);

        Assert.Equal("Jane Smith", p.FullName);
        Assert.Equal("Jane", p.GivenName);
        Assert.Equal("Smith", p.FamilyName);
        Assert.Equal("Acme", p.Organization);
        Assert.Equal(["jane@x.com", "j@y.com"], p.Emails!);
        Assert.Equal(["+4612345"], p.Phones!);
        Assert.Equal(new DateOnly(1990, 2, 15), p.Birthday);
    }

    [Theory]
    [InlineData("a\\b;c")]      // backslash + semicolon — pins the unescape ordering (\\ unescaped last)
    [InlineData("Doe, John")]   // comma
    [InlineData("Line1\nLine2")] // newline (escaped to \n on the wire, restored on parse)
    [InlineData("Plain Text")]
    public void Special_characters_survive_a_round_trip_in_the_full_name(string value)
    {
        var vcf = VCardSerializer.Build("uid@x", value, null, null, null, null, null, null);
        Assert.Equal(value, VCardSerializer.ParseVCard(vcf).FullName);
    }

    [Fact]
    public void N_property_maps_family_then_given()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nN:Smith;Jane;;;\r\nEND:VCARD\r\n");
        Assert.Equal("Smith", p.FamilyName);
        Assert.Equal("Jane", p.GivenName);
    }

    [Fact]
    public void Missing_FN_is_composed_from_the_name_parts()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nN:Smith;Jane;;;\r\nEND:VCARD\r\n");
        Assert.Equal("Jane Smith", p.FullName);
    }

    [Theory]
    [InlineData("19900215")]      // vCard 3.0 basic date
    [InlineData("1990-02-15")]    // ISO extended date
    public void Birthday_parses_both_formats(string bday)
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nBDAY:{bday}\r\nEND:VCARD\r\n");
        Assert.Equal(new DateOnly(1990, 2, 15), p.Birthday);
    }

    [Fact]
    public void No_emails_or_phones_parse_as_null_lists()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nEND:VCARD\r\n");
        Assert.Null(p.Emails);
        Assert.Null(p.Phones);
    }

    [Fact]
    public void Org_keeps_only_the_first_segment()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nORG:Acme;Sales\r\nEND:VCARD\r\n");
        Assert.Equal("Acme", p.Organization);
    }

    [Fact]
    public void Folded_continuation_lines_are_skipped()
    {
        // The line starting with a space is an RFC 6350 fold; it must not derail parsing.
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:John Doe\r\n  folded-noise\r\nEND:VCARD\r\n");
        Assert.Equal("John Doe", p.FullName);
    }
}
