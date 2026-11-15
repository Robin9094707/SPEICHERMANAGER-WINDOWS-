using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Simplex.Services;

public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, object content, string primary, string close = "Abbrechen")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        if (App.Window?.Content is FrameworkElement fe) dialog.RequestedTheme = fe.ActualTheme;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Löschdialog mit Wahl zwischen Papierkorb und endgültigem Löschen. Gibt null bei Abbruch zurück, sonst "toRecycleBin".</summary>
    public static async Task<bool?> ConfirmDeleteAsync(XamlRoot root, string title, string message, string? warning)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(warning))
        {
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Message = warning,
            });
        }
        var mode = new RadioButtons { Header = "Löschmethode" };
        mode.Items.Add("In den Papierkorb verschieben (wiederherstellbar)");
        mode.Items.Add("Endgültig löschen (gibt Platz sofort frei)");
        mode.SelectedIndex = AppSettings.Current.RecycleByDefault ? 0 : 1;
        panel.Children.Add(mode);

        bool ok = await ConfirmAsync(root, title, panel, "Löschen");
        if (!ok) return null;
        return mode.SelectedIndex == 0;
    }
}
