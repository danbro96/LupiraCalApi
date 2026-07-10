using LupiraCalApi.Domain;
using LupiraCalApi.Serialization;
using Xunit;

namespace LupiraCalApi.UnitTests;

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

    [Fact]
    public void Build_emits_related_with_type_label_and_urn_uuid()
    {
        var dad = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
            [new ContactRelation { ToContactId = dad, Kind = ContactRelationKind.Parent, Label = "dad" }]);

        Assert.Contains($"RELATED;TYPE=parent;X-LUPIRA-LABEL=dad:urn:uuid:{dad:D}\r\n", vcf);
    }

    [Fact]
    public void Related_round_trips_kind_target_and_label()
    {
        var dad = Guid.NewGuid();
        var friend = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
        [
            new ContactRelation { ToContactId = dad, Kind = ContactRelationKind.Parent, Label = "dad" },
            new ContactRelation { ToContactId = friend, Kind = ContactRelationKind.Friend },
        ]);

        var p = VCardSerializer.ParseVCard(vcf);

        Assert.Equal(2, p.Relations!.Length);
        Assert.Equal((dad, ContactRelationKind.Parent, "dad"), (p.Relations[0].ToContactId, p.Relations[0].Kind, p.Relations[0].Label));
        Assert.Equal((friend, ContactRelationKind.Friend, null), (p.Relations[1].ToContactId, p.Relations[1].Kind, p.Relations[1].Label));
    }

    [Theory]
    [InlineData("RELATED;TYPE=parent:https://example.com/x")]      // URL target — not ours
    [InlineData("RELATED;TYPE=parent:urn:uuid:not-a-guid")]
    [InlineData("RELATED;TYPE=parent:free text")]
    public void Related_with_non_urn_uuid_value_is_skipped(string line)
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\n{line}\r\nEND:VCARD\r\n");
        Assert.Null(p.Relations);
    }

    [Theory]
    [InlineData("co-worker", ContactRelationKind.Colleague)]
    [InlineData("sweetheart", ContactRelationKind.Partner)]
    [InlineData("kin", ContactRelationKind.Other)]
    [InlineData("muse", ContactRelationKind.Other)]
    [InlineData("CHILD", ContactRelationKind.Child)]   // case-insensitive enum name
    public void Related_type_synonyms_and_unknowns_map(string type, ContactRelationKind expected)
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nRELATED;TYPE={type}:urn:uuid:{Guid.NewGuid():D}\r\nEND:VCARD\r\n");
        Assert.Equal(expected, Assert.Single(p.Relations!).Kind);
    }

    [Fact]
    public void Related_without_type_defaults_to_other()
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nRELATED:urn:uuid:{Guid.NewGuid():D}\r\nEND:VCARD\r\n");
        Assert.Equal(ContactRelationKind.Other, Assert.Single(p.Relations!).Kind);
    }

    [Fact]
    public void Unsafe_label_is_dropped_from_the_param_but_the_line_still_emits()
    {
        var target = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
            [new ContactRelation { ToContactId = target, Kind = ContactRelationKind.Friend, Label = "a;b:c" }]);

        Assert.Contains($"RELATED;TYPE=friend:urn:uuid:{target:D}\r\n", vcf);
        Assert.DoesNotContain("X-LUPIRA-LABEL", vcf);
    }

    [Fact]
    public void Build_without_relations_is_byte_identical_to_the_pre_relations_form()
    {
        // Guards existing stored ETags: contacts without relations must keep hashing to the same canonical bytes.
        var before = VCardSerializer.Build("uid@x", "Jane Smith", "Jane", "Smith", null, ["jane@x.com"], null, new DateOnly(1990, 2, 15));
        var after = VCardSerializer.Build("uid@x", "Jane Smith", "Jane", "Smith", null, ["jane@x.com"], null, new DateOnly(1990, 2, 15), []);
        Assert.Equal(before, after);
        Assert.DoesNotContain("RELATED", before);
    }
}
