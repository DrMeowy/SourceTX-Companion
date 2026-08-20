# SourceTX Companion changelog

## Unreleased

- Removed bundled firmware and all fixed-offset application flashing. Factory
  installation now requires the current signed online package; normal updates
  use SourceTX's transactional inactive-slot OTA for both supported targets.
- Prepared Windows Companion v0.2.0 with consistent runtime, UI, HTTP client,
  assembly and hardware-catalog version metadata.
- Updated the official firmware target and user-facing reference from the
  retired pre-public v1.98 numbering to SourceTX v1.0.0. Companion keeps its
  independent pre-1.0 application version.
- Added an explicit ESP32-S3 N16R8 experimental factory-install target. It
  uses its own signed manifest, requires a detected 16 MB flash chip, and has
  no fallback to the bundled 4 MB factory image.
- Corrected the N16R8 factory profile to DIO/80 MHz so Companion requires the
  same flash mode encoded by the generated bootloader.
- Enabled the **Configure Transmitter** feature on the home dashboard with a
  dedicated hardware configuration view.
- Added always-available versioned USB hardware provisioning (`SOURCETX_HW`)
  for the reference ST7796 display/backlight, shared I²C/touch/INA219 bus,
  navigation buttons, steering/throttle ADCs, CRSF, status LED, audio/voice and
  vibration pins. Model backup/restore remains separately screen-gated.
- Added GPIO conflict and ADC-capability validation, with native USB,
  flash/PSRAM and boot-strapping pins excluded from unattended assignment.
- Added a post-installation and post-update prompt asking the user if they would
  like to configure hardware pins immediately after flashing completes.

## 0.1.5 - 2026-08-16

- Reworked the entire app around four end-user actions: install, update or
  repair, back up models, and restore models.
- Added Stable/Experimental release-channel indicators to Install and Update.
  Stable is the active default; Experimental is visibly unavailable until a
  separate tested feed exists.
- Removed the nonfunctional Config screen, placeholder tuning controls,
  arbitrary firmware browser, and local source-builder actions from the
  end-user interface.
- Made Regular Update the default, hid low-level flashing choices and logs,
  and added confirmation prompts with plain-language data-retention warnings.
- Added strict ESP32-S3 and flash-size preflight to Update as well as Install,
  so unsupported hardware is rejected before any write begins.
- Changed factory erase to opt-in and renamed it clearly as erasing every
  saved setting and model.
- Replaced protocol, offset, checksum, GPIO, and PlatformIO wording throughout
  the visible UI with concise instructions and actionable recovery steps.
- Removed developer-machine PlatformIO paths and unused hidden controls so
  release builds rely only on their packaged, verified tools and firmware.
- Made release builds clean and self-contained, without stale duplicate files
  or debug symbols, and included notices for bundled firmware and esptool.
- Retargeted the Windows app to .NET Framework 4.8 and documented its simple
  end-user requirements; PlatformIO and Python are not required.
- Restored Configure Transmitter as a locked, visibly In Development home card
  matching the Android Companion, without restoring the old fake controls.

## 0.1.4 - 2026-08-15

- Replaced the placeholder model generator and prepare-only import screen with
  real USB serial transfers to and from the transmitter.
- Added active-model export/import through the stable legacy firmware path.
- Added Export Everything and complete restore through the portable `.stxb`
  bundle format, including slot metadata, per-model FNV-1a validation, and a
  bundle-level SHA-256 integrity check.
- Added protocol/schema/payload compatibility checks and transmitter
  acknowledgements before Companion reports a restore as complete.
- Split model-envelope parsing, serial transport, and transfer UI orchestration
  out of the main window code-behind.

## 0.1.3 - 2026-08-15

- Fixed direct esptool 5.x writes crashing mid-flash on Windows when its
  Unicode progress bar was redirected through a CP1252 process stream.
- Matched PlatformIO's UTF-8 subprocess environment and switched direct
  esptool calls to the current hyphenated command and option names.

## 0.1.2 - 2026-08-15

- Fixed factory preflight rejecting esptool 5.x ESP32-S3 identification lines
  that use padded columns after `Chip type:`.
- Improved preflight errors so a failed esptool command is distinguished from
  a completed command that did not explicitly identify an ESP32-S3.

## 0.1.1 - 2026-08-15

- Fixed factory-image validation rejecting valid ESP-IDF partition tables by
  checking the little-endian `0x50AA` magic bytes in reverse order.

## 0.1.0 - 2026-08-15

- Added one-click blank-board provisioning from the public
  `DrMeowy/SourceTX-Updates` release feed.
- Added signed `factory.json` and factory-image verification using the SourceTX
  P-256 public key compiled into Companion.
- Added exact hardware, flash-size, flash-mode, frequency, offset, file-size
  and SHA-256 validation before esptool can write an image.
- Added a verified offline fallback whose factory digest and supported FH4R2
  hardware contract are compiled trust anchors.
- Hardened ESP32-S3 preflight to reject missing or mismatched flash-capacity
  reports instead of continuing.
- Updated the Windows build target to x64 and versioned Companion as 0.1.0.
