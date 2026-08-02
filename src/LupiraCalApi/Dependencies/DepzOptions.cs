namespace LupiraCalApi.Dependencies;

/// <summary>Binds <c>Depz</c> — the non-gating dependency probe (/depz). Blank
/// <see cref="ProbeKey"/> = feature off.</summary>
public sealed class DepzOptions
{
    public const string SectionName = "Depz";

    public string ProbeKey { get; set; } = "";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool Enabled => !string.IsNullOrWhiteSpace(ProbeKey);
}
