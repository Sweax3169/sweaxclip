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

        ["ScopeAll"] = ("Everything", "Hepsi"),
        ["ScopeText"] = ("Text", "Metinler"),
        ["ScopeLinks"] = ("Links", "Bağlantılar"),
        ["ScopeImages"] = ("Images", "Görseller"),
        ["ScopeScreenshots"] = ("Screenshots", "Ekran görüntüleri"),
        ["ScopeFiles"] = ("Files", "Dosyalar"),
        ["ScopeCode"] = ("Code", "Kod"),
        ["ScopeColors"] = ("Colors", "Renkler"),
        ["ScopePinned"] = ("Pinned", "Sabitlenenler"),
        ["ScopeTrash"] = ("Trash", "Çöp kutusu"),

        ["ShortAll"] = ("ALL", "HEPSİ"),
        ["ShortText"] = ("TEXT", "METİN"),
        ["ShortLinks"] = ("LINKS", "LİNK"),
        ["ShortImages"] = ("IMAGES", "GÖRSEL"),
        ["ShortScreenshots"] = ("SHOTS", "SS"),
        ["ShortFiles"] = ("FILES", "DOSYA"),
        ["ShortCode"] = ("CODE", "KOD"),
        ["ShortColors"] = ("COLORS", "RENK"),
        ["ShortPinned"] = ("PINNED", "SABİT"),
        ["ShortTrash"] = ("TRASH", "ÇÖP"),

        ["SearchHint"] = ("Search everything you ever copied…", "Kopyaladığın her şeyde ara…"),
        ["SearchHintShort"] = ("Type to search", "Aramak için yaz"),
        ["EmptyTitle"] = ("Nothing here yet", "Henüz burada bir şey yok"),
        ["EmptyBody"] = ("Copy anything — it stays here for good.", "Bir şey kopyala — burada kalıcı olarak durur."),
        ["NoResults"] = ("No matches", "Eşleşme yok"),
        ["NoResultsBody"] = ("Try fewer letters, or another category.", "Daha kısa yaz ya da başka kategoriye bak."),
        ["TrashEmpty"] = ("Trash is empty", "Çöp kutusu boş"),

        ["HintPaste"] = ("paste", "yapıştır"),
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
        ["Paused"] = ("PAUSED", "DURAKLATILDI"),

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

        ["SetGeneral"] = ("General", "Genel"),
        ["SetCapture"] = ("Capture", "Kayıt"),
        ["SetStorage"] = ("Storage & backup", "Depolama ve yedek"),
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
        ["SetDataFolder"] = ("Archive folder", "Arşiv klasörü"),
        ["SetOpenFolder"] = ("Open folder", "Klasörü aç"),
        ["SetBackups"] = ("Daily snapshot of the database", "Veritabanının günlük yedeği"),
        ["SetBackupNow"] = ("Back up now", "Şimdi yedekle"),
        ["SetExport"] = ("Export everything (.zip)", "Her şeyi dışa aktar (.zip)"),
        ["SetBackupDone"] = ("Backup saved", "Yedek kaydedildi"),
        ["SetExportDone"] = ("Export finished", "Dışa aktarma bitti"),
        ["SetRetention"] = ("Nothing is ever deleted automatically. Trash is only emptied when you say so.", "Hiçbir şey kendiliğinden silinmez. Çöp sadece sen istediğinde boşalır."),
        ["SetVersion"] = ("Version {0}", "Sürüm {0}"),
        ["SetSource"] = ("Source code on GitHub", "GitHub'daki kaynak kod"),
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
