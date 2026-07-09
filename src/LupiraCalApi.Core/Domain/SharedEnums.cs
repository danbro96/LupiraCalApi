namespace LupiraCalApi.Domain;

/// <summary>A principal's permission on a collection. <c>Owner</c> adds member-management + delete rights over <c>ReadWrite</c>.</summary>
public enum Access { Owner, ReadWrite, Read }
