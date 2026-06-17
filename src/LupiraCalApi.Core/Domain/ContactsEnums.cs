namespace LupiraCalApi.Domain;

/// <summary>A personal grouping (Friends/Family/Colleagues) vs a company/institution. An employer is membership in an <c>Organization</c>-kind group.</summary>
public enum ContactGroupKind { Group, Organization }

/// <summary>vCard <c>ADR</c> TYPE for a contact's postal address.</summary>
public enum ContactAddressType { Home, Work, Other }
