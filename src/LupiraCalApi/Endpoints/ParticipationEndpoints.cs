using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace LupiraCalApi.Endpoints;

public static class ParticipationEndpoints
{
    public static IEndpointRouteBuilder MapParticipation(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items/{id:guid}/participants").RequireAuthorization("ApiPolicy").WithTags("Participation");

        group.MapPost("/", (Guid id, Guid contactId, string? role, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ParticipationHandler h, CancellationToken ct) => h.InviteAsync(id, contactId, role, occurredAt, idempotencyKey, ct))
            .WithName("InviteParticipant")
            .WithSummary("Invite a contact (must be a Contact id). role = chair|req-participant|opt-participant|non-participant (or the enum name); default req-participant, anything else 400s. A contact already invited is a no-op. " + ReplaySafe)
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/", (Guid id, SetParticipantsRequest body, ParticipationHandler h, CancellationToken ct) => h.SetParticipantsAsync(id, body, ct))
            .WithName("SetParticipants")
            .WithSummary("Add a set of contacts as attendees in one call (add-only). Attended=true also marks them attended (historical backfill). Slim result (additions + already-present count), not the full item.")
            .Produces<SetParticipantsResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{participationId:guid}/respond", (Guid id, Guid participationId, string? status, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ParticipationHandler h, CancellationToken ct) => h.RespondAsync(id, participationId, status, occurredAt, idempotencyKey, ct))
            .WithName("RespondToInvitation")
            .WithSummary("Record an RSVP. status = needs-action|accepted|declined|tentative|delegated (or the enum name); required, anything else 400s. The current RSVP again is a no-op. " + ReplaySafe)
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{participationId:guid}/attend", (Guid id, Guid participationId, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ParticipationHandler h, CancellationToken ct) => h.ConfirmAsync(id, participationId, occurredAt, idempotencyKey, ct))
            .WithName("ConfirmAttendance")
            .WithSummary("Confirm attendance; already confirmed is a no-op. " + ReplaySafe)
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{participationId:guid}/leave", (Guid id, Guid participationId, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ParticipationHandler h, CancellationToken ct) => h.LeaveAsync(id, participationId, occurredAt, idempotencyKey, ct))
            .WithName("LeaveItem")
            .WithSummary("Record that the participant left; already left is a no-op. " + ReplaySafe)
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{participationId:guid}", (Guid id, Guid participationId, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ParticipationHandler h, CancellationToken ct) => h.RemoveAsync(id, participationId, idempotencyKey, ct))
            .WithName("RemoveParticipant")
            .WithSummary("Remove an attendee. Pass Idempotency-Key so a redelivered removal succeeds rather than 404s.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/", (Guid id, Guid contactId, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ParticipationHandler h, CancellationToken ct) => h.RemoveContactAsync(id, contactId, idempotencyKey, ct))
            .WithName("RemoveParticipantByContact")
            .WithSummary("Remove a contact from the item — every participation it holds; a contact holding none is a no-op. For clients that never learned the participation id.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/participation/summary", (DateTimeOffset? from, DateTimeOffset? to, ParticipationHandler h, CancellationToken ct) => h.SummaryAsync(from, to, ct))
            .RequireAuthorization("ApiPolicy").WithTags("Participation")
            .WithName("GetParticipationSummary")
            .WithSummary("Per-contact participation across your readable calendars (contactId, item count, most recent occurrence start), ordered most-interacted first. Optional from/to restricts the window. A ranking signal for contact pickers/resolvers.")
            .Produces<List<ParticipationSummaryEntry>>(StatusCodes.Status200OK);

        return app;
    }

    private const string ReplaySafe = "Offline clients pass ?occurredAt= + Idempotency-Key for replay-safe delivery.";
}
