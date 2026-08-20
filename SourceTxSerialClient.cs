using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace SourceTXCompanion
{
    public sealed class SourceTxDeviceTransferInfo
    {
        public int ProtocolVersion { get; set; }
        public int Schema { get; set; }
        public int PayloadSize { get; set; }
        public int ModelCount { get; set; }
        public int ActiveModel { get; set; }
    }

    public sealed class SourceTxExportSession
    {
        public SourceTxDeviceTransferInfo Device { get; set; }
        public SortedDictionary<int, SourceTxModelEnvelope> Models { get; set; }
    }

    public sealed class HardwarePinSettings
    {
        public int DisplayMosiPin { get; set; }
        public int DisplayClockPin { get; set; }
        public int DisplayMisoPin { get; set; }
        public int DisplayCsPin { get; set; }
        public int DisplayDcPin { get; set; }
        public int DisplayResetPin { get; set; }
        public int DisplayBacklightPin { get; set; }
        public int I2cSdaPin { get; set; }
        public int I2cSclPin { get; set; }
        public int TouchInterruptPin { get; set; }
        public int TouchResetPin { get; set; }
        public int TouchAddress { get; set; }
        public int Ina219Address { get; set; }
        public int NavigationUpPin { get; set; }
        public int NavigationDownPin { get; set; }
        public int NavigationLeftPin { get; set; }
        public int NavigationRightPin { get; set; }
        public int NavigationConfirmPin { get; set; }
        public int SteeringPin { get; set; }
        public int ThrottlePin { get; set; }
        public int CrsfPin { get; set; }
        public int StatusMode { get; set; }
        public int StatusMonoPin { get; set; }
        public int StatusRedPin { get; set; }
        public int StatusGreenPin { get; set; }
        public int StatusBluePin { get; set; }
        public int StatusBrightness { get; set; }
        public int SoundMode { get; set; }
        public int SoundPin { get; set; }
        public int VoiceRxPin { get; set; }
        public int VibrationPin { get; set; }

        public HardwarePinSettings()
        {
            DisplayMosiPin = 7;
            DisplayClockPin = 2;
            DisplayMisoPin = -1;
            DisplayCsPin = 14;
            DisplayDcPin = 13;
            DisplayResetPin = 10;
            DisplayBacklightPin = 3;
            I2cSdaPin = 8;
            I2cSclPin = 9;
            TouchInterruptPin = 12;
            TouchResetPin = 11;
            TouchAddress = 0x38;
            Ina219Address = 0x40;
            NavigationUpPin = 35;
            NavigationDownPin = 36;
            NavigationLeftPin = 37;
            NavigationRightPin = 38;
            NavigationConfirmPin = 39;
            SteeringPin = -1;
            ThrottlePin = -1;
            CrsfPin = 42;
            StatusMode = 0;
            StatusMonoPin = -1;
            StatusRedPin = -1;
            StatusGreenPin = -1;
            StatusBluePin = -1;
            StatusBrightness = 60;
            SoundMode = 0;
            SoundPin = -1;
            VoiceRxPin = -1;
            VibrationPin = -1;
        }
    }

    public sealed class SourceTxSerialClient : IDisposable
    {
        public const string CommandPrefix = "SOURCETX_XFER:";
        public const string HardwarePrefix = "SOURCETX_HW:";
        private readonly SerialPort _port;

        public SourceTxSerialClient(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("A serial port is required.", "portName");
            }
            _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.ASCII,
                NewLine = "\n",
                ReadTimeout = 250,
                WriteTimeout = 3000,
                DtrEnable = false,
                RtsEnable = false,
                ReadBufferSize = 131072,
                WriteBufferSize = 131072
            };
            _port.Open();
            Thread.Sleep(120);
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        public bool TryHandshake(
            int timeoutMilliseconds,
            out SourceTxDeviceTransferInfo info,
            out string error)
        {
            info = null;
            error = null;
            _port.WriteLine(CommandPrefix + "HELLO");
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value.StartsWith(CommandPrefix + "READY:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);
            if (line == null)
            {
                error = "The transmitter did not respond to the model-transfer request.";
                return false;
            }
            string[] fields = line.Substring(CommandPrefix.Length).Split(':');
            int protocol;
            int schema;
            int payloadSize;
            int modelCount;
            int activeModel;
            if (fields.Length != 6 || fields[0] != "READY" ||
                !int.TryParse(fields[1], out protocol) ||
                !int.TryParse(fields[2], out schema) ||
                !int.TryParse(fields[3], out payloadSize) ||
                !int.TryParse(fields[4], out modelCount) ||
                !int.TryParse(fields[5], out activeModel) ||
                protocol < 1 || schema < 1 || payloadSize < 1 ||
                modelCount < 1 || modelCount > ModelTransferProtocol.MaximumModels ||
                activeModel < 1 || activeModel > modelCount)
            {
                error = "The transmitter returned incompatible model-transfer information.";
                return false;
            }
            info = new SourceTxDeviceTransferInfo
            {
                ProtocolVersion = protocol,
                Schema = schema,
                PayloadSize = payloadSize,
                ModelCount = modelCount,
                ActiveModel = activeModel
            };
            return true;
        }

        public SourceTxExportSession ExportModels(
            SourceTxDeviceTransferInfo info,
            bool exportAll,
            int timeoutMilliseconds)
        {
            if (info == null) throw new ArgumentNullException("info");
            int expectedCount = exportAll ? info.ModelCount : 1;
            _port.WriteLine(CommandPrefix + (exportAll
                ? "EXPORT:ALL"
                : "EXPORT:" + info.ActiveModel));

            var models = new SortedDictionary<int, SourceTxModelEnvelope>();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            bool done = false;
            while (DateTime.UtcNow < deadline && !done)
            {
                string line = ReadOneLine(deadline);
                if (line == null) continue;
                if (line.StartsWith(CommandPrefix + "ERR:", StringComparison.Ordinal))
                {
                    throw new IOException("The transmitter could not provide the requested model backup.");
                }
                if (line.StartsWith(CommandPrefix + "DONE:EXPORT:", StringComparison.Ordinal))
                {
                    done = true;
                    break;
                }
                if (!line.StartsWith(CommandPrefix + "MODEL:", StringComparison.Ordinal))
                {
                    continue;
                }
                string remainder = line.Substring((CommandPrefix + "MODEL:").Length);
                int separator = remainder.IndexOf(':');
                int slot;
                if (separator <= 0 || !int.TryParse(remainder.Substring(0, separator), out slot))
                {
                    throw new InvalidDataException("The received model backup was incomplete. Try again.");
                }
                string envelopeText = ModelTransferProtocol.ModelPrefix +
                    remainder.Substring(separator + 1);
                SourceTxModelEnvelope envelope;
                string validationError;
                if (!ModelTransferProtocol.TryParseEnvelope(
                        envelopeText,
                        info.Schema,
                        info.PayloadSize,
                        out envelope,
                        out validationError))
                {
                    throw new InvalidDataException(
                        string.Format("Model slot {0} was damaged during transfer. Try the backup again.", slot));
                }
                if (slot < 1 || slot > info.ModelCount || models.ContainsKey(slot))
                {
                    throw new InvalidDataException("The transmitter returned an invalid model list. Try again.");
                }
                models.Add(slot, envelope);
            }
            if (!done || models.Count != expectedCount)
            {
                throw new TimeoutException(string.Format(
                    "The backup stopped before every model was received ({0} of {1}). Try again.",
                    models.Count,
                    expectedCount));
            }
            return new SourceTxExportSession { Device = info, Models = models };
        }

        public SourceTxModelEnvelope ListenForLegacyActiveExport(int timeoutMilliseconds)
        {
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value.StartsWith(
                        ModelTransferProtocol.ModelPrefix,
                        StringComparison.OrdinalIgnoreCase);
                },
                timeoutMilliseconds);
            if (line == null)
            {
                throw new TimeoutException(
                    "No model was received. On the transmitter, open Model Transfer and press Export To USB Serial, then try again.");
            }
            SourceTxModelEnvelope envelope;
            string error;
            if (!ModelTransferProtocol.TryParseEnvelope(line, 0, 0, out envelope, out error))
            {
                throw new InvalidDataException(error);
            }
            return envelope;
        }

        public void ImportModel(
            SourceTxDeviceTransferInfo info,
            int slot,
            SourceTxModelEnvelope envelope,
            int timeoutMilliseconds)
        {
            if (info == null || envelope == null) throw new ArgumentNullException();
            if (envelope.Schema != info.Schema || envelope.PayloadSize != info.PayloadSize)
            {
                throw new InvalidDataException("This backup was created by an incompatible SourceTX firmware version.");
            }
            if (slot < 1 || slot > info.ModelCount + 1)
            {
                throw new InvalidDataException(
                    "Choose an existing model slot or the next empty slot.");
            }
            _port.WriteLine(string.Format(
                "{0}IMPORT:{1}:{2}", CommandPrefix, slot, envelope.Hex));
            string expected = string.Format("{0}OK:IMPORT:{1}", CommandPrefix, slot);
            string rejected = string.Format("{0}ERR:IMPORT:{1}", CommandPrefix, slot);
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value == expected || value == rejected ||
                        value.StartsWith(CommandPrefix + "ERR:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);
            if (line == null) throw new TimeoutException("The transmitter did not confirm the restore. Try again.");
            if (line != expected) throw new IOException("The transmitter rejected the model restore.");
            if (slot == info.ModelCount + 1) info.ModelCount = slot;
        }

        public void SetModelCount(
            SourceTxDeviceTransferInfo info,
            int modelCount,
            int timeoutMilliseconds)
        {
            if (info == null) throw new ArgumentNullException("info");
            _port.WriteLine(CommandPrefix + "SET_COUNT:" + modelCount);
            string expected = CommandPrefix + "OK:SET_COUNT:" + modelCount;
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value == expected ||
                        value.StartsWith(CommandPrefix + "ERR:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);
            if (line != expected)
            {
                throw new IOException(line == null
                    ? "The transmitter did not confirm the restored model list."
                    : "The transmitter rejected the restored model list.");
            }
            info.ModelCount = modelCount;
        }

        public void SendLegacyActiveImport(SourceTxModelEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException("envelope");
            _port.WriteLine(envelope.Text);
            Thread.Sleep(300);
        }

        public bool TryGetHardwareSettings(
            int timeoutMilliseconds,
            out HardwarePinSettings settings,
            out string error)
        {
            settings = null;
            error = null;
            _port.WriteLine(HardwarePrefix + "GET");
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value.StartsWith(HardwarePrefix + "PROFILE:", StringComparison.Ordinal) ||
                           value.StartsWith(HardwarePrefix + "ERR:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);

            if (line == null)
            {
                error = "The transmitter did not respond. Make sure it is powered on and connected with a USB data cable.";
                return false;
            }
            if (line.StartsWith(HardwarePrefix + "ERR:", StringComparison.Ordinal))
            {
                error = "The transmitter returned an error: " + line;
                return false;
            }

            string payload = line.Substring((HardwarePrefix + "PROFILE:").Length);
            var result = new HardwarePinSettings();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[] pairs = payload.Split(':');
            foreach (var pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length != 2) continue;
                int val;
                if (!int.TryParse(kv[1], out val)) continue;
                seen.Add(kv[0]);

                switch (kv[0])
                {
                    case "DISP_MOSI": result.DisplayMosiPin = val; break;
                    case "DISP_SCLK": result.DisplayClockPin = val; break;
                    case "DISP_MISO": result.DisplayMisoPin = val; break;
                    case "DISP_CS": result.DisplayCsPin = val; break;
                    case "DISP_DC": result.DisplayDcPin = val; break;
                    case "DISP_RST": result.DisplayResetPin = val; break;
                    case "DISP_BL": result.DisplayBacklightPin = val; break;
                    case "I2C_SDA": result.I2cSdaPin = val; break;
                    case "I2C_SCL": result.I2cSclPin = val; break;
                    case "TOUCH_INT": result.TouchInterruptPin = val; break;
                    case "TOUCH_RST": result.TouchResetPin = val; break;
                    case "TOUCH_ADDR": result.TouchAddress = val; break;
                    case "INA_ADDR": result.Ina219Address = val; break;
                    case "NAV_U": result.NavigationUpPin = val; break;
                    case "NAV_D": result.NavigationDownPin = val; break;
                    case "NAV_L": result.NavigationLeftPin = val; break;
                    case "NAV_R": result.NavigationRightPin = val; break;
                    case "NAV_OK": result.NavigationConfirmPin = val; break;
                    case "STEER": result.SteeringPin = val; break;
                    case "THROT": result.ThrottlePin = val; break;
                    case "CRSF": result.CrsfPin = val; break;
                    case "STAT_MODE": result.StatusMode = val; break;
                    case "STAT_MONO": result.StatusMonoPin = val; break;
                    case "STAT_R": result.StatusRedPin = val; break;
                    case "STAT_G": result.StatusGreenPin = val; break;
                    case "STAT_B": result.StatusBluePin = val; break;
                    case "STAT_BRIGHT": result.StatusBrightness = val; break;
                    case "SND_MODE": result.SoundMode = val; break;
                    case "SND_PIN": result.SoundPin = val; break;
                    case "VOICE_RX": result.VoiceRxPin = val; break;
                    case "VIB_PIN": result.VibrationPin = val; break;
                }
            }
            string[] required = new string[] {
                "SCHEMA", "DISP_MOSI", "DISP_SCLK", "DISP_MISO", "DISP_CS",
                "DISP_DC", "DISP_RST", "DISP_BL", "I2C_SDA", "I2C_SCL",
                "TOUCH_INT", "TOUCH_RST", "TOUCH_ADDR", "INA_ADDR", "NAV_U",
                "NAV_D", "NAV_L", "NAV_R", "NAV_OK", "STEER", "THROT", "CRSF",
                "STAT_MODE", "STAT_MONO", "STAT_R", "STAT_G", "STAT_B",
                "STAT_BRIGHT", "SND_MODE", "SND_PIN", "VOICE_RX", "VIB_PIN"
            };
            foreach (string key in required)
            {
                if (seen.Contains(key)) continue;
                error = "The transmitter returned an incomplete hardware profile. Read it again before saving.";
                return false;
            }
            int schemaValue;
            if (!int.TryParse(Array.Find(pairs, p => p.StartsWith("SCHEMA=", StringComparison.Ordinal))
                    .Substring("SCHEMA=".Length), out schemaValue) || schemaValue != 1)
            {
                error = "This transmitter uses an incompatible hardware-profile version.";
                return false;
            }
            settings = result;
            return true;
        }

        public bool TrySetHardwareSettings(
            HardwarePinSettings settings,
            int timeoutMilliseconds,
            out string error)
        {
            error = null;
            if (settings == null) throw new ArgumentNullException("settings");

            string cmd = string.Format(
                "{0}SET:SCHEMA=1:DISP_MOSI={1}:DISP_SCLK={2}:DISP_MISO={3}:DISP_CS={4}:DISP_DC={5}:DISP_RST={6}:DISP_BL={7}:I2C_SDA={8}:I2C_SCL={9}:TOUCH_INT={10}:TOUCH_RST={11}:TOUCH_ADDR={12}:INA_ADDR={13}:NAV_U={14}:NAV_D={15}:NAV_L={16}:NAV_R={17}:NAV_OK={18}:STEER={19}:THROT={20}:CRSF={21}:STAT_MODE={22}:STAT_MONO={23}:STAT_R={24}:STAT_G={25}:STAT_B={26}:STAT_BRIGHT={27}:SND_MODE={28}:SND_PIN={29}:VOICE_RX={30}:VIB_PIN={31}",
                HardwarePrefix,
                settings.DisplayMosiPin, settings.DisplayClockPin,
                settings.DisplayMisoPin, settings.DisplayCsPin,
                settings.DisplayDcPin, settings.DisplayResetPin,
                settings.DisplayBacklightPin, settings.I2cSdaPin,
                settings.I2cSclPin, settings.TouchInterruptPin,
                settings.TouchResetPin, settings.TouchAddress,
                settings.Ina219Address, settings.NavigationUpPin,
                settings.NavigationDownPin, settings.NavigationLeftPin,
                settings.NavigationRightPin, settings.NavigationConfirmPin,
                settings.SteeringPin, settings.ThrottlePin, settings.CrsfPin,
                settings.StatusMode, settings.StatusMonoPin,
                settings.StatusRedPin, settings.StatusGreenPin,
                settings.StatusBluePin, settings.StatusBrightness,
                settings.SoundMode, settings.SoundPin, settings.VoiceRxPin,
                settings.VibrationPin
            );

            _port.WriteLine(cmd);
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value == HardwarePrefix + "OK:SET:REBOOT" ||
                           value.StartsWith(HardwarePrefix + "ERR:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);

            if (line == null)
            {
                error = "The transmitter did not confirm the hardware settings write. Check connection.";
                return false;
            }
            if (line != HardwarePrefix + "OK:SET:REBOOT")
            {
                error = "The transmitter rejected the hardware settings: " + line;
                return false;
            }
            return true;
        }

        private string ReadMatchingLine(Predicate<string> predicate, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                string line = ReadOneLine(deadline);
                if (line != null && predicate(line)) return line;
            }
            return null;
        }

        private string ReadOneLine(DateTime deadline)
        {
            int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            _port.ReadTimeout = Math.Min(250, remaining);
            try
            {
                return _port.ReadLine().Trim('\r', '\n', ' ');
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_port != null)
            {
                if (_port.IsOpen) _port.Close();
                _port.Dispose();
            }
        }
    }
}
