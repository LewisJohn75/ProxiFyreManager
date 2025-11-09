// ProxiFyre Manager v1.2 – WinForms, single-file CS, .NET Framework 4.8 compatible

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ProxiFyreManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            if (!File.Exists("ProxiFyre.exe"))
            {
                MessageBox.Show("Рядом с менеджером не найден ProxiFyre.exe",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // -------------------- CONFIG CLASSES --------------------

    public class ConfigRoot
    {
        [JsonPropertyName("logLevel")] public string LogLevel { get; set; }
        [JsonPropertyName("proxies")] public List<ProxyConfig> Proxies { get; set; }
    }

    public class ProxyConfig
    {
        [JsonPropertyName("appNames")] public List<string> AppNames { get; set; }
        [JsonPropertyName("socks5ProxyEndpoint")] public string Endpoint { get; set; }
        [JsonPropertyName("supportedProtocols")] public List<string> Protocols { get; set; }
    }

    // -------------------- MAIN FORM --------------------

    public class MainForm : Form
    {
        const string VERSION = "v1.2";
        const string CONFIG = "app-config.json";
        const string DEFAULT_ENDPOINT = "127.0.0.1:10808";

        ComboBox cbLog;
        ComboBox cbProto;
        TextBox tbEndpoint;
        FlowLayoutPanel listPanel;

        public MainForm()
        {
            this.Text = "ProxiFyre Manager " + VERSION;
            this.Width = 720;
            this.Height = 640;
            this.MinimumSize = new System.Drawing.Size(720, 640);

            this.Icon = Properties.Resources.imageres_68;

            // drag-drop enable
            this.AllowDrop = true;
            this.DragEnter += OnDragEnter;
            this.DragDrop += OnDragDrop;

            // ================= MAIN LAYOUT =================

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            this.Controls.Add(main);

            // ------------------- TOP PANEL -------------------

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                AutoSize = true
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var btnLoad = new Button { Text = "Загрузить", AutoSize = true };
            var lblCheck = new Label { Text = "Проверить:", AutoSize = true, Padding = new Padding(10, 8, 0, 0) };
            var btnCheckJson = new Button { Text = "Конфиг", AutoSize = true };
            var btnCheckProxy = new Button { Text = "Прокси", AutoSize = true };
            var btnSave = new Button { Text = "Сохранить", AutoSize = true };

            btnLoad.Click += (s, e) => LoadConfig();
            btnCheckJson.Click += (s, e) => CheckConfig();
            btnCheckProxy.Click += (s, e) => CheckProxy();
            btnSave.Click += (s, e) => SaveConfig();

            top.Controls.Add(btnLoad, 0, 0);
            top.Controls.Add(lblCheck, 1, 0);
            top.Controls.Add(btnCheckJson, 2, 0);
            top.Controls.Add(btnCheckProxy, 3, 0);
            top.Controls.Add(btnSave, 4, 0);

            main.Controls.Add(top);

            // ------------------- OPTIONS -------------------

            var opts = new GroupBox
            {
                Text = "Параметры прокси",
                Dock = DockStyle.Top,
                AutoSize = true
            };

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            opts.Controls.Add(grid);

            grid.Controls.Add(new Label { Text = "logLevel:", AutoSize = true }, 0, 0);
            cbLog = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbLog.Items.AddRange(new[] { "Error", "Warning", "Info", "Debug", "All" });
            cbLog.SelectedIndex = 0;
            grid.Controls.Add(cbLog, 1, 0);

            grid.Controls.Add(new Label { Text = "socks5ProxyEndpoint:", AutoSize = true }, 0, 1);
            tbEndpoint = new TextBox { Width = 200, Text = DEFAULT_ENDPOINT };
            grid.Controls.Add(tbEndpoint, 1, 1);

            grid.Controls.Add(new Label { Text = "supportedProtocols:", AutoSize = true }, 0, 2);
            cbProto = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbProto.Items.AddRange(new[] { "TCP", "UDP", "TCP+UDP" });
            cbProto.SelectedIndex = 2;
            grid.Controls.Add(cbProto, 1, 2);

            main.Controls.Add(opts);

            // ------------------- APP LIST -------------------

            var apps = new GroupBox
            {
                Text = "Приложения (appNames)",
                Dock = DockStyle.Fill
            };
            var layout = new TableLayoutPanel
            {
                RowCount = 2,
                ColumnCount = 1,
                Dock = DockStyle.Fill
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var btnAdd = new Button { Text = "Добавить", AutoSize = true };
            btnAdd.Click += (s, e) => AddFiles();
            layout.Controls.Add(btnAdd);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            listPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            scroll.Controls.Add(listPanel);
            layout.Controls.Add(scroll);
            apps.Controls.Add(layout);
            main.Controls.Add(apps);

            if (File.Exists(CONFIG)) LoadConfig();
        }

        // ===============================================================
        // ----------------------- DRAG & DROP ---------------------------
        // ===============================================================

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                AddRow(f);
        }

        // ===============================================================
        // ----------------------- APP ROW UI ----------------------------
        // ===============================================================

        void AddFiles()
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Выберите файлы"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
                foreach (var f in dlg.FileNames)
                    AddRow(f);
        }

        void AddRow(string path)
        {
            var row = new Panel
            {
                Height = 32,
                Width = 650,
                Margin = new Padding(3),
                BorderStyle = BorderStyle.FixedSingle
            };

            // кнопка открыть (если путь валиден)
            bool exists = File.Exists(path);

            if (exists)
            {
                var btnOpen = new Button
                {
                    Text = "📂",
                    Width = 36,
                    Height = 24,
                    Left = 3,
                    Top = 3
                };
                btnOpen.Click += (s, e) => OpenInExplorer(path);
                row.Controls.Add(btnOpen);
            }

            // кнопка удалить
            var btnDel = new Button
            {
                Text = "❌",
                Width = 36,
                Height = 24,
                Left = exists ? 42 : 3,
                Top = 3
            };
            btnDel.Click += (s, e) => listPanel.Controls.Remove(row);
            row.Controls.Add(btnDel);

            // текст пути
            var lbl = new Label
            {
                Text = path,
                AutoSize = true,
                Left = exists ? 85 : 45,
                Top = 7
            };
            row.Controls.Add(lbl);

            row.Tag = path;
            listPanel.Controls.Add(row);
        }

        void OpenInExplorer(string path)
        {
            if (File.Exists(path))
                Process.Start("explorer", $"/select,\"{path}\"");
            else
                Process.Start("explorer", ".");
        }

        // ===============================================================
        // ----------------------- LOAD & SAVE ---------------------------
        // ===============================================================

        void LoadConfig()
        {
            try
            {
                string json = File.ReadAllText(CONFIG);
                var cfg = System.Text.Json.JsonSerializer.Deserialize<ConfigRoot>(json);

                if (cfg == null)
                    throw new Exception("Не удалось распарсить конфиг.");

                cbLog.SelectedItem = cfg.LogLevel ?? "Error";

                var p = cfg.Proxies?[0];
                if (p == null)
                    throw new Exception("В конфиге отсутствует объект proxies[0]");

                tbEndpoint.Text = string.IsNullOrWhiteSpace(p.Endpoint) ? DEFAULT_ENDPOINT : p.Endpoint;

                if (p.Protocols == null || p.Protocols.Count == 0)
                    cbProto.SelectedItem = "TCP+UDP";
                else if (p.Protocols.Count == 2)
                    cbProto.SelectedItem = "TCP+UDP";
                else
                    cbProto.SelectedItem = p.Protocols[0];

                listPanel.Controls.Clear();
                if (p.AppNames != null)
                    foreach (var name in p.AppNames)
                        AddRow(name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void SaveConfig()
        {
            var names = new List<string>();
            foreach (Control c in listPanel.Controls)
                names.Add((string)c.Tag);

            // proto
            List<string> protos = new List<string>();
            string sel = cbProto.SelectedItem.ToString();
            if (sel == "TCP+UDP")
                protos.AddRange(new[] { "TCP", "UDP" });
            else
                protos.Add(sel);

            // endpoint
            string ep = tbEndpoint.Text.Trim();
            if (ep == "")
                ep = DEFAULT_ENDPOINT;

            var cfg = new ConfigRoot
            {
                LogLevel = cbLog.SelectedItem.ToString(),
                Proxies = new List<ProxyConfig>
                {
                    new ProxyConfig
                    {
                        AppNames = names,
                        Endpoint = ep,
                        Protocols = protos
                    }
                }
            };

            var opt = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            File.WriteAllText(CONFIG, System.Text.Json.JsonSerializer.Serialize(cfg, opt));
            MessageBox.Show("Сохранено", "OK");
        }

        // ===============================================================
        // ----------------------- CHECK CONFIG --------------------------
        // ===============================================================

        void CheckConfig()
        {
            try
            {
                string json = File.ReadAllText(CONFIG);
                JsonDocument.Parse(json);
                MessageBox.Show("JSON валиден", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===============================================================
        // ----------------------- CHECK PROXY ---------------------------
        // ===============================================================

        void CheckProxy()
        {
            try
            {
                string ep = tbEndpoint.Text.Trim();
                if (ep == "") ep = DEFAULT_ENDPOINT;

                string[] parts = ep.Split(':');
                if (parts.Length != 2)
                    throw new Exception("Endpoint должен быть вида HOST:PORT");

                string host = parts[0];
                int port = int.Parse(parts[1]);

                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(1500);

                    if (!ok)
                        throw new Exception("Таймаут подключения");

                    client.EndConnect(ar);
                }

                MessageBox.Show($"Прокси доступен ({ep})", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка прокси", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
