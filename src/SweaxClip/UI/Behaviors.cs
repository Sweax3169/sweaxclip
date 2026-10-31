using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>Routed commands shared by the quick panel and the library; windows decide what they mean.</summary>
public static class Cmds
{
    public static readonly RoutedUICommand Paste = new("Paste", nameof(Paste), typeof(Cmds));
    public static readonly RoutedUICommand PastePlain = new("PastePlain", nameof(PastePlain), typeof(Cmds));
    public static readonly RoutedUICommand Copy = new("Copy", nameof(Copy), typeof(Cmds));
    public static readonly RoutedUICommand TogglePin = new("TogglePin", nameof(TogglePin), typeof(Cmds));
    public static readonly RoutedUICommand Delete = new("Delete", nameof(Delete), typeof(Cmds));
    public static readonly RoutedUICommand Restore = new("Restore", nameof(Restore), typeof(Cmds));
    public static readonly RoutedUICommand DeleteForever = new("DeleteForever", nameof(DeleteForever), typeof(Cmds));
    public static readonly RoutedUICommand Open = new("Open", nameof(Open), typeof(Cmds));
    public static readonly RoutedUICommand ShowInFolder = new("ShowInFolder", nameof(ShowInFolder), typeof(Cmds));
    public static readonly RoutedUICommand SaveImage = new("SaveImage", nameof(SaveImage), typeof(Cmds));
    public static readonly RoutedUICommand CopyOcr = new("CopyOcr", nameof(CopyOcr), typeof(Cmds));
    public static readonly RoutedUICommand Select = new("Select", nameof(Select), typeof(Cmds));
    public static readonly RoutedUICommand SetScope = new("SetScope", nameof(SetScope), typeof(Cmds));
}

/// <summary>Picks the body template for a clip by its kind ("Body.Text", "Body.Image", …).</summary>
public sealed class BodySelector : DataTemplateSelector
{
    public string Prefix { get; set; } = "Body.";
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ClipItem c || container is not FrameworkElement fe) return null;
        string key = c.Kind switch
        {
            ClipKind.Image or ClipKind.Screenshot => "Image",
            ClipKind.Files when c.Thumb != null => "FilesVisual",
            _ => c.Kind.ToString(),
        };
        return fe.TryFindResource(Prefix + key) as DataTemplate ?? fe.TryFindResource(Prefix + "Text") as DataTemplate;
    }
}

/// <summary>
/// Lazily decodes thumbnails for the clip an Image is bound to, off the UI thread, at the size it is drawn.
/// </summary>
public static class ThumbLoader
{
    public static readonly DependencyProperty WidthProperty = DependencyProperty.RegisterAttached(
        "Width", typeof(double), typeof(ThumbLoader), new PropertyMetadata(0.0, OnWidthChanged));
    public static void SetWidth(DependencyObject d, double v) => d.SetValue(WidthProperty, v);
    public static double GetWidth(DependencyObject d) => (double)d.GetValue(WidthProperty);

    /// <summary>Load the original image rather than the thumbnail.</summary>
    public static readonly DependencyProperty FullProperty = DependencyProperty.RegisterAttached(
        "Full", typeof(bool), typeof(ThumbLoader), new PropertyMetadata(false));
    public static void SetFull(DependencyObject d, bool v) => d.SetValue(FullProperty, v);
    public static bool GetFull(DependencyObject d) => (bool)d.GetValue(FullProperty);

    static readonly SemaphoreSlim Gate = new(2);
    static readonly LinkedList<(string key, ImageSource img)> Lru = new();
    const int LruMax = 40;

    static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image img) return;
        img.DataContextChanged -= Refresh;
        img.DataContextChanged += Refresh;
        img.Loaded -= Loaded;
        img.Loaded += Loaded;
        Request(img);
    }

    static void Loaded(object sender, RoutedEventArgs e) => Request((Image)sender);
    static void Refresh(object sender, DependencyPropertyChangedEventArgs e) => Request((Image)sender);

    static void Request(Image img)
    {
        if (img.DataContext is not ClipItem item) { img.Source = null; return; }
        bool full = GetFull(img);
        string? rel = full ? item.Blob ?? item.Thumb : item.Thumb;
        if (rel == null) { img.Source = null; return; }

        double scale = 1;
        try { scale = VisualTreeHelper.GetDpi(img).DpiScaleX; } catch { }
        int px = (int)Math.Ceiling(GetWidth(img) * scale);
        if (!full) px = Math.Min(px, ImageTools.ThumbWidth);
        if (full && item.Width > 0) px = Math.Min(px, item.Width);
        string key = rel + "@" + px;

        lock (Lru)
        {
            var node = Lru.First;
            while (node != null && node.Value.key != key) node = node.Next;
            if (node != null)
            {
                Lru.Remove(node);
                Lru.AddFirst(node);
                img.Source = node.Value.img;
                return;
            }
        }

        img.Source = null;
        img.Tag = key;
        var dispatcher = img.Dispatcher;
        _ = Task.Run(async () =>
        {
            await Gate.WaitAsync();
            ImageSource? src;
            try { src = ImageTools.Load(rel, px); }
            finally { Gate.Release(); }
            if (src == null) return;
            lock (Lru)
            {
                Lru.AddFirst((key, src));
                while (Lru.Count > LruMax) Lru.RemoveLast();
            }
            _ = dispatcher.BeginInvoke(() =>
            {
                if (Equals(img.Tag, key)) img.Source = src;
            }, DispatcherPriority.Background);
        });
    }

    public static void Clear()
    {
        lock (Lru) Lru.Clear();
    }
}

/// <summary>Clips an element to a rounded rectangle that follows its size.</summary>
public static class Round
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(Round), new PropertyMetadata(0.0, (d, _) =>
        {
            if (d is not FrameworkElement fe) return;
            fe.SizeChanged -= Update;
            fe.SizeChanged += Update;
            Apply(fe);
        }));
    public static void SetRadius(DependencyObject d, double v) => d.SetValue(RadiusProperty, v);
    public static double GetRadius(DependencyObject d) => (double)d.GetValue(RadiusProperty);

    static void Update(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    static void Apply(FrameworkElement fe)
    {
        double r = GetRadius(fe);
        var g = new RectangleGeometry(new Rect(fe.RenderSize), r, r);
        g.Freeze();
        fe.Clip = g;
    }
}

public sealed class BoolToVis : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value switch { bool x => x, int i => i != 0, string s => !string.IsNullOrEmpty(s), null => false, _ => true };
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class UpperConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value as string)?.ToUpper(Loc.Culture) ?? "";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public static class VisualTreeExt
{
    public static T? FindChild<T>(this DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (c.FindChild<T>() is { } r) return r;
        }
        return null;
    }

    public static T? FindParent<T>(this DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
