using System.Text.Json.Serialization;

namespace LupiraCalApi.Domain;

/// <summary>What an LLM-interpreted run should accomplish.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PromptIntent>))]
public enum PromptIntent { EnrichRecord, Research, CreateFollowUp, Monitor, Summarise, AskUser }

/// <summary>The ProposedAction kind a run is contracted to yield (assistant-api validates against it; cal-api only stores it).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutputKind>))]
public enum OutputKind { RecordEdit, Event, Task, Message, Summary, Question, Relation, None }

/// <summary>Model size tier; the LLM gateway maps it to a concrete alias (Small→qwen3-1.7b, Medium→qwen3-14b, Large→gpt-oss-120b).
/// Vendor-neutral and durable across gateway model swaps.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelTier>))]
public enum ModelTier { Small, Medium, Large }

/// <summary>On a missed contract: retry once then ask (Retry), ask immediately (Ask), or drop (Drop).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FallbackMode>))]
public enum FallbackMode { Retry, Ask, Drop }

/// <summary>A deterministic, no-LLM action executed directly at fire time.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionKind>))]
public enum ActionKind { SendCheckIn, Notify, CreateLinkedTask, ExpireTarget, RescheduleSelf, RunJob, Rescore }

/// <summary>What a <see cref="Ref"/> points at.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RefKind>))]
public enum RefKind { Event, Contact, Task, External }

/// <summary>When a payload fires relative to its item's occurrence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PromptFireKind>))]
public enum PromptFireKind { OnStart, OnEnd, Offset, AllDayAt }

/// <summary>A reference the fired payload acts on. <c>Id</c> for Event/Contact/Task; <c>Url</c> for External.</summary>
public sealed record Ref(RefKind Kind, Guid? Id, string? Url);

/// <summary>Fire timing (flattened union): OnStart/OnEnd carry nothing; Offset uses <c>OffsetMinutes</c> (negative = lead time);
/// AllDayAt uses <c>AllDayAt</c> (local wall-clock time on the occurrence date).</summary>
public sealed record PromptFire(PromptFireKind Kind, int? OffsetMinutes, TimeOnly? AllDayAt);

/// <summary>An LLM-interpreted, contracted payload → an agent run. Declared at authoring time; enforced by assistant-api at fire time.</summary>
public sealed record ItemPrompt(
    PromptIntent Intent,
    Ref? Target,
    string Instruction,
    OutputKind Output,
    string[]? Tools,
    ModelTier? Tier,
    FallbackMode OnMiss,
    PromptFire Fire,
    bool Enabled);

/// <summary>A deterministic payload executed directly (no LLM). <c>ParamsJson</c> carries the frozen params (e.g. a SendCheckIn message).</summary>
public sealed record ItemAction(
    ActionKind Kind,
    Ref? Target,
    string ParamsJson,
    PromptFire Fire,
    bool Enabled);
