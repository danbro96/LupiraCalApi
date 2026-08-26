using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>How exact a start/end date is. A REST/MCP annotation only — not emitted in ICS and not part of the ETag,
/// so a DAV round-trip leaves it null (DAV is precision-agnostic). Used for historical/backfilled items whose date is
/// known only to the month, year, or roughly: the date is still stored as a concrete day, this records the confidence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DatePrecision>))]
public enum DatePrecision { Exact, Day, Month, Year, Approximate }
