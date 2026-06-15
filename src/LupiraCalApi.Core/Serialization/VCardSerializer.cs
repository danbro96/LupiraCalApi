using System.Globalization;
using System.Text;

namespace LupiraCalApi.Serialization;

/// <summary>Minimal vCard 3.0 writer + line-based parser. Full FolkerKinzel.VCards round-trip
/// (X-property preservation) is a later CardDAV-hardening step.</summary>
public static class VCardSerializer
{
    public static string Build(
        string uid, string fullName, string? given, string? family, string? organization,
        IEnumerable<string>? emails, IEnumerable<string>? phones, DateOnly? birthday)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        sb.Append("UID:").Append(Escape(uid)).Append("\r\n");
        sb.Append("FN:").Append(Escape(fullName)).Append("\r\n");
        sb.Append("N:").Append(Escape(family ?? "")).Append(';').Append(Escape(given ?? "")).Append(";;;\r\n");
        if (!string.IsNullOrWhiteSpace(organization)) sb.Append("ORG:").Append(Escape(organization)).Append("\r\n");
        foreach (var email in emails ?? []) sb.Append("EMAIL:").Append(Escape(email)).Append("\r\n");
        foreach (var phone in phones ?? []) sb.Append("TEL:").Append(Escape(phone)).Append("\r\n");
        if (birthday is { } b) sb.Append("BDAY:").Append(b.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    public static ParsedContact ParseVCard(string raw)
    {
        string? fn = null, org = null, given = null, family = null;
        DateOnly? bday = null;
        var emails = new List<string>();
        var phones = new List<string>();

        foreach (var line in raw.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length == 0 || l[0] == ' ' || l[0] == '\t') continue;   // skip blanks + folded continuations
            var colon = l.IndexOf(':');
            if (colon < 0) continue;
            var prop = l[..colon].Split(';')[0].ToUpperInvariant();
            var val = l[(colon + 1)..];
            switch (prop)
            {
                case "FN": fn = Unescape(val); break;
                case "ORG": org = Unescape(val.Split(';')[0]); break;
                case "N":
                    var parts = val.Split(';');
                    if (parts.Length > 0) family = Unescape(parts[0]);
                    if (parts.Length > 1) given = Unescape(parts[1]);
                    break;
                case "EMAIL": emails.Add(Unescape(val)); break;
                case "TEL": phones.Add(Unescape(val)); break;
                case "BDAY":
                    if (DateOnly.TryParse(val, CultureInfo.InvariantCulture, out var d1)) bday = d1;
                    else if (DateOnly.TryParseExact(val, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) bday = d2;
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(fn)) fn = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ParsedContact(fn ?? "", given, family, org,
            emails.Count > 0 ? [.. emails] : null, phones.Count > 0 ? [.. phones] : null, bday);
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
    static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
