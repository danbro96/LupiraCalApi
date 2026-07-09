using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class ContactsHandler(CurrentUser user, ContactService contacts)
{
    public async Task<Results<Ok<List<ContactDto>>, ProblemHttpResult, UnauthorizedHttpResult>> QueryAsync(string? query, Guid? addressBookId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.QueryAsync(u.Id, query, addressBookId, ct));
    }

    public async Task<Results<Ok<ContactDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateContactRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.GetAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ReviseAsync(Guid id, ReviseContactRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.ReviseAsync(u.Id, id, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await contacts.DeleteAsync(u.Id, id, ct));
    }
}
