using CsvEditor.Core;
using CsvEditor.Theming;

namespace CsvEditor.Ui;

/// <summary>One tab's content: the grid, its document, a busy overlay, and all editing operations.</summary>
public sealed class DocumentView : UserControl, ITabAware
{
    private static int _untitled;

    private readonly CsvGrid _grid = new() { Dock = DockStyle.Fill };
    private readonly Panel _overlay = new() { Visible = false, Size = new Size(360, 110) };
    private readonly Label _overlayText = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Top, Height = 40 };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 8, Minimum = 0, Maximum = 1000 };
    private readonly Button _cancel = new() { Text = "Cancel", Dock = DockStyle.Bottom, Height = 30 };
    private CancellationTokenSource? _cts;
    private Font? _font, _boldFont;
    private int _zoom = 100;
    private readonly int _untitledNo = ++_untitled;

    public CsvDocument Document { get; } = CsvDocument.CreateBlank();
    public bool IsBusy { get; private set; }
    public CsvGrid Grid => _grid;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ZoomPercent
    {
        get => _zoom;
        set
        {
            value = Math.Clamp(value, 25, 400);
            if (value == _zoom) return;
            double ratio = value / (double)_zoom;
            _zoom = value;
            _grid.SuspendLayout();
            foreach (DataGridViewColumn c in _grid.Columns) c.Width = Math.Max(24, (int)Math.Round(c.Width * ratio));
            ReapplyFont();
            _grid.ResumeLayout();
            ZoomChanged?.Invoke();
        }
    }

    public string DisplayName => Document.FilePath != null ? Path.GetFileName(Document.FilePath) : $"Untitled {_untitledNo}";

    public event Action? StateChanged;      // name / modified / busy
    public event Action? SelectionChanged;  // current cell moved or its text changed
    public event Action? ZoomChanged;
    public event Action<string>? Status;

    public DocumentView()
    {
        Dock = DockStyle.Fill;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
        Controls.Add(_grid);

        _overlay.Controls.Add(_cancel);
        _overlay.Controls.Add(_bar);
        _overlay.Controls.Add(_overlayText);
        _overlay.Padding = new Padding(16);
        _cancel.Click += (_, _) => _cts?.Cancel();
        Controls.Add(_overlay);
        _overlay.BringToFront();
        Resize += (_, _) => CenterOverlay();

        _grid.CellValueNeeded += (_, e) => e.Value = Document.Get(e.RowIndex, e.ColumnIndex);
        _grid.CellValuePushed += (_, e) => Document.SetCell(e.RowIndex, e.ColumnIndex, e.Value?.ToString() ?? "");
        _grid.CellFormatting += (_, e) =>
        {
            if (Document.HasHeader && e.RowIndex == 0 && e.ColumnIndex >= 0)
            {
                e.CellStyle!.Font = _boldFont;
                e.CellStyle.BackColor = ThemeManager.Current.Header;
            }
        };
        _grid.EditingControlShowing += (_, e) =>
        {
            var p = ThemeManager.Current;
            e.CellStyle.BackColor = p.Surface;
            e.CellStyle.ForeColor = p.Text;
        };
        _grid.CurrentCellChanged += (_, _) => SelectionChanged?.Invoke();
        _grid.ZoomDelta += d => ZoomPercent += d * 10;
        _grid.KeyDown += GridKeyDown;
        _grid.NeedColumnRight += () => { Document.Resize(Document.RowCount, Document.ColumnCount + 1); SyncGrid(); };
        _grid.NeedRowBelow += () => { Document.Resize(Document.RowCount + 1, Document.ColumnCount); SyncGrid(); };
        _grid.DataError += (_, e) => e.ThrowException = false;

        Document.Changed += OnDocumentChanged;
        SyncGrid();
        ThemeManager.Changed += ApplyTheme;
        ApplyTheme();
        ReapplyFont();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.Changed -= ApplyTheme;
            _cts?.Cancel();
            _cts?.Dispose();
            _font?.Dispose();
            _boldFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- appearance

    public void ApplyTheme()
    {
        var p = ThemeManager.Current;
        BackColor = p.Window;
        _grid.ApplyTheme();
        _overlay.BackColor = p.Header;
        _overlayText.ForeColor = p.Text;
        _cancel.FlatStyle = FlatStyle.Flat;
        _cancel.BackColor = p.Surface; _cancel.ForeColor = p.Text; _cancel.FlatAppearance.BorderColor = p.Border;
        _overlay.Paint -= PaintOverlayBorder; _overlay.Paint += PaintOverlayBorder;
    }

    private void PaintOverlayBorder(object? s, PaintEventArgs e)
    {
        using var pen = new Pen(ThemeManager.Current.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, _overlay.Width - 1, _overlay.Height - 1);
    }

    public void ReapplyFont()
    {
        var s = AppSettings.Current;
        float size = Math.Max(4f, s.FontSize * _zoom / 100f);
        var old = _font; var oldBold = _boldFont;
        try { _font = new Font(s.FontFamily, size); } catch { _font = new Font("Segoe UI", size); }
        _boldFont = new Font(_font, FontStyle.Bold);
        _grid.ApplyFont(_font);
        old?.Dispose(); oldBold?.Dispose();
    }

    private void CenterOverlay()
    {
        _overlay.Left = Math.Max(0, (ClientSize.Width - _overlay.Width) / 2);
        _overlay.Top = Math.Max(0, (ClientSize.Height - _overlay.Height) / 3);
    }

    // ---------------------------------------------------------------- grid <-> document sync

    private void OnDocumentChanged(ChangeKind kind)
    {
        if (kind == ChangeKind.Structure) SyncGrid(); else _grid.Invalidate();
        StateChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    private void SyncGrid()
    {
        _grid.SuspendLayout();
        int want = Document.ColumnCount;
        if (_grid.ColumnCount > want) while (_grid.ColumnCount > want) _grid.Columns.RemoveAt(_grid.ColumnCount - 1);
        if (_grid.ColumnCount < want)
        {
            int w = (int)(100 * _zoom / 100.0 * DeviceDpi / 96.0);
            var cols = new List<DataGridViewColumn>();
            for (int i = _grid.ColumnCount; i < want; i++)
                cols.Add(new DataGridViewTextBoxColumn { HeaderText = CsvGrid.ColumnName(i), SortMode = DataGridViewColumnSortMode.NotSortable, Width = w, MinimumWidth = 24 });
            _grid.Columns.AddRange(cols.ToArray());
        }
        // header letters shift after a column insert/delete
        for (int i = 0; i < _grid.ColumnCount; i++)
        {
            var name = CsvGrid.ColumnName(i);
            if (_grid.Columns[i].HeaderText != name) _grid.Columns[i].HeaderText = name;
        }
        if (_grid.RowCount != Document.RowCount) _grid.RowCount = Document.RowCount;
        _grid.UpdateRowHeaderWidth();
        _grid.ResumeLayout();
        _grid.Invalidate();
    }

    // ---------------------------------------------------------------- busy overlay

    private async Task<bool> RunBusyAsync(string text, bool cancellable, Func<IProgress<double>, CancellationToken, Task> work)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        _overlayText.Text = text;
        _bar.Value = 0;
        _bar.Style = ProgressBarStyle.Continuous;
        _cancel.Visible = cancellable;
        _overlay.Height = cancellable ? 120 : 90;
        CenterOverlay();
        _overlay.Visible = true;
        _overlay.BringToFront();
        _grid.Enabled = false;
        StateChanged?.Invoke();
        var progress = new Progress<double>(v => _bar.Value = (int)Math.Clamp(v * 1000, 0, 1000));
        try { await work(progress, _cts.Token); return true; }
        catch (OperationCanceledException) { return false; }
        finally
        {
            IsBusy = false;
            _overlay.Visible = false;
            _grid.Enabled = true;
            StateChanged?.Invoke();
        }
    }

    // ---------------------------------------------------------------- file I/O

    public async Task<bool> LoadFileAsync(string path, char? delimiter = null)
    {
        try
        {
            ParseResult? res = null;
            await RunBusyAsync($"Loading {Path.GetFileName(path)}…", true, async (progress, ct) =>
                res = await Task.Run(() => CsvParser.ReadFile(path, delimiter, progress, ct), ct));
            if (res == null) return false;
            Document.FilePath = path;
            Document.Delimiter = res.Delimiter;
            Document.Encoding = res.Encoding;
            Document.NewLine = res.NewLine;
            Document.Load(res.Rows, res.MaxColumns);
            if (_grid.RowCount > 0 && _grid.ColumnCount > 0) _grid.CurrentCell = _grid[0, 0];
            AppSettings.Current.AddRecent(path);
            Status?.Invoke($"Loaded {res.Rows.Count:N0} rows");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), $"Could not open \"{path}\".\n\n{ex.Message}", "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public async Task<bool> SaveAsync(string? path = null)
    {
        path ??= Document.FilePath;
        if (path == null) return false;
        try
        {
            if (!await RunBusyAsync($"Saving {Path.GetFileName(path)}…", false, async (progress, ct) =>
                    await Task.Run(() => CsvParser.WriteFile(Document, path, progress, ct), ct)))
                return false;
            Document.FilePath = path;
            Document.MarkSaved();
            AppSettings.Current.AddRecent(path);
            Status?.Invoke("Saved " + path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), $"Could not save \"{path}\".\n\n{ex.Message}", "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    // ---------------------------------------------------------------- selection helpers

    public (int R0, int R1, int C0, int C1) SelectionRect()
    {
        int rows = Document.RowCount, cols = Document.ColumnCount;
        if (_grid.AreAllCellsSelected(false)) return (0, rows - 1, 0, cols - 1);
        if (_grid.SelectedColumns.Count > 0)
        {
            int a = int.MaxValue, b = -1;
            foreach (DataGridViewColumn c in _grid.SelectedColumns) { a = Math.Min(a, c.Index); b = Math.Max(b, c.Index); }
            return (0, Math.Max(0, Document.LastUsedRow()), a, b);
        }
        if (_grid.SelectedRows.Count > 0)
        {
            int a = int.MaxValue, b = -1;
            foreach (DataGridViewRow r in _grid.SelectedRows) { a = Math.Min(a, r.Index); b = Math.Max(b, r.Index); }
            return (a, b, 0, cols - 1);
        }
        int r0 = int.MaxValue, r1 = -1, c0 = int.MaxValue, c1 = -1;
        foreach (DataGridViewCell c in _grid.SelectedCells)
        {
            r0 = Math.Min(r0, c.RowIndex); r1 = Math.Max(r1, c.RowIndex);
            c0 = Math.Min(c0, c.ColumnIndex); c1 = Math.Max(c1, c.ColumnIndex);
        }
        if (r1 < 0)
        {
            var cur = _grid.CurrentCellAddress;
            return (Math.Max(0, cur.Y), Math.Max(0, cur.Y), Math.Max(0, cur.X), Math.Max(0, cur.X));
        }
        return (r0, r1, c0, c1);
    }

    private bool RowsSelected => _grid.SelectedRows.Count > 0;
    private bool ColumnsSelected => _grid.SelectedColumns.Count > 0;

    public string CellAddress
    {
        get
        {
            var c = _grid.CurrentCellAddress;
            return c.X < 0 || c.Y < 0 ? "" : CsvGrid.ColumnName(c.X) + (c.Y + 1);
        }
    }

    public string CurrentCellText
    {
        get { var c = _grid.CurrentCellAddress; return c.X < 0 || c.Y < 0 ? "" : Document.Get(c.Y, c.X); }
    }

    public void SetCurrentCellText(string text)
    {
        var c = _grid.CurrentCellAddress;
        if (c.X < 0 || c.Y < 0) return;
        if (_grid.IsCurrentCellInEditMode) _grid.CancelEdit();
        Document.SetCell(c.Y, c.X, text);
    }

    // ---------------------------------------------------------------- edit operations

    private void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode) { DeleteSelection(); e.Handled = true; }
    }

    public void DeleteSelection()
    {
        var (r0, r1, c0, c1) = SelectionRect();
        var edits = new List<(int, int, string)>();
        r1 = Math.Min(r1, Document.RowCount - 1);
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
                if (Document.Get(r, c).Length > 0) edits.Add((r, c, ""));
        Document.SetCells(edits);
    }

    public async Task CopyAsync(bool cut)
    {
        if (_grid.IsCurrentCellInEditMode) return;
        var (r0, r1, c0, c1) = SelectionRect();
        var doc = Document;
        string text = await Task.Run(() => CsvParser.ToClipboardText(doc, r0, r1, c0, c1, CancellationToken.None));
        try { Clipboard.SetText(text); } catch (Exception ex) { Status?.Invoke("Clipboard unavailable: " + ex.Message); return; }
        if (cut) DeleteSelection();
        Status?.Invoke($"Copied {(r1 - r0 + 1) * (c1 - c0 + 1):N0} cells");
    }

    public async Task PasteAsync()
    {
        string text;
        try { text = Clipboard.GetText(); } catch { return; }
        if (text.Length == 0) return;
        var rows = await Task.Run(() => CsvParser.ParseClipboard(text));
        if (rows.Count == 0) return;
        var (r0, r1, c0, c1) = SelectionRect();
        int startR = r0, startC = c0;
        var edits = new List<(int, int, string)>();
        if (rows.Count == 1 && rows[0].Length == 1 && (r1 > r0 || c1 > c0))
        {
            var v = rows[0][0];
            for (int r = r0; r <= Math.Min(r1, Document.RowCount - 1); r++)
                for (int c = c0; c <= c1; c++) edits.Add((r, c, v));
        }
        else
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < rows[i].Length; j++)
                    edits.Add((startR + i, startC + j, rows[i][j]));
        Document.SetCells(edits);
        Status?.Invoke($"Pasted {edits.Count:N0} cells");
    }

    public void CancelBusy() => _cts?.Cancel();

    public void SelectAll() => _grid.SelectAll();

    public void Undo() { if (_grid.IsCurrentCellInEditMode) _grid.CancelEdit(); Document.Undo(); }
    public void Redo() => Document.Redo();

    public void InsertRow(bool below)
    {
        var (r0, r1, _, _) = SelectionRect();
        int count = RowsSelected ? r1 - r0 + 1 : 1;
        Document.InsertRows(below ? r1 + 1 : r0, count);
    }

    public void InsertColumn(bool right)
    {
        var (_, _, c0, c1) = SelectionRect();
        Document.InsertColumn(right ? c1 + 1 : c0);
    }

    public void DeleteRows()
    {
        var (r0, r1, _, _) = SelectionRect();
        r1 = Math.Min(r1, Document.RowCount - 1);
        Document.DeleteRows(Enumerable.Range(r0, r1 - r0 + 1).ToArray());
    }

    public void DeleteColumns()
    {
        var (_, _, c0, c1) = SelectionRect();
        if (c1 - c0 + 1 >= Document.ColumnCount) return;
        Document.DeleteColumns(Enumerable.Range(c0, c1 - c0 + 1).ToArray());
    }

    public async Task SortAsync(bool ascending)
    {
        var col = Math.Max(0, _grid.CurrentCellAddress.X);
        int[]? perm = null;
        var doc = Document;
        await RunBusyAsync("Sorting…", true, async (_, ct) =>
        {
            _bar.Style = ProgressBarStyle.Marquee;
            perm = await Task.Run(() => doc.ComputeSortOrder(col, ascending, ct), ct);
        });
        if (perm != null) Document.ApplyOrder(perm);
    }

    public void ToggleHeader()
    {
        Document.HasHeader = !Document.HasHeader;
        _grid.Invalidate();
        StateChanged?.Invoke();
    }

    // ---------------------------------------------------------------- find / replace

    public async Task<bool> FindAsync(string text, bool matchCase, bool forward)
    {
        if (text.Length == 0) return false;
        var cur = _grid.CurrentCellAddress;
        int sr = Math.Max(0, cur.Y), sc = Math.Max(0, cur.X);
        if (cur.X < 0) { sr = forward ? 0 : 0; sc = forward ? -1 : 0; }
        var doc = Document;
        (int R, int C)? hit = null;
        Status?.Invoke("Searching…");
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try { hit = await Task.Run(() => doc.Find(text, matchCase, sr, sc, forward, cts.Token), cts.Token); }
        catch (OperationCanceledException) { return false; }
        if (hit is { } h)
        {
            _grid.ClearSelection();
            _grid.CurrentCell = _grid[h.C, h.R];
            Status?.Invoke($"Found at {CsvGrid.ColumnName(h.C)}{h.R + 1}");
            return true;
        }
        Status?.Invoke($"\"{text}\" not found");
        return false;
    }

    public async Task<bool> ReplaceCurrentAsync(string find, string replace, bool matchCase)
    {
        var cur = _grid.CurrentCellAddress;
        if (cur.X >= 0 && cur.Y >= 0)
        {
            var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var s = Document.Get(cur.Y, cur.X);
            if (find.Length > 0 && s.Contains(find, cmp))
                Document.SetCell(cur.Y, cur.X, s.Replace(find, replace, cmp));
        }
        return await FindAsync(find, matchCase, true);
    }

    public async Task<int> ReplaceAllAsync(string find, string replace, bool matchCase)
    {
        var doc = Document;
        List<(int R, int C, string Value)>? edits = null;
        await RunBusyAsync("Replacing…", true, async (_, ct) =>
        {
            _bar.Style = ProgressBarStyle.Marquee;
            edits = await Task.Run(() => doc.ComputeReplaceAll(find, replace, matchCase, ct), ct);
        });
        if (edits is { Count: > 0 }) Document.SetCells(edits);
        Status?.Invoke($"Replaced in {edits?.Count ?? 0:N0} cells");
        return edits?.Count ?? 0;
    }
}
