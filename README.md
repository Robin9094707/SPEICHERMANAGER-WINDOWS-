# Simplex Speichermanager

Native **WinUI 3**-App für Windows 11 (Mica, Fluent Design, Hell/Dunkel), die Ihre Festplatten blitzschnell nach Speicherfressern durchsucht und sicher aufräumt.

## Funktionen

| Bereich | Was es macht |
|---|---|
| **Übersicht** | Alle Laufwerke mit Belegung, Gesamt-Freispeicher, in dieser Sitzung freigegebener Platz, Schnellaktionen |
| **Große Dateien** | Paralleler Scan über alle Prozessorkerne. 4 Scan-Modi: *Schnell (Benutzerordner)*, *Ein Laufwerk*, *Alle Laufwerke*, *Eigener Ordner*. 3 Leistungsprofile: *Maximal* (2× Kerne, für SSD/NVMe), *Ausgewogen*, *Schonend* (HDD). Findet auch **versteckte Dateien**, filtert nach Kategorie (Videos, ISOs, VM-Festplatten, Installer …), Suche, Sortierung, Mehrfachauswahl, Löschen in Papierkorb oder endgültig |
| **Ordneranalyse** | Größenbaum aus dem letzten Scan (wie TreeSize/WinDirStat): Ordner per Doppelklick öffnen, Prozentbalken, Ordner löschen |
| **Bereinigung** | Papierkorb, Temp-Dateien, Windows-Update-Cache, Übermittlungsoptimierung, Miniaturansichten, Fehlerberichte & Dumps, Shader-Caches (DirectX/NVIDIA/AMD/Intel), Treiber-Reste, Browser-Caches (Chrome, Edge, Brave, Opera/GX, Vivaldi, Firefox), Discord, Slack, Teams, VS Code, Spotify, Steam, Epic, Entwickler-Caches (npm, pip, NuGet, Gradle, Cargo …). Plus: Ruhezustand deaktivieren (hiberfil.sys), WinSxS-Bereinigung (DISM) |
| **Spiele** | Erkennt Steam, Epic, GOG, Ubisoft, EA, Battle.net, Riot, Rockstar, Xbox/Game Pass – nach Größe sortiert, mit Deinstallation über den Launcher |
| **Deinstallation** | Liest **alle** Desktop-Programme (Registry, 32/64 Bit, pro Benutzer) und **alle Windows-/Store-Apps** ein – mit echten Programmsymbolen. Bloatware (Bing-Apps, Candy Crush, TikTok, McAfee-Testversionen, OEM-Software …) ist markiert und mit einem Klick auswählbar, Laufzeiten/Treiber sind als *Vorsicht* gekennzeichnet, Systemkomponenten (Store, Defender, WebView2, VC-Libs …) sind **gesperrt**. Mehrfachauswahl, **stille Deinstallation** (MSI, Inno Setup, NSIS, Squirrel), optional Wiederherstellungspunkt und Store-Apps für alle Benutzer entfernen. Danach startet automatisch die Restesuche |
| **Reste-Finder** | Nach jeder Deinstallation: Installationsordner, Ordner in Programme/ProgramData/AppData/LocalLow/Temp, Startmenü & Desktop, Registry (Software, App Paths, Autostart, Uninstall-Eintrag, „Öffnen mit“), Dienste, geplante Aufgaben und Firewall-Regeln. **Altlasten-Suche**: erkennt Reste früher entfernter Programme – verwaiste Ordner, Einträge in „Apps & Features“ ohne Programm, tote Verknüpfungen, App-Daten entfernter Store-Apps, Dienste/Aufgaben/Autostarts mit gelöschter Datei, alte Registry-Schlüssel. Alles erscheint in einer **Vorschau** mit Bewertung (*Sicher*, *Wahrscheinlich*, *Prüfen*) und wird erst nach Bestätigung gelöscht – mit automatischer **Registry-Sicherung (.reg)** |

## Sicherheit

Systemwichtiges wird **nie** gelöscht: `C:\Windows`, Bootdateien, `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, Registry-Hives (`NTUSER.DAT`), Wiederherstellung, `System Volume Information`, Windows Defender, Store-Apps (`WindowsApps`), DPAPI-Schlüssel sowie alle Dateien mit System-Attribut. Programm-, Spiel- und App-Daten werden als **Vorsicht** markiert. Vor jedem Löschvorgang wird erneut geprüft.

## So arbeitet der Deinstaller

1. **Fingerabdruck** vor der Deinstallation: Installationsordner, Exe-Namen, Produkt-/Herstellernamen aus den Datei-Versionsinfos
2. Original-Deinstaller läuft (auf Wunsch still); Simplex wartet auch auf Hilfsprozesse (NSIS `Au_.exe`, Inno `_iu*.tmp`)
3. **Restesuche** mit dem Fingerabdruck – alles, was noch von einem anderen installierten Programm, einer Store-App, einem Spiel, Dienst oder laufenden Prozess genutzt wird, wird ausgelassen
4. **Vorschau** → Auswahl → Löschen (Papierkorb oder endgültig), vorher `.reg`-Sicherung unter `%LOCALAPPDATA%\SimplexSpeichermanager\Sicherungen`

## Download

Jeder Push baut die App automatisch per GitHub Actions:

1. Reiter **Actions** → neuester Lauf von *Build Simplex Speichermanager*
2. Unten bei **Artifacts**:
   - **`SimplexSpeichermanager-Setup-win-x64`** → Installer (`SimplexSpeichermanager-Setup-1.1.0-x64.exe`). Installiert sich einfach über eine bestehende Version (Einstellungen bleiben erhalten), mit Startmenü-Eintrag und sauberer Deinstallation.
   - oder `SimplexSpeichermanager-win-x64` → portable Version: ZIP entpacken → **`SimplexSpeichermanager.exe`** starten (bzw. `arm64` für ARM-Geräte)

Die App ist komplett eigenständig (*self-contained*): .NET 8 und Windows App SDK sind enthalten, nichts muss installiert werden.

Für einen Release mit ZIP-Download einen Tag pushen, z. B. `git tag v1.0.0 && git push origin v1.0.0`.

## Selbst bauen

Voraussetzung: Windows 10/11, .NET 8 SDK (oder Visual Studio 2022 mit „WinUI-Anwendungsentwicklung“).

```powershell
dotnet publish src/SimplexSpeichermanager/SimplexSpeichermanager.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o publish
```

## Aufbau

```
src/Simplex.Core/              UI-unabhängige Logik
  Scanning/FileScanner.cs      Multithread-Scanner (Work-Stealing-Stapel, NtQueryDirectoryFile, 0 Allokationen/Datei)
  Safety/SafetyGuard.cs        Schutzregeln für System-, Programm- und Benutzerdaten
  Safety/Deleter.cs            Löschen (Papierkorb via SHFileOperation oder endgültig)
  Cleanup/                     Cache-Katalog und Bereinigungs-Engine
  Games/                       Spieleerkennung
  Uninstall/                   Deinstallations-Modul: AppCatalog (Win32 + Store), AppClassifier (Bloatware/System),
                               UninstallRunner, LeftoverProfile/-Scanner, OrphanScanner, LeftoverCleaner, KnownSoftwareIndex
installer/                     Inno-Setup-Skript für den Installer
src/SimplexSpeichermanager/    WinUI-3-App (unpackaged, self-contained)
```
