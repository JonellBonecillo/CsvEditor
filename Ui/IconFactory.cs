using System.Drawing.Drawing2D;

namespace CsvEditor.Ui;

public enum Ico
{
    New, Open, Save, SaveAs, Find, Undo, Redo, ZoomIn, ZoomOut, Theme, Cut, Copy, Paste,
    InsertRow, DeleteRow, InsertCol, DeleteCol, SortAsc, SortDesc, File, Window, Close, Replace
}

/// <summary>Draws colorful vector-style icons at any pixel size (no image assets needed).</summary>
public static class IconFactory
{
    private static readonly Dictionary<(Ico, int), Bitmap> Cache = new();

    public static Bitmap Get(Ico kind, int px)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((kind, px), out var b)) return b;
            b = Draw(kind, px);
            Cache[(kind, px)] = b;
            return b;
        }
    }

    public static Icon AppIcon(int px = 32)
    {
        using var bmp = Draw(Ico.File, px);
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static Bitmap Draw(Ico kind, int px)
    {
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(px / 32f, px / 32f);
        Paint(g, kind);
        return bmp;
    }

    private static Color C(string hex) => ColorTranslator.FromHtml(hex);
    private static Brush Grad(RectangleF r, string a, string b) => new LinearGradientBrush(r, C(a), C(b), 90f);
    private static Pen P(string hex, float w) => new(C(hex), w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

    private static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void Page(Graphics g, RectangleF r, string fillA, string fillB, string edge)
    {
        float fold = r.Width * 0.32f;
        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(r.Left, r.Top), new PointF(r.Right - fold, r.Top), new PointF(r.Right, r.Top + fold),
            new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom),
        });
        using (var br = Grad(r, fillA, fillB)) g.FillPath(br, path);
        using (var pen = P(edge, 1.4f)) g.DrawPath(pen, path);
        using var tri = new SolidBrush(C("#BBD7F2"));
        g.FillPolygon(tri, new[] { new PointF(r.Right - fold, r.Top), new PointF(r.Right, r.Top + fold), new PointF(r.Right - fold, r.Top + fold) });
        using var pen2 = P(edge, 1.2f);
        g.DrawPolygon(pen2, new[] { new PointF(r.Right - fold, r.Top), new PointF(r.Right, r.Top + fold), new PointF(r.Right - fold, r.Top + fold) });
        path.Dispose();
    }

    private static void Badge(Graphics g, float cx, float cy, float r, string color, bool plus)
    {
        using var br = new SolidBrush(C(color));
        g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2);
        using var pen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx - r * 0.5f, cy, cx + r * 0.5f, cy);
        if (plus) g.DrawLine(pen, cx, cy - r * 0.5f, cx, cy + r * 0.5f);
    }

    private static void MiniGrid(Graphics g, string? hiRow, string? hiCol, int hiIndex)
    {
        var r = new RectangleF(4, 5, 24, 22);
        using (var path = Round(r, 2.5f))
        {
            using var br = Grad(r, "#FFFFFF", "#EAF1F8");
            g.FillPath(br, path);
            using var pen = P("#5B7FA6", 1.4f);
            g.DrawPath(pen, path);
        }
        if (hiRow != null)
        {
            using var br = new SolidBrush(C(hiRow));
            g.FillRectangle(br, 5, 5 + 7.33f * hiIndex + 0.7f, 22, 7.2f);
        }
        if (hiCol != null)
        {
            using var br = new SolidBrush(C(hiCol));
            g.FillRectangle(br, 4 + 8f * hiIndex + 0.7f, 6, 7.8f, 20);
        }
        using var grid = P("#7FA2C8", 1.1f);
        for (int i = 1; i < 3; i++)
        {
            g.DrawLine(grid, 4, 5 + 7.33f * i, 28, 5 + 7.33f * i);
            g.DrawLine(grid, 4 + 8 * i, 5, 4 + 8 * i, 27);
        }
    }

    private static void Floppy(Graphics g, string body)
    {
        var r = new RectangleF(4, 4, 24, 24);
        using (var path = Round(r, 3f))
        {
            using var br = Grad(r, body, "#1B5CC0");
            g.FillPath(br, path);
            using var pen = P("#174C9E", 1.2f);
            g.DrawPath(pen, path);
        }
        using var w = new SolidBrush(Color.White);
        g.FillRectangle(w, 9, 4.8f, 14, 8);
        using var lab = new SolidBrush(C("#DCE6F5"));
        g.FillRectangle(lab, 8, 17, 16, 10);
        using var shut = new SolidBrush(C("#2B3A55"));
        g.FillRectangle(shut, 18.5f, 6, 3, 5.5f);
        using var ln = P("#9CB0CC", 1f);
        g.DrawLine(ln, 10, 20.5f, 22, 20.5f);
        g.DrawLine(ln, 10, 23.5f, 22, 23.5f);
    }

    private static void Magnifier(Graphics g, string ring, string fill, string handle)
    {
        using (var pen = P(handle, 4.2f)) g.DrawLine(pen, 19.5f, 19.5f, 27.5f, 27.5f);
        var r = new RectangleF(4, 4, 18, 18);
        using (var br = new SolidBrush(C(fill))) g.FillEllipse(br, r);
        using var pen2 = P(ring, 3f);
        g.DrawEllipse(pen2, r);
        using var hi = new Pen(Color.FromArgb(160, Color.White), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(hi, 8, 8, 10, 10, 200, 70);
    }

    private static void CurvedArrow(Graphics g, bool undo, string color)
    {
        if (!undo) { g.TranslateTransform(32, 0); g.ScaleTransform(-1, 1); }
        using var pen = P(color, 3.6f);
        g.DrawArc(pen, 7, 10, 19, 17, 200, 230);
        using var br = new SolidBrush(C(color));
        g.FillPolygon(br, new[] { new PointF(3, 11), new PointF(14, 5), new PointF(13, 17) });
    }

    private static void Paint(Graphics g, Ico kind)
    {
        switch (kind)
        {
            case Ico.File:
            {
                var r = new RectangleF(3, 3, 26, 26);
                using (var path = Round(r, 5f))
                {
                    using var br = Grad(r, "#34C759", "#1E8E3E");
                    g.FillPath(br, path);
                }
                using var white = new SolidBrush(Color.White);
                g.FillRectangle(white, 7, 9, 18, 15);
                using var pen = P("#1E8E3E", 1.3f);
                g.DrawLine(pen, 7, 14, 25, 14); g.DrawLine(pen, 7, 19, 25, 19);
                g.DrawLine(pen, 13, 9, 13, 24); g.DrawLine(pen, 19, 9, 19, 24);
                using var hd = new SolidBrush(C("#BFEBCB"));
                g.FillRectangle(hd, 7, 9, 18, 5);
                g.DrawLine(pen, 7, 14, 25, 14); g.DrawLine(pen, 13, 9, 13, 14); g.DrawLine(pen, 19, 9, 19, 14);
                break;
            }
            case Ico.New:
                Page(g, new RectangleF(6, 3, 20, 26), "#FFFFFF", "#E6EEF7", "#5B7FA6");
                Badge(g, 23, 23, 7, "#2EB872", true);
                break;
            case Ico.Open:
            {
                using var back = new SolidBrush(C("#E09B1A"));
                using (var p = Round(new RectangleF(3, 7, 26, 20), 3f)) g.FillPath(back, p);
                using var tab = new SolidBrush(C("#E09B1A"));
                g.FillPolygon(tab, new[] { new PointF(3, 7), new PointF(12, 7), new PointF(15, 10), new PointF(3, 10) });
                var front = new RectangleF(3, 12, 26, 15);
                using var path = new GraphicsPath();
                path.AddPolygon(new[] { new PointF(7, 12), new PointF(31, 12), new PointF(27, 27), new PointF(3, 27) });
                using var br = Grad(front, "#FFD966", "#FFB300");
                g.FillPath(br, path);
                using var pen = P("#C98400", 1.2f);
                g.DrawPath(pen, path);
                break;
            }
            case Ico.Save: Floppy(g, "#3B8CF5"); break;
            case Ico.SaveAs:
                Floppy(g, "#8E6CF0");
                Badge(g, 24, 24, 6.5f, "#F59E0B", true);
                break;
            case Ico.Find: Magnifier(g, "#1E88E5", "#DDEFFF", "#D9822B"); break;
            case Ico.ZoomIn:
                Magnifier(g, "#2E9E5B", "#E0F7E9", "#D9822B");
                using (var pen = P("#2E9E5B", 2.2f)) { g.DrawLine(pen, 9.5f, 13, 16.5f, 13); g.DrawLine(pen, 13, 9.5f, 13, 16.5f); }
                break;
            case Ico.ZoomOut:
                Magnifier(g, "#D6453D", "#FDE6E4", "#D9822B");
                using (var pen = P("#D6453D", 2.2f)) g.DrawLine(pen, 9.5f, 13, 16.5f, 13);
                break;
            case Ico.Undo: CurvedArrow(g, true, "#F08A24"); break;
            case Ico.Redo: CurvedArrow(g, false, "#2E9E5B"); break;
            case Ico.Theme:
            {
                var r = new RectangleF(4, 4, 24, 24);
                using var day = new SolidBrush(C("#FFC22E"));
                g.FillPie(day, 4, 4, 24, 24, 270, 180);
                using var night = new SolidBrush(C("#2B3A67"));
                g.FillPie(night, 4, 4, 24, 24, 90, 180);
                using var pen = P("#6B7A99", 1.4f);
                g.DrawEllipse(pen, r);
                using var star = new SolidBrush(Color.White);
                g.FillEllipse(star, 9, 10, 3, 3); g.FillEllipse(star, 12, 19, 2.2f, 2.2f);
                using var sun = new SolidBrush(C("#FF8A00"));
                g.FillEllipse(sun, 19, 13, 5, 5);
                break;
            }
            case Ico.Cut:
            {
                using var pen = P("#6B7280", 2.4f);
                g.DrawLine(pen, 9, 4, 21, 19); g.DrawLine(pen, 23, 4, 11, 19);
                using var ring = P("#E5484D", 2.6f);
                g.DrawEllipse(ring, 5, 19, 8, 8); g.DrawEllipse(ring, 19, 19, 8, 8);
                break;
            }
            case Ico.Copy:
                Page(g, new RectangleF(4, 3, 15, 20), "#FFFFFF", "#E6EEF7", "#5B7FA6");
                Page(g, new RectangleF(13, 10, 15, 20), "#FFFFFF", "#CFE3F8", "#2F80ED");
                break;
            case Ico.Paste:
            {
                using var board = new SolidBrush(C("#B9803A"));
                using (var p = Round(new RectangleF(4, 5, 22, 24), 3f)) g.FillPath(board, p);
                using var clip = new SolidBrush(C("#7C7F86"));
                using (var p = Round(new RectangleF(10, 2, 10, 6), 1.5f)) g.FillPath(clip, p);
                Page(g, new RectangleF(10, 11, 17, 19), "#FFFFFF", "#E6EEF7", "#5B7FA6");
                break;
            }
            case Ico.InsertRow: MiniGrid(g, "#BFEBCB", null, 1); Badge(g, 24, 24, 6.5f, "#2EB872", true); break;
            case Ico.DeleteRow: MiniGrid(g, "#F9C7C4", null, 1); Badge(g, 24, 24, 6.5f, "#E5484D", false); break;
            case Ico.InsertCol: MiniGrid(g, null, "#BFEBCB", 1); Badge(g, 24, 24, 6.5f, "#2EB872", true); break;
            case Ico.DeleteCol: MiniGrid(g, null, "#F9C7C4", 1); Badge(g, 24, 24, 6.5f, "#E5484D", false); break;
            case Ico.SortAsc:
            case Ico.SortDesc:
            {
                bool asc = kind == Ico.SortAsc;
                using var f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var a = new SolidBrush(C("#2F80ED")); using var z = new SolidBrush(C("#E5484D"));
                g.DrawString(asc ? "A" : "Z", f, a, 2, 3);
                g.DrawString(asc ? "Z" : "A", f, z, 2, 17);
                using var pen = P("#7C4DFF", 3f);
                g.DrawLine(pen, 22, 6, 22, 24);
                using var br = new SolidBrush(C("#7C4DFF"));
                if (asc) g.FillPolygon(br, new[] { new PointF(16, 21), new PointF(28, 21), new PointF(22, 29) });
                else g.FillPolygon(br, new[] { new PointF(16, 11), new PointF(28, 11), new PointF(22, 3) });
                break;
            }
            case Ico.Window:
            {
                var r = new RectangleF(3, 5, 26, 22);
                using (var p = Round(r, 3f)) { using var br = Grad(r, "#FFFFFF", "#E6EEF7"); g.FillPath(br, p); using var pen = P("#5B7FA6", 1.3f); g.DrawPath(pen, p); }
                using var bar = new SolidBrush(C("#2F80ED"));
                g.FillRectangle(bar, 4, 6, 24, 5);
                break;
            }
            case Ico.Replace:
                Magnifier(g, "#7C4DFF", "#EDE7FF", "#D9822B");
                break;
            case Ico.Close:
            {
                using var pen = P("#E5484D", 3.2f);
                g.DrawLine(pen, 8, 8, 24, 24); g.DrawLine(pen, 24, 8, 8, 24);
                break;
            }
        }
    }
}
