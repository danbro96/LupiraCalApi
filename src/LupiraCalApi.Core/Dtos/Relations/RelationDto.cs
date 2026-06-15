using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Relations;

public record RelationDto(Guid Id, string FromKind, Guid FromId, string ToKind, string ToRef, string RelationType, JsonNode? Metadata);
