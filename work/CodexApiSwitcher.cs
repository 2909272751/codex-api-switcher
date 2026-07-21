using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexApiSwitcher
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    return RunCommand(args);
                }

                IntPtr console = GetConsoleWindow();
                if (console != IntPtr.Zero)
                {
                    ShowWindow(console, 0);
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }

                MessageBox.Show(ex.Message, "Codex API 切换器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunCommand(string[] args)
        {
            Dictionary<string, string> options = ParseArgs(args);
            string root = GetOption(options, "--root", GetDefaultRoot());
            SwitcherService service = new SwitcherService(root, Application.ExecutablePath);

            if (options.ContainsKey("--emit-token"))
            {
                Console.Out.Write(service.ReadToken());
                return 0;
            }

            if (options.ContainsKey("--status"))
            {
                Console.WriteLine(service.GetStatus().ToDisplayString());
                return 0;
            }

            if (options.ContainsKey("--switch-third-party"))
            {
                string url = RequireOption(options, "--url");
                string model = RequireOption(options, "--model");
                string key = RequireOption(options, "--key");
                service.SwitchToThirdParty(url, model, key);
                Console.WriteLine("Switched to third-party Responses API.");
                return 0;
            }

            if (options.ContainsKey("--switch-official"))
            {
                string model = GetOption(options, "--model", string.Empty);
                service.SwitchToOfficial(model);
                Console.WriteLine("Switched to official OpenAI login.");
                return 0;
            }

            if (options.ContainsKey("--rollback"))
            {
                service.Rollback();
                Console.WriteLine("Restored the latest backup.");
                return 0;
            }

            if (options.ContainsKey("--reset-config"))
            {
                string model = GetOption(options, "--model", string.Empty);
                service.ResetModelConfiguration(model);
                Console.WriteLine("Rebuilt the model configuration for official OpenAI login.");
                return 0;
            }

            if (options.ContainsKey("--repair-sidebar"))
            {
                Console.WriteLine(service.RepairConversationIndex());
                return 0;
            }

            if (options.ContainsKey("--test-provider"))
            {
                string url = RequireOption(options, "--url");
                string model = RequireOption(options, "--model");
                string key = GetOption(options, "--key", string.Empty);
                Console.WriteLine(service.TestProvider(url, model, key));
                return 0;
            }

            if (options.ContainsKey("--list-models"))
            {
                string url = RequireOption(options, "--url");
                string key = GetOption(options, "--key", string.Empty);
                foreach (string model in service.ListModels(url, key)) Console.WriteLine(model);
                return 0;
            }

            throw new InvalidOperationException("Unknown command.");
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                string value = "true";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                result[key] = value;
            }
            return result;
        }

        private static string RequireOption(Dictionary<string, string> options, string key)
        {
            string value;
            if (!options.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Missing required option: " + key);
            }
            return value;
        }

        private static string GetOption(Dictionary<string, string> options, string key, string fallback)
        {
            string value;
            return options.TryGetValue(key, out value) ? value : fallback;
        }

        internal static string GetDefaultRoot()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox rootBox = new TextBox();
        private readonly TextBox urlBox = new TextBox();
        private readonly TextBox thirdPartyModelBox = new TextBox();
        private readonly TextBox officialModelBox = new TextBox();
        private readonly TextBox keyBox = new TextBox();
        private readonly Label statusLabel = new Label();
        private readonly Label keyStateLabel = new Label();
        private readonly Label backupLocationLabel = new Label();
        private readonly Label watermarkLabel = new Label();
        private readonly Button officialButton = new Button();
        private readonly Button thirdPartyButton = new Button();
        private readonly Button rollbackButton = new Button();
        private readonly Button repairButton = new Button();
        private readonly Button resetConfigButton = new Button();
        private readonly Button testProviderButton = new Button();
        private readonly Button listModelsButton = new Button();
        private readonly CheckBox preflightCheckBox = new CheckBox();

        internal MainForm()
        {
            Text = "Codex API 切换器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(690, 640);
            MinimumSize = new Size(706, 679);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(247, 248, 250);

            BuildUi();
            rootBox.Text = Program.GetDefaultRoot();
            LoadRootSettings();
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "Codex API 切换器";
            title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(28, 22);
            Controls.Add(title);

            Label safety = new Label();
            safety.Text = "每次切换都会备份状态数据库并同步历史会话；不会修改会话内容、记忆文件或 auth.json。";
            safety.ForeColor = Color.FromArgb(53, 94, 59);
            safety.BackColor = Color.FromArgb(232, 244, 234);
            safety.BorderStyle = BorderStyle.FixedSingle;
            safety.Location = new Point(30, 64);
            safety.Size = new Size(630, 38);
            safety.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(safety);

            AddLabel("Codex 根目录", 30, 122);
            rootBox.Location = new Point(160, 117);
            rootBox.Size = new Size(405, 27);
            rootBox.Leave += delegate { LoadRootSettings(); };
            Controls.Add(rootBox);

            Button browseButton = new Button();
            browseButton.Text = "选择...";
            browseButton.Location = new Point(575, 116);
            browseButton.Size = new Size(85, 30);
            browseButton.Click += BrowseRoot;
            Controls.Add(browseButton);

            statusLabel.Location = new Point(30, 158);
            statusLabel.Size = new Size(630, 38);
            statusLabel.BackColor = Color.White;
            statusLabel.BorderStyle = BorderStyle.FixedSingle;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(statusLabel);

            AddLabel("第三方 Base URL", 30, 220);
            urlBox.Location = new Point(160, 215);
            urlBox.Size = new Size(500, 27);
            Controls.Add(urlBox);

            AddLabel("第三方模型", 30, 260);
            thirdPartyModelBox.Location = new Point(160, 255);
            thirdPartyModelBox.Size = new Size(210, 27);
            Controls.Add(thirdPartyModelBox);

            AddLabel("官方模型", 390, 260, 75);
            officialModelBox.Location = new Point(475, 255);
            officialModelBox.Size = new Size(185, 27);
            Controls.Add(officialModelBox);

            AddLabel("第三方 API Key", 30, 300);
            keyBox.Location = new Point(160, 295);
            keyBox.Size = new Size(410, 27);
            keyBox.UseSystemPasswordChar = true;
            Controls.Add(keyBox);

            CheckBox showKey = new CheckBox();
            showKey.Text = "显示";
            showKey.Location = new Point(580, 297);
            showKey.Size = new Size(70, 24);
            showKey.CheckedChanged += delegate { keyBox.UseSystemPasswordChar = !showKey.Checked; };
            Controls.Add(showKey);

            keyStateLabel.Location = new Point(160, 326);
            keyStateLabel.AutoSize = true;
            keyStateLabel.ForeColor = Color.DimGray;
            Controls.Add(keyStateLabel);

            preflightCheckBox.Text = "切换前自动预检";
            preflightCheckBox.Checked = true;
            preflightCheckBox.Location = new Point(160, 333);
            preflightCheckBox.Size = new Size(150, 24);
            Controls.Add(preflightCheckBox);

            testProviderButton.Text = "测试接口";
            testProviderButton.Location = new Point(470, 330);
            testProviderButton.Size = new Size(90, 30);
            testProviderButton.Click += TestProvider;
            Controls.Add(testProviderButton);

            listModelsButton.Text = "读取模型";
            listModelsButton.Location = new Point(570, 330);
            listModelsButton.Size = new Size(90, 30);
            listModelsButton.Click += ListModels;
            Controls.Add(listModelsButton);

            thirdPartyButton.Text = "切换到第三方 API";
            thirdPartyButton.Location = new Point(30, 372);
            thirdPartyButton.Size = new Size(195, 46);
            thirdPartyButton.BackColor = Color.FromArgb(27, 99, 214);
            thirdPartyButton.ForeColor = Color.White;
            thirdPartyButton.FlatStyle = FlatStyle.Flat;
            thirdPartyButton.Click += SwitchThirdParty;
            Controls.Add(thirdPartyButton);

            officialButton.Text = "切换到官方登录";
            officialButton.Location = new Point(245, 372);
            officialButton.Size = new Size(195, 46);
            officialButton.BackColor = Color.FromArgb(35, 43, 54);
            officialButton.ForeColor = Color.White;
            officialButton.FlatStyle = FlatStyle.Flat;
            officialButton.Click += SwitchOfficial;
            Controls.Add(officialButton);

            rollbackButton.Text = "恢复最近备份";
            rollbackButton.Location = new Point(460, 372);
            rollbackButton.Size = new Size(200, 46);
            rollbackButton.Click += Rollback;
            Controls.Add(rollbackButton);

            repairButton.Text = "修复会话列表";
            repairButton.Location = new Point(30, 438);
            repairButton.Size = new Size(195, 42);
            repairButton.Click += RepairSidebar;
            Controls.Add(repairButton);

            resetConfigButton.Text = "一键恢复基础配置";
            resetConfigButton.Location = new Point(245, 438);
            resetConfigButton.Size = new Size(195, 42);
            resetConfigButton.Click += ResetConfig;
            Controls.Add(resetConfigButton);

            Label maintenanceNote = new Label();
            maintenanceNote.Text = "修复会话列表用于历史缺失；恢复基础配置用于 model/provider 配置损坏。";
            maintenanceNote.Location = new Point(460, 438);
            maintenanceNote.Size = new Size(200, 48);
            maintenanceNote.ForeColor = Color.FromArgb(120, 76, 20);
            Controls.Add(maintenanceNote);

            Label footer = new Label();
            footer.Text = "切换完成后，请彻底退出并重新打开 Codex。每次切换都会自动备份原配置。";
            footer.Location = new Point(30, 510);
            footer.Size = new Size(630, 45);
            footer.ForeColor = Color.FromArgb(90, 90, 90);
            Controls.Add(footer);

            backupLocationLabel.Location = new Point(30, 555);
            backupLocationLabel.Size = new Size(630, 32);
            backupLocationLabel.ForeColor = Color.FromArgb(70, 70, 70);
            backupLocationLabel.AutoEllipsis = true;
            Controls.Add(backupLocationLabel);

            watermarkLabel.Text = "github.com/yin-yizhen/codex-api-switcher";
            watermarkLabel.Location = new Point(300, 595);
            watermarkLabel.Size = new Size(360, 24);
            watermarkLabel.ForeColor = Color.FromArgb(145, 145, 145);
            watermarkLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(watermarkLabel);
        }

        private void AddLabel(string text, int x, int y)
        {
            AddLabel(text, x, y, 125);
        }

        private void AddLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 24);
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);
        }

        private void BrowseRoot(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含 config.toml 的 Codex 根目录";
                dialog.SelectedPath = Directory.Exists(rootBox.Text) ? rootBox.Text : Program.GetDefaultRoot();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    rootBox.Text = dialog.SelectedPath;
                    LoadRootSettings();
                }
            }
        }

        private SwitcherService GetService()
        {
            return new SwitcherService(rootBox.Text.Trim(), Application.ExecutablePath);
        }

        private void LoadRootSettings()
        {
            string selectedRoot = rootBox.Text.Trim();
            backupLocationLabel.Text = "备份位置：" +
                Path.Combine(selectedRoot, "config-switcher-backups") + "；" +
                Path.Combine(selectedRoot, "history_sync_backups");

            try
            {
                SwitcherService service = GetService();
                ProviderStatus status = service.GetStatus();
                StoredSettings settings = service.LoadSettings();

                urlBox.Text = !string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? settings.BaseUrl
                    : (!string.IsNullOrWhiteSpace(status.BaseUrl) ? status.BaseUrl : "https://api.example.com");
                thirdPartyModelBox.Text = !string.IsNullOrWhiteSpace(settings.ThirdPartyModel)
                    ? settings.ThirdPartyModel
                    : (!string.IsNullOrWhiteSpace(status.Model) ? status.Model : "gpt-5.5");
                officialModelBox.Text = !string.IsNullOrWhiteSpace(settings.OfficialModel)
                    ? settings.OfficialModel
                    : "gpt-5.5";
                keyBox.Text = string.Empty;
                keyStateLabel.Text = service.HasStoredToken()
                    ? "已保存加密 Key；留空即可继续使用。"
                    : "尚未保存 Key。首次切换第三方时必须填写。";
                RenderStatus(status);
                SetButtonsEnabled(true);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "无法读取配置：" + ex.Message;
                statusLabel.ForeColor = Color.Firebrick;
                keyStateLabel.Text = string.Empty;
                SetButtonsEnabled(false);
            }
        }

        private void RenderStatus(ProviderStatus status)
        {
            statusLabel.ForeColor = status.IsThirdParty ? Color.FromArgb(24, 90, 180) : Color.FromArgb(35, 43, 54);
            statusLabel.Text = "当前状态：" + status.ToDisplayString();
        }

        private void SetButtonsEnabled(bool enabled)
        {
            officialButton.Enabled = enabled;
            thirdPartyButton.Enabled = enabled;
            rollbackButton.Enabled = enabled;
            repairButton.Enabled = enabled;
            resetConfigButton.Enabled = enabled;
            testProviderButton.Enabled = enabled;
            listModelsButton.Enabled = enabled;
        }

        private void TestProvider(object sender, EventArgs e)
        {
            RunAction(delegate
            {
                string result = GetService().TestProvider(urlBox.Text, thirdPartyModelBox.Text, keyBox.Text);
                MessageBox.Show(result, "接口测试通过", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private void ListModels(object sender, EventArgs e)
        {
            RunAction(delegate
            {
                List<string> models = GetService().ListModels(urlBox.Text, keyBox.Text);
                if (models.Count == 0) throw new InvalidOperationException("接口返回了空模型列表。");
                using (Form picker = new Form())
                using (ListBox list = new ListBox())
                using (Button select = new Button())
                {
                    picker.Text = "选择第三方模型";
                    picker.StartPosition = FormStartPosition.CenterParent;
                    picker.ClientSize = new Size(460, 380);
                    list.Dock = DockStyle.Top;
                    list.Height = 330;
                    list.Items.AddRange(models.Cast<object>().ToArray());
                    select.Text = "使用所选模型";
                    select.Dock = DockStyle.Bottom;
                    select.Height = 38;
                    select.DialogResult = DialogResult.OK;
                    picker.Controls.Add(list);
                    picker.Controls.Add(select);
                    picker.AcceptButton = select;
                    if (picker.ShowDialog(this) == DialogResult.OK && list.SelectedItem != null)
                    {
                        thirdPartyModelBox.Text = Convert.ToString(list.SelectedItem);
                    }
                }
            });
        }

        private void SwitchThirdParty(object sender, EventArgs e)
        {
            RunAction(delegate
            {
                string url = urlBox.Text.Trim().TrimEnd('/');
                string model = thirdPartyModelBox.Text.Trim();
                string key = keyBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(model))
                {
                    throw new InvalidOperationException("请填写第三方 Base URL 和模型名称。");
                }

                SwitcherService service = GetService();
                if (string.IsNullOrWhiteSpace(key) && !service.HasStoredToken())
                {
                    throw new InvalidOperationException("首次切换第三方 API 时必须填写 API Key。");
                }

                Uri providerUri;
                if (Uri.TryCreate(url, UriKind.Absolute, out providerUri) &&
                    string.Equals(providerUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    !providerUri.IsLoopback)
                {
                    DialogResult insecure = MessageBox.Show(
                        "该远程 API 使用明文 HTTP。API Key、代码和聊天内容可能被网络中的第三方读取或篡改。\n\n仍要继续吗？",
                        "不安全的远程 HTTP 地址",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (insecure != DialogResult.Yes) return;
                }

                if (preflightCheckBox.Checked)
                {
                    string probe = service.TestProvider(url, model, key);
                    if (probe.IndexOf("WARNING:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        DialogResult continueSwitch = MessageBox.Show(
                            probe + "\n\n普通 Responses 请求可用，但长会话压缩可能失败。仍要切换吗？",
                            "兼容性预检警告",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (continueSwitch != DialogResult.Yes) return;
                    }
                }

                service.SwitchToThirdParty(url, model, key);
                MessageBox.Show(
                    "已切换到第三方 Responses API，并已同步历史会话。\n\n请重新打开 Codex。",
                    "切换完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void SwitchOfficial(object sender, EventArgs e)
        {
            RunAction(delegate
            {
                string model = officialModelBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(model))
                {
                    throw new InvalidOperationException("请填写官方模型名称。");
                }

                GetService().SwitchToOfficial(model);
                MessageBox.Show(
                    "已切换到官方 OpenAI 登录，并已同步历史会话。\n\n第三方 Key 仍以加密形式保留，auth.json 未被修改。\n请重新打开 Codex。",
                    "切换完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void Rollback(object sender, EventArgs e)
        {
            RunAction(delegate
            {
                GetService().Rollback();
                MessageBox.Show(
                    "已恢复最近一次备份。\n\n请彻底退出并重新打开 Codex。",
                    "恢复完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void RepairSidebar(object sender, EventArgs e)
        {
            DialogResult confirmed = MessageBox.Show(
                "此操作不是普通 API 切换。\n\n它会自动识别并备份新旧状态数据库，然后恢复顶层用户会话的可见标记。不会改动会话 JSONL、记忆文件或 auth.json。\n\n请先彻底退出 Codex，再点击“是”。",
                "确认修复会话列表",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            RunAction(delegate
            {
                string result = GetService().RepairConversationIndex();
                MessageBox.Show(
                    result + "\n\n请重新打开 Codex 检查侧栏。",
                    "修复完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void ResetConfig(object sender, EventArgs e)
        {
            string model = officialModelBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "gpt-5.5";
            }

            DialogResult confirmed = MessageBox.Show(
                "将先完整备份 config.toml，然后重建官方模型配置。\n\n" +
                "会恢复 model_provider 和 model，移除损坏的 custom provider 段；MCP、插件、沙箱、记忆、会话和 auth.json 均保持不变。\n\n" +
                "确定继续吗？",
                "确认恢复基础配置",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            RunAction(delegate
            {
                GetService().ResetModelConfiguration(model);
                MessageBox.Show(
                    "基础模型配置已恢复为官方 OpenAI 登录。\n\n原 config.toml 已备份，MCP 和其他无关配置已保留。请重新打开 Codex。",
                    "恢复完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void RunAction(Action action)
        {
            try
            {
                SetButtonsEnabled(false);
                action();
                LoadRootSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetButtonsEnabled(true);
            }
        }
    }

    internal sealed class SwitcherService
    {
        private const string ProviderId = "custom";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexApiSwitcher-v1");

        private readonly string root;
        private readonly string executablePath;
        private readonly string configPath;
        private readonly string dataDirectory;
        private readonly string credentialPath;
        private readonly string settingsPath;
        private readonly string backupDirectory;
        private readonly string transactionDirectory;
        private readonly string helperPath;

        internal SwitcherService(string rootPath, string exePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new InvalidOperationException("请选择 Codex 根目录。");
            }

            root = Path.GetFullPath(rootPath);
            executablePath = Path.GetFullPath(exePath);
            configPath = Path.Combine(root, "config.toml");
            dataDirectory = Path.Combine(root, "api-switcher");
            credentialPath = Path.Combine(dataDirectory, "credential.dat");
            settingsPath = Path.Combine(dataDirectory, "settings.dat");
            backupDirectory = Path.Combine(root, "config-switcher-backups");
            transactionDirectory = Path.Combine(root, "api-switcher", "transactions");
            helperPath = Path.Combine(root, "api-switcher", "bin", "CodexApiCredentialHelper.exe");
        }

        internal ProviderStatus GetStatus()
        {
            List<string> lines = ReadConfig();
            string provider = GetTopLevelValue(lines, "model_provider");
            string model = GetTopLevelValue(lines, "model");
            string section = "model_providers." + ProviderId;
            string url = GetSectionValue(lines, section, "base_url");
            bool helperAuth = SectionExists(lines, section + ".auth");
            bool reusedLogin = string.Equals(
                GetSectionValue(lines, section, "requires_openai_auth"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            return new ProviderStatus(provider, model, url, helperAuth, reusedLogin);
        }

        internal StoredSettings LoadSettings()
        {
            if (!File.Exists(settingsPath))
            {
                return new StoredSettings();
            }

            string[] lines = File.ReadAllLines(settingsPath, Encoding.UTF8);
            StoredSettings settings = new StoredSettings();
            foreach (string line in lines)
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, separator);
                string encoded = line.Substring(separator + 1);
                string value;
                try
                {
                    value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                catch
                {
                    continue;
                }

                if (name == "url") settings.BaseUrl = value;
                if (name == "thirdModel") settings.ThirdPartyModel = value;
                if (name == "officialModel") settings.OfficialModel = value;
            }
            return settings;
        }

        internal bool HasStoredToken()
        {
            return File.Exists(credentialPath) && new FileInfo(credentialPath).Length > 0;
        }

        internal string ReadToken()
        {
            if (!HasStoredToken())
            {
                throw new InvalidOperationException("No encrypted third-party API key is stored.");
            }

            byte[] encrypted = File.ReadAllBytes(credentialPath);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        internal List<string> ListModels(string url, string key)
        {
            string token = string.IsNullOrWhiteSpace(key) ? ReadToken() : key.Trim();
            string baseUrl = NormalizeBaseUrl(url);
            using (HttpClient client = CreateApiClient(token))
            {
                HttpResponseMessage response = client.GetAsync(baseUrl + "/models").GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Model discovery failed (HTTP " + (int)response.StatusCode + "): " + RedactError(body));
                }
                Dictionary<string, object> envelope = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                object dataValue;
                IEnumerable data = envelope.TryGetValue("data", out dataValue) ? dataValue as IEnumerable : null;
                if (data == null) return new List<string>();
                return data.Cast<object>().Select(item => item as Dictionary<string, object>)
                    .Where(item => item != null && item.ContainsKey("id"))
                    .Select(item => Convert.ToString(item["id"]))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        internal string TestProvider(string url, string model, string key)
        {
            string token = string.IsNullOrWhiteSpace(key) ? ReadToken() : key.Trim();
            string baseUrl = NormalizeBaseUrl(url);
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["model"] = model.Trim();
            payload["input"] = "Reply with OK.";
            payload["stream"] = true;
            payload["tools"] = new object[]
            {
                new Dictionary<string, object>
                {
                    { "type", "function" },
                    { "name", "switcher_probe" },
                    { "description", "Compatibility probe. Do not call unless needed." },
                    { "parameters", new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } } }
                }
            };
            string json = new JavaScriptSerializer().Serialize(payload);
            using (HttpClient client = CreateApiClient(token))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/responses"))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                string mediaType = response.Content.Headers.ContentType == null ? string.Empty : response.Content.Headers.ContentType.MediaType;
                string sample = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Responses API check failed (HTTP " + (int)response.StatusCode + "): " + RedactError(sample));
                }
                bool eventStream = mediaType.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0 || sample.IndexOf("data:", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!eventStream)
                {
                    throw new InvalidOperationException("The endpoint responded, but did not return a Responses API event stream.");
                }
                string compactStatus = TestCompact(client, baseUrl, model.Trim());
                return "PASS: authentication, model, Responses API, SSE streaming, and function-tool schema are available. " + compactStatus;
            }
        }

        private static string TestCompact(HttpClient client, string baseUrl, string model)
        {
            Dictionary<string, object> userContent = new Dictionary<string, object>();
            userContent["type"] = "input_text";
            userContent["text"] = "Compatibility probe.";
            Dictionary<string, object> userItem = new Dictionary<string, object>();
            userItem["type"] = "message";
            userItem["role"] = "user";
            userItem["content"] = new object[] { userContent };
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["model"] = model;
            payload["input"] = new object[] { userItem };
            payload["instructions"] = "Compact the supplied test input.";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/responses/compact"))
            {
                request.Content = new StringContent(new JavaScriptSerializer().Serialize(payload), Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return response.IsSuccessStatusCode
                    ? "Compact endpoint is available."
                    : "WARNING: /responses/compact returned HTTP " + (int)response.StatusCode + " (long conversations may fail): " + RedactError(body);
            }
        }

        private static HttpClient CreateApiClient(string token)
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            return client;
        }

        private static string RedactError(string value)
        {
            string clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            return clean.Length <= 500 ? clean : clean.Substring(0, 500) + "...";
        }

        internal void SwitchToThirdParty(string url, string model, string key)
        {
            AssertConfig();
            string cleanUrl = NormalizeBaseUrl(url);
            string cleanModel = (model ?? string.Empty).Trim();
            if (cleanUrl.Length == 0 || cleanModel.Length == 0)
            {
                throw new InvalidOperationException("Base URL and model are required.");
            }
            if (!Uri.IsWellFormedUriString(cleanUrl, UriKind.Absolute))
            {
                throw new InvalidOperationException("Base URL is not a valid absolute URL.");
            }

            Directory.CreateDirectory(dataDirectory);
            if (!string.IsNullOrWhiteSpace(key))
            {
                SaveToken(key.Trim());
            }
            else if (!HasStoredToken())
            {
                throw new InvalidOperationException("An API key is required for the first third-party switch.");
            }

            AssertCodexStoppedAndDatabasesAvailable();
            List<string> lines = ReadConfig();
            string currentProvider = GetTopLevelValue(lines, "model_provider");
            string currentModel = GetTopLevelValue(lines, "model");
            SwitchTransaction transaction = SwitchTransaction.Begin(root, configPath, GetStateDatabasePaths(), currentProvider, ProviderId);
            try
            {
                BackupConfig();
                EnsureCredentialHelperInstalled();
                SynchronizeConversationProvider(ProviderId);
                SetTopLevelValue(lines, "model_provider", ProviderId);
                SetTopLevelValue(lines, "model", cleanModel);
                RemoveProviderSections(lines);
                AddProviderSections(lines, cleanUrl);
                WriteConfigAtomically(lines);
                transaction.Complete();
            }
            catch
            {
                transaction.Restore();
                throw;
            }

            StoredSettings settings = LoadSettings();
            settings.BaseUrl = cleanUrl;
            settings.ThirdPartyModel = cleanModel;
            if (string.Equals(currentProvider, "openai", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(currentModel))
            {
                settings.OfficialModel = currentModel;
            }
            else if (string.IsNullOrWhiteSpace(settings.OfficialModel))
            {
                settings.OfficialModel = currentModel;
            }
            SaveSettings(settings);
        }

        internal string RepairConversationIndex()
        {
            AssertConfig();
            List<string> statePaths = GetStateDatabasePaths();
            if (statePaths.Count == 0)
            {
                throw new FileNotFoundException(
                    "state_5.sqlite was not found in the Codex root or sqlite subdirectory.");
            }

            if (IsRealCodexRoot() && Process.GetProcessesByName("Codex").Length > 0)
            {
                throw new InvalidOperationException("Codex 仍在运行。请彻底退出 Codex 后再执行会话列表修复。");
            }

            int restored = 0;
            List<string> backupPaths = new List<string>();
            foreach (string statePath in statePaths)
            {
                backupPaths.Add(CreateStateBackup(statePath, "pre-sidebar-repair"));
                using (NativeSqlite database = NativeSqlite.Open(statePath))
                {
                    EnsureThreadColumns(database);
                    int before = database.ScalarInt(
                        "select count(*) from threads where has_user_event = 1");
                    database.Execute(
                        "update threads set has_user_event = 1 " +
                        "where has_user_event = 0 and first_user_message <> '' " +
                        "and source in ('vscode','cli')");
                    EnsureIntegrity(database);
                    int after = database.ScalarInt(
                        "select count(*) from threads where has_user_event = 1");
                    restored += after - before;
                }
            }

            return string.Format(
                "已检查 {0} 套状态数据库，恢复 {1} 条会话的侧栏可见标记。备份：{2}",
                statePaths.Count,
                restored,
                string.Join("；", backupPaths.ToArray()));
        }

        internal void SwitchToOfficial(string model)
        {
            AssertConfig();
            string cleanModel = ResolveOfficialModel(model);
            if (cleanModel.Length == 0)
            {
                throw new InvalidOperationException("Official model is required.");
            }

            AssertCodexStoppedAndDatabasesAvailable();
            List<string> lines = ReadConfig();
            SwitchTransaction transaction = SwitchTransaction.Begin(root, configPath, GetStateDatabasePaths(), GetTopLevelValue(lines, "model_provider"), "openai");
            try
            {
                BackupConfig();
                SynchronizeConversationProvider("openai");
                SetTopLevelValue(lines, "model_provider", "openai");
                SetTopLevelValue(lines, "model", cleanModel);
                WriteConfigAtomically(lines);
                transaction.Complete();
            }
            catch
            {
                transaction.Restore();
                throw;
            }

            StoredSettings settings = LoadSettings();
            settings.OfficialModel = cleanModel;
            SaveSettings(settings);
        }

        internal void ResetModelConfiguration(string model)
        {
            AssertConfig();
            string cleanModel = ResolveOfficialModel(model);

            List<string> lines = ReadConfig();
            BackupConfig();
            SetTopLevelValue(lines, "model_provider", "openai");
            SetTopLevelValue(lines, "model", cleanModel);
            RemoveProviderSections(lines);
            WriteConfigAtomically(lines);

            StoredSettings settings = LoadSettings();
            settings.OfficialModel = cleanModel;
            SaveSettings(settings);
        }

        internal void Rollback()
        {
            AssertConfig();
            AssertCodexStoppedAndDatabasesAvailable();
            SwitchTransaction latestTransaction = SwitchTransaction.FindLatestCompleted(transactionDirectory);
            if (latestTransaction != null)
            {
                latestTransaction.Restore();
                return;
            }
            if (!Directory.Exists(backupDirectory))
            {
                throw new InvalidOperationException("No backup directory exists.");
            }

            FileInfo latest = new DirectoryInfo(backupDirectory)
                .GetFiles("config.toml.*.bak")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null)
            {
                throw new InvalidOperationException("No configuration backup was found.");
            }

            BackupConfig();
            File.Copy(latest.FullName, configPath, true);
        }

        private string ResolveOfficialModel(string requested)
        {
            string clean = (requested ?? string.Empty).Trim();
            if (clean.Length > 0) return clean;
            StoredSettings settings = LoadSettings();
            if (!string.IsNullOrWhiteSpace(settings.OfficialModel)) return settings.OfficialModel.Trim();
            List<string> lines = ReadConfig();
            if (string.Equals(GetTopLevelValue(lines, "model_provider"), "openai", StringComparison.OrdinalIgnoreCase))
            {
                clean = GetTopLevelValue(lines, "model").Trim();
            }
            if (clean.Length == 0)
            {
                throw new InvalidOperationException("Official model is unknown. Enter the model name once; it will be remembered for later switches.");
            }
            return clean;
        }

        private void EnsureCredentialHelperInstalled()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(helperPath));
            string sourceHash = Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(executablePath)));
            string targetHash = File.Exists(helperPath)
                ? Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(helperPath)))
                : string.Empty;
            if (!string.Equals(sourceHash, targetHash, StringComparison.Ordinal))
            {
                string temporary = helperPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(executablePath, temporary, true);
                File.Copy(temporary, helperPath, true);
                File.Delete(temporary);
            }
        }

        private void AssertCodexStoppedAndDatabasesAvailable()
        {
            if (IsRealCodexRoot() && Process.GetProcesses().Any(process =>
                process.Id != Process.GetCurrentProcess().Id &&
                (string.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(process.ProcessName, "codex-cli", StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidOperationException("Codex is still running. Exit Codex completely before changing configuration or history.");
            }
            foreach (string path in GetStateDatabasePaths())
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
                }
                catch (IOException)
                {
                    throw new InvalidOperationException("Codex state database is locked: " + path);
                }
            }
        }

        private void SaveToken(string token)
        {
            byte[] plain = Encoding.UTF8.GetBytes(token);
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                WriteBytesAtomically(credentialPath, encrypted);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        private bool IsRealCodexRoot()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            string expected = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return string.Equals(
                Path.GetFullPath(expected).TrimEnd('\\'),
                root.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBaseUrl(string value)
        {
            string clean = (value ?? string.Empty).Trim().TrimEnd('/');
            Uri uri;
            if (!Uri.TryCreate(clean, UriKind.Absolute, out uri))
            {
                throw new InvalidOperationException("Base URL is not a valid absolute URL.");
            }
            string path = uri.AbsolutePath.TrimEnd('/');
            string lower = path.ToLowerInvariant();
            string[] endpointSuffixes =
            {
                "/v1/responses/compact",
                "/v1/chat/completions",
                "/v1/responses"
            };
            foreach (string suffix in endpointSuffixes)
            {
                if (lower.EndsWith(suffix, StringComparison.Ordinal))
                {
                    path = path.Substring(0, path.Length - suffix.Length) + "/v1";
                    lower = path.ToLowerInvariant();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                path = "/v1";
            }

            UriBuilder builder = new UriBuilder(uri);
            builder.Path = path;
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private string SynchronizeConversationProvider(string targetProvider)
        {
            List<string> statePaths = GetStateDatabasePaths();
            if (statePaths.Count == 0)
            {
                return "No state database exists yet; history synchronization was skipped.";
            }

            AssertCodexStoppedAndDatabasesAvailable();

            if (targetProvider != "openai" && targetProvider != ProviderId)
            {
                throw new InvalidOperationException("Unsupported provider: " + targetProvider);
            }

            string provider = targetProvider.Replace("'", "''");
            string where = "first_user_message <> '' and source in ('vscode','cli')";
            List<string> rolloutPaths = new List<string>();
            foreach (string statePath in statePaths)
            {
                using (NativeSqlite database = NativeSqlite.Open(statePath))
                {
                    EnsureThreadColumns(database);
                    rolloutPaths.AddRange(database.QueryTextColumn(
                        "select rollout_path from threads where " + where + " order by rollout_path"));
                }
            }

            SessionMetadataSyncResult metadataResult =
                SynchronizeSessionMetadata(
                    rolloutPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    targetProvider);
            List<string> backupPaths = new List<string>();
            int total = 0;
            int providerChanges = 0;
            int visibilityChanges = 0;
            try
            {
                foreach (string statePath in statePaths)
                {
                    backupPaths.Add(CreateStateBackup(statePath, "pre-provider-sync"));
                    using (NativeSqlite database = NativeSqlite.Open(statePath))
                    {
                        EnsureThreadColumns(database);
                        database.Execute("begin immediate");
                        try
                        {
                            total += database.ScalarInt(
                                "select count(*) from threads where " + where);
                            providerChanges += database.ScalarInt(
                                "select count(*) from threads where " + where +
                                " and model_provider <> '" + provider + "'");
                            visibilityChanges += database.ScalarInt(
                                "select count(*) from threads where " + where +
                                " and has_user_event = 0");
                            database.Execute(
                                "update threads set model_provider = '" + provider +
                                "', has_user_event = 1 where " + where);
                            EnsureIntegrity(database);
                            int remaining = database.ScalarInt(
                                "select count(*) from threads where " + where +
                                " and model_provider <> '" + provider + "'");
                            if (remaining != 0)
                            {
                                throw new InvalidOperationException(
                                    "Provider synchronization incomplete in " + statePath +
                                    ": " + remaining + " rows remain.");
                            }
                            database.Execute("commit");
                        }
                        catch
                        {
                            try { database.Execute("rollback"); } catch { }
                            throw;
                        }
                    }
                }

                return string.Format(
                    "已同步 {0} 套状态数据库中的 {1} 条用户会话到 {2}（数据库 provider 变更 {3} 条，JSONL 元数据变更 {4} 条，可见标记恢复 {5} 条）。数据库备份：{6}；会话备份：{7}",
                    statePaths.Count,
                    total,
                    targetProvider,
                    providerChanges,
                    metadataResult.ChangedCount,
                    visibilityChanges,
                    string.Join("；", backupPaths.ToArray()),
                    metadataResult.BackupDirectory);
            }
            catch
            {
                metadataResult.Rollback();
                throw;
            }
        }

        private List<string> GetStateDatabasePaths()
        {
            string[] candidates =
            {
                Path.Combine(root, "sqlite", "state_5.sqlite"),
                Path.Combine(root, "state_5.sqlite")
            };
            return candidates
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private SessionMetadataSyncResult SynchronizeSessionMetadata(
            List<string> rolloutPaths,
            string targetProvider)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backupRoot = Path.Combine(
                root,
                "history_sync_backups",
                "session-meta-provider-sync-" + stamp);
            List<SessionMetadataChange> changes = new List<SessionMetadataChange>();
            JavaScriptSerializer serializer = new JavaScriptSerializer();

            foreach (string rolloutPath in rolloutPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(rolloutPath))
                {
                    throw new InvalidOperationException("A user conversation has no rollout_path.");
                }

                string fullPath = Path.GetFullPath(RemoveExtendedPathPrefix(rolloutPath));
                string rootPrefix = Path.GetFullPath(RemoveExtendedPathPrefix(root)).TrimEnd('\\') + "\\";
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Conversation file is outside the selected Codex root: " + fullPath);
                }
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("Conversation JSONL was not found.", fullPath);
                }

                byte[] originalBytes = File.ReadAllBytes(fullPath);
                int lineEnd = Array.IndexOf(originalBytes, (byte)'\n');
                int firstLineLength = lineEnd >= 0 ? lineEnd : originalBytes.Length;
                if (firstLineLength > 0 && originalBytes[firstLineLength - 1] == (byte)'\r')
                {
                    firstLineLength--;
                }

                string firstLine = Encoding.UTF8.GetString(originalBytes, 0, firstLineLength);
                Dictionary<string, object> envelope =
                    serializer.Deserialize<Dictionary<string, object>>(firstLine);
                object payloadObject;
                if (!envelope.TryGetValue("payload", out payloadObject))
                {
                    throw new InvalidOperationException(
                        "Conversation JSONL has no payload object: " + fullPath);
                }
                Dictionary<string, object> payload = payloadObject as Dictionary<string, object>;
                if (payload == null)
                {
                    throw new InvalidOperationException(
                        "Conversation JSONL payload is invalid: " + fullPath);
                }

                object existingProvider;
                string existing = payload.TryGetValue("model_provider", out existingProvider)
                    ? Convert.ToString(existingProvider)
                    : string.Empty;
                if (string.Equals(existing, targetProvider, StringComparison.Ordinal))
                {
                    continue;
                }

                payload["model_provider"] = targetProvider;
                string replacementLine = serializer.Serialize(envelope);
                byte[] replacementBytes = Encoding.UTF8.GetBytes(replacementLine);
                int remainderOffset = lineEnd >= 0 ? lineEnd : originalBytes.Length;
                int remainderLength = originalBytes.Length - remainderOffset;
                byte[] updatedBytes = new byte[replacementBytes.Length + remainderLength];
                Buffer.BlockCopy(replacementBytes, 0, updatedBytes, 0, replacementBytes.Length);
                if (remainderLength > 0)
                {
                    Buffer.BlockCopy(
                        originalBytes,
                        remainderOffset,
                        updatedBytes,
                        replacementBytes.Length,
                        remainderLength);
                }

                string backupName = GetPathToken(fullPath) + "-" + Path.GetFileName(fullPath);
                string backupPath = Path.Combine(backupRoot, backupName);
                changes.Add(new SessionMetadataChange(
                    fullPath,
                    backupPath,
                    originalBytes,
                    updatedBytes));
            }

            if (changes.Count == 0)
            {
                return new SessionMetadataSyncResult(
                    0,
                    "无需创建（JSONL 已一致）",
                    new List<SessionMetadataChange>());
            }

            List<SessionMetadataChange> applied = new List<SessionMetadataChange>();
            try
            {
                foreach (SessionMetadataChange change in changes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(change.BackupPath));
                    File.Copy(change.Path, change.BackupPath, false);
                    WriteBytesAtomically(change.Path, change.UpdatedBytes);
                    applied.Add(change);
                }
            }
            catch
            {
                foreach (SessionMetadataChange change in applied)
                {
                    WriteBytesAtomically(change.Path, change.OriginalBytes);
                }
                throw;
            }

            return new SessionMetadataSyncResult(changes.Count, backupRoot, changes);
        }

        private static string GetPathToken(string path)
        {
            byte[] input = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(input);
                StringBuilder token = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    token.Append(digest[i].ToString("x2"));
                }
                return token.ToString();
            }
        }

        private static string RemoveExtendedPathPrefix(string path)
        {
            if (path == null)
            {
                return string.Empty;
            }
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path.Substring(4);
            }
            return path;
        }

        private string CreateStateBackup(string statePath, string purpose)
        {
            string directory = Path.Combine(root, "history_sync_backups");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string location = string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(statePath)).TrimEnd('\\'),
                Path.GetFullPath(root).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase)
                ? "root"
                : "sqlite";
            string backupPath = Path.Combine(
                directory,
                "state_5.sqlite." + location + "." + purpose + "." + stamp + ".bak");
            NativeSqlite.Backup(statePath, backupPath);
            return backupPath;
        }

        private static void EnsureThreadColumns(NativeSqlite database)
        {
            string[] required = { "source", "first_user_message", "has_user_event", "model_provider" };
            foreach (string column in required)
            {
                int exists = database.ScalarInt(
                    "select count(*) from pragma_table_info('threads') where name = '" + column + "'");
                if (exists == 0)
                {
                    throw new InvalidOperationException(
                        "threads table is missing required column: " + column);
                }
            }
        }

        private static void EnsureIntegrity(NativeSqlite database)
        {
            string result = database.ScalarText("pragma integrity_check");
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite integrity_check failed: " + result);
            }
        }

        private void SaveSettings(StoredSettings settings)
        {
            Directory.CreateDirectory(dataDirectory);
            List<string> lines = new List<string>();
            lines.Add("url=" + EncodeSetting(settings.BaseUrl));
            lines.Add("thirdModel=" + EncodeSetting(settings.ThirdPartyModel));
            lines.Add("officialModel=" + EncodeSetting(settings.OfficialModel));
            WriteTextAtomically(settingsPath, lines);
        }

        private static string EncodeSetting(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private List<string> ReadConfig()
        {
            AssertConfig();
            List<string> lines = new List<string>(File.ReadAllLines(configPath, Encoding.UTF8));
            TomlConfigurationDocument.Parse(lines);
            return lines;
        }

        private void AssertConfig()
        {
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("Codex root does not exist: " + root);
            }
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("config.toml was not found in the selected Codex root.", configPath);
            }
        }

        private string BackupConfig()
        {
            Directory.CreateDirectory(backupDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string destination = Path.Combine(backupDirectory, "config.toml." + stamp + ".bak");
            File.Copy(configPath, destination, false);
            return destination;
        }

        private void WriteConfigAtomically(List<string> lines)
        {
            TomlConfigurationDocument.Parse(lines);
            string temporary = Path.Combine(root, "config.toml.switcher." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                File.Copy(temporary, configPath, true);
                File.Delete(temporary);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void WriteTextAtomically(string path, List<string> lines)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(temporary, path, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void WriteBytesAtomically(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                {
                    File.Copy(temporary, path, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static string GetTopLevelValue(List<string> lines, string key)
        {
            return TomlConfigurationDocument.Parse(lines).GetString(string.Empty, key);
        }

        private static string GetSectionValue(List<string> lines, string section, string key)
        {
            return TomlConfigurationDocument.Parse(lines).GetString(section, key);
        }

        private static bool SectionExists(List<string> lines, string section)
        {
            return lines.Any(line =>
            {
                Match match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                return match.Success && string.Equals(match.Groups[1].Value, section, StringComparison.Ordinal);
            });
        }

        private static void SetTopLevelValue(List<string> lines, string key, string value)
        {
            int firstSection = lines.FindIndex(line => Regex.IsMatch(line, @"^\s*\["));
            if (firstSection < 0)
            {
                firstSection = lines.Count;
            }

            Regex expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            string replacement = key + " = \"" + EscapeToml(value) + "\"";
            bool found = false;
            for (int i = 0; i < firstSection; i++)
            {
                if (!expression.IsMatch(lines[i]))
                {
                    continue;
                }
                if (!found)
                {
                    lines[i] = replacement;
                    found = true;
                }
                else
                {
                    lines.RemoveAt(i);
                    i--;
                    firstSection--;
                }
            }

            if (!found)
            {
                lines.Insert(firstSection, replacement);
            }
        }

        private static string EscapeToml(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void RemoveProviderSections(List<string> lines)
        {
            List<string> result = new List<string>();
            bool skip = false;
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                if (match.Success)
                {
                    string section = match.Groups[1].Value;
                    skip = string.Equals(section, "model_providers." + ProviderId, StringComparison.Ordinal)
                        || section.StartsWith("model_providers." + ProviderId + ".", StringComparison.Ordinal);
                }
                if (!skip)
                {
                    result.Add(line);
                }
            }

            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }
            lines.Clear();
            lines.AddRange(result);
        }

        private void AddProviderSections(List<string> lines, string url)
        {
            string escapedExe = EscapeToml(helperPath);
            string escapedRoot = EscapeToml(root);
            lines.Add(string.Empty);
            lines.Add("[model_providers." + ProviderId + "]");
            lines.Add("name = \"custom\"");
            lines.Add("wire_api = \"responses\"");
            lines.Add("base_url = \"" + EscapeToml(url) + "\"");
            lines.Add(string.Empty);
            lines.Add("[model_providers." + ProviderId + ".auth]");
            lines.Add("command = \"" + escapedExe + "\"");
            lines.Add("args = [\"--emit-token\", \"--root\", \"" + escapedRoot + "\"]");
            lines.Add("timeout_ms = 5000");
            lines.Add("refresh_interval_ms = 0");
        }
    }

    internal sealed class TomlConfigurationDocument
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static TomlConfigurationDocument Parse(List<string> lines)
        {
            TomlConfigurationDocument document = new TomlConfigurationDocument();
            string section = string.Empty;
            int continuationDepth = 0;
            bool multilineBasic = false;
            bool multilineLiteral = false;
            for (int index = 0; index < lines.Count; index++)
            {
                string clean = StripComment(lines[index]).Trim();
                if (clean.Length == 0) continue;
                if (multilineBasic || multilineLiteral)
                {
                    if (multilineBasic && CountToken(clean, "\"\"\"") % 2 == 1) multilineBasic = false;
                    if (multilineLiteral && CountToken(clean, "'''") % 2 == 1) multilineLiteral = false;
                    continue;
                }
                if (continuationDepth > 0)
                {
                    continuationDepth += StructuralDelta(clean);
                    if (continuationDepth < 0) throw Error(index, "unbalanced array or inline table");
                    continue;
                }
                Match table = Regex.Match(clean, @"^\[\[?\s*([^\]]+?)\s*\]\]?$", RegexOptions.CultureInvariant);
                if (table.Success)
                {
                    section = table.Groups[1].Value.Trim();
                    if (section.Length == 0) throw Error(index, "empty table name");
                    continue;
                }
                int equals = FindUnquoted(clean, '=');
                if (equals <= 0) throw Error(index, "expected key = value");
                string key = clean.Substring(0, equals).Trim();
                string value = clean.Substring(equals + 1).Trim();
                if (key.Length == 0 || value.Length == 0) throw Error(index, "empty key or value");
                string composite = section + "\n" + key;
                if (document.values.ContainsKey(composite)) throw Error(index, "duplicate key " + key);
                document.values[composite] = value;
                if (CountToken(value, "\"\"\"") % 2 == 1) multilineBasic = true;
                else if (CountToken(value, "'''") % 2 == 1) multilineLiteral = true;
                else
                {
                    continuationDepth = StructuralDelta(value);
                    if (continuationDepth < 0) throw Error(index, "unbalanced array or inline table");
                }
            }
            if (continuationDepth != 0 || multilineBasic || multilineLiteral)
            {
                throw new InvalidOperationException("Invalid TOML: unterminated multiline value.");
            }
            return document;
        }

        internal string GetString(string section, string key)
        {
            string value;
            if (!values.TryGetValue((section ?? string.Empty) + "\n" + key, out value)) return string.Empty;
            value = value.Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static InvalidOperationException Error(int index, string message)
        {
            return new InvalidOperationException("Invalid TOML at line " + (index + 1) + ": " + message + ".");
        }
        private static int CountToken(string value, string token)
        {
            int count = 0;
            for (int at = 0; (at = value.IndexOf(token, at, StringComparison.Ordinal)) >= 0; at += token.Length) count++;
            return count;
        }
        private static int StructuralDelta(string value)
        {
            int result = 0;
            bool basic = false;
            bool literal = false;
            bool escape = false;
            foreach (char character in value)
            {
                if (basic)
                {
                    if (escape) { escape = false; continue; }
                    if (character == '\\') { escape = true; continue; }
                    if (character == '"') basic = false;
                    continue;
                }
                if (literal) { if (character == '\'') literal = false; continue; }
                if (character == '"') { basic = true; continue; }
                if (character == '\'') { literal = true; continue; }
                if (character == '[' || character == '{') result++;
                if (character == ']' || character == '}') result--;
            }
            return result;
        }
        private static int FindUnquoted(string value, char target)
        {
            bool basic = false;
            bool literal = false;
            bool escape = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (basic)
                {
                    if (escape) { escape = false; continue; }
                    if (character == '\\') { escape = true; continue; }
                    if (character == '"') basic = false;
                    continue;
                }
                if (literal) { if (character == '\'') literal = false; continue; }
                if (character == '"') { basic = true; continue; }
                if (character == '\'') { literal = true; continue; }
                if (character == target) return index;
            }
            return -1;
        }
        private static string StripComment(string value)
        {
            int comment = FindUnquoted(value, '#');
            return comment >= 0 ? value.Substring(0, comment) : value;
        }
    }

    internal sealed class SwitchTransaction
    {
        private readonly string manifestPath;
        private readonly SwitchTransactionManifest manifest;

        private SwitchTransaction(string path, SwitchTransactionManifest value)
        {
            manifestPath = path;
            manifest = value;
        }

        internal static SwitchTransaction Begin(string root, string configPath, List<string> databasePaths, string fromProvider, string toProvider)
        {
            string directory = Path.Combine(root, "api-switcher", "transactions", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SwitchTransactionManifest value = new SwitchTransactionManifest();
            value.Status = "started";
            value.CreatedUtc = DateTime.UtcNow.ToString("o");
            value.FromProvider = fromProvider;
            value.ToProvider = toProvider;
            value.ConfigPath = configPath;
            value.ConfigBackupPath = Path.Combine(directory, "config.toml.before");
            File.Copy(configPath, value.ConfigBackupPath, false);

            HashSet<string> rolloutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string databasePath in databasePaths)
            {
                DatabaseTransactionSnapshot databaseSnapshot = new DatabaseTransactionSnapshot();
                databaseSnapshot.Path = databasePath;
                using (NativeSqlite database = NativeSqlite.Open(databasePath))
                {
                    databaseSnapshot.Threads = database.QueryThreadSnapshots(
                        "select id, model_provider, has_user_event, rollout_path from threads " +
                        "where first_user_message <> '' and source in ('vscode','cli')");
                }
                value.Databases.Add(databaseSnapshot);
                foreach (ThreadTransactionSnapshot thread in databaseSnapshot.Threads)
                {
                    if (!string.IsNullOrWhiteSpace(thread.RolloutPath)) rolloutPaths.Add(RemoveExtendedPrefix(thread.RolloutPath));
                }
            }

            foreach (string path in rolloutPaths)
            {
                string fullPath = Path.GetFullPath(path);
                string rootPrefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) continue;
                SessionTransactionSnapshot session = new SessionTransactionSnapshot();
                session.Path = fullPath;
                session.FirstLine = ReadFirstLine(fullPath);
                value.Sessions.Add(session);
            }

            string manifestPath = Path.Combine(directory, "manifest.json");
            SwitchTransaction transaction = new SwitchTransaction(manifestPath, value);
            transaction.Save();
            return transaction;
        }

        internal static SwitchTransaction FindLatestCompleted(string transactionRoot)
        {
            if (!Directory.Exists(transactionRoot)) return null;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            foreach (string path in Directory.GetFiles(transactionRoot, "manifest.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    SwitchTransactionManifest value = serializer.Deserialize<SwitchTransactionManifest>(File.ReadAllText(path, Encoding.UTF8));
                    if (value != null && string.Equals(value.Status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        return new SwitchTransaction(path, value);
                    }
                }
                catch { }
            }
            return null;
        }

        internal void Complete()
        {
            manifest.Status = "completed";
            Save();
        }

        internal void Restore()
        {
            foreach (DatabaseTransactionSnapshot databaseSnapshot in manifest.Databases)
            {
                if (!File.Exists(databaseSnapshot.Path)) continue;
                using (NativeSqlite database = NativeSqlite.Open(databaseSnapshot.Path))
                {
                    database.Execute("begin immediate");
                    try
                    {
                        foreach (ThreadTransactionSnapshot thread in databaseSnapshot.Threads)
                        {
                            database.Execute(
                                "update threads set model_provider = '" + Sql(thread.ModelProvider) +
                                "', has_user_event = " + thread.HasUserEvent +
                                " where id = '" + Sql(thread.Id) + "'");
                        }
                        database.Execute("commit");
                    }
                    catch
                    {
                        try { database.Execute("rollback"); } catch { }
                        throw;
                    }
                }
            }
            foreach (SessionTransactionSnapshot session in manifest.Sessions)
            {
                if (File.Exists(session.Path)) ReplaceFirstLine(session.Path, session.FirstLine);
            }
            if (File.Exists(manifest.ConfigBackupPath)) File.Copy(manifest.ConfigBackupPath, manifest.ConfigPath, true);
            manifest.Status = "rolled_back";
            manifest.RolledBackUtc = DateTime.UtcNow.ToString("o");
            Save();
        }

        private void Save()
        {
            File.WriteAllText(manifestPath, new JavaScriptSerializer().Serialize(manifest), new UTF8Encoding(false));
        }

        private static string Sql(string value) { return (value ?? string.Empty).Replace("'", "''"); }
        private static string RemoveExtendedPrefix(string path) { return path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? path.Substring(4) : path; }
        private static string ReadFirstLine(string path)
        {
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true)) return reader.ReadLine() ?? string.Empty;
        }
        private static void ReplaceFirstLine(string path, string firstLine)
        {
            byte[] original = File.ReadAllBytes(path);
            int newline = Array.IndexOf(original, (byte)'\n');
            int remainder = newline >= 0 ? newline : original.Length;
            byte[] prefix = Encoding.UTF8.GetBytes(firstLine);
            byte[] updated = new byte[prefix.Length + original.Length - remainder];
            Buffer.BlockCopy(prefix, 0, updated, 0, prefix.Length);
            Buffer.BlockCopy(original, remainder, updated, prefix.Length, original.Length - remainder);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".rollback.tmp";
            File.WriteAllBytes(temporary, updated);
            File.Copy(temporary, path, true);
            File.Delete(temporary);
        }
    }

    internal sealed class SwitchTransactionManifest
    {
        public SwitchTransactionManifest()
        {
            Databases = new List<DatabaseTransactionSnapshot>();
            Sessions = new List<SessionTransactionSnapshot>();
        }
        public string Status { get; set; }
        public string CreatedUtc { get; set; }
        public string RolledBackUtc { get; set; }
        public string FromProvider { get; set; }
        public string ToProvider { get; set; }
        public string ConfigPath { get; set; }
        public string ConfigBackupPath { get; set; }
        public List<DatabaseTransactionSnapshot> Databases { get; set; }
        public List<SessionTransactionSnapshot> Sessions { get; set; }
    }

    internal sealed class DatabaseTransactionSnapshot
    {
        public DatabaseTransactionSnapshot() { Threads = new List<ThreadTransactionSnapshot>(); }
        public string Path { get; set; }
        public List<ThreadTransactionSnapshot> Threads { get; set; }
    }

    internal sealed class ThreadTransactionSnapshot
    {
        public string Id { get; set; }
        public string ModelProvider { get; set; }
        public int HasUserEvent { get; set; }
        public string RolloutPath { get; set; }
    }

    internal sealed class SessionTransactionSnapshot
    {
        public string Path { get; set; }
        public string FirstLine { get; set; }
    }

    internal sealed class SessionMetadataChange
    {
        internal readonly string Path;
        internal readonly string BackupPath;
        internal readonly byte[] OriginalBytes;
        internal readonly byte[] UpdatedBytes;

        internal SessionMetadataChange(
            string path,
            string backupPath,
            byte[] originalBytes,
            byte[] updatedBytes)
        {
            Path = path;
            BackupPath = backupPath;
            OriginalBytes = originalBytes;
            UpdatedBytes = updatedBytes;
        }
    }

    internal sealed class SessionMetadataSyncResult
    {
        internal readonly int ChangedCount;
        internal readonly string BackupDirectory;
        private readonly List<SessionMetadataChange> changes;

        internal SessionMetadataSyncResult(
            int changedCount,
            string backupDirectory,
            List<SessionMetadataChange> metadataChanges)
        {
            ChangedCount = changedCount;
            BackupDirectory = backupDirectory;
            changes = metadataChanges;
        }

        internal void Rollback()
        {
            foreach (SessionMetadataChange change in changes)
            {
                string temporary = change.Path + "." + Guid.NewGuid().ToString("N") + ".rollback.tmp";
                try
                {
                    File.WriteAllBytes(temporary, change.OriginalBytes);
                    File.Copy(temporary, change.Path, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }
    }

    internal sealed class NativeSqlite : IDisposable
    {
        private const int SqliteOk = 0;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private IntPtr handle;

        private NativeSqlite(IntPtr databaseHandle)
        {
            handle = databaseHandle;
        }

        internal static NativeSqlite Open(string path)
        {
            IntPtr database;
            int result = sqlite3_open16(path, out database);
            if (result != SqliteOk)
            {
                string message = GetError(database, result);
                if (database != IntPtr.Zero)
                {
                    sqlite3_close(database);
                }
                throw new InvalidOperationException("Unable to open SQLite database: " + message);
            }
            sqlite3_busy_timeout(database, 30000);
            return new NativeSqlite(database);
        }

        internal static void Backup(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                throw new IOException("Backup destination already exists: " + destinationPath);
            }

            using (NativeSqlite source = Open(sourcePath))
            using (NativeSqlite destination = Open(destinationPath))
            {
                IntPtr backup = sqlite3_backup_init(
                    destination.handle,
                    "main",
                    source.handle,
                    "main");
                if (backup == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "Unable to initialize SQLite backup: " +
                        GetError(destination.handle, sqlite3_errcode(destination.handle)));
                }

                int stepResult;
                try
                {
                    stepResult = sqlite3_backup_step(backup, -1);
                }
                finally
                {
                    int finishResult = sqlite3_backup_finish(backup);
                    if (finishResult != SqliteOk)
                    {
                        throw new InvalidOperationException(
                            "Unable to finish SQLite backup: " +
                            GetError(destination.handle, finishResult));
                    }
                }

                if (stepResult != SqliteDone)
                {
                    throw new InvalidOperationException(
                        "Unable to copy SQLite backup: " +
                        GetError(destination.handle, stepResult));
                }
            }
        }

        internal void Execute(string sql)
        {
            IntPtr errorPointer;
            int result = sqlite3_exec(handle, sql, IntPtr.Zero, IntPtr.Zero, out errorPointer);
            if (result == SqliteOk)
            {
                return;
            }

            string message = errorPointer == IntPtr.Zero
                ? GetError(handle, result)
                : Marshal.PtrToStringAnsi(errorPointer);
            if (errorPointer != IntPtr.Zero)
            {
                sqlite3_free(errorPointer);
            }
            throw new InvalidOperationException("SQLite command failed: " + message);
        }

        internal int ScalarInt(string sql)
        {
            IntPtr statement = Prepare(sql);
            try
            {
                int result = sqlite3_step(statement);
                if (result != SqliteRow)
                {
                    throw new InvalidOperationException(
                        "SQLite query did not return a row: " + GetError(handle, result));
                }
                return sqlite3_column_int(statement, 0);
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        internal string ScalarText(string sql)
        {
            IntPtr statement = Prepare(sql);
            try
            {
                int result = sqlite3_step(statement);
                if (result != SqliteRow)
                {
                    throw new InvalidOperationException(
                        "SQLite query did not return a row: " + GetError(handle, result));
                }
                IntPtr value = sqlite3_column_text16(statement, 0);
                return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value);
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        internal List<string> QueryTextColumn(string sql)
        {
            IntPtr statement = Prepare(sql);
            List<string> values = new List<string>();
            try
            {
                while (true)
                {
                    int result = sqlite3_step(statement);
                    if (result == SqliteDone)
                    {
                        return values;
                    }
                    if (result != SqliteRow)
                    {
                        throw new InvalidOperationException(
                            "SQLite query failed: " + GetError(handle, result));
                    }
                    IntPtr value = sqlite3_column_text16(statement, 0);
                    values.Add(value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value));
                }
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        internal List<ThreadTransactionSnapshot> QueryThreadSnapshots(string sql)
        {
            IntPtr statement = Prepare(sql);
            List<ThreadTransactionSnapshot> values = new List<ThreadTransactionSnapshot>();
            try
            {
                while (true)
                {
                    int result = sqlite3_step(statement);
                    if (result == SqliteDone) return values;
                    if (result != SqliteRow)
                    {
                        throw new InvalidOperationException("SQLite query failed: " + GetError(handle, result));
                    }
                    ThreadTransactionSnapshot value = new ThreadTransactionSnapshot();
                    value.Id = ColumnText(statement, 0);
                    value.ModelProvider = ColumnText(statement, 1);
                    value.HasUserEvent = sqlite3_column_int(statement, 2);
                    value.RolloutPath = ColumnText(statement, 3);
                    values.Add(value);
                }
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        private static string ColumnText(IntPtr statement, int index)
        {
            IntPtr value = sqlite3_column_text16(statement, index);
            return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value);
        }

        private IntPtr Prepare(string sql)
        {
            IntPtr statement;
            int result = sqlite3_prepare16_v2(handle, sql, -1, out statement, IntPtr.Zero);
            if (result != SqliteOk)
            {
                throw new InvalidOperationException(
                    "Unable to prepare SQLite query: " + GetError(handle, result));
            }
            return statement;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                sqlite3_close(handle);
                handle = IntPtr.Zero;
            }
        }

        private static string GetError(IntPtr database, int result)
        {
            if (database == IntPtr.Zero)
            {
                return "SQLite error " + result;
            }
            IntPtr pointer = sqlite3_errmsg16(database);
            string message = pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer);
            return string.IsNullOrWhiteSpace(message) ? "SQLite error " + result : message;
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open16(
            [MarshalAs(UnmanagedType.LPWStr)] string filename,
            out IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_exec(
            IntPtr database,
            string sql,
            IntPtr callback,
            IntPtr callbackArgument,
            out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr pointer);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare16_v2(
            IntPtr database,
            [MarshalAs(UnmanagedType.LPWStr)] string sql,
            int byteCount,
            out IntPtr statement,
            IntPtr remainingSql);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr statement, int columnIndex);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text16(IntPtr statement, int columnIndex);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg16(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_errcode(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr sqlite3_backup_init(
            IntPtr destinationDatabase,
            string destinationName,
            IntPtr sourceDatabase,
            string sourceName);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_backup_step(IntPtr backup, int pageCount);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_backup_finish(IntPtr backup);
    }

    internal sealed class ProviderStatus
    {
        internal readonly string Provider;
        internal readonly string Model;
        internal readonly string BaseUrl;
        internal readonly bool UsesCredentialHelper;
        internal readonly bool ReusesOpenAiLogin;

        internal ProviderStatus(
            string provider,
            string model,
            string baseUrl,
            bool usesCredentialHelper,
            bool reusesOpenAiLogin)
        {
            Provider = provider ?? string.Empty;
            Model = model ?? string.Empty;
            BaseUrl = baseUrl ?? string.Empty;
            UsesCredentialHelper = usesCredentialHelper;
            ReusesOpenAiLogin = reusesOpenAiLogin;
        }

        internal bool IsThirdParty
        {
            get { return string.Equals(Provider, "custom", StringComparison.OrdinalIgnoreCase); }
        }

        internal string ToDisplayString()
        {
            if (!IsThirdParty)
            {
                return "官方 OpenAI 登录 | 模型 " + Model;
            }

            string auth = UsesCredentialHelper
                ? "独立加密 Key"
                : (ReusesOpenAiLogin ? "复用 OpenAI 登录" : "未检测到认证");
            return "第三方 Responses API | " + BaseUrl + " | 模型 " + Model + " | " + auth;
        }
    }

    internal sealed class StoredSettings
    {
        internal string BaseUrl = string.Empty;
        internal string ThirdPartyModel = string.Empty;
        internal string OfficialModel = string.Empty;
    }
}
