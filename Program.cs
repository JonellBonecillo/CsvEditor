using CsvEditor.Theming;
using CsvEditor.Ui;

namespace CsvEditor;

/// <summary>Tracks open windows; the process exits when the last one closes.</summary>
internal sealed class WindowManager : ApplicationContext
{
    public static readonly List<MainForm> Windows = new();
    private static WindowManager? _instance;

    public WindowManager(string[] args)
    {
        _instance = this;
        var first = new MainForm();
        first.Show();
        var files = args.Where(File.Exists).ToArray();
        if (files.Length > 0) _ = first.OpenFilesAsync(files);
    }

    public static void Register(MainForm f)
    {
        Windows.Add(f);
        f.FormClosed += (_, _) =>
        {
            Windows.Remove(f);
            if (Windows.Count == 0) _instance?.ExitThread();
        };
    }
}

internal static class Program
{
    /// <summary>Writes a multi-size PNG-based .ico from the code-drawn app icon (used for the exe's ApplicationIcon).</summary>
    private static void ExportIcon(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = sizes.Select(s =>
        {
            using var ms = new MemoryStream();
            IconFactory.Get(Ico.File, s).Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--export-icon") { ExportIcon(args[1]); return; }
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.ToString(), "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        ThemeManager.Initialize();
        Application.Run(new WindowManager(args));
    }
}
