using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace RiseFlashTool
{
    public partial class MainWindow : Window
    {
        private string FlashromPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ft", "x32_flashrom", "flashrom.exe");


        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private List<string> _knownPorts = new List<string>();
        private string _selectedFirmwarePath = null;
        private const bool IS_ENTERPRISE = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int eventType = wParam.ToInt32();
                if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE)
                {
                    Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(AutoDetectPorts));
                }
            }
            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var startPorts = SerialPort.GetPortNames();
            cmbPorts.Items.Clear();
            foreach (var port in startPorts) cmbPorts.Items.Add(port);
            _knownPorts = startPorts.ToList();

            cmbPorts.SelectedIndex = -1;

            UpdateConnectionStatus(false, "Selecione uma porta...");
            lblFileInfo.Text = "Nenhum arquivo selecionado";

            if (IS_ENTERPRISE)
            {
                lblTitle.Text = "POSI";
                lblTitleSubtitle.Text = "FLASH TOOL";
                txtConsole.Text = "POSIFlashTool inicializado";
                this.Title = "PosiFlashTool - Enterprise Edition"; 
            }
            else
            {
                lblTitle.Text = "RISE";
                lblTitleSubtitle.Text = "FLASH TOOL - Community";
                txtConsole.Text = "RiseFlashTool inicializado";
                this.Title = "RiseFlashTool"; 
            }

            if (!File.Exists(FlashromPath))
            {
                MessageBox.Show("Erro Crítico: flashrom.exe não encontrado.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
            Log("Sistema iniciado.", "INFO");
        }


        private void AutoDetectPorts()
        {
            var currentPorts = SerialPort.GetPortNames().ToList();
            var selectedBefore = cmbPorts.SelectedItem?.ToString();


            cmbPorts.SelectionChanged -= CmbPorts_SelectionChanged;

            cmbPorts.Items.Clear();
            foreach (var p in currentPorts) cmbPorts.Items.Add(p);


            cmbPorts.SelectionChanged += CmbPorts_SelectionChanged;

            if (currentPorts.Count > _knownPorts.Count)
            {
                var newPort = currentPorts.Except(_knownPorts).FirstOrDefault();
                if (newPort != null)
                {
                    cmbPorts.SelectedItem = newPort;

                    Log($"Dispositivo NOVO detectado: {newPort}", "SUCCESS");
                }
            }
            else if (currentPorts.Count < _knownPorts.Count)
            {
                if (selectedBefore != null && !currentPorts.Contains(selectedBefore))
                {
                    UpdateConnectionStatus(false, "Dispositivo desconectado");
                    Log("Porta ativa foi removida.", "WARN");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(selectedBefore) && currentPorts.Contains(selectedBefore))
                    cmbPorts.SelectedItem = selectedBefore;
            }

            _knownPorts = currentPorts;
        }


        private void CmbPorts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPorts.SelectedItem != null)
            {
                string port = cmbPorts.SelectedItem.ToString();
                UpdateConnectionStatus(true, $"Conectado: {port}");
                Log($"Porta selecionada manualmente: {port}", "INFO");
            }
            else
            {
                UpdateConnectionStatus(false, "Selecione uma porta...");
            }
        }

        private void UpdateConnectionStatus(bool connected, string text)
        {
            lblConnectionStatus.Text = text;
            elStatus.Fill = connected ? Brushes.LightGreen : Brushes.Gray;
        }


        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog { Filter = "Binary Files|*.bin;*.rom;*.hex;*.fd", Title = "Selecionar Firmware" };
            if (openDialog.ShowDialog() == true)
            {
                _selectedFirmwarePath = openDialog.FileName;
                FileInfo fi = new FileInfo(_selectedFirmwarePath);


                lblFileInfo.Text = $"{fi.Name} ({(fi.Length / 1024.0):F1} KB)";
                Log($"Arquivo carregado: {fi.Name}", "INFO");
            }
        }


        private async void BtnWrite_Click(object sender, RoutedEventArgs e)
        {

            string programmer = GetProgrammerString();
            if (programmer == null)
            {
                Log("ERRO: Nenhuma porta selecionada.", "ERROR");
                MessageBox.Show("Selecione uma porta COM antes de gravar.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }


            if (string.IsNullOrEmpty(_selectedFirmwarePath))
            {
                Log("Nenhum arquivo pré-selecionado. Abrindo seletor...", "WARN");
                BtnSelectFile_Click(sender, e);


                if (string.IsNullOrEmpty(_selectedFirmwarePath)) return;
            }


            FileInfo fi = new FileInfo(_selectedFirmwarePath);
            if (MessageBox.Show($"GRAVAR NO CHIP?\n\nArquivo: {fi.Name}\nTamanho: {fi.Length} bytes\n\nISSO É DESTRUTIVO!", "Confirmar Gravação", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                LockUI(true);
                Log("--- INICIANDO GRAVAÇÃO ---", "WARN");

                await RunFlashromAsync($"-p {programmer} -w \"{_selectedFirmwarePath}\"");

                Log("Gravação concluída com SUCESSO!", "SUCCESS");
                MessageBox.Show("Operação finalizada com sucesso!", "RiseFlashTool", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Falha na gravação: {ex.Message}", "ERROR");
                MessageBox.Show("Erro ao gravar. Verifique o console.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LockUI(false);
            }
        }


        private async void BtnRead_Click(object sender, RoutedEventArgs e)
        {
            string programmer = GetProgrammerString();
            if (programmer == null) { Log("Selecione uma porta COM!", "ERROR"); return; }

            SaveFileDialog saveDialog = new SaveFileDialog { Filter = "Binary Files|*.bin;*.rom", FileName = "backup.bin" };
            if (saveDialog.ShowDialog() != true) return;

            string finalPath = saveDialog.FileName;
            string temp1 = Path.Combine(Path.GetTempPath(), "dump1.bin");
            string temp2 = Path.Combine(Path.GetTempPath(), "dump2.bin");

            try
            {
                LockUI(true);
                Log("--- INICIANDO SMART BACKUP ---", "INFO");


                lblFileInfo.Text = $"Lendo para: {Path.GetFileName(finalPath)}";

                Log("Lendo (1/2)...", "INFO");
                await RunFlashromAsync($"-p {programmer} -r \"{temp1}\"");

                Log("Verificando (2/2)...", "INFO");
                await RunFlashromAsync($"-p {programmer} -r \"{temp2}\"");

                Log("Validando integridade...", "WARN");
                bool areEqual = await Task.Run(() => File.ReadAllBytes(temp1).SequenceEqual(File.ReadAllBytes(temp2)));

                if (areEqual)
                {
                    File.Copy(temp1, finalPath, true);
                    Log($"Backup Salvo: {finalPath}", "SUCCESS");
                }
                else
                {
                    File.Copy(temp1, finalPath + ".err1", true);
                    File.Copy(temp2, finalPath + ".err2", true);
                    Log("ERRO DE INTEGRIDADE. Dumps divergentes salvos.", "ERROR");
                }

                if (File.Exists(temp1)) File.Delete(temp1);
                if (File.Exists(temp2)) File.Delete(temp2);
            }
            catch (Exception ex) { Log($"Erro: {ex.Message}", "ERROR"); }
            finally { LockUI(false); }
        }


        private void LockUI(bool isLocked)
        {
            this.IsEnabled = !isLocked;
            Mouse.OverrideCursor = isLocked ? Cursors.Wait : null;
        }

        private string GetProgrammerString()
        {
            if (cmbPorts.SelectedItem == null) return null;
            string baud = string.IsNullOrWhiteSpace(cmbBaud.Text) ? "115200" : cmbBaud.Text;
            return $"serprog:dev={cmbPorts.SelectedItem}:{baud}";
        }

        private Task RunFlashromAsync(string arguments)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(FlashromPath)) throw new Exception("Executável não encontrado.");

                Process p = new Process();
                p.StartInfo.FileName = FlashromPath;
                p.StartInfo.Arguments = arguments;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;

                p.OutputDataReceived += (s, d) => { if (!string.IsNullOrWhiteSpace(d.Data)) Log(d.Data, "CMD"); };
                p.ErrorDataReceived += (s, d) => { if (!string.IsNullOrWhiteSpace(d.Data)) Log(d.Data, "CMD"); };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();

                if (p.ExitCode != 0) throw new Exception("Processo flashrom falhou.");
            });
        }

        private void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            string p = GetProgrammerString();
            if (p != null) _ = RunFlashromAsync($"-p {p}");
            else Log("Selecione uma porta primeiro.", "WARN");
        }

        private void BtnCompare_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog o1 = new OpenFileDialog { Title = "Arquivo 1" }; if (o1.ShowDialog() != true) return;
            OpenFileDialog o2 = new OpenFileDialog { Title = "Arquivo 2" }; if (o2.ShowDialog() != true) return;

            Log($"Comparando {Path.GetFileName(o1.FileName)} x {Path.GetFileName(o2.FileName)}", "INFO");
            bool eq = File.ReadAllBytes(o1.FileName).SequenceEqual(File.ReadAllBytes(o2.FileName));
            Log(eq ? "RESULTADO: IDÊNTICOS" : "RESULTADO: DIFERENTES", eq ? "SUCCESS" : "ERROR");
        }

        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e) => AutoDetectPorts();
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            if (IS_ENTERPRISE)
            {
                AboutEnterpriseWindow about = new AboutEnterpriseWindow();
                about.Owner = this;
                about.ShowDialog();
            }
            else
            {
                AboutWindow about = new AboutWindow();
                about.Owner = this;
                about.ShowDialog();
            }
        }
        private void BtnClearConsole_Click(object sender, RoutedEventArgs e) => txtConsole.Text = "";

        private void Log(string message, string type = "INFO")
        {
            string color = type switch { "ERROR" => "#F44336", "SUCCESS" => "#4CAF50", "WARN" => "#FF9800", "CMD" => "#808080", _ => "#E0E0E0" };
            Dispatcher.Invoke(() =>
            {
                txtConsole.Text += $"\n[{DateTime.Now:HH:mm:ss}] {type}: {message}";
                scrollConsole.ScrollToBottom();
                if (type != "CMD") { lblStatus.Text = message; lblStatus.Foreground = (Brush)new BrushConverter().ConvertFrom(color); }
            });
        }
    }
}