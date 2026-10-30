using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

class Program
{
    class TrailPoint
    {
        public Point Position { get; set; }
        public float Alpha { get; set; } = 255f;
    }

    static List<TrailPoint> points = new List<TrailPoint>();
    static Form? overlayForm;
    static System.Windows.Forms.Timer? animationTimer;
    static Point lastMousePos = Point.Empty;
    static NotifyIcon? trayIcon;
    static ToolStripMenuItem? autostartMenuItem;

    static int currentR = 255;
    static int currentG = 255;
    static int currentB = 255;

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { int X; int Y; public Point ToPoint() => new Point(X, Y); }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    const string RegistryKeyPath = @"SOFTWARE\CursorLite";
    const string RegistryRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "CursorLite";

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }

        LoadSettings();
        InitTrayIcon();
        CreateOverlay();
    }

    static void LoadSettings()
    {
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key != null)
                {
                    currentR = Convert.ToInt32(key.GetValue("ColorR", 255));
                    currentG = Convert.ToInt32(key.GetValue("ColorG", 255));
                    currentB = Convert.ToInt32(key.GetValue("ColorB", 255));
                }
            }
        }
        catch { }
    }

    static void SaveSettings()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
            {
                key.SetValue("ColorR", currentR);
                key.SetValue("ColorG", currentG);
                key.SetValue("ColorB", currentB);
            }
        }
        catch { }
    }

    static bool IsAutostartEnabled()
    {
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunPath))
            {
                return key?.GetValue(AppName) != null;
            }
        }
        catch { return false; }
    }

    static void InitTrayIcon()
    {
        ContextMenuStrip trayMenu = new ContextMenuStrip();

        trayMenu.Items.Add("Изменить цвет", null, OnChangeColor);
        trayMenu.Items.Add("Сбросить цвет на белый", null, OnResetColor);
        trayMenu.Items.Add(new ToolStripSeparator());

        autostartMenuItem = new ToolStripMenuItem("Автозагрузка", null, OnToggleAutostart);
        autostartMenuItem.Checked = IsAutostartEnabled();
        trayMenu.Items.Add(autostartMenuItem);

        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Выход", null, OnExit);

        trayIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            ContextMenuStrip = trayMenu,
            Text = "CursorLite v1.0",
            Visible = true
        };

        trayIcon.MouseClick += (sender, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                autostartMenuItem.Checked = IsAutostartEnabled();
                trayMenu.Show(Cursor.Position);
            }
        };
    }

    static void OnChangeColor(object? sender, EventArgs e)
    {
        using (ColorDialog colorDialog = new ColorDialog())
        {
            colorDialog.AllowFullOpen = true;
            colorDialog.FullOpen = true;
            colorDialog.Color = Color.FromArgb(currentR, currentG, currentB);

            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                currentR = colorDialog.Color.R;
                currentG = colorDialog.Color.G;
                currentB = colorDialog.Color.B;
                SaveSettings();
            }
        }
    }

    static void OnResetColor(object? sender, EventArgs e)
    {
        currentR = 255;
        currentG = 255;
        currentB = 255;
        SaveSettings();
    }

    static void OnToggleAutostart(object? sender, EventArgs e)
    {
        try
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe");
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true))
            {
                if (key != null)
                {
                    if (IsAutostartEnabled())
                    {
                        key.DeleteValue(AppName, false);
                        if (autostartMenuItem != null) autostartMenuItem.Checked = false;
                    }
                    else
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        if (autostartMenuItem != null) autostartMenuItem.Checked = true;
                    }
                }
            }
        }
        catch { }
    }

    static void OnExit(object? sender, EventArgs e)
    {
        if (trayIcon != null) trayIcon.Visible = false;
        Environment.Exit(0);
    }

    static void CreateOverlay()
    {
        overlayForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            TopMost = true,
            WindowState = FormWindowState.Maximized,
            BackColor = Color.Black,
            TransparencyKey = Color.Black
        };

        overlayForm.GetType()
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(overlayForm, true);

        int initialStyle = GetWindowLong(overlayForm.Handle, -20);
        SetWindowLong(overlayForm.Handle, -20, initialStyle | 0x80000 | 0x20);

        overlayForm.Paint += OverlayForm_Paint;

        animationTimer = new System.Windows.Forms.Timer();
        animationTimer.Interval = 10;
        animationTimer.Tick += AnimationTimer_Tick;
        animationTimer.Start();

        Application.Run(overlayForm);
    }

    private static void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (GetCursorPos(out POINT mp))
        {
            Point currentPos = mp.ToPoint();
            double distance = Math.Sqrt(Math.Pow(currentPos.X - lastMousePos.X, 2) + Math.Pow(currentPos.Y - lastMousePos.Y, 2));

            float decayRate = distance > 0.5 ? 14f : 65f;

            if (distance > 0)
            {
                lock (points) { points.Add(new TrailPoint { Position = currentPos }); }
                lastMousePos = currentPos;
            }

            lock (points)
            {
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    points[i].Alpha -= decayRate;
                    if (points[i].Alpha <= 0) points.RemoveAt(i);
                }
            }
        }
        overlayForm?.Invalidate();
    }

    private static void OverlayForm_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int r = currentR;
        int gComp = currentG;
        int b = currentB;

        if (r == 0 && gComp == 0 && b == 0)
        {
            r = 1; gComp = 1; b = 1;
        }

        lock (points)
        {
            if (points.Count < 2) return;

            for (int i = 1; i < points.Count; i++)
            {
                var p1 = points[i - 1];
                var p2 = points[i];

                int alpha = (int)p2.Alpha;
                if (alpha < 0) alpha = 0;
                if (alpha > 255) alpha = 255;

                using (Pen trailPen = new Pen(Color.FromArgb(alpha, r, gComp, b)))
                {
                    trailPen.Width = 6f * (p2.Alpha / 255f);
                    if (trailPen.Width < 1f) trailPen.Width = 1f;

                    trailPen.StartCap = LineCap.Round;
                    trailPen.EndCap = LineCap.Round;

                    g.DrawLine(trailPen, p1.Position, p2.Position);
                }
            }
        }
    }
}
