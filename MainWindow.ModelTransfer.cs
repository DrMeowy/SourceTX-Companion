using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SourceTXCompanion
{
    public partial class MainWindow
    {
        private SourceTxModelEnvelope _pendingImportEnvelope;
        private SourceTxModelBundle _pendingImportBundle;
        private List<SourceTxModelEnvelope> _pendingBundleEnvelopes;
        private bool _modelTransferBusy;

        private async void ExecuteExport_Click(object sender, RoutedEventArgs e)
        {
            if (_modelTransferBusy) return;

            string portName = ExtractCleanPort(ExportPortComboBox.SelectedItem);
            if (string.IsNullOrEmpty(portName))
            {
                MessageBox.Show(
                    "Connect the transmitter with a USB data cable, then choose Find Transmitter.",
                    "Transmitter Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool exportAll = ExportScopeComboBox.SelectedIndex == 1;
            _modelTransferBusy = true;
            ExecuteExportButton.IsEnabled = false;
            ExportPreviewBlock.Text = exportAll
                ? "Connecting to SourceTX and reading every model..."
                : "Waiting for the current model...\n\nPress Export To USB Serial on the transmitter now.";
            StatusBarText.Text = "Reading model data from transmitter...";

            try
            {
                SourceTxExportSession session = await Task.Run(delegate
                {
                    using (var client = new SourceTxSerialClient(portName))
                    {
                        if (!exportAll)
                        {
                            SourceTxModelEnvelope legacy = client.ListenForLegacyActiveExport(30000);
                            var activeModels = new SortedDictionary<int, SourceTxModelEnvelope>();
                            activeModels.Add(1, legacy);
                            return new SourceTxExportSession
                            {
                                Device = null,
                                Models = activeModels
                            };
                        }

                        SourceTxDeviceTransferInfo device;
                        string handshakeError;
                        if (client.TryHandshake(4000, out device, out handshakeError))
                        {
                            return client.ExportModels(device, true, 45000);
                        }
                        throw new NotSupportedException(
                            "This transmitter firmware cannot back up all models automatically. " +
                            "Update the transmitter to the latest stable SourceTX release, or choose Current Model.");
                    }
                });

                var dialog = new SaveFileDialog
                {
                    AddExtension = true,
                    OverwritePrompt = true,
                    Title = exportAll
                        ? "Save Complete SourceTX Model Backup"
                        : "Save SourceTX Model Backup"
                };

                string content;
                if (exportAll)
                {
                    SourceTxModelBundle bundle = ModelTransferProtocol.CreateBundle(
                        session.Models,
                        session.Device.ActiveModel,
                        session.Device.ProtocolVersion);
                    content = ModelTransferProtocol.SerializeBundle(bundle);
                    dialog.Filter = "SourceTX Model Bundle (*.stxb)|*.stxb|All Files (*.*)|*.*";
                    dialog.DefaultExt = ".stxb";
                    dialog.FileName = "SourceTX_all_models_" + DateTime.Now.ToString("yyyy-MM-dd") + ".stxb";
                }
                else
                {
                    SourceTxModelEnvelope model = session.Models.Values.First();
                    content = model.Text;
                    dialog.Filter = "SourceTX Model Backup (*.stx)|*.stx|All Files (*.*)|*.*";
                    dialog.DefaultExt = ".stx";
                    dialog.FileName = MakeSafeFileName(model.ModelName) + "_model" + dialog.DefaultExt;
                }

                if (dialog.ShowDialog() != true)
                {
                    ExportPreviewBlock.Text = "The model was read successfully, but saving was cancelled.";
                    StatusBarText.Text = "Backup cancelled after reading transmitter";
                    return;
                }

                File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
                if (exportAll)
                {
                    ExportPreviewBlock.Text = string.Format(
                        "Backup complete.\n\nModels saved: {0}\nCurrent model slot: {1}\nFile: {2}",
                        session.Models.Count,
                        session.Device.ActiveModel,
                        dialog.FileName);
                }
                else
                {
                    SourceTxModelEnvelope model = session.Models.Values.First();
                    ExportPreviewBlock.Text = string.Format(
                        "Backup complete.\n\nModel: {0}\nFile: {1}",
                        model.ModelName,
                        dialog.FileName);
                }
                StatusBarText.Text = exportAll
                    ? "Complete model backup saved"
                    : "Active model backup saved";
            }
            catch (Exception ex)
            {
                ExportPreviewBlock.Text = "Backup failed.\n\n" + ex.Message;
                StatusBarText.Text = "Model backup failed";
                MessageBox.Show(ex.Message, "Backup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _modelTransferBusy = false;
                ExecuteExportButton.IsEnabled = true;
            }
        }

        private void BrowseImport_Click(object sender, RoutedEventArgs e)
        {
            if (_modelTransferBusy) return;

            var dialog = new OpenFileDialog
            {
                Filter = "SourceTX Backups (*.stxb;*.stx;*.txt)|*.stxb;*.stx;*.txt|All Files (*.*)|*.*",
                Title = "Select SourceTX Backup to Restore"
            };
            if (dialog.ShowDialog() != true) return;

            _pendingImportEnvelope = null;
            _pendingImportBundle = null;
            _pendingBundleEnvelopes = null;
            ImportPathBox.Text = dialog.FileName;

            try
            {
                var file = new FileInfo(dialog.FileName);
                if (file.Length > 8 * 1024 * 1024)
                {
                    throw new InvalidDataException("This backup is larger than the 8 MB safety limit and cannot be opened.");
                }
                string content = File.ReadAllText(dialog.FileName).Trim();
                bool isBundle = dialog.FileName.EndsWith(".stxb", StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith("{", StringComparison.Ordinal);

                string error;
                if (isBundle)
                {
                    SourceTxModelBundle bundle;
                    List<SourceTxModelEnvelope> envelopes;
                    if (!ModelTransferProtocol.TryParseBundle(content, out bundle, out envelopes, out error))
                    {
                        throw new InvalidDataException(error);
                    }
                    _pendingImportBundle = bundle;
                    _pendingBundleEnvelopes = envelopes;
                    ImportTargetSlotComboBox.SelectedIndex = 0;
                    ImportTargetSlotComboBox.IsEnabled = false;
                    ImportValidationTag.Text = "Backup verified: " + bundle.ModelCount + " models";
                    ImportValidationTag.Foreground = (Brush)FindResource("SuccessBrush");
                    ImportLogBlock.Text = string.Format(
                        "This complete backup is valid and contains {0} models.\n\n" +
                        "Every model slot in the backup will be restored. The transmitter will keep its currently selected model slot.",
                        bundle.ModelCount);
                }
                else
                {
                    SourceTxModelEnvelope envelope;
                    if (!ModelTransferProtocol.TryParseEnvelope(content, 0, 0, out envelope, out error))
                    {
                        throw new InvalidDataException(error);
                    }
                    _pendingImportEnvelope = envelope;
                    ImportTargetSlotComboBox.IsEnabled = true;
                    ImportValidationTag.Text = "Backup verified";
                    ImportValidationTag.Foreground = (Brush)FindResource("SuccessBrush");
                    ImportLogBlock.Text = string.Format(
                        "This backup is valid.\n\nModel: {0}\n\nChoose Current Model for the simplest restore, or choose a numbered slot when using current firmware.",
                        envelope.ModelName);
                }
            }
            catch (Exception ex)
            {
                ImportTargetSlotComboBox.IsEnabled = true;
                ImportValidationTag.Text = "Backup rejected";
                ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                ImportLogBlock.Text = "This backup cannot be used.\n\n" + ex.Message;
            }
        }

        private async void ExecuteImport_Click(object sender, RoutedEventArgs e)
        {
            if (_modelTransferBusy) return;
            if (_pendingImportEnvelope == null && _pendingImportBundle == null)
            {
                MessageBox.Show("Choose and validate a backup file first.", "No Backup Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string portName = ExtractCleanPort(ImportPortComboBox.SelectedItem);
            if (string.IsNullOrEmpty(portName))
            {
                MessageBox.Show("Connect the transmitter with a USB data cable, then return to this screen.",
                    "Transmitter Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool restoringBundle = _pendingImportBundle != null;
            int selectedTarget = ImportTargetSlotComboBox.SelectedIndex;
            SourceTxModelEnvelope pendingEnvelope = _pendingImportEnvelope;
            SourceTxModelBundle pendingBundle = _pendingImportBundle;
            List<SourceTxModelEnvelope> pendingBundleEnvelopes = _pendingBundleEnvelopes;
            string warning = restoringBundle
                ? string.Format("Restore all {0} model slots from this bundle?\n\nExisting models in those slots will be overwritten.", pendingBundle.ModelCount)
                : selectedTarget == 0
                    ? "Restore this backup over the transmitter's current model?\n\nThe current model will be overwritten. Before choosing Yes, press Import From USB Serial on the transmitter."
                    : "Restore this backup to the selected model slot?\n\nThe model currently stored in that slot will be overwritten.";
            if (MessageBox.Show(warning, "Confirm Model Restore", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            _modelTransferBusy = true;
            ExecuteImportButton.IsEnabled = false;
            ImportLogBlock.Text = "Connecting to SourceTX...";
            StatusBarText.Text = "Restoring model data to transmitter...";

            try
            {
                const string manualConfirmationPrefix = "\u0001";
                string result = await Task.Run(delegate
                {
                    using (var client = new SourceTxSerialClient(portName))
                    {
                        if (!restoringBundle && selectedTarget == 0)
                        {
                            client.SendLegacyActiveImport(pendingEnvelope);
                            return manualConfirmationPrefix + "The model was sent to the transmitter.\n\n" +
                                   "Confirm that the transmitter displays MODEL IMPORTED.";
                        }

                        SourceTxDeviceTransferInfo device;
                        string handshakeError;
                        bool hasProtocol = client.TryHandshake(4000, out device, out handshakeError);
                        if (restoringBundle)
                        {
                            if (!hasProtocol)
                            {
                                throw new NotSupportedException(
                                    "This transmitter firmware cannot restore a complete backup automatically. " +
                                    "Update to the latest stable SourceTX release, or restore one model to Current Model.");
                            }
                            ValidateBundleAgainstDevice(pendingBundle, device);
                            if (device.ActiveModel > pendingBundle.ModelCount &&
                                device.ModelCount > pendingBundle.ModelCount)
                            {
                                throw new InvalidOperationException(
                                    "The transmitter's active slot is above the bundle model count. " +
                                    "Select one of the lower-numbered models on the transmitter, then try again.");
                            }

                            int restored = 0;
                            for (int index = 0; index < pendingBundleEnvelopes.Count; index++)
                            {
                                try
                                {
                                    client.ImportModel(device, index + 1, pendingBundleEnvelopes[index], 15000);
                                    restored++;
                                }
                                catch (Exception ex)
                                {
                                    throw new IOException(string.Format(
                                        "Restore stopped at model slot {0} after {1} models were restored: {2}",
                                        index + 1,
                                        restored,
                                        ex.Message), ex);
                                }
                            }
                            if (device.ModelCount > pendingBundle.ModelCount)
                            {
                                client.SetModelCount(device, pendingBundle.ModelCount, 10000);
                            }
                            return string.Format(
                                "Restored {0} models successfully.\n\nThe transmitter kept its currently selected model slot.",
                                restored);
                        }

                        if (hasProtocol)
                        {
                            int targetSlot = selectedTarget;
                            client.ImportModel(device, targetSlot, pendingEnvelope, 15000);
                            return string.Format(
                                "Restored '{0}' to model slot {1}.\n\nThe transmitter confirmed the restore.",
                                pendingEnvelope.ModelName,
                                targetSlot);
                        }

                        throw new NotSupportedException(
                            "This transmitter firmware cannot restore directly to a numbered slot. " +
                            "Choose Current Model instead, or update to the latest stable SourceTX release.");
                    }
                });

                bool requiresManualVerification = result.StartsWith(
                    manualConfirmationPrefix, StringComparison.Ordinal);
                if (requiresManualVerification)
                {
                    result = result.Substring(manualConfirmationPrefix.Length);
                }
                ImportLogBlock.Text = result;
                ImportValidationTag.Text = requiresManualVerification
                    ? "Sent — verify transmitter"
                    : "Restore completed";
                ImportValidationTag.Foreground = (Brush)FindResource(
                    requiresManualVerification ? "WarningBrush" : "SuccessBrush");
                StatusBarText.Text = requiresManualVerification
                    ? "Active model sent; awaiting on-radio confirmation"
                    : "Model restore completed";
                MessageBox.Show(
                    result,
                    requiresManualVerification ? "Transfer Sent" : "Restore Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ImportLogBlock.Text = "Restore failed.\n\n" + ex.Message;
                ImportValidationTag.Text = "Restore failed";
                ImportValidationTag.Foreground = (Brush)FindResource("DangerBrush");
                StatusBarText.Text = "Model restore failed";
                MessageBox.Show(ex.Message, "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _modelTransferBusy = false;
                ExecuteImportButton.IsEnabled = true;
            }
        }

        private static void ValidateBundleAgainstDevice(
            SourceTxModelBundle bundle,
            SourceTxDeviceTransferInfo device)
        {
            if (bundle.Schema != device.Schema || bundle.PayloadSize != device.PayloadSize)
            {
                throw new InvalidDataException(
                    "This backup was created by an incompatible SourceTX firmware version. Update the transmitter and try again.");
            }
        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "SourceTX_model";
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(invalid.Contains(character) ? '_' : character);
            }
            string result = builder.ToString().Trim().Replace(' ', '_');
            return string.IsNullOrWhiteSpace(result) ? "SourceTX_model" : result;
        }
    }
}
