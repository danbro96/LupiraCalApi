using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class ParticipationEndpoints
{
    public static IEndpointRouteBuilder MapParticipation(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items/{id:guid}/participants").RequireAuthorization("ApiPolicy").WithTags("Participation");

        group.MapPost("/", (Guid id, Guid contactId, string? role, ParticipationHandler h, CancellationToken ct) => h.InviteAsync(id, contactId, role, ct))
            .WithName("InviteParticipant")
            .WithSummary("Invite a contact (must be a Contact id). role = chair|req-participant|opt-participant|non-participant.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPut("/", (Guid id, SetParticipantsRequest body, ParticipationHandler h, CancellationToken ct) => h.SetParticipantsAsync(id, body, ct))
            .WithName("SetParticipants")
            .WithSummary("Add a set of contacts as attendees in one call (add-only). Attended=true also marks them attended (historical backfill). Slim result (additions + already-present count), not the full item.")
            .Produces<SetParticipantsResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{participationId:guid}/respond", (Guid id, Guid participationId, string? status, ParticipationHandler h, CancellationToken ct) => h.RespondAsync(id, participationId, status, ct))
            .WithName("RespondToInvitation")
            .WithSummary("Record an RSVP. status = needs-action|accepted|declined|tentative|delegated.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{participationId:guid}/attend", (Guid id, Guid participationId, ParticipationHandler h, CancellationToken ct) => h.ConfirmAsync(id, participationId, ct))
            .WithName("ConfirmAttendance")
            .WithSummary("Confirm attendance.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{participationId:guid}/leave", (Guid id, Guid participationId, ParticipationHandler h, CancellationToken ct) => h.LeaveAsync(id, participationId, ct))
            .WithName("LeaveItem")
            .WithSummary("Record that the participant left.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{participationId:guid}", (Guid id, Guid participationId, ParticipationHandler h, CancellationToken ct) => h.RemoveAsync(id, participationId, ct))
            .WithName("RemoveParticipant")
            .WithSummary("Remove an attendee.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
