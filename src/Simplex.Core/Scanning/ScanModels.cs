namespace Simplex.Core.Scanning;

public enum PerformanceProfile
{
    /// <summary>Alle Kerne, starke Überbuchung – ideal für NVMe/SSD.</summary>
    Maximum,
    /// <summary>Hälfte der Kerne – PC bleibt flüssig bedienbar.</summary>
    Balanced,
    /// <summary>Zwei Threads mit niedriger Priorität – für HDDs und Hintergrund.</summary>
    Gentle,
}

public sealed class ScanOptions
{
    public required IReadOnlyList<string> Roots { get; init; }
    public long MinFileSize { get; init; } = 100L * 1024 * 1024;
    public bool IncludeHidden { get; init; } = true;
    public int MaxResults { get; init; } = 20_000;
    public PerformanceProfile Profile { get; init; } = PerformanceProfile.Maximum;

    public int ResolveThreadCount() => ThreadsFor(Profile);

    public static int ThreadsFor(PerformanceProfile p)
    {
        int cores = Environment.ProcessorCount;
        return p switch
        {
            PerformanceProfile.Maximum => Math.Clamp(cores * 2, 4, 64),
            PerformanceProfile.Balanced => Math.Clamp(cores / 2, 2, 16),
            _ => 2,
        };
    }
}

/// <summary>Live-Zähler, die während des Scans von allen Threads aktualisiert werden.</summary>
public sealed class ScanProgress
{
    internal long _files, _dirs, _bytes, _errors, _found;
    private volatile string _current = "";

    public long Files => Interlocked.Read(ref _files);
    public long Directories => Interlocked.Read(ref _dirs);
    public long Bytes => Interlocked.Read(ref _bytes);
    public long Errors => Interlocked.Read(ref _errors);
    public long Found => Interlocked.Read(ref _found);
    public string CurrentPath { get => _current; internal set => _current = value; }
}

public sealed class FileEntry
{
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public FileAttributes Attributes { get; init; }
    public FileCategory Category { get; init; }

    public string Name => Path.GetFileName(FullPath);
    public string Directory => Path.GetDirectoryName(FullPath) ?? "";
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
}

/// <summary>Ein Ordner im Größenbaum. Speichert nur seinen Namen; der Pfad wird über die Eltern berechnet (spart RAM).</summary>
public sealed class DirNode
{
    public DirNode(string name, DirNode? parent, int depth, bool hidden)
    {
        Name = name;
        Parent = parent;
        Depth = depth;
        IsHidden = hidden;
    }

    public string Name { get; }
    public DirNode? Parent { get; internal set; }
    public int Depth { get; }
    public bool IsHidden { get; }
    public long OwnSize { get; internal set; }
    public long OwnFiles { get; internal set; }
    public long TotalSize { get; internal set; }
    public long TotalFiles { get; internal set; }
    public long TotalDirs { get; internal set; }
    public List<DirNode>? Children { get; internal set; }
    public bool IsDeleted { get; private set; }

    public string FullPath
    {
        get
        {
            if (Parent == null) return Name;
            var parts = new Stack<string>();
            for (var n = this; n != null; n = n.Parent) parts.Push(n.Name);
            string root = parts.Pop();
            return Path.Join(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, string.Join(Path.DirectorySeparatorChar, parts));
        }
    }

    /// <summary>Entfernt diesen Ordner aus dem Baum und zieht seine Größe bei allen Vorfahren ab.</summary>
    public void RemoveFromTree()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        for (var p = Parent; p != null; p = p.Parent)
        {
            p.TotalSize -= TotalSize;
            p.TotalFiles -= TotalFiles;
            p.TotalDirs -= TotalDirs + 1;
        }
        Parent?.Children?.Remove(this);
    }

    /// <summary>Zieht eine gelöschte Einzeldatei von diesem Ordner und allen Vorfahren ab.</summary>
    public void SubtractFile(long size)
    {
        OwnSize -= size;
        OwnFiles--;
        for (var n = this; n != null; n = n.Parent)
        {
            n.TotalSize -= size;
            n.TotalFiles--;
        }
    }

    public override string ToString() => Name;
}

public sealed class ScanResult
{
    public required ScanOptions Options { get; init; }
    public required List<DirNode> Roots { get; init; }
    public required List<FileEntry> LargeFiles { get; init; }
    public required long[] CategoryBytes { get; init; }
    public required long[] CategoryCounts { get; init; }
    public long TotalFiles { get; init; }
    public long TotalDirectories { get; init; }
    public long TotalBytes { get; init; }
    public long Errors { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Cancelled { get; init; }
    public int Threads { get; init; }
    public DateTime FinishedAt { get; init; } = DateTime.Now;

    /// <summary>Findet den Ordnerknoten zu einem Pfad (für Aktualisierung nach dem Löschen).</summary>
    public DirNode? FindNode(string directoryPath)
    {
        string target = directoryPath.TrimEnd('\\', '/');
        foreach (var root in Roots)
        {
            string rootPath = root.Name.TrimEnd('\\', '/');
            if (!target.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (target.Length == rootPath.Length) return root;
            if (target[rootPath.Length] is not ('\\' or '/')) continue;
            var parts = target[(rootPath.Length + 1)..].Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
            DirNode? cur = root;
            foreach (var part in parts)
            {
                cur = cur?.Children?.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (cur == null) break;
            }
            if (cur != null) return cur;
        }
        return null;
    }
}
