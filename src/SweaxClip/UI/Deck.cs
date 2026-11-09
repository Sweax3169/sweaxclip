using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>
/// The quick-panel deck: the selected clip is the card in front, older clips stack behind it with only
/// their headers showing. Every card is driven by springs, so moving through history, dealing the deck
/// open, throwing a card at the caret or dropping it in the bin all share one physical feel.
/// </summary>
public sealed class Deck : Canvas
{
    public const double CardW = 392, CardH = 232, Step = 42;
    public const int Behind = 6;
    const double BottomPad = 6;

    readonly Dictionary<ClipItem, CardHost> _hosts = new(ReferenceEqualityComparer.Instance);
    readonly List<CardHost> _leaving = [];
    ClipListModel? _model;
    bool _ticking;
    int _deepest;
    Spring _top, _bottom;

    /// <summary>When set, the deck is advanced only by <see cref="StepFrame"/> (used to film the README).</summary>
    public bool ManualClock { get; set; }

    /// <summary>
    /// True: the front card rests on the bottom edge and the stack grows upward (the panel sits above the caret
    /// or the taskbar). False: the stack hangs from the top edge (the panel opened below the caret).
    /// </summary>
    public bool AnchorBottom { get; set; } = true;

    /// <summary>Top of the deepest card and bottom of the front card, in deck coordinates, as they move.</summary>
    public event Action<double, double>? EdgesChanged;

    /// <summary>The front card was clicked.</summary>
    public event Action<ClipItem>? Activated;

    public ClipListModel? Model
    {
        get => _model;
        set
        {
            if (_model != null) _model.PropertyChanged -= ModelChanged;
            _model = value;
            if (_model != null) _model.PropertyChanged += ModelChanged;
        }
    }

    public Deck()
    {
        ClipToBounds = false;
        Background = Brushes.Transparent;
        SizeChanged += (_, _) => Sync(Mode.Instant);
        MouseWheel += (_, e) =>
        {
            _model?.Move(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
    }

    void ModelChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipListModel.Selected)) Sync(Mode.Move);
        else if (e.PropertyName == nameof(ClipListModel.Rows)) Sync(Mode.Reshuffle);
    }

    public enum Mode { Instant, Move, Deal, Reshuffle }

    double FrontY => AnchorBottom ? Math.Max(0, ActualHeight - CardH - BottomPad) : Offset(_deepest);

    /// <summary>How far above the front card the card <paramref name="d"/> deep sits; each shows exactly its header.</summary>
    static double Offset(int d) => Step * (d - 0.042 * d * (d + 1) / 2);
    double Left => (ActualWidth - CardW) / 2;

    record struct Pose(double Y, double X, double Scale, double Opacity, double Rot, double Dim);

    Pose PoseFor(int depth)
    {
        double y0 = FrontY;
        if (depth < 0) return new Pose(y0 + 86, 0, 1.03, 0, -2.5, 0);
        if (depth == 0) return new Pose(y0, 0, 1, 1, 0, 0);
        int d = Math.Min(depth, Behind);
        double scale = 1 - 0.042 * d;
        double y = y0 - Offset(d);
        double opacity = depth >= Behind ? 0 : 1;
        return new Pose(y, 0, scale, opacity, 0, Math.Min(0.3, 0.055 * d));
    }

    /// <summary>Re-targets every card for the current selection.</summary>
    public void Sync(Mode mode)
    {
        if (_model == null || ActualHeight <= 0) return;
        var items = _model.Items;
        int sel = _model.Selected is { } s ? Math.Max(0, items.IndexOf(s)) : 0;
        _deepest = Math.Clamp(items.Count - 1 - sel, 0, Behind - 1);
        _top.T = FrontY - Offset(_deepest);
        _bottom.T = FrontY + CardH;
        if (mode is Mode.Instant or Mode.Deal) { _top.Snap(); _bottom.Snap(); EdgesChanged?.Invoke(_top.X, _bottom.X); }
        var wanted = new Dictionary<ClipItem, int>(ReferenceEqualityComparer.Instance);
        for (int i = Math.Max(0, sel - 1); i < Math.Min(items.Count, sel + Behind + 1); i++) wanted[items[i]] = i - sel;

        foreach (var (item, host) in _hosts.ToList())
        {
            if (wanted.ContainsKey(item)) continue;
            _hosts.Remove(item);
            if (mode is Mode.Instant || mode is Mode.Reshuffle && host.O.X < 0.05) { Children.Remove(host); continue; }
            var gone = host.Depth < 0 || mode == Mode.Reshuffle ? PoseFor(-1) : PoseFor(Behind + 1);
            if (mode == Mode.Reshuffle) gone = gone with { Y = host.Y.X + 30, Rot = 0 };
            host.Target(gone);
            host.Leaving = true;
            _leaving.Add(host);
        }

        foreach (var (item, depth) in wanted)
        {
            var pose = PoseFor(depth);
            if (!_hosts.TryGetValue(item, out var host))
            {
                host = new CardHost(item, Left) { Owner = this };
                host.MouseLeftButtonUp += Card_Click;
                Children.Add(host);
                _hosts[item] = host;
                Pose start = mode switch
                {
                    Mode.Deal or Mode.Reshuffle => PoseFor(0) with { Y = FrontY + 34, Scale = 0.965, Opacity = 0, Dim = 0 },
                    Mode.Move when depth < 0 => PoseFor(-1),
                    Mode.Move => PoseFor(Behind + 1),
                    _ => pose,
                };
                host.Place(start);
                host.Delay = mode is Mode.Deal or Mode.Reshuffle ? Math.Max(0, depth) * (mode == Mode.Deal ? 0.034 : 0.018) : 0;
            }
            host.Depth = depth;
            host.Target(pose);
            SetZIndex(host, 100 - depth);
            host.IsHitTestVisible = depth >= 0 && depth < Behind;
        }
        if (mode == Mode.Instant)
        {
            foreach (var h in _hosts.Values) h.Snap();
            foreach (var h in _leaving) Children.Remove(h);
            _leaving.Clear();
        }
        Kick();
    }

    void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CardHost h || _model == null) return;
        if (e.OriginalSource is DependencyObject d && d.FindParent<Button>() != null) return;
        e.Handled = true;
        if (h.Depth == 0) Activated?.Invoke(h.Item);
        else _model.Selected = h.Item;
    }

    /// <summary>Throws the front card towards <paramref name="target"/> (deck coordinates) and fades it.</summary>
    public void Fling(Point target)
    {
        if (_model?.Selected is not { } s || !_hosts.TryGetValue(s, out var h)) return;
        double dx = target.X - (Left + CardW / 2), dy = target.Y - (FrontY + CardH / 2);
        h.Stiffness = 520;
        h.Target(new Pose(FrontY + dy * 0.85, dx * 0.85, 0.18, 0, dx > 0 ? 6 : -6, 0));
        // the rest of the deck settles down and fades with the lid
        foreach (var other in _hosts.Values)
            if (!ReferenceEquals(other, h)) { other.Stiffness = 420; other.Y.T += 24; other.O.T = 0; }
        Kick();
    }

    /// <summary>Tips the front card over and drops it out of the deck.</summary>
    public void Discard(ClipItem item)
    {
        if (!_hosts.TryGetValue(item, out var h)) return;
        _hosts.Remove(item);
        h.Leaving = true;
        h.Stiffness = 160;
        h.Target(new Pose(h.Y.X + 150, 30, 0.92, 0, 11, 0));
        _leaving.Add(h);
        Kick();
    }

    /// <summary>A little hop, e.g. when a card gets pinned.</summary>
    public void Bump(ClipItem item)
    {
        if (_hosts.TryGetValue(item, out var h)) { h.S.V += 1.6; h.Y.V -= 260; Kick(); }
    }

    void Kick()
    {
        if (_ticking || ManualClock) return;
        _ticking = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += Tick;
    }

    TimeSpan _last;

    void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0.001, 1 / 30.0);
        if (now == _last) return;
        _last = now;
        if (!StepFrame(dt))
        {
            CompositionTarget.Rendering -= Tick;
            _ticking = false;
        }
    }

    /// <summary>Advances every spring by <paramref name="dt"/> seconds. Returns false once everything has settled.</summary>
    public bool StepFrame(double dt)
    {
        bool moving = _top.Step(dt, 330, 0.85) | _bottom.Step(dt, 330, 0.85);
        EdgesChanged?.Invoke(_top.X, _bottom.X);
        foreach (var h in _hosts.Values) moving |= h.Step(dt);
        for (int i = _leaving.Count - 1; i >= 0; i--)
        {
            var h = _leaving[i];
            if (!h.Step(dt) || h.O.X < 0.01)
            {
                Children.Remove(h);
                _leaving.RemoveAt(i);
            }
            else moving = true;
        }
        return moving;
    }

    public void Clear()
    {
        Children.Clear();
        _hosts.Clear();
        _leaving.Clear();
    }

    // ── a card on the deck ──────────────────────────────────────────────────

    sealed class CardHost : Grid
    {
        public readonly ClipItem Item;
        public Deck? Owner;
        public int Depth;
        public bool Leaving;
        public double Delay;
        public double Stiffness = 330;
        public Spring Y, X, S, O, R, D;

        readonly ScaleTransform _scale = new();
        readonly RotateTransform _rot = new();
        readonly TranslateTransform _move = new();
        readonly Border _dim;

        public CardHost(ClipItem item, double left)
        {
            Item = item;
            DataContext = item;
            Width = CardW;
            Height = CardH;
            Cursor = Cursors.Hand;
            SetLeft(this, left);
            _scale.CenterX = CardW / 2;
            _rot.CenterX = CardW / 2;
            _rot.CenterY = CardH / 2;
            RenderTransform = new TransformGroup { Children = { _scale, _rot, _move } };
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = 0.3, Color = Color.FromRgb(0x1A, 0x10, 0x08), RenderingBias = RenderingBias.Performance };
            Children.Add(new ContentPresenter
            {
                Content = item,
                ContentTemplateSelector = (DataTemplateSelector)ThreadResources.Find("Cards")!,
            });
            _dim = new Border { CornerRadius = new CornerRadius(14), IsHitTestVisible = false, Opacity = 0 };
            _dim.SetResourceReference(Border.BackgroundProperty, "ShadowTint");
            Children.Add(_dim);
            if (ThreadResources.Find("ClipMenu") is ContextMenu menu) ContextMenu = menu;
            MouseEnter += (_, _) => Owner?.Kick();
            MouseLeave += (_, _) => Owner?.Kick();
        }

        public void Place(Pose p)
        {
            Y = new Spring(p.Y); X = new Spring(p.X); S = new Spring(p.Scale); O = new Spring(p.Opacity); R = new Spring(p.Rot); D = new Spring(p.Dim);
            Apply();
        }

        public void Target(Pose p)
        {
            Y.T = p.Y; X.T = p.X; S.T = p.Scale; O.T = p.Opacity; R.T = p.Rot; D.T = p.Dim;
        }

        public void Snap()
        {
            Y.Snap(); X.Snap(); S.Snap(); O.Snap(); R.Snap(); D.Snap();
            Delay = 0;
            Apply();
        }

        public bool Step(double dt)
        {
            if (Delay > 0) { Delay -= dt; return true; }
            double lift = !Leaving && Depth > 0 && IsMouseOver ? -9 : 0;
            double k = Stiffness;
            bool a = Y.Step(dt, k, 0.74, lift);
            bool b = X.Step(dt, k, 0.8);
            bool c = S.Step(dt, k, 0.7);
            bool d = O.Step(dt, k * 1.6, 1.0);
            bool e = R.Step(dt, k, 0.62);
            bool f = D.Step(dt, k, 1.0);
            Apply();
            return a | b | c | d | e | f;
        }

        void Apply()
        {
            _move.Y = Y.X;
            _move.X = X.X;
            _scale.ScaleX = _scale.ScaleY = Math.Max(0.05, S.X);
            _rot.Angle = R.X;
            Opacity = Math.Clamp(O.X, 0, 1);
            _dim.Opacity = Math.Clamp(D.X, 0, 0.6);
        }
    }

    /// <summary>A damped spring for one property.</summary>
    struct Spring(double x)
    {
        public double X = x, V = 0, T = x;

        public void Snap() { X = T; V = 0; }

        /// <returns>True while still moving.</returns>
        public bool Step(double dt, double stiffness, double dampingRatio, double offset = 0)
        {
            double target = T + offset;
            double c = 2 * dampingRatio * Math.Sqrt(stiffness);
            // two half steps for stability at 30 fps
            for (int i = 0; i < 2; i++)
            {
                double h = dt / 2;
                double a = (target - X) * stiffness - V * c;
                V += a * h;
                X += V * h;
            }
            if (Math.Abs(target - X) < 0.0015 && Math.Abs(V) < 0.01) { X = target; V = 0; return false; }
            return true;
        }
    }
}
