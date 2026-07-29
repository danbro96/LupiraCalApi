using System.Text.Json.Serialization;
using System.Text.Json;

namespace LupiraCalApi.Serialization;

/// <summary>
/// Canonicalizes every <see cref="DateTimeOffset"/> on the HTTP contract to UTC ("Z" form). Stored values
/// keep whatever offset they were written with (notably the imported history's +02:00) — but the wire must
/// be deterministic: one instant, one representation. Reading stays liberal (any RFC 3339 offset).
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);   // DateTime with Kind=Utc serializes with the Z suffix
}
