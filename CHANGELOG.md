# SourceTX Companion changelog

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
