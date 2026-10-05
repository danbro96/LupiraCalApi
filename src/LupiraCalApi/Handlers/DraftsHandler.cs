using System.Text;
using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace LupiraCalApi.Handlers;

public sealed class DraftsHandler(CurrentUser user)
{
    private const int MaxFileBytes = 5 * 1024 * 1024;

    private static readonly string[] FileMediaTypes = ["text/calendar", "text/plain"];

    public async Task<Results<Ok<List<ItemDraftDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ReadAsync(HttpRequest request, string? zone, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !FileMediaTypes.Contains(mediaType.MediaType.Value, StringComparer.OrdinalIgnoreCase))
            return TypedResults.Problem(title: "Unsupported media type", detail: "Send the calendar file as text/calendar or text/plain.",
                statusCode: StatusCodes.Status415UnsupportedMediaType, type: "https://httpstatuses.com/415");
        if (request.ContentLength > MaxFileBytes) return TooLarge();

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes) return TooLarge();
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, mediaType.Encoding ?? Encoding.UTF8);
        return OpResultMap.OkProblem(ItemDraftReader.Read(await reader.ReadToEndAsync(ct), (await user.GetAsync(ct)).Id, zone));
    }

    private static ProblemHttpResult TooLarge() =>
        TypedResults.Problem(title: "Payload too large", detail: $"The calendar file exceeds {MaxFileBytes / (1024 * 1024)} MB.",
            statusCode: StatusCodes.Status413PayloadTooLarge, type: "https://httpstatuses.com/413");
}
