using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Relations;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Relation"/> document to its response DTO.</summary>
internal static class RelationMapper
{
    public static RelationDto ToResponse(this Relation r) => new(
        r.Id, r.FromKind, r.FromId, r.ToKind, r.ToRef, r.RelationType,
        string.IsNullOrWhiteSpace(r.Metadata) ? null : JsonNode.Parse(r.Metadata));
}
