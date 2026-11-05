using System.Text;

namespace CsvEditor.Core;

public enum ChangeKind { Cells, Structure }

/// <summary>
/// In-memory table with undo/redo. Rows may be ragged; missing cells read as "".
/// All mutation happens on the UI thread; background work only reads while the owning view is locked (busy).
/// </summary>
public sealed class CsvDocument
{
    private static readonly string[] Empty = Array.Empty<string>();

    private readonly Stack<(Action Undo, Action Redo)> _undo = new();
    private readonly Stack<(Action Undo, Action Redo)> _redo = new();

    public List<string[]> Rows { get; private set; } = new();
    public int ColumnCount { get; private set; }
    public string? FilePath { get; set; }
    public char Delimiter { get; set; } = ',';
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);
    public string NewLine { get; set; } = "\r\n";
    public bool Modified { get; private set; }
    public bool HasHeader { get; set; }

    public int RowCount => Rows.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event Action<ChangeKind>? Changed;

    public static CsvDocument CreateBlank(int rows = 100, int cols = 26)
    {
        var d = new CsvDocument();
        d.Resize(rows, cols);
        return d;
    }

    public void Load(List<string[]> rows, int maxColumns)
    {
        Rows = rows;
        ColumnCount = maxColumns;
        _undo.Clear();
        _redo.Clear();
        Modified = false;
        Resize(Math.Max(rows.Count, 1), Math.Max(maxColumns, 1));
        Changed?.Invoke(ChangeKind.Structure);
    }

    /// <summary>Grows the blank padding (not undoable, never shrinks).</summary>
    public void Resize(int rows, int cols)
    {
        while (Rows.Count < rows) Rows.Add(Empty);
        if (cols > ColumnCount) ColumnCount = cols;
    }

    public string Get(int r, int c)
    {
        if ((uint)r >= (uint)Rows.Count) return "";
        var row = Rows[r];
        return (uint)c < (uint)row.Length ? row[c] : "";
    }

    private void SetRaw(int r, int c, string value)
    {
        var row = Rows[r];
        if (c >= row.Length)
        {
            if (value.Length == 0) return;
            var n = new string[Math.Max(c + 1, Math.Min(ColumnCount, row.Length * 2))];
            Array.Fill(n, "");
            Array.Copy(row, n, row.Length);
            Rows[r] = row = n;
        }
        row[c] = value;
    }

    private void Push(Action redo, Action undo, bool markModified = true)
    {
        redo();
        _undo.Push((undo, redo));
        _redo.Clear();
        if (markModified) Modified = true;
    }

    public void MarkSaved() { Modified = false; Changed?.Invoke(ChangeKind.Cells); }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var e = _undo.Pop();
        e.Undo();
        _redo.Push(e);
        Modified = true;
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo.Pop();
        e.Redo();
        _undo.Push(e);
        Modified = true;
    }

    public void SetCells(IReadOnlyList<(int R, int C, string Value)> edits)
    {
        if (edits.Count == 0) return;
        var olds = new (int R, int C, string Value)[edits.Count];
        int maxR = 0, maxC = 0;
        for (int i = 0; i < edits.Count; i++)
        {
            olds[i] = (edits[i].R, edits[i].C, Get(edits[i].R, edits[i].C));
            maxR = Math.Max(maxR, edits[i].R);
            maxC = Math.Max(maxC, edits[i].C);
        }
        bool structural = maxR >= Rows.Count || maxC >= ColumnCount;
        Push(
            () =>
            {
                if (structural) Resize(Math.Max(Rows.Count, maxR + 1), Math.Max(ColumnCount, maxC + 1));
                foreach (var e in edits) SetRaw(e.R, e.C, e.Value);
                Changed?.Invoke(structural ? ChangeKind.Structure : ChangeKind.Cells);
            },
            () =>
            {
                foreach (var e in olds) SetRaw(e.R, e.C, e.Value);
                Changed?.Invoke(ChangeKind.Cells);
            });
    }

    public void SetCell(int r, int c, string value)
    {
        if (Get(r, c) == value) return;
        SetCells(new[] { (r, c, value) });
    }

    public void InsertRows(int at, int count)
    {
        at = Math.Clamp(at, 0, Rows.Count);
        Push(
            () => { Rows.InsertRange(at, Enumerable.Repeat(Empty, count)); Changed?.Invoke(ChangeKind.Structure); },
            () => { Rows.RemoveRange(at, count); Changed?.Invoke(ChangeKind.Structure); });
    }

    public void DeleteRows(IReadOnlyList<int> sortedIndices)
    {
        if (sortedIndices.Count == 0) return;
        var removed = sortedIndices.Select(i => (Index: i, Row: Rows[i])).ToArray();
        int pad = 0;
        Push(
            () =>
            {
                for (int i = removed.Length - 1; i >= 0; i--) Rows.RemoveAt(removed[i].Index);
                pad = Rows.Count == 0 ? 1 : 0; // grid needs at least one row
                Resize(pad, ColumnCount);
                Changed?.Invoke(ChangeKind.Structure);
            },
            () =>
            {
                if (pad > 0) Rows.RemoveRange(Rows.Count - pad, pad);
                foreach (var e in removed) Rows.Insert(e.Index, e.Row);
                Changed?.Invoke(ChangeKind.Structure);
            });
    }

    public void InsertColumn(int at)
    {
        at = Math.Clamp(at, 0, ColumnCount);
        Push(
            () =>
            {
                for (int r = 0; r < Rows.Count; r++)
                {
                    var row = Rows[r];
                    if (row.Length <= at) continue;
                    var n = new string[row.Length + 1];
                    Array.Copy(row, n, at);
                    n[at] = "";
                    Array.Copy(row, at, n, at + 1, row.Length - at);
                    Rows[r] = n;
                }
                ColumnCount++;
                Changed?.Invoke(ChangeKind.Structure);
            },
            () => { RemoveColumnsRaw(new[] { at }); Changed?.Invoke(ChangeKind.Structure); });
    }

    private void RemoveColumnsRaw(int[] sortedCols)
    {
        for (int r = 0; r < Rows.Count; r++)
        {
            var row = Rows[r];
            if (row.Length == 0) continue;
            var keep = new List<string>(row.Length);
            int k = 0;
            for (int c = 0; c < row.Length; c++)
            {
                while (k < sortedCols.Length && sortedCols[k] < c) k++;
                if (k < sortedCols.Length && sortedCols[k] == c) continue;
                keep.Add(row[c]);
            }
            Rows[r] = keep.Count == row.Length ? row : keep.ToArray();
        }
        ColumnCount = Math.Max(1, ColumnCount - sortedCols.Length);
    }

    public void DeleteColumns(int[] sortedCols)
    {
        if (sortedCols.Length == 0) return;
        var saved = new string[sortedCols.Length][];
        for (int i = 0; i < sortedCols.Length; i++)
        {
            var col = new string[Rows.Count];
            for (int r = 0; r < Rows.Count; r++) col[r] = Get(r, sortedCols[i]);
            saved[i] = col;
        }
        Push(
            () => { RemoveColumnsRaw(sortedCols); Changed?.Invoke(ChangeKind.Structure); },
            () =>
            {
                for (int i = 0; i < sortedCols.Length; i++)
                {
                    int at = sortedCols[i];
                    for (int r = 0; r < Rows.Count; r++)
                    {
                        var row = Rows[r];
                        var val = saved[i][r];
                        if (row.Length < at && val.Length == 0) continue;
                        var n = new string[Math.Max(row.Length, at) + 1];
                        Array.Fill(n, "");
                        Array.Copy(row, n, Math.Min(at, row.Length));
                        n[at] = val;
                        if (row.Length > at) Array.Copy(row, at, n, at + 1, row.Length - at);
                        Rows[r] = n;
                    }
                    ColumnCount++;
                }
                Changed?.Invoke(ChangeKind.Structure);
            });
    }

    /// <summary>Reorders rows: new[i] = old[perm[i]].</summary>
    public void ApplyOrder(int[] perm)
    {
        var inverse = new int[perm.Length];
        for (int i = 0; i < perm.Length; i++) inverse[perm[i]] = i;
        Push(() => Reorder(perm), () => Reorder(inverse));
    }

    private void Reorder(int[] perm)
    {
        var old = Rows;
        var n = new List<string[]>(old.Count);
        for (int i = 0; i < perm.Length; i++) n.Add(old[perm[i]]);
        for (int i = perm.Length; i < old.Count; i++) n.Add(old[i]);
        Rows = n;
        Changed?.Invoke(ChangeKind.Cells);
    }

    /// <summary>Called after the document is replaced wholesale (e.g. delimiter reparse).</summary>
    public void Touch() { Modified = true; Changed?.Invoke(ChangeKind.Cells); }

    // ---- read-only background helpers (callers must ensure the doc is not being edited) ----

    public int[] ComputeSortOrder(int column, bool ascending, CancellationToken ct)
    {
        int start = HasHeader ? 1 : 0;
        int end = LastUsedRow() + 1;
        var idx = Enumerable.Range(start, Math.Max(0, end - start)).ToArray();
        var keys = new string[Rows.Count];
        var nums = new double[Rows.Count];
        var isNum = new bool[Rows.Count];
        for (int i = start; i < end; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = Get(i, column).Trim();
            keys[i] = s;
            isNum[i] = double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out nums[i]);
        }
        int Cmp(int a, int b)
        {
            int res;
            if (isNum[a] && isNum[b]) res = nums[a].CompareTo(nums[b]);
            else if (keys[a].Length == 0 || keys[b].Length == 0)
                return keys[a].Length == keys[b].Length ? a.CompareTo(b) : (keys[a].Length == 0 ? 1 : -1); // blanks last
            else res = string.Compare(keys[a], keys[b], StringComparison.CurrentCultureIgnoreCase);
            if (!ascending) res = -res;
            return res != 0 ? res : a.CompareTo(b);
        }
        Array.Sort(idx, Cmp);
        var perm = new int[Rows.Count];
        for (int i = 0; i < start; i++) perm[i] = i;
        for (int i = 0; i < idx.Length; i++) perm[start + i] = idx[i];
        for (int i = end; i < perm.Length; i++) perm[i] = i;
        return perm;
    }

    public int LastUsedRow()
    {
        for (int r = Rows.Count - 1; r >= 0; r--)
            foreach (var s in Rows[r])
                if (s.Length > 0) return r;
        return -1;
    }

    public int LastUsedColumn()
    {
        int max = -1;
        foreach (var row in Rows)
            for (int c = row.Length - 1; c > max; c--)
                if (row[c].Length > 0) { max = c; break; }
        return max;
    }

    public (int R, int C)? Find(string text, bool matchCase, int startR, int startC, bool forward, CancellationToken ct)
    {
        if (text.Length == 0) return null;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int rows = Rows.Count, cols = ColumnCount;
        long total = (long)rows * cols;
        if (total == 0) return null;
        long pos = (long)startR * cols + startC;
        for (long n = 1; n <= total; n++)
        {
            if ((n & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            pos = forward ? (pos + 1) % total : (pos - 1 + total) % total;
            int r = (int)(pos / cols), c = (int)(pos % cols);
            var row = Rows[r];
            if (c < row.Length && row[c].Contains(text, cmp)) return (r, c);
        }
        return null;
    }

    public List<(int R, int C, string Value)> ComputeReplaceAll(string find, string replace, bool matchCase, CancellationToken ct)
    {
        var res = new List<(int, int, string)>();
        if (find.Length == 0) return res;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int r = 0; r < Rows.Count; r++)
        {
            if ((r & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
            var row = Rows[r];
            for (int c = 0; c < row.Length; c++)
                if (row[c].Contains(find, cmp))
                    res.Add((r, c, row[c].Replace(find, replace, cmp)));
        }
        return res;
    }
}
