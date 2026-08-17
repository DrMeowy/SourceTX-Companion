using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SourceTXCompanion
{
    public class GpioOption
    {
        public int Pin { get; set; }
        public string Display { get; set; }
        public override string ToString() { return Display; }
    }

    public partial class MainWindow
    {
        private bool _hardwareConfigBusy;

        private List<GpioOption> GetGpioOptions()
        {
            var list = new List<GpioOption>();
            list.Add(new GpioOption { Pin = -1, Display = "Disabled (-1)" });

            // Assignable GPIOs on ESP32-S3 (excluding internal Flash/PSRAM 26-32 and USB D-/D+ 19, 20)
            int[] gpios = new int[] {
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 21,
                33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48
            };

            foreach (int pin in gpios)
            {
                string extra = "";
                if (pin == 42) extra = " (Default CRSF Single-Wire)";
                else if (pin == 43) extra = " (ESP32-S3 UART0 TX)";
                else if (pin == 44) extra = " (ESP32-S3 UART0 RX)";
                else if (pin == 45 || pin == 46) extra = " (Boot Strap)";
                list.Add(new GpioOption { Pin = pin, Display = string.Format("GPIO {0}{1}", pin, extra) });
            }
            return list;
        }

        private void PopulatePinComboBoxes()
        {
            if (CrsfPinComboBox.Items.Count > 0) return;

            var options = GetGpioOptions();
            var pinBoxes = new ComboBox[] {
                CrsfPinComboBox,
                StatusMonoPinComboBox,
                StatusRedPinComboBox,
                StatusGreenPinComboBox,
                StatusBluePinComboBox,
                SoundPinComboBox,
                VibrationPinComboBox
            };

            foreach (var cb in pinBoxes)
            {
                cb.Items.Clear();
                foreach (var opt in options) cb.Items.Add(opt);
                cb.SelectedIndex = 0;
            }

            // Defaults (GPIO 42 for single-wire CRSF)
            SelectPin(CrsfPinComboBox, 42);
            SelectPin(StatusMonoPinComboBox, -1);
            SelectPin(StatusRedPinComboBox, -1);
            SelectPin(StatusGreenPinComboBox, -1);
            SelectPin(StatusBluePinComboBox, -1);
            SelectPin(SoundPinComboBox, -1);
            SelectPin(VibrationPinComboBox, -1);
            StatusModeComboBox.SelectedIndex = 0;
            SoundModeComboBox.SelectedIndex = 0;
        }

        private void SelectPin(ComboBox cb, int pin)
        {
            if (cb == null) return;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                var opt = cb.Items[i] as GpioOption;
                if (opt != null && opt.Pin == pin)
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        private int GetSelectedPin(ComboBox cb)
        {
            if (cb == null) return -1;
            var opt = cb.SelectedItem as GpioOption;
            return opt != null ? opt.Pin : -1;
        }

        private void StatusMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StatusMonoPinPanel == null || StatusRgbPinsPanel == null) return;

            int mode = StatusModeComboBox.SelectedIndex;
            if (mode == 1) // Mono Single-Color
            {
                StatusMonoPinPanel.Visibility = Visibility.Visible;
                StatusRgbPinsPanel.Visibility = Visibility.Collapsed;
                if (StatusMonoPinLabel != null) StatusMonoPinLabel.Text = "Mono LED Pin";
            }
            else if (mode == 2) // RGB PWM 3-pin
            {
                StatusMonoPinPanel.Visibility = Visibility.Collapsed;
                StatusRgbPinsPanel.Visibility = Visibility.Visible;
            }
            else if (mode == 3) // Addressable RGB (WS2812 / NeoPixel)
            {
                StatusMonoPinPanel.Visibility = Visibility.Visible;
                StatusRgbPinsPanel.Visibility = Visibility.Collapsed;
                if (StatusMonoPinLabel != null) StatusMonoPinLabel.Text = "WS2812 / NeoPixel Data Pin";
            }
            else // Disabled
            {
                StatusMonoPinPanel.Visibility = Visibility.Collapsed;
                StatusRgbPinsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void StatusBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (StatusBrightnessText != null)
            {
                StatusBrightnessText.Text = string.Format("{0}%", (int)e.NewValue);
            }
        }

        private async void ReadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_hardwareConfigBusy) return;

            string port = ExtractCleanPort(ConfigPortComboBox != null ? ConfigPortComboBox.SelectedItem : null);
            if (string.IsNullOrEmpty(port))
            {
                MessageBox.Show(
                    "Connect the transmitter via USB data cable, then choose Find Transmitter.",
                    "Transmitter Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _hardwareConfigBusy = true;
            AppendConfigLog(string.Format("[READ] Requesting hardware settings from {0}...", port));
            StatusBarText.Text = "Reading hardware settings from transmitter...";

            try
            {
                HardwarePinSettings hw = null;
                string readError = null;

                bool success = await Task.Run(delegate
                {
                    using (var client = new SourceTxSerialClient(port))
                    {
                        return client.TryGetHardwareSettings(4000, out hw, out readError);
                    }
                });

                if (!success || hw == null)
                {
                    AppendConfigLog("[ERROR] " + (readError ?? "Unknown read failure."));
                    MessageBox.Show(
                        readError ?? "Transmitter did not respond. Open Settings → Transmitter → Model Transfer on the radio.",
                        "Read Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SelectPin(CrsfPinComboBox, hw.CrsfPin);
                StatusModeComboBox.SelectedIndex = Math.Max(0, Math.Min(3, hw.StatusMode));
                SelectPin(StatusMonoPinComboBox, hw.StatusMonoPin);
                SelectPin(StatusRedPinComboBox, hw.StatusRedPin);
                SelectPin(StatusGreenPinComboBox, hw.StatusGreenPin);
                SelectPin(StatusBluePinComboBox, hw.StatusBluePin);
                StatusBrightnessSlider.Value = Math.Max(0, Math.Min(100, hw.StatusBrightness));
                SoundModeComboBox.SelectedIndex = Math.Max(0, Math.Min(2, hw.SoundMode));
                SelectPin(SoundPinComboBox, hw.SoundPin);
                SelectPin(VibrationPinComboBox, hw.VibrationPin);

                AppendConfigLog(string.Format("[SUCCESS] Loaded from NVS: CRSF=GPIO {0}, StatusMode={1}, SoundMode={2}, SoundPin={3}, VibPin={4}",
                    hw.CrsfPin, hw.StatusMode, hw.SoundMode, hw.SoundPin, hw.VibrationPin));
                StatusBarText.Text = "Hardware settings loaded from transmitter NVS";
            }
            catch (Exception ex)
            {
                AppendConfigLog("[ERROR] " + ex.Message);
                MessageBox.Show("Could not communicate with transmitter: " + ex.Message, "Communication Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _hardwareConfigBusy = false;
            }
        }

        private async void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_hardwareConfigBusy) return;

            string port = ExtractCleanPort(ConfigPortComboBox != null ? ConfigPortComboBox.SelectedItem : null);
            if (string.IsNullOrEmpty(port))
            {
                MessageBox.Show(
                    "Connect the transmitter via USB data cable, then choose Find Transmitter.",
                    "Transmitter Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var hw = new HardwarePinSettings
            {
                CrsfPin = GetSelectedPin(CrsfPinComboBox),
                StatusMode = StatusModeComboBox.SelectedIndex,
                StatusMonoPin = GetSelectedPin(StatusMonoPinComboBox),
                StatusRedPin = GetSelectedPin(StatusRedPinComboBox),
                StatusGreenPin = GetSelectedPin(StatusGreenPinComboBox),
                StatusBluePin = GetSelectedPin(StatusBluePinComboBox),
                StatusBrightness = (int)StatusBrightnessSlider.Value,
                SoundMode = SoundModeComboBox.SelectedIndex,
                SoundPin = GetSelectedPin(SoundPinComboBox),
                VibrationPin = GetSelectedPin(VibrationPinComboBox)
            };

            // Validate pin collisions
            var usedPins = new Dictionary<int, string>();
            Action<int, string> checkCollision = delegate(int pin, string name)
            {
                if (pin >= 0)
                {
                    if (usedPins.ContainsKey(pin))
                    {
                        throw new InvalidOperationException(
                            string.Format("GPIO {0} is assigned to both '{1}' and '{2}'. Each physical pin can only be assigned to one function.",
                                pin, usedPins[pin], name));
                    }
                    usedPins[pin] = name;
                }
            };

            try
            {
                checkCollision(hw.CrsfPin, "CRSF UART");
                if (hw.StatusMode == 1) checkCollision(hw.StatusMonoPin, "Status Mono LED");
                if (hw.StatusMode == 3) checkCollision(hw.StatusMonoPin, "Status WS2812 NeoPixel LED");
                if (hw.StatusMode == 2)
                {
                    checkCollision(hw.StatusRedPin, "Status Red LED");
                    checkCollision(hw.StatusGreenPin, "Status Green LED");
                    checkCollision(hw.StatusBluePin, "Status Blue LED");
                }
                if (hw.SoundMode != 0) checkCollision(hw.SoundPin, "Sound Output");
                checkCollision(hw.VibrationPin, "Vibration Motor");
            }
            catch (InvalidOperationException conflictEx)
            {
                MessageBox.Show(conflictEx.Message, "GPIO Conflict Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _hardwareConfigBusy = true;
            SaveConfigButton.IsEnabled = false;
            AppendConfigLog(string.Format("[WRITE] Writing hardware settings to {0} NVS...", port));
            StatusBarText.Text = "Saving hardware configuration to transmitter...";

            try
            {
                string writeError = null;
                bool success = await Task.Run(delegate
                {
                    using (var client = new SourceTxSerialClient(port))
                    {
                        return client.TrySetHardwareSettings(hw, 4000, out writeError);
                    }
                });

                if (!success)
                {
                    AppendConfigLog("[ERROR] " + (writeError ?? "Unknown write failure."));
                    MessageBox.Show(
                        writeError ?? "Transmitter rejected the hardware settings.",
                        "Save Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                AppendConfigLog(string.Format("[SUCCESS] Saved to NVS! CRSF=GPIO {0}, LED Mode={1}, Sound Mode={2}, SoundPin={3}, VibPin={4}",
                    hw.CrsfPin, hw.StatusMode, hw.SoundMode, hw.SoundPin, hw.VibrationPin));
                StatusBarText.Text = "Hardware settings saved to transmitter NVS";

                MessageBox.Show(
                    "Hardware pin settings were successfully saved to transmitter NVS memory!\n\nChanges will take effect on the transmitter after reboot.",
                    "Settings Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendConfigLog("[ERROR] " + ex.Message);
                MessageBox.Show("Could not save settings: " + ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _hardwareConfigBusy = false;
                SaveConfigButton.IsEnabled = true;
            }
        }

        private void AppendConfigLog(string message)
        {
            if (ConfigLogBlock != null)
            {
                ConfigLogBlock.Text += "\n" + message;
            }
        }
    }
}
