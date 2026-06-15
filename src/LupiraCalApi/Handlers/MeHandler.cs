using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.Me;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class MeHandler(CurrentUser user)
{
    public async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(new MeDto(u.Id, u.Email, u.DisplayName));
    }
}
