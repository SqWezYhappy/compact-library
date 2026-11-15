using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Native
{
    [DllImport("user32.dll")] static extern int PrivateExtractIcons(string f, int i, int w, int h, IntPtr[] ic, int[] id, int n, int fl);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint m, uint vk);
    delegate IntPtr HP(int c, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HP f, IntPtr m, uint t);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
    [DllImport("user32.dll")] static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);

    public static IntPtr Icon(string f, int i, int s)
    {
        IntPtr[] a = new IntPtr[1]; int[] b = new int[1];
        try { int r = PrivateExtractIcons(f, i, s, s, a, b, 1, 0); return r > 0 ? a[0] : IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }
    public static void Kill(IntPtr h) { DestroyIcon(h); }
    public static void Dark(IntPtr h) { int v = 1; DwmSetWindowAttribute(h, 20, ref v, 4); }
    public static bool Hot(IntPtr h) { return RegisterHotKey(h, 0x5142, 3, 0x20); }

    static HP proc; static IntPtr hook; static bool win, other;
    public static bool On = true; public static Action OnWin; public static Dispatcher D;
    public static void Hook() { proc = new HP(Cb); hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); }
    static IntPtr Cb(int c, IntPtr w, IntPtr l)
    {
        if (c >= 0 && On)
        {
            int vk = Marshal.ReadInt32(l); int fl = Marshal.ReadInt32(l, 8);
            bool inj = (fl & 0x10) != 0; int m = (int)w; bool down = (m == 0x100 || m == 0x104);
            if (!inj)
            {
                if (vk == 0x5B || vk == 0x5C)
                {
                    if (down) { win = true; other = false; }
                    else if (win)
                    {
                        win = false;
                        if (!other)
                        {
                            keybd_event(0xE8, 0, 0, UIntPtr.Zero); keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                            if (OnWin != null && D != null) D.BeginInvoke((Delegate)OnWin, new object[0]);
                        }
                    }
                }
                else if (win && down) { other = true; }
            }
        }
        return CallNextHookEx(hook, c, w, l);
    }
}

class AppEntry { public string Name; public string Path; public ImageSource Icon; public int Glyph; }

class SPage { public string Name, Sub, Color; public int Glyph; public StackPanel Panel = new StackPanel(); }

class Lib
{
    static readonly Dictionary<string, string> Def = new Dictionary<string, string> {
        {"Width","400"},{"Height","550"},{"Left","34"},{"Bottom","96"},{"Radius","30"},{"IconPin","54"},{"IconAll","44"},
        {"Alpha","88"},{"Glow","28"},{"C1","#442A9A"},{"C2","#1E0E55"},{"Edge","#D6B3FF"},{"GlowC","#8A3CFF"},{"Text","#FFFFFF"},
        {"WinKey","1"},{"AutoUpdate","1"},{"Pins",""}};

    Dictionary<string, string> cfg = new Dictionary<string, string>();
    string cfgDir, cfgPath; bool pinsSaved;
    List<AppEntry> apps = new List<AppEntry>();
    Dictionary<string, AppEntry> byName = new Dictionary<string, AppEntry>();
    Application app; EventWaitHandle ev; string[] args;
    Window lw, sw; Border root; TextBox search; TextBlock ph; StackPanel stack;
    System.Windows.Forms.NotifyIcon ni;
    bool allMode, menuOpen, quitting; DateTime hiddenAt = DateTime.Now.AddSeconds(-5);
    Brush fg = Brushes.White; double tw = 120;
    List<Action> refreshers = new List<Action>();
    List<SPage> pages = new List<SPage>(); List<Border> sideItems = new List<Border>();
    TextBlock htitle, hsub; TextBox sbox; int curPage;
    Brush cardBg = Br("#FF3A383F");
    const string CurVer = "0.0.0-dev";   // CI подставляет версию из тега релиза
    const string Repo = "OWNER/REPO";    // CI подставляет имя репозитория GitHub
    TextBlock updStatus; Button updBtn; string newUrl, newTag;

    public Lib(Application a, EventWaitHandle e, string[] ar)
    {
        app = a; ev = e; args = ar;
        cfgDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PurpleLibrary");
        cfgPath = System.IO.Path.Combine(cfgDir, "settings.txt");
    }

    // ---------- helpers ----------
    static Color Col(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    static Color ColA(string hex, int a) { Color c = Col(hex); c.A = (byte)Math.Round(255.0 * a / 100); return c; }
    static SolidColorBrush Br(string hex) { SolidColorBrush b = new SolidColorBrush(Col(hex)); b.Freeze(); return b; }
    static TextBlock Sym(int code, double size)
    {
        return new TextBlock { Text = ((char)code).ToString(), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }
    int I(string k) { int v; return int.TryParse(cfg[k], out v) ? v : int.Parse(Def[k]); }
    string S(string k) { return cfg[k]; }
    bool B(string k) { return cfg[k] == "1"; }
    void Set(string k, string v) { cfg[k] = v; Save(); }

    void LoadCfg()
    {
        foreach (KeyValuePair<string, string> p in Def) cfg[p.Key] = p.Value;
        try
        {
            if (File.Exists(cfgPath))
                foreach (string line in File.ReadAllLines(cfgPath))
                {
                    int i = line.IndexOf('='); if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    if (Def.ContainsKey(k)) { cfg[k] = v; if (k == "Pins") pinsSaved = true; }
                }
        }
        catch { }
    }
    void Save()
    {
        try
        {
            Directory.CreateDirectory(cfgDir);
            List<string> l = new List<string>();
            foreach (KeyValuePair<string, string> p in cfg) l.Add(p.Key + "=" + p.Value);
            File.WriteAllLines(cfgPath, l.ToArray());
        }
        catch { }
    }
    List<string> Pins() { return new List<string>(cfg["Pins"].Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries)); }
    void TogglePin(string n) { List<string> l = Pins(); if (l.Contains(n)) l.Remove(n); else l.Add(n); Set("Pins", string.Join("|", l.ToArray())); pinsSaved = true; }
    void DefaultPins()
    {
        List<string> l = new List<string>();
        foreach (string rx in new string[] { "discord", "spotify", "youtube", "chrome", "telegram", "steam" })
            foreach (AppEntry a in apps) if (Regex.IsMatch(a.Name, rx, RegexOptions.IgnoreCase)) { l.Add(a.Name); break; }
        Set("Pins", string.Join("|", l.ToArray()));
    }

    // ---------- приложения ----------
    static ImageSource BmpFrom(IntPtr h, bool kill)
    {
        BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        b.Freeze(); if (kill) Native.Kill(h); return b;
    }
    static ImageSource GetIcon(string lnk, string target, string iconLoc)
    {
        List<KeyValuePair<string, int>> c = new List<KeyValuePair<string, int>>();
        if (!string.IsNullOrEmpty(iconLoc))
        {
            string p = Environment.ExpandEnvironmentVariables(iconLoc); int i = 0; int k = p.LastIndexOf(',');
            if (k > 0) { int.TryParse(p.Substring(k + 1), out i); p = p.Substring(0, k); }
            if (File.Exists(p)) c.Add(new KeyValuePair<string, int>(p, i));
        }
        if (!string.IsNullOrEmpty(target) && File.Exists(target)) c.Add(new KeyValuePair<string, int>(target, 0));
        foreach (KeyValuePair<string, int> x in c)
        {
            IntPtr h = Native.Icon(x.Key, x.Value, 256);
            if (h != IntPtr.Zero) return BmpFrom(h, true);
        }
        try
        {
            string src = (!string.IsNullOrEmpty(target) && File.Exists(target)) ? target : lnk;
            using (System.Drawing.Icon ic = System.Drawing.Icon.ExtractAssociatedIcon(src)) { return BmpFrom(ic.Handle, false); }
        }
        catch { return null; }
    }
    void Walk(string dir, List<string> res)
    {
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*.lnk")) res.Add(f);
            foreach (string d in Directory.GetDirectories(dir)) Walk(d, res);
        }
        catch { }
    }
    void Scan()
    {
        apps.Clear(); byName.Clear();
        dynamic sh = null;
        try { sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); } catch { }
        List<string> files = new List<string>();
        Walk(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), files);
        Walk(Environment.GetFolderPath(Environment.SpecialFolder.Programs), files);
        foreach (string f in files)
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(f);
            if (Regex.IsMatch(n, "Uninstall|Удал", RegexOptions.IgnoreCase) || byName.ContainsKey(n)) continue;
            string t = "", il = "";
            try { if (sh != null) { dynamic l = sh.CreateShortcut(f); t = (string)l.TargetPath; il = (string)l.IconLocation; } } catch { }
            AppEntry a = new AppEntry { Name = n, Path = f, Icon = GetIcon(f, t, il) };
            apps.Add(a); byName[n] = a;
        }
        string ex = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        IntPtr eh = Native.Icon(ex, 0, 256);
        AppEntry e1 = new AppEntry { Name = "Проводник", Path = "explorer.exe", Icon = eh != IntPtr.Zero ? BmpFrom(eh, true) : null, Glyph = 0xE838 };
        AppEntry e2 = new AppEntry { Name = "Параметры Windows", Path = "ms-settings:", Glyph = 0xE713 };
        apps.Add(e1); byName[e1.Name] = e1; apps.Add(e2); byName[e2.Name] = e2;
        apps.Sort(delegate(AppEntry x, AppEntry y) { return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase); });
    }
    static void Launch(AppEntry a)
    {
        try { Process.Start(new ProcessStartInfo(a.Path) { UseShellExecute = true }); } catch { }
    }

    // ---------- библиотека ----------
    void BuildLibrary()
    {
        lw = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, Width = 440, Height = 590 };
        root = new Border { Margin = new Thickness(20), BorderThickness = new Thickness(1.5), RenderTransformOrigin = new Point(0.5, 1) };
        lw.Content = root;
        DockPanel dp = new DockPanel(); root.Child = dp;

        Grid bot = new Grid { Height = 54, Margin = new Thickness(8, 0, 8, 0) };
        bot.ColumnDefinitions.Add(new ColumnDefinition()); bot.ColumnDefinitions.Add(new ColumnDefinition());
        bot.Children.Add(new Border { Height = 1, Background = Br("#25FFFFFF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 8, 0) });
        Button pwr = new Button { Content = Sym(0xE7E8, 20), Width = 42, Height = 42, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, ToolTip = "Питание" };
        Button gear = new Button { Content = Sym(0xE713, 20), Width = 42, Height = 42, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand, ToolTip = "Настройки библиотеки" };
        Grid.SetColumn(gear, 1); bot.Children.Add(pwr); bot.Children.Add(gear);
        DockPanel.SetDock(bot, Dock.Bottom); dp.Children.Add(bot);
        gear.Click += delegate { OpenSettings(); };

        ContextMenu cm = new ContextMenu();
        string[][] items = new string[][] { new string[] { "Завершение работы", "/s /t 0" }, new string[] { "Перезагрузка", "/r /t 0" }, new string[] { "Сон", "SLEEP" }, new string[] { "Закрыть библиотеку", "EXIT" } };
        foreach (string[] it in items)
        {
            string arg = it[1];
            MenuItem mi = new MenuItem { Header = it[0] };
            mi.Click += delegate
            {
                if (arg == "EXIT") Quit();
                else if (arg == "SLEEP") { lw.Hide(); Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"); }
                else { lw.Hide(); Process.Start("shutdown", arg); }
            };
            cm.Items.Add(mi);
        }
        cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; };
        pwr.Click += delegate { cm.PlacementTarget = pwr; cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Top; cm.IsOpen = true; };

        Grid top = new Grid { Margin = new Thickness(18, 18, 18, 0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Border pill = new Border { Height = 46, CornerRadius = new CornerRadius(23), Background = Br("#28FFFFFF"), BorderBrush = Br("#38FFFFFF"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
        Grid pg = new Grid(); pill.Child = pg;
        TextBlock ic = Sym(0xE721, 17); ic.HorizontalAlignment = HorizontalAlignment.Left; ic.Margin = new Thickness(16, 0, 0, 0);
        search = new TextBox { Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 15, CaretBrush = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(44, 0, 14, 0) };
        ph = new TextBlock { Text = "Поиск приложений...", Foreground = Br("#99FFFFFF"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(46, 0, 0, 0), IsHitTestVisible = false };
        pg.Children.Add(ic); pg.Children.Add(ph); pg.Children.Add(search);
        Button gb = new Button { Width = 46, Height = 46, Cursor = Cursors.Hand, BorderThickness = new Thickness(1.2), BorderBrush = Br("#70C9A5FF"), Background = new LinearGradientBrush(ColA("#7A3CFF", 56), ColA("#4A1FB8", 38), 45), Content = Sym(0xE80A, 20), ToolTip = "Все приложения / закреплённые" };
        Grid.SetColumn(gb, 1); top.Children.Add(pill); top.Children.Add(gb);
        DockPanel.SetDock(top, Dock.Top); dp.Children.Add(top);

        stack = new StackPanel();
        dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = stack, Margin = new Thickness(0, 6, 0, 0) });

        search.TextChanged += delegate { ph.Visibility = search.Text.Length > 0 ? Visibility.Hidden : Visibility.Visible; Fill(); };
        gb.Click += delegate { allMode = !allMode; Fill(); };
        lw.Deactivated += delegate { if (!menuOpen && !(sw != null && sw.IsVisible)) { lw.Hide(); hiddenAt = DateTime.Now; } };
        lw.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) lw.Hide();
            else if (e.Key == Key.Return)
            {
                foreach (UIElement c in stack.Children)
                {
                    WrapPanel w = c as WrapPanel;
                    if (w != null && w.Children.Count > 0)
                    {
                        FrameworkElement t = (FrameworkElement)(search.Text.Length > 0 ? w.Children[0] : w.Children[w.Children.Count - 1]);
                        Launch((AppEntry)t.Tag); lw.Hide(); break;
                    }
                }
            }
        };
    }
    TextBlock Head(string t) { return new TextBlock { Text = t + "  ›", Foreground = fg, FontSize = 16, FontWeight = FontWeights.Medium, Margin = new Thickness(24, 14, 0, 6) }; }
    FrameworkElement Tile(AppEntry a, bool big)
    {
        int sz = big ? I("IconPin") : I("IconAll");
        StackPanel p = new StackPanel { Width = tw, Margin = new Thickness(0, 0, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = a };
        if (a.Icon != null)
        {
            Image im = new Image { Source = a.Icon, Width = sz, Height = sz, Margin = new Thickness(0, 8, 0, 5) };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); p.Children.Add(im);
        }
        else
        {
            Border b = new Border { Width = sz, Height = sz, CornerRadius = new CornerRadius(12), Background = Br("#FF4A3A8C"), Margin = new Thickness(0, 8, 0, 5) };
            if (a.Glyph > 0) b.Child = Sym(a.Glyph, sz / 2.0);
            else b.Child = new TextBlock { Text = a.Name.Substring(0, 1), Foreground = Brushes.White, FontSize = sz / 2.0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            p.Children.Add(b);
        }
        p.Children.Add(new TextBlock { Text = a.Name, Foreground = fg, FontSize = 12.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 6, 4) });
        p.MouseEnter += delegate { p.Background = Br("#30FFFFFF"); };
        p.MouseLeave += delegate { p.Background = Brushes.Transparent; };
        p.MouseLeftButtonUp += delegate { Launch(a); lw.Hide(); };
        ContextMenu cm = new ContextMenu();
        bool pinned = Pins().Contains(a.Name);
        MenuItem mo = new MenuItem { Header = "Открыть" }; mo.Click += delegate { Launch(a); lw.Hide(); };
        MenuItem mp = new MenuItem { Header = pinned ? "Открепить" : "Закрепить" }; mp.Click += delegate { TogglePin(a.Name); Fill(); };
        cm.Items.Add(mo); cm.Items.Add(mp);
        cm.Opened += delegate { menuOpen = true; }; cm.Closed += delegate { menuOpen = false; };
        p.ContextMenu = cm;
        return p;
    }
    void Fill()
    {
        string f = search.Text.Trim(); stack.Children.Clear();
        if (f.Length == 0 && !allMode)
        {
            stack.Children.Add(Head("Закреплённые"));
            WrapPanel w1 = new WrapPanel { Margin = new Thickness(10, 0, 0, 4) };
            foreach (string n in Pins()) { AppEntry a; if (byName.TryGetValue(n, out a)) w1.Children.Add(Tile(a, true)); }
            stack.Children.Add(w1);
            stack.Children.Add(new Border { Height = 1, Background = Br("#25FFFFFF"), Margin = new Thickness(24, 6, 24, 0) });
        }
        stack.Children.Add(Head("Все приложения"));
        WrapPanel w2 = new WrapPanel { Margin = new Thickness(10, 0, 0, 10) };
        foreach (AppEntry a in apps)
        {
            if (f.Length > 0 && a.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
            w2.Children.Add(Tile(a, false));
        }
        stack.Children.Add(w2);
    }
    void Place()
    {
        lw.Left = I("Left") - 20;
        lw.Top = SystemParameters.PrimaryScreenHeight - I("Bottom") - I("Height") - 20;
    }
    void ApplyTheme(bool refill)
    {
        int a = I("Alpha");
        root.Background = new LinearGradientBrush(ColA(S("C1"), a), ColA(S("C2"), a), 45);
        root.BorderBrush = new LinearGradientBrush(ColA(S("Edge"), 70), ColA(S("GlowC"), 30), 45);
        root.CornerRadius = new CornerRadius(I("Radius"));
        root.Effect = new DropShadowEffect { Color = Col(S("GlowC")), BlurRadius = I("Glow"), ShadowDepth = 0, Opacity = 0.55 };
        fg = Br(S("Text")); tw = Math.Floor((I("Width") - 30) / 3.0);
        lw.Width = I("Width") + 40; lw.Height = I("Height") + 40; Place();
        Native.On = B("WinKey");
        if (refill) Fill();
    }
    void Toggle()
    {
        if (lw.IsVisible) { lw.Hide(); return; }
        if ((DateTime.Now - hiddenAt).TotalMilliseconds < 300) return;
        Place(); allMode = false; search.Text = ""; Fill();
        ScaleTransform sc = new ScaleTransform(0.94, 0.94); root.RenderTransform = sc;
        Duration dur = new Duration(TimeSpan.FromMilliseconds(220));
        DoubleAnimation an = new DoubleAnimation(0.94, 1.0, dur); an.EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, an); sc.BeginAnimation(ScaleTransform.ScaleYProperty, an);
        lw.Opacity = 0; lw.Show(); lw.Activate(); search.Focus();
        lw.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0.0, 1.0, dur));
    }
    void Quit() { quitting = true; ni.Visible = false; ni.Dispose(); app.Shutdown(); }

    // ---------- настройки ----------
    Border Card(string label, FrameworkElement ctrl)
    {
        Border b = new Border { CornerRadius = new CornerRadius(12), Background = cardBg, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Tag = label };
        Grid g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (ctrl != null) { ctrl.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(ctrl, 1); g.Children.Add(ctrl); }
        b.Child = g; return b;
    }
    Border SliderRow(string label, string key, int min, int max, string unit, bool refill)
    {
        TextBlock vl = new TextBlock { Text = S(key) + unit, Foreground = Brushes.White, Width = 58, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Slider sl = new Slider { Minimum = min, Maximum = max, Value = I(key), Width = 190, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        sl.ValueChanged += delegate { int v = (int)sl.Value; Set(key, v.ToString()); vl.Text = v + unit; ApplyTheme(refill); };
        refreshers.Add(delegate { sl.Value = I(key); });
        StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(sl); sp.Children.Add(vl);
        return Card(label, sp);
    }
    Border ColorRow(string label, string key, string[] pal)
    {
        StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
        TextBox tb = new TextBox { Width = 82, Text = S(key), Background = Br("#FF2A2830"), Foreground = Brushes.White, BorderBrush = Br("#40FFFFFF"), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(8, 0, 0, 0), CaretBrush = Brushes.White };
        foreach (string h in pal)
        {
            string hex = h;
            Border s = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Br(hex), Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand, ToolTip = hex, BorderBrush = Br("#60FFFFFF"), BorderThickness = new Thickness(1) };
            s.MouseLeftButtonUp += delegate { tb.Text = hex; };
            sp.Children.Add(s);
        }
        sp.Children.Add(tb);
        tb.TextChanged += delegate { if (Regex.IsMatch(tb.Text, "^#[0-9A-Fa-f]{6}$")) { Set(key, tb.Text.ToUpper()); ApplyTheme(true); } };
        refreshers.Add(delegate { tb.Text = S(key); });
        return Card(label, sp);
    }
    Border CheckRow(string label, string key)
    {
        CheckBox cb = new CheckBox { IsChecked = B(key) };
        cb.Click += delegate { Set(key, cb.IsChecked == true ? "1" : "0"); ApplyTheme(false); };
        refreshers.Add(delegate { cb.IsChecked = B(key); });
        return Card(label, cb);
    }
    static Button Btn(string t) { return new Button { Content = t, Background = Br("#FF4A4852"), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(3), Cursor = Cursors.Hand }; }
    void RefreshAll() { foreach (Action a in refreshers) a(); }

    void BuildSettings()
    {
        sw = new Window { Title = "Purple Library — настройки", Width = 820, Height = 580, Background = Br("#FF1F1E24"), ResizeMode = ResizeMode.CanMinimize, WindowStartupLocation = WindowStartupLocation.Manual };
        sw.SourceInitialized += delegate { Native.Dark(new WindowInteropHelper(sw).Handle); };
        sw.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!quitting) { e.Cancel = true; sw.Hide(); } };
        sw.IsVisibleChanged += delegate { if (!sw.IsVisible) lw.Hide(); };

        string[] pal = { "#442A9A", "#1F4FA8", "#A3277A", "#1F7A4A", "#A32727", "#2A2A2E", "#C77B1F" };
        string[] palE = { "#D6B3FF", "#A8C8FF", "#FFB3E6", "#B3FFD6", "#FFB3B3", "#FFFFFF", "#8A8A92" };
        string[] palG = { "#8A3CFF", "#3C8AFF", "#FF3CB4", "#3CFF8A", "#FF3C3C", "#FFB43C", "#666680" };
        string[] palT = { "#FFFFFF", "#E8E0FF", "#FFD6F2", "#CFFFE5", "#FFE1B3", "#C9C9D1", "#111111" };

        SPage p1 = new SPage { Name = "Внешний вид", Sub = "Цвета и прозрачность", Glyph = 0xE790, Color = "#FF7B5CFF" };
        WrapPanel pw = new WrapPanel { Width = 300 };
        string[][] presets = new string[][] {
            new string[]{"Фиолетовая","#442A9A","#1E0E55","#D6B3FF","#8A3CFF"}, new string[]{"Синяя","#1F4FA8","#0B1E55","#A8C8FF","#3C8AFF"},
            new string[]{"Розовая","#A3277A","#470F3A","#FFB3E6","#FF3CB4"}, new string[]{"Зелёная","#1F7A4A","#0A3322","#B3FFD6","#3CFF8A"},
            new string[]{"Красная","#A32727","#470F0F","#FFB3B3","#FF3C3C"}, new string[]{"Тёмная","#2A2A2E","#121214","#8A8A92","#666680"} };
        foreach (string[] pr in presets)
        {
            string[] x = pr; Button b = Btn(x[0]);
            b.Click += delegate { cfg["C1"] = x[1]; cfg["C2"] = x[2]; cfg["Edge"] = x[3]; cfg["GlowC"] = x[4]; cfg["Text"] = "#FFFFFF"; Save(); RefreshAll(); ApplyTheme(true); };
            pw.Children.Add(b);
        }
        p1.Panel.Children.Add(Card("Готовые темы", pw));
        p1.Panel.Children.Add(ColorRow("Цвет фона (верх)", "C1", pal));
        p1.Panel.Children.Add(ColorRow("Цвет фона (низ)", "C2", pal));
        p1.Panel.Children.Add(ColorRow("Цвет рамки", "Edge", palE));
        p1.Panel.Children.Add(ColorRow("Цвет свечения", "GlowC", palG));
        p1.Panel.Children.Add(ColorRow("Цвет текста", "Text", palT));
        p1.Panel.Children.Add(SliderRow("Непрозрачность", "Alpha", 30, 100, "%", false));
        p1.Panel.Children.Add(SliderRow("Сила свечения", "Glow", 0, 40, "", false));
        p1.Panel.Children.Add(SliderRow("Скругление углов", "Radius", 0, 50, "", false));

        SPage p2 = new SPage { Name = "Размеры", Sub = "Ширина, высота, положение и иконки", Glyph = 0xE740, Color = "#FF34C759" };
        p2.Panel.Children.Add(SliderRow("Ширина", "Width", 300, 700, " px", true));
        p2.Panel.Children.Add(SliderRow("Высота", "Height", 350, 900, " px", false));
        p2.Panel.Children.Add(SliderRow("Отступ слева", "Left", 0, 800, " px", false));
        p2.Panel.Children.Add(SliderRow("Отступ снизу", "Bottom", 0, 400, " px", false));
        p2.Panel.Children.Add(SliderRow("Иконки закреплённых", "IconPin", 32, 90, " px", true));
        p2.Panel.Children.Add(SliderRow("Иконки всех приложений", "IconAll", 28, 72, " px", true));

        SPage p3 = new SPage { Name = "Закреплённые", Sub = "Приложения в верхнем блоке", Glyph = 0xE718, Color = "#FFFF9F0A" };
        Border pc = new Border { CornerRadius = new CornerRadius(12), Background = cardBg, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Tag = "Закреплённые приложения" };
        StackPanel ps = new StackPanel();
        ps.Children.Add(new TextBlock { Text = "Отметьте приложения для верхнего блока", Foreground = Br("#B0FFFFFF"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (AppEntry a in apps)
        {
            string nm = a.Name;
            CheckBox cb = new CheckBox { Content = nm, Foreground = Brushes.White, IsChecked = Pins().Contains(nm), Margin = new Thickness(0, 3, 0, 3) };
            cb.Click += delegate { if ((cb.IsChecked == true) != Pins().Contains(nm)) TogglePin(nm); Fill(); };
            ps.Children.Add(cb);
        }
        pc.Child = ps; p3.Panel.Children.Add(pc);

        SPage p4 = new SPage { Name = "Поведение", Sub = "Клавиша Win, автозапуск, сброс", Glyph = 0xE713, Color = "#FF3A9BDC" };
        p4.Panel.Children.Add(CheckRow("Открывать библиотеку клавишей Win", "WinKey"));
        string lnkPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Purple Library.lnk");
        CheckBox au = new CheckBox { IsChecked = File.Exists(lnkPath) };
        au.Click += delegate
        {
            try
            {
                if (au.IsChecked == true)
                {
                    dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    dynamic l = sh.CreateShortcut(lnkPath);
                    l.TargetPath = Assembly.GetExecutingAssembly().Location; l.Arguments = "/bg"; l.Save();
                }
                else File.Delete(lnkPath);
            }
            catch { }
        };
        p4.Panel.Children.Add(Card("Запускать вместе с Windows", au));
        Button rb = Btn("Сбросить");
        rb.Click += delegate { foreach (KeyValuePair<string, string> d in Def) if (d.Key != "Pins") cfg[d.Key] = d.Value; Save(); RefreshAll(); ApplyTheme(true); };
        p4.Panel.Children.Add(Card("Сбросить цвета и размеры", rb));

        SPage p5 = new SPage { Name = "О программе", Sub = "", Glyph = 0xE946, Color = "#FFAF52DE" };
        p5.Panel.Children.Add(Card("Версия", new TextBlock { Text = CurVer, Foreground = Br("#B0FFFFFF") }));
        p5.Panel.Children.Add(CheckRow("Проверять обновления автоматически", "AutoUpdate"));
        updStatus = new TextBlock { Foreground = Br("#B0FFFFFF"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        updBtn = Btn("Проверить");
        updBtn.Click += delegate { if (newUrl != null) ApplyUpdate(); else CheckUpdates(true); };
        StackPanel us = new StackPanel { Orientation = Orientation.Horizontal };
        us.Children.Add(updStatus); us.Children.Add(updBtn);
        p5.Panel.Children.Add(Card("Обновления", us));
        p5.Panel.Children.Add(Card("Purple Library. Значок у часов открывает библиотеку и настройки. Повторный запуск ярлыка тоже открывает библиотеку.", null));

        pages.Add(p1); pages.Add(p2); pages.Add(p3); pages.Add(p4); pages.Add(p5);

        Grid grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        DockPanel sidebar = new DockPanel { Background = Br("#FF2A2930") };
        TextBlock foot = new TextBlock { Text = "Изменения сохраняются сразу", Foreground = Br("#90FFFFFF"), FontSize = 11, Margin = new Thickness(22, 0, 0, 16) };
        DockPanel.SetDock(foot, Dock.Bottom); sidebar.Children.Add(foot);
        StackPanel side = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        side.Children.Add(new TextBlock { Text = "Purple Library", Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 20, 0, 2) });
        side.Children.Add(new TextBlock { Text = "версия 1.0", Foreground = Br("#90FFFFFF"), FontSize = 11, Margin = new Thickness(8, 0, 0, 14) });
        sbox = new TextBox { Background = Br("#FF3C3A44"), Foreground = Brushes.White, BorderBrush = Br("#40FFFFFF"), Padding = new Thickness(10, 8, 10, 8), CaretBrush = Brushes.White, Margin = new Thickness(0, 0, 0, 12), ToolTip = "Поиск настроек" };
        side.Children.Add(sbox);
        for (int i = 0; i < pages.Count; i++)
        {
            int idx = i; SPage pg = pages[i];
            Border b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            Border ib = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Br(pg.Color), Child = Sym(pg.Glyph, 15) };
            sp.Children.Add(ib);
            sp.Children.Add(new TextBlock { Text = pg.Name, Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            b.Child = sp; b.MouseLeftButtonUp += delegate { sbox.Text = ""; SelectPage(idx); };
            side.Children.Add(b); sideItems.Add(b);
        }
        sidebar.Children.Add(side); grid.Children.Add(sidebar);
        DockPanel right = new DockPanel { Margin = new Thickness(32, 24, 32, 0) };
        htitle = new TextBlock { Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold };
        hsub = new TextBlock { Foreground = Br("#A0FFFFFF"), FontSize = 14, Margin = new Thickness(0, 2, 0, 14) };
        StackPanel hd = new StackPanel(); hd.Children.Add(htitle); hd.Children.Add(hsub);
        DockPanel.SetDock(hd, Dock.Top); right.Children.Add(hd);
        StackPanel content = new StackPanel();
        foreach (SPage pg in pages) content.Children.Add(pg.Panel);
        right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content });
        Grid.SetColumn(right, 1); grid.Children.Add(right);
        sw.Content = grid;
        sbox.TextChanged += delegate
        {
            string q = sbox.Text.Trim().ToLower();
            if (q.Length == 0) { SelectPage(curPage); return; }
            htitle.Text = "Результаты поиска"; hsub.Text = "";
            foreach (SPage pg in pages)
            {
                pg.Panel.Visibility = Visibility.Visible;
                foreach (FrameworkElement r in pg.Panel.Children) r.Visibility = (r.Tag != null && r.Tag.ToString().ToLower().Contains(q)) ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        SelectPage(0);
    }
    void SelectPage(int i)
    {
        curPage = i;
        for (int k = 0; k < pages.Count; k++)
        {
            pages[k].Panel.Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement r in pages[k].Panel.Children) r.Visibility = Visibility.Visible;
            sideItems[k].Background = k == i ? Br("#FF55535B") : (Brush)Brushes.Transparent;
        }
        htitle.Text = pages[i].Name; hsub.Text = pages[i].Sub;
    }
    void OpenSettings()
    {
        if (!lw.IsVisible) { hiddenAt = DateTime.Now.AddSeconds(-5); Toggle(); }
        sw.Left = Math.Max(0, Math.Min(I("Left") + I("Width") + 30, SystemParameters.PrimaryScreenWidth - 840));
        sw.Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - sw.Height) / 2 - 40);
        sw.Show(); sw.Activate();
    }

    // ---------- обновления через GitHub Releases ----------
    void SetUpd(string s) { app.Dispatcher.BeginInvoke(new Action(delegate { if (updStatus != null) updStatus.Text = s; })); }
    static Version ParseVer(string s)
    {
        Match m = Regex.Match(s ?? "", @"(\d+)\.(\d+)\.(\d+)");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : new Version(0, 0, 0);
    }
    void CheckUpdates(bool manual)
    {
        if (Repo.StartsWith("OWNER")) { if (manual) SetUpd("Репозиторий не указан (см. README)"); return; }
        if (manual) SetUpd("Проверяю...");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                r.UserAgent = "PurpleLibrary"; r.Timeout = 15000;
                string json;
                using (WebResponse resp = r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();
                Match t = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                Match u = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*PurpleLibrary\\.exe)\"");
                if (!t.Success || !u.Success) { if (manual) SetUpd("В релизе нет файла PurpleLibrary.exe"); return; }
                if (ParseVer(t.Groups[1].Value) > ParseVer(CurVer))
                {
                    newTag = t.Groups[1].Value; newUrl = u.Groups[1].Value;
                    app.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        updStatus.Text = "Доступна версия " + newTag; updBtn.Content = "Обновить";
                        if (!manual) ni.ShowBalloonTip(6000, "Purple Library", "Доступна версия " + newTag + ". Откройте настройки, раздел О программе.", System.Windows.Forms.ToolTipIcon.Info);
                    }));
                }
                else if (manual) SetUpd("У вас последняя версия");
            }
            catch (Exception) { if (manual) SetUpd("Не удалось проверить (нет доступа к GitHub?)"); }
        });
    }
    void ApplyUpdate()
    {
        SetUpd("Скачиваю...");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location, tmp = exe + ".new";
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (WebClient wc = new WebClient()) { wc.Headers["User-Agent"] = "PurpleLibrary"; wc.DownloadFile(newUrl, tmp); }
                if (new FileInfo(tmp).Length < 100000) throw new Exception("bad download");
                string cmd = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "purplelibrary-update.cmd");
                File.WriteAllText(cmd, "@echo off\r\nping 127.0.0.1 -n 4 >nul\r\nmove /y \"" + tmp + "\" \"" + exe + "\" >nul\r\nstart \"\" \"" + exe + "\"\r\ndel \"%~f0\"\r\n");
                app.Dispatcher.BeginInvoke(new Action(delegate
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                    psi.CreateNoWindow = true; psi.UseShellExecute = false; Process.Start(psi); Quit();
                }));
            }
            catch (Exception) { SetUpd("Не удалось скачать обновление"); }
        });
    }

    // ---------- трей и запуск ----------
    void BuildTray()
    {
        ni = new System.Windows.Forms.NotifyIcon();
        try { ni.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
        catch { ni.Icon = System.Drawing.SystemIcons.Application; }
        ni.Text = "Purple Library"; ni.Visible = true;
        System.Windows.Forms.ContextMenuStrip ms = new System.Windows.Forms.ContextMenuStrip();
        ms.Items.Add("Открыть библиотеку").Click += delegate { Toggle(); };
        ms.Items.Add("Настройки").Click += delegate { OpenSettings(); };
        ms.Items.Add("Проверить обновления").Click += delegate { OpenSettings(); SelectPage(4); CheckUpdates(true); };
        ms.Items.Add("-");
        ms.Items.Add("Выход").Click += delegate { Quit(); };
        ni.ContextMenuStrip = ms;
        ni.BalloonTipClicked += delegate { OpenSettings(); SelectPage(4); };
        ni.MouseClick += delegate(object s, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) Toggle(); };
    }
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == 0x5142) { Toggle(); handled = true; }
        return IntPtr.Zero;
    }
    public void Start()
    {
        LoadCfg(); Scan();
        if (!pinsSaved) DefaultPins();
        BuildLibrary(); BuildSettings(); BuildTray();
        ApplyTheme(true);
        Native.D = Dispatcher.CurrentDispatcher; Native.OnWin = delegate { Toggle(); };
        Native.Hook(); Native.On = B("WinKey");
        IntPtr h = new WindowInteropHelper(lw).EnsureHandle();
        HwndSource.FromHwnd(h).AddHook(WndProc); Native.Hot(h);
        Thread t = new Thread(delegate() { while (true) { ev.WaitOne(); lw.Dispatcher.BeginInvoke(new Action(delegate { Toggle(); })); } });
        t.IsBackground = true; t.Start();
        bool bg = false; foreach (string a in args) if (a == "/bg") bg = true;
        if (!bg) Toggle();
        Thread upd = new Thread(delegate() { Thread.Sleep(8000); while (true) { if (B("AutoUpdate")) CheckUpdates(false); Thread.Sleep(6 * 3600 * 1000); } });
        upd.IsBackground = true; upd.Start();
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool created;
        Mutex mx = new Mutex(true, "PurpleLibrarySingleton", out created);
        EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "PurpleLibraryShow");
        if (!created) { ev.Set(); return; }
        try
        {
            Application app = new Application(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Lib lib = new Lib(app, ev, args); lib.Start(); app.Run();
        }
        catch (Exception e)
        {
            File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PurpleLibrary-error.txt"), e.ToString());
            MessageBox.Show(e.Message, "Purple Library");
        }
        GC.KeepAlive(mx);
    }
}
