namespace LupiraCalApi.Core.Application.Subscriptions;

/// <summary>A feed host resolved only to addresses <see cref="PrivateAddressGuard"/> refuses.</summary>
public sealed class FeedBlockedException(string host) : Exception($"Feed host {host} resolves only to private or reserved addresses.");
