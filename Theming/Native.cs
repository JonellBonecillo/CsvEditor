using System.Runtime.InteropServices;

namespace CsvEditor.Theming;

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point p);

    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on early Win10 builds)
        if (DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref v, sizeof(int));
    }

    /// <summary>Applies dark/light Explorer theme (scrollbars etc.) to a window and all its child windows.</summary>
    public static void SetControlTheme(IntPtr hwnd, bool dark)
    {
        string name = dark ? "DarkMode_Explorer" : "Explorer";
        SetWindowTheme(hwnd, name, null);
        EnumChildWindows(hwnd, (h, _) => { SetWindowTheme(h, name, null); return true; }, IntPtr.Zero);
    }
}
