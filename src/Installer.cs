// QQNT LiteLoader 一键安装器
// 内置：官方补丁 QQ.exe、dbghelp.dll、LiteLoaderQQNT 本体、琉璃与 QQ 增强插件。
// 编译：见 build.ps1（用 Windows 自带的 .NET Framework csc，无需另装运行库）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyVersion("1.0.8.0")]
[assembly: AssemblyFileVersion("1.0.8.0")]

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
                    if (mode == "/diag")
                    {
                        // 无界面导出诊断包：不要求 QQ 就在装好的位置，装不上时也能用
                        string zip = f.BuildDiag();
                        w.WriteLine("诊断包: " + zip);
                        w.WriteLine("提交 issue: " + BuildIssueUrl(zip));
                        return 0;
                    }
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
                        SetStatus("诊断包已存到桌面", Ok);
                        Log("诊断包：" + zip);
                        Log("去 GitHub 反馈：" + url);
                        bool go = MessageBox.Show(this,
                            "诊断包已存到桌面：" + Environment.NewLine + "    " + Path.GetFileName(zip)
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
            try
            {
                var info = new StringBuilder();
                info.AppendLine("========== 琉璃 QQ · 诊断包 ==========");
                CollectLogs(tmp, info);
                WriteEnvInfo(info);
                File.WriteAllText(Path.Combine(tmp, "environment.txt"), info.ToString(), Utf8);

                string zip = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "QQ诊断包-" + stamp + ".zip");
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(tmp, zip, CompressionLevel.Optimal, false);
                return zip;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
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
        static void CollectLogs(string tmp, StringBuilder info)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            long total = 0;
            //                        目录                                             打包名        单文件MB 最近N天 扩展名                       上限 必须含 递归
            total += CollectOne(tmp, info, Path.Combine(appData, @"QQ\log"),               "QQ-log",       5, 30, new[] { ".log", ".txt" },              50, null, true);
            total += CollectOne(tmp, info, Path.Combine(appData, @"QQ\arks"),              "QQ-arks",      3,  7, new[] { ".log", ".txt", ".json" },       30, null, true);
            total += CollectOne(tmp, info, Path.Combine(appData, @"QQ\Crashpad\reports"),  "QQ-crashpad",  2,  7, new[] { ".txt", ".log" },               10, null, true);
            total += CollectOne(tmp, info, Path.Combine(appData, @"Tencent\Logs"),         "Tencent-Logs", 3,  3, new[] { ".log", ".tlg", ".txt" },       10, "QQ", true);
            // LiteLoader 只看它自己的日志，不碰 data（用户表情）/ plugins / installer-backup
            total += CollectOne(tmp, info, Path.Combine(appData, "LiteLoaderQQNT"),        "LiteLoader",   2,  0, new[] { ".log", ".txt" },              20, null, false);
            info.AppendLine("日志合计约 " + (total / 1024) + " KB");
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
                return true;
            }
            catch { return false; }
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

        static string PatchFlag(string name, string path)
        {
            try { return SameAsRes(name, path) ? "是（安装器内置补丁）" : "否（不是内置补丁）"; }
            catch { return "未知"; }
        }

        static void WriteEnvInfo(StringBuilder info)
        {
            info.AppendLine();
            info.AppendLine("---------- 环境 ----------");
            info.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            info.AppendLine("计算机: " + Environment.MachineName + "   用户: " + Environment.UserName);
            info.AppendLine("系统: " + Environment.OSVersion.VersionString + "   64位进程: " + Environment.Is64BitProcess);
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                        info.AppendLine("Windows: " + k.GetValue("ProductName") + " " + k.GetValue("DisplayVersion")
                            + "  Build " + k.GetValue("CurrentBuild") + "." + k.GetValue("UBR"));
                }
            }
            catch { }
            try { info.AppendLine("安装器版本: " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion); } catch { }
            info.AppendLine("文档目录: " + Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            info.AppendLine();
            info.AppendLine("---------- QQ 安装 ----------");
            string root = null;
            try { root = FindQQ(); } catch { }
            info.AppendLine("安装目录: " + (root == null ? "未找到" : root));
            if (root != null)
            {
                try
                {
                    foreach (var v in Directory.GetDirectories(Path.Combine(root, "versions")))
                    {
                        string vj = Path.Combine(v, "version.json");
                        string ver = "";
                        try { if (File.Exists(vj)) ver = File.ReadAllText(vj).Replace("\r", " ").Replace("\n", " "); } catch { }
                        info.AppendLine("  版本目录: " + Path.GetFileName(v) + "   " + ver);
                    }
                }
                catch (Exception ex) { info.AppendLine("  读 versions 失败: " + ex.Message); }

                foreach (var n in new[] { "QQ.exe", "dbghelp.dll", "winmm.dll" })
                {
                    string p = Path.Combine(root, n);
                    if (!File.Exists(p)) { info.AppendLine("  " + n + ": 不存在"); continue; }
                    var fi = new FileInfo(p);
                    info.AppendLine("  " + n + ": " + fi.Length + " 字节  修改 " + fi.LastWriteTime);
                    info.AppendLine("      SHA256 " + Sha256Of(p) + "   内置补丁=" + PatchFlag(n, p));
                }
            }
            info.AppendLine("原版备份: " + (Directory.Exists(Path.Combine(BackupRoot, "original")) ? Path.Combine(BackupRoot, "original") : "不存在"));

            info.AppendLine();
            info.AppendLine("---------- LiteLoader 插件（判断插件装没装上）----------");
            try
            {
                string plugins = Path.Combine(LLRoot, "plugins");
                if (!Directory.Exists(plugins))
                {
                    info.AppendLine("  plugins 目录不存在：" + plugins + "   <<== 插件没装");
                }
                else
                {
                    string[] pluginDirs = Directory.GetDirectories(plugins);
                    info.AppendLine("  plugins 目录: " + plugins + "，共 " + pluginDirs.Length + " 个插件");
                    foreach (var d in pluginDirs)
                    {
                        string name = Path.GetFileName(d);
                        string mf = Path.Combine(d, "manifest.json");
                        info.AppendLine("  [" + name + "]");
                        if (!File.Exists(mf)) { info.AppendLine("      缺 manifest.json   <<== 这个插件不会被加载"); continue; }
                        bool bom = false;
                        try
                        {
                            byte[] head = new byte[3];
                            using (var fs = File.OpenRead(mf)) fs.Read(head, 0, 3);
                            bom = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
                        }
                        catch { }
                        info.AppendLine("      manifest.json " + new FileInfo(mf).Length + " 字节  UTF-8 BOM=" +
                            (bom ? "有   <<== LiteLoader 解析会失败" : "无"));
                        try
                        {
                            string json = File.ReadAllText(mf).Replace("\r", " ").Replace("\n", " ");
                            if (json.Length > 400) json = json.Substring(0, 400) + "...";
                            info.AppendLine("      " + json);
                        }
                        catch (Exception ex2) { info.AppendLine("      读 manifest 失败: " + ex2.Message); }
                    }
                    // 插件是否被禁用（LiteLoader 用 .disabled 之类的标记）
                    foreach (var f in Directory.GetFiles(plugins))
                        info.AppendLine("  其它文件: " + Path.GetFileName(f));
                }
            }
            catch (Exception ex) { info.AppendLine("  读取插件目录失败: " + ex.Message); }

            string log = Path.Combine(LLRoot, "installer.log");
            if (File.Exists(log))
            {
                info.AppendLine();
                info.AppendLine("---------- 安装器日志（尾部 60 行）----------");
                try
                {
                    var lines = new List<string>();
                    using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Utf8))
                    {
                        string ln;
                        while ((ln = sr.ReadLine()) != null) lines.Add(ln);
                    }
                    int from = Math.Max(0, lines.Count - 60);
                    if (lines.Count == 0) info.AppendLine("  (日志为空)");
                    for (int i = from; i < lines.Count; i++) info.AppendLine("  " + lines[i]);
                }
                catch (Exception ex) { info.AppendLine("  读取失败: " + ex.Message); }
            }

            info.AppendLine();
            info.AppendLine("---------- QQ 进程（多个主进程会抢同一份聊天记录库）----------");
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process WHERE Name='QQ.exe' OR Name='QQEX.exe'");
                int mains = 0;
                foreach (System.Management.ManagementObject o in searcher.Get())
                {
                    int pid = Convert.ToInt32(o["ProcessId"]);
                    string nm = Convert.ToString(o["Name"]);
                    string cmd = Convert.ToString(o["CommandLine"]);
                    string exe = Convert.ToString(o["ExecutablePath"]);
                    // 子进程（--type=xxx）不算主进程；命令行读不到时用“有没有主窗口”兜底
                    bool isMain = string.IsNullOrEmpty(cmd) ? HasMainWindow(pid) : cmd.IndexOf("--type=") < 0;
                    if (isMain) mains++;
                    info.AppendLine("  PID " + pid + "  " + nm + "  权限: " + Elevated(pid) + (isMain ? "  [主进程]" : ""));
                    info.AppendLine("      exe: " + exe);
                    if (!string.IsNullOrEmpty(cmd))
                        info.AppendLine("      cmd: " + (cmd.Length > 300 ? cmd.Substring(0, 300) + "..." : cmd));
                }
                info.AppendLine("  QQ 主进程数: " + mains + (mains > 1 ? "  <<== 超过 1 个，双实例抢库会直接弹此报错" : ""));
            }
            catch (Exception ex) { info.AppendLine("  查询进程失败（可能没有管理员权限）: " + ex.Message); }

            info.AppendLine();
            info.AppendLine("---------- 聊天记录目录（只列元数据，不含内容）----------");
            var dirs = DataDirs();
            if (dirs.Count == 0) info.AppendLine("  没找到 nt_qq 数据目录");
            foreach (var nt in dirs) DescribeDataDir(info, nt);

            info.AppendLine();
            info.AppendLine("---------- 磁盘 ----------");
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) { info.AppendLine("  " + d.Name + "  " + d.DriveType + "  (未就绪)"); continue; }
                    info.AppendLine("  " + d.Name + "  " + d.DriveType + "  " + d.DriveFormat
                        + "  剩余 " + (d.AvailableFreeSpace / 1024 / 1024) + " MB / 共 " + (d.TotalSize / 1024 / 1024) + " MB");
                }
                catch { }
            }

            info.AppendLine();
            info.AppendLine("---------- 受控文件夹访问（会拦被替换过的 QQ.exe 写“文档”）----------");
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access"))
                {
                    object v = k == null ? null : k.GetValue("EnableControlledFolderAccess");
                    info.AppendLine("  EnableControlledFolderAccess = " + (v == null ? "(未设置)" : v.ToString()) + "   (0=关 1=开 2=仅审计)");
                }
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access\ProtectedFolders"))
                {
                    if (k != null) foreach (var s in k.GetSubKeyNames()) info.AppendLine("  受保护文件夹: " + s);
                }
            }
            catch (Exception ex) { info.AppendLine("  读取失败: " + ex.Message); }

            info.AppendLine();
            info.AppendLine("---------- Defender 拦截事件（1123/1124/1127/1128）----------");
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
                        info.AppendLine("  " + e.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss") + "  事件 " + id);
                        string[] lines = (e.Message == null ? "" : e.Message).Split('\n');
                        for (int j = 0; j < Math.Min(lines.Length, 6); j++) info.AppendLine("      " + lines[j].TrimEnd('\r'));
                    }
                    if (shown == 0) info.AppendLine("  无记录");
                }
            }
            catch (Exception ex) { info.AppendLine("  读取失败（常见于没有管理员权限或没启用 Defender）: " + ex.Message); }

            info.AppendLine();
            info.AppendLine("---------- 最近 3 天 Application 错误事件 ----------");
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
                        // 只看和 QQ 有关的崩溃，避免 Steam/游戏等无关条目
                        if (e.Message == null || e.Message.IndexOf("QQ", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        shown++;
                        info.AppendLine("  " + e.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Source + "  " + id);
                        string[] lines = (e.Message == null ? "" : e.Message).Split('\n');
                        for (int j = 0; j < Math.Min(lines.Length, 4); j++) info.AppendLine("      " + lines[j].TrimEnd('\r'));
                    }
                    if (shown == 0) info.AppendLine("  无记录");
                }
            }
            catch (Exception ex) { info.AppendLine("  读取失败: " + ex.Message); }
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
