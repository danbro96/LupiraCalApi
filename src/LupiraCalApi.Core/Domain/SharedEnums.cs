namespace LupiraCalApi.Domain;

/// <summary>A principal's permission on a collection. <c>Owner</c> adds member-management + delete rights over <c>ReadWrite</c>.</summary>
public enum Access { Owner, ReadWrite, Read }

/// <summary>Level of a node in the hierarchical <see cref="Place"/> tree.</summary>
public enum PlaceKind { Country, City, Address, Venue }
