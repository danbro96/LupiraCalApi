using LupiraCalApi.Serialization;
using System.Text.Json;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class UtcDateTimeOffsetConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeOffsetConverter() } };

    [Fact]
    public void Offset_values_serialize_as_utc_z()
    {
        var value = DateTimeOffset.Parse("2022-04-18T16:50:00+02:00");
        Assert.Equal("\"2022-04-18T14:50:00Z\"", JsonSerializer.Serialize(value, Options));
    }

    [Fact]
    public void Utc_values_stay_z_and_keep_fractions()
    {
        var value = DateTimeOffset.Parse("2022-04-18T14:50:00.1234567+00:00");
        Assert.Equal("\"2022-04-18T14:50:00.1234567Z\"", JsonSerializer.Serialize(value, Options));
    }

    [Fact]
    public void Nullable_and_read_round_trip()
    {
        Assert.Equal("null", JsonSerializer.Serialize((DateTimeOffset?)null, Options));
        var read = JsonSerializer.Deserialize<DateTimeOffset>("\"2022-04-18T16:50:00+02:00\"", Options);
        Assert.Equal(DateTimeOffset.Parse("2022-04-18T14:50:00Z"), read);   // same instant; offsets accepted on read
    }
}
