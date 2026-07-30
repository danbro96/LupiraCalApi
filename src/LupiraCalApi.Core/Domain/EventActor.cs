using System.Diagnostics;
using LupiraCalApi.Domain.Identity;
using Marten;

namespace LupiraCalApi.Domain;

/// <summary>
/// Stamps event provenance onto the write session before its commit, so every event and document in this unit of
/// work carries it: the acting principal (Marten <c>LastModifiedBy</c>), their email (<c>actor.email</c>), the
/// writing surface (<c>source</c>), and the ambient OTel trace/span as correlation/causation. All unbackfillable,
/// hence stamped on every request.
///
/// Deliberately separate from <c>PrincipalDirectory</c>: resolving an identity must not mutate session state.
/// Stamping inside the lookup misattributed writes whenever a *third party* was resolved — granting calendar
/// access recorded the grantee as the actor, and tagged the write <c>source=dav</c> because a target lookup carries
/// no OIDC sub. Only the caller's own resolution site stamps.
/// </summary>
public static class EventActor
{
    public const string EmailHeaderKey = "actor.email";
    public const string SourceHeaderKey = "source";

    /// <summary>The writing surface. DAV resolves by email only (no OIDC sub).</summary>
    public const string SourceApi = "api";
    public const string SourceDav = "dav";

    public static void Stamp(IDocumentSession session, Principal principal, string source)
    {
        session.LastModifiedBy = principal.Id.ToString();
        session.SetHeader(EmailHeaderKey, principal.Email);
        session.SetHeader(SourceHeaderKey, source);
        if (Activity.Current is { } a)
        {
            session.CorrelationId = a.TraceId.ToString();
            session.CausationId = a.SpanId.ToString();
        }
    }
}
