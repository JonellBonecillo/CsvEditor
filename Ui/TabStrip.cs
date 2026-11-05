using System.Drawing.Drawing2D;
using CsvEditor.Theming;

namespace CsvEditor.Ui;

public sealed class TabItem
{
    public string Text { get; set; } = "";
    public bool Modified { get; set; }
    public bool Busy { get; set; }
    public object? Tag { get; set; }
    internal Rectangle Bounds;
    internal Rectangle CloseBounds;
}

public sealed class TabDroppedEventArgs(TabItem item, Point screenPoint) : EventArgs
{
    public TabItem Item { get; } = item;
    public Point ScreenPoint { get; } = screenPoint;
}

/// <summary>Owner-drawn tab strip: reorder by dragging, drag out (or onto another window) to detach.</summary>
public sealed class TabStrip : Control, ITabAware
{
    private readonly List<TabItem> _items = new();
    private readonly System.Windows.Forms.Timer _spin = new() { Interval = 70 };
    private int _selected = -1, _hot = -1;
    private bool _hotClose, _hotPlus;
    private TabItem? _dragItem;
    private Point _dragStart;
    private bool _dragging, _outside;
    private int _spinAngle;
    private Rectangle _plus;

    public IReadOnlyList<TabItem> Items => _items;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public TabItem? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public event EventHandler? SelectedIndexChanged;
    public event EventHandler<TabItem>? CloseRequested;
    public event EventHandler? NewTabRequested;
    public event EventHandler<TabDroppedEventArgs>? TabDroppedOutside;
    public event EventHandler? TabsReordered;

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = 38;
        Font = new Font("Segoe UI", 9.5f);
        _spin.Tick += (_, _) => { _spinAngle = (_spinAngle + 30) % 360; Invalidate(); };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _spin.Dispose();
        base.Dispose(disposing);
    }

    private float S => DeviceDpi / 96f;

    public TabItem Add(string text, object? tag, int? index = null)
    {
        var t = new TabItem { Text = text, Tag = tag };
        _items.Insert(index ?? _items.Count, t);
        UpdateSpinner();
        Invalidate();
        return t;
    }

    public void Remove(TabItem t)
    {
        int i = _items.IndexOf(t);
        if (i < 0) return;
        _items.RemoveAt(i);
        int newSel = _selected;
        if (i < _selected) newSel--;
        else if (i == _selected) newSel = Math.Min(i, _items.Count - 1);
        _selected = -2; // force change notification
        SelectedIndex = newSel;
        UpdateSpinner();
    }

    public void Select(TabItem t) => SelectedIndex = _items.IndexOf(t);

    public void Touch(TabItem _)
    {
        UpdateSpinner();
        Invalidate();
    }

    private void UpdateSpinner() => _spin.Enabled = _items.Any(i => i.Busy);

    private void DoLayout()
    {
        int pad = (int)(8 * S), top = (int)(6 * S);
        int plusW = (int)(34 * S);
        int avail = Width - pad * 2 - plusW;
        int w = _items.Count == 0 ? 0 : Math.Clamp(avail / _items.Count, (int)(80 * S), (int)(220 * S));
        int x = pad;
        foreach (var t in _items)
        {
            t.Bounds = new Rectangle(x, top, w - 2, Height - top);
            int cs = (int)(18 * S);
            t.CloseBounds = new Rectangle(t.Bounds.Right - cs - (int)(8 * S), t.Bounds.Top + (t.Bounds.Height - cs) / 2 + 1, cs, cs);
            x += w;
        }
        _plus = new Rectangle(x + (int)(2 * S), top + (int)(4 * S), (int)(28 * S), Height - top - (int)(8 * S));
    }

    private int HitTab(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Bounds.Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Current;
        var g = e.Graphics;
        g.Clear(p.TabBar);
        DoLayout();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (int i = 0; i < _items.Count; i++)
        {
            if (i == _selected) continue;
            DrawTab(g, p, i);
        }
        if (_selected >= 0 && _selected < _items.Count) DrawTab(g, p, _selected);

        // new-tab button
        if (_hotPlus) { using var br = new SolidBrush(p.Hover); FillRound(g, br, _plus, 6 * S); }
        using (var pen = new Pen(p.Subtle, 1.8f * S))
        {
            int cx = _plus.X + _plus.Width / 2, cy = _plus.Y + _plus.Height / 2, a = (int)(5 * S);
            g.DrawLine(pen, cx - a, cy, cx + a, cy); g.DrawLine(pen, cx, cy - a, cx, cy + a);
        }

        if (_dragging && _outside)
        {
            using var pen = new Pen(p.Accent, 2);
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    private static void FillRound(Graphics g, Brush br, Rectangle r, float rad)
    {
        using var path = new GraphicsPath();
        float d = rad * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(br, path);
    }

    private void DrawTab(Graphics g, Palette p, int i)
    {
        var t = _items[i];
        bool sel = i == _selected, hot = i == _hot;
        var b = t.Bounds;
        float rad = 8 * S;

        // rounded top corners, flat bottom
        using (var path = new GraphicsPath())
        {
            float d = rad * 2;
            path.AddArc(b.X, b.Y, d, d, 180, 90);
            path.AddArc(b.Right - d, b.Y, d, d, 270, 90);
            path.AddLine(b.Right, b.Bottom, b.X, b.Bottom);
            path.CloseFigure();
            using var br = new SolidBrush(sel ? p.Window : hot ? p.Hover : p.TabIdle);
            g.FillPath(br, path);
        }
        if (sel)
        {
            using var pen = new Pen(p.Accent, 2.5f * S);
            g.DrawLine(pen, b.X + rad, b.Y + 1, b.Right - rad, b.Y + 1);
        }

        int ic = (int)(16 * S);
        var iconRect = new Rectangle(b.X + (int)(10 * S), b.Y + (b.Height - ic) / 2 + 1, ic, ic);
        g.DrawImage(IconFactory.Get(Ico.File, ic), iconRect);

        var textRect = new Rectangle(iconRect.Right + (int)(6 * S), b.Y, t.CloseBounds.X - iconRect.Right - (int)(10 * S), b.Height);
        TextRenderer.DrawText(g, t.Text, Font, textRect, sel ? p.Text : p.Subtle,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);

        var cb = t.CloseBounds;
        bool hotClose = hot && _hotClose;
        if (hotClose) { using var br = new SolidBrush(Color.FromArgb(sel ? 40 : 60, p.Text)); g.FillEllipse(br, cb); }
        if (t.Busy)
        {
            using var pen = new Pen(p.Accent, 2f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var r = Rectangle.Inflate(cb, -(int)(3 * S), -(int)(3 * S));
            if (hotClose) DrawX(g, p, cb); else g.DrawArc(pen, r, _spinAngle, 270);
        }
        else if (t.Modified && !hot) { using var br = new SolidBrush(p.Accent); int d = (int)(8 * S); g.FillEllipse(br, cb.X + (cb.Width - d) / 2, cb.Y + (cb.Height - d) / 2, d, d); }
        else if (sel || hot) DrawX(g, p, cb);
    }

    private void DrawX(Graphics g, Palette p, Rectangle cb)
    {
        using var pen = new Pen(p.Text, 1.5f * S);
        int a = (int)(4 * S), cx = cb.X + cb.Width / 2, cy = cb.Y + cb.Height / 2;
        g.DrawLine(pen, cx - a, cy - a, cx + a, cy + a);
        g.DrawLine(pen, cx + a, cy - a, cx - a, cy + a);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = HitTab(e.Location);
        if (e.Button == MouseButtons.Middle && i >= 0) { CloseRequested?.Invoke(this, _items[i]); return; }
        if (e.Button != MouseButtons.Left) return;
        if (_plus.Contains(e.Location)) { NewTabRequested?.Invoke(this, EventArgs.Empty); return; }
        if (i < 0) return;
        if (_items[i].CloseBounds.Contains(e.Location)) { _pressedClose = _items[i]; return; }
        SelectedIndex = i;
        _dragItem = _items[i];
        _dragStart = e.Location;
        _dragging = false;
        Capture = true;
    }

    private TabItem? _pressedClose;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragItem != null)
        {
            if (!_dragging)
            {
                var d = SystemInformation.DragSize;
                if (Math.Abs(e.X - _dragStart.X) < d.Width && Math.Abs(e.Y - _dragStart.Y) < d.Height) return;
                _dragging = true;
            }
            int slack = (int)(24 * S);
            _outside = e.Y < -slack || e.Y > Height + slack || e.X < -slack || e.X > Width + slack;
            Cursor = _outside ? Cursors.SizeAll : Cursors.Default;
            if (!_outside) ReorderTo(e.X);
            Invalidate();
            return;
        }
        int i = HitTab(e.Location);
        bool hc = i >= 0 && _items[i].CloseBounds.Contains(e.Location);
        bool hp = _plus.Contains(e.Location);
        if (i != _hot || hc != _hotClose || hp != _hotPlus) { _hot = i; _hotClose = hc; _hotPlus = hp; Invalidate(); }
    }

    private void ReorderTo(int x)
    {
        int from = _items.IndexOf(_dragItem!);
        int target = from;
        for (int i = 0; i < _items.Count; i++)
        {
            var b = _items[i].Bounds;
            if (x >= b.Left && x < b.Right) { target = i; break; }
        }
        if (x < _items[0].Bounds.Left) target = 0;
        if (x >= _items[^1].Bounds.Right) target = _items.Count - 1;
        if (target == from) return;
        var sel = SelectedItem;
        _items.RemoveAt(from);
        _items.Insert(target, _dragItem!);
        _selected = _items.IndexOf(sel!);
        TabsReordered?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pressedClose != null)
        {
            var t = _pressedClose; _pressedClose = null;
            if (_items.Contains(t) && t.CloseBounds.Contains(e.Location)) CloseRequested?.Invoke(this, t);
            return;
        }
        if (_dragItem == null) return;
        var item = _dragItem;
        bool detach = _dragging && _outside;
        _dragItem = null; _dragging = false; _outside = false;
        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
        if (detach) TabDroppedOutside?.Invoke(this, new TabDroppedEventArgs(item, Cursor.Position));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragItem != null) return;
        _hot = -1; _hotClose = false; _hotPlus = false; Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTab(e.Location) < 0 && !_plus.Contains(e.Location))
            NewTabRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_items.Count < 2) return;
        SelectedIndex = Math.Clamp(_selected + (e.Delta > 0 ? -1 : 1), 0, _items.Count - 1);
    }

    /// <summary>Insert index for a screen point (used when another window's tab is dropped here).</summary>
    public int IndexAtScreenPoint(Point screen)
    {
        var p = PointToClient(screen);
        DoLayout();
        for (int i = 0; i < _items.Count; i++)
            if (p.X < _items[i].Bounds.Left + _items[i].Bounds.Width / 2) return i;
        return _items.Count;
    }
}
