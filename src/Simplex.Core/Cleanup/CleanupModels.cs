namespace Simplex.Core.Cleanup;

public enum CleanupKind
{
    Files,
    RecycleBin,
}

/// <summary>Ein Ordner, dessen (passende) Inhalte gelöscht werden dürfen. Der Ordner selbst bleibt bestehen.</summary>
public sealed record CleanupTarget(string Directory, string Pattern = "*", bool Recursive = true, double MinAgeHours = 0);

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public bool RequiresAdmin { get; init; }
    public bool DefaultSelected { get; init; } = true;
    public CleanupKind Kind { get; init; } = CleanupKind.Files;
    public IReadOnlyList<CleanupTarget> Targets { get; init; } = Array.Empty<CleanupTarget>();
}

public sealed class CleanupAnalysis
{
    public long Bytes { get; init; }
    public long Files { get; init; }
}

public sealed class CleanupRunResult
{
    public long FreedBytes { get; set; }
    public long DeletedFiles { get; set; }
    public long FailedFiles { get; set; }
}
