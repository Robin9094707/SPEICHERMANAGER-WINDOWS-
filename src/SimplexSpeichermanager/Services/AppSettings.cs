using System.Text.Json;

namespace Simplex.Services;

public sealed class AppSettings
{
    public int Theme { get; set; }               // 0 = System, 1 = Hell, 2 = Dunkel
    public bool RecycleByDefault { get; set; } = true;
    public int MinSizeIndex { get; set; } = 2;
    public int PerformanceIndex { get; set; }
    public bool IncludeHidden { get; set; } = true;
    public bool HideProtected { get; set; } = true;
    public bool SilentUninstall { get; set; } = true;
    public bool RestorePoint { get; set; } = true;
    public bool StoreAllUsers { get; set; }

    private static readonly string FilePath = Path.Combine(Log.Folder, "settings.json");

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
