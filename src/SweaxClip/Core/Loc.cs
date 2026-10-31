using System.Globalization;
using System.Windows.Markup;

namespace SweaxClip.Core;

/// <summary>Tiny two-language string table (English / Türkçe).</summary>
public static class Loc
{
    public static bool Turkish { get; private set; }
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-GB");

    public static void Init(string? setting)
    {
        Turkish = setting switch
        {
            "tr" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr",
        };
        Culture = CultureInfo.GetCultureInfo(Turkish ? "tr-TR" : "en-GB");
    }

    public static string T(string key) => S.TryGetValue(key, out var v) ? (Turkish ? v.tr : v.en) : key;
    public static string Format(string key, params object[] args) => string.Format(Culture, T(key), args);

    public static string KindName(ClipKind k) => T("Kind" + k);
    public static string ScopeName(Scope s) => T("Scope" + s);
    public static string ScopeShort(Scope s) => T("Short" + s);

    static readonly Dictionary<string, (string en, string tr)> S = new()
    {
        ["AppTagline"] = ("A clipboard that never forgets", "Hiç unutmayan pano"),
        ["Now"] = ("now", "şimdi"),
        ["NowShort"] = ("now", "şimdi"),
        ["MinShort"] = ("{0}m", "{0} dk"),
        ["HourShort"] = ("{0}h", "{0} sa"),
        ["YesterdayShort"] = ("yest.", "dün"),
        ["SearchCount"] = ("Search {0} clips", "{0} kayıtta ara"),
        ["Found"] = ("{0} found", "{0} sonuç"),
        ["HintLibrary"] = ("library", "kütüphane"),
        ["MinutesAgo"] = ("{0}m ago", "{0} dk önce"),
        ["HoursAgo"] = ("{0}h ago", "{0} sa önce"),
        ["Today"] = ("Today", "Bugün"),
        ["Yesterday"] = ("Yesterday", "Dün"),
        ["UnknownApp"] = ("Unknown app", "Bilinmeyen uygulama"),
        ["Chars"] = ("{0} chars", "{0} karakter"),
        ["CharsLines"] = ("{0} chars · {1} lines", "{0} karakter · {1} satır"),
        ["FilesCount"] = ("{0} file(s)", "{0} dosya"),
        ["MoreFiles"] = ("+{0} more", "+{0} tane daha"),

        ["KindText"] = ("Text", "Metin"),
        ["KindLink"] = ("Link", "Bağlantı"),
        ["KindCode"] = ("Code", "Kod"),
        ["KindColor"] = ("Color", "Renk"),
        ["KindImage"] = ("Image", "Görsel"),
        ["KindScreenshot"] = ("Screenshot", "Ekran görüntüsü"),
        ["KindFiles"] = ("Files", "Dosyalar"),

        ["ScopeAll"] = ("All items", "Tümü"),
        ["ScopeText"] = ("Text", "Metinler"),
        ["ScopeLinks"] = ("Links", "Bağlantılar"),
        ["ScopeImages"] = ("Images", "Görseller"),
        ["ScopeScreenshots"] = ("Screenshots", "Ekran görüntüleri"),
        ["ScopeFiles"] = ("Files", "Dosyalar"),
        ["ScopeCode"] = ("Code", "Kod"),
        ["ScopeColors"] = ("Colors", "Renkler"),
        ["ScopePinned"] = ("Pinned", "Sabitlenenler"),
        ["ScopeTrash"] = ("Trash", "Çöp kutusu"),

        ["ShortAll"] = ("All", "Tümü"),
        ["ShortText"] = ("Text", "Metin"),
        ["ShortLinks"] = ("Links", "Link"),
        ["ShortImages"] = ("Images", "Görsel"),
        ["ShortScreenshots"] = ("Shots", "Ekran"),
        ["ShortFiles"] = ("Files", "Dosya"),
        ["ShortCode"] = ("Code", "Kod"),
        ["ShortColors"] = ("Colors", "Renk"),
        ["ShortPinned"] = ("Pinned", "Sabit"),
        ["ShortTrash"] = ("Trash", "Çöp"),

        ["SearchHint"] = ("Search", "Ara"),
        ["SearchHintShort"] = ("Search clipboard history", "Pano geçmişinde ara"),
        ["EmptyTitle"] = ("Nothing here yet", "Henüz burada bir şey yok"),
        ["EmptyBody"] = ("Copy anything — it stays here for good.", "Bir şey kopyala — burada kalıcı olarak durur."),
        ["NoResults"] = ("No matches", "Eşleşme yok"),
        ["NoResultsBody"] = ("Try fewer letters, or another category.", "Daha kısa yaz ya da başka kategoriye bak."),
        ["TrashEmpty"] = ("Trash is empty", "Çöp kutusu boş"),

        ["HintPaste"] = ("paste", "yapıştır"),
        ["HintBrowse"] = ("browse", "gez"),
        ["Details"] = ("Details", "Ayrıntılar"),
        ["PressSpace"] = ("Click a card to pick it up", "Bir karta tıkla, eline al"),
        ["HintPlain"] = ("plain", "düz metin"),
        ["HintPin"] = ("pin", "sabitle"),
        ["HintDelete"] = ("delete", "sil"),
        ["HintTabs"] = ("category", "kategori"),
        ["HintPreview"] = ("preview", "önizle"),

        ["Paste"] = ("Paste", "Yapıştır"),
        ["PastePlain"] = ("Paste as plain text", "Düz metin olarak yapıştır"),
        ["CopyOnly"] = ("Copy to clipboard", "Panoya kopyala"),
        ["CopyShort"] = ("Copy", "Kopyala"),
        ["Copied"] = ("Copied", "Kopyalandı"),
        ["On"] = ("On", "Açık"),
        ["Off"] = ("Off", "Kapalı"),
        ["GridView"] = ("Gallery", "Galeri"),
        ["ListView"] = ("List", "Liste"),
        ["SetPastePlainNote"] = ("Shift + Enter does the opposite.", "Shift + Enter tersini yapar."),
        ["SetExportNote"] = ("One .zip with the database and every image — for a new PC or safe keeping.", "Veritabanı ve tüm görseller tek .zip dosyasında — yeni bilgisayar ya da güvenli saklama için."),
        ["SetExportButton"] = ("Export…", "Dışa aktar…"),
        ["ClipboardBusy"] = ("The clipboard is busy, try again", "Pano meşgul, tekrar dene"),
        ["Pinned"] = ("Pinned", "Sabitlendi"),
        ["Unpinned"] = ("Unpinned", "Sabitleme kaldırıldı"),
        ["MovedToTrash"] = ("Moved to trash", "Çöp kutusuna taşındı"),
        ["Restored"] = ("Restored", "Geri yüklendi"),
        ["CaptureResumed"] = ("Capture resumed", "Kayıt devam ediyor"),
        ["Pin"] = ("Pin", "Sabitle"),
        ["Unpin"] = ("Unpin", "Sabitlemeyi kaldır"),
        ["Delete"] = ("Move to trash", "Çöpe taşı"),
        ["Restore"] = ("Restore", "Geri yükle"),
        ["DeleteForever"] = ("Delete forever", "Kalıcı olarak sil"),
        ["EmptyTrash"] = ("Empty trash", "Çöpü boşalt"),
        ["EmptyTrashConfirm"] = ("Permanently delete {0} item(s) in the trash? This cannot be undone.", "Çöpteki {0} öğe kalıcı olarak silinsin mi? Bu geri alınamaz."),
        ["DeleteForeverConfirm"] = ("Permanently delete this item? This cannot be undone.", "Bu öğe kalıcı olarak silinsin mi? Bu geri alınamaz."),
        ["OpenLink"] = ("Open link", "Bağlantıyı aç"),
        ["OpenFile"] = ("Open", "Aç"),
        ["ShowInFolder"] = ("Show in folder", "Klasörde göster"),
        ["SaveImage"] = ("Save image as…", "Görseli farklı kaydet…"),
        ["CopyOcr"] = ("Copy recognised text", "Tanınan metni kopyala"),
        ["OpenLibrary"] = ("Open library", "Kütüphaneyi aç"),
        ["Settings"] = ("Settings", "Ayarlar"),
        ["Quit"] = ("Quit SweaxClip", "SweaxClip'ten çık"),
        ["PauseCapture"] = ("Pause capture", "Kaydı duraklat"),
        ["ResumeCapture"] = ("Resume capture", "Kayda devam et"),
        ["Paused"] = ("Paused", "Duraklatıldı"),

        ["Inspector"] = ("Details", "Ayrıntılar"),
        ["NoSelection"] = ("Select something to see it in full.", "Tamamını görmek için bir öğe seç."),
        ["FirstCopied"] = ("First copied", "İlk kopyalama"),
        ["LastUsed"] = ("Last used", "Son kullanım"),
        ["TimesCopied"] = ("Times copied", "Kopyalanma"),
        ["Source"] = ("Source", "Kaynak"),
        ["Window"] = ("Window", "Pencere"),
        ["Type"] = ("Type", "Tür"),
        ["Size"] = ("Size", "Boyut"),
        ["Recognised"] = ("Recognised text", "Tanınan metin"),
        ["OcrPending"] = ("Reading text in image…", "Görseldeki metin okunuyor…"),
        ["Missing"] = ("missing", "bulunamadı"),

        ["Storage"] = ("Archive", "Arşiv"),
        ["ClipsStored"] = ("{0} clips", "{0} kayıt"),
        ["Since"] = ("since {0}", "{0} tarihinden beri"),
        ["OnDisk"] = ("{0} on disk", "diskte {0}"),

        ["SetGrab"] = ("Copy text from screen", "Ekrandan metin kopyala"),
        ["SetGrabNote"] = ("Press the shortcut and drag over any text on screen — in a picture, a video, an app that won't let you select. It is read on your PC and copied.", "Kısayola bas, ekrandaki herhangi bir yazının üstünden sürükle — görselde, videoda, seçtirmeyen bir uygulamada. Bilgisayarında okunur ve kopyalanır."),
        ["GrabRecording"] = ("Press a shortcut…", "Kısayola bas…"),
        ["GrabNeedsModifier"] = ("Use Win, Ctrl or Alt together with a key.", "Bir tuşla birlikte Win, Ctrl veya Alt kullan."),
        ["GrabTaken"] = ("That shortcut already opens the panel.", "Bu kısayol zaten paneli açıyor."),
        ["GrabText"] = ("Copy text from screen", "Ekrandan metin kopyala"),
        ["GrabHint"] = ("Drag over the text you want to copy", "Kopyalamak istediğin yazının üstünden sürükle"),
        ["GrabCancel"] = ("cancel", "iptal"),
        ["GrabCopied"] = ("Copied", "Kopyalandı"),
        ["GrabWords"] = ("{0} words", "{0} kelime"),
        ["GrabNothing"] = ("No text found there", "Orada yazı bulunamadı"),
        ["SetAppearance"] = ("Appearance", "Görünüm"),
        ["SetPanelStyle"] = ("Win + V panel", "Win + V paneli"),
        ["SetPanelStyleNote"] = ("How your clips appear when you press the shortcut.", "Kısayola bastığında kayıtların nasıl görüneceği."),
        ["StyleTimeline"] = ("Timeline", "Zaman çizelgesi"),
        ["StyleTimelineNote"] = ("Many clips at once; the selected one opens up.", "Aynı anda çok kayıt; seçili olan açılır."),
        ["StyleDeck"] = ("Deck", "Deste"),
        ["StyleDeckNote"] = ("One card in front, the rest stacked behind.", "Önde bir kart, gerisi arkasında dizili."),
        ["SetTheme"] = ("Theme", "Tema"),
        ["SetThemeNote"] = ("Follow Windows, or always light or dark.", "Windows'u izle ya da hep açık veya koyu kalsın."),
        ["ThemeSystem"] = ("Windows", "Windows"),
        ["ThemeLight"] = ("Light", "Açık"),
        ["ThemeDark"] = ("Dark", "Koyu"),
        ["SetPalette"] = ("Colours", "Renkler"),
        ["SetPaletteNote"] = ("Changes the whole app at once, in light and dark.", "Uygulamanın tamamını anında değiştirir, açıkta da koyuda da."),
        ["SetGeneral"] = ("General", "Genel"),
        ["SetCapture"] = ("Capture", "Kayıt"),
        ["SetStorage"] = ("Storage", "Depolama"),
        ["SetAbout"] = ("About", "Hakkında"),
        ["SetLanguage"] = ("Language", "Dil"),
        ["SetLanguageAuto"] = ("Automatic", "Otomatik"),
        ["SetLanguageNote"] = ("Takes effect after restart.", "Yeniden başlatınca uygulanır."),
        ["SetStartup"] = ("Start with Windows", "Windows ile başlat"),
        ["SetStartupNote"] = ("Runs silently in the tray, ready the moment you press the shortcut.", "Tepside sessizce çalışır; kısayola bastığın an hazırdır."),
        ["SetHotkey"] = ("Shortcut", "Kısayol"),
        ["SetHotkeyNote"] = ("Win + V replaces the built-in Windows clipboard panel.", "Win + V, Windows'un kendi pano panelinin yerine geçer."),
        ["SetPastePlain"] = ("Paste plain text by default", "Varsayılan olarak düz metin yapıştır"),
        ["SetMoveToTop"] = ("Move pasted items to the top", "Yapıştırılanı en üste taşı"),
        ["SetCaptureText"] = ("Text, links, code and colors", "Metin, bağlantı, kod ve renkler"),
        ["SetCaptureImages"] = ("Images and screenshots", "Görseller ve ekran görüntüleri"),
        ["SetCaptureFiles"] = ("Copied files", "Kopyalanan dosyalar"),
        ["SetWatchShots"] = ("Watch the Screenshots folder", "Ekran görüntüleri klasörünü izle"),
        ["SetWatchShotsNote"] = ("Win + PrtScn and Snipping Tool saves land in your archive too.", "Win + PrtScn ve Ekran Alıntısı kayıtları da arşive düşer."),
        ["SetImportShots"] = ("Import existing screenshots", "Mevcut ekran görüntülerini içe aktar"),
        ["SetOcr"] = ("Read text inside images (offline)", "Görsellerdeki metni oku (çevrimdışı)"),
        ["SetOcrNote"] = ("Uses the Windows OCR engine on this PC. Nothing leaves your computer.", "Bu bilgisayardaki Windows OCR motorunu kullanır. Hiçbir şey dışarı çıkmaz."),
        ["SetPrivacy"] = ("Respect password managers", "Şifre yöneticilerine saygı göster"),
        ["SetPrivacyNote"] = ("Skips anything marked private (KeePass, 1Password, Bitwarden…).", "Gizli işaretlenen kopyaları atlar (KeePass, 1Password, Bitwarden…)."),
        ["SetIgnoredApps"] = ("Ignored apps", "Yok sayılan uygulamalar"),
        ["SetIgnoredAppsNote"] = ("One executable per line, e.g. keepass.exe", "Her satıra bir uygulama, ör. keepass.exe"),
        ["SetDataFolder"] = ("Archive location", "Arşiv konumu"),
        ["SetOpenFolder"] = ("Open folder", "Klasörü aç"),
        ["SetBackups"] = ("Daily backups", "Günlük yedek"),
        ["SetBackupNow"] = ("Back up now", "Şimdi yedekle"),
        ["SetExport"] = ("Export archive", "Arşivi dışa aktar"),
        ["SetBackupDone"] = ("Backup saved", "Yedek kaydedildi"),
        ["SetExportDone"] = ("Export finished", "Dışa aktarma bitti"),
        ["SetRetention"] = ("Nothing is ever deleted automatically. Trash is only emptied when you say so.", "Hiçbir şey kendiliğinden silinmez. Çöp sadece sen istediğinde boşalır."),
        ["SetVersion"] = ("Version {0}", "Sürüm {0}"),
        ["SetSource"] = ("GitHub", "GitHub"),
        ["SetMemory"] = ("Memory in use", "Kullanılan bellek"),
        ["Importing"] = ("Importing {0}/{1}…", "İçe aktarılıyor {0}/{1}…"),
        ["Imported"] = ("{0} screenshots imported", "{0} ekran görüntüsü içe aktarıldı"),

        ["TrayTip"] = ("SweaxClip — clipboard archive", "SweaxClip — pano arşivi"),
        ["Welcome"] = ("SweaxClip is running. Press Win + V any time.", "SweaxClip çalışıyor. İstediğin an Win + V'ye bas."),
        ["Back"] = ("Back", "Geri"),
        ["Close"] = ("Close", "Kapat"),
        ["Yes"] = ("Yes", "Evet"),
        ["Cancel"] = ("Cancel", "Vazgeç"),
    };
}

/// <summary>XAML: Text="{c:T SearchHint}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public TExtension() : this("") { }
    public string Key { get; set; } = key;
    public bool Upper { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) => Upper ? Loc.T(Key).ToUpper(Loc.Culture) : Loc.T(Key);
}
