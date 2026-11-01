// QQNT LiteLoader 一键安装器
// 内置：官方补丁 QQ.exe、dbghelp.dll、LiteLoaderQQNT 本体、琉璃与 QQ 增强插件。
// 编译：见 build.ps1（用 Windows 自带的 .NET Framework csc，无需另装运行库）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyVersion("1.0.9.0")]
[assembly: AssemblyFileVersion("1.0.9.0")]

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
        LinkLabel restoreBtn, browseBtn, detailBtn, diagBtn;
        Label statusLbl;
        ProgressBar bar;
        TextBox logBox;
        const int CollapsedH = 360, ExpandedH = 540;

        // LiteLoader 本体/数据目录。LiteLoader 自己认 LITELOADERQQNT_PROFILE（见 loader/src/main/api.js），
        // 这里保持一致：用户设了自定义 profile 时，安装器和诊断包也看同一个地方。
        static string LLRoot
        {
            get
            {
                try
                {
                    string p = Environment.GetEnvironmentVariable("LITELOADERQQNT_PROFILE");
                    if (!string.IsNullOrEmpty(p)) return p.Trim().Trim('"').TrimEnd('\\');
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteLoaderQQNT");
            }
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

            // 右上角：一键导出诊断包（装不上或 QQ 报错时让用户点这里）
            diagBtn = MakeLink("导出诊断包", 0);
            diagBtn.Location = new Point(452 - diagBtn.PreferredWidth, 32);
            diagBtn.LinkClicked += delegate { ExportDiag(); };

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

            Controls.AddRange(new Control[] { title, sub, diagBtn, card, installBtn, bar, statusLbl, restoreBtn, detailBtn, logBox });
            OpenGuiLog();
            Log("安装器界面已启动（GUI 日志写入 installer.log，带时间戳）");
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
            if (headlessLog != null)
            {
                try { headlessLog.WriteLine(LogStamp() + " [HEADLESS] " + s); headlessLog.Flush(); } catch { }
                return;
            }
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            // GUI：窗口里显示的同时 best-effort 落盘，用户用界面装失败也能拿到记录
            if (fileLog != null)
            {
                try { fileLog.WriteLine(LogStamp() + " [GUI] " + s); fileLog.Flush(); } catch { }
            }
            logBox.AppendText(s + "\r\n");
            SetStatus(s, TextSub);
        }

        static string LogStamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        // 轮转：超过 maxBytes 就把 installer.log 移到 installer.1.log（只留 1 个历史）
        static void RotateLogIfNeeded(string path, long maxBytes)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length <= maxBytes) return;
                string dir = Path.GetDirectoryName(path);
                string bak = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".1.log");
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(path, bak);
            }
            catch { }
        }

        // GUI 模式打开 installer.log（追加；FileShare.ReadWrite 让诊断包能同时读一份）
        void OpenGuiLog()
        {
            try
            {
                Directory.CreateDirectory(LLRoot);
                string path = Path.Combine(LLRoot, "installer.log");
                RotateLogIfNeeded(path, 1024 * 1024);
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                fileLog = new StreamWriter(fs, Utf8);
                fileLog.AutoFlush = true;
            }
            catch { fileLog = null; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                if (fileLog != null) { fileLog.Flush(); fileLog.Dispose(); fileLog = null; }
            }
            catch { }
            base.OnFormClosed(e);
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
        StreamWriter fileLog;   // GUI 模式下 best-effort 落盘的 installer.log
        MainForm(StreamWriter w) { headlessLog = w; }

        public static int Headless(string mode, bool plugins)
        {
            Directory.CreateDirectory(LLRoot);
            string logPath = Path.Combine(LLRoot, "installer.log");
            RotateLogIfNeeded(logPath, 1024 * 1024);
            // FileShare.ReadWrite：诊断包导出时要能把正在写的 installer.log 也读一份进包。
            // /diag 用追加模式，不抹掉之前的安装记录（对排查「装过什么、哪次失败」很重要）。
            using (var logStream = new FileStream(logPath,
                mode == "/diag" ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var w = new StreamWriter(logStream, Utf8))
            {
                var f = new MainForm(w);
                try
                {
                    if (mode == "/diag")
                    {
                        // 无界面导出诊断包：不要求 QQ 就在装好的位置，装不上时也能用
                        string zip = f.BuildDiag();
                        HeadlessLine(w, "诊断包: " + zip);
                        HeadlessLine(w, "输出目录: " + Path.GetDirectoryName(zip));
                        HeadlessLine(w, "提交 issue: " + BuildIssueUrl(zip));
                        return 0;
                    }
                    string root = FindQQ();
                    if (root == null) throw new Exception("找不到 QQ");
                    HeadlessLine(w, "QQ: " + root);
                    if (mode == "/install") f.Install(root, plugins);
                    else if (mode == "/restore") f.Restore(root);
                    else throw new Exception("未知参数 " + mode);
                    HeadlessLine(w, "OK");
                    return 0;
                }
                catch (Exception ex) { HeadlessLine(w, "失败：" + ex.Message); return 1; }
            }
        }

        static void HeadlessLine(StreamWriter w, string s)
        {
            try { w.WriteLine(LogStamp() + " [HEADLESS] " + s); w.Flush(); } catch { }
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
            // 聊天记录默认放在「文档」下；「文档」被 OneDrive 接管时，云文件 + 同步锁
            // 很容易让 QQ 报「文件系统访问异常」，提前提醒一句
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (docs.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log("提示：你的「文档」在 OneDrive 里，QQ 聊天记录也存在那儿。若 QQ 报「文件系统访问异常」，请到 QQ 设置 → 文件管理，把存储位置改到非 OneDrive 的目录（比如 D:\\Tencent Files）。");
            }
            catch { }
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

        // ---------- 诊断包 ----------
        // 用户点一下「导出诊断包」，把 QQ 日志 + 环境信息打成桌面上的 zip。
        // 只读；只收日志和元数据，绝不收集聊天记录库内容。

        void ExportDiag()
        {
            diagBtn.Enabled = false;
            installBtn.Enabled = false;
            SetStatus("正在收集诊断包…", TextSub);
            Log("开始收集诊断包（只读，不含聊天记录）");
            new Thread(delegate ()
            {
                string zip = null, fail = null;
                try { zip = BuildDiag(); }
                catch (Exception ex) { fail = "诊断包生成失败：" + ex.Message; }
                BeginInvoke(new Action(delegate
                {
                    diagBtn.Enabled = true;
                    installBtn.Enabled = PathOk();
                    if (zip != null)
                    {
                        string url = BuildIssueUrl(zip);
                        string dir = Path.GetDirectoryName(zip);
                        bool onDesktop = string.Equals(dir, DesktopPath(), StringComparison.OrdinalIgnoreCase);
                        SetStatus(onDesktop ? "诊断包已存到桌面" : "诊断包已存到 " + dir, Ok);
                        Log("诊断包：" + zip);
                        if (!onDesktop) Log("桌面不可用，已按回退顺序改存到：" + dir);
                        Log("去 GitHub 反馈：" + url);
                        bool go = MessageBox.Show(this,
                            "诊断包已存到" + (onDesktop ? "桌面" : "（桌面不可用，已改存）") + "：" + Environment.NewLine
                            + "    " + zip
                            + Environment.NewLine + Environment.NewLine
                            + "要不要打开 GitHub 提交问题？" + Environment.NewLine
                            + "打开后：写一句问题现象，再把这个 zip 拖进输入框就能上传。",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                        if (go)
                        {
                            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                            catch (Exception ex2) { Log("打开浏览器失败：" + ex2.Message); ToggleDetail(true); }
                        }
                        else
                        {
                            // 不想发 issue 就帮他定位到文件，自己留着看
                            try { Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch { }
                        }
                    }
                    else { SetStatus(fail, Err); Log(fail); ToggleDetail(true); }
                }));
            }) { IsBackground = true }.Start();
        }

        string BuildDiag()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string tmp = Path.Combine(Path.GetTempPath(), "liuli-diag-" + stamp);
            Directory.CreateDirectory(tmp);
            var ctx = new DiagContext();
            try
            {
                ctx.StartedAt = DateTime.Now;
                PrepareContext(ctx);

                // 输出目录：桌面 → %USERPROFILE%\Desktop → 文档 → %TEMP% → APPDATA\LiteLoaderQQNT\diag
                // 先定下来，environment.txt / 自动体检 / UI 日志里都要写最终路径
                ctx.OutputDir = PickOutputDir(ctx);

                var info = new StringBuilder();
                info.AppendLine("========== 琉璃 QQ · 诊断包 ==========");
                info.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                try { info.AppendLine("安装器版本: " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion); } catch { }
                info.AppendLine("隐私: 只读采集。不含聊天记录内容（nt_msg.db 等）、表情缓存、插件源码全文；安装器自身不截图。");
                info.AppendLine("      LiteLoader/ 与插件日志已脱敏；QQ/Tencent 原始日志按原样收集（可能含本机路径/账号 ID/token 片段，公开前请确认）；");
                info.AppendLine("      plugin-diag/ 由插件导出策略决定，默认含设置页截图，勾选后还可能含聊天画面。");
                info.AppendLine("提示: 先看下面「自动体检」；日志里的可疑行汇总在 errors-digest.txt；包内说明见 README.txt。");
                info.AppendLine();

                var collectInfo = new StringBuilder();
                CollectLogs(tmp, collectInfo, ctx);
                CollectPluginDiag(tmp, collectInfo, ctx);
                // 隐私：LiteLoader/ 与 LiteLoader-data/ 的文本日志落盘前脱敏（QQ/Tencent 原始日志保持原样）
                try { SanitizeCollectedLogs(tmp, collectInfo, ctx); }
                catch (Exception ex) { collectInfo.AppendLine("隐私脱敏失败: " + ex.Message); }

                var sections = new List<StringBuilder>();
                BuildSections(sections, ctx);
                sections.Add(Section("采集清单（包内日志与插件包）", delegate (StringBuilder sb) { sb.Append(collectInfo.ToString()); }));

                // 错误聚合要在体积截断之前扫完（截断后原文件可能已被删）
                string digest = BuildErrorsDigest(tmp, ctx);

                var capNote = new StringBuilder();
                EnforceSizeCap(tmp, capNote, ctx);

                // FinishChecks 必须在 EnforceSizeCap 之后：自动体检要读截断结果（CapTruncated/CapBytesLimit）
                FinishChecks(ctx);

                WriteEnvInfo(info, ctx, sections, capNote.ToString());
                File.WriteAllText(Path.Combine(tmp, "environment.txt"), info.ToString(), Utf8);
                File.WriteAllText(Path.Combine(tmp, "errors-digest.txt"), digest, Utf8);
                File.WriteAllText(Path.Combine(tmp, "README.txt"), ReadmeText(), Utf8);

                return WriteZip(tmp, ctx.OutputDir, "QQ诊断包-" + stamp + ".zip");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        static string DesktopPath()
        {
            try
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            try
            {
                string up = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!string.IsNullOrEmpty(up)) return Path.Combine(up, "Desktop");
            }
            catch { }
            return "";
        }

        // 候选输出目录，按契约顺序：桌面 → %USERPROFILE%\Desktop → 文档 → %TEMP% → APPDATA\LiteLoaderQQNT\diag
        // bool = 目录不存在时是否允许创建
        static List<KeyValuePair<string, bool>> DiagOutputCandidates()
        {
            var list = new List<KeyValuePair<string, bool>>();
            var seen = new List<string>();
            Action<string, bool> add = delegate (string dir, bool create)
            {
                if (string.IsNullOrEmpty(dir)) return;
                string full;
                try { full = Path.GetFullPath(dir); } catch { return; }
                foreach (var s in seen) if (string.Equals(s, full, StringComparison.OrdinalIgnoreCase)) return;
                seen.Add(full);
                list.Add(new KeyValuePair<string, bool>(full, create));
            };
            try { add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false); } catch { }
            try
            {
                string up = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!string.IsNullOrEmpty(up)) add(Path.Combine(up, "Desktop"), false);
            }
            catch { }
            try { add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false); } catch { }
            try { add(Path.GetTempPath(), true); } catch { }
            try { add(Path.Combine(LLRoot, "diag"), true); } catch { }
            return list;
        }

        static bool WritableDir(string dir, bool create)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir))
                {
                    if (!create) return false;
                    Directory.CreateDirectory(dir);
                }
                string probe = Path.Combine(dir, ".liuli-diag-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var f = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.WriteByte(0);
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        string PickOutputDir(DiagContext ctx)
        {
            var cands = DiagOutputCandidates();
            var skipped = new List<string>();
            foreach (var kv in cands)
            {
                string dir = kv.Key;
                bool existed = Directory.Exists(dir);
                if (!WritableDir(dir, kv.Value))
                {
                    skipped.Add(dir + (existed ? "（只读/不可写）" : "（不存在）"));
                    continue;
                }
                if (!existed && kv.Value) ctx.OutputNotes.Add("新建目录: " + dir);
                if (skipped.Count > 0)
                    ctx.OutputNotes.Add("桌面不可用，回退到: " + dir);
                foreach (var s in skipped) ctx.OutputNotes.Add("跳过: " + s);
                if (skipped.Count > 0) Log("桌面不可用，诊断包改存到 " + dir);
                Log("诊断包输出目录: " + dir);
                return dir;
            }
            throw new Exception("没有可写的输出目录（桌面/文档/TEMP/APPDATA 都失败）：" + string.Join("；", skipped.ToArray()));
        }

        string WriteZip(string tmp, string outDir, string name)
        {
            var tried = new List<string>();
            var cands = new List<string>();
            if (!string.IsNullOrEmpty(outDir)) cands.Add(outDir);
            foreach (var kv in DiagOutputCandidates())
            {
                bool dup = false;
                foreach (var c in cands) if (string.Equals(c, kv.Key, StringComparison.OrdinalIgnoreCase)) dup = true;
                if (!dup) cands.Add(kv.Key);
            }
            Exception last = null;
            foreach (var dir in cands)
            {
                try
                {
                    if (!WritableDir(dir, true)) { tried.Add(dir + "（不可写）"); continue; }
                    string p = Path.Combine(dir, name);
                    if (File.Exists(p)) File.Delete(p);
                    ZipFile.CreateFromDirectory(tmp, p, CompressionLevel.Optimal, false);
                    if (!string.Equals(dir, outDir, StringComparison.OrdinalIgnoreCase))
                        Log("诊断包写入 " + outDir + " 失败，已改存到 " + dir);
                    return p;
                }
                catch (Exception ex) { last = ex; tried.Add(dir + "（" + ex.Message + "）"); }
            }
            throw new Exception("写入诊断包失败: " + (last == null ? "没有可用目录" : last.Message)
                + "（尝试过: " + string.Join("；", tried.ToArray()) + "）");
        }

        // 体积上限 ~30MB：超限按「日志 → 插件包 → 其它」顺序截断，并把截断情况写进报告
        // 上限 30MB；LIULI_DIAG_CAP_MB 可临时改小，仅用于验证截断逻辑
        static long CapBytes()
        {
            try
            {
                string s = Environment.GetEnvironmentVariable("LIULI_DIAG_CAP_MB");
                double mb;
                if (!string.IsNullOrEmpty(s)
                    && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out mb) && mb > 0)
                    return (long)(mb * 1024.0 * 1024.0);
            }
            catch { }
            return 30L * 1024 * 1024;
        }

        static void EnforceSizeCap(string tmp, StringBuilder note, DiagContext ctx)
        {
            long cap = CapBytes();
            ctx.CapBytesLimit = cap;
            long total = DirSize(tmp);
            note.AppendLine("  打包前合计: " + MbText(total) + "（上限 " + MbText(cap) + "，可用 LIULI_DIAG_CAP_MB 覆盖）");
            if (total <= cap) { note.AppendLine("  未超限，未截断。"); return; }
            note.AppendLine("  注意：下面「采集清单」里的文件数量/大小是截断前的数字。");

            long removedBytes = 0;
            int removedFiles = 0;

            // 1) 日志
            var logs = new List<FileInfo>();
            foreach (var pack in new[] { "QQ-log", "QQ-arks", "QQ-crashpad", "Tencent-Logs", "LiteLoader", "LiteLoader-data" })
            {
                string dir = Path.Combine(tmp, pack);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in SafeFiles(dir)) logs.Add(f);
            }
            logs.Sort(delegate (FileInfo a, FileInfo b) { return b.Length.CompareTo(a.Length); });
            foreach (var f in logs)
            {
                if (total <= CapBytes()) break;
                try { long len = f.Length; f.Delete(); total -= len; removedBytes += len; removedFiles++; } catch { }
            }
            if (removedFiles > 0)
                note.AppendLine("  日志截断: 删除 " + removedFiles + " 个日志文件（按从大到小），释放约 " + MbText(removedBytes) + "。原始日志仍在用户电脑上。");

            // 2) 插件导出的诊断包
            removedBytes = 0; removedFiles = 0;
            string pd = Path.Combine(tmp, "plugin-diag");
            if (total > CapBytes() && Directory.Exists(pd))
            {
                var zips = new List<FileInfo>();
                foreach (var f in new DirectoryInfo(pd).GetFiles()) zips.Add(f);
                zips.Sort(delegate (FileInfo a, FileInfo b) { return a.LastWriteTime.CompareTo(b.LastWriteTime); });
                foreach (var f in zips)
                {
                    if (total <= CapBytes()) break;
                    try { long len = f.Length; f.Delete(); total -= len; removedBytes += len; removedFiles++; } catch { }
                }
            }
            if (removedFiles > 0)
                note.AppendLine("  插件包截断: 删除 " + removedFiles + " 个插件导出的诊断包（先删旧的），释放约 " + MbText(removedBytes) + "。");

            // 3) 其它（保留三份人读报告）
            removedBytes = 0; removedFiles = 0;
            if (total > CapBytes())
            {
                var others = new List<FileInfo>();
                foreach (var f in SafeFiles(tmp))
                {
                    string n = f.Name;
                    if (string.Equals(n, "environment.txt", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n, "README.txt", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n, "errors-digest.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    others.Add(f);
                }
                others.Sort(delegate (FileInfo a, FileInfo b) { return b.Length.CompareTo(a.Length); });
                foreach (var f in others)
                {
                    if (total <= CapBytes()) break;
                    try { long len = f.Length; f.Delete(); total -= len; removedBytes += len; removedFiles++; } catch { }
                }
            }
            if (removedFiles > 0)
                note.AppendLine("  其它文件截断: 删除 " + removedFiles + " 个文件，释放约 " + MbText(removedBytes) + "。");
            note.AppendLine("  截断后合计: " + MbText(DirSize(tmp)));
            ctx.CapTruncated = true;
        }

        static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                string[] files;
                try { files = Directory.GetFiles(dir); } catch { files = new string[0]; }
                foreach (var f in files) { try { total += new FileInfo(f).Length; } catch { } }
                string[] subs;
                try { subs = Directory.GetDirectories(dir); } catch { subs = new string[0]; }
                foreach (var d in subs) total += DirSize(d);
            }
            catch { }
            return total;
        }

        static string MbText(long bytes)
        {
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " 字节";
            if (bytes < 1024L * 1024L) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        static string NStr(object v)
        {
            string s = Convert.ToString(v);
            return string.IsNullOrEmpty(s) ? "(未报告)" : s;
        }

        static string ReadmeText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("琉璃 QQ 诊断包（安装器版）");
            sb.AppendLine("========================================");
            sb.AppendLine();
            sb.AppendLine("怎么用");
            sb.AppendLine("  1. 先打开 environment.txt，看「自动体检（先看这里）」这一节；带 <<== 的就是");
            sb.AppendLine("     大概率对应的原因，每个结论下面都有原始证据。");
            sb.AppendLine("  2. 想看日志里的可疑行，看 errors-digest.txt（自动筛选的，不保证是根因）。");
            sb.AppendLine("  3. 各目录里的原始日志与上面两节对应，可以在 QQ-log/、LiteLoader/ 等目录里继续翻。");
            sb.AppendLine();
            sb.AppendLine("包含什么");
            sb.AppendLine("  environment.txt        人读总报告（环境、插件、补丁、本体、显示器、窗口/对话框、本地存储、时间线）");
            sb.AppendLine("  errors-digest.txt      日志错误聚合（关键字筛选 + 时间范围 + 样例行）");
            sb.AppendLine("  README.txt             本说明");
            sb.AppendLine("  QQ-log/                 %APPDATA%\\QQ\\log 下的日志文本");
            sb.AppendLine("  QQ-arks/                %APPDATA%\\QQ\\arks 下近 7 天的日志");
            sb.AppendLine("  QQ-crashpad/            QQ 崩溃报告里的文本");
            sb.AppendLine("  Tencent-Logs/           %APPDATA%\\Tencent\\Logs 里和 QQ 有关的日志");
            sb.AppendLine("  LiteLoader/             %APPDATA%\\LiteLoaderQQNT 顶层的 .log/.txt（installer.log 等）");
            sb.AppendLine("  LiteLoader-data/        各插件 data 目录下的小日志（.log/.txt，≤2MB/个，30 天内）");
            sb.AppendLine("  plugin-diag/            在琉璃设置页点「导出诊断包」生成的最新 zip（可选；内容由插件导出策略决定）");
            sb.AppendLine();
            sb.AppendLine("隐私边界（重要）");
            sb.AppendLine("  x 聊天记录数据库内容（nt_msg.db 等一律不复制，只列文件名/大小/占用者）");
            sb.AppendLine("  x 表情、图片、语音等聊天内容（安装器本身不截图）");
            sb.AppendLine("  x 插件源码全文（只给哈希/大小/修改时间）");
            sb.AppendLine("  x QQ Local Storage 的内容（只列元数据：文件数/大小/时间）");
            sb.AppendLine();
            sb.AppendLine("  已脱敏：LiteLoader/installer.log、LiteLoader/liteLoader.log、LiteLoader-data/** 下的");
            sb.AppendLine("  .log/.txt 文本日志，以及 errors-digest.txt 的样例行。规则：uin=数字、uid=…、u_xxx、");
            sb.AppendLine("  邮箱、sk-、Bearer、长 base64/dataURL → ***。无法按 UTF-8 解码或二进制文件跳过脱敏，");
            sb.AppendLine("  报告里会注明「未脱敏（非文本）」。");
            sb.AppendLine("  未脱敏：QQ-log/、QQ-arks/、Tencent-Logs/、QQ-crashpad/ 的 QQ 原始日志按原样收集");
            sb.AppendLine("  （脱敏会破坏排查），可能含本机路径、账号 ID、token 片段，公开提交前请自行确认。");
            sb.AppendLine();
            sb.AppendLine("  注意：plugin-diag/ 是插件自己导出的包，安装器只是原样带上。内容由插件导出策略决定，");
            sb.AppendLine("  默认包含设置页截图；如果导出时勾选了「包含聊天窗口截图」，里面还可能有聊天画面。");
            sb.AppendLine("  安装器自身不截图。提交前请确认是否要包含这些截图。");
            sb.AppendLine();
            sb.AppendLine("怎么提交给开发者");
            sb.AppendLine("  安装器里点「导出诊断包」后会自动询问要不要打开 GitHub 新建 issue；");
            sb.AppendLine("  把本 zip 直接拖进 issue 输入框即可上传。写一句现象（例如「每次启动都弹报错」");
            sb.AppendLine("  或「换了壁纸背景不显示」）会更容易定位。");
            sb.AppendLine();
            sb.AppendLine("想要更细的壁纸/渲染证据");
            sb.AppendLine("  打开 QQ → 设置 → 琉璃 → 最下面的「诊断」小节 → 勾选需要的内容 →「导出诊断包」。");
            sb.AppendLine("  那个包会带每个渲染窗口的快照（localStorage 键、#app::before 计算值、壁纸操作审计、");
            sb.AppendLine("  渲染进程错误环形缓冲）；勾选「包含界面截图」时可能含当前聊天画面，提交前请自己确认。");
            sb.AppendLine("  导出成功后，本安装器下次生成的诊断包会自动带上 plugin-diag/ 里的最新一份。");
            return sb.ToString();
        }

        // 打开 GitHub 新建 issue：预填问题模板 + 环境摘要，提醒把 zip 拖进去
        static string BuildIssueUrl(string zipPath)
        {
            string qqVer = "", win = "", ver = "";
            try
            {
                string root = FindQQ();
                if (root != null)
                {
                    string[] vs = Directory.GetDirectories(Path.Combine(root, "versions"));
                    if (vs.Length > 0) qqVer = Path.GetFileName(vs[vs.Length - 1]);
                }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                        win = Convert.ToString(k.GetValue("ProductName")) + " " + Convert.ToString(k.GetValue("CurrentBuild"));
                }
            }
            catch { }
            try { ver = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion; } catch { }

            var body = new StringBuilder();
            body.AppendLine("## 问题现象");
            body.AppendLine("（写一句：QQ 报什么错 / 卡在哪一步 / 做了什么之后出现的）");
            body.AppendLine();
            body.AppendLine("## 诊断包");
            body.AppendLine("请把桌面上的 `" + Path.GetFileName(zipPath) + "` 拖进这个输入框，GitHub 会自动上传。");
            body.AppendLine("里面只有日志和环境信息，不含聊天记录内容。");
            body.AppendLine();
            body.AppendLine("## 环境（自动生成）");
            body.AppendLine("- 安装器：" + (ver.Length > 0 ? ver : "未知"));
            body.AppendLine("- QQ：" + (qqVer.Length > 0 ? qqVer : "未知"));
            body.AppendLine("- 系统：" + (win.Length > 0 ? win : Environment.OSVersion.VersionString));
            body.AppendLine("- 文档目录：" + Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            string title = "【安装器 " + ver + "】问题描述（改成一句话）";
            return "https://github.com/LilycleHeart/qqnt-liuli-theme/issues/new?title="
                + Uri.EscapeDataString(title)
                + "&body=" + Uri.EscapeDataString(body.ToString());
        }

        // 收日志：只收真日志（不含 .db / 表情缓存 / 插件源码 / 备份文件）
        static void CollectLogs(string tmp, StringBuilder info, DiagContext ctx)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            long total = 0;
            //                        目录                                             打包名        单文件MB 最近N天 扩展名                       上限 必须含 递归
            total += CollectOne(tmp, info, Path.Combine(appData, @"QQ\log"),               "QQ-log",       5, 30, new[] { ".log", ".txt" },              50, null, true);
            total += CollectOne(tmp, info, Path.Combine(appData, @"QQ\arks"),              "QQ-arks",      3,  7, new[] { ".log", ".txt", ".json" },       30, null, true);
            total += CollectOne(tmp, info, Path.Combine(appData, @"QQ\Crashpad\reports"),  "QQ-crashpad",  2,  7, new[] { ".txt", ".log" },               10, null, true);
            total += CollectOne(tmp, info, Path.Combine(appData, @"Tencent\Logs"),         "Tencent-Logs", 3,  3, new[] { ".log", ".tlg", ".txt" },       10, "QQ", true);
            // LiteLoader 顶层的 .log/.txt（installer.log 等）；不碰 plugins / data 里的大文件
            total += CollectOne(tmp, info, LLRoot,                                         "LiteLoader",   2,  0, new[] { ".log", ".txt" },              20, null, false);
            // 新增：各插件 data 目录下的小日志（.log/.txt，≤2MB/个，30 天内）
            total += CollectOne(tmp, info, Path.Combine(LLRoot, "data"),                    "LiteLoader-data", 2, 30, new[] { ".log", ".txt" },          20, null, true);
            info.AppendLine("日志合计约 " + (total / 1024) + " KB");
            ctx.CollectedLogBytes = total;
        }

        // 插件自己导出的诊断包（data/<slug>/diag 里最新一个 zip），只复制文件、不解压
        static void CollectPluginDiag(string tmp, StringBuilder info, DiagContext ctx)
        {
            info.AppendLine("插件导出的诊断包:");
            int copied = 0;
            string data = Path.Combine(LLRoot, "data");
            try
            {
                if (!Directory.Exists(data)) { info.AppendLine("  (没有 data 目录)"); return; }
                foreach (var d in Directory.GetDirectories(data))
                {
                    string slug = Path.GetFileName(d);
                    string diag = Path.Combine(d, "diag");
                    if (!Directory.Exists(diag)) continue;
                    var zips = new List<FileInfo>();
                    try { foreach (var f in new DirectoryInfo(diag).GetFiles("*.zip")) zips.Add(f); }
                    catch (Exception ex) { info.AppendLine("  " + slug + ": 列目录失败 " + ex.Message); continue; }
                    if (zips.Count == 0) { info.AppendLine("  " + slug + ": diag 目录存在，但没有 zip"); continue; }
                    zips.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                    int listed = 0;
                    foreach (var z in zips)
                    {
                        if (listed >= 5) break;
                        info.AppendLine("  " + slug + ": " + z.Name + "  " + (z.Length / 1024) + " KB  修改 "
                            + z.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + (listed == 0 ? "   [取最新]" : ""));
                        listed++;
                    }
                    FileInfo newest = zips[0];
                    string dst = Path.Combine(tmp, "plugin-diag", slug + ".zip");
                    if (SafeCopy(newest.FullName, dst))
                    {
                        info.AppendLine("      来源: " + newest.FullName);
                        info.AppendLine("      打包为: plugin-diag/" + slug + ".zip（取 diag/ 里最新的一个）");
                        copied++;
                        if (ctx.PluginDiagTime == null || newest.LastWriteTime > ctx.PluginDiagTime.Value)
                        {
                            ctx.PluginDiagTime = newest.LastWriteTime;
                            ctx.PluginDiagPath = newest.FullName;
                            ctx.PluginDiagSlug = slug;
                        }
                        ctx.PluginDiagCount++;
                    }
                    else info.AppendLine("  复制 " + newest.FullName + " 失败（被占用？）");
                }
            }
            catch (Exception ex) { info.AppendLine("  采集失败: " + ex.Message); }
            if (copied == 0) info.AppendLine("  (没有找到插件导出的诊断包；壁纸细节请在琉璃设置页「诊断」里导出)");
            else info.AppendLine("  共收进 " + copied + " 个插件诊断包（plugin-diag/ 下）");
            info.AppendLine("  说明: plugin-diag/ 是插件自己导出的包，安装器原样带上；内容由插件导出策略决定，");
            info.AppendLine("        默认包含设置页截图，勾选「包含聊天窗口截图」时还可能含聊天画面（安装器自身不截图）。");
        }

        static string RelToTmp(string tmp, string path)
        {
            try
            {
                string root = tmp.TrimEnd('\\');
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return path.Substring(root.Length).TrimStart('\\').Replace('\\', '/');
            }
            catch { }
            return Path.GetFileName(path);
        }

        // 隐私：只对我们/插件写出的文本日志落盘前脱敏（QQ/Tencent 原始日志保留原样，脱敏会破坏排查）。
        // 规则与 errors-digest 一致；非文本/非 UTF-8 跳过并在报告里注明「未脱敏（非文本）」。
        static void SanitizeCollectedLogs(string tmp, StringBuilder info, DiagContext ctx)
        {
            int sanitized = 0, skipped = 0;
            var skippedNames = new List<string>();
            foreach (var root in new[] { Path.Combine(tmp, "LiteLoader"), Path.Combine(tmp, "LiteLoader-data") })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in SafeFiles(root))
                {
                    string ext = f.Extension.ToLowerInvariant();
                    if (ext != ".log" && ext != ".txt") continue;
                    string rel = RelToTmp(tmp, f.FullName);
                    try
                    {
                        byte[] data = ReadAllBytesShared(f.FullName, 32 * 1024 * 1024);
                        if (data == null)
                        {
                            skipped++;
                            if (skippedNames.Count < 20) skippedNames.Add(rel + "（读不了）");
                            continue;
                        }
                        bool binary = false;
                        int probe = Math.Min(data.Length, 2048);
                        for (int i = 0; i < probe; i++) if (data[i] == 0) { binary = true; break; }
                        string text = null;
                        if (!binary)
                        {
                            try { text = new UTF8Encoding(false, true).GetString(data); }
                            catch { text = null; }
                        }
                        if (text == null)
                        {
                            skipped++;
                            if (skippedNames.Count < 20) skippedNames.Add(rel + (binary ? "（二进制）" : "（非 UTF-8）"));
                            continue;
                        }
                        string safe = SanitizeText(text);
                        if (!string.Equals(safe, text, StringComparison.Ordinal))
                        {
                            try
                            {
                                var enc = new UTF8Encoding(false);
                                using (var fs = new FileStream(f.FullName, FileMode.Create, FileAccess.Write, FileShare.Read))
                                using (var sw = new StreamWriter(fs, enc)) sw.Write(safe);
                                try { File.SetLastWriteTimeUtc(f.FullName, f.LastWriteTimeUtc); } catch { }
                            }
                            catch
                            {
                                skipped++;
                                if (skippedNames.Count < 20) skippedNames.Add(rel + "（重写失败）");
                                continue;
                            }
                        }
                        sanitized++;
                    }
                    catch
                    {
                        skipped++;
                        if (skippedNames.Count < 20) skippedNames.Add(rel + "（处理失败）");
                    }
                }
            }
            ctx.RedactedFiles = sanitized;
            ctx.RedactionSkipped = skipped;
            info.AppendLine("隐私脱敏: LiteLoader/ 与 LiteLoader-data/ 下的文本日志（.log/.txt）已按 errors-digest 同一套规则脱敏");
            info.AppendLine("          （uin=/uid=/u_xxx/邮箱/sk-/Bearer/长 base64/dataURL → ***）；本次处理 " + sanitized + " 个文件");
            if (skipped > 0)
                info.AppendLine("          未脱敏（非文本）: " + skipped + " 个" + (skippedNames.Count > 0 ? "，" + string.Join("；", skippedNames.ToArray()) : ""));
            info.AppendLine("          QQ-log/、QQ-arks/、Tencent-Logs/、QQ-crashpad/ 的原始日志按原样收集（未脱敏，公开前请自行确认）");
        }

        static long CollectOne(string tmp, StringBuilder info, string src, string packName,
            int maxFileMB, int sinceDays, string[] exts, int maxFiles, string mustContain, bool recurse)
        {
            if (!Directory.Exists(src)) { info.AppendLine("日志目录不存在: " + src); return 0; }
            string srcFull = src.TrimEnd('\\');
            DateTime cutoff = sinceDays > 0 ? DateTime.Now.AddDays(-sinceDays) : DateTime.MinValue;
            long maxBytes = maxFileMB * 1024L * 1024L;

            var cands = new List<FileInfo>();
            foreach (var f in SafeFiles(src))
            {
                if (!recurse)
                {
                    string parent = (f.DirectoryName == null ? "" : f.DirectoryName.TrimEnd('\\'));
                    if (!string.Equals(parent, srcFull, StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (f.Length > maxBytes) continue;
                if (f.LastWriteTime < cutoff) continue;
                bool extOk = false;
                foreach (var e in exts) if (f.Extension.Equals(e, StringComparison.OrdinalIgnoreCase)) { extOk = true; break; }
                if (!extOk) continue;
                if (mustContain != null && f.Name.IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) < 0) continue;
                cands.Add(f);
            }
            cands.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });

            long got = 0;
            int n = 0;
            foreach (var f in cands)
            {
                if (n >= maxFiles) break;
                string rel = f.FullName.Substring(srcFull.Length).TrimStart('\\');
                if (SafeCopy(f.FullName, Path.Combine(tmp, packName, rel))) { got += f.Length; n++; }
            }
            info.AppendLine("日志: " + src + "  -> " + packName + "，" + n + " 个文件，" + (got / 1024) + " KB");
            return got;
        }

        static IEnumerable<FileInfo> SafeFiles(string dir)
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                FileInfo[] files;
                try { files = new DirectoryInfo(d).GetFiles(); } catch { continue; }
                foreach (var f in files) yield return f;
                string[] subs;
                try { subs = Directory.GetDirectories(d); } catch { continue; }
                foreach (var s in subs)
                {
                    string leaf = Path.GetFileName(s);
                    if (leaf == "Cache" || leaf == "Code Cache" || leaf == "blob_storage" || leaf == "GPUCache") continue;
                    stack.Push(s);
                }
            }
        }

        static bool SafeCopy(string src, string dst)
        {
            try
            {
                string dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var d = File.Create(dst)) s.CopyTo(d);
                // 保留修改时间：errors-digest / 启动时间线里没时间戳的行要回落到文件 mtime
                try { File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); } catch { }
                return true;
            }
            catch
            {
                // 别留下 0 字节的半成品（比如被占用的 installer.log）
                try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                return false;
            }
        }

        static string Sha256Of(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var f = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
            }
            catch { return "(读取失败)"; }
        }

        static string Sha256Bytes(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        // 文本类扩展名：比较时去掉 \r（CRLF/CR → LF），否则历史安装器打的 CRLF 包和现在 LF 的工作区
        // 会被误报成「文件不一致 / 主题样式缺失」，这类假警报会毁掉 S3 结论的可信度。
        static readonly HashSet<string> TextFileExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".css", ".js", ".mjs", ".cjs", ".json", ".md", ".markdown", ".txt", ".html", ".htm",
            ".svg", ".map", ".xml", ".yml", ".yaml", ".ts", ".tsx", ".jsx", ".vue",
            ".less", ".scss", ".sass", ".styl", ".csv", ".ini", ".cfg", ".toml", ".ps1", ".sh", ".bat", ".cmd"
        };

        static bool IsTextRelativePath(string rel)
        {
            try
            {
                string ext = Path.GetExtension(rel);
                return ext.Length > 0 && TextFileExts.Contains(ext);
            }
            catch { return false; }
        }

        // 去掉所有 \r（0x0D）。UTF-8 文本里 0x0D 只会是换行符的一部分，不会出现在多字节字符中间。
        static byte[] StripCr(byte[] data)
        {
            int n = 0;
            for (int i = 0; i < data.Length; i++) if (data[i] != 13) n++;
            if (n == data.Length) return data;
            var outBuf = new byte[n];
            int j = 0;
            for (int i = 0; i < data.Length; i++) if (data[i] != 13) outBuf[j++] = data[i];
            return outBuf;
        }

        static byte[] ReadAllBytesShared(string path, int maxBytes)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > maxBytes) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        static long ResourceLength(string name)
        {
            try { using (var s = Res(name)) return s.Length; }
            catch { return -1; }
        }

        // 补丁文件的中性描述：大小相同但哈希不同，通常是「较早的安装器构建」（例如 winmm 的 PE 时间戳
        // 每次重编译都不同），不影响功能；只有大小也不同才算被覆盖/破坏。
        static string PatchCompareText(string name, string path, long fileLength)
        {
            try
            {
                if (SameAsRes(name, path)) return "是（与安装器内置一致）";
                long builtinLen = ResourceLength(name);
                if (builtinLen > 0 && fileLength == builtinLen)
                    return "否（与内置构建不同：大小相同，通常只是 PE 时间戳差异，不影响功能；重跑安装器可对齐）";
                return "否（大小也不同：可能被 QQ 更新或其它安装器覆盖，重装安装器可修复）";
            }
            catch { return "未知"; }
        }

        static string PatchFlag(string name, string path)
        {
            long len = -1;
            try { if (File.Exists(path)) len = new FileInfo(path).Length; } catch { }
            return PatchCompareText(name, path, len);
        }

        // ================= 诊断包：采集实现（1.0.9） =================
        // 约定：每节都套 Section(...) 的 try/catch，单项失败只写「采集失败: ...」，绝不中断整包。
        // 所有日志用 FileShare.ReadWrite|Delete 读；凡是内容敏感的，只列元数据。

        static StringBuilder Section(string title, Action<StringBuilder> body)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("---------- " + title + " ----------");
            try { body(sb); }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
            return sb;
        }

        static void BuildSections(List<StringBuilder> sections, DiagContext ctx)
        {
            sections.Add(Section("环境", delegate (StringBuilder sb) { EnvSection(sb, ctx); }));
            sections.Add(Section("已安装插件（版本 / 是否被禁用 / 文件是否完整）", delegate (StringBuilder sb) { PluginsSection(sb, ctx); }));
            sections.Add(Section("LiteLoader 配置与插件数据", delegate (StringBuilder sb) { LlConfigSection(sb, ctx); }));
            sections.Add(Section("QQ 应用打补丁状态", delegate (StringBuilder sb) { QqPatchSection(sb, ctx); }));
            sections.Add(Section("LiteLoader 本体", delegate (StringBuilder sb) { LiteLoaderBodySection(sb, ctx); }));
            sections.Add(Section("显示器 / 显卡 / 会话（壁纸与渲染异常）", delegate (StringBuilder sb) { DisplaySection(sb, ctx); }));
            sections.Add(Section("QQ 窗口与对话框（启动报错取证）", delegate (StringBuilder sb) { WindowSection(sb, ctx); }));
            sections.Add(Section("QQ 本地存储（壁纸设置所在）", delegate (StringBuilder sb) { LocalStorageSection(sb, ctx); }));
            sections.Add(Section("最近启动时间线", delegate (StringBuilder sb) { TimelineSection(sb, ctx); }));
            sections.Add(Section("QQ 安装", delegate (StringBuilder sb) { QqInstallSection(sb, ctx); }));
            sections.Add(Section("QQ 进程（多个主进程会抢同一份聊天记录库）", delegate (StringBuilder sb) { ProcSection(sb, ctx); }));
            sections.Add(Section("聊天记录目录（只列元数据，不含内容）", delegate (StringBuilder sb) { ChatDataSection(sb, ctx); }));
            sections.Add(Section("磁盘", delegate (StringBuilder sb) { DiskSection(sb); }));
            sections.Add(Section("受控文件夹访问（会拦被替换过的 QQ.exe 写“文档”）", delegate (StringBuilder sb) { CfaSection(sb, ctx); }));
            sections.Add(Section("Defender 拦截事件（1123/1124/1127/1128）", delegate (StringBuilder sb) { DefenderEventsSection(sb); }));
            sections.Add(Section("最近 3 天 Application 错误事件", delegate (StringBuilder sb) { AppErrorEventsSection(sb); }));
            sections.Add(Section("安装器日志（尾部 60 行）", delegate (StringBuilder sb) { InstallerLogSection(sb); }));
        }

        // 自动体检放在文件最顶部，但它依赖各节采集到的结论，所以在全部节构建完之后才拼装
        static void WriteEnvInfo(StringBuilder info, DiagContext ctx, List<StringBuilder> sections, string capNote)
        {
            info.AppendLine();
            info.AppendLine("---------- 自动体检（先看这里） ----------");
            info.AppendLine("  标记: [OK]=正常    [!]=需要注意（行尾带 <<== 的就是最可能对应症状的点）    [ ]=无法判断 / 仅供参考");
            int bad = 0, warn = 0, ok = 0, other = 0;
            foreach (var c in ctx.Checks)
            {
                if (c.Level == "bad") bad++;
                else if (c.Level == "warn") warn++;
                else if (c.Level == "ok") ok++;
                else other++;
            }
            info.AppendLine("  结论: 严重 " + bad + " 项，提示 " + warn + " 项，正常 " + ok + " 项，其它 " + other + " 项");
            info.AppendLine();
            info.AppendLine("  常见因果（照着这条修就行）:");
            info.AppendLine("    插件版本落后 / 插件文件与安装器内置不一致 / 缺 assets/*.css");
            info.AppendLine("      → 主题 CSS 或资源加载不全（渲染进程里 data-liuli-theme=failed）");
            info.AppendLine("      → 设置页里的琉璃面板像没套样式（黑底、默认按钮）、主题不生效  → 重跑安装器「一键安装」覆盖修复即可");
            info.AppendLine("  症状对照: S1 启动弹报错 → 「QQ 窗口与对话框」「QQ 应用打补丁状态」+ errors-digest.txt；");
            info.AppendLine("            S2 壁纸不显示 → 「显示器 / 显卡 / 会话」「QQ 本地存储」+ plugin-diag/；");
            info.AppendLine("            S3 主题不生效 → 「已安装插件」里的逐文件对比与 assets/*.css 对照。");
            info.AppendLine();
            foreach (var c in ctx.Checks)
            {
                string mark;
                if (c.Level == "bad" || c.Level == "warn") mark = "[!]";
                else if (c.Level == "ok") mark = "[OK]";
                else mark = "[ ]";
                info.AppendLine("  " + mark + " " + c.Text + (c.Level == "bad" ? "   <<==" : ""));
            }
            if (ctx.Checks.Count == 0)
                info.AppendLine("  [ ] 没有采集到任何体检项（采集可能整体失败，见下面各节的「采集失败」）");
            foreach (var s in sections)
            {
                if (s.Length == 0) continue;
                info.Append(s);
            }
            if (!string.IsNullOrEmpty(capNote))
            {
                info.AppendLine();
                info.AppendLine("---------- 体积上限处理 ----------");
                info.Append(capNote);
            }
        }

        // 全局体检项（依赖日志聚合、插件包收集、窗口枚举的结果）
        static void FinishChecks(DiagContext ctx)
        {
            try
            {
                string winProcsOverride = null;
                try { winProcsOverride = Environment.GetEnvironmentVariable("LIULI_DIAG_WINDOW_PROCS"); } catch { }
                if (ctx.DialogCount > 0)
                    ctx.Add("bad", "当前枚举到 " + ctx.DialogCount + " 个原生对话框（#32770）窗口"
                        + (string.IsNullOrEmpty(winProcsOverride)
                            ? "（进程：QQ）"
                            : "（含 LIULI_DIAG_WINDOW_PROCS 指定的自测进程，不一定是 QQ）")
                        + "，启动报错很可能就是它，正文见「QQ 窗口与对话框」");
                else if (ctx.Procs.Count > 0)
                    ctx.Add("ok", "当前没有原生对话框窗口");
                else
                    ctx.Add("info", "QQ 没在运行，抓不到启动时报的弹窗；下次报错时别关弹窗，直接导诊断包");

                if (ctx.ErrorHits > 0)
                    ctx.Add("warn", "日志里按关键字命中 " + ctx.ErrorHits + " 行可疑内容（见 errors-digest.txt，自动筛选不保证是根因）");
                else
                    ctx.Add("ok", "日志里没有命中已知错误关键字");

                if (ctx.LiteLoaderLogFound)
                {
                    int n = ctx.LiteLoaderLogMainFail + ctx.LiteLoaderLogDialog + ctx.LiteLoaderLogFatal;
                    if (n > 0)
                        ctx.Add("bad", "LiteLoader/liteLoader.log 里有 " + n + " 条启动问题记录（[Main] 加载失败 / installHook / loadAllPlugins 失败 "
                            + ctx.LiteLoaderLogMainFail + "、[dialog] " + ctx.LiteLoaderLogDialog + "、[fatal]/uncaughtException " + ctx.LiteLoaderLogFatal
                            + "）：看 LiteLoader/liteLoader.log 的 [Main]/[dialog]/[fatal] 行"
                            + (ctx.LiteLoaderLogSamples.Count > 0 ? "，例: " + JoinLimited(ctx.LiteLoaderLogSamples, 3) : ""));
                    else
                        ctx.Add("ok", "LiteLoader 启动日志（liteLoader.log）没有 [Main] 加载失败 / [dialog] / [fatal] 记录");
                }
                else
                {
                    ctx.Add("info", "没有找到 LiteLoader/liteLoader.log：本体可能是旧版安装器装的，或装完还没启动过 QQ；重装安装器并启动一次 QQ 后才有");
                }

                if (ctx.PluginDiagTime == null)
                    ctx.Add("info", "没有插件诊断包（可选）：需要壁纸/UI 细节时在 QQ → 设置 → 琉璃 → 诊断 → 导出诊断包，安装器下次会自动带上");
                else
                {
                    double days = (DateTime.Now - ctx.PluginDiagTime.Value).TotalDays;
                    if (days > 7)
                        ctx.Add("warn", "插件诊断包是 " + ((int)days) + " 天前的（" + ctx.PluginDiagTime.Value.ToString("yyyy-MM-dd HH:mm:ss") + "），建议重新导出");
                    else
                        ctx.Add("ok", "已带上插件诊断包：" + ctx.PluginDiagSlug + "（" + ctx.PluginDiagTime.Value.ToString("yyyy-MM-dd HH:mm:ss") + "）");
                }

                        ctx.Add("info", "隐私: QQ 原始日志未脱敏（QQ-log/、QQ-arks/、Tencent-Logs/、QQ-crashpad/ 按原样收集，可能含本机路径/账号 ID/token 片段），公开提交前请确认");
                ctx.Add("info", "隐私: LiteLoader/installer.log、LiteLoader/liteLoader.log、LiteLoader-data/** 的文本日志与 errors-digest.txt 样例行已脱敏（uin/uid/u_xxx/邮箱/sk-/Bearer/长 base64 → ***）");
                if (ctx.RedactionSkipped > 0)
                    ctx.Add("info", "隐私: 有 " + ctx.RedactionSkipped + " 个日志因非文本/非 UTF-8 未脱敏（原样保留，见「采集清单」）");

                ctx.Add("info", "诊断包输出目录: " + ctx.OutputDir);
                if (ctx.CapTruncated)
                    ctx.Add("warn", "诊断包超过上限，已按「日志→插件包→其它」截断（实际上限 " + MbText(ctx.CapBytesLimit) + "，细节见文末「体积上限处理」）");
            }
            catch (Exception ex) { ctx.Add("warn", "生成体检结论失败: " + ex.Message); }
        }

        // ---------- 一次性上下文：进程 / 窗口 / 插件配置 / 内置插件清单 ----------

        static void PrepareContext(DiagContext ctx)
        {
            try { ctx.QQRoot = FindQQ(); }
            catch (Exception ex) { ctx.QQRoot = null; ctx.Add("warn", "查找 QQ 安装目录失败: " + ex.Message); }

            bool wmiOk = false;
            try
            {
                var q = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath, CreationDate FROM Win32_Process WHERE Name='QQ.exe' OR Name='QQEX.exe'");
                foreach (System.Management.ManagementObject o in q.Get())
                {
                    var p = new DiagProc();
                    try { p.Pid = Convert.ToInt32(o["ProcessId"]); } catch { continue; }
                    p.Name = Convert.ToString(o["Name"]);
                    try { p.ParentPid = Convert.ToInt32(o["ParentProcessId"]); } catch { }
                    p.Exe = Convert.ToString(o["ExecutablePath"]);
                    p.Cmd = Convert.ToString(o["CommandLine"]);
                    try
                    {
                        string cd = Convert.ToString(o["CreationDate"]);
                        if (!string.IsNullOrEmpty(cd)) p.Started = System.Management.ManagementDateTimeConverter.ToDateTime(cd);
                    }
                    catch { }
                    wmiOk = true;
                    ctx.Procs.Add(p);
                }
                ctx.ProcessSource = "WMI";
            }
            catch (Exception ex) { ctx.ProcessError = ex.Message; }

            if (!wmiOk)
            {
                ctx.ProcessSource = "降级（Process API，拿不到命令行/父进程）";
                foreach (var nm in new[] { "QQ", "QQEX" })
                {
                    Process[] ps;
                    try { ps = Process.GetProcessesByName(nm); } catch { continue; }
                    foreach (var pr in ps)
                    {
                        var p = new DiagProc();
                        p.Pid = pr.Id;
                        p.Name = nm + ".exe";
                        try { p.Started = pr.StartTime; } catch { }
                        p.IsMain = pr.MainWindowHandle != IntPtr.Zero || string.Equals(nm, "QQ", StringComparison.OrdinalIgnoreCase);
                        ctx.Procs.Add(p);
                    }
                }
            }
            else
            {
                foreach (var p in ctx.Procs)
                {
                    if (p.Started == null)
                    {
                        try { using (var pr = Process.GetProcessById(p.Pid)) p.Started = pr.StartTime; } catch { }
                    }
                    p.IsMain = string.IsNullOrEmpty(p.Cmd) ? HasMainWindow(p.Pid) : p.Cmd.IndexOf("--type=") < 0;
                }
            }
            foreach (var p in ctx.Procs) if (p.IsMain) ctx.MainCount++;

            try { LoadLiteLoaderConfig(ctx); } catch { }
            try { ScanLiteLoaderLog(ctx); } catch { }
            try { ctx.Builtin = LoadBuiltinPlugins(); }
            catch (Exception ex) { ctx.Add("warn", "读取安装器内置插件清单失败: " + ex.Message); }

            // 顶层窗口（顺带统计 #32770 对话框）
            foreach (var p in ctx.Procs)
            {
                try { p.Windows = EnumerateWindowsForPid(p.Pid); }
                catch { p.Windows = new List<DiagWindow>(); }
                foreach (var w in p.Windows)
                    if (w.Visible && w.Cls == "#32770") ctx.DialogCount++;
            }

            // 自测钩子：额外枚举指定进程的窗口（LIULI_DIAG_WINDOW_PROCS=进程名;进程名），不参与 QQ 相关体检
            try
            {
                string extra = Environment.GetEnvironmentVariable("LIULI_DIAG_WINDOW_PROCS");
                if (!string.IsNullOrEmpty(extra))
                {
                    foreach (var raw in extra.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string nm = raw.Trim();
                        if (nm.Length == 0) continue;
                        if (nm.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) nm = nm.Substring(0, nm.Length - 4);
                        Process[] ps;
                        try { ps = Process.GetProcessesByName(nm); } catch { continue; }
                        foreach (var pr in ps)
                        {
                            var p = new DiagProc();
                            p.Pid = pr.Id;
                            p.Name = nm + ".exe";
                            p.IsMain = true;
                            p.Extra = true;
                            p.Cmd = "(LIULI_DIAG_WINDOW_PROCS 自测进程)";
                            try { p.Started = pr.StartTime; } catch { }
                            try { p.Windows = EnumerateWindowsForPid(p.Pid); }
                            catch { p.Windows = new List<DiagWindow>(); }
                            foreach (var w in p.Windows)
                                if (w.Visible && w.Cls == "#32770") ctx.DialogCount++;
                            ctx.Procs.Add(p);
                        }
                    }
                }
            }
            catch { }
        }

        // LiteLoader 主进程 tee 日志（loader/src/main/logger.js 写的 liteLoader.log）：
        // 插件加载失败、LiteLoader 自己弹的错误框正文、未捕获异常都会落在这里，
        // 是「每次启动都弹报错但只打在 QQ 控制台」的唯一落盘证据。
        static void ScanLiteLoaderLog(DiagContext ctx)
        {
            try
            {
                string path = Path.Combine(LLRoot, "liteLoader.log");
                if (!File.Exists(path)) return;
                ctx.LiteLoaderLogFound = true;
                foreach (var ln in ReadTailLines(path, 4000))
                {
                    bool isDialog = ln.IndexOf("[dialog]", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isFatal = ln.IndexOf("[fatal]", StringComparison.OrdinalIgnoreCase) >= 0
                        || (ln.IndexOf("[process]", StringComparison.OrdinalIgnoreCase) >= 0
                            && ln.IndexOf("uncaughtException", StringComparison.OrdinalIgnoreCase) >= 0);
                    bool isMainFail = ln.IndexOf("加载失败", StringComparison.Ordinal) >= 0
                        || ln.IndexOf("loadAllPlugins 失败", StringComparison.Ordinal) >= 0
                        || ln.IndexOf("installHook 失败", StringComparison.Ordinal) >= 0;
                    if (!isDialog && !isFatal && !isMainFail) continue;
                    if (isDialog) ctx.LiteLoaderLogDialog++;
                    else if (isFatal) ctx.LiteLoaderLogFatal++;
                    else ctx.LiteLoaderLogMainFail++;
                    if (ctx.LiteLoaderLogSamples.Count < 5)
                        ctx.LiteLoaderLogSamples.Add(Truncate(SanitizeText(ln.Trim()), 200));
                }
            }
            catch { }
        }

        static void LoadLiteLoaderConfig(DiagContext ctx)
        {            string cfg = Path.Combine(LLRoot, "data", "LiteLoader", "config.json");
            if (!File.Exists(cfg)) return;
            try
            {
                object o = MiniJson.Parse(File.ReadAllText(cfg, Utf8));
                var map = MiniJson.AsObj(o);
                if (map == null) return;
                object en;
                if (map.TryGetValue("enable_plugins", out en))
                    ctx.EnablePlugins = Convert.ToBoolean(en);
                var dis = MiniJson.Arr(map, "disabled_plugins");
                if (dis != null)
                    foreach (var it in dis)
                    {
                        string s = it as string;
                        if (!string.IsNullOrEmpty(s) && !ctx.DisabledPlugins.Contains(s)) ctx.DisabledPlugins.Add(s);
                    }
            }
            catch { }
        }

        // 安装器内置的插件清单：直接从 exe 里的 plugins.zip 读，算每个文件的 SHA256 用于对比
        static Dictionary<string, BuiltinPlugin> LoadBuiltinPlugins()
        {
            var res = new Dictionary<string, BuiltinPlugin>(StringComparer.OrdinalIgnoreCase);
            using (var s = Res("plugins.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var groups = new Dictionary<string, BuiltinPlugin>(StringComparer.OrdinalIgnoreCase);
                var manifestText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var sha = System.Security.Cryptography.SHA256.Create();
                try
                {
                    foreach (var e in zip.Entries)
                    {
                        string full = e.FullName.Replace('\\', '/');
                        if (full.EndsWith("/") || full.Length == 0) continue;
                        int idx = full.IndexOf('/');
                        if (idx <= 0) continue;
                        string dir = full.Substring(0, idx);
                        string rel = full.Substring(idx + 1);
                        if (rel.Length == 0) continue;
                        byte[] buf;
                        using (var es = e.Open())
                        using (var ms = new MemoryStream())
                        {
                            es.CopyTo(ms);
                            buf = ms.ToArray();
                        }
                        string hash = BitConverter.ToString(sha.ComputeHash(buf)).Replace("-", "");
                        BuiltinPlugin bp;
                        if (!groups.TryGetValue(dir, out bp))
                        {
                            bp = new BuiltinPlugin();
                            bp.DirName = dir;
                            groups[dir] = bp;
                        }
                        bp.Sha[rel] = hash;
                        bp.Len[rel] = buf.Length;
                        // 文本文件额外存「去掉 \r 后」的长度/哈希，用于换行符无关的对比
                        if (IsTextRelativePath(rel))
                        {
                            byte[] norm = StripCr(buf);
                            bp.NormLen[rel] = norm.Length;
                            bp.NormSha[rel] = Sha256Bytes(norm);
                        }
                        if (string.Equals(rel, "manifest.json", StringComparison.OrdinalIgnoreCase))
                            manifestText[dir] = Utf8.GetString(buf);
                    }
                }
                finally { sha.Dispose(); }

                foreach (var kv in groups)
                {
                    BuiltinPlugin bp = kv.Value;
                    bp.Slug = kv.Key;
                    string rawMf;
                    if (manifestText.TryGetValue(kv.Key, out rawMf))
                    {
                        try
                        {
                            var map = MiniJson.AsObj(MiniJson.Parse(rawMf));
                            if (map != null)
                            {
                                string slug = MiniJson.Str(map, "slug");
                                if (!string.IsNullOrEmpty(slug)) bp.Slug = slug;
                                bp.Version = MiniJson.Str(map, "version");
                            }
                        }
                        catch { }
                    }
                    res[kv.Key] = bp;
                    if (!string.IsNullOrEmpty(bp.Slug) && !res.ContainsKey(bp.Slug)) res[bp.Slug] = bp;
                }
            }
            return res;
        }

        // ---------- 各节 ----------

        static void EnvSection(StringBuilder sb, DiagContext ctx)
        {
            sb.AppendLine("  时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("  计算机: " + Environment.MachineName + "   用户: " + Environment.UserName);
            sb.AppendLine("  系统: " + Environment.OSVersion.VersionString + "   64位进程: " + Environment.Is64BitProcess);
            sb.AppendLine("  管理员运行: " + IsElevated());
            sb.AppendLine("  .NET: " + Environment.Version);
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                        sb.AppendLine("  Windows: " + k.GetValue("ProductName") + " " + k.GetValue("DisplayVersion")
                            + "  Build " + k.GetValue("CurrentBuild") + "." + k.GetValue("UBR"));
                }
            }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
            try { sb.AppendLine("  安装器版本: " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion); }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
            sb.AppendLine("  文档目录: " + Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            sb.AppendLine("  用户目录: " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            sb.AppendLine("  APPDATA: " + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            sb.AppendLine("  TEMP: " + Path.GetTempPath());
            sb.AppendLine("  LiteLoader 目录: " + LLRoot + (Directory.Exists(LLRoot) ? "" : "  （不存在）"));
            sb.AppendLine("  QQ 安装目录: " + (string.IsNullOrEmpty(ctx.QQRoot) ? "未找到" : ctx.QQRoot));
            sb.AppendLine("  诊断包输出目录: " + (string.IsNullOrEmpty(ctx.OutputDir) ? "(未定)" : ctx.OutputDir) + "（若写包时该目录被占用，实际路径以 installer.log 的「诊断包:」为准）");
            foreach (var n in ctx.OutputNotes) sb.AppendLine("      输出目录说明: " + n);
            sb.AppendLine("  原版备份: " + (Directory.Exists(Path.Combine(BackupRoot, "original")) ? Path.Combine(BackupRoot, "original") : "不存在"));
            foreach (var n in ctx.Notes) sb.AppendLine("  说明: " + n);
        }

        static void PluginsSection(StringBuilder sb, DiagContext ctx)
        {
            string plugins = Path.Combine(LLRoot, "plugins");
            sb.AppendLine("  plugins 路径: " + plugins);
            if (!Directory.Exists(plugins))
            {
                sb.AppendLine("  plugins 目录不存在   <<== 插件没装，或 LiteLoader 没装到这个 profile");
                ctx.Add("bad", "LiteLoader plugins 目录不存在（" + plugins + "）：插件没装，或本体装到了别的 profile");
                return;
            }
            string[] dirs;
            try { dirs = Directory.GetDirectories(plugins); }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); ctx.Add("bad", "读 plugins 目录失败: " + ex.Message); return; }

            var realDirs = new List<string>();
            var residual = new List<string>();
            foreach (var d in dirs)
            {
                string n = Path.GetFileName(d);
                if (Regex.IsMatch(n, "_\\d{10,}$")) residual.Add(d);
                else realDirs.Add(d);
            }
            sb.AppendLine("  plugins 下共 " + dirs.Length + " 个目录，其中插件 " + realDirs.Count + " 个（残留 " + residual.Count + " 个）");
            sb.AppendLine("  disabled_plugins: " + (ctx.DisabledPlugins.Count == 0 ? "(空，全部启用)" : string.Join(", ", ctx.DisabledPlugins.ToArray()))
                + (ctx.EnablePlugins ? "" : "   <<== enable_plugins=false，所有插件都不会加载"));
            if (!ctx.EnablePlugins)
                ctx.Add("bad", "LiteLoader 配置 enable_plugins=false，所有插件都不会加载");

            foreach (var r in residual)
            {
                string n = Path.GetFileName(r);
                sb.AppendLine("  残留目录: " + n + "   <<== 上次安装/删除插件时留下的副本，可手动删除");
                ctx.Add("warn", "发现插件残留目录 " + n + "（通常是插件更新/删除失败留下的），建议手动删掉后重启 QQ");
            }
            foreach (var d in realDirs)
            {
                try { PluginDetail(sb, ctx, d); }
                catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
            }

            // S3（界面渲染异常 / 主题不生效）单列一条结论：文件不完整/内容与内置不一致时，主题 CSS 一定加载不全
            if (ctx.ThemeFileIssues.Count > 0)
            {
                sb.AppendLine("  插件文件完整性: 有 " + ctx.ThemeFileIssues.Count + " 处问题   <<== 见上面每个插件的逐文件对比");
                if (ctx.NewlineOnlyCount > 0)
                    sb.AppendLine("  另：" + ctx.NewlineOnlyCount + " 个文件内容一致（仅换行符不同），已排除，不计入问题");
                ctx.Add("bad", "插件文件不完整或与内置内容不一致（可能造成主题不生效、UI 渲染异常）："
                    + JoinLimited(ctx.ThemeFileIssues, 8) + "  → 重跑安装器「一键安装」覆盖修复");
            }
            else if (ctx.BuiltinPluginsCompared > 0)
            {
                sb.AppendLine("  插件文件完整性: 与安装器内置清单一致（" + ctx.ComparedFileCount + " 个文件内容一致"
                    + (ctx.NewlineOnlyCount > 0 ? "，其中 " + ctx.NewlineOnlyCount + " 个仅换行符不同" : "") + "）");
                ctx.Add("ok", "已装插件与安装器内置逐文件一致（" + ctx.ComparedFileCount + " 个文件内容一致"
                    + (ctx.NewlineOnlyCount > 0 ? "，其中 " + ctx.NewlineOnlyCount + " 个仅换行符不同" : "") + "）");
            }
            else
            {
                sb.AppendLine("  插件文件完整性: 没有可比对的安装器内置插件");
                ctx.Add("info", "没有可比对的安装器内置插件（当前只有第三方插件），跳过逐文件完整性结论");
            }
            try
            {
                foreach (var f in Directory.GetFiles(plugins))
                    sb.AppendLine("  plugins 下的散落文件: " + Path.GetFileName(f));
            }
            catch { }
        }

        static void PluginDetail(StringBuilder sb, DiagContext ctx, string dir)
        {
            string name = Path.GetFileName(dir);
            sb.AppendLine("  [" + name + "]");
            sb.AppendLine("      目录: " + dir);
            try { sb.AppendLine("      修改时间: " + Directory.GetLastWriteTime(dir).ToString("yyyy-MM-dd HH:mm:ss")); } catch { }

            Dictionary<string, object> manifest = null;
            bool bom = false;
            string manifestErr = null;
            string mf = Path.Combine(dir, "manifest.json");
            bool hasManifest = File.Exists(mf);
            if (!hasManifest)
            {
                sb.AppendLine("      缺 manifest.json   <<== 这个插件不会被 LiteLoader 加载");
                ctx.Add("bad", "插件 " + name + " 缺 manifest.json，不会被加载");
            }
            else
            {
                bom = HasBom(mf);
                try
                {
                    object o = MiniJson.Parse(File.ReadAllText(mf, Utf8));
                    manifest = MiniJson.AsObj(o);
                    if (manifest == null) manifestErr = "顶层不是 JSON 对象";
                }
                catch (Exception ex) { manifestErr = ex.Message; }
                long len = 0;
                try { len = new FileInfo(mf).Length; } catch { }
                string mtime = "";
                try { mtime = File.GetLastWriteTime(mf).ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                sb.AppendLine("      manifest.json: " + len + " 字节  修改 " + mtime
                    + "  UTF-8 BOM=" + (bom ? "有   <<== LiteLoader 用 JSON.parse 会直接失败" : "无")
                    + "  JSON=" + (manifestErr == null ? "正常" : "解析失败: " + manifestErr + "   <<=="));
                if (bom) ctx.Add("bad", "插件 " + name + " 的 manifest.json 带 UTF-8 BOM，LiteLoader 解析会失败，插件加载不了");
                if (manifestErr != null) ctx.Add("bad", "插件 " + name + " 的 manifest.json 解析失败: " + manifestErr);
                try
                {
                    string raw = Regex.Replace(File.ReadAllText(mf, Utf8), @"\s+", " ");
                    sb.AppendLine("      内容(截断): " + Truncate(raw, 400));
                }
                catch (Exception ex) { sb.AppendLine("      读 manifest 失败: " + ex.Message); }
            }

            string slug = name, ver = "";
            if (manifest != null)
            {
                string s = MiniJson.Str(manifest, "slug");
                if (!string.IsNullOrEmpty(s)) slug = s;
                ver = MiniJson.Str(manifest, "version");
            }
            bool disabled = ctx.DisabledPlugins.Contains(slug);
            sb.AppendLine("      slug=" + slug + "  version=" + (ver.Length == 0 ? "(未写)" : ver)
                + "  禁用=" + (disabled ? "是   <<== 已被 LiteLoader 禁用（disabled_plugins）" : "否"));
            if (disabled) ctx.Add("warn", "插件 " + slug + " 被 LiteLoader 禁用（disabled_plugins）");
            if (ver.Length == 0) ctx.Add("warn", "插件 " + slug + " 的 manifest 没写 version");

            BuiltinPlugin bp = null;
            if (ctx.Builtin != null) ctx.Builtin.TryGetValue(name, out bp);
            if (bp == null && ctx.Builtin != null && manifest != null) ctx.Builtin.TryGetValue(slug, out bp);
            if (bp != null)
            {
                sb.AppendLine("      安装器内置版本: " + (bp.Version.Length == 0 ? "(未知)" : bp.Version)
                    + (string.Equals(bp.Version, ver, StringComparison.OrdinalIgnoreCase) ? "   （与已装版本一致）" : "   <<== 与已装版本不同"));
                int cmp = CompareVersion(ver, bp.Version);
                if (cmp < 0) ctx.Add("warn", "插件 " + slug + " 已装版本 " + ver + " 落后于安装器内置 " + bp.Version
                    + "：旧版缺新样式/新资源，可能导致「设置页像没套样式、主题不生效」；重跑安装器「一键安装」覆盖修复");
                else if (cmp > 0) ctx.Add("info", "插件 " + slug + " 已装版本 " + ver + " 比安装器内置 " + bp.Version + " 新（安装器可能旧了）");
            }
            else
            {
                sb.AppendLine("      安装器内置版本: 无（不是安装器带的插件，跳过版本/哈希对比）");
            }

            // injects 文件是否都在
            if (manifest != null)
            {
                var inj = MiniJson.AsObj(MiniJson.Get(manifest, "injects"));
                if (inj != null)
                {
                    foreach (var key in new[] { "main", "preload", "renderer" })
                    {
                        string rel = MiniJson.Str(inj, key);
                        if (rel.Length == 0) { sb.AppendLine("      injects." + key + ": (manifest 没写)"); continue; }
                        string full = Path.GetFullPath(Path.Combine(dir, rel.Replace('/', '\\')));
                        if (File.Exists(full))
                        {
                            long len = 0;
                            try { len = new FileInfo(full).Length; } catch { }
                            sb.AppendLine("      injects." + key + ": " + rel + "  存在 " + len + " 字节");
                        }
                        else
                        {
                            sb.AppendLine("      injects." + key + ": " + rel + "   <<== 文件不存在，插件加载会失败");
                            ctx.Add("bad", "插件 " + slug + " 的注入文件不存在: " + rel + "（插件无法正常加载）");
                        }
                    }
                }
                else sb.AppendLine("      injects: (manifest 没写 injects)");
            }

            // 目录内文件核对（只对比内置插件；不打包源码，只列哈希/大小）
            var localRel = new List<string>();
            try { CollectRelFiles(dir, dir, localRel, 0); } catch { }
            long total = 0;
            foreach (var rel in localRel) { try { total += new FileInfo(Path.Combine(dir, rel.Replace('/', '\\'))).Length; } catch { } }
            sb.AppendLine("      本地文件: " + localRel.Count + " 个，共 " + SizeText(total));
            // 壁纸资源：安装器版故意不发布默认壁纸（插件 .gitignore 排除了 wallpaper.css/jpg），
            // 渲染进程把它当可选资源 —— 缺失属正常，不能当故障提示，否则会误导「换壁纸不显示」的判断。
            string lowerName = (slug + "/" + name).ToLowerInvariant();
            bool wallpaperPresent = false;
            if (lowerName.IndexOf("liuli") >= 0)
            {
                foreach (var wrel in new[] { "assets/wallpaper.css", "assets/wallpaper.jpg", "assets/wallpaper.png" })
                {
                    string wp = Path.Combine(dir, wrel.Replace('/', '\\'));
                    if (File.Exists(wp))
                    {
                        wallpaperPresent = true;
                        sb.AppendLine("      壁纸资源: " + wrel + " 存在 " + SizeText(new FileInfo(wp).Length) + "（用户自己放的默认壁纸）");
                    }
                    else
                        sb.AppendLine("      壁纸资源: " + wrel + " 不存在（安装器版故意不带，属正常）");
                }
                if (!wallpaperPresent)
                    ctx.Add("info", "没有默认壁纸资源（assets/wallpaper.css 不存在）：安装器版故意不带，属正常；用户在设置里选图后壁纸才显示");
            }
            if (bp != null)
            {
                ctx.BuiltinPluginsCompared++;
                var localSet = new HashSet<string>(localRel, StringComparer.OrdinalIgnoreCase);
                int sameCount = 0, newlineOnly = 0, sizeDiff = 0, shaDiff = 0, missing = 0, extra = 0, skipped = 0;
                var sizeList = new List<string>();
                var shaList = new List<string>();
                var missingList = new List<string>();
                var extraList = new List<string>();
                var issueList = new List<string>();
                foreach (var rel in localRel)
                {
                    string full = Path.Combine(dir, rel.Replace('/', '\\'));
                    long rawLen = 0;
                    try { rawLen = new FileInfo(full).Length; }
                    catch { skipped++; continue; }
                    long builtinRawLen;
                    if (!bp.Len.TryGetValue(rel, out builtinRawLen))
                    {
                        extra++;
                        if (extraList.Count < 60) extraList.Add(rel);
                        continue;
                    }

                    // 文本类：按「去掉 \r 后」比较（历史安装器包是 CRLF，现在是 LF，不能算不一致）
                    bool isText = IsTextRelativePath(rel);
                    long localCmpLen = rawLen, builtinCmpLen = builtinRawLen;
                    string localCmpSha = null, builtinCmpSha = null;
                    if (isText)
                    {
                        byte[] localBytes = ReadAllBytesShared(full, 32 * 1024 * 1024);
                        long bnl;
                        string bns;
                        if (localBytes != null
                            && bp.NormLen.TryGetValue(rel, out bnl) && bp.NormSha.TryGetValue(rel, out bns))
                        {
                            byte[] localNorm = StripCr(localBytes);
                            localCmpLen = localNorm.Length;
                            localCmpSha = Sha256Bytes(localNorm);
                            builtinCmpLen = bnl;
                            builtinCmpSha = bns;
                        }
                        else
                        {
                            // 读不了或老清单没有归一化数据：退回原始字节比较
                            isText = false;
                        }
                    }
                    if (!isText)
                    {
                        localCmpSha = Sha256Of(full);
                        string bsha;
                        bp.Sha.TryGetValue(rel, out bsha);
                        builtinCmpSha = bsha;
                    }

                    if (builtinCmpLen != localCmpLen)
                    {
                        sizeDiff++;
                        string detail = "（" + (isText ? "去 CR 后 " : "原始 ") + localCmpLen + " 字节 ≠ 内置 " + builtinCmpLen + " 字节";
                        if (isText) detail += "；原始 本地 " + rawLen + " / 内置 " + builtinRawLen + " 字节";
                        detail += "）";
                        if (sizeList.Count < 60) sizeList.Add(rel + detail);
                        if (IsRuntimePluginFile(rel) && issueList.Count < 60) issueList.Add(rel + "（大小不同）");
                        continue;
                    }
                    if (builtinCmpSha != null && string.Equals(localCmpSha, builtinCmpSha, StringComparison.OrdinalIgnoreCase))
                    {
                        // 归一化内容一致；原始长度不同 = 只是换行符差异，不算问题
                        if (isText && rawLen != builtinRawLen) newlineOnly++;
                        else sameCount++;
                        continue;
                    }
                    shaDiff++;
                    if (shaList.Count < 60) shaList.Add(rel + (isText ? "（去 CR 后 SHA256 与内置不一致）" : "（SHA256 与内置不一致）"));
                    if (IsRuntimePluginFile(rel) && issueList.Count < 60) issueList.Add(rel + "（内容不一致）");
                }
                foreach (var kv in bp.Len)
                    if (!localSet.Contains(kv.Key))
                    {
                        missing++;
                        if (missingList.Count < 60) missingList.Add(kv.Key);
                        if (issueList.Count < 60) issueList.Add(kv.Key + "（缺失）");
                    }
                ctx.ComparedFileCount += sameCount;
                ctx.NewlineOnlyCount += newlineOnly;

                sb.AppendLine("      与安装器内置逐文件对比（内置 plugins.zip 条目 " + bp.Len.Count + " 个，本地 " + localRel.Count + " 个）:");
                sb.AppendLine("        一致 " + sameCount + " 个；内容一致（仅换行符不同）" + newlineOnly + " 个；大小不同 " + sizeDiff
                    + " 个；SHA256 不一致 " + shaDiff + " 个；缺失 " + missing + " 个；未在内置清单里 " + extra + " 个");
                sb.AppendLine("        内容一致（仅换行符不同）" + newlineOnly + " 个（CRLF/LF 差异不计入问题，不影响主题）");
                sb.AppendLine("        口径: 文本类扩展名（.css/.js/.mjs/.cjs/.json/.md/.svg/.map 等）按「去掉 \\r 后」比较；二进制按原始字节比较。");
                if (sizeList.Count > 0) { sb.AppendLine("        大小不同:"); foreach (var x in sizeList) sb.AppendLine("          - " + x); }
                if (shaList.Count > 0) { sb.AppendLine("        与内置不一致（SHA256）:"); foreach (var x in shaList) sb.AppendLine("          - " + x); }
                if (missingList.Count > 0) { sb.AppendLine("        缺失（内置有、本地没有）:"); foreach (var x in missingList) sb.AppendLine("          - " + x); }
                if (extraList.Count > 0) { sb.AppendLine("        未在内置清单里（旧文件残留 / 本地多出）:"); foreach (var x in extraList) sb.AppendLine("          - " + x); }
                if (skipped > 0) sb.AppendLine("        跳过（读不了）: " + skipped + " 个");

                // 重点：assets 下的 CSS —— 主题样式全靠它们，缺一个就可能整块变成原生控件
                sb.AppendLine("      重点文件 assets/*.css（缺失/内容不一致会直接造成主题不生效、设置页像没套样式）:");
                var cssRels = new List<string>();
                foreach (var k in bp.Len.Keys)
                {
                    if (k.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                        && k.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) cssRels.Add(k);
                }
                cssRels.Sort(StringComparer.OrdinalIgnoreCase);
                int cssBad = 0, cssNewlineOnly = 0;
                foreach (var rel in cssRels)
                {
                    string full = Path.Combine(dir, rel.Replace('/', '\\'));
                    string status;
                    if (!File.Exists(full)) { status = "缺失   <<=="; cssBad++; }
                    else
                    {
                        long rawLen = 0;
                        try { rawLen = new FileInfo(full).Length; }
                        catch { rawLen = -1; }
                        long builtinRawLen = bp.Len[rel];
                        long bnl = 0;
                        string bns = null;
                        long normLenTmp = 0;
                        string normShaTmp = null;
                        bool hasNorm = bp.NormLen.TryGetValue(rel, out normLenTmp) && bp.NormSha.TryGetValue(rel, out normShaTmp);
                        if (hasNorm) { bnl = normLenTmp; bns = normShaTmp; }
                        long cmpLen;
                        string localSha;
                        byte[] localBytes = ReadAllBytesShared(full, 32 * 1024 * 1024);
                        if (hasNorm && localBytes != null)
                        {
                            byte[] norm = StripCr(localBytes);
                            cmpLen = norm.Length;
                            localSha = Sha256Bytes(norm);
                        }
                        else
                        {
                            cmpLen = rawLen;
                            localSha = Sha256Of(full);
                            if (!hasNorm)
                            {
                                bnl = builtinRawLen;
                                bp.Sha.TryGetValue(rel, out bns);
                            }
                        }
                        bool same = bns != null && string.Equals(localSha, bns, StringComparison.OrdinalIgnoreCase);
                        if (cmpLen != bnl) { status = "大小不同（去 CR 后 本地 " + cmpLen + " ≠ 内置 " + bnl + "）   <<=="; cssBad++; }
                        else if (same)
                        {
                            if (rawLen != builtinRawLen) { status = "一致（仅换行符不同）"; cssNewlineOnly++; }
                            else status = "一致";
                        }
                        else { status = "去 CR 后 SHA256 与内置不一致   <<=="; cssBad++; }
                    }
                    sb.AppendLine("        " + rel.PadRight(34) + status);
                }
                if (cssRels.Count == 0) sb.AppendLine("        (内置清单里没有 assets/*.css)");
                else if (cssNewlineOnly > 0) sb.AppendLine("        （其中 " + cssNewlineOnly + " 个仅换行符不同，不计为问题）");

                // 同一件事（文件不完整/内容不一致）只在自动体检里出一条总结论（全局那一条），
                // 插件内部细节已经在上面逐文件列出，避免三行重复。
                if (missing + sizeDiff + shaDiff + cssBad > 0)
                {
                    foreach (var x in issueList) ctx.ThemeFileIssues.Add(slug + "/" + x);
                }
            }

            // 关键文件的哈希，方便人肉对比（文本按去 CR 后比较，换行符不同标注出来）
            if (manifest != null && bp != null)
            {
                var inj = MiniJson.AsObj(MiniJson.Get(manifest, "injects"));
                if (inj != null)
                {
                    foreach (var key in new[] { "main", "preload", "renderer" })
                    {
                        string rel = MiniJson.Str(inj, key);
                        if (rel.Length == 0) continue;
                        string norm = rel.Replace('\\', '/').TrimStart('.', '/');
                        string full = Path.Combine(dir, norm.Replace('/', '\\'));
                        if (!File.Exists(full)) continue;
                        long len = 0;
                        string mtime = "";
                        try { len = new FileInfo(full).Length; mtime = File.GetLastWriteTime(full).ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                        string lsha = Sha256Of(full);
                        bool newlineOnly;
                        bool same = CompareWithBuiltin(bp, norm, full, out newlineOnly);
                        sb.AppendLine("      " + key + " 文件: " + norm + "  " + len + " 字节  修改 " + mtime);
                        sb.AppendLine("          SHA256 " + lsha + "  与内置一致=" + (same ? "是" + (newlineOnly ? "（仅换行符不同）" : "") : "否"));
                    }
                }
            }
        }

        // 与内置条目比较：文本按去 CR 后比较，其它按原始字节比较；newlineOnly 表示仅换行符差异。
        static bool CompareWithBuiltin(BuiltinPlugin bp, string rel, string full, out bool newlineOnly)
        {
            newlineOnly = false;
            try
            {
                long rawLen = new FileInfo(full).Length;
                long builtinRawLen;
                if (!bp.Len.TryGetValue(rel, out builtinRawLen)) return false;
                if (IsTextRelativePath(rel))
                {
                    byte[] bytes = ReadAllBytesShared(full, 32 * 1024 * 1024);
                    long bnl;
                    string bns;
                    if (bytes != null && bp.NormLen.TryGetValue(rel, out bnl) && bp.NormSha.TryGetValue(rel, out bns))
                    {
                        byte[] norm = StripCr(bytes);
                        if (norm.Length != bnl) return false;
                        if (!string.Equals(Sha256Bytes(norm), bns, StringComparison.OrdinalIgnoreCase)) return false;
                        newlineOnly = bytes.Length != builtinRawLen;
                        return true;
                    }
                }
                if (rawLen != builtinRawLen) return false;
                string bsha;
                bp.Sha.TryGetValue(rel, out bsha);
                return bsha != null && string.Equals(Sha256Of(full), bsha, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static void CollectRelFiles(string root, string dir, List<string> outList, int depth)
        {
            if (depth > 6 || outList.Count > 4000) return;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    string rel = f.Substring(root.Length).TrimStart('\\').Replace('\\', '/');
                    outList.Add(rel);
                }
            }
            catch { }
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(d);
                    if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                    CollectRelFiles(root, d, outList, depth + 1);
                }
            }
            catch { }
        }

        static void LlConfigSection(StringBuilder sb, DiagContext ctx)
        {
            string data = Path.Combine(LLRoot, "data");
            string cfg = Path.Combine(data, "LiteLoader", "config.json");
            sb.AppendLine("  LiteLoader 配置: " + cfg);
            if (!File.Exists(cfg))
            {
                sb.AppendLine("    不存在。LiteLoader 会使用默认值:");
                sb.AppendLine("      enable_plugins = true");
                sb.AppendLine("      disabled_plugins = [] （全部插件启用）");
                sb.AppendLine("      deleting_plugins = {}");
                sb.AppendLine("      installing_plugins = {}");
            }
            else
            {
                try
                {
                    long len = new FileInfo(cfg).Length;
                    string mtime = File.GetLastWriteTime(cfg).ToString("yyyy-MM-dd HH:mm:ss");
                    bool bom = HasBom(cfg);
                    Dictionary<string, object> map = null;
                    string err = null;
                    try { map = MiniJson.AsObj(MiniJson.Parse(File.ReadAllText(cfg, Utf8))); }
                    catch (Exception ex) { err = ex.Message; }
                    sb.AppendLine("    大小 " + len + " 字节  修改 " + mtime + "  BOM=" + (bom ? "有" : "无")
                        + "  JSON=" + (err == null ? "正常" : "解析失败: " + err));
                    if (map != null)
                    {
                        sb.AppendLine("    enable_plugins = " + MiniJson.BoolText(map, "enable_plugins"));
                        sb.AppendLine("    disabled_plugins = " + JoinJsonArray(map, "disabled_plugins", 40));
                        sb.AppendLine("    deleting_plugins = " + JoinJsonObjectKeys(map, "deleting_plugins", 20));
                        sb.AppendLine("    installing_plugins = " + JoinJsonObjectKeys(map, "installing_plugins", 20));
                    }
                    else
                    {
                        string raw = "";
                        try { raw = File.ReadAllText(cfg, Utf8); } catch { }
                        sb.AppendLine("    内容(截断): " + Truncate(SanitizeText(Regex.Replace(raw, @"\s+", " ")), 600));
                    }
                    if (err != null) ctx.Add("warn", "LiteLoader 配置 config.json 解析失败（LiteLoader 会退回默认配置）: " + err);
                }
                catch (Exception ex) { sb.AppendLine("    采集失败: " + ex.Message); }
            }

            sb.AppendLine("  插件数据目录: " + data);
            if (!Directory.Exists(data))
            {
                sb.AppendLine("    不存在（插件没写过数据，或 LiteLoader 没装到这个 profile）");
                return;
            }
            string[] subs;
            try { subs = Directory.GetDirectories(data); }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); return; }
            foreach (var d in subs)
            {
                string slug = Path.GetFileName(d);
                try
                {
                    var files = new List<FileInfo>();
                    try { foreach (var f in SafeFiles(d)) files.Add(f); } catch { }
                    long total = 0;
                    foreach (var f in files) total += f.Length;
                    sb.AppendLine("    [" + slug + "] " + files.Count + " 个文件，共 " + SizeText(total));
                    string pcfg = Path.Combine(d, "config.json");
                    if (File.Exists(pcfg))
                    {
                        try
                        {
                            string raw = File.ReadAllText(pcfg, Utf8);
                            int cfgLen = raw.Length;
                            bool cut = cfgLen > 4096;
                            if (cut) raw = raw.Substring(0, 4096);
                            sb.AppendLine("        config.json: " + cfgLen + " 字符  修改 " + File.GetLastWriteTime(pcfg).ToString("yyyy-MM-dd HH:mm:ss") + (cut ? "（已截断到 4KB）" : ""));
                            sb.AppendLine("        " + SanitizeText(Regex.Replace(raw, @"\s+", " ")));
                        }
                        catch (Exception ex) { sb.AppendLine("        config.json 读取失败: " + ex.Message); }
                    }
                    // 日志尾部（每个 data 目录取最新的 .log/.txt）
                    var logs = new List<FileInfo>();
                    foreach (var f in files)
                        if (string.Equals(f.Extension, ".log", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(f.Extension, ".txt", StringComparison.OrdinalIgnoreCase))
                            logs.Add(f);
                    logs.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                    int logShown = 0;
                    foreach (var lf in logs)
                    {
                        if (logShown >= 2) break;
                        sb.AppendLine("        日志 " + lf.Name + " (" + lf.Length + " 字节，修改 " + lf.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + ") 尾部 80 行:");
                        var lines = ReadTailLines(lf.FullName, 80);
                        if (lines.Count == 0) sb.AppendLine("            (空)");
                        foreach (var ln in lines) sb.AppendLine("            " + Truncate(SanitizeText(ln), 400));
                        logShown++;
                    }
                    // diag 导出的诊断包
                    string diag = Path.Combine(d, "diag");
                    if (Directory.Exists(diag))
                    {
                        try
                        {
                            var zips = new List<FileInfo>();
                            foreach (var z in new DirectoryInfo(diag).GetFiles("*.zip")) zips.Add(z);
                            zips.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                            sb.AppendLine("        diag/ 导出包: " + zips.Count + " 个");
                            int n = 0;
                            foreach (var z in zips)
                            {
                                if (n >= 10) break;
                                sb.AppendLine("            " + z.Name + "  " + (z.Length / 1024) + " KB  修改 " + z.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                                n++;
                            }
                        }
                        catch (Exception ex) { sb.AppendLine("        diag/ 列目录失败: " + ex.Message); }
                    }
                }
                catch (Exception ex) { sb.AppendLine("    [" + slug + "] 采集失败: " + ex.Message); }
            }
        }

        static void QqPatchSection(StringBuilder sb, DiagContext ctx)
        {
            string root = ctx.QQRoot;
            if (string.IsNullOrEmpty(root))
            {
                sb.AppendLine("  未找到 QQ 安装目录（注册表卸载项和默认路径都没有）");
                return;
            }
            sb.AppendLine("  安装目录: " + root + "  （存在=" + (Directory.Exists(root) ? "是" : "否") + "）");
            string exe = Path.Combine(root, "QQ.exe");
            if (File.Exists(exe))
            {
                string fv = "", pv = "";
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(exe);
                    if (vi != null)
                    {
                        fv = vi.FileVersion == null ? "" : vi.FileVersion;
                        pv = vi.ProductVersion == null ? "" : vi.ProductVersion;
                    }
                }
                catch { }
                sb.AppendLine("  QQ.exe 文件版本: " + (fv.Length == 0 ? "(读不到)" : fv) + "  产品版本: " + (pv.Length == 0 ? "(读不到)" : pv));
                try { sb.AppendLine("  QQ.exe 修改时间: " + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss")); } catch { }
            }
            else
            {
                sb.AppendLine("  QQ.exe 不存在   <<==");
                ctx.Add("bad", "QQ 安装目录里没有 QQ.exe（" + root + "），安装目录可能不对或安装被破坏");
            }

            string versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions))
            {
                sb.AppendLine("  versions 目录不存在   <<== 安装不完整？");
                ctx.Add("bad", "QQ 的 versions 目录不存在（" + versions + "）");
                return;
            }
            foreach (var cfgName in new[] { "config.json", "setting.json", "channel.json" })
            {
                string p = Path.Combine(versions, cfgName);
                if (!File.Exists(p)) { sb.AppendLine("  versions\\" + cfgName + ": 不存在"); continue; }
                try
                {
                    string raw = Regex.Replace(File.ReadAllText(p, Utf8), @"\s+", " ");
                    sb.AppendLine("  versions\\" + cfgName + " (" + new FileInfo(p).Length + " 字节，修改 "
                        + File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss") + "): " + Truncate(SanitizeText(raw), 500));
                }
                catch (Exception ex) { sb.AppendLine("  读 versions\\" + cfgName + " 失败: " + ex.Message); }
            }

            string[] vdirs;
            try { vdirs = Directory.GetDirectories(versions); }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); return; }
            sb.AppendLine("  versions 下共 " + vdirs.Length + " 个版本目录");
            int noLauncher = 0, patched = 0;
            foreach (var v in vdirs)
            {
                string vname = Path.GetFileName(v);
                sb.AppendLine("  [" + vname + "]");
                try { sb.AppendLine("      目录修改时间: " + Directory.GetLastWriteTime(v).ToString("yyyy-MM-dd HH:mm:ss")); } catch { }
                string app = Path.Combine(v, "resources", "app");
                if (!Directory.Exists(app)) { sb.AppendLine("      resources\\app 不存在，跳过"); continue; }
                string pkg = Path.Combine(app, "package.json");
                bool mainOk = false, launcherOk = false;
                if (!File.Exists(pkg))
                {
                    sb.AppendLine("      package.json 不存在   <<==");
                    ctx.Add("bad", "版本 " + vname + " 的 resources\\app\\package.json 不存在（QQ 起不来或起的是别的东西）");
                }
                else
                {
                    bool bom = HasBom(pkg);
                    string main = null;
                    string err = null;
                    try
                    {
                        var map = MiniJson.AsObj(MiniJson.Parse(File.ReadAllText(pkg, Utf8)));
                        if (map == null) err = "顶层不是 JSON 对象";
                        else main = MiniJson.Str(map, "main");
                    }
                    catch (Exception ex) { err = ex.Message; }
                    sb.AppendLine("      package.json: " + new FileInfo(pkg).Length + " 字节  修改 " + File.GetLastWriteTime(pkg).ToString("yyyy-MM-dd HH:mm:ss")
                        + "  BOM=" + (bom ? "有   <<== JSON.parse 会失败" : "无")
                        + "  JSON=" + (err == null ? "正常" : "解析失败: " + err + "   <<=="));
                    if (err != null)
                        ctx.Add("bad", "版本 " + vname + " 的 package.json 解析失败: " + err + "（QQ 更新器可能改坏了；用安装器重装一次）");
                    if (main == null)
                    {
                        sb.AppendLine("      main 字段: 缺失   <<== 没指向 LiteLoader");
                        ctx.Add("bad", "版本 " + vname + " 的 package.json 没有 main 字段，LiteLoader 不会加载");
                    }
                    else
                    {
                        string norm = main.Replace('\\', '/').Trim();
                        mainOk = string.Equals(norm, "./app_launcher/LiteLoader.js", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(norm, "app_launcher/LiteLoader.js", StringComparison.OrdinalIgnoreCase);
                        sb.AppendLine("      main = " + main + (mainOk ? "   （已指向 LiteLoader）" : "   <<== 没指向 app_launcher/LiteLoader.js，LiteLoader 不会加载"));
                        if (!mainOk)
                            ctx.Add("bad", "版本 " + vname + " 的 package.json main 指向 " + main + "，不是 app_launcher/LiteLoader.js（多半被 QQ 更新覆盖了，用安装器重装）");
                    }
                    if (bom)
                        ctx.Add("bad", "版本 " + vname + " 的 package.json 带 BOM（QQ 的 JSON.parse 会失败）");
                }

                string launcher = Path.Combine(app, "app_launcher", "LiteLoader.js");
                if (File.Exists(launcher))
                {
                    launcherOk = true;
                    string content = "";
                    try
                    {
                        using (var fs = new FileStream(launcher, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs, Utf8)) content = sr.ReadToEnd();
                    }
                    catch (Exception ex) { content = "(读失败: " + ex.Message + ")"; }
                    sb.AppendLine("      app_launcher\\LiteLoader.js: 存在 " + new FileInfo(launcher).Length + " 字节  修改 "
                        + File.GetLastWriteTime(launcher).ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine("      内容: " + Truncate(SanitizeText(Regex.Replace(content, @"\s+", " ")), 300));
                    if (content.IndexOf(LLRoot, StringComparison.OrdinalIgnoreCase) < 0)
                        ctx.Add("warn", "版本 " + vname + " 的 app_launcher/LiteLoader.js 指向的目录和当前 LiteLoader 目录不一致（" + LLRoot + "），可能是装了多份或改过 profile");
                }
                else
                {
                    sb.AppendLine("      app_launcher\\LiteLoader.js: 不存在   <<== 启动器没写，LiteLoader 不会加载");
                    ctx.Add("bad", "版本 " + vname + " 缺 app_launcher/LiteLoader.js（被打补丁的 QQ 会当成启动失败）");
                }
                if (mainOk && launcherOk) patched++; else noLauncher++;
            }
            sb.AppendLine("  汇总: 已打补丁 " + patched + " 个版本，未打补丁/异常 " + noLauncher + " 个版本");
            if (vdirs.Length > 1 && patched < vdirs.Length)
                ctx.Add("warn", "有 " + (vdirs.Length - patched) + " 个版本的 app 没打补丁/补丁不完整（QQ 自动更新会新建版本目录，需要重装一次安装器）");

            // 运行中的 QQ 是否就是安装目录里这份
            string rootExe = Path.Combine(root, "QQ.exe");
            bool anyMain = false;
            foreach (var p in ctx.Procs)
            {
                if (p.Extra || !p.IsMain || string.IsNullOrEmpty(p.Exe)) continue;
                anyMain = true;
                bool same = false;
                try { same = string.Equals(Path.GetFullPath(p.Exe), Path.GetFullPath(rootExe), StringComparison.OrdinalIgnoreCase); }
                catch { same = false; }
                sb.AppendLine("  运行中的 " + p.Name + "（PID " + p.Pid + "）: " + p.Exe + (same ? "   （就是安装目录里这份）" : "   <<== 不在安装目录"));
                if (!same)
                    ctx.Add("bad", "运行中的 QQ 不在安装目录（" + p.Exe + "），装补丁的是 " + rootExe + "；启动的是另一份 QQ");
            }
            if (anyMain && !File.Exists(rootExe))
                ctx.Add("bad", "运行中的 QQ 在别处，而安装目录里没有 QQ.exe");
        }

        static void LiteLoaderBodySection(StringBuilder sb, DiagContext ctx)
        {
            sb.AppendLine("  本体目录: " + LLRoot + "  （存在=" + (Directory.Exists(LLRoot) ? "是" : "否") + "）");
            string pkg = Path.Combine(LLRoot, "package.json");
            string ver = "";
            bool pkgOk = false;
            if (File.Exists(pkg))
            {
                try
                {
                    var map = MiniJson.AsObj(MiniJson.Parse(File.ReadAllText(pkg, Utf8)));
                    if (map != null) ver = MiniJson.Str(map, "version");
                    pkgOk = true;
                    sb.AppendLine("  本体版本: " + (ver.Length == 0 ? "(package.json 没写 version)" : ver)
                        + "   package.json " + new FileInfo(pkg).Length + " 字节  修改 " + File.GetLastWriteTime(pkg).ToString("yyyy-MM-dd HH:mm:ss"));
                }
                catch (Exception ex) { sb.AppendLine("  package.json 解析失败: " + ex.Message); }
            }
            else
            {
                sb.AppendLine("  package.json 不存在   <<== LiteLoader 本体没装好");
                ctx.Add("bad", "LiteLoader 本体不完整：缺 " + pkg);
            }
            if (File.Exists(pkg) && !pkgOk)
                ctx.Add("warn", "LiteLoader 本体 package.json 解析失败（版本信息读不到，本体可能被改坏）");

            string store = Path.Combine(LLRoot, @"src\main\store.js");
            string runtimeFile = Path.Combine(LLRoot, @"src\main\runtime.js");
            bool storeExists = File.Exists(store), direntBad = false, fixOk = false, direntName = false;
            if (storeExists)
            {
                string text = ReadTextSafe(store, 512 * 1024);
                direntBad = text.IndexOf("dirent.path", StringComparison.OrdinalIgnoreCase) >= 0;
                fixOk = text.IndexOf("LiteLoader.path.plugins", StringComparison.Ordinal) >= 0;
                direntName = text.IndexOf("dirent.name", StringComparison.Ordinal) >= 0;
                sb.AppendLine("  src\\main\\store.js: " + new FileInfo(store).Length + " 字节  修改 " + File.GetLastWriteTime(store).ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("      含 dirent.path（官方版写法，新版 Node 下插件扫描会抛错）: " + (direntBad ? "有   <<==" : "无（已修补）"));
                sb.AppendLine("      含 dirent.name（修补后的写法）: " + (direntName ? "有" : "无   <<=="));
                sb.AppendLine("      含 LiteLoader.path.plugins（修补标记）: " + (fixOk ? "有" : "无   <<=="));
            }
            else
            {
                sb.AppendLine("  src\\main\\store.js 不存在   <<== 本体不完整");
            }

            bool runtimeOk = false;
            if (File.Exists(runtimeFile))
            {
                string text = ReadTextSafe(runtimeFile, 256 * 1024);
                runtimeOk = text.IndexOf("unregisterPlugin", StringComparison.Ordinal) >= 0;
                sb.AppendLine("  src\\main\\runtime.js: " + new FileInfo(runtimeFile).Length + " 字节  含 unregisterPlugin: " + (runtimeOk ? "有" : "无   <<=="));
            }
            else sb.AppendLine("  src\\main\\runtime.js 不存在   <<==");

            // 本体是否修补版
            bool patched = storeExists && !direntBad && fixOk;
            if (!Directory.Exists(LLRoot))
            {
                ctx.Add("bad", "LiteLoader 本体目录不存在（" + LLRoot + "）：本体没装");
            }
            else if (!patched)
            {
                if (direntBad || storeExists)
                    ctx.Add("bad", "LiteLoader 本体不是修补版（store.js 仍有 dirent.path / 缺 LiteLoader.path.plugins）：插件扫描会抛错，插件加载不出来");
            }
            else
            {
                ctx.Add("ok", "LiteLoader 本体是修补版（" + (ver.Length == 0 ? "版本未知" : ver) + "）");
            }
            if (!runtimeOk && Directory.Exists(LLRoot))
                ctx.Add("warn", "LiteLoader runtime.js 里没有 unregisterPlugin（插件热重载/删除可能报错）");

            sb.AppendLine("  src 关键文件（只列元数据，不打包源码）:");
            foreach (var rel in new[] { "src/main.js", "src/preload.js", "src/renderer.js", "src/main/api.js", "src/main/loader.js", "src/main/hook.js", "src/main/runtime.js", "src/main/store.js" })
            {
                string p = Path.Combine(LLRoot, rel.Replace('/', '\\'));
                try
                {
                    if (!File.Exists(p)) { sb.AppendLine("      " + rel + ": 不存在"); continue; }
                    sb.AppendLine("      " + rel + ": " + new FileInfo(p).Length + " 字节  修改 " + File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss"));
                }
                catch (Exception ex) { sb.AppendLine("      " + rel + ": 读取失败 " + ex.Message); }
            }
        }

        static void DisplaySection(StringBuilder sb, DiagContext ctx)
        {
            bool remote = false;
            try { remote = SystemInformation.TerminalServerSession; } catch { }
            sb.AppendLine("  远程会话（TerminalServerSession）: " + (remote ? "是   <<== 远程桌面/虚拟机里壁纸、透明度、硬件加速都可能异常" : "否"));
            if (remote) ctx.Add("warn", "当前是远程桌面会话：壁纸/磨砂效果可能与本机显示不同，排查壁纸问题时请在本机会话里试");

            int dpiX = 96, dpiY = 96;
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) { dpiX = (int)Math.Round(g.DpiX); dpiY = (int)Math.Round(g.DpiY); }
            }
            catch { }
            uint sysDpi = 0;
            try { sysDpi = GetDpiForSystem(); } catch { }
            sb.AppendLine("  缩放/DPI: " + dpiX + " DPI（约 " + (dpiX * 100 / 96) + "%）" + (sysDpi > 0 ? "   系统 DPI=" + sysDpi + "（约 " + (int)(sysDpi * 100 / 96) + "%）" : ""));

            try
            {
                var screens = Screen.AllScreens;
                sb.AppendLine("  显示器数量: " + screens.Length + "  SystemInformation.MonitorCount=" + SystemInformation.MonitorCount);
                foreach (var s in screens)
                {
                    sb.AppendLine("    " + s.DeviceName + "  " + s.Bounds.Width + "x" + s.Bounds.Height
                        + "  工作区 " + s.WorkingArea.Width + "x" + s.WorkingArea.Height
                        + "  位置(" + s.Bounds.X + "," + s.Bounds.Y + ")" + (s.Primary ? "  [主屏]" : "")
                        + "  色深 " + s.BitsPerPixel + "bpp");
                }
                sb.AppendLine("  虚拟屏幕: " + SystemInformation.VirtualScreen.Width + "x" + SystemInformation.VirtualScreen.Height);
                if (screens.Length == 0)
                {
                    sb.AppendLine("  <<== 没有枚举到显示器（无头会话？）");
                    ctx.Add("warn", "没有枚举到显示器：壁纸取色/布局可能出现异常");
                }
            }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }

            try
            {
                var q = new System.Management.ManagementObjectSearcher(
                    "SELECT Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, AdapterRAM, VideoProcessor FROM Win32_VideoController");
                int n = 0;
                foreach (System.Management.ManagementObject o in q.Get())
                {
                    string name = Convert.ToString(o["Name"]);
                    if (string.IsNullOrEmpty(name)) continue;
                    n++;
                    sb.AppendLine("  显卡: " + name + "   驱动 " + NStr(o["DriverVersion"]));
                    sb.AppendLine("      当前模式: " + NStr(o["CurrentHorizontalResolution"]) + "x" + NStr(o["CurrentVerticalResolution"])
                        + " @" + NStr(o["CurrentRefreshRate"]) + "Hz   处理器 " + NStr(o["VideoProcessor"]));
                }
                if (n == 0) sb.AppendLine("  显卡: WMI 没返回数据");
            }
            catch (Exception ex) { sb.AppendLine("  显卡 WMI 采集失败（可能没有权限）: " + ex.Message); }

            try
            {
                var q = new System.Management.ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem");
                foreach (System.Management.ManagementObject o in q.Get())
                {
                    sb.AppendLine("  物理内存: 共 " + (Convert.ToInt64(o["TotalVisibleMemorySize"]) / 1024) + " MB，可用 " + (Convert.ToInt64(o["FreePhysicalMemory"]) / 1024) + " MB");
                    sb.AppendLine("  虚拟内存: 共 " + (Convert.ToInt64(o["TotalVirtualMemorySize"]) / 1024) + " MB，可用 " + (Convert.ToInt64(o["FreeVirtualMemory"]) / 1024) + " MB");
                }
            }
            catch (Exception ex) { sb.AppendLine("  内存采集失败: " + ex.Message); }
        }

        static void WindowSection(StringBuilder sb, DiagContext ctx)
        {
            sb.AppendLine("  进程来源: " + ctx.ProcessSource + (ctx.ProcessError == null ? "" : "（WMI 失败: " + ctx.ProcessError + "）"));
            if (ctx.Procs.Count == 0)
            {
                sb.AppendLine("  未找到 QQ.exe / QQEX.exe 进程：QQ 没运行，抓不到启动时弹出的窗口。");
                sb.AppendLine("  下次弹报错时不要关弹窗，直接点「导出诊断包」，这里就能抓到标题和正文。");
                return;
            }
            int totalWnd = 0;
            foreach (var p in ctx.Procs)
            {
                sb.AppendLine("  PID " + p.Pid + "  " + p.Name
                    + (p.Extra ? "  [自测进程]" : (p.IsMain ? "  [主进程]" : "  [子进程]"))
                    + "  窗口数 " + p.Windows.Count);
                foreach (var w in p.Windows)
                {
                    totalWnd++;
                    NativeRect r = w.Rect;
                    sb.AppendLine("    窗口 类=" + (w.Cls.Length == 0 ? "?" : w.Cls)
                        + "  可见=" + (w.Visible ? "是" : "否")
                        + "  矩形=(" + r.Left + "," + r.Top + ")-(" + r.Right + "," + r.Bottom + ")"
                        + "  标题=" + (w.Title.Length == 0 ? "(空)" : w.Title));
                    if (w.Cls == "#32770")
                    {
                        sb.AppendLine("      对话框正文: " + (w.Body.Length == 0 ? "(没有拿到子控件文本)" : w.Body));
                        if (w.Children != null)
                            foreach (var c in w.Children) sb.AppendLine("        子控件 " + c);
                    }
                }
            }
            sb.AppendLine("  顶层窗口合计: " + totalWnd + " 个；#32770 对话框: " + ctx.DialogCount + " 个（最多列 30 个子控件，每个 ≤500 字符）");
        }

        static void LocalStorageSection(StringBuilder sb, DiagContext ctx)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string qq = Path.Combine(appData, "QQ");
            sb.AppendLine("  QQ 数据目录: " + qq + "（存在=" + (Directory.Exists(qq) ? "是" : "否") + "）");
            string leveldb = Path.Combine(qq, @"Local Storage\leveldb");
            DescribeDirMeta(sb, "Local Storage\\leveldb", leveldb, 20);
            if (!Directory.Exists(leveldb))
                DescribeDirMeta(sb, "Local Storage", Path.Combine(qq, "Local Storage"), 10);
            DescribeFileMeta(sb, "Preferences", Path.Combine(qq, "Preferences"));
            DescribeFileMeta(sb, "Local State", Path.Combine(qq, "Local State"));
            sb.AppendLine("  提示: 这里只列元数据（文件数/大小/时间），不读取内容、不复制 leveldb；");
            sb.AppendLine("        壁纸键值、#app::before 计算值、壁纸操作审计请用琉璃设置页「导出诊断包」。");
        }

        static void DescribeDirMeta(StringBuilder sb, string label, string dir, int listMax)
        {
            try
            {
                if (!Directory.Exists(dir)) { sb.AppendLine("  " + label + ": 不存在（" + dir + "）"); return; }
                FileInfo[] files = new DirectoryInfo(dir).GetFiles();
                long total = 0;
                DateTime newest = DateTime.MinValue;
                foreach (var f in files)
                {
                    total += f.Length;
                    if (f.LastWriteTime > newest) newest = f.LastWriteTime;
                }
                sb.AppendLine("  " + label + ": " + files.Length + " 个文件，共 " + SizeText(total) + "，最新修改 "
                    + (files.Length == 0 ? "(空)" : newest.ToString("yyyy-MM-dd HH:mm:ss")));
                sb.AppendLine("      路径: " + dir);
                int n = 0;
                foreach (var f in files)
                {
                    if (n >= listMax) break;
                    sb.AppendLine("      " + f.Name.PadRight(28) + f.Length.ToString("N0").PadLeft(12) + " 字节  " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    n++;
                }
                if (files.Length > listMax) sb.AppendLine("      … 另有 " + (files.Length - listMax) + " 个文件");
            }
            catch (Exception ex) { sb.AppendLine("  " + label + " 采集失败: " + ex.Message); }
        }

        static void DescribeFileMeta(StringBuilder sb, string label, string file)
        {
            try
            {
                if (!File.Exists(file)) { sb.AppendLine("  " + label + ": 不存在（" + file + "）"); return; }
                var fi = new FileInfo(file);
                sb.AppendLine("  " + label + ": " + fi.Length + " 字节  修改 " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "  （只列元数据）");
            }
            catch (Exception ex) { sb.AppendLine("  " + label + " 采集失败: " + ex.Message); }
        }

        static void TimelineSection(StringBuilder sb, DiagContext ctx)
        {
            var entries = new List<TimelineEntry>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 1) installer.log 尾部（行里带 yyyy-MM-dd HH:mm:ss 的用行时间，老行没有时间戳才退回文件时间）
            try
            {
                string log = Path.Combine(LLRoot, "installer.log");
                DateTime? mt = FileTimeOrNull(log);
                if (mt != null)
                {
                    sb.AppendLine("  installer.log 修改时间: " + mt.Value.ToString("yyyy-MM-dd HH:mm:ss") + "（行里有时间戳的按行，解析不到才用文件时间并标 (mtime)）");
                    foreach (var ln in ReadTailLines(log, 12))
                    {
                        string t = ln.Trim();
                        if (t.Length == 0) continue;
                        bool fromFileTime;
                        DateTime when = ParseOr(log, t, mt, out fromFileTime);
                        AddTimeline(entries, when, "installer.log", t, 0, fromFileTime);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  读 installer.log 失败: " + ex.Message); }

            // 2) 插件 data 日志（session.log / plugin.log）
            try
            {
                string data = Path.Combine(LLRoot, "data");
                if (Directory.Exists(data))
                {
                    foreach (var d in Directory.GetDirectories(data))
                    {
                        string slug = Path.GetFileName(d);
                        foreach (var name in new[] { "session.log", "session.1.log", "plugin.log" })
                        {
                            string p = Path.Combine(d, name);
                            if (!File.Exists(p)) continue;
                            DateTime? mt = FileTimeOrNull(p);
                            foreach (var ln in ReadTailLines(p, 15))
                            {
                                bool fromFileTime;
                                DateTime when = ParseOr(p, ln, mt, out fromFileTime);
                                AddTimeline(entries, when, slug + "/" + name, ln, 2, fromFileTime);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  读插件 data 日志失败: " + ex.Message); }

            // 3) QQ app_launcher 最新日志
            try
            {
                string dir = Path.Combine(appData, @"QQ\log");
                if (Directory.Exists(dir))
                {
                    FileInfo newest = null;
                    foreach (var f in new DirectoryInfo(dir).GetFiles("app_launcher-*.log"))
                        if (newest == null || f.LastWriteTime > newest.LastWriteTime) newest = f;
                    if (newest != null)
                    {
                        sb.AppendLine("  QQ 启动日志（最新）: " + newest.Name + "  " + newest.Length + " 字节  修改 " + newest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        foreach (var ln in ReadTailLines(newest.FullName, 20))
                        {
                            bool fromFileTime;
                            DateTime when = ParseOr(newest.FullName, ln, newest.LastWriteTime, out fromFileTime);
                            AddTimeline(entries, when, "QQ/" + newest.Name, ln, 3, fromFileTime);
                        }
                    }
                    else sb.AppendLine("  QQ\\log 下没有 app_launcher-*.log");
                }
            }
            catch (Exception ex) { sb.AppendLine("  读 QQ 启动日志失败: " + ex.Message); }

            // 4) LiteLoader 主进程 tee 日志（插件加载 / 报错 / 弹框）
            try
            {
                string ll = Path.Combine(LLRoot, "liteLoader.log");
                DateTime? mt = FileTimeOrNull(ll);
                if (mt != null)
                {
                    sb.AppendLine("  LiteLoader/liteLoader.log 修改时间: " + mt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                    foreach (var ln in ReadTailLines(ll, 25))
                    {
                        bool fromFileTime;
                        DateTime when = ParseOr(ll, ln, mt, out fromFileTime);
                        AddTimeline(entries, when, "LiteLoader/liteLoader.log", ln, 1, fromFileTime);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  读 liteLoader.log 失败: " + ex.Message); }

            // 4) 目录 / exe 修改时间
            try
            {
                if (!string.IsNullOrEmpty(ctx.QQRoot))
                {
                    string versions = Path.Combine(ctx.QQRoot, "versions");
                    if (Directory.Exists(versions))
                        entries.Add(new TimelineEntry { When = Directory.GetLastWriteTime(versions), Source = "versions 目录", Text = "最近修改（QQ 更新/打补丁会动这里）" });
                    string exe = Path.Combine(ctx.QQRoot, "QQ.exe");
                    if (File.Exists(exe))
                        entries.Add(new TimelineEntry { When = File.GetLastWriteTime(exe), Source = "QQ.exe", Text = "QQ.exe 修改时间（打补丁/更新）" });
                }
                string store = Path.Combine(LLRoot, @"src\main\store.js");
                if (File.Exists(store))
                    entries.Add(new TimelineEntry { When = File.GetLastWriteTime(store), Source = "LiteLoader 本体", Text = "store.js 修改时间（本体更新/重装）" });
            }
            catch (Exception ex) { sb.AppendLine("  读目录时间失败: " + ex.Message); }

            // 目录/exe 这类没有日志行的，用文件 mtime；同时间排在日志之后
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Seq == 0 && entries[i].Priority == 0)
                {
                    entries[i].Priority = 4;
                    entries[i].Seq = 100000 + i;
                }
            entries.Sort(delegate (TimelineEntry a, TimelineEntry b)
            {
                int c = b.When.CompareTo(a.When);
                if (c != 0) return c;
                c = a.Priority.CompareTo(b.Priority);
                if (c != 0) return c;
                return a.Seq.CompareTo(b.Seq);
            });
            sb.AppendLine("  合并来源: installer.log / liteLoader.log / 插件 data 日志 / QQ app_launcher 日志 / versions 与 QQ.exe 修改时间；按真实时间倒序最多 40 行（同时间按 installer.log > liteLoader.log > 插件 > QQ 排序）。");
            int shown = 0;
            foreach (var e in entries)
            {
                if (shown >= 40) break;
                sb.AppendLine("  " + e.When.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + e.Source + "]  " + e.Text + (e.FromFileTime ? "  (mtime)" : ""));
                shown++;
            }
            if (shown == 0) sb.AppendLine("  (没有拿到任何时间线信息)");
        }

        // priority：installer.log > liteLoader.log > 插件日志 > QQ app_launcher > 目录/exe（同时间按此稳定排序）
        static void AddTimeline(List<TimelineEntry> entries, DateTime when, string source, string line, int priority, bool fromFileTime)
        {
            string clean = Regex.Replace(line == null ? "" : line, @"\s+", " ").Trim();
            if (clean.Length == 0) return;
            var e = MakeEntry(when, source, clean);
            e.Priority = priority;
            e.Seq = entries.Count;
            e.FromFileTime = fromFileTime;
            entries.Add(e);
        }

        static TimelineEntry MakeEntry(DateTime when, string source, string text)
        {
            return new TimelineEntry
            {
                When = when,
                Source = source,
                Text = Truncate(SanitizeText(Regex.Replace(text, @"\s+", " ")), 180)
            };
        }

        // 行里能解析出时间就用行时间；解析不到才退回文件 mtime，并标记 fromFileTime（显示时加 (mtime)）
        static DateTime ParseOr(string path, string line, DateTime? fallback, out bool fromFileTime)
        {
            DateTime t;
            if (TryFindTime(line, out t)) { fromFileTime = false; return t; }
            fromFileTime = true;
            if (fallback != null) return fallback.Value;
            DateTime? mt = FileTimeOrNull(path);
            return mt == null ? DateTime.MinValue : mt.Value;
        }

        static DateTime? FileTimeOrNull(string path)
        {
            try { if (File.Exists(path)) return File.GetLastWriteTime(path); } catch { }
            return null;
        }

        static bool TryFindTime(string line, out DateTime t)
        {
            t = DateTime.MinValue;
            if (string.IsNullOrEmpty(line)) return false;
            try
            {
                var m = Regex.Match(line, @"(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2}):(\d{2})");
                if (m.Success)
                    return DateTime.TryParseExact(m.Value.Replace('T', ' '), "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
                m = Regex.Match(line, @"\[(\d{4})/(\d{1,2})/(\d{1,2})[ T](\d{1,2}):(\d{2}):(\d{2})");
                if (m.Success)
                {
                    t = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                        int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value));
                    return true;
                }
            }
            catch { }
            return false;
        }

        static void QqInstallSection(StringBuilder sb, DiagContext ctx)
        {
            string root = ctx.QQRoot;
            sb.AppendLine("  安装目录: " + (string.IsNullOrEmpty(root) ? "未找到" : root));
            if (!string.IsNullOrEmpty(root))
            {
                try
                {
                    string versions = Path.Combine(root, "versions");
                    if (Directory.Exists(versions))
                    {
                        foreach (var v in Directory.GetDirectories(versions))
                        {
                            string vj = Path.Combine(v, "version.json");
                            string ver = "";
                            try { if (File.Exists(vj)) ver = Regex.Replace(File.ReadAllText(vj, Utf8), @"\s+", " "); }
                            catch { }
                            sb.AppendLine("  版本目录: " + Path.GetFileName(v) + "  " + Truncate(SanitizeText(ver), 300));
                        }
                    }
                    else sb.AppendLine("  versions 目录不存在");
                }
                catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }

                foreach (var n in new[] { "QQ.exe", "dbghelp.dll", "winmm.dll" })
                {
                    try
                    {
                        string p = Path.Combine(root, n);
                        if (!File.Exists(p)) { sb.AppendLine("  " + n + ": 不存在   <<=="); continue; }
                        var fi = new FileInfo(p);
                        string fv = "";
                        try
                        {
                            var vi = FileVersionInfo.GetVersionInfo(p);
                            if (vi != null && vi.FileVersion != null) fv = vi.FileVersion;
                        }
                        catch { }
                        string cmpText = PatchCompareText(n, p, fi.Length);
                        sb.AppendLine("  " + n + ": " + fi.Length + " 字节  修改 " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                            + (fv.Length == 0 ? "" : "  文件版本 " + fv));
                        sb.AppendLine("      SHA256 " + Sha256Of(p) + "   内置补丁=" + cmpText);
                    }
                    catch (Exception ex) { sb.AppendLine("  " + n + " 采集失败: " + ex.Message); }
                }
            }
            sb.AppendLine("  原版备份: " + (Directory.Exists(Path.Combine(BackupRoot, "original")) ? Path.Combine(BackupRoot, "original") : "不存在"));

            // 体检：补丁文件
            if (string.IsNullOrEmpty(root))
            {
                ctx.Add("bad", "没找到 QQ 安装目录（注册表卸载项和默认路径都没有）");
            }
            else
            {
                foreach (var n in new[] { "QQ.exe", "dbghelp.dll", "winmm.dll" })
                {
                    string p = Path.Combine(root, n);
                    try
                    {
                        if (!File.Exists(p))
                        {
                            ctx.Add("bad", "补丁文件缺失：" + p + "（QQ 更新或被杀软删了，需要重装安装器）");
                            continue;
                        }
                        bool same = SameAsRes(n, p);
                        if (same)
                        {
                            ctx.Add("ok", "补丁文件 " + n + " 校验通过（与安装器内置一致）");
                        }
                        else
                        {
                            long fileLen = 0;
                            try { fileLen = new FileInfo(p).Length; } catch { }
                            long builtinLen = ResourceLength(n);
                            if (builtinLen > 0 && fileLen == builtinLen)
                                ctx.Add("info", "补丁文件 " + n + " 与内置构建不同（大小相同，通常只是 PE 时间戳差异，不影响功能；重跑安装器可对齐）");
                            else
                                ctx.Add("bad", "安装目录里的 " + n + " 与安装器内置补丁不一致（大小也不同，多半被 QQ 更新或其它安装器覆盖了）→ 重装安装器可修复");
                        }
                    }
                    catch (Exception ex) { ctx.Add("warn", n + " 校验失败: " + ex.Message); }
                }
            }
        }

        static void ProcSection(StringBuilder sb, DiagContext ctx)
        {
            sb.AppendLine("  进程来源: " + ctx.ProcessSource);
            if (ctx.ProcessError != null)
                sb.AppendLine("  WMI 查询失败（可能没有管理员权限，已降级）: " + ctx.ProcessError);
            if (ctx.Procs.Count == 0)
            {
                sb.AppendLine("  没有运行中的 QQ.exe / QQEX.exe");
                return;
            }
            foreach (var p in ctx.Procs)
            {
                if (p.Extra)
                {
                    sb.AppendLine("  PID " + p.Pid + "  " + p.Name + "  [自测进程，不参与体检]");
                    continue;
                }
                sb.AppendLine("  PID " + p.Pid + "  " + p.Name + "  父 PID " + p.ParentPid + "  权限: " + Elevated(p.Pid) + (p.IsMain ? "  [主进程]" : ""));
                if (!string.IsNullOrEmpty(p.Exe)) sb.AppendLine("      exe: " + p.Exe);
                if (p.Started != null) sb.AppendLine("      启动时间: " + p.Started.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                if (!string.IsNullOrEmpty(p.Cmd)) sb.AppendLine("      cmd: " + Truncate(p.Cmd, 300));
            }
            sb.AppendLine("  QQ 主进程数: " + ctx.MainCount
                + (ctx.MainCount > 1 ? "   <<== 超过 1 个，双实例抢库会直接弹此报错" : ""));
        }

        static void ChatDataSection(StringBuilder sb, DiagContext ctx)
        {
            var dirs = DataDirs();
            if (dirs.Count == 0) sb.AppendLine("  没找到 nt_qq 数据目录");
            foreach (var nt in dirs)
            {
                try
                {
                    var di = new DriveInfo(Path.GetPathRoot(nt));
                    bool risky = di.DriveType == DriveType.Removable || di.DriveType == DriveType.Network;
                    if (risky)
                        ctx.Add("bad", "聊天记录在" + (di.DriveType == DriveType.Removable ? "可移动盘" : "网络盘") + "（" + nt + "），这是启动报「文件系统访问异常」最常见的原因");
                    else
                        ctx.Add("ok", "聊天记录在本地固定盘（" + nt + "）");
                }
                catch (Exception ex) { sb.AppendLine("  盘信息采集失败: " + ex.Message); }
                try { DescribeDataDir(sb, nt); }
                catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
            }
        }

        static void DiskSection(StringBuilder sb)
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) { sb.AppendLine("  " + d.Name + "  " + d.DriveType + "  (未就绪)"); continue; }
                    sb.AppendLine("  " + d.Name + "  " + d.DriveType + "  " + d.DriveFormat
                        + "  剩余 " + (d.AvailableFreeSpace / 1024 / 1024) + " MB / 共 " + (d.TotalSize / 1024 / 1024) + " MB");
                }
                catch { }
            }
        }

        static void CfaSection(StringBuilder sb, DiagContext ctx)
        {
            try
            {
                object v = null;
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access"))
                {
                    if (k != null) v = k.GetValue("EnableControlledFolderAccess");
                }
                sb.AppendLine("  EnableControlledFolderAccess = " + (v == null ? "(未设置，默认关闭)" : v.ToString()) + "   (0=关 1=开 2=仅审计)");
                if (v != null)
                {
                    int iv = Convert.ToInt32(v);
                    if (iv == 1) ctx.Add("bad", "Defender「受控文件夹访问」已开启：可能拦截 dbghelp.dll 注入和聊天记录写入，先把它关掉再试");
                    else if (iv == 2) ctx.Add("warn", "Defender「受控文件夹访问」处于「仅审计」模式（只记录不拦）");
                    else ctx.Add("ok", "Defender「受控文件夹访问」已关闭");
                }
                else ctx.Add("ok", "Defender「受控文件夹访问」未设置（默认关闭）");
            }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access\ProtectedFolders"))
                {
                    if (k != null)
                    {
                        var names = k.GetSubKeyNames();
                        if (names.Length == 0) sb.AppendLine("  (没有额外的受保护文件夹)");
                        foreach (var s in names) sb.AppendLine("  受保护文件夹: " + s);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  受保护文件夹采集失败: " + ex.Message); }
        }

        static void DefenderEventsSection(StringBuilder sb)
        {
            try
            {
                using (var lg = new EventLog("Microsoft-Windows-Windows Defender/Operational"))
                {
                    int shown = 0, count = lg.Entries.Count, start = Math.Max(0, count - 3000);
                    for (int i = count - 1; i >= start && shown < 10; i--)
                    {
                        var e = lg.Entries[i];
                        int id = (int)((uint)e.InstanceId & 0xFFFF);
                        if (id != 1123 && id != 1124 && id != 1127 && id != 1128) continue;
                        shown++;
                        sb.AppendLine("  " + e.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss") + "  事件 " + id);
                        string[] lines = (e.Message == null ? "" : e.Message).Split('\n');
                        for (int j = 0; j < Math.Min(lines.Length, 6); j++) sb.AppendLine("      " + lines[j].TrimEnd('\r'));
                    }
                    if (shown == 0) sb.AppendLine("  无记录");
                }
            }
            catch (Exception ex) { sb.AppendLine("  读取失败（常见于没有管理员权限或没启用 Defender）: " + ex.Message); }
        }

        static void AppErrorEventsSection(StringBuilder sb)
        {
            try
            {
                using (var lg = new EventLog("Application"))
                {
                    int shown = 0, count = lg.Entries.Count, start = Math.Max(0, count - 3000);
                    for (int i = count - 1; i >= start && shown < 12; i--)
                    {
                        var e = lg.Entries[i];
                        if (!string.IsNullOrEmpty(e.Source) && (e.Source.Contains("Application Error") || e.Source.Contains("Application Hang")) == false) continue;
                        int id = (int)((uint)e.InstanceId & 0xFFFF);
                        if (id != 1000 && id != 1002) continue;
                        if (e.Message == null || e.Message.IndexOf("QQ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        shown++;
                        sb.AppendLine("  " + e.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Source + "  " + id);
                        string[] lines = e.Message.Split('\n');
                        for (int j = 0; j < Math.Min(lines.Length, 4); j++) sb.AppendLine("      " + lines[j].TrimEnd('\r'));
                    }
                    if (shown == 0) sb.AppendLine("  无记录");
                }
            }
            catch (Exception ex) { sb.AppendLine("  读取失败: " + ex.Message); }
        }

        static void InstallerLogSection(StringBuilder sb)
        {
            string log = Path.Combine(LLRoot, "installer.log");
            if (!File.Exists(log)) { sb.AppendLine("  installer.log 不存在（" + log + "）"); return; }
            try
            {
                var lines = ReadTailLines(log, 60);
                if (lines.Count == 0) sb.AppendLine("  (日志为空)");
                foreach (var ln in lines) sb.AppendLine("  " + SanitizeText(ln));
            }
            catch (Exception ex) { sb.AppendLine("  采集失败: " + ex.Message); }
        }

        // ---------- 日志错误聚合（errors-digest.txt） ----------

        static readonly string[] ErrorPatterns = new string[]
        {
            "error", "exception", "failed", "fail ", "无法", "失败", "错误", "异常",
            "cannot find", "ENOENT", "EACCES", "EPERM", "已损坏", "文件校验", "版本不支持",
            "not supported", "LiteLoader", "找不到", "拒绝访问", "0x800", "crash"
        };

        static string BuildErrorsDigest(string tmp, DiagContext ctx)
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== 日志错误聚合（errors-digest）==========");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("说明: 这是**自动筛选**：按关键字抓日志里可疑的行，不保证命中根因，也可能有噪音。");
            sb.AppendLine("组织方式: 文件 → 命中次数 → 首/末时间 → 最多 3 条样例行。");
            sb.AppendLine("原始日志都在包里同名目录下（QQ-log/、QQ-arks/、Tencent-Logs/、LiteLoader/、LiteLoader-data/）。");
            sb.AppendLine();
            sb.AppendLine("---------- 自动体检里最相关的 3 条提示 ----------");
            int hints = 0;
            foreach (var c in ctx.Checks)
            {
                if (c.Level != "bad") continue;
                sb.AppendLine("  - " + c.Text);
                if (++hints >= 3) break;
            }
            if (hints == 0)
                foreach (var c in ctx.Checks)
                {
                    if (c.Level != "warn") continue;
                    sb.AppendLine("  - " + c.Text);
                    if (++hints >= 3) break;
                }
            if (hints == 0) sb.AppendLine("  （自动体检没有发现问题）");
            sb.AppendLine();

            var blocks = new List<string>();
            int totalHits = 0, filesHit = 0, emitted = 0;
            bool truncated = false;
            string[] packs = new string[] { "QQ-log", "QQ-arks", "Tencent-Logs", "LiteLoader", "LiteLoader-data" };
            foreach (var pack in packs)
            {
                string dir = Path.Combine(tmp, pack);
                if (!Directory.Exists(dir)) { sb.AppendLine("[" + pack + "] 目录不存在（没有收集到）"); continue; }
                foreach (var f in SafeFiles(dir))
                {
                    if (!IsDigestTextFile(f.Name)) continue;
                    if (!FileLooksText(f.FullName)) continue;
                    var hits = new List<string>();
                    int count = 0;
                    DateTime first = DateTime.MinValue, last = DateTime.MinValue;
                    bool firstSet = false;
                    try
                    {
                        using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs, Utf8, true))
                        {
                            string ln;
                            int read = 0;
                            while ((ln = sr.ReadLine()) != null)
                            {
                                if (++read > 200000) break;
                                if (!LineMatchesPatterns(ln)) continue;
                                count++;
                                DateTime t;
                                if (TryFindTime(ln, out t))
                                {
                                    if (!firstSet) { first = t; firstSet = true; }
                                    last = t;
                                }
                                if (hits.Count < 200) hits.Add(Truncate(SanitizeText(ln.Trim()), 400));
                            }
                        }
                    }
                    catch { continue; }
                    if (count == 0) continue;
                    filesHit++;
                    totalHits += count;
                    if (!firstSet) { first = f.LastWriteTime; last = f.LastWriteTime; }
                    string rel = pack + "/" + f.FullName.Substring(dir.Length).TrimStart('\\').Replace('\\', '/');
                    var b = new StringBuilder();
                    b.AppendLine("[" + rel + "]  命中 " + count + " 行" + (count > 200 ? "（单文件最多取 200 条）" : ""));
                    b.AppendLine("    时间: " + (firstSet ? first.ToString("yyyy-MM-dd HH:mm:ss") : "(行里没有时间戳，用文件时间) " + first.ToString("yyyy-MM-dd HH:mm:ss"))
                        + "  →  " + last.ToString("yyyy-MM-dd HH:mm:ss") + "    文件修改: " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    int sample = 0;
                    foreach (var h in hits)
                    {
                        if (sample >= 3) break;
                        b.AppendLine("    · " + h);
                        sample++;
                    }
                    if (count > 3) b.AppendLine("    … 其余 " + (count - 3) + " 行见原文件");
                    blocks.Add(b.ToString());
                    emitted += 4 + sample;
                    if (emitted >= 600) { truncated = true; break; }
                }
                if (truncated) break;
            }
            foreach (var b in blocks) sb.Append(b);
            sb.AppendLine("---------- 汇总 ----------");
            sb.AppendLine("  命中文件 " + filesHit + " 个，命中行 " + totalHits + " 行"
                + (truncated ? "（输出已截断到约 600 行，更多见包内原始日志）" : ""));
            if (filesHit == 0) sb.AppendLine("  没有命中任何已知错误关键字（也可能是日志被清过 / 已轮转）。");
            ctx.ErrorHits = totalHits;
            return sb.ToString();
        }

        static bool IsDigestTextFile(string name)
        {
            string ext = Path.GetExtension(name);
            return string.Equals(ext, ".log", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase);
        }

        static bool LineMatchesPatterns(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            foreach (var p in ErrorPatterns)
                if (line.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // 前 2KB 里有 NUL 就当二进制跳过（.tlg 之类）
        static bool FileLooksText(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var buf = new byte[2048];
                    int n = fs.Read(buf, 0, buf.Length);
                    for (int i = 0; i < n; i++) if (buf[i] == 0) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ---------- 窗口/对话框枚举 ----------

        delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr parent, EnumWindowsCallback callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        static List<DiagWindow> EnumerateWindowsForPid(int pid)
        {
            var list = new List<DiagWindow>();
            EnumWindowsCallback cb = delegate (IntPtr h, IntPtr l)
            {
                uint p;
                try { GetWindowThreadProcessId(h, out p); } catch { return true; }
                if ((int)p != pid) return true;
                if (list.Count >= 80) return false;
                try { list.Add(DescribeWindow(h)); }
                catch { }
                return true;
            };
            try { EnumWindows(cb, IntPtr.Zero); }
            catch { }
            GC.KeepAlive(cb);
            return list;
        }

        static DiagWindow DescribeWindow(IntPtr h)
        {
            var w = new DiagWindow();
            w.Handle = h;
            try { w.Cls = ClassNameOf(h); } catch { w.Cls = "?"; }
            try { w.Title = CleanWindowText(WindowTextOf(h)); } catch { }
            try { w.Visible = IsWindowVisible(h); } catch { }
            NativeRect r;
            try { if (GetWindowRect(h, out r)) w.Rect = r; } catch { }
            if (w.Cls == "#32770")
            {
                var children = new List<string>();
                EnumWindowsCallback cb = delegate (IntPtr c, IntPtr l)
                {
                    if (children.Count >= 30) return false;
                    try
                    {
                        string cls = ClassNameOf(c);
                        string txt = CleanWindowText(WindowTextOf(c));
                        if (cls.Length == 0 && txt.Length == 0) return true;
                        children.Add("[" + (cls.Length == 0 ? "?" : cls) + "] " + (txt.Length == 0 ? "(无文本)" : txt));
                    }
                    catch { }
                    return true;
                };
                try { EnumChildWindows(h, cb, IntPtr.Zero); }
                catch { }
                GC.KeepAlive(cb);
                w.Children = children;
                var body = new StringBuilder();
                foreach (var c in children)
                {
                    if (body.Length > 0) body.Append(" | ");
                    body.Append(c);
                }
                w.Body = body.ToString();
            }
            return w;
        }

        // GetWindowText 对别的进程的控件拿不到文本，这里补 WM_GETTEXT（系统会自动跨进程编组）
        static string WindowTextOf(IntPtr h)
        {
            try
            {
                int len = GetWindowTextLength(h);
                if (len > 0)
                {
                    var sb2 = new StringBuilder(len + 2);
                    if (GetWindowText(h, sb2, sb2.Capacity) > 0) return sb2.ToString();
                }
            }
            catch { }
            try
            {
                var sb3 = new StringBuilder(1024);
                IntPtr res;
                const uint WM_GETTEXT = 0x000D;
                const uint SMTO_ABORTIFHUNG = 0x0002;
                IntPtr ok = SendMessageTimeout(h, WM_GETTEXT, (IntPtr)sb3.Capacity, sb3, SMTO_ABORTIFHUNG, 150, out res);
                if (ok != IntPtr.Zero) return sb3.ToString();
            }
            catch { }
            return "";
        }

        static string ClassNameOf(IntPtr h)
        {
            try
            {
                var sb = new StringBuilder(256);
                int n = GetClassName(h, sb, sb.Capacity);
                return n <= 0 ? "" : sb.ToString();
            }
            catch { return ""; }
        }

        static string CleanWindowText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string r = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Replace("\0", "");
            r = Regex.Replace(r, @"\s{2,}", " ").Trim();
            if (r.Length > 500) r = r.Substring(0, 500) + "…";
            return r;
        }

        // ---------- 文本 / 元数据工具 ----------

        static bool HasBom(string path)
        {
            try
            {
                byte[] head = new byte[3];
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int n = fs.Read(head, 0, 3);
                    return n == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
                }
            }
            catch { return false; }
        }

        static string ReadTextSafe(string path, int maxChars)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Utf8, true))
                {
                    var sb = new StringBuilder();
                    var buf = new char[8192];
                    int n;
                    while ((n = sr.Read(buf, 0, buf.Length)) > 0)
                    {
                        sb.Append(buf, 0, n);
                        if (sb.Length >= maxChars) break;
                    }
                    string s = sb.ToString();
                    if (s.Length > maxChars) s = s.Substring(0, maxChars);
                    return s;
                }
            }
            catch { return ""; }
        }

        static List<string> ReadTailLines(string path, int maxLines)
        {
            var all = new List<string>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Utf8, true))
                {
                    string ln;
                    int n = 0;
                    while ((ln = sr.ReadLine()) != null)
                    {
                        if (++n > 200000) break;
                        all.Add(ln.Length > 2000 ? ln.Substring(0, 2000) : ln);
                    }
                }
            }
            catch { }
            if (all.Count <= maxLines) return all;
            return all.GetRange(all.Count - maxLines, maxLines);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        // 脱敏：uin/uid/邮箱/sk-/Bearer/长 base64（含壁纸 dataURL）都抹掉
        static string SanitizeText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string r = s;
            try
            {
                r = Regex.Replace(r, @"(uin\s*[=:]\s*)\d+", "$1***");
                r = Regex.Replace(r, @"(uid\s*[=:]\s*)[A-Za-z0-9_\-]+", "$1***");
                r = Regex.Replace(r, @"u_[A-Za-z0-9]{6,}", "u_***");
                r = Regex.Replace(r, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", "***@***");
                r = Regex.Replace(r, @"(sk-)[A-Za-z0-9]{8,}", "$1***");
                r = Regex.Replace(r, @"(Bearer\s+)[A-Za-z0-9\._\-]{8,}", "$1***");
                r = Regex.Replace(r, @"data:image/[a-zA-Z0-9.+\-]+;base64,[A-Za-z0-9+/=]{100,}", "data:image/...;base64,<已省略>");
                // 通用长 base64（没有 data: 前缀的 token/壁纸串）
                r = Regex.Replace(r, @"[A-Za-z0-9+/=]{300,}", "<长 base64 已省略>");
            }
            catch { }
            return r;
        }

        // 只有运行时真正加载的文件才进「主题不生效」总结论（.gitignore / README / LICENSE 这类不算）
        static bool IsRuntimePluginFile(string rel)
        {
            string name = Path.GetFileName(rel);
            if (name.Length == 0) return false;
            if (name.StartsWith(".", StringComparison.Ordinal)) return false;
            string low = rel.ToLowerInvariant();
            if (low == "license" || low == "readme.md" || low.StartsWith("readme.")) return false;
            return true;
        }

        static string JoinLimited(List<string> list, int max)
        {
            if (list == null || list.Count == 0) return "(无)";
            var head = new List<string>();
            for (int i = 0; i < list.Count && i < max; i++) head.Add(list[i]);
            string s = string.Join("；", head.ToArray());
            if (list.Count > max) s += " …（共 " + list.Count + " 项）";
            return s;
        }

        static string JoinJsonArray(Dictionary<string, object> map, string key, int max)
        {
            try
            {
                var arr = MiniJson.Arr(map, key);
                if (arr == null) return "(缺失)";
                var list = new List<string>();
                foreach (var it in arr)
                {
                    if (list.Count >= max) { list.Add("…"); break; }
                    if (it is string) list.Add((string)it);
                    else list.Add(Convert.ToString(it));
                }
                return list.Count == 0 ? "[]（空）" : "[" + string.Join(", ", list.ToArray()) + "]";
            }
            catch (Exception ex) { return "(采集失败: " + ex.Message + ")"; }
        }

        static string JoinJsonObjectKeys(Dictionary<string, object> map, string key, int max)
        {
            try
            {
                var o = MiniJson.AsObj(MiniJson.Get(map, key));
                if (o == null) return "(缺失)";
                var list = new List<string>();
                foreach (var k in o.Keys)
                {
                    if (list.Count >= max) { list.Add("…"); break; }
                    list.Add(k);
                }
                return list.Count == 0 ? "{}（空）" : "{" + string.Join(", ", list.ToArray()) + "}";
            }
            catch (Exception ex) { return "(采集失败: " + ex.Message + ")"; }
        }

        // 版本比较：按 . 分段数字比较，非数字按字符串
        static int CompareVersion(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 0;
            if (string.IsNullOrEmpty(a)) return -1;
            if (string.IsNullOrEmpty(b)) return 1;
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                string x = i < pa.Length ? pa[i] : "0";
                string y = i < pb.Length ? pb[i] : "0";
                int ix, iy;
                if (int.TryParse(x, out ix) && int.TryParse(y, out iy))
                {
                    if (ix != iy) return ix < iy ? -1 : 1;
                }
                else
                {
                    int c = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c < 0 ? -1 : 1;
                }
            }
            return 0;
        }

        static string IsElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) ? "是（提权运行）" : "否（普通用户）";
            }
            catch { return "未知"; }
        }

        static void DescribeDataDir(StringBuilder info, string nt)
        {
            info.AppendLine();
            info.AppendLine("  ---- " + nt);
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(nt));
                info.AppendLine("      盘: " + di.Name + "  类型 " + di.DriveType + "  格式 " + di.DriveFormat
                    + "  剩余 " + (di.AvailableFreeSpace / 1024 / 1024) + " MB / 共 " + (di.TotalSize / 1024 / 1024) + " MB");
                if (di.DriveType == DriveType.Removable || di.DriveType == DriveType.Network)
                    info.AppendLine("      <<== 聊天记录在可移动盘/网络盘上，这是该报错最常见的原因");
            }
            catch (Exception ex) { info.AppendLine("      读盘失败: " + ex.Message); }
            try
            {
                var dir = new DirectoryInfo(nt);
                info.AppendLine("      属性: " + dir.Attributes + "  最后修改: " + dir.LastWriteTime);
                var acl = Directory.GetAccessControl(nt);
                info.AppendLine("      所有者: " + acl.GetOwner(typeof(System.Security.Principal.NTAccount)));
                foreach (System.Security.AccessControl.FileSystemAccessRule r in
                    acl.GetAccessRules(true, false, typeof(System.Security.Principal.NTAccount)))
                {
                    info.AppendLine("      非继承ACL: " + r.IdentityReference + "  " + r.FileSystemRights + "  " + r.AccessControlType);
                }
            }
            catch (Exception ex) { info.AppendLine("      ACL 读取失败: " + ex.Message); }

            string db = Path.Combine(nt, "nt_db");
            if (!Directory.Exists(db)) { info.AppendLine("      (没有 nt_db 目录)"); return; }
            try
            {
                FileInfo[] fs = new DirectoryInfo(db).GetFiles();
                Array.Sort(fs, delegate (FileInfo a, FileInfo b) { return b.Length.CompareTo(a.Length); });
                info.AppendLine("      ---- nt_db 文件（前 12 大）----");
                int n = 0;
                foreach (var f in fs)
                {
                    info.AppendLine("      " + f.Name.PadRight(34) + f.Length.ToString("N0").PadLeft(15) + " 字节  " + f.LastWriteTime);
                    if (++n >= 12) break;
                }
                foreach (var f in fs)
                {
                    if (f.Name.EndsWith("-wal") || f.Name.EndsWith("-shm") || f.Name.EndsWith("-journal"))
                        info.AppendLine("      辅助文件: " + f.Name.PadRight(30) + f.Length.ToString("N0").PadLeft(12) + " 字节  " + f.LastWriteTime);
                }
            }
            catch (Exception ex) { info.AppendLine("      列文件失败: " + ex.Message); }

            string main = Path.Combine(db, "nt_msg.db");
            if (File.Exists(main))
            {
                info.AppendLine("      ---- 谁正在占用 nt_msg.db ----");
                info.AppendLine(WhoLocks(main));
            }
        }

        static List<string> DataDirs()
        {
            var res = new List<string>();
            var bases = new List<string>();
            bases.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tencent Files"));
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                bases.Add(Path.Combine(d.Name, "Tencent Files"));
                bases.Add(Path.Combine(d.Name, @"Documents\Tencent Files"));
            }
            foreach (var b in bases)
            {
                if (!Directory.Exists(b)) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(b); } catch { continue; }
                foreach (var s in subs)
                {
                    string nt = Path.Combine(s, "nt_qq");
                    if (Directory.Exists(nt) && !res.Contains(nt)) res.Add(nt);
                }
            }
            return res;
        }

        // ---------- 诊断包：系统 API ----------

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr proc, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int cls, out int value, int len, out int ret);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        // 命令行读不到时，用“有没有主窗口”判断主进程
        static bool HasMainWindow(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid)) return p.MainWindowHandle != IntPtr.Zero;
            }
            catch { return false; }
        }

        // 装完的 QQ 是不是带着管理员令牌在跑
        static string Elevated(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return "未知(打不开进程)";
            try
            {
                IntPtr t;
                if (!OpenProcessToken(h, 0x0008, out t)) return "未知(读令牌失败)";   // TOKEN_QUERY
                try
                {
                    int value, ret;
                    if (!GetTokenInformation(t, 20, out value, 4, out ret)) return "未知";   // TokenElevation
                    return value != 0 ? "已提升(管理员)" : "普通用户";
                }
                finally { CloseHandle(t); }
            }
            finally { CloseHandle(h); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
        [DllImport("rstrtmgr.dll")]
        static extern int RmRegisterResources(uint handle, uint nFiles, string[] files,
            uint nApps, ref RM_UNIQUE_PROCESS apps, uint nServices, string[] services);
        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint handle, out uint needed, ref uint count,
            [In, Out] RM_PROCESS_INFO[] procs, ref uint reasons);
        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint handle);

        // 谁锁着这个文件（另一个 QQ、NapCat、网盘、杀软都会出现在这里）
        static string WhoLocks(string path)
        {
            var sb = new StringBuilder();
            uint session;
            var key = new StringBuilder(256);
            int r = RmStartSession(out session, 0, key);
            if (r != 0) return "      (RmStartSession 失败 " + r + ")";
            try
            {
                string[] files = new string[] { path };
                RM_UNIQUE_PROCESS dummy = new RM_UNIQUE_PROCESS();
                r = RmRegisterResources(session, 1, files, 0, ref dummy, 0, null);
                if (r != 0) return "      (查询失败 " + r + (r == 5 ? "，需要管理员权限" : "") + ")";
                uint needed = 0, count = 0, reasons = 0;
                RmGetList(session, out needed, ref count, null, ref reasons);
                if (needed == 0) return "      (没有其它进程占用)";
                var arr = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(session, out needed, ref count, arr, ref reasons) == 0)
                {
                    for (int i = 0; i < count; i++)
                        sb.AppendLine("      PID " + arr[i].Process.dwProcessId + "  " + arr[i].strAppName + "  type=" + arr[i].ApplicationType);
                }
                return sb.ToString();
            }
            finally { RmEndSession(session); }
        }

        // ---------- 工具 ----------
        static string FindQQ()
        {
            // 手工指定 QQ 安装目录（诊断/非常规安装时用）：LIULI_QQ_ROOT=C:\xxx\QQNT
            try
            {
                string env = Environment.GetEnvironmentVariable("LIULI_QQ_ROOT");
                if (!string.IsNullOrEmpty(env)) return env.Trim().Trim('"').TrimEnd('\\');
            }
            catch { }
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
            string exe = Path.Combine(root, "QQ.exe");
            // 安装器是提权进程，直接 Process.Start 会让 QQ 继承管理员令牌。
            // 带管理员令牌跑 QQ 时，OneDrive 里的云文件（很多人「文档」被 OneDrive 接管，
            // 聊天记录就在那）会访问失败，QQ 就弹「聊天记录所在文件系统访问异常」；
            // 之后用户再用普通方式启动，文件句柄/权限也不一致。
            // 交给 explorer 代启动，QQ 回到普通权限。
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = true });
                return;
            }
            catch { }
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = true });
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

namespace LiteLoaderInstaller
{
    // ---------- 诊断包用的数据结构 ----------

    internal sealed class DiagCheck
    {
        public string Level = "info";
        public string Text = "";
    }

    internal sealed class DiagProc
    {
        public int Pid;
        public int ParentPid;
        public string Name = "";
        public string Exe = "";
        public string Cmd = "";
        public bool IsMain;
        public bool Extra;
        public DateTime? Started;
        public List<DiagWindow> Windows = new List<DiagWindow>();
    }

    internal sealed class DiagWindow
    {
        public IntPtr Handle;
        public string Cls = "";
        public string Title = "";
        public bool Visible;
        public NativeRect Rect;
        public List<string> Children;
        public string Body = "";
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal sealed class BuiltinPlugin
    {
        public string DirName = "";
        public string Slug = "";
        public string Version = "";
        public readonly Dictionary<string, string> Sha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, long> Len = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> NormSha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, long> NormLen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class TimelineEntry
    {
        public DateTime When;
        public string Source = "";
        public string Text = "";
        public int Priority = 4;   // 0=installer.log 1=liteLoader.log 2=插件日志 3=QQ app_launcher 4=目录/exe
        public int Seq;
        public bool FromFileTime;  // 行里没有时间戳、退回文件 mtime 时标 (mtime)
    }

    internal sealed class DiagContext
    {
        public DateTime StartedAt;
        public string QQRoot;
        public string ProcessSource = "";
        public string ProcessError;
        public readonly List<DiagProc> Procs = new List<DiagProc>();
        public int MainCount;
        public readonly List<DiagCheck> Checks = new List<DiagCheck>();
        public readonly List<string> DisabledPlugins = new List<string>();
        public readonly List<string> ThemeFileIssues = new List<string>();
        public int BuiltinPluginsCompared;
        public int ComparedFileCount;
        public int NewlineOnlyCount;
        public bool LiteLoaderLogFound;
        public int LiteLoaderLogMainFail;
        public int LiteLoaderLogDialog;
        public int LiteLoaderLogFatal;
        public readonly List<string> LiteLoaderLogSamples = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public bool EnablePlugins = true;
        public Dictionary<string, BuiltinPlugin> Builtin;
        public int DialogCount;
        public int ErrorHits;
        public long CollectedLogBytes;
        public int RedactedFiles;
        public int RedactionSkipped;
        public long CapBytesLimit;
        public string PluginDiagPath;
        public DateTime? PluginDiagTime;
        public string PluginDiagSlug = "";
        public int PluginDiagCount;
        public string OutputDir;
        public readonly List<string> OutputNotes = new List<string>();
        public bool CapTruncated;

        public void Add(string level, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var c = new DiagCheck();
            c.Level = level;
            c.Text = text;
            Checks.Add(c);
        }
    }

    // ---------- 极简 JSON 解析 ----------
    // 只用于诊断包：判断 manifest/config 是否 BOM、能否解析，取 version/slug/main/injects 等字段。
    // 严格按 JSON 语法校验（多余尾逗号/注释/非 JSON 一律报错），够用来复现 LiteLoader 的 JSON.parse 失败。
    internal static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("内容为空");
            var p = new Parser();
            p.Text = text;
            p.Pos = 0;
            p.SkipWs();
            object v = p.ReadValue(0);
            p.SkipWs();
            if (p.Pos < p.Text.Length) throw new FormatException("JSON 末尾还有多余内容（位置 " + p.Pos + "）");
            return v;
        }

        sealed class Parser
        {
            public string Text;
            public int Pos;

            public void SkipWs()
            {
                while (Pos < Text.Length)
                {
                    char c = Text[Pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF') Pos++;
                    else break;
                }
            }

            void Expect(string word)
            {
                if (Pos + word.Length > Text.Length || string.Compare(Text, Pos, word, 0, word.Length, StringComparison.Ordinal) != 0)
                    throw new FormatException("非法字面量（位置 " + Pos + "）");
                Pos += word.Length;
            }

            public object ReadValue(int depth)
            {
                if (depth > 64) throw new FormatException("JSON 嵌套过深");
                SkipWs();
                if (Pos >= Text.Length) throw new FormatException("JSON 提前结束");
                char c = Text[Pos];
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return ReadString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new FormatException("非法字符 '" + c + "'（位置 " + Pos + "）");
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                Pos++; // {
                SkipWs();
                if (Pos < Text.Length && Text[Pos] == '}') { Pos++; return map; }
                while (true)
                {
                    SkipWs();
                    if (Pos >= Text.Length || Text[Pos] != '"') throw new FormatException("对象的键必须是字符串（位置 " + Pos + "）");
                    string key = ReadString();
                    SkipWs();
                    if (Pos >= Text.Length || Text[Pos] != ':') throw new FormatException("键后面缺冒号（位置 " + Pos + "）");
                    Pos++;
                    object v = ReadValue(depth + 1);
                    map[key] = v;
                    SkipWs();
                    if (Pos >= Text.Length) throw new FormatException("对象没有闭合（位置 " + Pos + "）");
                    if (Text[Pos] == ',') { Pos++; SkipWs(); if (Pos < Text.Length && Text[Pos] == '}') throw new FormatException("对象末尾多了逗号（位置 " + Pos + "）"); continue; }
                    if (Text[Pos] == '}') { Pos++; return map; }
                    throw new FormatException("对象里期待 , 或 }（位置 " + Pos + "）");
                }
            }

            List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                Pos++; // [
                SkipWs();
                if (Pos < Text.Length && Text[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    object v = ReadValue(depth + 1);
                    list.Add(v);
                    SkipWs();
                    if (Pos >= Text.Length) throw new FormatException("数组没有闭合（位置 " + Pos + "）");
                    if (Text[Pos] == ',') { Pos++; SkipWs(); if (Pos < Text.Length && Text[Pos] == ']') throw new FormatException("数组末尾多了逗号（位置 " + Pos + "）"); continue; }
                    if (Text[Pos] == ']') { Pos++; return list; }
                    throw new FormatException("数组里期待 , 或 ]（位置 " + Pos + "）");
                }
            }

            string ReadString()
            {
                Pos++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= Text.Length) throw new FormatException("字符串没有闭合");
                    char c = Text[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= Text.Length) throw new FormatException("转义不完整");
                    char e = Text[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > Text.Length) throw new FormatException("\\u 转义不完整");
                            int code;
                            if (!int.TryParse(Text.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new FormatException("\\u 转义非法（位置 " + Pos + "）");
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default: throw new FormatException("未知转义 \\" + e);
                    }
                }
            }

            object ReadNumber()
            {
                int start = Pos;
                if (Pos < Text.Length && Text[Pos] == '-') Pos++;
                while (Pos < Text.Length)
                {
                    char c = Text[Pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') Pos++;
                    else break;
                }
                string s = Text.Substring(start, Pos - start);
                long l;
                if (s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0
                    && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                double d;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw new FormatException("数字格式非法: " + s);
            }
        }

        public static Dictionary<string, object> AsObj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> AsArray(object o)
        {
            return o as List<object>;
        }

        public static object Get(Dictionary<string, object> map, string key)
        {
            if (map == null) return null;
            object v;
            return map.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(Dictionary<string, object> map, string key)
        {
            object v = Get(map, key);
            if (v == null) return "";
            if (v is string) return (string)v;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static List<object> Arr(Dictionary<string, object> map, string key)
        {
            return AsArray(Get(map, key));
        }

        public static string BoolText(Dictionary<string, object> map, string key)
        {
            object v = Get(map, key);
            if (v == null) return "(缺失)";
            if (v is bool) return ((bool)v) ? "true" : "false";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }
}
