using Microsoft.UI.Xaml;
using Simplex.Core;
using Simplex.Core.Cleanup;

namespace Simplex.ViewModels;

public sealed class CleanupItem : Observable
{
    public CleanupItem(CleanupCategory category, bool isAdmin)
    {
        Category = category;
        CanRun = !category.RequiresAdmin || isAdmin;
        _isChecked = category.DefaultSelected && CanRun;
    }

    public CleanupCategory Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public string Glyph => Category.Glyph;
    public bool CanRun { get; }
    public Visibility AdminVisibility => CanRun ? Visibility.Collapsed : Visibility.Visible;
    public double ItemOpacity => CanRun ? 1.0 : 0.6;

    private bool? _isChecked;
    public bool? IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;

    private long _bytes = -1;
    public long Bytes
    {
        get => _bytes;
        set
        {
            if (Set(ref _bytes, value))
            {
                Raise(nameof(SizeText));
            }
        }
    }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value)) { Raise(nameof(BusyVisibility)); Raise(nameof(SizeText)); }
        }
    }
    public Visibility BusyVisibility => _busy ? Visibility.Visible : Visibility.Collapsed;

    private string? _status;
    public string? Status { get => _status; set { if (Set(ref _status, value)) Raise(nameof(SizeText)); } }

    public string SizeText => _busy ? "" : _status ?? (_bytes < 0 ? "–" : Fmt.Bytes(_bytes));
}

public sealed class CleanupGroup : List<CleanupItem>
{
    public CleanupGroup(string key, IEnumerable<CleanupItem> items) : base(items) { Key = key; }
    public string Key { get; }
}
