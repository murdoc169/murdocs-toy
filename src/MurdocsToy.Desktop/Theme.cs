using System.Runtime.InteropServices;

namespace MurdocsToy.Desktop;

/// <summary>User interface choices offered at login.</summary>
public static class Ui
{
    public const string ModernDark = "ModernDark", ModernLight = "ModernLight", Classic = "Classic";
    public static readonly string[] All = { ModernDark, ModernLight, Classic };
    public static string Label(string ui) => ui switch { ModernLight => "Modern (light)", Classic => "Classic", _ => "Modern (dark)" };
    public static string FromLabel(string label) => All.FirstOrDefault(u => Label(u) == label) ?? ModernDark;
}

/// <summary>Colours and the helper that restyles a form (and everything on it) for dark or light mode.</summary>
static class Theme
{
    public static bool Dark { get; set; } = true;

    public static Color Bg => Dark ? Color.FromArgb(18, 21, 26) : SystemColors.Control;
    public static Color Panel => Dark ? Color.FromArgb(27, 32, 40) : Color.FromArgb(240, 240, 240);
    public static Color Window => Dark ? Color.FromArgb(14, 17, 22) : SystemColors.Window;
    public static Color Text => Dark ? Color.FromArgb(230, 233, 238) : SystemColors.ControlText;
    public static Color Mute => Dark ? Color.FromArgb(139, 149, 165) : Color.DimGray;
    public static Color Line => Dark ? Color.FromArgb(48, 56, 68) : Color.FromArgb(215, 215, 215);
    public static Color Accent => Color.FromArgb(245, 197, 66);
    public static Color Select => Dark ? Color.FromArgb(38, 79, 120) : SystemColors.Highlight;

    /// <summary>Back/lay colours: as chosen in light mode, deepened in dark mode so white text stays readable.</summary>
    public static Color Cell(Color c, bool strong) => Dark ? Mix(c, Color.Black, strong ? 0.3f : 0.6f) : (strong ? c : ControlPaint.Light(c, 0.6f));

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar (Windows 10 1809+ / 11).</summary>
    public static void TitleBar(Form f, Color? caption = null, Color? captionText = null)
    {
        void Set()
        {
            int dark = Dark ? 1 : 0;
            DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
            if (caption is Color c) { int v = c.R | (c.G << 8) | (c.B << 16); DwmSetWindowAttribute(f.Handle, 35, ref v, 4); }     // caption colour (Windows 11)
            if (captionText is Color t) { int v = t.R | (t.G << 8) | (t.B << 16); DwmSetWindowAttribute(f.Handle, 36, ref v, 4); }
        }
        if (f.IsHandleCreated) Set(); else f.HandleCreated += (_, _) => Set();
    }

    /// <summary>Restyle a form and every control on it for the current mode. Safe to call again after rebuilding controls.</summary>
    public static void Apply(Control root)
    {
        if (root is Form f) { f.BackColor = Bg; f.ForeColor = Text; TitleBar(f); }
        foreach (Control c in root.Controls) Style(c);
    }

    static void Style(Control c)
    {
        switch (c)
        {
            case LadderControl or LiquidityPanel: c.Invalidate(); break;            // custom painted: they read Theme themselves
            case DataGridView g: StyleGrid(g); break;
            case TreeView t: t.BackColor = Window; t.ForeColor = Text; t.LineColor = Line; t.BorderStyle = BorderStyle.None; break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Line; b.FlatAppearance.BorderSize = 1;
                b.UseVisualStyleBackColor = false;
                if (b.BackColor != Color.Gold) { b.BackColor = Panel; b.ForeColor = Text; }
                break;
            case ComboBox cb: cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Window; cb.ForeColor = Text; break;
            case NumericUpDown n: n.BackColor = Window; n.ForeColor = Text; break;
            case TextBox tb: tb.BackColor = Window; tb.ForeColor = Text; tb.BorderStyle = BorderStyle.FixedSingle; break;
            case CheckBox cb: cb.ForeColor = Text; break;
            case Label l: if (l.ForeColor == SystemColors.ControlText || l.ForeColor == Color.FromArgb(230, 233, 238)) l.ForeColor = Text; break;
            case GroupBox gb: gb.ForeColor = Text; break;
            case SplitContainer sc: sc.BackColor = Line; sc.Panel1.BackColor = Bg; sc.Panel2.BackColor = Bg; break;
            case System.Windows.Forms.Panel or FlowLayoutPanel or TableLayoutPanel: c.BackColor = Bg; c.ForeColor = Text; break;
        }
        foreach (Control child in c.Controls) Style(child);
        if (c is SplitContainer s) { foreach (Control x in s.Panel1.Controls) Style(x); foreach (Control x in s.Panel2.Controls) Style(x); }
    }

    static void StyleGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BackgroundColor = Window; g.GridColor = Line; g.BorderStyle = BorderStyle.None;
        g.DefaultCellStyle.BackColor = Window; g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = Select; g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.BackColor = Panel; g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel;
    }

    /// <summary>Dark right-click menus.</summary>
    public static void Menu(ContextMenuStrip m)
    {
        m.Renderer = new ToolStripProfessionalRenderer(new MenuColours());
        m.BackColor = Panel; m.ForeColor = Text;
    }

    sealed class MenuColours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color MenuItemSelected => Select;
        public override Color MenuItemBorder => Line;
        public override Color MenuBorder => Line;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color SeparatorDark => Line;
    }
}
