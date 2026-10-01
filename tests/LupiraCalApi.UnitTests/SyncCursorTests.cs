using LupiraCalApi.Core.Application.Sync;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class SyncCursorTests
{
    [Fact]
    public void Round_trips_through_its_string_form()
    {
        var cursor = new SyncCursor(4242, SyncCursor.ScopeOf([Guid.NewGuid()]));
        Assert.True(SyncCursor.TryParse(cursor.ToString(), out var parsed));
        Assert.Equal(cursor, parsed);
    }

    [Fact]
    public void Scope_ignores_order_and_changes_with_membership()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal(SyncCursor.ScopeOf([a, b]), SyncCursor.ScopeOf([b, a]));
        Assert.NotEqual(SyncCursor.ScopeOf([a]), SyncCursor.ScopeOf([a, b]));
        Assert.NotEqual(SyncCursor.ScopeOf([]), SyncCursor.ScopeOf([a]));
    }

    [Fact]
    public void Bare_sequence_parses_with_an_empty_scope()
    {
        Assert.True(SyncCursor.TryParse("17", out var legacy));
        Assert.Equal(new SyncCursor(17, string.Empty), legacy);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData(".scope")]
    [InlineData("1e3.scope")]
    public void Rejects_what_it_never_issued(string value) => Assert.False(SyncCursor.TryParse(value, out _));
}
