using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SweaxClip.Core;

/// <summary>
/// The permanent archive. One SQLite file (WAL, fully synchronous) plus content-addressed image files.
/// Every call is serialised through one connection; all of them are short.
/// </summary>
public sealed class ClipStore : IDisposable
{
    const int SchemaVersion = 1;
    const string ListColumns = "id,kind,hash,substr(text,1,3000),blob,thumb,width,height,size,app,app_path,title,created,used,uses,pinned,deleted,ocr,length(text)";

    readonly SqliteConnection _db;
    readonly object _gate = new();

    public event Action? Changed;

    public ClipStore(string path)
    {
        SqliteConnection.ClearAllPools();
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-2000; PRAGMA mmap_size=0;");
        Migrate();
    }

    void Migrate()
    {
        long v = (long)Scalar("PRAGMA user_version")!;
        if (v >= SchemaVersion) return;
        Exec("""
            CREATE TABLE IF NOT EXISTS clips(
              id INTEGER PRIMARY KEY,
              kind INTEGER NOT NULL,
              hash TEXT NOT NULL UNIQUE,
              text TEXT, html TEXT, rtf TEXT,
              blob TEXT, thumb TEXT,
              width INTEGER NOT NULL DEFAULT 0, height INTEGER NOT NULL DEFAULT 0,
              size INTEGER NOT NULL DEFAULT 0,
              app TEXT, app_path TEXT, title TEXT,
              created INTEGER NOT NULL, used INTEGER NOT NULL,
              uses INTEGER NOT NULL DEFAULT 1,
              pinned INTEGER NOT NULL DEFAULT 0,
              deleted INTEGER,
              ocr TEXT, ocr_state INTEGER NOT NULL DEFAULT 0,
              extra TEXT);
            CREATE INDEX IF NOT EXISTS ix_used ON clips(deleted, used DESC);
            CREATE INDEX IF NOT EXISTS ix_kind ON clips(kind, deleted, used DESC);
            CREATE INDEX IF NOT EXISTS ix_pinned ON clips(pinned, deleted, used DESC);
            CREATE INDEX IF NOT EXISTS ix_ocr ON clips(ocr_state) WHERE ocr_state = 1;
            CREATE VIRTUAL TABLE IF NOT EXISTS clips_fts USING fts5(body, tokenize='trigram');
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            """);
        Exec($"PRAGMA user_version={SchemaVersion}");
        Exec($"INSERT OR IGNORE INTO meta(key,value) VALUES('created', '{TimeText.Now}')");
    }

    // ── writes ────────────────────────────────────────────────────────────────

    /// <summary>Returns the id of an existing clip with this hash (reviving it) or null.</summary>
    public long? Touch(string hash, long when)
    {
        lock (_gate)
        {
            using var cmd = Cmd("UPDATE clips SET used=@w, uses=uses+1, deleted=NULL WHERE hash=@h RETURNING id", ("@w", when), ("@h", hash));
            var r = cmd.ExecuteScalar();
            if (r is long id) { Fire(); return id; }
            return null;
        }
    }

    public void TouchId(long id)
    {
        lock (_gate)
        {
            using var cmd = Cmd("UPDATE clips SET used=@w, uses=uses+1, deleted=NULL WHERE id=@id", ("@w", TimeText.Now), ("@id", id));
            cmd.ExecuteNonQuery();
        }
        Fire();
    }

    public long Insert(ClipItem c, string? html, string? rtf, int ocrState)
    {
        long id;
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            using (var cmd = Cmd("""
                INSERT INTO clips(kind,hash,text,html,rtf,blob,thumb,width,height,size,app,app_path,title,created,used,uses,pinned,ocr_state)
                VALUES(@k,@h,@t,@html,@rtf,@b,@th,@w,@hh,@s,@a,@ap,@ti,@c,@u,1,0,@os) RETURNING id
                """,
                ("@k", (int)c.Kind), ("@h", c.Hash), ("@t", c.Text), ("@html", html), ("@rtf", rtf), ("@b", c.Blob), ("@th", c.Thumb),
                ("@w", c.Width), ("@hh", c.Height), ("@s", c.Size), ("@a", c.App), ("@ap", c.AppPath), ("@ti", c.Title),
                ("@c", c.Created), ("@u", c.Used), ("@os", ocrState)))
            {
                cmd.Transaction = tx;
                id = (long)cmd.ExecuteScalar()!;
            }
            using (var f = Cmd("INSERT INTO clips_fts(rowid, body) VALUES(@id, @b)", ("@id", id), ("@b", Body(c.Text, null, c.App, c.Title))))
            {
                f.Transaction = tx;
                f.ExecuteNonQuery();
            }
            tx.Commit();
        }
        c.Id = id;
        Fire();
        return id;
    }

    public void SetPinned(long id, bool pinned) { Run("UPDATE clips SET pinned=@p WHERE id=@id", ("@p", pinned ? 1 : 0), ("@id", id)); Fire(); }
    public void Trash(long id) { Run("UPDATE clips SET deleted=@d WHERE id=@id", ("@d", TimeText.Now), ("@id", id)); Fire(); }
    public void Restore(long id) { Run("UPDATE clips SET deleted=NULL WHERE id=@id", ("@id", id)); Fire(); }

    /// <summary>Removes rows for good and returns the files that belonged to them.</summary>
    public List<string> DeleteForever(IEnumerable<long> ids)
    {
        var files = new List<string>();
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            foreach (long id in ids)
            {
                using (var q = Cmd("SELECT blob, thumb FROM clips WHERE id=@id", ("@id", id)))
                {
                    q.Transaction = tx;
                    using var r = q.ExecuteReader();
                    if (r.Read())
                    {
                        if (!r.IsDBNull(0)) files.Add(r.GetString(0));
                        if (!r.IsDBNull(1)) files.Add(r.GetString(1));
                    }
                }
                foreach (var sql in new[] { "DELETE FROM clips WHERE id=@id", "DELETE FROM clips_fts WHERE rowid=@id" })
                {
                    using var d = Cmd(sql, ("@id", id));
                    d.Transaction = tx;
                    d.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        Fire();
        return files;
    }

    public List<long> TrashIds()
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT id FROM clips WHERE deleted IS NOT NULL");
            using var r = cmd.ExecuteReader();
            var ids = new List<long>();
            while (r.Read()) ids.Add(r.GetInt64(0));
            return ids;
        }
    }

    public void SetOcr(long id, string? text, int state)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            string? app = null, title = null;
            using (var q = Cmd("UPDATE clips SET ocr=@o, ocr_state=@s WHERE id=@id RETURNING app, title", ("@o", text), ("@s", state), ("@id", id)))
            {
                q.Transaction = tx;
                using var r = q.ExecuteReader();
                if (r.Read()) { app = r.IsDBNull(0) ? null : r.GetString(0); title = r.IsDBNull(1) ? null : r.GetString(1); }
            }
            using (var f = Cmd("UPDATE clips_fts SET body=@b WHERE rowid=@id", ("@b", Body(null, text, app, title)), ("@id", id)))
            {
                f.Transaction = tx;
                f.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public List<(long id, string blob)> PendingOcr(int limit)
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT id, blob FROM clips WHERE ocr_state=1 AND blob IS NOT NULL ORDER BY used DESC LIMIT @n", ("@n", limit));
            using var r = cmd.ExecuteReader();
            var list = new List<(long, string)>();
            while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1)));
            return list;
        }
    }

    // ── reads ─────────────────────────────────────────────────────────────────

    public List<ClipItem> Query(Scope scope, string? search, int limit, int offset)
    {
        var sb = new StringBuilder($"SELECT {ListColumns} FROM clips WHERE ");
        var args = new List<(string, object?)>();
        sb.Append(scope switch
        {
            Scope.All => "deleted IS NULL",
            Scope.Text => "kind=0 AND deleted IS NULL",
            Scope.Links => "kind=1 AND deleted IS NULL",
            Scope.Code => "kind=2 AND deleted IS NULL",
            Scope.Colors => "kind=3 AND deleted IS NULL",
            Scope.Images => "kind IN (4,5) AND deleted IS NULL",
            Scope.Screenshots => "kind=5 AND deleted IS NULL",
            Scope.Files => "kind=6 AND deleted IS NULL",
            Scope.Pinned => "pinned=1 AND deleted IS NULL",
            Scope.Trash => "deleted IS NOT NULL",
            _ => "deleted IS NULL",
        });
        AppendSearch(sb, args, search);
        sb.Append(scope == Scope.Trash ? " ORDER BY deleted DESC" : " ORDER BY used DESC");
        sb.Append(" LIMIT @lim OFFSET @off");
        args.Add(("@lim", limit));
        args.Add(("@off", offset));

        lock (_gate)
        {
            using var cmd = Cmd(sb.ToString(), args.ToArray());
            using var r = cmd.ExecuteReader();
            var list = new List<ClipItem>(Math.Min(limit, 256));
            while (r.Read()) list.Add(Read(r));
            return list;
        }
    }

    static void AppendSearch(StringBuilder sb, List<(string, object?)> args, string? search)
    {
        string q = SearchText.Normalize(search ?? "", 200).Trim();
        if (q.Length == 0) return;
        var terms = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct().Take(8).ToList();
        var longTerms = terms.Where(t => t.Length >= 3).ToList();
        if (longTerms.Count > 0)
        {
            sb.Append(" AND id IN (SELECT rowid FROM clips_fts WHERE clips_fts MATCH @m)");
            args.Add(("@m", string.Join(" AND ", longTerms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""))));
        }
        int i = 0;
        foreach (var t in terms.Where(t => t.Length < 3))
        {
            string p = "@s" + i++;
            sb.Append($" AND id IN (SELECT rowid FROM clips_fts WHERE body LIKE {p} ESCAPE '\\')");
            args.Add((p, "%" + t.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
        }
    }

    public ClipItem? Get(long id)
    {
        lock (_gate)
        {
            using var cmd = Cmd($"SELECT {ListColumns.Replace("substr(text,1,3000)", "text")}, html, rtf FROM clips WHERE id=@id", ("@id", id));
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var c = Read(r);
            c.Html = r.IsDBNull(19) ? null : r.GetString(19);
            c.Rtf = r.IsDBNull(20) ? null : r.GetString(20);
            return c;
        }
    }

    public Dictionary<Scope, int> Counts()
    {
        var d = Enum.GetValues<Scope>().ToDictionary(s => s, _ => 0);
        lock (_gate)
        {
            using (var cmd = Cmd("SELECT kind, COUNT(*), SUM(pinned) FROM clips WHERE deleted IS NULL GROUP BY kind"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var k = (ClipKind)r.GetInt32(0);
                    int n = r.GetInt32(1);
                    d[Scope.All] += n;
                    d[Scope.Pinned] += r.IsDBNull(2) ? 0 : r.GetInt32(2);
                    switch (k)
                    {
                        case ClipKind.Text: d[Scope.Text] += n; break;
                        case ClipKind.Link: d[Scope.Links] += n; break;
                        case ClipKind.Code: d[Scope.Code] += n; break;
                        case ClipKind.Color: d[Scope.Colors] += n; break;
                        case ClipKind.Image: d[Scope.Images] += n; break;
                        case ClipKind.Screenshot: d[Scope.Images] += n; d[Scope.Screenshots] += n; break;
                        case ClipKind.Files: d[Scope.Files] += n; break;
                    }
                }
            }
            d[Scope.Trash] = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM clips WHERE deleted IS NOT NULL"));
        }
        return d;
    }

    public (long first, long bytes) Stats()
    {
        lock (_gate)
        {
            object? first = Scalar("SELECT MIN(created) FROM clips");
            object? bytes = Scalar("SELECT SUM(size) FROM clips");
            return (first is long f ? f : 0, bytes is long b ? b : 0);
        }
    }

    public bool HasHash(string hash)
    {
        lock (_gate) return Scalar("SELECT 1 FROM clips WHERE hash=@h", ("@h", hash)) != null;
    }

    public void BackupTo(string file)
    {
        if (File.Exists(file)) File.Delete(file);
        lock (_gate)
        {
            using var cmd = Cmd("VACUUM INTO @f", ("@f", file));
            cmd.ExecuteNonQuery();
        }
    }

    public void Checkpoint()
    {
        lock (_gate) Exec("PRAGMA wal_checkpoint(TRUNCATE)");
    }

    // ── plumbing ──────────────────────────────────────────────────────────────

    static string Body(string? text, string? ocr, string? app, string? title)
    {
        var sb = new StringBuilder();
        sb.Append(SearchText.Normalize(text));
        if (!string.IsNullOrEmpty(ocr)) sb.Append('\n').Append(SearchText.Normalize(ocr, 16_000));
        if (!string.IsNullOrEmpty(app)) sb.Append('\n').Append(SearchText.Normalize(app));
        if (!string.IsNullOrEmpty(title)) sb.Append('\n').Append(SearchText.Normalize(title, 400));
        return sb.ToString();
    }

    static ClipItem Read(SqliteDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        var c = new ClipItem
        {
            Id = r.GetInt64(0),
            Kind = (ClipKind)r.GetInt32(1),
            Hash = r.GetString(2),
            Text = S(3),
            Blob = S(4),
            Thumb = S(5),
            Width = r.GetInt32(6),
            Height = r.GetInt32(7),
            Size = r.GetInt64(8),
            App = S(9),
            AppPath = S(10),
            Title = S(11),
            Created = r.GetInt64(12),
            Uses = r.GetInt32(14),
            Pinned = r.GetInt32(15) != 0,
            Deleted = r.IsDBNull(16) ? 0 : r.GetInt64(16),
            Ocr = S(17),
            TextLength = r.IsDBNull(18) ? 0 : r.GetInt64(18),
        };
        c.Used = r.GetInt64(13);
        return c;
    }

    SqliteCommand Cmd(string sql, params (string name, object? value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    void Run(string sql, params (string, object?)[] args)
    {
        lock (_gate)
        {
            using var cmd = Cmd(sql, args);
            cmd.ExecuteNonQuery();
        }
    }

    void Exec(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    object? Scalar(string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(sql, args);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    void Fire() => Changed?.Invoke();

    /// <summary>
    /// Re-sorts plain-text clips after the rules for spotting code improve. Only Text -> Code moves; links,
    /// colours and everything else stay where they are. Runs once per <paramref name="rulesVersion"/>.
    /// </summary>
    public int ReclassifyText(int rulesVersion, Func<string, ClipKind> classify)
    {
        var moved = new List<long>();
        lock (_gate)
        {
            if (Scalar("SELECT value FROM meta WHERE key='classifier'") is string s && int.TryParse(s, out int done) && done >= rulesVersion) return 0;
            using (var cmd = Cmd("SELECT id, text FROM clips WHERE kind=@k AND text IS NOT NULL AND length(text) < 20000", ("@k", (int)ClipKind.Text)))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    if (classify(r.GetString(1)) == ClipKind.Code) moved.Add(r.GetInt64(0));
            foreach (var chunk in moved.Chunk(500))
                Run($"UPDATE clips SET kind=@k WHERE id IN ({string.Join(',', chunk)})", ("@k", (int)ClipKind.Code));
            Run("INSERT OR REPLACE INTO meta(key,value) VALUES('classifier', @v)", ("@v", rulesVersion.ToString()));
        }
        if (moved.Count > 0) Fire();
        return moved.Count;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { Exec("PRAGMA wal_checkpoint(TRUNCATE)"); } catch { }
            _db.Dispose();
        }
    }
}
