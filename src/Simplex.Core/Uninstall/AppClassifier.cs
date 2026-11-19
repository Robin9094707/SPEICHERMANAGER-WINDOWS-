namespace Simplex.Core.Uninstall;

/// <summary>
/// Ordnet Programme und Store-Apps ein: Bloatware (gefahrlos entfernbar), Laufzeit/Treiber (Vorsicht)
/// und Systemkomponenten (gesperrt).
/// </summary>
public static class AppClassifier
{
    // ---------------- Store-Apps ----------------

    /// <summary>Store-Pakete, die Windows zum Funktionieren braucht – niemals entfernen.</summary>
    private static readonly string[] s_storeProtected =
    {
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.VCLibs", "Microsoft.UI.Xaml",
        "Microsoft.NET.", "Microsoft.WindowsAppRuntime", "Microsoft.WinAppRuntime", "Microsoft.Services.Store.Engagement", "Microsoft.SecHealthUI",
        "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost", "MicrosoftWindows.Client.CBS", "MicrosoftWindows.Client.Core",
        "MicrosoftWindows.Client.FileExp", "MicrosoftWindows.Client.AIX", "MicrosoftWindows.Client.OOBE", "MicrosoftWindows.Client.Photon",
        "MicrosoftWindows.Client.LKG", "MicrosoftWindows.Client.Lighting", "Microsoft.Windows.Search",
        "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "Microsoft.LockApp", "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.Win32WebViewHost", "Microsoft.Windows.ContentDeliveryManager", "Microsoft.Windows.SecureAssessmentBrowser",
        "Microsoft.Windows.Apprep.ChxApp", "Microsoft.Windows.AssignedAccessLockApp", "Microsoft.Windows.CallingShellApp",
        "Microsoft.Windows.CapturePicker", "Microsoft.Windows.OOBENetwork", "Microsoft.Windows.PeopleExperienceHost",
        "Microsoft.Windows.PinningConfirmationDialog", "Microsoft.Windows.PrintQueueActionCenter", "Microsoft.Windows.XGpuEjectDialog",
        "Microsoft.ECApp", "Microsoft.CredDialogHost", "Microsoft.BioEnrollment", "Microsoft.AsyncTextService", "Microsoft.MicrosoftEdge",
        "Microsoft.MicrosoftEdgeDevToolsClient", "Microsoft.Windows.NarratorQuickStart", "Microsoft.Windows.ParentalControls",
        "Microsoft.WindowsAppSDK", "Microsoft.ApplicationCompatibilityEnhancements", "Microsoft.WidgetsPlatformRuntime",
        "Microsoft.Windows.DevicesFlowHost", "Microsoft.XboxGameCallableUI", "Microsoft.Windows.FileExplorer", "Microsoft.Windows.FilePicker",
        "NcsiUwpApp", "Windows.CBSPreview", "windows.immersivecontrolpanel", "Windows.PrintDialog", "1527c705-839a-4832-9118-54d4Bd6a0c89",
        "c5e2524a-ea46-4f67-841f-6a9465d9d515", "E2A4F912-2574-4A75-9BB0-0D023378592B", "F46D4000-FD22-4DB4-AC8E-4E1DDDE828FE",
        "Microsoft.Winget.Source", "Microsoft.SecureAssessmentBrowser", "Microsoft.WindowsDefender", "MicrosoftWindows.UndockedDevKit",
        "Microsoft.Windows.Ai.Copilot.Provider", "MicrosoftWindows.CrossDevice",
    };

    /// <summary>Vorinstallierte Werbe- und Zusatz-Apps.</summary>
    private static readonly (string Prefix, string Reason)[] s_storeBloat =
    {
        ("Microsoft.BingNews", "Nachrichten-App mit Werbung"),
        ("Microsoft.BingWeather", "Wetter-App (vorinstalliert)"),
        ("Microsoft.BingFinance", "Finanzen-App (vorinstalliert)"),
        ("Microsoft.BingSports", "Sport-App (vorinstalliert)"),
        ("Microsoft.BingSearch", "Bing-Suche (vorinstalliert)"),
        ("Microsoft.BingTranslator", "Übersetzer (vorinstalliert)"),
        ("Microsoft.News", "Nachrichten-App mit Werbung"),
        ("Microsoft.GetHelp", "„Hilfe anfordern“ (vorinstalliert)"),
        ("Microsoft.Getstarted", "Tipps-App (vorinstalliert)"),
        ("Microsoft.MicrosoftSolitaireCollection", "Spiel mit Werbung"),
        ("Microsoft.MicrosoftOfficeHub", "Office-Werbe-App"),
        ("Microsoft.Office.OneNote", "OneNote für Windows 10 (veraltet)"),
        ("Microsoft.Office.Sway", "Sway (vorinstalliert)"),
        ("Microsoft.People", "Kontakte-App (veraltet)"),
        ("Microsoft.PowerAutomateDesktop", "Power Automate (vorinstalliert)"),
        ("Microsoft.Todos", "To Do (vorinstalliert)"),
        ("Microsoft.WindowsFeedbackHub", "Feedback-Hub"),
        ("Microsoft.WindowsMaps", "Karten-App (wird eingestellt)"),
        ("Microsoft.ZuneVideo", "Filme & TV (veraltet)"),
        ("Microsoft.MixedReality.Portal", "Mixed-Reality-Portal"),
        ("Microsoft.SkypeApp", "Skype (eingestellt)"),
        ("Microsoft.3DBuilder", "3D Builder (veraltet)"),
        ("Microsoft.Microsoft3DViewer", "3D-Viewer (veraltet)"),
        ("Microsoft.Print3D", "Print 3D (veraltet)"),
        ("Microsoft.MSPaint", "Paint 3D (eingestellt)"),
        ("Microsoft.OneConnect", "Mobile Tarife"),
        ("Microsoft.Wallet", "Microsoft Wallet"),
        ("Microsoft.Messaging", "Nachrichten-App (veraltet)"),
        ("Microsoft.549981C3F5F10", "Cortana (eingestellt)"),
        ("Microsoft.NetworkSpeedTest", "Netzwerk-Speedtest"),
        ("Microsoft.MinecraftEducationEdition", "Minecraft Education (vorinstalliert)"),
        ("Microsoft.YourPhone", "Smartphone-Link (optional)"),
        ("Microsoft.OutlookForWindows", "Neues Outlook (vorinstalliert)"),
        ("Microsoft.Windows.DevHome", "Dev Home (eingestellt)"),
        ("Microsoft.Copilot", "Copilot-App (vorinstalliert)"),
        ("Microsoft.MicrosoftJournal", "Journal (vorinstalliert)"),
        ("Microsoft.MicrosoftStickyNotes", "Kurznotizen (optional)"),
        ("Microsoft.WindowsSoundRecorder", "Audiorekorder (optional)"),
        ("Microsoft.WindowsAlarms", "Uhr (optional)"),
        ("Microsoft.XboxApp", "Xbox Console Companion (veraltet)"),
        ("Microsoft.GamingApp", "Xbox-App (nur für PC Game Pass nötig)"),
        ("Microsoft.XboxGamingOverlay", "Xbox Game Bar (optional)"),
        ("Microsoft.XboxGameOverlay", "Xbox Game Overlay (optional)"),
        ("Microsoft.XboxSpeechToTextOverlay", "Xbox Sprachausgabe-Overlay"),
        ("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family Safety"),
        ("MicrosoftCorporationII.QuickAssist", "Remotehilfe (optional)"),
        ("MicrosoftTeams", "Teams (privat, vorinstalliert)"),
        ("MSTeams", "Teams (vorinstalliert)"),
        ("MicrosoftWindows.Client.WebExperience", "Widgets (Nachrichten-Feed)"),
        ("Clipchamp.Clipchamp", "Clipchamp-Videoeditor (vorinstalliert)"),
        ("SpotifyAB.SpotifyMusic", "Spotify (vorinstalliert)"),
        ("Disney.", "Disney+ (vorinstalliert)"),
        ("4DF9E0F8.Netflix", "Netflix (vorinstalliert)"),
        ("king.com.", "Candy Crush & Co. (Werbe-Spiel)"),
        ("Facebook.", "Facebook (vorinstalliert)"),
        ("FACEBOOK.", "Facebook (vorinstalliert)"),
        ("BytedancePte.Ltd.TikTok", "TikTok (vorinstalliert)"),
        ("AmazonVideo.PrimeVideo", "Prime Video (vorinstalliert)"),
        ("Amazon.com.Amazon", "Amazon (vorinstalliert)"),
        ("5A894077.McAfeeSecurity", "McAfee-Testversion"),
        ("7EE7776C.LinkedInforWindows", "LinkedIn (vorinstalliert)"),
        ("Duolingo-LearnLanguagesforFree", "Duolingo (vorinstalliert)"),
        ("PandoraMediaInc.", "Pandora (vorinstalliert)"),
        ("AdobeSystemsIncorporated.AdobePhotoshopExpress", "Photoshop Express (vorinstalliert)"),
        ("ActiproSoftwareLLC.", "Werbe-App"),
        ("Flipboard.Flipboard", "Flipboard (vorinstalliert)"),
        ("ShazamEntertainmentLtd.Shazam", "Shazam (vorinstalliert)"),
        ("ClearChannelRadioDigital.iHeartRadio", "iHeartRadio (vorinstalliert)"),
        ("TuneIn.TuneInRadio", "TuneIn (vorinstalliert)"),
        ("Microsoft.Advertising.Xaml", "Werbe-Bibliothek"),
        ("DolbyLaboratories.DolbyAccess", "Dolby Access (Werbung für Abo)"),
        ("WildTangentGames.", "WildTangent-Spiele (Werbung)"),
        ("Nordcurrent.", "Werbe-Spiel"),
        ("A278AB0D.", "Werbe-Spiel (Gameloft)"),
        ("Playtika.", "Werbe-Spiel"),
        ("NORDCURRENT.", "Werbe-Spiel"),
        ("E046963F.LenovoCompanion", "Lenovo Vantage (OEM)"),
        ("AD2F1837.", "HP-Zusatzsoftware (OEM)"),
        ("B9ECED6F.", "ASUS-Zusatzsoftware (OEM)"),
        ("DellInc.", "Dell-Zusatzsoftware (OEM)"),
        ("AcerIncorporated.", "Acer-Zusatzsoftware (OEM)"),
    };

    /// <summary>Komponenten, die andere Apps brauchen (Codecs, Xbox-Anmeldung …).</summary>
    private static readonly (string Prefix, string Reason)[] s_storeRuntime =
    {
        ("Microsoft.HEIFImageExtension", "Bild-Codec – Fotos im HEIC-Format"),
        ("Microsoft.HEVCVideoExtension", "Video-Codec (HEVC/H.265)"),
        ("Microsoft.VP9VideoExtensions", "Video-Codec (VP9)"),
        ("Microsoft.AV1VideoExtension", "Video-Codec (AV1)"),
        ("Microsoft.WebMediaExtensions", "Medien-Codecs (Web)"),
        ("Microsoft.WebpImageExtension", "Bild-Codec (WebP)"),
        ("Microsoft.RawImageExtension", "Bild-Codec (RAW)"),
        ("Microsoft.MPEG2VideoExtension", "Video-Codec (MPEG-2)"),
        ("Microsoft.AVCEncoderVideoExtension", "Video-Codec (H.264-Encoder)"),
        ("Microsoft.DolbyAudioExtensions", "Audio-Codec (Dolby)"),
        ("Microsoft.Xbox.TCUI", "Wird von Spielen für Xbox-Anmeldung/Freunde benötigt"),
        ("Microsoft.XboxIdentityProvider", "Wird von Spielen für die Xbox-Anmeldung benötigt"),
        ("Microsoft.GamingServices", "Wird von Game-Pass-Spielen benötigt"),
        ("Microsoft.WindowsTerminal", "Standard-Konsole von Windows 11"),
        ("Microsoft.WindowsNotepad", "Editor (Standard-Textprogramm)"),
        ("Microsoft.WindowsCalculator", "Rechner"),
        ("Microsoft.ScreenSketch", "Snipping Tool (Bildschirmfotos, Win+Umschalt+S)"),
        ("Microsoft.Paint", "Paint"),
        ("Microsoft.WindowsCamera", "Kamera"),
        ("Microsoft.Windows.Photos", "Fotos (Standard-Bildbetrachter)"),
        ("Microsoft.ZuneMusic", "Medienwiedergabe (Standard-Player)"),
        ("Microsoft.OneDriveSync", "OneDrive-Synchronisierung"),
        ("Microsoft.LanguageExperiencePack", "Sprachpaket der Windows-Oberfläche"),
        ("Microsoft.WindowsCommunicationsApps", "Mail & Kalender"),
        ("Microsoft.MicrosoftEdge.Stable", "Microsoft Edge"),
        ("Microsoft.ApplicationCompatibilityEnhancements", "Kompatibilitätskomponente"),
        ("Microsoft.Winget", "Paketverwaltung (winget)"),
        ("MicrosoftWindows.", "Windows-Komponente"),
        ("Microsoft.Windows.", "Windows-Komponente"),
    };

    public static (AppClass Class, string Reason) ClassifyStore(string packageName, bool systemSigned)
    {
        foreach (var p in s_storeProtected)
            if (packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return (AppClass.System, "Windows-Systemkomponente – wird nicht entfernt");
        if (systemSigned)
            return (AppClass.System, "Teil von Windows (systemsigniert) – kann nicht deinstalliert werden");
        foreach (var (p, r) in s_storeBloat)
            if (packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return (AppClass.Bloatware, r);
        foreach (var (p, r) in s_storeRuntime)
            if (packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return (AppClass.Runtime, r);
        return (AppClass.Normal, "");
    }

    // ---------------- Desktop-Programme ----------------

    private static readonly (string Needle, string Reason)[] s_win32Protected =
    {
        ("Microsoft Edge WebView2", "Wird von Windows und vielen Programmen zur Anzeige benötigt"),
        ("Microsoft Update Health Tools", "Teil von Windows Update"),
        ("Microsoft Edge Update", "Teil von Microsoft Edge"),
        ("Windows Driver Package", "Treiberpaket – Entfernen kann Hardware lahmlegen"),
        ("Microsoft Windows Desktop Runtime - ", ""),
        ("Windows Subsystem for Linux Update", "Teil von WSL"),
        ("Microsoft GameInput", "Eingabe-Komponente für Spiele/Controller"),
        ("Windows Software Development Kit", ""),
    };

    private static readonly (string Needle, string Reason)[] s_win32Runtime =
    {
        ("Microsoft Visual C++", "Laufzeitbibliothek – wird von vielen Programmen und Spielen benötigt"),
        ("Microsoft .NET", "Laufzeitumgebung – wird von Programmen benötigt"),
        ("Microsoft Windows Desktop Runtime", "Laufzeitumgebung – wird von Programmen benötigt"),
        ("Microsoft ASP.NET", "Laufzeitumgebung"),
        ("DirectX", "Spiele-Laufzeitkomponente"),
        ("Microsoft XNA", "Spiele-Laufzeitkomponente"),
        ("OpenAL", "Audio-Laufzeitkomponente für Spiele"),
        ("PhysX", "Physik-Laufzeitkomponente für Spiele"),
        ("Vulkan Run Time", "Grafik-Laufzeitkomponente"),
        ("Java", "Laufzeitumgebung – Programme können darauf angewiesen sein"),
        ("Microsoft Edge", "Standardbrowser-Komponente von Windows"),
        ("NVIDIA Graphics Driver", "Grafiktreiber"),
        ("NVIDIA HD Audio", "Treiber"),
        ("NVIDIA PhysX", "Spiele-Laufzeitkomponente"),
        ("AMD Software", "Grafiktreiber-Software"),
        ("AMD Chipset", "Chipsatztreiber"),
        ("Intel(R) Management Engine", "Chipsatz-Treiber"),
        ("Intel(R) Graphics", "Grafiktreiber"),
        ("Intel(R) Chipset", "Chipsatztreiber"),
        ("Intel(R) Serial IO", "Treiber"),
        ("Intel(R) Rapid Storage", "Speichertreiber"),
        ("Intel(R) Wireless", "WLAN-Treiber"),
        ("Intel(R) PROSet", "WLAN-Treiber"),
        ("Realtek", "Hardware-Treiber"),
        ("Synaptics", "Touchpad-Treiber"),
        ("ELAN", "Touchpad-Treiber"),
        ("Dolby", "Audio-Treiberkomponente"),
        ("Waves MaxxAudio", "Audio-Treiberkomponente"),
        ("Conexant", "Audio-Treiber"),
        ("Bonjour", "Netzwerkdienst (z. B. für iTunes/Drucker)"),
        ("Microsoft OneDrive", "Cloud-Synchronisierung – eigene Dateien vorher sichern"),
        ("Windows App Runtime", "Laufzeitumgebung für Apps"),
        ("Microsoft Windows App Runtime", "Laufzeitumgebung für Apps"),
        ("Python Launcher", "Python-Komponente"),
        ("Microsoft Office", "Office – Dokumente bleiben erhalten"),
    };

    private static readonly (string Needle, string Reason)[] s_win32Bloat =
    {
        ("McAfee", "Vorinstallierte Testversion (McAfee)"),
        ("Norton Security", "Vorinstallierte Testversion (Norton)"),
        ("Norton 360", "Vorinstallierte Testversion (Norton)"),
        ("WildTangent", "Vorinstallierte Werbe-Spiele"),
        ("Windows PC Health Check", "PC-Integritätsprüfung (nur für Upgrade-Check)"),
        ("Booking.com", "Werbe-Verknüpfung"),
        ("ExpressVPN", "Vorinstallierte Testversion"),
        ("Dropbox Promotion", "Werbung"),
        ("CyberLink", "Vorinstallierte OEM-Software"),
        ("HP Wolf Security", "Vorinstallierte OEM-Software"),
        ("HP Sure", "Vorinstallierte OEM-Software"),
        ("HP Documentation", "Vorinstallierte OEM-Software"),
        ("HP JumpStart", "Vorinstallierte OEM-Software"),
        ("Lenovo Welcome", "Vorinstallierte OEM-Software"),
        ("Dell SupportAssist Remediation", "Vorinstallierte OEM-Software"),
        ("Dell Digital Delivery", "Vorinstallierte OEM-Software"),
        ("Acer Jumpstart", "Vorinstallierte OEM-Software"),
        ("Avast Secure Browser", "Mitinstallierter Browser"),
        ("AVG Secure Browser", "Mitinstallierter Browser"),
        ("CCleaner Browser", "Mitinstallierter Browser"),
        ("Opera GX Installer", "Installer-Rest"),
        ("Microsoft 365 - ", "Vorinstalliertes Office (nur nötig, wenn Sie es nutzen)"),
        ("Microsoft Teams classic", "Teams (klassisch, eingestellt)"),
        ("Teams Machine-Wide Installer", "Teams-Installer-Rest"),
    };

    public static (AppClass Class, string Reason) ClassifyWin32(string name, string publisher)
    {
        foreach (var (n, r) in s_win32Protected)
            if (name.StartsWith(n, StringComparison.OrdinalIgnoreCase) && r.Length > 0)
                return (AppClass.System, r);
        foreach (var (n, r) in s_win32Bloat)
            if (name.StartsWith(n, StringComparison.OrdinalIgnoreCase))
                return (AppClass.Bloatware, r);
        foreach (var (n, r) in s_win32Runtime)
            if (name.StartsWith(n, StringComparison.OrdinalIgnoreCase))
                return (AppClass.Runtime, r);
        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("Runtime", StringComparison.OrdinalIgnoreCase) || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)))
            return (AppClass.Runtime, "Laufzeitkomponente – wird von anderen Programmen benötigt");
        if (name.Contains("Driver", StringComparison.OrdinalIgnoreCase) || name.Contains("Treiber", StringComparison.OrdinalIgnoreCase))
            return (AppClass.Runtime, "Treiber – Entfernen kann Hardware beeinträchtigen");
        return (AppClass.Normal, "");
    }
}
