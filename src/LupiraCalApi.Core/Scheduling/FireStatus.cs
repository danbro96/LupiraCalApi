namespace LupiraCalApi.Core.Scheduling;

/// <summary>Lifecycle of a materialized fire in <c>cal.scheduled_fire</c>. The materializer only ever writes <c>Pending</c>;
/// the (separate) dispatcher advances the rest. Stored as lowercase text.</summary>
public enum FireStatus { Pending, Claimed, Done, Failed, Expired }
