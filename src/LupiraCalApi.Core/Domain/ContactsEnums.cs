using System.Text.Json.Serialization;

namespace LupiraCalApi.Domain;

/// <summary>A personal grouping (Friends/Family/Colleagues) vs a company/institution. An employer is membership in an <c>Organization</c>-kind group.</summary>
public enum ContactGroupKind { Group, Organization }

/// <summary>vCard <c>ADR</c> TYPE for a contact's postal address.</summary>
public enum ContactAddressType { Home, Work, Other }

/// <summary>Kind of a contact-to-contact edge, aligned with vCard <c>RELATED</c> TYPE values:
/// the related (To) contact's role relative to the owning contact ("To is my Kind").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactRelationKind>))]
public enum ContactRelationKind { Parent, Child, Sibling, Spouse, Partner, Friend, Colleague, Neighbor, Emergency, Other }
