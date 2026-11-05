using System.Text;

namespace CsvEditor.Core;

public sealed record ParseResult(List<string[]> Rows, int MaxColumns, char Delimiter, Encoding Encoding, string NewLine);

public static class CsvParser
{
    public static readonly char[] Candidates = { ',', ';', '\t', '|' };

    public static ParseResult ReadFile(string path, char? delimiter, IProgress<double>? progress, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
        var encoding = DetectEncoding(fs);
        fs.Position = 0;
        using var reader = new StreamReader(fs, encoding, true, 1 << 16);
        var buf = new char[1 << 16];

        // sniff delimiter from the first block
        int n0 = reader.Read(buf, 0, buf.Length);
        char delim = delimiter ?? Sniff(buf, n0);
        var rows = new List<string[]>(1024);
        var state = new State(delim, rows);

        long length = Math.Max(1, fs.Length);
        long lastReport = -1;
        int n = n0;
        while (n > 0)
        {
            ct.ThrowIfCancellationRequested();
            state.Feed(buf, n);
            long pct = fs.Position * 100 / length;
            if (pct != lastReport) { lastReport = pct; progress?.Report(pct / 100.0); }
            n = reader.Read(buf, 0, buf.Length);
        }
        state.Finish();
        return new ParseResult(rows, state.MaxColumns, delim, encoding, state.NewLine ?? "\r\n");
    }

    private static Encoding DetectEncoding(Stream s)
    {
        Span<byte> b = stackalloc byte[4];
        int n = s.Read(b);
        if (n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(true);
        if (n >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode;
        if (n >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false);
    }

    public static char Sniff(char[] buf, int len)
    {
        var counts = new int[Candidates.Length];
        bool quoted = false;
        int lines = 0;
        for (int i = 0; i < len && lines < 20; i++)
        {
            char c = buf[i];
            if (c == '"') quoted = !quoted;
            else if (!quoted)
            {
                if (c == '\n') lines++;
                else
                {
                    int k = Array.IndexOf(Candidates, c);
                    if (k >= 0) counts[k]++;
                }
            }
        }
        int best = 0;
        for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
        return counts[best] == 0 ? ',' : Candidates[best];
    }

    private sealed class State
    {
        private readonly char _delim;
        private readonly List<string[]> _rows;
        private readonly StringBuilder _field = new();
        private readonly List<string> _row = new();
        private bool _inQuotes, _quoteSeen, _pendingQuote, _pendingCr, _fieldStarted;
        public int MaxColumns;
        public string? NewLine;

        public State(char delim, List<string[]> rows) { _delim = delim; _rows = rows; }

        public void Feed(char[] buf, int n)
        {
            for (int i = 0; i < n; i++)
            {
                char c = buf[i];
                if (_pendingCr)
                {
                    _pendingCr = false;
                    NewLine ??= c == '\n' ? "\r\n" : "\r";
                    EndRow();
                    if (c == '\n') continue;
                }
                if (_pendingQuote)
                {
                    _pendingQuote = false;
                    if (c == '"') { _field.Append('"'); continue; }
                    _inQuotes = false; // closing quote
                }
                if (_inQuotes)
                {
                    if (c == '"') _pendingQuote = true;
                    else _field.Append(c);
                    continue;
                }
                if (c == '"' && !_fieldStarted) { _inQuotes = true; _quoteSeen = true; _fieldStarted = true; }
                else if (c == _delim) EndField();
                else if (c == '\r') _pendingCr = true;
                else if (c == '\n') { NewLine ??= "\n"; EndRow(); }
                else { _field.Append(c); _fieldStarted = true; }
            }
        }

        public void Finish()
        {
            if (_pendingCr) { NewLine ??= "\r"; _pendingCr = false; EndRow(); return; }
            if (_pendingQuote) { _pendingQuote = false; _inQuotes = false; }
            if (_fieldStarted || _field.Length > 0 || _row.Count > 0 || _quoteSeen) EndRow();
        }

        private void EndField()
        {
            _row.Add(_field.ToString());
            _field.Clear();
            _fieldStarted = false;
            _quoteSeen = false;
        }

        private void EndRow()
        {
            EndField();
            if (_row.Count > MaxColumns) MaxColumns = _row.Count;
            _rows.Add(_row.ToArray());
            _row.Clear();
        }
    }

    public static void WriteFile(CsvDocument doc, string path, IProgress<double>? progress, CancellationToken ct)
    {
        int lastRow = doc.LastUsedRow();
        int cols = doc.LastUsedColumn() + 1;
        var tmp = path + ".tmp~";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var w = new StreamWriter(fs, doc.Encoding, 1 << 16))
            {
                var sb = new StringBuilder();
                char d = doc.Delimiter;
                int lastPct = -1;
                for (int r = 0; r <= lastRow; r++)
                {
                    if ((r & 0x3FF) == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        int pct = (int)((long)r * 100 / Math.Max(1, lastRow + 1));
                        if (pct != lastPct) { lastPct = pct; progress?.Report(pct / 100.0); }
                    }
                    sb.Clear();
                    for (int c = 0; c < cols; c++)
                    {
                        if (c > 0) sb.Append(d);
                        AppendField(sb, doc.Get(r, c), d);
                    }
                    sb.Append(doc.NewLine);
                    w.Write(sb.ToString());
                }
            }
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }
        }
    }

    private static void AppendField(StringBuilder sb, string s, char d)
    {
        if (s.Length == 0) return;
        bool quote = s.AsSpan().IndexOfAny(new[] { d, '"', '\r', '\n' }) >= 0 || s[0] == ' ' || s[^1] == ' ';
        if (!quote) { sb.Append(s); return; }
        sb.Append('"').Append(s.Replace("\"", "\"\"")).Append('"');
    }

    public static string ToClipboardText(CsvDocument doc, int r0, int r1, int c0, int c1, CancellationToken ct)
    {
        var sb = new StringBuilder();
        for (int r = r0; r <= r1; r++)
        {
            if ((r & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            for (int c = c0; c <= c1; c++)
            {
                if (c > c0) sb.Append('\t');
                var s = doc.Get(r, c);
                if (s.AsSpan().IndexOfAny('\t', '\n', '"') >= 0 || s.Contains('\r'))
                    s = '"' + s.Replace("\"", "\"\"") + '"';
                sb.Append(s);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static List<string[]> ParseClipboard(string text)
    {
        var rows = new List<string[]>();
        var st = new State('\t', rows);
        var chars = text.ToCharArray();
        st.Feed(chars, chars.Length);
        st.Finish();
        // a trailing newline yields no extra row because EndRow only fires when data exists
        return rows;
    }
}
