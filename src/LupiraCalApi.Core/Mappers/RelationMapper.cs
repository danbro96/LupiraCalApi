using System.Text.Json.Nodes;
using LupiraCalApi.Data.Entities;
using LupiraCalApi.Dtos.Relations;

namespace LupiraCalApi.Mappers;

/// <summary>Maps the <see cref="Relation"/> entity to its response DTO.</summary>
internal static class RelationMapper
{
    public static RelationDto ToResponse(this Relation r) => new(
        r.Id, r.FromKind, r.FromId, r.ToKind, r.ToRef, r.RelationType,
        string.IsNullOrWhiteSpace(r.Metadata) ? null : JsonNode.Parse(r.Metadata));
}
