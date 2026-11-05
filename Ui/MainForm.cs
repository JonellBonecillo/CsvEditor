using CsvEditor.Core;
using CsvEditor.Theming;

namespace CsvEditor.Ui;

public sealed class MainForm : Form
{
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _toolbar = new();
    private readonly TabStrip _tabs = new();
    private readonly Panel _formulaBar = new() { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 6, 8, 4) };
    private readonly Label _addr = new() { Dock = DockStyle.Left, Width = 64, TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _caption = new() { Dock = DockStyle.Left, Width = 78, Text = "Cell text:", TextAlign = ContentAlignment.MiddleRight };
    private readonly TextBox _formula = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _findBar = new() { Dock = DockStyle.Top, Height = 42, Visible = false, WrapContents = false, Padding = new Padding(8, 4, 8, 2) };
    private readonly TextBox _findText = new() { Width = 200 };
    private readonly TextBox _replaceText = new() { Width = 200 };
    private readonly CheckBox _matchCase = new() { Text = "Match case", AutoSize = true };
    private readonly List<Control> _replaceControls = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _sizeLabel = new();
    private readonly ToolStripDropDownButton _delimButton = new() { ShowDropDownArrow = false };
    private readonly ToolStripStatusLabel _encLabel = new();
    private readonly ToolStripButton _zoomOut = new() { Text = "−" };
    private readonly ToolStripButton _zoomLabel = new() { ToolTipText = "Click to reset zoom" };
    private readonly ToolStripButton _zoomIn = new() { Text = "+" };

    private readonly Dictionary<DocumentView, Action> _unbind = new();
    private readonly ContextMenuStrip _cellMenu = new();
    private readonly List<ToolStripItem> _undoItems = new(), _redoItems = new();
    private ToolStripMenuItem? _themeSystem, _themeLight, _themeDark, _formulaItem, _headerItem;
    private bool _closing;
    private int _px16, _px24;

    private IEnumerable<DocumentView> Views => _tabs.Items.Select(t => (DocumentView)t.Tag!);
    private DocumentView? CurrentView => _tabs.SelectedItem?.Tag as DocumentView;
    public TabStrip Tabs => _tabs;

    public MainForm(DocumentView? initial = null)
    {
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "CSV Editor";
        Icon = IconFactory.AppIcon();
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;
        _px16 = (int)Math.Round(16 * DeviceDpi / 96.0);
        _px24 = (int)Math.Round(24 * DeviceDpi / 96.0);

        BuildMenu();
        BuildToolbar();
        BuildFormulaBar();
        BuildFindBar();
        BuildStatus();
        BuildCellMenu();

        SuspendLayout();
        Controls.Add(_content);
        Controls.Add(_status);
        Controls.Add(_findBar);
        Controls.Add(_formulaBar);
        Controls.Add(_tabs);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        ResumeLayout();

        _tabs.SelectedIndexChanged += (_, _) => ShowCurrent();
        _tabs.CloseRequested += (_, t) => Run(() => CloseView((DocumentView)t.Tag!));
        _tabs.NewTabRequested += (_, _) => NewTab();
        _tabs.TabDroppedOutside += OnTabDropped;

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] f) Run(() => OpenFilesAsync(f)); };

        ThemeManager.Changed += ApplyTheme;
        WindowManager.Register(this);

        if (initial != null) AddView(initial); else NewTab();
        _formulaBar.Visible = AppSettings.Current.ShowFormulaBar;
        ApplyTheme();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle, ThemeManager.Current.IsDark);
    }

    // ================================================================ construction

    private ToolStripMenuItem MI(string text, Ico? ico, Keys keys, Action act, string? shortcutText = null)
    {
        var mi = new ToolStripMenuItem(text) { ShortcutKeys = keys, ShortcutKeyDisplayString = shortcutText };
        if (ico != null) mi.Image = IconFactory.Get(ico.Value, _px16);
        mi.Click += (_, _) => act();
        return mi;
    }

    private ToolStripButton TB(string tip, Ico ico, Action act)
    {
        var b = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = IconFactory.Get(ico, _px24),
            ImageScaling = ToolStripItemImageScaling.None,
            ToolTipText = tip,
            Margin = new Padding(2, 2, 2, 2),
            Padding = new Padding(3),
        };
        b.Click += (_, _) => act();
        return b;
    }

    private async void Run(Func<Task> f)
    {
        try { await f(); }
        catch (Exception ex) { SetStatus("Error: " + ex.Message); }
    }

    private void BuildMenu()
    {
        _menu.ImageScalingSize = new Size(_px16, _px16);
        _menu.Padding = new Padding(6, 2, 0, 2);

        var recent = new ToolStripMenuItem("Open Rece&nt");
        recent.DropDownOpening += (_, _) => FillRecent(recent);
        recent.DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false });
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            MI("&New", Ico.New, Keys.Control | Keys.N, NewTab),
            MI("New &Window", Ico.Window, Keys.Control | Keys.Shift | Keys.N, NewWindow),
            MI("&Open…", Ico.Open, Keys.Control | Keys.O, () => Run(OpenDialogAsync)),
            recent,
            new ToolStripSeparator(),
            MI("&Save", Ico.Save, Keys.Control | Keys.S, () => Run(() => SaveCurrent(false))),
            MI("Save &As…", Ico.SaveAs, Keys.Control | Keys.Shift | Keys.S, () => Run(() => SaveCurrent(true))),
            MI("&Reload from disk", null, Keys.None, () => Run(ReloadCurrent)),
            new ToolStripSeparator(),
            MI("&Close Tab", Ico.Close, Keys.Control | Keys.W, () => { if (CurrentView is { } v) Run(() => CloseView(v)); }),
            MI("E&xit", null, Keys.Alt | Keys.F4, () => Application.Exit(), "Alt+F4"),
        });

        var undo = MI("&Undo", Ico.Undo, Keys.Control | Keys.Z, DoUndo);
        var redo = MI("&Redo", Ico.Redo, Keys.Control | Keys.Y, DoRedo);
        _undoItems.Add(undo); _redoItems.Add(redo);
        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.AddRange(new ToolStripItem[]
        {
            undo, redo, new ToolStripSeparator(),
            MI("Cu&t", Ico.Cut, Keys.Control | Keys.X, () => Clip(ClipOp.Cut)),
            MI("&Copy", Ico.Copy, Keys.Control | Keys.C, () => Clip(ClipOp.Copy)),
            MI("&Paste", Ico.Paste, Keys.Control | Keys.V, () => Clip(ClipOp.Paste)),
            MI("&Delete", null, Keys.None, () => CurrentView?.DeleteSelection(), "Del"),
            MI("Select &All", null, Keys.Control | Keys.A, () => Clip(ClipOp.SelectAll)),
        });

        var search = new ToolStripMenuItem("&Search");
        search.DropDownItems.AddRange(new ToolStripItem[]
        {
            MI("&Find…", Ico.Find, Keys.Control | Keys.F, () => ShowFind(false)),
            MI("&Replace…", Ico.Replace, Keys.Control | Keys.H, () => ShowFind(true)),
            MI("Find &Next", null, Keys.None, () => FindNext(true), "F3"),
            MI("Find &Previous", null, Keys.None, () => FindNext(false), "Shift+F3"),
        });

        var view = new ToolStripMenuItem("&View");
        var fontSizes = new ToolStripMenuItem("Font &size");
        foreach (var sz in new[] { 8f, 9f, 10f, 11f, 12f, 14f, 16f, 18f, 20f, 24f })
        {
            var s = sz;
            var it = new ToolStripMenuItem($"{s:0} pt");
            it.Click += (_, _) => SetFont(AppSettings.Current.FontFamily, s);
            fontSizes.DropDownItems.Add(it);
        }
        fontSizes.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripMenuItem it in fontSizes.DropDownItems)
                it.Checked = Math.Abs(float.Parse(it.Text!.Split(' ')[0]) - AppSettings.Current.FontSize) < 0.01f;
        };
        _themeSystem = MI("&System default", null, Keys.None, () => ThemeManager.SetMode(ThemeMode.System));
        _themeLight = MI("&Light", null, Keys.None, () => ThemeManager.SetMode(ThemeMode.Light));
        _themeDark = MI("&Dark", null, Keys.None, () => ThemeManager.SetMode(ThemeMode.Dark));
        var theme = new ToolStripMenuItem("&Theme") { Image = IconFactory.Get(Ico.Theme, _px16) };
        theme.DropDownItems.AddRange(new ToolStripItem[] { _themeSystem, _themeLight, _themeDark });
        _formulaItem = MI("&Cell text bar", null, Keys.None, () => SetFormulaBarVisible(!_formulaBar.Visible));
        _headerItem = MI("&First row is header", null, Keys.None, () => CurrentView?.ToggleHeader());
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            MI("Zoom &In", Ico.ZoomIn, Keys.None, () => Zoom(10), "Ctrl++"),
            MI("Zoom &Out", Ico.ZoomOut, Keys.None, () => Zoom(-10), "Ctrl+-"),
            MI("&Reset Zoom", null, Keys.None, () => Zoom(0), "Ctrl+0"),
            new ToolStripSeparator(),
            MI("Grid &Font…", null, Keys.None, PickFont),
            fontSizes,
            new ToolStripSeparator(),
            theme, _formulaItem, _headerItem,
        });
        view.DropDownOpening += (_, _) =>
        {
            var m = AppSettings.Current.Theme;
            _themeSystem.Checked = m == ThemeMode.System; _themeLight.Checked = m == ThemeMode.Light; _themeDark.Checked = m == ThemeMode.Dark;
            _formulaItem.Checked = _formulaBar.Visible;
            _headerItem.Checked = CurrentView?.Document.HasHeader == true;
        };

        var insert = new ToolStripMenuItem("&Insert");
        insert.DropDownItems.AddRange(InsertItems());

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.AddRange(new ToolStripItem[]
        {
            MI("Sort &Ascending (current column)", Ico.SortAsc, Keys.None, () => Run(() => CurrentView?.SortAsync(true) ?? Task.CompletedTask)),
            MI("Sort &Descending (current column)", Ico.SortDesc, Keys.None, () => Run(() => CurrentView?.SortAsync(false) ?? Task.CompletedTask)),
            new ToolStripSeparator(),
            DelimiterMenu("&Delimiter"),
            MI("&Move Tab to New Window", Ico.Window, Keys.None, () => { if (CurrentView is { } v && _tabs.Items.Count > 1) MoveToNewWindow(v, Cursor.Position); }),
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(MI("&About", null, Keys.None, () =>
            MessageBox.Show(this, "CSV Editor 1.0\nMulti-tab, multi-threaded CSV editor for .NET 10.\n\nDrag a tab out of the strip to open it in its own window; drag it onto another window's tab strip to merge.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        _menu.Items.AddRange(new ToolStripItem[] { file, edit, search, view, insert, tools, help });
    }

    private void FillRecent(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        var list = AppSettings.Current.RecentFiles;
        if (list.Count == 0) { menu.DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false }); return; }
        for (int i = 0; i < list.Count; i++)
        {
            var path = list[i];
            string label = i < 9 ? $"&{i + 1}  " : i < 20 ? "    " : "";
            var it = new ToolStripMenuItem(label + path.Replace("&", "&&")) { ToolTipText = path };
            it.Click += (_, _) =>
            {
                if (File.Exists(path)) Run(() => OpenFilesAsync(new[] { path }));
                else
                {
                    MessageBox.Show(this, $"File not found:\n{path}", "Open Recent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    list.RemoveAll(p => p == path); AppSettings.Current.Save();
                }
            };
            menu.DropDownItems.Add(it);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("&Clear list", null, (_, _) => AppSettings.Current.ClearRecent());
    }

    private ToolStripItem[] InsertItems() => new ToolStripItem[]
    {
        MI("Row &Above", Ico.InsertRow, Keys.None, () => CurrentView?.InsertRow(false)),
        MI("Row &Below", Ico.InsertRow, Keys.None, () => CurrentView?.InsertRow(true)),
        MI("Column &Left", Ico.InsertCol, Keys.None, () => CurrentView?.InsertColumn(false)),
        MI("Column &Right", Ico.InsertCol, Keys.None, () => CurrentView?.InsertColumn(true)),
        new ToolStripSeparator(),
        MI("Delete Ro&w(s)", Ico.DeleteRow, Keys.None, () => CurrentView?.DeleteRows()),
        MI("Delete Colu&mn(s)", Ico.DeleteCol, Keys.None, () => CurrentView?.DeleteColumns()),
    };

    private ToolStripMenuItem DelimiterMenu(string title)
    {
        var m = new ToolStripMenuItem(title);
        foreach (var (name, ch) in new[] { ("Comma  ( , )", ','), ("Semicolon  ( ; )", ';'), ("Tab", '\t'), ("Pipe  ( | )", '|') })
        {
            var c = ch;
            var it = new ToolStripMenuItem(name);
            it.Click += (_, _) => SetDelimiter(c);
            m.DropDownItems.Add(it);
        }
        m.DropDownOpening += (_, _) =>
        {
            var d = CurrentView?.Document.Delimiter;
            for (int i = 0; i < m.DropDownItems.Count; i++)
                ((ToolStripMenuItem)m.DropDownItems[i]).Checked = d == CsvParser.Candidates[i];
        };
        return m;
    }

    private void BuildToolbar()
    {
        _toolbar.ImageScalingSize = new Size(_px24, _px24);
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Padding = new Padding(8, 3, 8, 3);
        var undo = TB("Undo (Ctrl+Z)", Ico.Undo, DoUndo);
        var redo = TB("Redo (Ctrl+Y)", Ico.Redo, DoRedo);
        _undoItems.Add(undo); _redoItems.Add(redo);
        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            TB("New (Ctrl+N)", Ico.New, NewTab),
            TB("Open (Ctrl+O)", Ico.Open, () => Run(OpenDialogAsync)),
            TB("Save (Ctrl+S)", Ico.Save, () => Run(() => SaveCurrent(false))),
            TB("Save As (Ctrl+Shift+S)", Ico.SaveAs, () => Run(() => SaveCurrent(true))),
            new ToolStripSeparator(),
            undo, redo,
            new ToolStripSeparator(),
            TB("Cut (Ctrl+X)", Ico.Cut, () => Clip(ClipOp.Cut)),
            TB("Copy (Ctrl+C)", Ico.Copy, () => Clip(ClipOp.Copy)),
            TB("Paste (Ctrl+V)", Ico.Paste, () => Clip(ClipOp.Paste)),
            new ToolStripSeparator(),
            TB("Find (Ctrl+F)", Ico.Find, () => ShowFind(false)),
            TB("Replace (Ctrl+H)", Ico.Replace, () => ShowFind(true)),
            new ToolStripSeparator(),
            TB("Insert row below", Ico.InsertRow, () => CurrentView?.InsertRow(true)),
            TB("Delete row(s)", Ico.DeleteRow, () => CurrentView?.DeleteRows()),
            TB("Insert column right", Ico.InsertCol, () => CurrentView?.InsertColumn(true)),
            TB("Delete column(s)", Ico.DeleteCol, () => CurrentView?.DeleteColumns()),
            new ToolStripSeparator(),
            TB("Sort A → Z", Ico.SortAsc, () => Run(() => CurrentView?.SortAsync(true) ?? Task.CompletedTask)),
            TB("Sort Z → A", Ico.SortDesc, () => Run(() => CurrentView?.SortAsync(false) ?? Task.CompletedTask)),
            new ToolStripSeparator(),
            TB("Zoom in", Ico.ZoomIn, () => Zoom(10)),
            TB("Zoom out", Ico.ZoomOut, () => Zoom(-10)),
            TB("Toggle light / dark theme", Ico.Theme, () => ThemeManager.SetMode(ThemeManager.Current.IsDark ? ThemeMode.Light : ThemeMode.Dark)),
        });
    }

    private void BuildFormulaBar()
    {
        _formulaBar.Controls.Add(_formula);
        _formulaBar.Controls.Add(_caption);
        _formulaBar.Controls.Add(_addr);
        _formula.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                CurrentView?.SetCurrentCellText(_formula.Text);
                CurrentView?.Grid.Focus();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                UpdateFormula();
                CurrentView?.Grid.Focus();
                e.SuppressKeyPress = true;
            }
        };
    }

    private void BuildFindBar()
    {
        Button B(string text, EventHandler click, int w = 0)
        {
            var b = new Button { Text = text, AutoSize = w == 0, Height = 28, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 3, 0) };
            if (w > 0) b.Width = w;
            b.Click += click;
            return b;
        }
        Label L(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 0, 4, 0) };
        _findText.Anchor = _replaceText.Anchor = _matchCase.Anchor = AnchorStyles.Left;
        _findText.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { FindNext(!e.Shift); e.SuppressKeyPress = true; } };
        _replaceText.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ReplaceOne(); e.SuppressKeyPress = true; } };

        var replaceLabel = L("Replace:");
        var rep = B("Replace", (_, _) => ReplaceOne());
        var repAll = B("Replace All", (_, _) => ReplaceAll());
        _replaceControls.AddRange(new Control[] { replaceLabel, _replaceText, rep, repAll });

        _findBar.Controls.AddRange(new Control[]
        {
            L("Find:"), _findText, B("▲", (_, _) => FindNext(false), 32), B("▼", (_, _) => FindNext(true), 32), _matchCase,
            replaceLabel, _replaceText, rep, repAll, B("✕", (_, _) => HideFind(), 32),
        });
        foreach (var c in _replaceControls) c.Visible = false;
    }

    private void BuildStatus()
    {
        _status.ImageScalingSize = new Size(_px16, _px16);
        _status.SizingGrip = false;
        _delimButton.DropDownItems.Clear();
        foreach (var (name, ch) in new[] { ("Comma  ( , )", ','), ("Semicolon  ( ; )", ';'), ("Tab", '\t'), ("Pipe  ( | )", '|') })
        {
            var c = ch;
            _delimButton.DropDownItems.Add(name, null, (_, _) => SetDelimiter(c));
        }
        _delimButton.ToolTipText = "Delimiter used when saving (click to change)";
        _zoomOut.Click += (_, _) => Zoom(-10);
        _zoomIn.Click += (_, _) => Zoom(10);
        _zoomLabel.Click += (_, _) => Zoom(0);
        foreach (var z in new[] { _zoomOut, _zoomIn, _zoomLabel }) { z.AutoSize = false; z.Width = z == _zoomLabel ? 54 : 26; }
        _status.Items.AddRange(new ToolStripItem[]
        {
            _statusText, _sizeLabel, new ToolStripSeparator(), _delimButton, new ToolStripSeparator(), _encLabel, new ToolStripSeparator(), _zoomOut, _zoomLabel, _zoomIn,
        });
    }

    private void BuildCellMenu()
    {
        _cellMenu.ImageScalingSize = new Size(_px16, _px16);
        _cellMenu.Items.AddRange(new ToolStripItem[]
        {
            MI("Cu&t", Ico.Cut, Keys.None, () => Clip(ClipOp.Cut), "Ctrl+X"),
            MI("&Copy", Ico.Copy, Keys.None, () => Clip(ClipOp.Copy), "Ctrl+C"),
            MI("&Paste", Ico.Paste, Keys.None, () => Clip(ClipOp.Paste), "Ctrl+V"),
            MI("&Clear", null, Keys.None, () => CurrentView?.DeleteSelection(), "Del"),
            new ToolStripSeparator(),
        });
        _cellMenu.Items.AddRange(InsertItems());
        _cellMenu.Items.Add(new ToolStripSeparator());
        _cellMenu.Items.Add(MI("Sort A → Z", Ico.SortAsc, Keys.None, () => Run(() => CurrentView?.SortAsync(true) ?? Task.CompletedTask)));
        _cellMenu.Items.Add(MI("Sort Z → A", Ico.SortDesc, Keys.None, () => Run(() => CurrentView?.SortAsync(false) ?? Task.CompletedTask)));
    }

    // ================================================================ theming

    private void ApplyTheme()
    {
        var p = ThemeManager.Current;
        SuspendLayout();
        ThemeManager.Apply(this);
        _tabs.Invalidate();
        _content.BackColor = p.Window;
        _addr.BackColor = p.Surface; _addr.ForeColor = p.Text; _addr.BorderStyle = BorderStyle.FixedSingle;
        _formulaBar.BackColor = p.Header;
        _findBar.BackColor = p.Header;
        _menu.BackColor = p.Header; _toolbar.BackColor = p.Header; _status.BackColor = p.Header;
        foreach (var s in new ToolStrip[] { _menu, _toolbar, _status, _cellMenu }) s.ForeColor = p.Text;
        _cellMenu.BackColor = p.Surface;
        _caption.BackColor = Color.Transparent;
        ResumeLayout(true);
        if (IsHandleCreated) Native.SetDarkTitleBar(Handle, p.IsDark);
        Invalidate(true);
    }

    // ================================================================ tab / view management

    private TabItem ItemFor(DocumentView v) => _tabs.Items.First(t => t.Tag == v);

    private void AddView(DocumentView v, int? index = null)
    {
        v.Visible = false;
        v.Dock = DockStyle.Fill;
        _content.Controls.Add(v);
        v.Grid.ContextMenuStrip = _cellMenu;
        var item = _tabs.Add(v.DisplayName, v, index);

        Action state = () => UpdateTab(v), sel = () => { if (v == CurrentView) UpdateFormula(); };
        Action zoom = () => { if (v == CurrentView) UpdateZoomLabel(); };
        Action<string> status = s => SetStatus(s);
        v.StateChanged += state; v.SelectionChanged += sel; v.ZoomChanged += zoom; v.Status += status;
        _unbind[v] = () => { v.StateChanged -= state; v.SelectionChanged -= sel; v.ZoomChanged -= zoom; v.Status -= status; };

        UpdateTab(v);
        _tabs.Select(item);
    }

    /// <summary>Detaches a view from this window without disposing it.</summary>
    private void RemoveView(DocumentView v)
    {
        if (_unbind.Remove(v, out var un)) un();
        _content.Controls.Remove(v);
        v.Grid.ContextMenuStrip = null;
        _tabs.Remove(ItemFor(v));
    }

    private void UpdateTab(DocumentView v)
    {
        var item = _tabs.Items.FirstOrDefault(t => t.Tag == v);
        if (item == null) return;
        item.Text = v.DisplayName;
        item.Modified = v.Document.Modified;
        item.Busy = v.IsBusy;
        _tabs.Touch(item);
        if (v == CurrentView)
        {
            UpdateTitle();
            UpdateCommands();
            UpdateInfo();
            UpdateFormula();
        }
    }

    private void ShowCurrent()
    {
        var cur = CurrentView;
        foreach (var v in Views) v.Visible = v == cur;
        if (cur != null)
        {
            cur.BringToFront();
            if (IsHandleCreated && !_findBar.ContainsFocus && !_formula.Focused) cur.Grid.Focus();
        }
        UpdateTitle(); UpdateCommands(); UpdateInfo(); UpdateFormula(); UpdateZoomLabel();
    }

    private void UpdateTitle()
    {
        var v = CurrentView;
        Text = v == null ? "CSV Editor" : $"{v.DisplayName}{(v.Document.Modified ? " •" : "")} - CSV Editor";
    }

    private void UpdateCommands()
    {
        var d = CurrentView?.Document;
        foreach (var i in _undoItems) i.Enabled = d?.CanUndo == true;
        foreach (var i in _redoItems) i.Enabled = d?.CanRedo == true;
    }

    private void UpdateInfo()
    {
        var v = CurrentView;
        if (v == null) return;
        var d = v.Document;
        _sizeLabel.Text = $"{d.LastUsedRow() + 1:N0} rows × {d.LastUsedColumn() + 1:N0} cols";
        _delimButton.Text = "Delimiter: " + d.Delimiter switch { '\t' => "Tab", var c => c.ToString() };
        _encLabel.Text = d.Encoding.WebName.ToUpperInvariant() + (d.Encoding.GetPreamble().Length > 0 ? " BOM" : "") + "  " + d.NewLine.Replace("\r", "CR").Replace("\n", "LF");
    }

    private void UpdateFormula()
    {
        var v = CurrentView;
        _addr.Text = v?.CellAddress ?? "";
        if (!_formula.Focused) _formula.Text = v?.CurrentCellText ?? "";
    }

    private void UpdateZoomLabel() => _zoomLabel.Text = (CurrentView?.ZoomPercent ?? 100) + "%";

    private void SetStatus(string s) => _statusText.Text = s;

    private void SetFormulaBarVisible(bool on)
    {
        _formulaBar.Visible = on;
        AppSettings.Current.ShowFormulaBar = on;
        AppSettings.Current.Save();
    }

    // ================================================================ file operations

    private void NewTab() => AddView(new DocumentView());

    private static void NewWindow() => new MainForm().Show();

    private async Task OpenDialogAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open CSV",
            Multiselect = true,
            Filter = "CSV / text files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await OpenFilesAsync(dlg.FileNames);
    }

    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        var loads = new List<Task>();
        foreach (var raw in paths)
        {
            var path = Path.GetFullPath(raw);
            var existing = Views.FirstOrDefault(v => string.Equals(v.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { _tabs.Select(ItemFor(existing)); continue; }

            var cur = CurrentView;
            bool reuse = cur != null && cur.Document.FilePath == null && !cur.Document.Modified && !cur.IsBusy && cur.Document.LastUsedRow() < 0;
            var view = reuse ? cur! : new DocumentView();
            if (!reuse) AddView(view);
            loads.Add(LoadInto(view, path, reuse));
        }
        await Task.WhenAll(loads);
    }

    private async Task LoadInto(DocumentView view, string path, bool reused)
    {
        bool ok = await view.LoadFileAsync(path);
        if (!ok && !reused && !view.IsDisposed && _tabs.Items.Any(t => t.Tag == view))
        {
            RemoveView(view);
            view.Dispose();
            EnsureOneTab();
        }
    }

    private async Task ReloadCurrent()
    {
        var v = CurrentView;
        if (v?.Document.FilePath is not { } path) return;
        if (v.Document.Modified &&
            MessageBox.Show(this, "Discard unsaved changes and reload from disk?", "Reload", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await v.LoadFileAsync(path);
    }

    private async Task<bool> SaveCurrent(bool saveAs) => CurrentView is { } v && await SaveView(v, saveAs);

    private async Task<bool> SaveView(DocumentView v, bool saveAs)
    {
        var path = v.Document.FilePath;
        if (saveAs || path == null)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Save CSV",
                Filter = "CSV (*.csv)|*.csv|Tab-separated (*.tsv)|*.tsv|Text (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = path != null ? Path.GetFileName(path) : "Untitled.csv",
                InitialDirectory = path != null ? Path.GetDirectoryName(path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
            if (string.Equals(Path.GetExtension(path), ".tsv", StringComparison.OrdinalIgnoreCase)) v.Document.Delimiter = '\t';
        }
        return await v.SaveAsync(path);
    }

    private async Task<bool> ConfirmClose(DocumentView v)
    {
        if (!v.Document.Modified) return true;
        _tabs.Select(ItemFor(v));
        var r = MessageBox.Show(this, $"Save changes to \"{v.DisplayName}\"?", "CSV Editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.No) return true;
        return await SaveView(v, false);
    }

    private async Task CloseView(DocumentView v)
    {
        if (!await ConfirmClose(v)) return;
        v.CancelBusy();
        RemoveView(v);
        v.Dispose();
        EnsureOneTab();
    }

    private void EnsureOneTab()
    {
        if (_tabs.Items.Count > 0 || _closing) return;
        if (WindowManager.Windows.Count > 1) { _closing = true; Close(); }
        else NewTab();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_closing || e.Cancel) return;
        if (Views.Any(v => v.Document.Modified))
        {
            e.Cancel = true;
            Run(ConfirmAllAndClose);
        }
    }

    private async Task ConfirmAllAndClose()
    {
        foreach (var v in Views.ToList())
            if (!await ConfirmClose(v)) return;
        _closing = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ThemeManager.Changed -= ApplyTheme;
        foreach (var v in Views.ToList()) { v.CancelBusy(); RemoveView(v); v.Dispose(); }
        base.OnFormClosed(e);
    }

    // ================================================================ detachable tabs

    private void OnTabDropped(object? sender, TabDroppedEventArgs e)
    {
        if (e.Item.Tag is not DocumentView v) return;
        var target = WindowManager.Windows.FirstOrDefault(w => w != this && w.Visible && w.WindowState != FormWindowState.Minimized &&
            w._tabs.RectangleToScreen(w._tabs.ClientRectangle) is var r && Rectangle.Inflate(r, 0, 12).Contains(e.ScreenPoint));
        if (target != null)
        {
            int idx = target._tabs.IndexAtScreenPoint(e.ScreenPoint);
            RemoveView(v);
            target.AdoptView(v, idx);
            EnsureOneTab();
        }
        else if (_tabs.Items.Count > 1) MoveToNewWindow(v, e.ScreenPoint);
    }

    public void AdoptView(DocumentView v, int index)
    {
        AddView(v, index);
        Activate();
    }

    private void MoveToNewWindow(DocumentView v, Point screenPoint)
    {
        RemoveView(v);
        var f = new MainForm(v)
        {
            StartPosition = FormStartPosition.Manual,
            Size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size,
        };
        f.Location = new Point(screenPoint.X - 140, screenPoint.Y - 18);
        f.Show();
        f.Activate();
        EnsureOneTab();
    }

    // ================================================================ edit commands

    private enum ClipOp { Cut, Copy, Paste, SelectAll }

    private static TextBoxBase? FocusedTextBox() => Control.FromHandle(Native.GetFocus()) as TextBoxBase;

    private void Clip(ClipOp op)
    {
        if (FocusedTextBox() is { } tb)
        {
            switch (op)
            {
                case ClipOp.Cut: tb.Cut(); break;
                case ClipOp.Copy: tb.Copy(); break;
                case ClipOp.Paste: tb.Paste(); break;
                case ClipOp.SelectAll: tb.SelectAll(); break;
            }
            return;
        }
        var v = CurrentView;
        if (v == null) return;
        switch (op)
        {
            case ClipOp.Cut: Run(() => v.CopyAsync(true)); break;
            case ClipOp.Copy: Run(() => v.CopyAsync(false)); break;
            case ClipOp.Paste: Run(v.PasteAsync); break;
            case ClipOp.SelectAll: v.SelectAll(); break;
        }
    }

    private void DoUndo()
    {
        if (FocusedTextBox() is { } tb && tb.CanUndo) { tb.Undo(); return; }
        CurrentView?.Undo();
    }

    private void DoRedo() => CurrentView?.Redo();

    private void Zoom(int delta)
    {
        if (CurrentView is not { } v) return;
        v.ZoomPercent = delta == 0 ? 100 : v.ZoomPercent + delta;
        UpdateZoomLabel();
    }

    private void SetDelimiter(char d)
    {
        if (CurrentView is not { } v) return;
        v.Document.Delimiter = d;
        if (v.Document.FilePath is { } path && !v.Document.Modified && !v.IsBusy) Run(() => v.LoadFileAsync(path, d));
        else SetStatus("Delimiter will be used when saving");
        UpdateInfo();
    }

    private void PickFont()
    {
        using var dlg = new FontDialog
        {
            ShowEffects = false,
            FontMustExist = true,
            MinSize = 6,
            MaxSize = 48,
            Font = new Font(AppSettings.Current.FontFamily, AppSettings.Current.FontSize),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetFont(dlg.Font.FontFamily.Name, dlg.Font.SizeInPoints);
    }

    private static void SetFont(string family, float size)
    {
        AppSettings.Current.FontFamily = family;
        AppSettings.Current.FontSize = size;
        AppSettings.Current.Save();
        foreach (var w in WindowManager.Windows.ToList())
            foreach (var v in w.Views) v.ReapplyFont();
    }

    // ================================================================ find / replace

    private void ShowFind(bool replace)
    {
        _findBar.Visible = true;
        foreach (var c in _replaceControls) c.Visible = replace;
        var v = CurrentView;
        if (v != null && _findText.Text.Length == 0 && v.CurrentCellText.Length is > 0 and < 80) _findText.Text = v.CurrentCellText;
        _findText.Focus();
        _findText.SelectAll();
    }

    private void HideFind()
    {
        _findBar.Visible = false;
        CurrentView?.Grid.Focus();
    }

    private void FindNext(bool forward)
    {
        if (!_findBar.Visible) { ShowFind(false); return; }
        if (CurrentView is not { } v) return;
        Run(async () =>
        {
            if (!await v.FindAsync(_findText.Text, _matchCase.Checked, forward))
                System.Media.SystemSounds.Beep.Play();
        });
    }

    private void ReplaceOne()
    {
        if (CurrentView is { } v) Run(() => v.ReplaceCurrentAsync(_findText.Text, _replaceText.Text, _matchCase.Checked));
    }

    private void ReplaceAll()
    {
        if (CurrentView is { } v) Run(() => v.ReplaceAllAsync(_findText.Text, _replaceText.Text, _matchCase.Checked));
    }

    // ================================================================ keys

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Tab: SwitchTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: SwitchTab(-1); return true;
            case Keys.Control | Keys.T: NewTab(); return true;
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Add: Zoom(10); return true;
            case Keys.Control | Keys.OemMinus:
            case Keys.Control | Keys.Subtract: Zoom(-10); return true;
            case Keys.Control | Keys.D0:
            case Keys.Control | Keys.NumPad0: Zoom(0); return true;
            case Keys.F3: FindNext(true); return true;
            case Keys.Shift | Keys.F3: FindNext(false); return true;
            case Keys.Escape when _findBar.Visible && _findBar.ContainsFocus: HideFind(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SwitchTab(int dir)
    {
        int n = _tabs.Items.Count;
        if (n > 1) _tabs.SelectedIndex = (_tabs.SelectedIndex + dir + n) % n;
    }
}
