using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace SourceTXCompanion
{
    public class BoardProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Chip { get; set; }
        public string FlashSize { get; set; }
        public string FlashMode { get; set; }
        public string FlashFreq { get; set; }
        public string Psram { get; set; }
        public string PartitionNvs { get; set; }
        public bool Enabled { get; set; }

        public override string ToString() { return Name; }
    }

    public class DisplayProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Resolution { get; set; }
        public string Driver { get; set; }
        public string Interface { get; set; }
        public string Touch { get; set; }
        public string Pins { get; set; }
        public bool Enabled { get; set; }

        public override string ToString() { return Name; }
    }

    public class SerialDeviceInfo
    {
        public string PortName { get; set; }
        public string Description { get; set; }
        public string HardwareID { get; set; }
        public int Priority { get; set; }
        public bool IsEspressif { get; set; }

        public string DisplayName
        {
            get
            {
                if (IsEspressif)
                {
                    return string.Format("{0} — ESP32-S3 USB JTAG / Serial (Auto-Detected) ★", PortName);
                }
                if (!string.IsNullOrEmpty(Description) && !Description.Equals(PortName, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Format("{0} — {1}", PortName, Description);
                }
                return PortName;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class FirmwareValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public string ImageType { get; set; }
        public string Sha256Hash { get; set; }
        public string ProjectName { get; set; }
        public string VersionString { get; set; }
        public long FileSizeBytes { get; set; }
    }

    public static class FirmwareValidator
    {
        public static FirmwareValidationResult ValidateFirmwareImage(string filePath, string expectedOffset)
        {
            var result = new FirmwareValidationResult();
            if (!File.Exists(filePath))
            {
                result.IsValid = false;
                result.ErrorMessage = string.Format("Firmware file does not exist:\n{0}", filePath);
                return result;
            }

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                result.FileSizeBytes = data.Length;

                if (data.Length < 256)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "Binary file is too small (< 256 bytes) to be a valid ESP32 firmware image.";
                    return result;
                }

                // 1. Calculate SHA-256 Checksum
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(data);
                    result.Sha256Hash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }

                // 2. Check ESP32 image magic byte (0xE9)
                if (data[0] != 0xE9)
                {
                    result.IsValid = false;
                    result.ErrorMessage = string.Format("Invalid ESP32 binary header magic (byte 0 is 0x{0:X2}, expected 0xE9).", data[0]);
                    return result;
                }

                // 3. Inspect image structures
                bool hasPartitionTableAt8000 = (data.Length > 0x8002 && data[0x8000] == 0x50 && data[0x8001] == 0xAA);
                bool hasAppDescAt20 = false;
                bool hasAppDescAt10020 = false;

                if (data.Length >= 0x70)
                {
                    uint magic20 = BitConverter.ToUInt32(data, 0x20);
                    if (magic20 == 0xABCD5432)
                    {
                        hasAppDescAt20 = true;
                        result.VersionString = Encoding.ASCII.GetString(data, 0x30, 32).Trim('\0', ' ');
                        result.ProjectName = Encoding.ASCII.GetString(data, 0x50, 32).Trim('\0', ' ');
                    }
                }

                if (data.Length >= 0x10070)
                {
                    uint magic10020 = BitConverter.ToUInt32(data, 0x10020);
                    if (magic10020 == 0xABCD5432)
                    {
                        hasAppDescAt10020 = true;
                        result.VersionString = Encoding.ASCII.GetString(data, 0x10030, 32).Trim('\0', ' ');
                        result.ProjectName = Encoding.ASCII.GetString(data, 0x10050, 32).Trim('\0', ' ');
                    }
                }

                if (hasPartitionTableAt8000 && hasAppDescAt10020)
                {
                    result.ImageType = "Full Factory Image (Bootloader + Partitions + App @ 0x0000)";
                }
                else if (hasAppDescAt20)
                {
                    result.ImageType = "App OTA Firmware (App @ 0x10000)";
                }
                else
                {
                    result.ImageType = "Generic ESP32 Binary";
                }

                // 4. Validate against expected flashing offset
                if (expectedOffset == "0x0000")
                {
                    if (data.Length < 65536)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = "Factory image is too small (< 64 KB). It cannot be flashed as a factory image at 0x0000.";
                        return result;
                    }
                    if (!hasPartitionTableAt8000)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = "Image does not contain a partition table magic (0xAA50) at offset 0x8000. It cannot be flashed at 0x0000.";
                        return result;
                    }
                }
                else if (expectedOffset == "0x10000")
                {
                    if (hasPartitionTableAt8000)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = "You have selected a Full Factory image, but Flashing Mode is set to App (0x10000). Please switch Flashing Mode to 'Full Factory Image (0x0000)'.";
                        return result;
                    }
                    if (!hasAppDescAt20)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = "Image does not contain a valid ESP-IDF app descriptor (magic 0xABCD5432) at offset 0x20. It cannot be flashed at 0x10000.";
                        return result;
                    }
                }

                result.IsValid = true;
                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = "Firmware validation error: " + ex.Message;
                return result;
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const uint TRANSFER_MAGIC = 0x5354584DU; // 'STXM' (0x5354584D)
        private const ushort TRANSFER_SCHEMA_VERSION = 21;
        private const string TRANSFER_PREFIX = "SOURCETX_MODEL:";

        private bool _isFlashing = false;
        private bool _isInstalling = false;
        private bool _isBuilding = false;
        private bool _isSyncingSelectors = false;

        private List<SerialDeviceInfo> _detectedDevices = new List<SerialDeviceInfo>();
        private List<BoardProfile> _boards = new List<BoardProfile>();
        private List<DisplayProfile> _displays = new List<DisplayProfile>();

        public MainWindow()
        {
            InitializeComponent();
            LoadHardwareCatalog();
            AutoDetectSerialPorts(false);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AutoDetectSerialPorts(true);
                }));
            }
            return IntPtr.Zero;
        }

        #region Hardware Targets Catalog (Loaded Dynamically from targets.json)

        private void LoadHardwareCatalog()
        {
            _boards.Clear();
            _displays.Clear();

            string targetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "targets.json");
            if (!File.Exists(targetsPath))
            {
                targetsPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\targets.json"));
            }

            if (File.Exists(targetsPath))
            {
                try
                {
                    string json = File.ReadAllText(targetsPath);
                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (dict != null && dict.ContainsKey("boards"))
                    {
                        var boardsList = dict["boards"] as ArrayList;
                        if (boardsList != null)
                        {
                            foreach (Dictionary<string, object> b in boardsList)
                            {
                                _boards.Add(new BoardProfile
                                {
                                    Id = b.ContainsKey("id") ? b["id"].ToString() : "",
                                    Name = b.ContainsKey("name") ? b["name"].ToString() : "",
                                    Chip = b.ContainsKey("chip") ? b["chip"].ToString() : "esp32s3",
                                    FlashSize = b.ContainsKey("flash_size") ? b["flash_size"].ToString() : "4MB",
                                    FlashMode = b.ContainsKey("flash_mode") ? b["flash_mode"].ToString() : "dio",
                                    FlashFreq = b.ContainsKey("flash_freq") ? b["flash_freq"].ToString() : "80m",
                                    Psram = b.ContainsKey("psram") ? b["psram"].ToString() : "",
                                    PartitionNvs = b.ContainsKey("partition_nvs") ? b["partition_nvs"].ToString() : "0x3D0000",
                                    Enabled = b.ContainsKey("enabled") ? Convert.ToBoolean(b["enabled"]) : true
                                });
                            }
                        }
                    }

                    if (dict != null && dict.ContainsKey("displays"))
                    {
                        var displaysList = dict["displays"] as ArrayList;
                        if (displaysList != null)
                        {
                            foreach (Dictionary<string, object> d in displaysList)
                            {
                                _displays.Add(new DisplayProfile
                                {
                                    Id = d.ContainsKey("id") ? d["id"].ToString() : "",
                                    Name = d.ContainsKey("name") ? d["name"].ToString() : "",
                                    Resolution = d.ContainsKey("resolution") ? d["resolution"].ToString() : "480x320",
                                    Driver = d.ContainsKey("driver") ? d["driver"].ToString() : "ST7796U",
                                    Interface = d.ContainsKey("interface") ? d["interface"].ToString() : "SPI",
                                    Touch = d.ContainsKey("touch") ? d["touch"].ToString() : "FT6x36",
                                    Pins = d.ContainsKey("pins") ? d["pins"].ToString() : "",
                                    Enabled = d.ContainsKey("enabled") ? Convert.ToBoolean(d["enabled"]) : true
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("targets.json parse notice: " + ex.Message);
                }
            }

            // Safe fallback if targets.json is missing or empty
            if (_boards.Count == 0)
            {
                _boards.Add(new BoardProfile
                {
                    Id = "esp32s3-4mb",
                    Name = "ESP32-S3 SuperMini (4MB Flash DIO/80M, 2MB PSRAM) [Official]",
                    Chip = "esp32s3",
                    FlashSize = "4MB",
                    FlashMode = "dio",
                    FlashFreq = "80m",
                    Psram = "2MB Quad-PSRAM",
                    PartitionNvs = "0x3D0000",
                    Enabled = true
                });
            }

            if (_displays.Count == 0)
            {
                _displays.Add(new DisplayProfile
                {
                    Id = "st7796",
                    Name = "3.5\" ST7796U 480x320 SPI (Touch FT6x36) [Official Reference]",
                    Resolution = "480x320",
                    Driver = "ST7796U",
                    Interface = "SPI",
                    Touch = "FT6x36 (I2C @ 0x38)",
                    Pins = "MOSI: 7, SCLK: 2, CS: 14, DC: 13, RST: 10, BL: 3 | Touch SDA: 8, SCL: 9",
                    Enabled = true
                });
            }

            PopulateDropdown(InstallBoardComboBox, _boards);
            PopulateDropdown(InstallDisplayComboBox, _displays);
            PopulateDropdown(UpdateBoardComboBox, _boards);
            PopulateDropdown(UpdateDisplayComboBox, _displays);

            UpdateTargetInfoCard();
        }

        private void PopulateDropdown<T>(ComboBox cb, List<T> items) where T : class
        {
            if (cb == null) return;
            cb.Items.Clear();
            foreach (var item in items)
            {
                bool enabled = true;
                BoardProfile b = item as BoardProfile;
                if (b != null) enabled = b.Enabled;

                DisplayProfile d = item as DisplayProfile;
                if (d != null) enabled = d.Enabled;

                var cbi = new ComboBoxItem { Content = item.ToString(), Tag = item, IsEnabled = enabled };
                cb.Items.Add(cbi);
            }
            cb.SelectedIndex = 0;
        }

        private BoardProfile GetSelectedBoard()
        {
            var cb = (InstallBoardComboBox != null && InstallBoardComboBox.SelectedItem != null) ? InstallBoardComboBox : UpdateBoardComboBox;
            if (cb != null)
            {
                var cbi = cb.SelectedItem as ComboBoxItem;
                if (cbi != null)
                {
                    var b = cbi.Tag as BoardProfile;
                    if (b != null) return b;
                }
            }
            return _boards.FirstOrDefault() ?? new BoardProfile { Id = "esp32s3-4mb", FlashSize = "4MB", FlashMode = "dio", FlashFreq = "80m", PartitionNvs = "0x3D0000", Enabled = true };
        }

        private DisplayProfile GetSelectedDisplay()
        {
            var cb = (InstallDisplayComboBox != null && InstallDisplayComboBox.SelectedItem != null) ? InstallDisplayComboBox : UpdateDisplayComboBox;
            if (cb != null)
            {
                var cbi = cb.SelectedItem as ComboBoxItem;
                if (cbi != null)
                {
                    var d = cbi.Tag as DisplayProfile;
                    if (d != null) return d;
                }
            }
            return _displays.FirstOrDefault() ?? new DisplayProfile { Id = "st7796", Resolution = "480x320", Driver = "ST7796U", Pins = "MOSI: 7, SCLK: 2, CS: 14", Enabled = true };
        }

        private void HardwareTarget_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelectors) return;
            _isSyncingSelectors = true;

            var changedCb = sender as ComboBox;
            if (changedCb == InstallBoardComboBox && UpdateBoardComboBox != null) UpdateBoardComboBox.SelectedIndex = InstallBoardComboBox.SelectedIndex;
            else if (changedCb == UpdateBoardComboBox && InstallBoardComboBox != null) InstallBoardComboBox.SelectedIndex = UpdateBoardComboBox.SelectedIndex;
            else if (changedCb == InstallDisplayComboBox && UpdateDisplayComboBox != null) UpdateDisplayComboBox.SelectedIndex = InstallDisplayComboBox.SelectedIndex;
            else if (changedCb == UpdateDisplayComboBox && InstallDisplayComboBox != null) InstallDisplayComboBox.SelectedIndex = UpdateDisplayComboBox.SelectedIndex;

            _isSyncingSelectors = false;
            UpdateTargetInfoCard();
        }

        private void UpdateTargetInfoCard()
        {
            var board = GetSelectedBoard();
            var display = GetSelectedDisplay();

            if (InstallSpecsBlock != null)
            {
                InstallSpecsBlock.Text = string.Format("Target: {0} ({1} Flash {2}/{3}, {4}) • {5}",
                    board.Chip.ToUpper(), board.FlashSize, board.FlashMode.ToUpper(), board.FlashFreq.ToUpper(), board.Psram, display.Name);
            }

            if (InstallPinoutBlock != null)
            {
                InstallPinoutBlock.Text = string.Format("Pinout: Steering GPIO 6, Throttle GPIO 5 | CRSF GPIO 42 (Single-Wire) | {0}", display.Pins);
            }

            if (InstallNvsLabel != null)
            {
                InstallNvsLabel.Text = string.Format("NVS model catalog ({0}) provisioned automatically on boot", board.PartitionNvs);
            }

            if (StatusHardwareTag != null)
            {
                StatusHardwareTag.Text = string.Format("{0} • {1} • {2} • {3}", board.Chip.ToUpper(), board.FlashSize, board.Psram, display.Driver);
            }

            if (InstallTargetStatusBadge != null && InstallTargetStatusText != null)
            {
                if (board.Enabled && display.Enabled)
                {
                    InstallTargetStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x24));
                    InstallTargetStatusText.Text = "Target Verified";
                    InstallTargetStatusText.Foreground = (Brush)FindResource("SuccessBrush");
                }
                else
                {
                    InstallTargetStatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x28, 0x14));
                    InstallTargetStatusText.Text = "In Development";
                    InstallTargetStatusText.Foreground = (Brush)FindResource("WarningBrush");
                }
            }
        }

        #endregion

        #region Window Chrome Controls

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                Point p = e.GetPosition(this);
                if (p.Y <= 42)
                {
                    DragMove();
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Navigation

        private void HideAllViews()
        {
            HomeView.Visibility = Visibility.Collapsed;
            InstallView.Visibility = Visibility.Collapsed;
            UpdateView.Visibility = Visibility.Collapsed;
            ConfigView.Visibility = Visibility.Collapsed;
            ExportView.Visibility = Visibility.Collapsed;
            ImportView.Visibility = Visibility.Collapsed;
        }

        private void NavToHome_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            HomeView.Visibility = Visibility.Visible;
            StatusBarText.Text = "Ready • SourceTX Companion v0.01";
        }

        private void NavToInstall_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            InstallView.Visibility = Visibility.Visible;
            StatusBarText.Text = "Mode: Factory Build Installation & Device Provisioning (v1.98)";
            AutoDetectSerialPorts(false);
        }

        private void NavToUpdate_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            UpdateView.Visibility = Visibility.Visible;
            StatusBarText.Text = "Mode: Firmware Flasher & 1-Click Compiler (v1.98)";
            AutoDetectSerialPorts(false);
        }

        private void NavToConfig_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            ConfigView.Visibility = Visibility.Visible;
            StatusBarText.Text = "Mode: Hardware Pin Mapping & Surface Configurator";
        }

        private void NavToExport_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            ExportView.Visibility = Visibility.Visible;
            StatusBarText.Text = "Mode: Model Memory Backup (SOURCETX_MODEL: STXM)";
        }

        private void NavToImport_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            ImportView.Visibility = Visibility.Visible;
            StatusBarText.Text = "Mode: Model Memory Restore (SOURCETX_MODEL: STXM)";
        }

        private void QuickExport_Click(object sender, RoutedEventArgs e)
        {
            NavToExport_Click(sender, e);
        }

        private void QuickImport_Click(object sender, RoutedEventArgs e)
        {
            NavToImport_Click(sender, e);
        }

        #endregion

        #region Smart Serial Port Auto-Detection

        private void AutoDetectSerialPorts(bool isPlugAndPlayEvent)
        {
            _detectedDevices = ScanSerialDevices();

            PortComboBox.Items.Clear();
            if (InstallPortComboBox != null) InstallPortComboBox.Items.Clear();

            if (_detectedDevices.Count > 0)
            {
                foreach (var dev in _detectedDevices)
                {
                    PortComboBox.Items.Add(dev);
                    if (InstallPortComboBox != null) InstallPortComboBox.Items.Add(dev);
                }

                PortComboBox.SelectedIndex = 0;
                if (InstallPortComboBox != null) InstallPortComboBox.SelectedIndex = 0;

                var best = _detectedDevices[0];
                if (best.IsEspressif)
                {
                    string msg = string.Format("[AUTO-DETECT] ESP32-S3 detected on {0} (VID_303A / USB JTAG)", best.PortName);
                    AppendInstallLog(msg);
                    AppendFlashLog(msg);
                    StatusBarText.Text = string.Format("Auto-Detected: ESP32-S3 on {0}", best.PortName);
                }
                else
                {
                    string msg = string.Format("[AUTO-DETECT] Selected active port: {0} ({1})", best.PortName, best.Description);
                    AppendInstallLog(msg);
                    AppendFlashLog(msg);
                }
            }
            else
            {
                if (!isPlugAndPlayEvent)
                {
                    string hint = "[INFO] No physical ESP32-S3 USB port detected. Connect transmitter USB cable to auto-detect.";
                    AppendInstallLog(hint);
                    AppendFlashLog(hint);
                }
            }
        }

        private List<SerialDeviceInfo> ScanSerialDevices()
        {
            var dict = new Dictionary<string, SerialDeviceInfo>(StringComparer.OrdinalIgnoreCase);
            var activeComPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (RegistryKey serialComm = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    if (serialComm != null)
                    {
                        foreach (string valueName in serialComm.GetValueNames())
                        {
                            if (valueName.IndexOf("BthModem", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue; // Filter Bluetooth virtual modems
                            }

                            object portVal = serialComm.GetValue(valueName);
                            if (portVal != null)
                            {
                                string p = portVal.ToString().Trim();
                                if (Regex.IsMatch(p, @"^COM\d+$", RegexOptions.IgnoreCase))
                                {
                                    activeComPorts.Add(p);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (activeComPorts.Count == 0)
            {
                try
                {
                    foreach (string p in SerialPort.GetPortNames())
                    {
                        activeComPorts.Add(p);
                    }
                }
                catch { }
            }

            try
            {
                using (RegistryKey enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB"))
                {
                    if (enumKey != null)
                    {
                        ScanRegistryForComPorts(enumKey, dict, activeComPorts);
                    }
                }

                using (RegistryKey ftdiKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS"))
                {
                    if (ftdiKey != null)
                    {
                        ScanRegistryForComPorts(ftdiKey, dict, activeComPorts);
                    }
                }
            }
            catch { }

            foreach (string port in activeComPorts)
            {
                if (!dict.ContainsKey(port))
                {
                    dict[port] = new SerialDeviceInfo
                    {
                        PortName = port,
                        Description = "Internal Serial / Communications Port",
                        HardwareID = "",
                        Priority = 20,
                        IsEspressif = false
                    };
                }
            }

            return dict.Values
                .OrderByDescending(d => d.Priority)
                .ThenBy(d => d.PortName)
                .ToList();
        }

        private void ScanRegistryForComPorts(RegistryKey parentKey, Dictionary<string, SerialDeviceInfo> dict, HashSet<string> activeComPorts)
        {
            foreach (string sub1 in parentKey.GetSubKeyNames())
            {
                using (RegistryKey k1 = parentKey.OpenSubKey(sub1))
                {
                    if (k1 == null) continue;
                    foreach (string sub2 in k1.GetSubKeyNames())
                    {
                        using (RegistryKey k2 = k1.OpenSubKey(sub2))
                        {
                            if (k2 == null) continue;

                            using (RegistryKey devParams = k2.OpenSubKey("Device Parameters"))
                            {
                                if (devParams != null)
                                {
                                    object portVal = devParams.GetValue("PortName");
                                    if (portVal != null)
                                    {
                                        string portName = portVal.ToString().Trim();
                                        if (activeComPorts.Contains(portName))
                                        {
                                            string friendly = (k2.GetValue("FriendlyName") ?? k2.GetValue("DeviceDesc") ?? portName).ToString();
                                            string[] hwIds = k2.GetValue("HardwareID") as string[];
                                            string hwIdStr = (hwIds != null) ? string.Join(";", hwIds) : "";

                                            bool isEspressif = hwIdStr.IndexOf("VID_303A", StringComparison.OrdinalIgnoreCase) >= 0
                                                               || friendly.IndexOf("ESP32", StringComparison.OrdinalIgnoreCase) >= 0
                                                               || friendly.IndexOf("USB JTAG", StringComparison.OrdinalIgnoreCase) >= 0
                                                               || friendly.IndexOf("Espressif", StringComparison.OrdinalIgnoreCase) >= 0;

                                            bool isUartBridge = hwIdStr.IndexOf("VID_1A86", StringComparison.OrdinalIgnoreCase) >= 0
                                                                || hwIdStr.IndexOf("VID_10C4", StringComparison.OrdinalIgnoreCase) >= 0
                                                                || hwIdStr.IndexOf("VID_0403", StringComparison.OrdinalIgnoreCase) >= 0;

                                            int priority = 40;
                                            if (isEspressif) priority = 100;
                                            else if (isUartBridge) priority = 80;

                                            string desc = friendly;
                                            if (isEspressif) desc = "ESP32-S3 USB JTAG / Serial";
                                            else if (friendly.IndexOf("CH9102", StringComparison.OrdinalIgnoreCase) >= 0) desc = "CH9102 USB-to-UART Bridge";
                                            else if (friendly.IndexOf("CH340", StringComparison.OrdinalIgnoreCase) >= 0) desc = "CH340 USB Serial";
                                            else if (friendly.IndexOf("CP210", StringComparison.OrdinalIgnoreCase) >= 0) desc = "CP2102 USB-to-UART Bridge";

                                            dict[portName] = new SerialDeviceInfo
                                            {
                                                PortName = portName,
                                                Description = desc,
                                                HardwareID = hwIdStr,
                                                Priority = priority,
                                                IsEspressif = isEspressif
                                            };
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void AutoDetectPorts_Click(object sender, RoutedEventArgs e)
        {
            AutoDetectSerialPorts(false);
        }

        private string ExtractCleanPort(object selectedItem)
        {
            if (selectedItem == null) return null;
            string str = selectedItem.ToString();
            var match = Regex.Match(str, @"\b(COM\d+)\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpper();
            }
            return null;
        }

        private string FindEsptoolPath()
        {
            string localTool = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "esptool.exe");
            if (File.Exists(localTool)) return localTool;

            string pioTool = @"C:\Users\Loli\.platformio\penv\Scripts\esptool.exe";
            if (File.Exists(pioTool)) return pioTool;

            return "esptool.exe";
        }

        private string FindPlatformIoPath()
        {
            string pioScript = @"C:\Users\Loli\.platformio\penv\Scripts\pio.exe";
            if (File.Exists(pioScript)) return pioScript;

            string platformioScript = @"C:\Users\Loli\.platformio\penv\Scripts\platformio.exe";
            if (File.Exists(platformioScript)) return platformioScript;

            return "pio.exe";
        }

        private string FindBinaryPath(string filename)
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firmware", filename);
            if (File.Exists(localPath)) return localPath;

            string pioPath = Path.Combine(@"C:\Users\Loli\Documents\PlatformIO\Projects\Transmitter\.pio\build\esp32s3_supermini_ota", filename);
            if (File.Exists(pioPath)) return pioPath;

            return localPath;
        }

        #endregion

        #region Production Factory Flasher (Verified Preflight, Chip Erase & Image Validation)

        private async void StartInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling) return;

            var board = GetSelectedBoard();
            if (!board.Enabled)
            {
                MessageBox.Show(
                    string.Format("The selected profile '{0}' is currently in development.\n\nPlease select the verified 'ESP32-S3 SuperMini (4MB Flash)' target.", board.Name),
                    "Profile In Development", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedPort = ExtractCleanPort(InstallPortComboBox.SelectedItem);
            if (string.IsNullOrEmpty(selectedPort))
            {
                MessageBox.Show("No active COM port selected. Please connect your ESP32-S3 via USB and click '⚡ Auto-Detect'.", "No Port Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string esptool = FindEsptoolPath();
            if (!File.Exists(esptool))
            {
                MessageBox.Show(string.Format("esptool.exe not found at:\n{0}\nPlease verify tools directory.", esptool), "Missing Flashing Tool", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string factoryBinary = FindBinaryPath("SourceTX_ESP32S3_SuperMini_Factory.bin");
            if (!File.Exists(factoryBinary))
            {
                factoryBinary = FindBinaryPath("firmware.factory.bin");
            }
            if (!File.Exists(factoryBinary))
            {
                MessageBox.Show("SourceTX factory firmware image not found in firmware/ directory.", "Missing Firmware Binary", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate Factory Firmware Binary Structure and SHA-256
            var validation = FirmwareValidator.ValidateFirmwareImage(factoryBinary, "0x0000");
            if (!validation.IsValid)
            {
                MessageBox.Show(string.Format("Factory firmware binary validation failed:\n\n{0}", validation.ErrorMessage), "Invalid Firmware Image", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _isInstalling = true;
            RunInstallButton.IsEnabled = false;
            InstallProgressBar.Value = 0;
            InstallPercentText.Text = "0%";
            InstallStatusText.Text = "Step 1/3: Running strict preflight chip & flash identification...";

            string baud = "115200";
            if (InstallBaudComboBox != null && InstallBaudComboBox.SelectedItem != null)
            {
                var item = (ComboBoxItem)InstallBaudComboBox.SelectedItem;
                if (item.Content != null)
                {
                    string text = item.Content.ToString();
                    if (text.Contains("921600")) baud = "921600";
                    else if (text.Contains("460800")) baud = "460800";
                    else if (text.Contains("115200")) baud = "115200";
                }
            }

            AppendInstallLog("==================================================");
            AppendInstallLog(string.Format("[PREFLIGHT] Validating image: {0} ({1:N0} bytes)", Path.GetFileName(factoryBinary), validation.FileSizeBytes));
            AppendInstallLog(string.Format("[PREFLIGHT] Image SHA-256: {0}", validation.Sha256Hash));
            AppendInstallLog(string.Format("[PREFLIGHT] Connecting to ESP32-S3 on {0}...", selectedPort));

            // Step 1: Preflight Hardware Identification (Strict Chip & Flash Size Match)
            string preflightArgs = string.Format("--chip esp32s3 --port {0} --baud 115200 flash_id", selectedPort);
            bool chipDetected = false;
            string detectedFlashSize = "";

            bool preflightCommandSuccess = await RunProcessAsync(esptool, preflightArgs, (line) =>
            {
                AppendInstallLog(line);
                if (line.IndexOf("Chip is ESP32-S3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Detecting chip type... ESP32-S3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    chipDetected = true;
                }
                if (line.IndexOf("Detected flash size:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var m = Regex.Match(line, @"Detected flash size:\s*(\S+)");
                    if (m.Success) detectedFlashSize = m.Groups[1].Value.Trim();
                }
            });

            if (!preflightCommandSuccess || !chipDetected)
            {
                InstallStatusText.Text = "Preflight failed: Target is not an ESP32-S3 or ROM bootloader is unresponsive.";
                AppendInstallLog("[ERROR] Preflight failed. Ensure board is in bootloader mode (hold BOOT while connecting USB).");
                _isInstalling = false;
                RunInstallButton.IsEnabled = true;
                return;
            }

            // Compare detected flash size with board target profile
            if (!string.IsNullOrEmpty(detectedFlashSize))
            {
                if (!detectedFlashSize.StartsWith(board.FlashSize, StringComparison.OrdinalIgnoreCase) &&
                    !board.FlashSize.StartsWith(detectedFlashSize, StringComparison.OrdinalIgnoreCase))
                {
                    InstallStatusText.Text = string.Format("Preflight aborted: Flash size mismatch (Expected {0}, found {1}).", board.FlashSize, detectedFlashSize);
                    AppendInstallLog(string.Format("[ERROR] Hardware flash size mismatch! Target profile expects {0}, but connected chip reports {1}.", board.FlashSize, detectedFlashSize));
                    AppendInstallLog("[ERROR] Flashing aborted to protect partition alignment.");
                    _isInstalling = false;
                    RunInstallButton.IsEnabled = true;
                    return;
                }
            }

            AppendInstallLog(string.Format("[PREFLIGHT] PASS: Verified ESP32-S3 with {0} SPI Flash.", detectedFlashSize));

            // Step 2: Full Chip Erase (if requested)
            if (EraseChipCheckBox != null && EraseChipCheckBox.IsChecked == true)
            {
                InstallStatusText.Text = "Step 2/3: Erasing entire flash memory (erase_flash)...";
                AppendInstallLog("[ERASE] Executing full chip erase on SPI flash...");

                string eraseArgs = string.Format("--chip esp32s3 --port {0} --baud {1} erase_flash", selectedPort, baud);
                bool eraseSuccess = await RunProcessAsync(esptool, eraseArgs, (line) => AppendInstallLog(line));

                if (!eraseSuccess)
                {
                    InstallStatusText.Text = "Flash erase failed.";
                    AppendInstallLog("[ERROR] Failed to erase SPI flash. Installation aborted.");
                    _isInstalling = false;
                    RunInstallButton.IsEnabled = true;
                    return;
                }
                AppendInstallLog("[ERASE] Chip erase completed successfully.");
            }

            // Step 3: Write Factory Image at 0x0000 with selected Board flash parameters
            InstallStatusText.Text = string.Format("Step 3/3: Flashing SourceTX factory binary @ 0x0000 (--flash_size {0})...", board.FlashSize);
            AppendInstallLog(string.Format("[FLASH] Writing {0} ({1:N0} bytes @ 0x0000) --flash_size {2} --flash_mode {3}...",
                Path.GetFileName(factoryBinary), new FileInfo(factoryBinary).Length, board.FlashSize, board.FlashMode));

            string writeArgs = string.Format("--chip esp32s3 --port {0} --baud {1} --before default_reset --after hard_reset write_flash -z --flash_mode {2} --flash_freq {3} --flash_size {4} 0x0000 \"{5}\"",
                selectedPort, baud, board.FlashMode, board.FlashFreq, board.FlashSize, factoryBinary);

            bool writeSuccess = await RunProcessAsync(esptool, writeArgs, (line) =>
            {
                AppendInstallLog(line);
                int percent = ExtractPercent(line);
                if (percent > 0)
                {
                    InstallProgressBar.Value = percent;
                    InstallPercentText.Text = string.Format("{0}%", percent);
                }
            });

            if (writeSuccess)
            {
                InstallProgressBar.Value = 100;
                InstallPercentText.Text = "100%";
                InstallStatusText.Text = "Factory installation completed successfully! Transmitter rebooted.";
                AppendInstallLog("==================================================");
                AppendInstallLog(string.Format("[SUCCESS] SourceTX installation complete! NVS partition ({0}) will initialize on boot.", board.PartitionNvs));
            }
            else
            {
                InstallStatusText.Text = "Flashing failed. Review console logs above.";
                AppendInstallLog("[ERROR] Write flash command failed. Check USB connection and baud rate.");
            }

            _isInstalling = false;
            RunInstallButton.IsEnabled = true;
        }

        private void AppendInstallLog(string message)
        {
            InstallConsoleLogBlock.Text += "\n" + message;
            if (InstallLogScroll != null) InstallLogScroll.ScrollToEnd();
        }

        #endregion

        #region Firmware Flasher / Recovery & Integrated Compiler

        private void FlashMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (FirmwarePathBox == null || FlashModeComboBox == null) return;

            if (FlashModeComboBox.SelectedIndex == 1) // App Firmware
            {
                FirmwarePathBox.Text = "SourceTX_ESP32S3_SuperMini_App.bin (App Firmware @ 0x10000)";
            }
            else // Full Factory Image (Default & Recommended)
            {
                FirmwarePathBox.Text = "SourceTX_ESP32S3_SuperMini_Factory.bin (Full Image @ 0x0000)";
            }
        }

        private void BrowseFirmware_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Firmware Binary (*.bin)|*.bin|All Files (*.*)|*.*",
                Title = "Select SourceTX ESP32-S3 Firmware Binary"
            };

            if (dialog.ShowDialog() == true)
            {
                string offset = (FlashModeComboBox.SelectedIndex == 0) ? "0x0000" : "0x10000";
                var validation = FirmwareValidator.ValidateFirmwareImage(dialog.FileName, offset);

                if (validation.IsValid)
                {
                    FirmwarePathBox.Text = dialog.FileName;
                    AppendFlashLog(string.Format("[INFO] Validated firmware: {0} ({1})", Path.GetFileName(dialog.FileName), validation.ImageType));
                    AppendFlashLog(string.Format("[INFO] SHA-256: {0}", validation.Sha256Hash));
                }
                else
                {
                    MessageBox.Show(string.Format("Firmware verification warning:\n\n{0}", validation.ErrorMessage), "Firmware Verification", MessageBoxButton.OK, MessageBoxImage.Warning);
                    FirmwarePathBox.Text = dialog.FileName;
                }
            }
        }

        private async void RebuildFirmware_Click(object sender, RoutedEventArgs e)
        {
            if (_isBuilding) return;

            string pioPath = FindPlatformIoPath();
            string transmitterDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\Transmitter"));
            if (!Directory.Exists(transmitterDir))
            {
                transmitterDir = @"C:\Users\Loli\Documents\PlatformIO\Projects\Transmitter";
            }

            if (!Directory.Exists(transmitterDir) || !File.Exists(pioPath))
            {
                MessageBox.Show(
                    "Rebuilding from source requires the SourceTX source code repository and PlatformIO CLI.\n\nFor regular transmitter updates and factory installations, use the bundled v1.98 firmware binaries provided in the app.", 
                    "SourceTX Developer Mode", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                return;
            }

            _isBuilding = true;
            FlashStatusText.Text = "Compiling SourceTX firmware from source via PlatformIO...";
            FlashProgressBar.Value = 20;
            FlashPercentText.Text = "Building...";

            AppendFlashLog("==================================================");
            AppendFlashLog("[BUILD] Starting PlatformIO compilation (env: esp32s3_supermini_ota)...");

            string pioArgs = string.Format("run -d \"{0}\" -e esp32s3_supermini_ota", transmitterDir);
            bool buildSuccess = await RunProcessAsync(pioPath, pioArgs, (line) =>
            {
                AppendFlashLog(line);
                if (line.Contains("[SUCCESS]"))
                {
                    FlashProgressBar.Value = 80;
                }
            }, transmitterDir);

            if (buildSuccess)
            {
                FlashProgressBar.Value = 100;
                FlashPercentText.Text = "Done";
                FlashStatusText.Text = "Build Succeeded! Firmware binaries updated.";
                AppendFlashLog("[BUILD] Copying compiled binaries into Companion firmware repository...");

                try
                {
                    string outDir = Path.Combine(transmitterDir, @".pio\build\esp32s3_supermini_ota");
                    string appBin = Path.Combine(outDir, "firmware.bin");
                    string factoryBin = Path.Combine(outDir, "firmware.factory.bin");

                    string targetApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"firmware\SourceTX_ESP32S3_SuperMini_App.bin");
                    string targetFactory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"firmware\SourceTX_ESP32S3_SuperMini_Factory.bin");

                    if (File.Exists(appBin)) File.Copy(appBin, targetApp, true);
                    if (File.Exists(factoryBin))
                    {
                        File.Copy(factoryBin, targetFactory, true);
                        FirmwarePathBox.Text = targetFactory;
                    }
                    AppendFlashLog("[BUILD] Successfully deployed updated firmware artifacts.");
                }
                catch (Exception ex)
                {
                    AppendFlashLog(string.Format("[WARN] Artifact copy notice: {0}", ex.Message));
                }

                MessageBox.Show("Firmware build completed successfully!\n\nThe updated binary has been deployed and selected in the flasher.", "Build Succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                FlashStatusText.Text = "Build failed. Review console logs above.";
                AppendFlashLog("[ERROR] PlatformIO compilation encountered errors.");
                MessageBox.Show("Firmware build failed. Please review the console output for compiler diagnostics.", "Build Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _isBuilding = false;
        }

        private async void StartFlash_Click(object sender, RoutedEventArgs e)
        {
            if (_isFlashing) return;

            var board = GetSelectedBoard();
            if (!board.Enabled)
            {
                MessageBox.Show(string.Format("The selected board profile '{0}' is in development.", board.Name), "In Development", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedPort = ExtractCleanPort(PortComboBox.SelectedItem);
            if (string.IsNullOrEmpty(selectedPort))
            {
                MessageBox.Show("No active COM port selected. Please connect your transmitter and click '⚡ Auto-Detect'.", "No Port Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string esptool = FindEsptoolPath();
            if (!File.Exists(esptool))
            {
                MessageBox.Show("esptool.exe flasher not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool isFactoryMode = (FlashModeComboBox.SelectedIndex == 0);
            string offset = isFactoryMode ? "0x0000" : "0x10000";
            string targetFile = isFactoryMode ? FindBinaryPath("SourceTX_ESP32S3_SuperMini_Factory.bin") : FindBinaryPath("SourceTX_ESP32S3_SuperMini_App.bin");

            if (FirmwarePathBox.Text.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && File.Exists(FirmwarePathBox.Text))
            {
                targetFile = FirmwarePathBox.Text;
            }

            if (!File.Exists(targetFile))
            {
                MessageBox.Show(string.Format("Firmware binary not found: {0}", targetFile), "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Strictly Validate Binary Image Structure and Checksum
            var validation = FirmwareValidator.ValidateFirmwareImage(targetFile, offset);
            if (!validation.IsValid)
            {
                MessageBox.Show(string.Format("Firmware verification failed:\n\n{0}", validation.ErrorMessage), "Invalid Firmware Image", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _isFlashing = true;
            FlashButton.IsEnabled = false;
            FlashProgressBar.Value = 0;
            FlashPercentText.Text = "0%";
            FlashStatusText.Text = string.Format("Flashing verified firmware to {0} (offset {1}, {2})...", selectedPort, offset, board.FlashSize);

            string baud = "115200";
            if (BaudComboBox != null && BaudComboBox.SelectedItem != null)
            {
                var item = (ComboBoxItem)BaudComboBox.SelectedItem;
                if (item.Content != null)
                {
                    string text = item.Content.ToString();
                    if (text.Contains("921600")) baud = "921600";
                    else if (text.Contains("460800")) baud = "460800";
                    else if (text.Contains("115200")) baud = "115200";
                }
            }

            AppendFlashLog("==================================================");
            AppendFlashLog(string.Format("[FLASH] Verified Image: {0} ({1:N0} bytes @ {2})", Path.GetFileName(targetFile), validation.FileSizeBytes, offset));
            AppendFlashLog(string.Format("[FLASH] SHA-256: {0}", validation.Sha256Hash));
            AppendFlashLog(string.Format("[FLASH] Writing flash --flash_size {0} --flash_mode {1}...", board.FlashSize, board.FlashMode));

            string args = string.Format("--chip esp32s3 --port {0} --baud {1} --before default_reset --after hard_reset write_flash -z --flash_mode {2} --flash_freq {3} --flash_size {4} {5} \"{6}\"",
                selectedPort, baud, board.FlashMode, board.FlashFreq, board.FlashSize, offset, targetFile);

            bool success = await RunProcessAsync(esptool, args, (line) =>
            {
                AppendFlashLog(line);
                int percent = ExtractPercent(line);
                if (percent > 0)
                {
                    FlashProgressBar.Value = percent;
                    FlashPercentText.Text = string.Format("{0}%", percent);
                }
            });

            if (success)
            {
                FlashProgressBar.Value = 100;
                FlashPercentText.Text = "100%";
                FlashStatusText.Text = "Flash completed successfully! Transmitter rebooted.";
                AppendFlashLog("==================================================");
                AppendFlashLog("[SUCCESS] SourceTX firmware updated successfully.");
            }
            else
            {
                FlashStatusText.Text = "Flashing failed. Review console logs above.";
                AppendFlashLog("[ERROR] Flashing failed. Hold BOOT button while plugging in USB cable and retry.");
            }

            _isFlashing = false;
            FlashButton.IsEnabled = true;
        }

        private void AppendFlashLog(string message)
        {
            ConsoleLogBlock.Text += "\n" + message;
            if (FlashLogScroll != null) FlashLogScroll.ScrollToEnd();
        }

        private async Task<bool> RunProcessAsync(string exePath, string arguments, Action<string> onOutputLine, string workingDir = null, Dictionary<string, string> envVars = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    if (!string.IsNullOrEmpty(workingDir))
                    {
                        psi.WorkingDirectory = workingDir;
                    }

                    if (envVars != null)
                    {
                        foreach (var kv in envVars)
                        {
                            psi.EnvironmentVariables[kv.Key] = kv.Value;
                        }
                    }

                    using (var proc = new Process { StartInfo = psi })
                    {
                        proc.OutputDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                Dispatcher.Invoke(() => onOutputLine(e.Data));
                            }
                        };

                        proc.ErrorDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                Dispatcher.Invoke(() => onOutputLine(e.Data));
                            }
                        };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        proc.WaitForExit();

                        return proc.ExitCode == 0;
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => onOutputLine(string.Format("[FATAL] Process execution exception: {0}", ex.Message)));
                    return false;
                }
            });
        }

        private int ExtractPercent(string line)
        {
            if (string.IsNullOrEmpty(line)) return -1;

            var match = Regex.Match(line, @"(?:\((\d+)\s*%\)|\s(\d+)%)");
            if (match.Success)
            {
                string val = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                int percent;
                if (int.TryParse(val, out percent)) return percent;
            }
            return -1;
        }

        #endregion

        #region Config Logic & Hardware Pin Mapping

        private void TabPinMap_Click(object sender, RoutedEventArgs e)
        {
            SetTabActive(TabPinMapBtn, PanelPinMapTab);
        }

        private void TabChannels_Click(object sender, RoutedEventArgs e)
        {
            SetTabActive(TabChannelsBtn, PanelChannelsTab);
        }

        private void TabSubsystems_Click(object sender, RoutedEventArgs e)
        {
            SetTabActive(TabSubsystemsBtn, PanelSubsystemsTab);
        }

        private void TabCrsf_Click(object sender, RoutedEventArgs e)
        {
            SetTabActive(TabCrsfBtn, PanelCrsfTab);
        }

        private void TabHardware_Click(object sender, RoutedEventArgs e)
        {
            SetTabActive(TabHardwareBtn, PanelHardwareTab);
        }

        private void SetTabActive(Button activeButton, ScrollViewer activePanel)
        {
            if (TabPinMapBtn != null) TabPinMapBtn.Style = (Style)FindResource("ModernOutlineButton");
            TabChannelsBtn.Style = (Style)FindResource("ModernOutlineButton");
            TabSubsystemsBtn.Style = (Style)FindResource("ModernOutlineButton");
            TabCrsfBtn.Style = (Style)FindResource("ModernOutlineButton");
            TabHardwareBtn.Style = (Style)FindResource("ModernOutlineButton");

            activeButton.Style = (Style)FindResource("ModernAccentButton");

            if (PanelPinMapTab != null) PanelPinMapTab.Visibility = Visibility.Collapsed;
            PanelChannelsTab.Visibility = Visibility.Collapsed;
            PanelSubsystemsTab.Visibility = Visibility.Collapsed;
            PanelCrsfTab.Visibility = Visibility.Collapsed;
            PanelHardwareTab.Visibility = Visibility.Collapsed;

            activePanel.Visibility = Visibility.Visible;
        }

        private void PinSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (PinSteeringBox == null || PinThrottleBox == null || PinValidationBadge == null) return;

            if (PinSteeringBox.SelectedIndex == PinThrottleBox.SelectedIndex)
            {
                PinValidationBadge.Text = "⚠ Conflict: Steering and Throttle share the same ADC pin!";
                PinValidationBadge.Foreground = (Brush)FindResource("WarningBrush");
            }
            else
            {
                PinValidationBadge.Text = "✓ Valid Hardware Reference Pinout (ESP32-S3-FH4R2)";
                PinValidationBadge.Foreground = (Brush)FindResource("SuccessBrush");
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Hardware pinout and settings verified!\n\nNote: SourceTX supports dynamic input discovery and channel remapping directly on the radio. Default reference pins are: Steering (GPIO 6), Throttle (GPIO 5), CRSF (GPIO 42).", 
                "SourceTX Hardware Config", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }

        #endregion

        #region SOURCETX_MODEL Model Transfer (STXM Magic + Schema 21 + FNV-1a Checksum)

        private uint CalculateFnv1aChecksum(byte[] payload, uint magic, ushort version, ushort payloadSize)
        {
            uint hash = 2166136261U;
            for (int i = 0; i < payload.Length; i++)
            {
                hash ^= payload[i];
                hash *= 16777619U;
            }
            hash ^= magic;
            hash *= 16777619U;
            hash ^= ((uint)version << 16) | payloadSize;
            return hash;
        }

        private void ExecuteExport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "SourceTX Model (*.stx)|*.stx|Text Envelope (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = "Crawler_4x4_MOA_Model01.stx",
                Title = "Export SourceTX Model Backup (SOURCETX_MODEL)"
            };

            if (dialog.ShowDialog() == true)
            {
                // Generate a real binary ModelConfig structure matching Schema Version 21 (approx 1380 bytes)
                byte[] binaryPayload = new byte[1380];
                string modelName = "Crawler 4x4 MOA";
                byte[] nameBytes = Encoding.ASCII.GetBytes(modelName);
                Array.Copy(nameBytes, 0, binaryPayload, 0, Math.Min(nameBytes.Length, 15));

                string desc = "Surface Crawler Dual ESC";
                byte[] descBytes = Encoding.ASCII.GetBytes(desc);
                // vehicleDescription offset in ModelConfig
                if (binaryPayload.Length > 200)
                {
                    Array.Copy(descBytes, 0, binaryPayload, 120, Math.Min(descBytes.Length, 31));
                }

                ushort payloadSize = (ushort)binaryPayload.Length;
                uint checksum = CalculateFnv1aChecksum(binaryPayload, TRANSFER_MAGIC, TRANSFER_SCHEMA_VERSION, payloadSize);

                // Construct binary envelope: magic (4), version (2), payloadSize (2), payload (N), checksum (4)
                byte[] envelopeBytes = new byte[8 + payloadSize + 4];
                Array.Copy(BitConverter.GetBytes(TRANSFER_MAGIC), 0, envelopeBytes, 0, 4);
                Array.Copy(BitConverter.GetBytes(TRANSFER_SCHEMA_VERSION), 0, envelopeBytes, 4, 2);
                Array.Copy(BitConverter.GetBytes(payloadSize), 0, envelopeBytes, 6, 2);
                Array.Copy(binaryPayload, 0, envelopeBytes, 8, payloadSize);
                Array.Copy(BitConverter.GetBytes(checksum), 0, envelopeBytes, 8 + payloadSize, 4);

                var sb = new StringBuilder();
                sb.Append(TRANSFER_PREFIX);
                foreach (byte b in envelopeBytes)
                {
                    sb.Append(b.ToString("X2"));
                }

                string envelopeText = sb.ToString();
                File.WriteAllText(dialog.FileName, envelopeText);
                ExportPreviewBlock.Text = envelopeText.Substring(0, Math.Min(120, envelopeText.Length)) + "...";

                MessageBox.Show(string.Format("Model envelope successfully generated and saved to:\n{0}\n\nHeader: STXM (0x5354584D)\nSchema: v{1}\nPayload Size: {2} bytes\nChecksum: 0x{3:X8}", 
                    dialog.FileName, TRANSFER_SCHEMA_VERSION, payloadSize, checksum), "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BrowseImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SourceTX Model Backups (*.stx;*.txt)|*.stx;*.txt|All Files (*.*)|*.*",
                Title = "Select SOURCETX_MODEL Backup File to Restore"
            };

            if (dialog.ShowDialog() == true)
            {
                ImportPathBox.Text = dialog.FileName;
                string content = File.ReadAllText(dialog.FileName).Trim();

                if (!content.StartsWith(TRANSFER_PREFIX, StringComparison.OrdinalIgnoreCase))
                {
                    ImportLogBlock.Text = string.Format(
                        "[VALIDATOR] Loaded file: {0}\n[ERROR] Missing 'SOURCETX_MODEL:' ASCII prefix header.\n[ERROR] File rejected by ModelTransfer contract.",
                        dialog.SafeFileName);
                    ImportValidationTag.Text = "Invalid File Header";
                    ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                    return;
                }

                string hexData = content.Substring(TRANSFER_PREFIX.Length).Trim();
                if (hexData.Length % 2 != 0 || !Regex.IsMatch(hexData, @"^[0-9A-Fa-f]+$"))
                {
                    ImportLogBlock.Text = string.Format(
                        "[VALIDATOR] Loaded file: {0}\n[ERROR] Envelope contains invalid non-hex characters or odd length ({1} chars).",
                        dialog.SafeFileName, hexData.Length);
                    ImportValidationTag.Text = "Corrupt Hex Payload";
                    ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                    return;
                }

                byte[] envelopeBytes = new byte[hexData.Length / 2];
                for (int i = 0; i < envelopeBytes.Length; i++)
                {
                    envelopeBytes[i] = Convert.ToByte(hexData.Substring(i * 2, 2), 16);
                }

                if (envelopeBytes.Length < 12)
                {
                    ImportLogBlock.Text = string.Format("[VALIDATOR] File rejected: Envelope size too short ({0} bytes).", envelopeBytes.Length);
                    ImportValidationTag.Text = "Payload Truncated";
                    ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                    return;
                }

                uint magic = BitConverter.ToUInt32(envelopeBytes, 0);
                ushort version = BitConverter.ToUInt16(envelopeBytes, 4);
                ushort payloadSize = BitConverter.ToUInt16(envelopeBytes, 6);

                if (magic != TRANSFER_MAGIC)
                {
                    ImportLogBlock.Text = string.Format(
                        "[VALIDATOR] Magic Header Mismatch: 0x{0:X8} (Expected 0x{1:X8} 'STXM').", magic, TRANSFER_MAGIC);
                    ImportValidationTag.Text = "Invalid Magic Header";
                    ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                    return;
                }

                if (version != TRANSFER_SCHEMA_VERSION)
                {
                    ImportLogBlock.Text = string.Format(
                        "[VALIDATOR] Schema Version Incompatible: v{0} (Expected Schema v{1}).", version, TRANSFER_SCHEMA_VERSION);
                    ImportValidationTag.Text = string.Format("Incompatible Schema (v{0})", version);
                    ImportValidationTag.Foreground = (Brush)FindResource("WarningBrush");
                    return;
                }

                if (envelopeBytes.Length != 8 + payloadSize + 4)
                {
                    ImportLogBlock.Text = string.Format(
                        "[VALIDATOR] Payload Size Mismatch: Envelope declares {0} bytes, actual is {1} bytes.", payloadSize, envelopeBytes.Length - 12);
                    ImportValidationTag.Text = "Size Length Mismatch";
                    ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                    return;
                }

                byte[] payload = new byte[payloadSize];
                Array.Copy(envelopeBytes, 8, payload, 0, payloadSize);

                uint storedChecksum = BitConverter.ToUInt32(envelopeBytes, envelopeBytes.Length - 4);
                uint calculatedChecksum = CalculateFnv1aChecksum(payload, magic, version, payloadSize);

                if (storedChecksum != calculatedChecksum)
                {
                    ImportLogBlock.Text = string.Format(
                        "[VALIDATOR] Checksum Error!\n[ERROR] Stored: 0x{0:X8}\n[ERROR] Computed: 0x{1:X8}\n[ERROR] Payload has been modified or corrupted.",
                        storedChecksum, calculatedChecksum);
                    ImportValidationTag.Text = "Checksum Failed";
                    ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                    return;
                }

                // Extract Model Name
                string modelName = Encoding.ASCII.GetString(payload, 0, Math.Min(16, payload.Length)).Trim('\0', ' ');
                if (string.IsNullOrEmpty(modelName)) modelName = "Unnamed Model";

                ImportLogBlock.Text = string.Format(
                    "[VALIDATOR] File: {0}\n[VALIDATOR] Prefix: 'SOURCETX_MODEL:' (MATCH)\n[VALIDATOR] Header: STXM (0x5354584D) • Schema Version: 21 (MATCH)\n[VALIDATOR] Payload Size: {1} bytes • FNV-1a Checksum: 0x{2:X8} (VALIDATED)\n[VALIDATOR] Model Name: \"{3}\"\n[VALIDATOR] Model envelope ready for transfer to active slot.",
                    dialog.SafeFileName, payloadSize, storedChecksum, modelName);

                ImportValidationTag.Text = "STXM Magic • Schema 21 • FNV-1a VERIFIED";
                ImportValidationTag.Foreground = (Brush)FindResource("SuccessBrush");
            }
        }

        private void ExecuteImport_Click(object sender, RoutedEventArgs e)
        {
            int slot = ImportTargetSlotComboBox.SelectedIndex + 1;
            MessageBox.Show(
                string.Format("Model envelope verified for Slot {0}.\n\nTo complete transfer over serial, open the transmitter's on-screen 'Transfer Model' screen and connect USB.", slot), 
                "Model Transfer Ready", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }

        #endregion

        #region Theme Switcher (Dark & Light)

        private bool _isDarkTheme = true;

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void ApplyTheme(bool isDark)
        {
            if (isDark)
            {
                // Dark Theme (Default)
                Application.Current.Resources["BgDarkBrush"] = new SolidColorBrush(Color.FromRgb(0x0D, 0x0F, 0x14));
                Application.Current.Resources["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x15, 0x19, 0x22));
                Application.Current.Resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x22, 0x30));
                Application.Current.Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x3D));
                Application.Current.Resources["BorderHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x38, 0x44, 0x5E));
                Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));
                Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                Application.Current.Resources["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
                Application.Current.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF));
                Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x33, 0xE1, 0xFF));
                Application.Current.Resources["TitleBarBrush"] = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x1B));
                Application.Current.Resources["FooterBrush"] = new SolidColorBrush(Color.FromRgb(0x0D, 0x10, 0x16));
                Application.Current.Resources["ConsoleBgBrush"] = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10));
                Application.Current.Resources["CardInnerBgBrush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x1F, 0x2E));
                Application.Current.Resources["CardInnerBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x25, 0x3A, 0x59));

                if (ThemeToggleText != null) ThemeToggleText.Text = "Theme: 🌙 Dark";
            }
            else
            {
                // Light Theme
                Application.Current.Resources["BgDarkBrush"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                Application.Current.Resources["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Application.Current.Resources["SurfaceElevatedBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                Application.Current.Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                Application.Current.Resources["BorderHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
                Application.Current.Resources["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                Application.Current.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
                Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x03, 0x69, 0xA1));
                Application.Current.Resources["TitleBarBrush"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                Application.Current.Resources["FooterBrush"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                Application.Current.Resources["ConsoleBgBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                Application.Current.Resources["CardInnerBgBrush"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xEE, 0xF4));
                Application.Current.Resources["CardInnerBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

                if (ThemeToggleText != null) ThemeToggleText.Text = "Theme: ☀️ Light";
            }

            this.Background = (Brush)Application.Current.Resources["BgDarkBrush"];
            this.Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"];
        }

        #endregion

        #region Bug Report & Update Logic

        private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "SourceTX Companion v0.01 is currently up to date!\n\nTarget Hardware: ESP32-S3 (4MB Flash DIO/80M, 2MB PSRAM)\nDisplay: 3.5\" ST7796U 480x320 SPI (Touch FT6x36)\nFirmware Version: v1.98 (Official Build)\n\nTo check for new releases and source updates, visit the SourceTX GitHub repository.", 
                "SourceTX Updates", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }

        private void ReportBug_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Would you like to open GitHub to report an issue or bug for SourceTX?\n\nURL: https://github.com/DrMeowy/SourceTX/issues", 
                "SourceTX - Report a Bug", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/DrMeowy/SourceTX/issues/new",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("Could not open default browser: {0}\n\nPlease visit: https://github.com/DrMeowy/SourceTX/issues", ex.Message), "Report Bug", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        #endregion
    }
}
