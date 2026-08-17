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
        public int CrsfPin { get; set; }
        public int StatusMode { get; set; }
        public int StatusMonoPin { get; set; }
        public int StatusRedPin { get; set; }
        public int StatusGreenPin { get; set; }
        public int StatusBluePin { get; set; }
        public int StatusBrightness { get; set; }
        public int SoundMode { get; set; }
        public int SoundPin { get; set; }
        public int VibrationPin { get; set; }

        public HardwarePinSettings()
        {
            CrsfPin = -1;
            StatusMode = 0;
            StatusMonoPin = -1;
            StatusRedPin = -1;
            StatusGreenPin = -1;
            StatusBluePin = -1;
            StatusBrightness = 60;
            SoundMode = 0;
            SoundPin = -1;
            VibrationPin = -1;
        }
    }

    public sealed class SourceTxSerialClient : IDisposable
    {
        public const string CommandPrefix = "SOURCETX_XFER:";
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
            _port.WriteLine(CommandPrefix + "GET_HW");
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value.StartsWith(CommandPrefix + "HW:", StringComparison.Ordinal) ||
                           value.StartsWith(CommandPrefix + "ERR:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);

            if (line == null)
            {
                error = "The transmitter did not respond to the hardware settings request. Make sure the transmitter is connected and on the Model Transfer screen.";
                return false;
            }
            if (line.StartsWith(CommandPrefix + "ERR:", StringComparison.Ordinal))
            {
                error = "The transmitter returned an error: " + line;
                return false;
            }

            string payload = line.Substring((CommandPrefix + "HW:").Length);
            var result = new HardwarePinSettings();
            string[] pairs = payload.Split(':');
            foreach (var pair in pairs)
            {
                string[] kv = pair.Split('=');
                if (kv.Length != 2) continue;
                int val;
                if (!int.TryParse(kv[1], out val)) continue;

                switch (kv[0])
                {
                    case "CRSF": result.CrsfPin = val; break;
                    case "STAT_MODE": result.StatusMode = val; break;
                    case "STAT_MONO": result.StatusMonoPin = val; break;
                    case "STAT_R": result.StatusRedPin = val; break;
                    case "STAT_G": result.StatusGreenPin = val; break;
                    case "STAT_B": result.StatusBluePin = val; break;
                    case "STAT_BRIGHT": result.StatusBrightness = val; break;
                    case "SND_MODE": result.SoundMode = val; break;
                    case "SND_PIN": result.SoundPin = val; break;
                    case "VIB_PIN": result.VibrationPin = val; break;
                }
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
                "{0}SET_HW:CRSF={1}:STAT_MODE={2}:STAT_MONO={3}:STAT_R={4}:STAT_G={5}:STAT_B={6}:STAT_BRIGHT={7}:SND_MODE={8}:SND_PIN={9}:VIB_PIN={10}",
                CommandPrefix,
                settings.CrsfPin,
                settings.StatusMode,
                settings.StatusMonoPin,
                settings.StatusRedPin,
                settings.StatusGreenPin,
                settings.StatusBluePin,
                settings.StatusBrightness,
                settings.SoundMode,
                settings.SoundPin,
                settings.VibrationPin
            );

            _port.WriteLine(cmd);
            string line = ReadMatchingLine(
                delegate(string value)
                {
                    return value == CommandPrefix + "OK:SET_HW" ||
                           value.StartsWith(CommandPrefix + "ERR:", StringComparison.Ordinal);
                },
                timeoutMilliseconds);

            if (line == null)
            {
                error = "The transmitter did not confirm the hardware settings write. Check connection.";
                return false;
            }
            if (line != CommandPrefix + "OK:SET_HW")
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
