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

            // Safe unattended provisioning pins. Native USB, flash/PSRAM,
            // GPIO0 and boot-strapping pins stay fixed for recovery safety.
            int[] gpios = new int[] {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 21,
                33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 47, 48
            };

            foreach (int pin in gpios)
            {
                string extra = "";
                if (pin == 42) extra = " (Default CRSF Single-Wire)";
                else if (pin == 43) extra = " (ESP32-S3 UART0 TX)";
                else if (pin == 44) extra = " (ESP32-S3 UART0 RX)";
                list.Add(new GpioOption { Pin = pin, Display = string.Format("GPIO {0}{1}", pin, extra) });
            }
            return list;
        }

        private List<GpioOption> GetAnalogGpioOptions()
        {
            var list = new List<GpioOption>();
            list.Add(new GpioOption { Pin = -1, Display = "Not assigned" });
            foreach (int pin in new int[] { 1, 4, 5, 6, 15, 16, 17, 18 })
            {
                list.Add(new GpioOption { Pin = pin, Display = string.Format("GPIO {0} (ADC)", pin) });
            }
            return list;
        }

        private void PopulatePinComboBoxes()
        {
            if (CrsfPinComboBox.Items.Count > 0) return;

            var options = GetGpioOptions();
            var pinBoxes = new ComboBox[] {
                DisplayMosiPinComboBox, DisplayClockPinComboBox,
                DisplayMisoPinComboBox, DisplayCsPinComboBox,
                DisplayDcPinComboBox, DisplayResetPinComboBox,
                DisplayBacklightPinComboBox, I2cSdaPinComboBox,
                I2cSclPinComboBox, TouchInterruptPinComboBox,
                TouchResetPinComboBox, NavigationUpPinComboBox,
                NavigationDownPinComboBox, NavigationLeftPinComboBox,
                NavigationRightPinComboBox, NavigationConfirmPinComboBox,
                SteeringPinComboBox, ThrottlePinComboBox,
                CrsfPinComboBox,
                StatusMonoPinComboBox,
                StatusRedPinComboBox,
                StatusGreenPinComboBox,
                StatusBluePinComboBox,
                SoundPinComboBox,
                VoiceRxPinComboBox,
                VibrationPinComboBox
            };

            foreach (var cb in pinBoxes)
            {
                cb.Items.Clear();
                foreach (var opt in options) cb.Items.Add(opt);
                cb.SelectedIndex = 0;
            }
            foreach (var cb in new ComboBox[] { SteeringPinComboBox, ThrottlePinComboBox })
            {
                cb.Items.Clear();
                foreach (var opt in GetAnalogGpioOptions()) cb.Items.Add(opt);
                cb.SelectedIndex = 0;
            }

            // Defaults (GPIO 42 for single-wire CRSF)
            SelectPin(DisplayMosiPinComboBox, 7);
            SelectPin(DisplayClockPinComboBox, 2);
            SelectPin(DisplayMisoPinComboBox, -1);
            SelectPin(DisplayCsPinComboBox, 14);
            SelectPin(DisplayDcPinComboBox, 13);
            SelectPin(DisplayResetPinComboBox, 10);
            SelectPin(DisplayBacklightPinComboBox, 3);
            SelectPin(I2cSdaPinComboBox, 8);
            SelectPin(I2cSclPinComboBox, 9);
            SelectPin(TouchInterruptPinComboBox, 12);
            SelectPin(TouchResetPinComboBox, 11);
            SelectPin(NavigationUpPinComboBox, 35);
            SelectPin(NavigationDownPinComboBox, 36);
            SelectPin(NavigationLeftPinComboBox, 37);
            SelectPin(NavigationRightPinComboBox, 38);
            SelectPin(NavigationConfirmPinComboBox, 39);
            SelectPin(SteeringPinComboBox, -1);
            SelectPin(ThrottlePinComboBox, -1);
            SelectPin(CrsfPinComboBox, 42);
            SelectPin(StatusMonoPinComboBox, -1);
            SelectPin(StatusRedPinComboBox, -1);
            SelectPin(StatusGreenPinComboBox, -1);
            SelectPin(StatusBluePinComboBox, -1);
            SelectPin(SoundPinComboBox, -1);
            SelectPin(VoiceRxPinComboBox, -1);
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
                        readError ?? "Transmitter did not respond over USB.",
                        "Read Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SelectPin(DisplayMosiPinComboBox, hw.DisplayMosiPin);
                SelectPin(DisplayClockPinComboBox, hw.DisplayClockPin);
                SelectPin(DisplayMisoPinComboBox, hw.DisplayMisoPin);
                SelectPin(DisplayCsPinComboBox, hw.DisplayCsPin);
                SelectPin(DisplayDcPinComboBox, hw.DisplayDcPin);
                SelectPin(DisplayResetPinComboBox, hw.DisplayResetPin);
                SelectPin(DisplayBacklightPinComboBox, hw.DisplayBacklightPin);
                SelectPin(I2cSdaPinComboBox, hw.I2cSdaPin);
                SelectPin(I2cSclPinComboBox, hw.I2cSclPin);
                SelectPin(TouchInterruptPinComboBox, hw.TouchInterruptPin);
                SelectPin(TouchResetPinComboBox, hw.TouchResetPin);
                TouchAddressTextBox.Text = hw.TouchAddress.ToString("X2");
                InaAddressTextBox.Text = hw.Ina219Address.ToString("X2");
                SelectPin(NavigationUpPinComboBox, hw.NavigationUpPin);
                SelectPin(NavigationDownPinComboBox, hw.NavigationDownPin);
                SelectPin(NavigationLeftPinComboBox, hw.NavigationLeftPin);
                SelectPin(NavigationRightPinComboBox, hw.NavigationRightPin);
                SelectPin(NavigationConfirmPinComboBox, hw.NavigationConfirmPin);
                SelectPin(SteeringPinComboBox, hw.SteeringPin);
                SelectPin(ThrottlePinComboBox, hw.ThrottlePin);
                SelectPin(CrsfPinComboBox, hw.CrsfPin);
                StatusModeComboBox.SelectedIndex = Math.Max(0, Math.Min(3, hw.StatusMode));
                SelectPin(StatusMonoPinComboBox, hw.StatusMonoPin);
                SelectPin(StatusRedPinComboBox, hw.StatusRedPin);
                SelectPin(StatusGreenPinComboBox, hw.StatusGreenPin);
                SelectPin(StatusBluePinComboBox, hw.StatusBluePin);
                StatusBrightnessSlider.Value = Math.Max(0, Math.Min(100, hw.StatusBrightness));
                SoundModeComboBox.SelectedIndex = Math.Max(0, Math.Min(2, hw.SoundMode));
                SelectPin(SoundPinComboBox, hw.SoundPin);
                SelectPin(VoiceRxPinComboBox, hw.VoiceRxPin);
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

            int touchAddress;
            int inaAddress;
            if (!int.TryParse(TouchAddressTextBox.Text,
                    System.Globalization.NumberStyles.HexNumber, null,
                    out touchAddress) || touchAddress < 0x08 || touchAddress > 0x77 ||
                !int.TryParse(InaAddressTextBox.Text,
                    System.Globalization.NumberStyles.HexNumber, null,
                    out inaAddress) || inaAddress < 0x40 || inaAddress > 0x4F)
            {
                MessageBox.Show("Enter the I²C addresses as hexadecimal values. Touch must be 08–77 and INA219 must be 40–4F.",
                    "Invalid I²C Address", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var hw = new HardwarePinSettings
            {
                DisplayMosiPin = GetSelectedPin(DisplayMosiPinComboBox),
                DisplayClockPin = GetSelectedPin(DisplayClockPinComboBox),
                DisplayMisoPin = GetSelectedPin(DisplayMisoPinComboBox),
                DisplayCsPin = GetSelectedPin(DisplayCsPinComboBox),
                DisplayDcPin = GetSelectedPin(DisplayDcPinComboBox),
                DisplayResetPin = GetSelectedPin(DisplayResetPinComboBox),
                DisplayBacklightPin = GetSelectedPin(DisplayBacklightPinComboBox),
                I2cSdaPin = GetSelectedPin(I2cSdaPinComboBox),
                I2cSclPin = GetSelectedPin(I2cSclPinComboBox),
                TouchInterruptPin = GetSelectedPin(TouchInterruptPinComboBox),
                TouchResetPin = GetSelectedPin(TouchResetPinComboBox),
                TouchAddress = touchAddress,
                Ina219Address = inaAddress,
                NavigationUpPin = GetSelectedPin(NavigationUpPinComboBox),
                NavigationDownPin = GetSelectedPin(NavigationDownPinComboBox),
                NavigationLeftPin = GetSelectedPin(NavigationLeftPinComboBox),
                NavigationRightPin = GetSelectedPin(NavigationRightPinComboBox),
                NavigationConfirmPin = GetSelectedPin(NavigationConfirmPinComboBox),
                SteeringPin = GetSelectedPin(SteeringPinComboBox),
                ThrottlePin = GetSelectedPin(ThrottlePinComboBox),
                CrsfPin = GetSelectedPin(CrsfPinComboBox),
                StatusMode = StatusModeComboBox.SelectedIndex,
                StatusMonoPin = GetSelectedPin(StatusMonoPinComboBox),
                StatusRedPin = GetSelectedPin(StatusRedPinComboBox),
                StatusGreenPin = GetSelectedPin(StatusGreenPinComboBox),
                StatusBluePin = GetSelectedPin(StatusBluePinComboBox),
                StatusBrightness = (int)StatusBrightnessSlider.Value,
                SoundMode = SoundModeComboBox.SelectedIndex,
                SoundPin = GetSelectedPin(SoundPinComboBox),
                VoiceRxPin = GetSelectedPin(VoiceRxPinComboBox),
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
                checkCollision(hw.DisplayMosiPin, "Display MOSI");
                checkCollision(hw.DisplayClockPin, "Display clock");
                checkCollision(hw.DisplayMisoPin, "Display MISO");
                checkCollision(hw.DisplayCsPin, "Display CS");
                checkCollision(hw.DisplayDcPin, "Display data/command");
                checkCollision(hw.DisplayResetPin, "Display reset");
                checkCollision(hw.DisplayBacklightPin, "Display backlight");
                checkCollision(hw.I2cSdaPin, "I²C SDA");
                checkCollision(hw.I2cSclPin, "I²C SCL");
                checkCollision(hw.TouchInterruptPin, "Touch interrupt");
                checkCollision(hw.TouchResetPin, "Touch reset");
                checkCollision(hw.NavigationUpPin, "Navigation up");
                checkCollision(hw.NavigationDownPin, "Navigation down");
                checkCollision(hw.NavigationLeftPin, "Navigation left");
                checkCollision(hw.NavigationRightPin, "Navigation right");
                checkCollision(hw.NavigationConfirmPin, "Navigation confirm");
                checkCollision(hw.SteeringPin, "Steering input");
                checkCollision(hw.ThrottlePin, "Throttle input");
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
                if (hw.SoundMode == 2) checkCollision(hw.VoiceRxPin, "DFPlayer RX");
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
