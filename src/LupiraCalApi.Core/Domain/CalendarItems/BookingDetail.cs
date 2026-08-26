namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A reservation/confirmation attached to any category (a booked meal, a ticketed outing, a hotel, a flight).
/// <c>ProviderContactId</c> reuses a <c>Contact</c> (airline, hotel, restaurant, venue); <c>PartySize</c> covers a
/// table/seat reservation; <c>Amount</c>/<c>Currency</c> the paid cost.</summary>
public sealed record BookingDetail(
    Guid? ProviderContactId, string? ConfirmationNumber, string? Reference, string? Url,
    decimal? Amount, string? Currency, int? PartySize);
