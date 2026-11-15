using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;

namespace Simplex.Core.Scanning;

/// <summary>
/// Hochparalleler Dateisystem-Scanner.
/// Jeder Ordner ist ein Arbeitspaket auf einem gemeinsamen Stapel (LIFO = cache-freundlich, wenig RAM).
/// Beliebig viele Worker-Threads ziehen Pakete, lesen den Ordner mit großen Puffern über
/// NtQueryDirectoryFile (FileSystemEnumerator) und legen Unterordner wieder auf den Stapel.
/// Pro Datei wird nichts allokiert, außer sie ist groß genug für die Ergebnisliste.
/// </summary>
public static class FileScanner
{
    // Cloud-Platzhalter (OneDrive "Nur online") belegen keinen lokalen Platz.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes CloudOnlyMask = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    private static readonly EnumerationOptions s_enumOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024,
        MatchType = MatchType.Simple,
    };

    public static Task<ScanResult> RunAsync(ScanOptions options, ScanProgress progress, CancellationToken ct) =>
        Task.Factory.StartNew(() => Run(options, progress, ct), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private sealed class Shared
    {
        public readonly ConcurrentStack<WorkItem> Stack = new();
        public int Pending;
    }

    private readonly record struct WorkItem(DirNode Node, string Path);

    private sealed class WorkerState
    {
        public readonly List<FileEntry> Found = new();
        public readonly List<DirNode> Nodes = new(1024);
        public readonly long[] CatBytes = new long[FileCategories.Count];
        public readonly long[] CatCounts = new long[FileCategories.Count];
    }

    public static ScanResult Run(ScanOptions options, ScanProgress progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var shared = new Shared();
        var roots = new List<DirNode>();

        foreach (var r in options.Roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string root;
            try { root = Path.GetFullPath(r); } catch { continue; }
            if (!Directory.Exists(root)) continue;
            var node = new DirNode(root, null, 0, false);
            roots.Add(node);
            shared.Pending++;
            shared.Stack.Push(new WorkItem(node, root));
        }

        int threadCount = options.ResolveThreadCount();
        var states = new WorkerState[threadCount];
        var threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            var state = states[i] = new WorkerState();
            threads[i] = new Thread(() => Worker(options, progress, shared, state, ct))
            {
                IsBackground = true,
                Name = "Simplex-Scan-" + i,
                Priority = options.Profile == PerformanceProfile.Gentle ? ThreadPriority.BelowNormal : ThreadPriority.Normal,
            };
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();

        // --- Ergebnisse zusammenführen ---
        var allNodes = new List<DirNode>(states.Sum(s => s.Nodes.Count));
        var found = new List<FileEntry>(states.Sum(s => s.Found.Count));
        var catBytes = new long[FileCategories.Count];
        var catCounts = new long[FileCategories.Count];
        foreach (var s in states)
        {
            allNodes.AddRange(s.Nodes);
            found.AddRange(s.Found);
            for (int c = 0; c < FileCategories.Count; c++)
            {
                catBytes[c] += s.CatBytes[c];
                catCounts[c] += s.CatCounts[c];
            }
        }

        // Ordnergrößen von unten nach oben aufsummieren (tiefste Ordner zuerst).
        allNodes.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
        foreach (var n in allNodes)
        {
            n.TotalSize += n.OwnSize;
            n.TotalFiles += n.OwnFiles;
            if (n.Parent is { } p)
            {
                p.TotalSize += n.TotalSize;
                p.TotalFiles += n.TotalFiles;
                p.TotalDirs += n.TotalDirs + 1;
            }
        }
        foreach (var n in allNodes)
            n.Children?.Sort(static (a, b) => b.TotalSize.CompareTo(a.TotalSize));

        found.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        if (found.Count > options.MaxResults)
            found.RemoveRange(options.MaxResults, found.Count - options.MaxResults);

        sw.Stop();
        return new ScanResult
        {
            Options = options,
            Roots = roots,
            LargeFiles = found,
            CategoryBytes = catBytes,
            CategoryCounts = catCounts,
            TotalFiles = progress.Files,
            TotalDirectories = progress.Directories,
            TotalBytes = progress.Bytes,
            Errors = progress.Errors,
            Duration = sw.Elapsed,
            Cancelled = ct.IsCancellationRequested,
            Threads = threadCount,
        };
    }

    private static void Worker(ScanOptions options, ScanProgress progress, Shared shared, WorkerState state, CancellationToken ct)
    {
        var spin = new SpinWait();
        while (!ct.IsCancellationRequested)
        {
            if (shared.Stack.TryPop(out var item))
            {
                spin.Reset();
                try
                {
                    ProcessDirectory(item, options, progress, shared, state);
                }
                catch
                {
                    Interlocked.Increment(ref progress._errors);
                }
                finally
                {
                    Interlocked.Decrement(ref shared.Pending);
                }
            }
            else
            {
                if (Volatile.Read(ref shared.Pending) == 0) break;
                spin.SpinOnce(sleep1Threshold: 30);
            }
        }
    }

    private static void ProcessDirectory(WorkItem item, ScanOptions options, ScanProgress progress, Shared shared, WorkerState state)
    {
        state.Nodes.Add(item.Node);
        progress.CurrentPath = item.Path;
        using var en = new DirectoryReader(item, options, shared, state, progress);
        while (en.MoveNext()) { }

        Interlocked.Increment(ref progress._dirs);
        if (en.FileCount > 0)
        {
            Interlocked.Add(ref progress._files, en.FileCount);
            Interlocked.Add(ref progress._bytes, en.ByteCount);
        }
        item.Node.OwnSize = en.ByteCount;
        item.Node.OwnFiles = en.FileCount;
    }

    /// <summary>Liest genau einen Ordner. Alle Arbeit passiert in ShouldIncludeEntry; es wird nichts zurückgegeben.</summary>
    private sealed class DirectoryReader : FileSystemEnumerator<byte>
    {
        private readonly WorkItem _item;
        private readonly ScanOptions _options;
        private readonly Shared _shared;
        private readonly WorkerState _state;
        private readonly ScanProgress _progress;
        public long FileCount;
        public long ByteCount;

        public DirectoryReader(WorkItem item, ScanOptions options, Shared shared, WorkerState state, ScanProgress progress)
            : base(item.Path, s_enumOptions)
        {
            _item = item;
            _options = options;
            _shared = shared;
            _state = state;
            _progress = progress;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            var attrs = entry.Attributes;
            if (entry.IsDirectory)
            {
                // Junctions/Symlinks nicht folgen – verhindert Endlosschleifen und Doppelzählung.
                if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
                var parent = _item.Node;
                var child = new DirNode(entry.FileName.ToString(), parent, parent.Depth + 1,
                    (attrs & FileAttributes.Hidden) != 0);
                (parent.Children ??= new List<DirNode>()).Add(child);
                Interlocked.Increment(ref _shared.Pending);
                _shared.Stack.Push(new WorkItem(child, entry.ToFullPath()));
                return false;
            }

            if ((attrs & CloudOnlyMask) != 0) return false;

            long size = entry.Length;
            FileCount++;
            ByteCount += size;

            if (size >= 65536)
            {
                var cat = FileCategories.Classify(entry.FileName);
                _state.CatBytes[(int)cat] += size;
                _state.CatCounts[(int)cat]++;

                if (size >= _options.MinFileSize &&
                    (_options.IncludeHidden || (attrs & FileAttributes.Hidden) == 0))
                {
                    _state.Found.Add(new FileEntry
                    {
                        FullPath = entry.ToFullPath(),
                        Size = size,
                        LastWriteUtc = entry.LastWriteTimeUtc.UtcDateTime,
                        Attributes = attrs,
                        Category = cat,
                    });
                    Interlocked.Increment(ref _progress._found);
                }
            }
            else
            {
                _state.CatBytes[(int)FileCategory.Other] += size;
                _state.CatCounts[(int)FileCategory.Other]++;
            }
            return false;
        }

        protected override byte TransformEntry(ref FileSystemEntry entry) => 0;

        protected override bool ContinueOnError(int error)
        {
            Interlocked.Increment(ref _progress._errors);
            return true;
        }
    }
}
