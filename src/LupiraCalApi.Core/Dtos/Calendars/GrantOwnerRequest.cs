namespace LupiraCalApi.Dtos.Calendars;

/// <summary>Grant a member access to a container, identified by their login <c>Email</c> (provisioned if they have
/// not logged in yet). <c>Access</c> is <c>owner</c> (default), <c>read-write</c>, or <c>read</c> — case-insensitive.</summary>
public record GrantOwnerRequest(string Email, string? Access);
