using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SourceTXCompanion
{
    public sealed class SourceTxModelEnvelope
    {
        public string Text { get; set; }
        public string Hex { get; set; }
        public uint Magic { get; set; }
        public ushort Schema { get; set; }
        public ushort PayloadSize { get; set; }
        public byte[] Payload { get; set; }
        public uint Checksum { get; set; }
        public string ModelName { get; set; }
    }

    public sealed class SourceTxModelBundleEntry
    {
        public int Slot { get; set; }
        public string Name { get; set; }
        public string Envelope { get; set; }
    }

    public sealed class SourceTxModelBundle
    {
        public string Format { get; set; }
        public int Version { get; set; }
        public int Protocol { get; set; }
        public int Schema { get; set; }
        public int PayloadSize { get; set; }
        public int ModelCount { get; set; }
        public int ActiveModel { get; set; }
        public string CreatedUtc { get; set; }
        public string ChecksumSha256 { get; set; }
        public List<SourceTxModelBundleEntry> Models { get; set; }
    }

    public static class ModelTransferProtocol
    {
        public const uint TransferMagic = 0x5354584DU;
        public const ushort PublicSchemaVersion = 21;
        public const string ModelPrefix = "SOURCETX_MODEL:";
        public const string BundleFormat = "SOURCETX_MODEL_BUNDLE";
        public const int BundleVersion = 1;
        public const int MaximumModels = 20;

        public static uint CalculateFnv1a(
            byte[] payload,
            uint magic,
            ushort version,
            ushort payloadSize)
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

        public static bool TryParseEnvelope(
            string text,
            int expectedSchema,
            int expectedPayloadSize,
            out SourceTxModelEnvelope envelope,
            out string error)
        {
            envelope = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The model file is empty.";
                return false;
            }

            string content = text.Trim();
            if (!content.StartsWith(ModelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "This is not a recognized SourceTX model backup.";
                return false;
            }
            string hex = content.Substring(ModelPrefix.Length).Trim();
            if (hex.Length < 24 || hex.Length > 131094 || (hex.Length & 1) != 0)
            {
                error = "The backup has an invalid size and may be damaged.";
                return false;
            }
            for (int index = 0; index < hex.Length; index++)
            {
                char value = hex[index];
                bool validHex = (value >= '0' && value <= '9') ||
                    (value >= 'A' && value <= 'F') ||
                    (value >= 'a' && value <= 'f');
                if (!validHex)
                {
                    error = "The backup contains invalid data and may be damaged.";
                    return false;
                }
            }

            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }
            if (bytes.Length < 12)
            {
                error = "The backup is incomplete.";
                return false;
            }

            uint magic = BitConverter.ToUInt32(bytes, 0);
            ushort schema = BitConverter.ToUInt16(bytes, 4);
            ushort payloadSize = BitConverter.ToUInt16(bytes, 6);
            if (magic != TransferMagic)
            {
                error = "This is not a compatible SourceTX model backup.";
                return false;
            }
            if (bytes.Length != 12 + payloadSize)
            {
                error = "The backup is incomplete or damaged.";
                return false;
            }
            if (payloadSize == 0)
            {
                error = "The backup contains no model data and cannot be restored.";
                return false;
            }
            if (expectedSchema > 0 && schema != expectedSchema)
            {
                error = "The backup was created by an incompatible SourceTX firmware version.";
                return false;
            }
            if (expectedPayloadSize > 0 && payloadSize != expectedPayloadSize)
            {
                error = "The backup does not match the connected transmitter firmware.";
                return false;
            }

            byte[] payload = new byte[payloadSize];
            Array.Copy(bytes, 8, payload, 0, payload.Length);
            uint storedChecksum = BitConverter.ToUInt32(bytes, bytes.Length - 4);
            uint calculated = CalculateFnv1a(payload, magic, schema, payloadSize);
            if (storedChecksum != calculated)
            {
                error = "The backup failed its integrity check and may be damaged or modified.";
                return false;
            }

            string name = Encoding.ASCII.GetString(
                payload,
                0,
                Math.Min(16, payload.Length)).Trim('\0', ' ');
            if (string.IsNullOrWhiteSpace(name)) name = "Unnamed Model";
            envelope = new SourceTxModelEnvelope
            {
                Text = ModelPrefix + hex.ToUpperInvariant(),
                Hex = hex.ToUpperInvariant(),
                Magic = magic,
                Schema = schema,
                PayloadSize = payloadSize,
                Payload = payload,
                Checksum = storedChecksum,
                ModelName = name
            };
            return true;
        }

        public static SourceTxModelBundle CreateBundle(
            IEnumerable<KeyValuePair<int, SourceTxModelEnvelope>> models,
            int activeModel,
            int protocolVersion)
        {
            List<SourceTxModelBundleEntry> entries = models
                .OrderBy(item => item.Key)
                .Select(item => new SourceTxModelBundleEntry
                {
                    Slot = item.Key,
                    Name = item.Value.ModelName,
                    Envelope = item.Value.Text
                })
                .ToList();
            if (entries.Count == 0) throw new InvalidDataException("No models were exported.");
            SourceTxModelEnvelope first;
            string parseError;
            if (!TryParseEnvelope(entries[0].Envelope, 0, 0, out first, out parseError))
            {
                throw new InvalidDataException(parseError);
            }
            var bundle = new SourceTxModelBundle
            {
                Format = BundleFormat,
                Version = BundleVersion,
                Protocol = protocolVersion,
                Schema = first.Schema,
                PayloadSize = first.PayloadSize,
                ModelCount = entries.Count,
                ActiveModel = activeModel,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                Models = entries
            };
            bundle.ChecksumSha256 = CalculateBundleChecksum(bundle);
            return bundle;
        }

        public static string SerializeBundle(SourceTxModelBundle bundle)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            return serializer.Serialize(bundle);
        }

        public static bool TryParseBundle(
            string json,
            out SourceTxModelBundle bundle,
            out List<SourceTxModelEnvelope> envelopes,
            out string error)
        {
            bundle = null;
            envelopes = null;
            error = null;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                bundle = serializer.Deserialize<SourceTxModelBundle>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Model bundle parser notice: " + ex.Message);
                error = "This complete backup is damaged or is not a valid SourceTX backup.";
                return false;
            }
            if (bundle == null || bundle.Format != BundleFormat ||
                bundle.Version != BundleVersion || bundle.Models == null)
            {
                error = "This is not a supported SourceTX model bundle.";
                return false;
            }
            if (bundle.ModelCount < 1 || bundle.ModelCount > MaximumModels ||
                bundle.Models.Count != bundle.ModelCount ||
                bundle.ActiveModel < 1 || bundle.ActiveModel > bundle.ModelCount)
            {
                error = "The complete backup contains invalid model information and cannot be restored.";
                return false;
            }
            if (bundle.Models.Any(entry => entry == null))
            {
                error = "The complete backup is incomplete or damaged.";
                return false;
            }
            if (!string.Equals(
                    bundle.ChecksumSha256,
                    CalculateBundleChecksum(bundle),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The complete backup failed its integrity check and may be damaged or modified.";
                return false;
            }

            envelopes = new List<SourceTxModelEnvelope>();
            var seen = new HashSet<int>();
            foreach (SourceTxModelBundleEntry entry in bundle.Models.OrderBy(item => item.Slot))
            {
                if (entry.Slot < 1 || entry.Slot > bundle.ModelCount || !seen.Add(entry.Slot))
                {
                    error = "The complete backup contains invalid or repeated model slots.";
                    return false;
                }
                SourceTxModelEnvelope envelope;
                string envelopeError;
                if (!TryParseEnvelope(
                        entry.Envelope,
                        bundle.Schema,
                        bundle.PayloadSize,
                        out envelope,
                        out envelopeError))
                {
                    error = string.Format("Slot {0}: {1}", entry.Slot, envelopeError);
                    return false;
                }
                envelopes.Add(envelope);
            }
            for (int slot = 1; slot <= bundle.ModelCount; slot++)
            {
                if (!seen.Contains(slot))
                {
                    error = string.Format("The complete backup is missing model slot {0} and may be damaged.", slot);
                    return false;
                }
            }
            return true;
        }

        private static string CalculateBundleChecksum(SourceTxModelBundle bundle)
        {
            var canonical = new StringBuilder();
            canonical.Append(bundle.Format).Append('|')
                .Append(bundle.Version).Append('|')
                .Append(bundle.Protocol).Append('|')
                .Append(bundle.Schema).Append('|')
                .Append(bundle.PayloadSize).Append('|')
                .Append(bundle.ModelCount).Append('|')
                .Append(bundle.ActiveModel).Append('|');
            if (bundle.Models != null)
            {
                foreach (SourceTxModelBundleEntry entry in bundle.Models.OrderBy(item => item.Slot))
                {
                    canonical.Append(entry.Slot).Append(':')
                        .Append(entry.Envelope == null ? "" : entry.Envelope.Trim().ToUpperInvariant())
                        .Append('|');
                }
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
