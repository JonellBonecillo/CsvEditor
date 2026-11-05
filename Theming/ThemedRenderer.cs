namespace CsvEditor.Theming;

internal sealed class ThemedColors : ProfessionalColorTable
{
    private readonly Palette _p;
    public ThemedColors(Palette p) { _p = p; UseSystemColors = false; }

    private Color Sel => _p.IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(214, 232, 247);
    private Color Press => _p.IsDark ? Color.FromArgb(90, 90, 90) : Color.FromArgb(190, 218, 243);

    public override Color ToolStripDropDownBackground => _p.Surface;
    public override Color ImageMarginGradientBegin => _p.Surface;
    public override Color ImageMarginGradientMiddle => _p.Surface;
    public override Color ImageMarginGradientEnd => _p.Surface;
    public override Color MenuBorder => _p.Border;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Sel;
    public override Color MenuItemSelectedGradientBegin => Sel;
    public override Color MenuItemSelectedGradientEnd => Sel;
    public override Color MenuItemPressedGradientBegin => _p.Surface;
    public override Color MenuItemPressedGradientMiddle => _p.Surface;
    public override Color MenuItemPressedGradientEnd => _p.Surface;
    public override Color MenuStripGradientBegin => _p.Header;
    public override Color MenuStripGradientEnd => _p.Header;
    public override Color ToolStripGradientBegin => _p.Header;
    public override Color ToolStripGradientMiddle => _p.Header;
    public override Color ToolStripGradientEnd => _p.Header;
    public override Color ToolStripBorder => _p.Border;
    public override Color ToolStripContentPanelGradientBegin => _p.Window;
    public override Color ToolStripContentPanelGradientEnd => _p.Window;
    public override Color ToolStripPanelGradientBegin => _p.Window;
    public override Color ToolStripPanelGradientEnd => _p.Window;
    public override Color StatusStripGradientBegin => _p.Header;
    public override Color StatusStripGradientEnd => _p.Header;
    public override Color SeparatorDark => _p.Border;
    public override Color SeparatorLight => _p.Border;
    public override Color ButtonSelectedHighlight => Sel;
    public override Color ButtonSelectedHighlightBorder => Sel;
    public override Color ButtonSelectedGradientBegin => Sel;
    public override Color ButtonSelectedGradientMiddle => Sel;
    public override Color ButtonSelectedGradientEnd => Sel;
    public override Color ButtonSelectedBorder => Sel;
    public override Color ButtonPressedHighlight => Press;
    public override Color ButtonPressedHighlightBorder => Press;
    public override Color ButtonPressedGradientBegin => Press;
    public override Color ButtonPressedGradientMiddle => Press;
    public override Color ButtonPressedGradientEnd => Press;
    public override Color ButtonPressedBorder => Press;
    public override Color ButtonCheckedGradientBegin => Press;
    public override Color ButtonCheckedGradientMiddle => Press;
    public override Color ButtonCheckedGradientEnd => Press;
    public override Color ButtonCheckedHighlight => Press;
    public override Color ButtonCheckedHighlightBorder => Press;
    public override Color CheckBackground => Press;
    public override Color CheckSelectedBackground => Press;
    public override Color CheckPressedBackground => Press;
    public override Color GripDark => _p.Border;
    public override Color GripLight => _p.Border;
    public override Color OverflowButtonGradientBegin => _p.Header;
    public override Color OverflowButtonGradientMiddle => _p.Header;
    public override Color OverflowButtonGradientEnd => _p.Header;
}

internal sealed class ThemedRenderer : ToolStripProfessionalRenderer
{
    private readonly Palette _p;

    public ThemedRenderer(Palette p) : base(new ThemedColors(p))
    {
        _p = p;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _p.Text : _p.Subtle;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _p.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = new Rectangle(Point.Empty, e.Item.Size);
        using var br = new SolidBrush(_p.AccentSoft);
        e.Graphics.FillRectangle(br, new Rectangle(e.ImageRectangle.Location, e.ImageRectangle.Size));
        using var pen = new Pen(_p.Text, 2f);
        var ir = e.ImageRectangle;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            new Point(ir.Left + ir.Width / 5, ir.Top + ir.Height / 2),
            new Point(ir.Left + ir.Width * 2 / 5, ir.Top + ir.Height * 7 / 10),
            new Point(ir.Right - ir.Width / 5, ir.Top + ir.Height * 3 / 10),
        });
    }
}
