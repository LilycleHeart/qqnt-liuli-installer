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
        TextBox qqBox;
        CheckBox pluginBox;
        Button installBtn, restoreBtn, browseBtn;
        TextBox logBox;

        static string LLRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteLoaderQQNT"); }
        }
        static string BackupRoot { get { return Path.Combine(LLRoot, "installer-backup"); } }

        public MainForm()
        {
            Text = "QQNT LiteLoader 一键安装";
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lbl = new Label { Text = "QQ 安装位置", Location = new Point(16, 18), AutoSize = true };
            qqBox = new TextBox { Location = new Point(16, 42), Width = 440, Text = FindQQ() ?? "" };
            browseBtn = new Button { Text = "浏览…", Location = new Point(464, 40), Width = 80 };
            browseBtn.Click += delegate
            {
                using (var d = new FolderBrowserDialog { Description = "选择 QQ.exe 所在的文件夹" })
                    if (d.ShowDialog(this) == DialogResult.OK) qqBox.Text = d.SelectedPath;
            };
            pluginBox = new CheckBox { Text = "同时安装琉璃主题插件", Location = new Point(16, 78), AutoSize = true, Checked = true };
            installBtn = new Button { Text = "一键安装", Location = new Point(16, 110), Size = new Size(140, 36) };
            restoreBtn = new Button { Text = "还原", Location = new Point(166, 110), Size = new Size(100, 36) };
            installBtn.Click += delegate { Run(true); };
            restoreBtn.Click += delegate { Run(false); };
            logBox = new TextBox
            {
                Location = new Point(16, 158), Size = new Size(528, 226), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window
            };
            Controls.AddRange(new Control[] { lbl, qqBox, browseBtn, pluginBox, installBtn, restoreBtn, logBox });
            Log("会自动关闭 QQ。安装前会备份原文件，点「还原」可以恢复。");
        }

        void Log(string s)
        {
            if (headlessLog != null) { headlessLog.WriteLine(s); headlessLog.Flush(); return; }
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText(s + "\r\n");
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
            if (!File.Exists(Path.Combine(root, "QQ.exe")) || !Directory.Exists(Path.Combine(root, "versions")))
            {
                MessageBox.Show(this, "这个文件夹里没有 QQ。请选 QQ.exe 所在的文件夹。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            installBtn.Enabled = restoreBtn.Enabled = browseBtn.Enabled = false;
            new Thread(delegate ()
            {
                try
                {
                    if (install) Install(root, plugins); else Restore(root);
                    Log(install ? "安装完成，QQ 已启动。" : "还原完成，QQ 已启动。");
                }
                catch (Exception ex) { Log("失败：" + ex.Message); }
                finally { BeginInvoke(new Action(delegate { installBtn.Enabled = restoreBtn.Enabled = browseBtn.Enabled = true; })); }
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
                foreach (var n in new[] { "QQ.exe", "dbghelp.dll" })
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
            Log("已放入 QQ.exe 和 dbghelp.dll");

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
            foreach (var n in new[] { "QQ.exe", "dbghelp.dll" })
            {
                string src = Path.Combine(bk, n), dst = Path.Combine(root, n);
                if (File.Exists(src)) File.Copy(src, dst, true);
                else if (n == "dbghelp.dll" && File.Exists(dst)) File.Delete(dst);
            }
            string[] apps = File.ReadAllLines(Path.Combine(bk, "apps.txt"));
            for (int i = 0; i < apps.Length; i++)
            {
                string src = Path.Combine(bk, "package." + i + ".json");
                if (apps[i].Trim().Length > 0 && File.Exists(src) && Directory.Exists(apps[i]))
                    File.Copy(src, Path.Combine(apps[i], "package.json"), true);
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
            for (int i = 0; i < 25 && Process.GetProcessesByName("QQ").Length > 0; i++) Thread.Sleep(1000);
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
