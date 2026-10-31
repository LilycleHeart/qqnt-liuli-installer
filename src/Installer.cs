// QQNT LiteLoader 一键安装器
// 内置：官方补丁 QQ.exe、dbghelp.dll、LiteLoaderQQNT 本体、琉璃与 QQ 增强插件。
// 编译：见 build.ps1（用 Windows 自带的 .NET Framework csc，无需另装运行库）。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteLoaderInstaller
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 无界面模式：/install [/noplugins] 或 /restore，日志写到 %APPDATA%\LiteLoaderQQNT\installer.log
            if (args.Length > 0)
            {
                string mode = args[0].ToLowerInvariant();
                bool plugins = Array.IndexOf(args, "/noplugins") < 0;
                return_code = MainForm.Headless(mode, plugins);
                Environment.Exit(return_code);
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
        static int return_code;
    }

    class MainForm : Form
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        // 配色：琉璃粉
        static readonly Color Brand = Color.FromArgb(0xD9, 0x57, 0x7B);
        static readonly Color BrandHover = Color.FromArgb(0xC8, 0x48, 0x6C);
        static readonly Color Bg = Color.FromArgb(0xFB, 0xF8, 0xF9);
        static readonly Color CardBg = Color.White;
        static readonly Color Line = Color.FromArgb(0xEC, 0xE4, 0xE7);
        static readonly Color TextMain = Color.FromArgb(0x22, 0x1A, 0x1D);
        static readonly Color TextSub = Color.FromArgb(0x7A, 0x6E, 0x72);
        static readonly Color Ok = Color.FromArgb(0x2E, 0x7D, 0x4F);
        static readonly Color Err = Color.FromArgb(0xC6, 0x28, 0x28);

        TextBox qqBox;
        CheckBox pluginBox;
        RoundButton installBtn;
        LinkLabel restoreBtn, browseBtn, detailBtn;
        Label statusLbl;
        ProgressBar bar;
        TextBox logBox;
        const int CollapsedH = 360, ExpandedH = 540;

        static string LLRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteLoaderQQNT"); }
        }
        static string BackupRoot { get { return Path.Combine(LLRoot, "installer-backup"); } }

        public MainForm()
        {
            Text = "琉璃安装器";
            Font = new Font("Microsoft YaHei UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, CollapsedH);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = TextMain;
            Icon = AppIcon();

            var title = new Label
            {
                Text = "琉璃 for QQ", Font = new Font(Font.FontFamily, 18f, FontStyle.Bold),
                ForeColor = TextMain, Location = new Point(28, 26), AutoSize = true
            };
            var sub = new Label
            {
                Text = "一键装好 LiteLoader 和琉璃主题", ForeColor = TextSub,
                Location = new Point(30, 68), AutoSize = true
            };

            // 卡片：QQ 位置 + 插件开关
            var card = new CardPanel { Location = new Point(28, 104), Size = new Size(424, 116), BackColor = CardBg, Border = Line };
            var pathLbl = new Label { Text = "QQ 位置", ForeColor = TextSub, Location = new Point(18, 16), AutoSize = true };
            browseBtn = MakeLink("更改", 0);
            browseBtn.Location = new Point(card.Width - 18 - browseBtn.PreferredWidth, 16);
            browseBtn.LinkClicked += delegate
            {
                using (var d = new FolderBrowserDialog { Description = "选择 QQ.exe 所在的文件夹" })
                    if (d.ShowDialog(this) == DialogResult.OK) { qqBox.Text = d.SelectedPath; CheckPath(); }
            };
            qqBox = new TextBox
            {
                Text = FindQQ() ?? "", BorderStyle = BorderStyle.None, BackColor = CardBg, ForeColor = TextMain,
                Location = new Point(18, 42), Width = card.Width - 36
            };
            qqBox.TextChanged += delegate { CheckPath(); };
            var sep = new Panel { BackColor = Line, Location = new Point(18, 70), Size = new Size(card.Width - 36, 1) };
            pluginBox = new CheckBox
            {
                Text = "同时安装琉璃主题", Checked = true, ForeColor = TextMain, FlatStyle = FlatStyle.System,
                Location = new Point(18, 82), AutoSize = true
            };
            card.Controls.AddRange(new Control[] { pathLbl, browseBtn, qqBox, sep, pluginBox });

            installBtn = new RoundButton
            {
                Text = "一键安装", Location = new Point(28, 240), Size = new Size(424, 46),
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Normal = Brand, Hover = BrandHover
            };
            installBtn.Click += delegate { Run(true); };

            bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Location = new Point(28, 294), Size = new Size(424, 3), Visible = false };

            statusLbl = new Label
            {
                Text = "会先关闭 QQ，并备份原文件", ForeColor = TextSub,
                Location = new Point(28, 312), Size = new Size(280, 22), AutoEllipsis = true
            };
            restoreBtn = MakeLink("还原", 0);
            detailBtn = MakeLink("详情", 0);
            detailBtn.Location = new Point(452 - detailBtn.PreferredWidth, 312);
            restoreBtn.Location = new Point(detailBtn.Left - 16 - restoreBtn.PreferredWidth, 312);
            restoreBtn.LinkClicked += delegate
            {
                if (MessageBox.Show(this, "把 QQ 恢复成安装前的样子？\n琉璃的设置和数据会留着。", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Run(false);
            };
            detailBtn.LinkClicked += delegate { ToggleDetail(!logBox.Visible); };

            logBox = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = CardBg, ForeColor = TextSub, Font = new Font("Consolas", 9f),
                Location = new Point(28, 348), Size = new Size(424, 168), Visible = false
            };

            Controls.AddRange(new Control[] { title, sub, card, installBtn, bar, statusLbl, restoreBtn, detailBtn, logBox });
            CheckPath();
        }

        LinkLabel MakeLink(string text, int x)
        {
            return new LinkLabel
            {
                Text = text, AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline,
                LinkColor = Brand, ActiveLinkColor = BrandHover, DisabledLinkColor = TextSub, BackColor = Color.Transparent
            };
        }

        void ToggleDetail(bool show)
        {
            logBox.Visible = show;
            detailBtn.Text = show ? "收起" : "详情";
            detailBtn.Left = 452 - detailBtn.PreferredWidth;
            ClientSize = new Size(ClientSize.Width, show ? ExpandedH : CollapsedH);
        }

        bool PathOk()
        {
            string root = qqBox.Text.Trim().Trim('"');
            return root.Length > 0 && File.Exists(Path.Combine(root, "QQ.exe")) && Directory.Exists(Path.Combine(root, "versions"));
        }

        void CheckPath()
        {
            if (installBtn == null || bar.Visible) return;
            bool ok = PathOk();
            installBtn.Enabled = ok;
            SetStatus(ok ? "会先关闭 QQ，并备份原文件" : "没找到 QQ，请点「更改」选 QQ.exe 所在文件夹", ok ? TextSub : Err);
        }

        void SetStatus(string s, Color c)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, Color>(SetStatus), s, c); return; }
            statusLbl.Text = s;
            statusLbl.ForeColor = c;
        }

        void Log(string s)
        {
            if (headlessLog != null) { headlessLog.WriteLine(s); headlessLog.Flush(); return; }
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText(s + "\r\n");
            SetStatus(s, TextSub);
        }

        // 窗口图标：Material "palette" 图标的几何，画在品牌色圆底上
        static Icon AppIcon()
        {
            using (var bmp = new Bitmap(64, 64))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(Brand)) g.FillEllipse(b, 2, 2, 60, 60);
                    using (var w = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(w, 14, 14, 36, 36);
                        using (var dot = new SolidBrush(Brand))
                        {
                            g.FillEllipse(dot, 20, 26, 7, 7);
                            g.FillEllipse(dot, 27, 18, 7, 7);
                            g.FillEllipse(dot, 37, 21, 7, 7);
                        }
                        using (var hole = new SolidBrush(Brand)) g.FillEllipse(hole, 34, 34, 9, 9);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        StreamWriter headlessLog;
        MainForm(StreamWriter w) { headlessLog = w; }

        public static int Headless(string mode, bool plugins)
        {
            Directory.CreateDirectory(LLRoot);
            using (var w = new StreamWriter(Path.Combine(LLRoot, "installer.log"), false, Utf8))
            {
                var f = new MainForm(w);
                try
                {
                    string root = FindQQ();
                    if (root == null) throw new Exception("找不到 QQ");
                    w.WriteLine("QQ: " + root);
                    if (mode == "/install") f.Install(root, plugins);
                    else if (mode == "/restore") f.Restore(root);
                    else throw new Exception("未知参数 " + mode);
                    w.WriteLine("OK");
                    return 0;
                }
                catch (Exception ex) { w.WriteLine("失败：" + ex.Message); return 1; }
            }
        }

        void Run(bool install)
        {
            string root = qqBox.Text.Trim().Trim('"');
            bool plugins = pluginBox.Checked;
            if (!PathOk()) { CheckPath(); return; }
            installBtn.Enabled = restoreBtn.Enabled = browseBtn.Enabled = pluginBox.Enabled = false;
            qqBox.ReadOnly = true;
            installBtn.Text = install ? "正在安装…" : "正在还原…";
            bar.Visible = true;
            new Thread(delegate ()
            {
                string done = null, fail = null;
                try
                {
                    if (install) Install(root, plugins); else Restore(root);
                    done = install ? "安装完成，QQ 已打开" : "还原完成，QQ 已打开";
                    Log(done);
                }
                catch (Exception ex) { fail = "失败：" + ex.Message; Log(fail); }
                BeginInvoke(new Action(delegate
                {
                    bar.Visible = false;
                    qqBox.ReadOnly = false;
                    installBtn.Enabled = restoreBtn.Enabled = browseBtn.Enabled = pluginBox.Enabled = true;
                    installBtn.Text = done != null && install ? "重新安装" : "一键安装";
                    SetStatus(done ?? fail, done != null ? Ok : Err);
                    if (fail != null) ToggleDetail(true);
                }));
            }) { IsBackground = true }.Start();
        }

        // ---------- 安装 ----------
        void Install(string root, bool plugins)
        {
            StopQQ();
            string[] apps = AppDirs(root);
            if (apps.Length == 0) throw new Exception("找不到 versions\\*\\resources\\app\\package.json");

            // 备份（只在第一次安装时备份原版，避免把已打补丁的文件当成原版）
            string bk = Path.Combine(BackupRoot, "original");
            if (!Directory.Exists(bk))
            {
                Directory.CreateDirectory(bk);
                foreach (var n in new[] { "QQ.exe", "dbghelp.dll", "winmm.dll" })
                {
                    string p = Path.Combine(root, n);
                    // 已经是我们的补丁文件就不算原版（比如卸载 QQ 后残留的 dbghelp.dll）
                    if (File.Exists(p) && !SameAsRes(n, p)) File.Copy(p, Path.Combine(bk, n), true);
                }
                var list = new StringBuilder();
                for (int i = 0; i < apps.Length; i++)
                {
                    File.Copy(Path.Combine(apps[i], "package.json"), Path.Combine(bk, "package." + i + ".json"), true);
                    list.AppendLine(apps[i]);
                }
                File.WriteAllText(Path.Combine(bk, "apps.txt"), list.ToString(), Utf8);
                Log("已备份原文件到 " + bk);
            }
            else Log("已有原版备份，跳过备份。");

            // 补丁文件
            WriteRes("QQ.exe", Path.Combine(root, "QQ.exe"));
            WriteRes("dbghelp.dll", Path.Combine(root, "dbghelp.dll"));
            // QQEX.exe（内置浏览器）不加载 dbghelp，靠这个 winmm 代理把补丁带进去，
            // 否则打开网页时会弹"文件已损坏"
            WriteRes("winmm.dll", Path.Combine(root, "winmm.dll"));
            Log("已放入 QQ.exe、dbghelp.dll 和 winmm.dll");

            // LiteLoader 本体（保留已有的 plugins 和 data）
            Directory.CreateDirectory(LLRoot);
            ExtractRes("LiteLoaderQQNT.zip", LLRoot);
            Log("LiteLoader 已装到 " + LLRoot);

            if (plugins)
            {
                ExtractRes("plugins.zip", Path.Combine(LLRoot, "plugins"));
                Log("琉璃插件已装好");
            }

            // 启动器 + package.json
            string launcher = "require(String.raw`" + LLRoot + "`);\n";
            foreach (var app in apps)
            {
                string dir = Path.Combine(app, "app_launcher");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "LiteLoader.js"), launcher, Utf8);
                string pkg = Path.Combine(app, "package.json");
                string json = File.ReadAllText(pkg);
                string updated = Regex.Replace(json, "\"main\"\\s*:\\s*\"[^\"]*\"", "\"main\": \"./app_launcher/LiteLoader.js\"");
                File.WriteAllText(pkg, updated, Utf8);
                Log("已改 " + pkg);
            }
            StartQQ(root);
        }

        // ---------- 还原 ----------
        void Restore(string root)
        {
            string bk = Path.Combine(BackupRoot, "original");
            if (!Directory.Exists(bk)) throw new Exception("没有找到备份，无法还原。");
            StopQQ();
            foreach (var n in new[] { "QQ.exe", "dbghelp.dll", "winmm.dll" })
            {
                string src = Path.Combine(bk, n), dst = Path.Combine(root, n);
                if (File.Exists(src)) File.Copy(src, dst, true);
                else if (n != "QQ.exe" && File.Exists(dst)) File.Delete(dst);
            }
            string[] apps = File.ReadAllLines(Path.Combine(bk, "apps.txt"));
            for (int i = 0; i < apps.Length; i++)
            {
                string src = Path.Combine(bk, "package." + i + ".json");
                if (apps[i].Trim().Length > 0 && File.Exists(src) && Directory.Exists(apps[i]))
                    File.Copy(src, Path.Combine(apps[i], "package.json"), true);
                // 安装时多建的 app_launcher 也删掉，app 目录回到原样（多出的文件夹可能让 QQ 自检报「文件已损坏」）
                string launcherDir = Path.Combine(apps[i].Trim(), "app_launcher");
                if (apps[i].Trim().Length > 0 && Directory.Exists(launcherDir))
                {
                    try { Directory.Delete(launcherDir, true); Log("已删除 " + launcherDir); }
                    catch (Exception e) { Log("删除 " + launcherDir + " 失败：" + e.Message); }
                }
            }
            Directory.Delete(bk, true);
            Log("已还原 QQ.exe、dbghelp.dll 和 package.json。插件和数据保留在 " + LLRoot);
            StartQQ(root);
        }

        // ---------- 工具 ----------
        static string FindQQ()
        {
            foreach (var hive in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, hive))
                    using (var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QQ"))
                    {
                        if (k == null) continue;
                        string u = (k.GetValue("UninstallString") as string ?? "").Trim('"');
                        string dir = Path.GetDirectoryName(u);
                        if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "QQ.exe"))) return dir;
                    }
                }
                catch { }
            }
            string def = @"C:\Program Files\Tencent\QQNT";
            return File.Exists(Path.Combine(def, "QQ.exe")) ? def : null;
        }

        static string[] AppDirs(string root)
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (var v in Directory.GetDirectories(Path.Combine(root, "versions")))
            {
                string app = Path.Combine(v, "resources", "app");
                if (File.Exists(Path.Combine(app, "package.json"))) result.Add(app);
            }
            return result.ToArray();
        }

        void StopQQ()
        {
            foreach (var name in new[] { "QQ", "QQEX" })
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.Kill(); } catch { }
            for (int i = 0; i < 25 && Process.GetProcessesByName("QQ").Length > 0; i++)
            {
                // 子进程可能晚一步冒出来或第一次没杀掉，每秒再杀一遍
                foreach (var p in Process.GetProcessesByName("QQ")) try { p.Kill(); } catch { }
                Thread.Sleep(1000);
            }
            if (Process.GetProcessesByName("QQ").Length > 0) throw new Exception("QQ 关不掉，请手动退出 QQ 后再试。");
            Thread.Sleep(800);
            Log("QQ 已关闭");
        }

        static void StartQQ(string root)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(root, "QQ.exe")) { WorkingDirectory = root, UseShellExecute = true });
        }

        static Stream Res(string name)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) throw new Exception("安装包缺少文件：" + name);
            return s;
        }

        static bool SameAsRes(string name, string file)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var s = Res(name))
            using (var f = File.OpenRead(file))
                return Convert.ToBase64String(sha.ComputeHash(s)) == Convert.ToBase64String(sha.ComputeHash(f));
        }

        static void WriteRes(string name, string dst)
        {
            using (var s = Res(name))
            using (var f = File.Create(dst)) s.CopyTo(f);
        }

        // 解压并覆盖同名文件；目标里多出来的文件不动
        static void ExtractRes(string name, string dstRoot)
        {
            using (var s = Res(name))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                string full = Path.GetFullPath(dstRoot).TrimEnd('\\') + "\\";
                foreach (var e in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(dstRoot, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            }
        }
    }
}


namespace LiteLoaderInstaller
{
    using System.Drawing.Drawing2D;

    static class Shapes
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // 圆角主按钮：自己画，悬停变深，禁用变灰
    class RoundButton : Control
    {
        public Color Normal, Hover;
        bool over, down;

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            ForeColor = Color.White;
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            Color fill = !Enabled ? Color.FromArgb(0xE6, 0xDC, 0xDF) : (down || over ? Hover : Normal);
            using (var path = Shapes.Round(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle,
                Enabled ? ForeColor : Color.FromArgb(0x9A, 0x8E, 0x92),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // 白底圆角卡片，带细边
    class CardPanel : Panel
    {
        public Color Border;
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            using (var path = Shapes.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
                using (var p = new Pen(Border)) g.DrawPath(p, path);
            }
        }
    }
}
