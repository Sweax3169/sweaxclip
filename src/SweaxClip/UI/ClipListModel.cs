using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SweaxClip.Core;

namespace SweaxClip.UI;

/// <summary>Shared browsing state: scope, search, the flattened row list (date headers + clips) and the selection.</summary>
public sealed class ClipListModel : Observable
{
    readonly ClipStore _store;
    readonly DispatcherTimer _searchDelay;
    int _version;
    bool _hasMore, _loadingMore;
    const int PageSize = 150;

    public ObservableCollection<ScopeInfo> Scopes { get; }
    public List<ClipItem> Items { get; } = [];

    ObservableCollection<object> _rows = [];
    public ObservableCollection<object> Rows { get => _rows; private set => Set(ref _rows, value); }

    public ClipListModel(ClipStore store, bool library)
    {
        _store = store;
        var scopes = new List<Scope> { Scope.All, Scope.Text, Scope.Links, Scope.Images, Scope.Screenshots, Scope.Files, Scope.Code, Scope.Colors, Scope.Pinned };
        if (library) scopes.Add(Scope.Trash);
        int n = 1;
        Scopes = new(scopes.Select(s => new ScopeInfo { Scope = s, Glyph = Glyphs.ForScope(s), Number = n++, Active = s == Scope.All }));
        _searchDelay = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(110) };
        _searchDelay.Tick += async (_, _) => { _searchDelay.Stop(); await Reload(false); };
    }

    Scope _scope = Scope.All;
    public Scope Scope
    {
        get => _scope;
        set
        {
            if (!Set(ref _scope, value)) return;
            foreach (var s in Scopes) s.Active = s.Scope == value;
            Raise(nameof(ScopeTitle));
            Raise(nameof(UseGrid));
            _ = Reload(false);
        }
    }

    public string ScopeTitle => Loc.ScopeName(_scope);

    string _search = "";
    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? "")) return;
            _searchDelay.Stop();
            _searchDelay.Start();
        }
    }

    ClipItem? _selected;
    public ClipItem? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (_selected != null) _selected.IsSelected = false;
            _selected = value;
            if (_selected != null) _selected.IsSelected = true;
            Raise();
        }
    }

    bool _gridPreferred = true;
    public bool GridPreferred { get => _gridPreferred; set { if (Set(ref _gridPreferred, value)) { Raise(nameof(UseGrid)); Rebuild(); } } }
    public bool AllowGrid { get; init; }
    public bool UseGrid => AllowGrid && _gridPreferred && _scope is Scope.Images or Scope.Screenshots;

    int _columns = 4;
    public int Columns
    {
        get => _columns;
        set
        {
            value = Math.Clamp(value, 1, 12);
            if (Set(ref _columns, value) && UseGrid) Rebuild();
        }
    }

    bool _isEmpty; public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }
    string _emptyTitle = ""; public string EmptyTitle { get => _emptyTitle; private set => Set(ref _emptyTitle, value); }
    string _emptyBody = ""; public string EmptyBody { get => _emptyBody; private set => Set(ref _emptyBody, value); }

    int _total; public int Total { get => _total; private set => Set(ref _total, value); }

    public async Task Reload(bool keepSelection = true)
    {
        int v = ++_version;
        var scope = _scope;
        var search = _search;
        List<ClipItem> items;
        try { items = await Task.Run(() => _store.Query(scope, search, PageSize, 0)); }
        catch (Exception ex) { Paths.Log("query: " + ex.Message); return; }
        if (v != _version) return;

        long? keepId = keepSelection ? _selected?.Id : null;
        Items.Clear();
        Items.AddRange(items);
        _hasMore = items.Count == PageSize;
        Rebuild();
        Selected = (keepId != null ? Items.FirstOrDefault(i => i.Id == keepId) : null) ?? Items.FirstOrDefault();

        IsEmpty = Items.Count == 0;
        if (IsEmpty)
        {
            (EmptyTitle, EmptyBody) = _search.Length > 0 ? (Loc.T("NoResults"), Loc.T("NoResultsBody"))
                : _scope == Scope.Trash ? (Loc.T("TrashEmpty"), "")
                : (Loc.T("EmptyTitle"), Loc.T("EmptyBody"));
        }
        await RefreshCounts();
    }

    public async Task LoadMore()
    {
        if (!_hasMore || _loadingMore) return;
        _loadingMore = true;
        int v = _version;
        try
        {
            var scope = _scope;
            var search = _search;
            int offset = Items.Count;
            var more = await Task.Run(() => _store.Query(scope, search, PageSize, offset));
            if (v != _version) return;
            _hasMore = more.Count == PageSize;
            var known = Items.Select(i => i.Id).ToHashSet();
            more = more.Where(m => !known.Contains(m.Id)).ToList();
            if (more.Count == 0) return;
            Items.AddRange(more);
            if (UseGrid) { Rebuild(); return; }
            DateTime? lastDay = Items.Count > more.Count ? DayOf(Items[Items.Count - more.Count - 1]) : null;
            foreach (var c in more)
            {
                var day = DayOf(c);
                if (day != lastDay) { Rows.Add(MakeHeader(day)); lastDay = day; }
                Rows.Add(c);
            }
        }
        finally { _loadingMore = false; }
    }

    DateTime DayOf(ClipItem c) => TimeText.Local(_scope == Scope.Trash && c.Deleted > 0 ? c.Deleted : c.Used).Date;

    static HeaderRow MakeHeader(DateTime day)
    {
        var (label, sub) = TimeText.DayHeader(day);
        return new HeaderRow { Label = label, Sub = sub };
    }

    void Rebuild()
    {
        var rows = new ObservableCollection<object>();
        int idx = 1;
        foreach (var c in Items) c.Index = idx <= 9 ? idx++ : 0;

        if (UseGrid)
        {
            foreach (var g in Items.GroupBy(DayOf))
            {
                rows.Add(MakeHeader(g.Key));
                foreach (var chunk in g.Chunk(_columns)) rows.Add(new GridRow { Items = chunk, Columns = _columns });
            }
        }
        else
        {
            DateTime? last = null;
            foreach (var c in Items)
            {
                var day = DayOf(c);
                if (day != last) { rows.Add(MakeHeader(day)); last = day; }
                rows.Add(c);
            }
        }
        Rows = rows;
    }

    public async Task RefreshCounts()
    {
        try
        {
            var counts = await Task.Run(_store.Counts);
            foreach (var s in Scopes) s.Count = counts[s.Scope];
            Total = counts[Scope.All];
        }
        catch { }
    }

    public void Move(int delta)
    {
        if (Items.Count == 0) return;
        int i = _selected == null ? -1 : Items.IndexOf(_selected);
        i = Math.Clamp(i + delta, 0, Items.Count - 1);
        Selected = Items[i];
        if (i > Items.Count - 20) _ = LoadMore();
    }

    public void MoveScope(int delta)
    {
        int i = Scopes.ToList().FindIndex(s => s.Scope == _scope);
        i = (i + delta + Scopes.Count) % Scopes.Count;
        Scope = Scopes[i].Scope;
    }

    /// <summary>Removes a clip from the visible list without a reload (used after delete/restore).</summary>
    public void Drop(ClipItem item)
    {
        int i = Items.IndexOf(item);
        if (i < 0) return;
        Items.RemoveAt(i);
        bool wasSelected = ReferenceEquals(item, _selected);
        Rebuild();
        if (wasSelected) Selected = Items.Count == 0 ? null : Items[Math.Min(i, Items.Count - 1)];
        IsEmpty = Items.Count == 0;
        _ = RefreshCounts();
    }

    /// <summary>The row object that holds <paramref name="item"/> (the clip itself, or its grid row).</summary>
    public object? RowOf(ClipItem item) => UseGrid ? Rows.OfType<GridRow>().FirstOrDefault(r => r.Items.Contains(item)) : item;

    public void RefreshTimes()
    {
        foreach (var c in Items) c.RefreshTime();
    }
}

/// <summary>Chooses row templates: the clip row (panel or library flavour), date headers and grid rows.</summary>
public sealed class RowSelector : DataTemplateSelector
{
    public string ClipKey { get; set; } = "Row.Panel";
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (container is not FrameworkElement fe) return null;
        string key = item switch
        {
            HeaderRow => "Header",
            GridRow => "GridRow",
            _ => ClipKey,
        };
        return fe.TryFindResource(key) as DataTemplate;
    }
}
