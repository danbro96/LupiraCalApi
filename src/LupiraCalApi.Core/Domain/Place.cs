namespace LupiraCalApi.Domain;

/// <summary>
/// A node in the hierarchical location catalog (plain document), shared by calendar items and contact addresses.
/// <see cref="Kind"/> is the level; <see cref="ParentPlaceId"/> is the enclosing place (Address → City → Country),
/// so "everything in Stockholm" is a tree query and cities/countries are entered once.
/// </summary>
public sealed class Place
{
    public Guid Id { get; set; }
    public Guid? ParentPlaceId { get; set; }
    public PlaceKind Kind { get; set; }
    public string Name { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
