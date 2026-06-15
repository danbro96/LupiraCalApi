namespace LupiraCalApi.Application;

/// <summary>Outcome of a CalDAV/CardDAV object PUT: whether the resource was created (→201) vs updated (→204), plus its new ETag.</summary>
public readonly record struct DavWriteResult(bool Created, string Etag);
