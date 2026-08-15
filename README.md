# SourceTX-Companion

Public Windows installer and maintenance application for SourceTX transmitters.

## Signed factory installation

The Install screen provisions a blank supported ESP32-S3 from the public
`DrMeowy/SourceTX-Updates` feed. One action:

1. downloads `factory.json` and `factory.json.sig`;
2. verifies the manifest with the SourceTX P-256 public key compiled into the
   Companion executable;
3. validates the exact hardware ID and flash contract;
4. downloads the combined factory image and verifies its size, SHA-256 digest
   and ECDSA signature;
5. checks the connected ESP32-S3 and exact flash capacity with esptool;
6. optionally erases flash, writes the complete image at `0x0000`, verifies the
   write and reboots.

When the public feed is unavailable, Companion uses its bundled image only if
the file matches the immutable SHA-256 trust anchor compiled into the
application. `targets.json` mirrors the digest for diagnostics but cannot
replace the compiled feed, key, digest, chip, or flash contract.

The current supported target is
`sourcetx-s3-st7796-ft6x36`: ESP32-S3-FH4R2, 4 MB DIO/80 MHz flash, 2 MB
quad PSRAM, ST7796U 480x320 display and FT6x36 touch controller. Development
profiles remain disabled until they have their own builds and signed factory
contracts.

## Build

Run `build.bat` or `build.ps1`. Release output is written to `bin/Release` and
must be distributed with `targets.json`, `firmware/`, `tools/`, `LICENSE`, and
the third-party notices required by the bundled components.
