using CsvEditor.Core;
using Microsoft.Win32;

namespace CsvEditor.Theming;

public sealed record Palette(
    bool IsDark,
    Color Window,      // form / panels
    Color Surface,     // grid cells, text boxes
    Color Header,      // grid headers, toolbars, status
    Color Text,
    Color Subtle,
    Color Border,
    Color Grid,
    Color Accent,
    Color AccentSoft,  // selection
    Color TabBar,
    Color TabIdle,
    Color Hover);

public static class ThemeManager
{
    public static readonly Palette Light = new(false,
        Window: Color.FromArgb(243, 243, 243), Surface: Color.White, Header: Color.FromArgb(247, 247, 247),
        Text: Color.FromArgb(31, 31, 31), Subtle: Color.FromArgb(107, 107, 107), Border: Color.FromArgb(217, 217, 217),
        Grid: Color.FromArgb(227, 227, 227), Accent: Color.FromArgb(15, 108, 189), AccentSoft: Color.FromArgb(204, 228, 247),
        TabBar: Color.FromArgb(232, 232, 232), TabIdle: Color.FromArgb(222, 222, 222), Hover: Color.FromArgb(229, 229, 229));

    public static readonly Palette Dark = new(true,
        Window: Color.FromArgb(32, 32, 32), Surface: Color.FromArgb(39, 39, 39), Header: Color.FromArgb(47, 47, 47),
        Text: Color.FromArgb(240, 240, 240), Subtle: Color.FromArgb(160, 160, 160), Border: Color.FromArgb(62, 62, 62),
        Grid: Color.FromArgb(56, 56, 56), Accent: Color.FromArgb(96, 205, 255), AccentSoft: Color.FromArgb(45, 74, 99),
        TabBar: Color.FromArgb(24, 24, 24), TabIdle: Color.FromArgb(36, 36, 36), Hover: Color.FromArgb(55, 55, 55));

    public static Palette Current { get; private set; } = Light;
    public static event Action? Changed;

    public static void Initialize()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && AppSettings.Current.Theme == ThemeMode.System)
                Refresh();
        };
        Refresh();
    }

    public static void SetMode(ThemeMode mode)
    {
        AppSettings.Current.Theme = mode;
        AppSettings.Current.Save();
        Refresh();
    }

    private static void Refresh()
    {
        bool dark = AppSettings.Current.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDark(),
        };
        Current = dark ? Dark : Light;
        ToolStripManager.Renderer = new ThemedRenderer(Current);
        Changed?.Invoke();
    }

    private static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Themes a standard control tree. Custom controls handle their own palette.</summary>
    public static void Apply(Control root)
    {
        var p = Current;
        if (root is Form f)
        {
            f.BackColor = p.Window;
            f.ForeColor = p.Text;
            if (f.IsHandleCreated) Native.SetDarkTitleBar(f.Handle, p.IsDark);
        }
        ApplyChildren(root, p);
    }

    private static void ApplyChildren(Control c, Palette p)
    {
        foreach (Control child in c.Controls)
        {
            switch (child)
            {
                case ToolStrip ts:
                    ts.ForeColor = p.Text;
                    ts.BackColor = p.Header;
                    continue;
                case TextBox tb:
                    tb.BackColor = p.Surface; tb.ForeColor = p.Text; tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = p.Header; b.ForeColor = p.Text;
                    b.FlatAppearance.BorderColor = p.Border;
                    b.FlatAppearance.MouseOverBackColor = p.Hover;
                    b.UseVisualStyleBackColor = false;
                    break;
                case ITabAware:
                    continue;
                case Label or CheckBox or Panel or FlowLayoutPanel or TableLayoutPanel:
                    child.ForeColor = p.Text;
                    if (child is not Label) child.BackColor = p.Header;
                    else child.BackColor = Color.Transparent;
                    break;
            }
            ApplyChildren(child, p);
        }
    }
}

/// <summary>Marker for custom controls that style themselves (skipped by the generic walker).</summary>
public interface ITabAware { }
