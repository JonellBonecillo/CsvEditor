using CsvEditor.Theming;

namespace CsvEditor.Ui;

/// <summary>Virtual-mode grid: column letters, row numbers, thick current-cell border, Ctrl+wheel zoom.</summary>
public sealed class CsvGrid : DataGridView, ITabAware
{
    public event Action<int>? ZoomDelta;

    public CsvGrid()
    {
        DoubleBuffered = true;
        VirtualMode = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = false;
        RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
        MultiSelect = true;
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        EnableHeadersVisualStyles = false;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.Single;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        ShowCellToolTips = false;
        StandardTab = true;
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var p = ThemeManager.Current;
        BackgroundColor = p.Surface;
        GridColor = p.Grid;
        DefaultCellStyle.BackColor = p.Surface;
        DefaultCellStyle.ForeColor = p.Text;
        DefaultCellStyle.SelectionBackColor = p.AccentSoft;
        DefaultCellStyle.SelectionForeColor = p.Text;
        DefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
        foreach (var s in new[] { ColumnHeadersDefaultCellStyle, RowHeadersDefaultCellStyle })
        {
            s.BackColor = p.Header;
            s.ForeColor = p.Subtle;
            s.SelectionBackColor = p.IsDark ? Color.FromArgb(58, 90, 120) : Color.FromArgb(160, 200, 238);
            s.SelectionForeColor = p.Text;
        }
        if (IsHandleCreated) Native.SetControlTheme(Handle, p.IsDark);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetControlTheme(Handle, ThemeManager.Current.IsDark);
    }

    public void ApplyFont(Font font)
    {
        Font = font;
        DefaultCellStyle.Font = font;
        ColumnHeadersDefaultCellStyle.Font = font;
        RowHeadersDefaultCellStyle.Font = font;
        int h = font.Height + (int)Math.Round(8 * DeviceDpi / 96.0);
        RowTemplate.Height = h;
        ColumnHeadersHeight = h;
        UpdateRowHeaderWidth();
        Invalidate();
    }

    public void UpdateRowHeaderWidth()
    {
        string sample = Math.Max(RowCount, 10).ToString();
        RowHeadersWidth = Math.Max(40, TextRenderer.MeasureText(sample + "00", RowHeadersDefaultCellStyle.Font ?? Font).Width + 12);
    }

    public static string ColumnName(int i)
    {
        var s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // DataGridView supports only one selection mode at a time; pick it from what was clicked
        var ht = HitTest(e.X, e.Y);
        var want = ht.Type == DataGridViewHitTestType.ColumnHeader
            ? DataGridViewSelectionMode.ColumnHeaderSelect
            : DataGridViewSelectionMode.RowHeaderSelect;
        if (SelectionMode != want)
        {
            var cur = CurrentCellAddress;
            SelectionMode = want;
            if (cur.X >= 0 && cur.Y >= 0 && cur.Y < RowCount && cur.X < ColumnCount) { try { CurrentCell = this[cur.X, cur.Y]; } catch { } }
        }
        base.OnMouseDown(e);
    }

    protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0 && !this[e.ColumnIndex, e.RowIndex].Selected)
        {
            ClearSelection();
            CurrentCell = this[e.ColumnIndex, e.RowIndex];
        }
        base.OnCellMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
        {
            ZoomDelta?.Invoke(e.Delta > 0 ? 1 : -1);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        var p = ThemeManager.Current;
        if (e.ColumnIndex < 0 && e.RowIndex >= 0)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
            bool sel = (e.State & DataGridViewElementStates.Selected) != 0 || CurrentCellAddress.Y == e.RowIndex;
            TextRenderer.DrawText(e.Graphics!, (e.RowIndex + 1).ToString(), e.CellStyle!.Font ?? Font, e.CellBounds,
                sel ? p.Text : p.Subtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (CurrentCellAddress.Y == e.RowIndex)
            {
                using var br = new SolidBrush(Color.FromArgb(70, p.Accent));
                e.Graphics!.FillRectangle(br, e.CellBounds);
            }
            e.Handled = true;
            return;
        }
        if (e.RowIndex < 0 && e.ColumnIndex >= 0 && CurrentCellAddress.X == e.ColumnIndex)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            using var br = new SolidBrush(Color.FromArgb(70, p.Accent));
            e.Graphics!.FillRectangle(br, e.CellBounds);
            e.Handled = true;
            return;
        }
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && CurrentCellAddress.X == e.ColumnIndex && CurrentCellAddress.Y == e.RowIndex)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            var r = e.CellBounds;
            using var pen = new Pen(p.IsDark ? p.Accent : Color.Black, 2f);
            e.Graphics!.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            e.Handled = true;
            return;
        }
        base.OnCellPainting(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // the grid fills the corner between its scrollbars with the system color; repaint it themed
        var h = Controls.OfType<HScrollBar>().FirstOrDefault(s => s.Visible);
        var v = Controls.OfType<VScrollBar>().FirstOrDefault(s => s.Visible);
        if (h != null && v != null)
            using (var br = new SolidBrush(ThemeManager.Current.Header))
                e.Graphics.FillRectangle(br, v.Left, h.Top, v.Width, h.Height);
    }

    public event Action? NeedRowBelow, NeedColumnRight;

    // Excel-like: moving past the last row / column grows the sheet
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var cur = CurrentCellAddress;
        if (cur.X >= 0 && cur.Y >= 0)
        {
            bool editing = IsCurrentCellInEditMode;
            if (cur.Y == RowCount - 1 && (keyData == Keys.Enter || keyData == Keys.Next || (!editing && keyData == Keys.Down)))
                NeedRowBelow?.Invoke();
            if (cur.X == ColumnCount - 1 && (keyData == Keys.Tab || (!editing && keyData == Keys.Right)))
                NeedColumnRight?.Invoke();
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnCurrentCellChanged(EventArgs e)
    {
        base.OnCurrentCellChanged(e);
        InvalidateHeaders();
    }

    private void InvalidateHeaders()
    {
        Invalidate(new Rectangle(0, 0, Width, ColumnHeadersHeight + 1));
        Invalidate(new Rectangle(0, 0, RowHeadersWidth + 1, Height));
    }
}
