using System.Runtime.InteropServices;

namespace MurdocsToy.Desktop;

/// <summary>
/// A window without the Windows title bar: a slim bar in the app's own colours (title, minimise, maximise, close) is drawn at the top instead.
/// The normal window frame styles stay, so edges resize, windows snap and the shadow stays.
/// </summary>
class ChromeForm : Form
{
    public const int BarH = 26, Strip = 5;
    readonly ChromeBar _bar;
    bool _grown;

    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NcCalcSize { public Rect R0, R1, R2; public IntPtr Pos; }

    public ChromeForm() { _bar = new ChromeBar(this) { Dock = DockStyle.Top, Height = BarH }; Controls.Add(_bar); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var mg = new Margins { T = 1 }; DwmExtendFrameIntoClientArea(Handle, ref mg);      // keeps the drop shadow
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27);                                // recalculate the frame now
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _bar.SendToBack();                                                                  // docked first, so it is the topmost strip
        if (!_grown) { _grown = true; var min = MinimumSize; Height += BarH; if (min != Size.Empty) MinimumSize = new Size(min.Width, min.Height + BarH); }   // the sizes set by the window are for its content
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); _bar?.Invalidate(); }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            var p = Marshal.PtrToStructure<NcCalcSize>(m.LParam);
            int top = p.R0.Top;
            base.WndProc(ref m);                                  // sides and bottom as normal
            p = Marshal.PtrToStructure<NcCalcSize>(m.LParam);
            p.R0.Top = top;                                       // but no Windows title bar
            if (WindowState == FormWindowState.Maximized)         // a maximized window overhangs the screen by the frame width
            {
                uint dpi = GetDpiForWindow(Handle);
                p.R0.Top += GetSystemMetricsForDpi(33, dpi) + GetSystemMetricsForDpi(92, dpi);
            }
            Marshal.StructureToPtr(p, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)1)                            // HTCLIENT: decide here what the bar area is
            {
                long l = m.LParam.ToInt64();
                var pt = PointToClient(new Point((short)(l & 0xffff), (short)((l >> 16) & 0xffff)));
                bool normal = WindowState == FormWindowState.Normal, sizable = FormBorderStyle == FormBorderStyle.Sizable;
                if (pt.Y >= 0 && pt.Y < BarH && !_bar.OverButton(pt))
                {
                    if (normal && sizable && pt.Y < Strip) m.Result = (IntPtr)(pt.X < 8 ? 13 : pt.X > ClientSize.Width - 8 ? 14 : 12);   // HTTOPLEFT / HTTOPRIGHT / HTTOP
                    else m.Result = (IntPtr)2;                                                                                           // HTCAPTION: drag, double-click to maximise
                }
            }
            return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>The drawn bar: icon, title and the three window buttons.</summary>
sealed class ChromeBar : Control
{
    readonly Form _form;
    int _hot = -1;                                   // 0 minimise, 1 maximise, 2 close
    const int ButtonW = 42;

    public ChromeBar(Form form)
    {
        _form = form;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = new Font("Tahoma", 8.25f, FontStyle.Bold);
        form.Resize += (_, _) => Invalidate();
    }

    Rectangle ButtonRect(int i) => new(Width - ButtonW * (3 - i), 0, ButtonW, Height);
    bool Has(int i) => i == 2 || (i == 0 ? _form.MinimizeBox : _form.MaximizeBox);

    /// <summary>True when the point (in the form's client coordinates) is over a button, so clicks go to the button and not to the drag area.</summary>
    public bool OverButton(Point formClient)
    {
        var p = new Point(formClient.X - Left, formClient.Y - Top);
        for (int i = 0; i < 3; i++) if (Has(i) && ButtonRect(i).Contains(p)) return true;
        return false;
    }

    int Hit(Point p) { for (int i = 0; i < 3; i++) if (Has(i) && ButtonRect(i).Contains(p)) return i; return -1; }

    /// <summary>Over the buttons the bar takes the mouse; everywhere else it lets the window decide (drag, resize edge).</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84)
        {
            long l = m.LParam.ToInt64();
            if (Hit(PointToClient(new Point((short)(l & 0xffff), (short)((l >> 16) & 0xffff)))) < 0) { m.Result = (IntPtr)(-1); return; }   // HTTRANSPARENT
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e) { var h = Hit(e.Location); if (h != _hot) { _hot = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        switch (Hit(e.Location))
        {
            case 0: _form.WindowState = FormWindowState.Minimized; break;
            case 1: _form.WindowState = _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
            case 2: _form.Close(); break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Cl.Gradient(g, ClientRectangle, Cl.BarTop, Cl.BarBottom);
        int x = 8;
        if (_form.Icon != null) { g.DrawIcon(_form.Icon, new Rectangle(8, (Height - 16) / 2, 16, 16)); x = 30; }
        TextRenderer.DrawText(g, _form.Text, Font, new Rectangle(x, 0, Math.Max(0, Width - x - ButtonW * 3), Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        for (int i = 0; i < 3; i++)
        {
            if (!Has(i)) continue;
            var r = ButtonRect(i);
            if (_hot == i) { using var b = new SolidBrush(i == 2 ? Color.FromArgb(196, 43, 28) : Color.FromArgb(70, 255, 255, 255)); g.FillRectangle(b, r); }
            using var pen = new Pen(Color.White, 1f);
            int cx = r.X + r.Width / 2, cy = r.Height / 2;
            switch (i)
            {
                case 0: g.DrawLine(pen, cx - 5, cy + 1, cx + 5, cy + 1); break;
                case 1:
                    if (_form.WindowState == FormWindowState.Maximized) { g.DrawRectangle(pen, cx - 5, cy - 2, 7, 7); g.DrawLines(pen, new[] { new Point(cx - 2, cy - 2), new Point(cx - 2, cy - 5), new Point(cx + 5, cy - 5), new Point(cx + 5, cy + 2), new Point(cx + 2, cy + 2) }); }
                    else g.DrawRectangle(pen, cx - 5, cy - 5, 9, 9);
                    break;
                default: g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5); break;
            }
        }
    }
}
