using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class ParticipationEndpoints
{
    public static IEndpointRouteBuilder MapParticipation(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items/{id:guid}/participants").RequireAuthorization("ApiPolicy").WithTags("Participation");

        group.MapPost("/", (Guid id, Guid contactId, string? role, ParticipationHandler h, CancellationToken ct) => h.InviteAsync(id, contactId, role, ct))
            .WithSummary("Invite a contact (must be a Contact id). role = chair|req-participant|opt-participant|non-participant.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{participationId:guid}/respond", (Guid id, Guid participationId, string? status, ParticipationHandler h, CancellationToken ct) => h.RespondAsync(id, participationId, status, ct))
            .WithSummary("Record an RSVP. status = needs-action|accepted|declined|tentative|delegated.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{participationId:guid}/attend", (Guid id, Guid participationId, ParticipationHandler h, CancellationToken ct) => h.ConfirmAsync(id, participationId, ct))
            .WithSummary("Confirm attendance.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{participationId:guid}/leave", (Guid id, Guid participationId, ParticipationHandler h, CancellationToken ct) => h.LeaveAsync(id, participationId, ct))
            .WithSummary("Record that the participant left.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{participationId:guid}", (Guid id, Guid participationId, ParticipationHandler h, CancellationToken ct) => h.RemoveAsync(id, participationId, ct))
            .WithSummary("Remove an attendee.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
