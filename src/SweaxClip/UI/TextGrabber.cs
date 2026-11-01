using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SweaxClip.Core;
using SweaxClip.Native;

namespace SweaxClip.UI;

/// <summary>
/// "Copy text from screen": the screen freezes under a dimmed layer, you drag over any text — in a picture,
/// a video, an app that won't let you select — and the Windows OCR engine reads it onto the clipboard.
/// </summary>
public static class TextGrabber
{
    static List<GrabOverlay>? _open;

    public static bool Active => _open != null;

    public static void Start()
    {
        if (_open != null) return;
        var monitors = Win32.Monitors();
        if (monitors.Count == 0) return;
        Win32.GetCursorPos(out var mouse);

        // take every picture first, so no overlay ends up in another monitor's shot
        var shots = new List<(Win32.RECT rect, BitmapSource shot)>();
        foreach (var r in monitors)
        {
            try { shots.Add((r, Win32.CaptureScreen(r))); }
            catch (Exception ex) { Paths.Log("grab capture: " + ex.Message); }
        }
        if (shots.Count == 0) return;

        _open = [];
        foreach (var (rect, shot) in shots)
        {
            var o = new GrabOverlay(rect, shot);
            o.Picked += Picked;
            o.Cancelled += Close;
            o.Deactivated += (_, _) => Application.Current.Dispatcher.BeginInvoke(CloseIfLeft, DispatcherPriority.Background);
            _open.Add(o);
        }
        foreach (var o in _open) o.ShowOver();
        var under = _open.FirstOrDefault(o => o.Contains(mouse)) ?? _open[0];
        under.ShowHint();
        Win32.ForceForeground(new WindowInteropHelper(under).Handle);
        under.Activate();
        under.Focus();
    }

    /// <summary>Clicking another monitor's overlay keeps the grab going; switching to anything else ends it.</summary>
    static void CloseIfLeft()
    {
        if (_open == null) return;
        IntPtr fg = Win32.GetForegroundWindow();
        if (!_open.Any(o => new WindowInteropHelper(o).Handle == fg)) Close();
    }

    static void Close()
    {
        var open = _open;
        _open = null;
        if (open == null) return;
        foreach (var o in open) o.Close();
        // the frozen monitor pictures are tens of MB: hand them back once the overlays are gone
        (Application.Current as App)?.ScheduleTrim();
    }

    static async void Picked(BitmapSource crop, Win32.RECT where)
    {
        Close();
        string text = "";
        try { text = (await OcrService.ReadScreenAsync(crop)).Trim(); }
        catch (Exception ex) { Paths.Log("grab ocr: " + ex.Message); }

        if (text.Length > 0)
        {
            (Application.Current as App)?.KeepNextOwnCopy();
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); break; }
                catch { await Task.Delay(60); }
            }
        }
        GrabToast.Show(where, text);
    }
}

/// <summary>One monitor, frozen: the picture of the screen under a dim layer, and the selection cut out of it.</summary>
internal sealed class GrabOverlay : Window
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new QuietPeer(this, "SweaxClip");

    readonly Win32.RECT _rect;
    readonly BitmapSource _shot;
    readonly Canvas _canvas = new() { IsHitTestVisible = false };
    readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x96, 0x08, 0x08, 0x0A)), IsHitTestVisible = false };
    readonly Rectangle _frame = new() { Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, Visibility = Visibility.Collapsed };
    readonly Path _marks = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square, Visibility = Visibility.Collapsed };
    readonly Border _size;
    readonly TextBlock _sizeText = new() { FontSize = 11.5, Foreground = Brushes.White, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
    readonly Border _hint;
    Point? _start;
    Rect _sel;

    public event Action<BitmapSource, Win32.RECT>? Picked;

    /// <summary>For recordings: stay where WPF puts the window instead of covering the real monitor.</summary>
    internal bool Offscreen { get; init; }
    public event Action? Cancelled;

    public GrabOverlay(Win32.RECT rect, BitmapSource shot)
    {
        _rect = rect;
        _shot = shot;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; Width = 200; Height = 200;
        UseLayoutRounding = true;
        _marks.SetResourceReference(Shape.StrokeProperty, "Accent");

        _size = new Border { Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x10, 0x12)), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Child = _sizeText, Visibility = Visibility.Collapsed };
        _hint = BuildHint();

        var grid = new Grid();
        grid.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
        grid.Children.Add(_dim);
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_marks);
        _canvas.Children.Add(_size);
        grid.Children.Add(_canvas);
        grid.Children.Add(_hint);
        Content = grid;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long ex = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)((ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW));
            Place();
        };
        Loaded += (_, _) => { Place(); Update(); };
        DpiChanged += (_, _) => Place();
        SizeChanged += (_, _) => Update();

        MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(this);
            _sel = new Rect(_start.Value, _start.Value);
            CaptureMouse();
            _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            Update();
        };
        MouseMove += (_, e) =>
        {
            if (_start is not { } s) return;
            _sel = new Rect(s, e.GetPosition(this));
            Update();
        };
        MouseLeftButtonUp += (_, _) =>
        {
            if (_start == null) return;
            _start = null;
            ReleaseMouseCapture();
            Finish();
        };
        MouseRightButtonUp += (_, _) => Cancelled?.Invoke();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancelled?.Invoke(); };
    }

    /// <summary>Physical pixels per WPF unit on this monitor.</summary>
    double Scale => ActualWidth > 0 ? _shot.PixelWidth / ActualWidth : 1;

    public bool Contains(Win32.POINT p) => p.X >= _rect.Left && p.X < _rect.Right && p.Y >= _rect.Top && p.Y < _rect.Bottom;

    public void ShowOver()
    {
        Show();
        Place();
    }

    public void ShowHint()
    {
        _hint.Visibility = Visibility.Visible;
        _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(80) });
    }

    /// <summary>Exactly over the monitor, in physical pixels (WPF may resize on a DPI change; this undoes it).</summary>
    void Place()
    {
        if (Offscreen) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, _rect.Left, _rect.Top, _rect.Width, _rect.Height, Win32.SWP_NOACTIVATE);
    }

    void Update()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        var full = new RectangleGeometry(new Rect(0, 0, w, h));
        bool any = _start != null && _sel.Width >= 1 && _sel.Height >= 1;
        if (!any)
        {
            _dim.Data = full;
            _frame.Visibility = _marks.Visibility = _size.Visibility = Visibility.Collapsed;
            return;
        }
        _dim.Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { full, new RectangleGeometry(_sel) } };

        _frame.Visibility = _marks.Visibility = _size.Visibility = Visibility.Visible;
        Canvas.SetLeft(_frame, _sel.X - 0.5);
        Canvas.SetTop(_frame, _sel.Y - 0.5);
        _frame.Width = _sel.Width + 1;
        _frame.Height = _sel.Height + 1;

        // the app's crop marks, on the corners of the selection
        double m = Math.Min(14, Math.Min(_sel.Width, _sel.Height) / 2), x0 = _sel.Left - 2, y0 = _sel.Top - 2, x1 = _sel.Right + 2, y1 = _sel.Bottom + 2;
        _marks.Data = Geometry.Parse(FormattableString.Invariant(
            $"M{x0},{y0 + m} L{x0},{y0} L{x0 + m},{y0} M{x1 - m},{y0} L{x1},{y0} L{x1},{y0 + m} M{x1},{y1 - m} L{x1},{y1} L{x1 - m},{y1} M{x0 + m},{y1} L{x0},{y1} L{x0},{y1 - m}"));

        double s = Scale;
        _sizeText.Text = $"{Math.Round(_sel.Width * s)} × {Math.Round(_sel.Height * s)}";
        _size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lx = Math.Min(_sel.Right - _size.DesiredSize.Width, w - _size.DesiredSize.Width - 6);
        double ly = _sel.Bottom + 8 + _size.DesiredSize.Height > h ? _sel.Bottom - _size.DesiredSize.Height - 8 : _sel.Bottom + 8;
        Canvas.SetLeft(_size, Math.Max(6, lx));
        Canvas.SetTop(_size, ly);
    }

    /// <summary>Shows a selection as if it had just been dragged, and returns the picture under it (recordings).</summary>
    internal BitmapSource Select(Rect sel)
    {
        _start = sel.TopLeft;
        _sel = sel;
        Update();
        double k = Scale;
        var crop = new CroppedBitmap(_shot, new Int32Rect((int)(sel.X * k), (int)(sel.Y * k), (int)(sel.Width * k), (int)(sel.Height * k)));
        crop.Freeze();
        return crop;
    }

    void Finish()
    {
        double s = Scale;
        int x = (int)Math.Round(_sel.X * s), y = (int)Math.Round(_sel.Y * s);
        int w = (int)Math.Round(_sel.Width * s), h = (int)Math.Round(_sel.Height * s);
        x = Math.Clamp(x, 0, _shot.PixelWidth - 1);
        y = Math.Clamp(y, 0, _shot.PixelHeight - 1);
        w = Math.Min(w, _shot.PixelWidth - x);
        h = Math.Min(h, _shot.PixelHeight - y);
        if (w < 6 || h < 6) { Update(); return; } // a click, not a drag: keep waiting
        var crop = new CroppedBitmap(_shot, new Int32Rect(x, y, w, h));
        crop.Freeze();
        Picked?.Invoke(crop, new Win32.RECT { Left = _rect.Left + x, Top = _rect.Top + y, Right = _rect.Left + x + w, Bottom = _rect.Top + y + h });
    }

    static Border BuildHint()
    {
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) };
        dot.SetResourceReference(Shape.FillProperty, "Accent");
        var text = new TextBlock { Text = Loc.T("GrabHint"), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var cap = new Border { Margin = new Thickness(16, 0, 6, 0), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1, 1, 1, 2), Padding = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Esc", FontSize = 11.5, FontWeight = FontWeights.SemiBold } };
        cap.SetResourceReference(Border.BackgroundProperty, "Field");
        cap.SetResourceReference(Border.BorderBrushProperty, "Line2");
        ((TextBlock)cap.Child).SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
        var cancel = new TextBlock { Text = Loc.T("GrabCancel"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        cancel.SetResourceReference(TextBlock.ForegroundProperty, "Ink3");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text, cap, cancel } };
        var hint = new Border
        {
            Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 9, 16, 10), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 28, 0, 0),
            Visibility = Visibility.Collapsed, Opacity = 0, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black },
        };
        hint.SetResourceReference(Border.BackgroundProperty, "Surface");
        hint.SetResourceReference(Border.BorderBrushProperty, "Line2");
        TextElement.SetFontFamily(hint, new FontFamily("Segoe UI"));
        return hint;
    }
}

/// <summary>A small note beside the selection: what was copied, or that nothing was found.</summary>
internal static class GrabToast
{
    static Window? _current;

    public static void Show(Win32.RECT near, string text)
    {
        _current?.Close();
        var chip = Chip(text);
        bool found = text.Length > 0;

        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true, ShowActivated = false, SizeToContent = SizeToContent.WidthAndHeight,
            Left = -32000, Top = -32000, Content = chip, FontFamily = new FontFamily("Segoe UI"), Opacity = 0,
        };
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            long ex = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
        };
        w.Show();
        w.UpdateLayout();

        // below the selection (or above it near the bottom), kept on the selection's monitor
        var hwnd = new WindowInteropHelper(w).Handle;
        Win32.GetWindowRect(hwnd, out var size);
        IntPtr mon = Win32.MonitorFromPoint(new Win32.POINT { X = near.Left, Y = near.Top }, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(mon, ref mi);
        var work = mi.rcWork;
        int x = Math.Clamp(near.Left - 16, work.Left, Math.Max(work.Left, work.Right - size.Width));
        int y = near.Bottom - 8;
        if (y + size.Height > work.Bottom) y = near.Top - size.Height + 8;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - size.Height));
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

        w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        _current = w;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(found ? 2400 : 1800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => { w.Close(); if (ReferenceEquals(_current, w)) _current = null; };
            w.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        timer.Start();
    }

    /// <summary>The note itself: a check and the first words copied, or "no text found".</summary>
    internal static Border Chip(string text)
    {
        bool found = text.Length > 0;
        int words = found ? text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length : 0;

        var mark = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        mark.SetResourceReference(Border.BackgroundProperty, found ? "Accent" : "Field");
        var glyph = new TextBlock { Text = found ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, found ? "AccentInk" : "Ink2");
        mark.Child = glyph;

        var title = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Text = found ? $"{Loc.T("GrabCopied")} · {Loc.Format("GrabWords", words)}" : Loc.T("GrabNothing") };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var body = new StackPanel { Children = { title } };
        if (found)
        {
            var preview = new TextBlock { Text = text.ReplaceLineEndings(" "), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 38, MaxWidth = 320, Margin = new Thickness(0, 3, 0, 0) };
            preview.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            body.Children.Add(preview);
        }

        var chip = new Border
        {
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, body } },
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 16, 11), BorderThickness = new Thickness(1), Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35, Color = Colors.Black },
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Surface");
        chip.SetResourceReference(Border.BorderBrushProperty, "Line2");
        return chip;
    }
}
