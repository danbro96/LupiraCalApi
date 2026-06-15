using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Relations;

public record CreateRelationRequest(string ToKind, string ToRef, string RelationType, JsonNode? Metadata);
