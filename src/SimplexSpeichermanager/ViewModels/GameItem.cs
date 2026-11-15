using Microsoft.UI.Xaml;
using Simplex.Core;
using Simplex.Core.Games;

namespace Simplex.ViewModels;

public sealed class GameItem : Observable
{
    public GameItem(GameInfo game) { Game = game; }

    public GameInfo Game { get; }
    public string Name => Game.Name;
    public string Launcher => Game.Launcher;
    public string InstallPath => Game.InstallPath;
    public string Initial => string.IsNullOrEmpty(Game.Name) ? "?" : Game.Name[..1].ToUpperInvariant();

    public long Size => Game.Size;
    public string SizeText => Game.SizeKnown || Game.Size > 0 ? Fmt.Bytes(Game.Size) : "wird berechnet…";

    public double Percent { get; private set; }

    public void UpdateSize(long size, long max)
    {
        Game.Size = size;
        Game.SizeKnown = true;
        Raise(nameof(Size));
        Raise(nameof(SizeText));
        SetMax(max);
    }

    public void SetMax(long max)
    {
        Percent = max > 0 ? 100.0 * Game.Size / max : 0;
        Raise(nameof(Percent));
    }

    public bool CanUninstall => Game.UninstallUri != null || !string.IsNullOrEmpty(Game.UninstallCommand);
    public Visibility UninstallVisibility => CanUninstall ? Visibility.Visible : Visibility.Collapsed;
}
