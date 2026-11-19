namespace Simplex.Core.Uninstall;

public enum AppSource
{
    /// <summary>Klassisches Desktop-Programm (Registry-Eintrag unter …\Uninstall).</summary>
    Win32,
    /// <summary>Microsoft-Store-/AppX-/MSIX-Paket.</summary>
    Store,
}

public enum AppClass
{
    /// <summary>Gewöhnliches Programm.</summary>
    Normal,
    /// <summary>Vorinstallierte Werbe-/Zusatz-App, die man gefahrlos entfernen kann.</summary>
    Bloatware,
    /// <summary>Laufzeitumgebung, Treiber, Codec oder Komponente, die andere Programme brauchen – Vorsicht.</summary>
    Runtime,
    /// <summary>Systemkomponente – Deinstallation gesperrt.</summary>
    System,
}

/// <summary>Ein installiertes Programm oder eine Store-App.</summary>
public sealed class AppEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public long Size { get; set; }
    public DateTime? InstallDate { get; init; }
    public AppSource Source { get; init; }
    public AppClass Class { get; set; }
    public string ClassReason { get; set; } = "";

    // ---- Win32 ----
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    /// <summary>Vollständiger Registry-Pfad des Uninstall-Eintrags, z. B. HKLM\SOFTWARE\…\Uninstall\Foo.</summary>
    public string RegistryPath { get; init; } = "";
    public string? MsiProductCode { get; init; }
    public bool PerUser { get; init; }
    public string DisplayIcon { get; init; } = "";
    /// <summary>Der im Eintrag hinterlegte Deinstaller existiert nicht mehr (Programm wurde unsauber entfernt).</summary>
    public bool UninstallerMissing { get; init; }

    // ---- Store ----
    public string PackageFullName { get; init; } = "";
    public string PackageFamilyName { get; init; } = "";
    public string PackageName { get; init; } = "";
    public string LogoPath { get; init; } = "";

    public bool CanUninstall => Class != AppClass.System;
    public bool IsStore => Source == AppSource.Store;
}

public enum LeftoverKind
{
    Folder,
    File,
    Shortcut,
    RegistryKey,
    RegistryValue,
    UninstallEntry,
    Service,
    ScheduledTask,
    FirewallRule,
}

public enum Confidence
{
    /// <summary>Gehört sicher zum Programm (z. B. Installationsordner, Uninstall-Eintrag).</summary>
    High,
    /// <summary>Sehr wahrscheinlich (Name stimmt genau überein).</summary>
    Medium,
    /// <summary>Möglicherweise – bitte selbst prüfen. Standardmäßig nicht ausgewählt.</summary>
    Low,
}

/// <summary>Ein gefundener Rest (Datei, Ordner, Registry-Schlüssel, Dienst …).</summary>
public sealed class LeftoverItem
{
    public required LeftoverKind Kind { get; init; }
    /// <summary>Dateipfad, Registry-Pfad (HKLM\… / HKCU\…), Dienstname oder Aufgabenpfad.</summary>
    public required string Path { get; init; }
    /// <summary>Bei RegistryValue/FirewallRule: Name des Werts.</summary>
    public string? ValueName { get; init; }
    public string Display { get; init; } = "";
    public long Size { get; set; }
    public Confidence Confidence { get; init; }
    public string Reason { get; init; } = "";
    /// <summary>Programm, zu dem der Rest gehört (oder „Unbekannt“ bei Altlasten).</summary>
    public string Owner { get; init; } = "";
    public bool RequiresAdmin { get; init; }

    public string Key => Kind + "|" + Path + "|" + ValueName;

    public bool IsRegistry => Kind is LeftoverKind.RegistryKey or LeftoverKind.RegistryValue or LeftoverKind.UninstallEntry or LeftoverKind.FirewallRule;
    public bool IsFileSystem => Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut;
}

public sealed class LeftoverCleanResult
{
    public int Removed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public long FreedBytes { get; set; }
    public string? BackupFile { get; set; }
    public List<string> Errors { get; } = new();
    public List<LeftoverItem> RemovedItems { get; } = new();
}
