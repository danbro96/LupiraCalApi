using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class ParticipationTokensTests
{
    [Theory]
    [InlineData("chair", ParticipationRole.Chair)]
    [InlineData("req-participant", ParticipationRole.RequiredParticipant)]
    [InlineData("OPT-PARTICIPANT", ParticipationRole.OptionalParticipant)]
    [InlineData("non-participant", ParticipationRole.NonParticipant)]
    [InlineData("OptionalParticipant", ParticipationRole.OptionalParticipant)]
    [InlineData(" nonparticipant ", ParticipationRole.NonParticipant)]
    public void Reads_ical_tokens_and_enum_names_as_roles(string raw, ParticipationRole expected)
    {
        Assert.True(ParticipationTokens.TryParseRole(raw, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData("needs-action", ParticipationStatus.NeedsAction)]
    [InlineData("ACCEPTED", ParticipationStatus.Accepted)]
    [InlineData("Tentative", ParticipationStatus.Tentative)]
    [InlineData("NeedsAction", ParticipationStatus.NeedsAction)]
    public void Reads_ical_tokens_and_enum_names_as_statuses(string raw, ParticipationStatus expected)
    {
        Assert.True(ParticipationTokens.TryParseStatus(raw, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("optional")]
    [InlineData("99")]
    public void Rejects_anything_else(string? raw)
    {
        Assert.False(ParticipationTokens.TryParseRole(raw, out _));
        Assert.False(ParticipationTokens.TryParseStatus(raw, out _));
    }
}
