using Lupira.Results;
using LupiraCalApi.Dav;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class DavStatusTests
{
    [Theory]
    [InlineData(OpStatus.Ok, StatusCodes.Status204NoContent)]
    [InlineData(OpStatus.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(OpStatus.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(OpStatus.Conflict, StatusCodes.Status412PreconditionFailed)]
    [InlineData(OpStatus.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(OpStatus.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public void Maps_each_outcome_to_its_status_code(OpStatus status, int expected)
    {
        Assert.Equal(expected, DavBackendHandler.DavStatus(status));
    }
}
