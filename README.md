# SourceTX-Companion

Public Windows installer and maintenance application for SourceTX transmitters.

The main screen provides four supported actions:

- **Install SourceTX** on a blank or freshly erased supported board;
- **Update or Repair** an existing transmitter while keeping compatible saved
  data during a regular update;
- **Back Up Models** from the transmitter;
- **Restore Models** from a verified SourceTX backup.

The home screen also shows **Configure Transmitter** as a locked **In
Development** preview. It intentionally opens no placeholder settings screen;
the card will only be enabled when live USB configuration is implemented and
verified in both Companion and SourceTX firmware.

## What users need

- a 64-bit Windows 10 or Windows 11 PC with .NET Framework 4.8;
- a USB data cable (a charge-only cable will not work); and
- the supported SourceTX ESP32-S3 reference transmitter or a blank supported
  board for a new installation.

PlatformIO, Python, source code, and command-line tools are not required. Keep
the entire Companion release folder together when moving or extracting it.

Install and Update use the Stable channel by default. The Experimental channel
is displayed as unavailable until a separately tested and signed feed exists.
Low-level flashing offsets, arbitrary firmware selection, source compilation,
and placeholder radio-configuration controls are intentionally not exposed in
the end-user interface.

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

Factory installation is online-only and fails closed when the signed release
feed is unavailable. Companion never substitutes a bundled or cached firmware
image. `targets.json` describes the UI catalog but cannot replace the compiled
feed, key, chip, flash, hardware-ID, or release-channel trust anchors.

The Stable target is `sourcetx-s3-st7796-ft6x36`: ESP32-S3-FH4R2, 4 MB
DIO/80 MHz flash, 2 MB quad PSRAM, ST7796U 480x320 display and FT6x36 touch.
The separately identified Experimental target is
`sourcetx-s3-n16r8-st7796-ft6x36`: 16 MB QIO/80 MHz flash with 8 MB octal
PSRAM and the same display/touch reference. Other profiles remain disabled
until they have their own builds and signed factory contracts.

Normal application updates are installed from
**Settings → System → Firmware Update** on SourceTX. The transmitter verifies
the signed target-specific feed and writes the inactive OTA slot
transactionally. Companion does not write an application image to a fixed
offset.

## Model backup and restore

Open **Settings → Transmitter → Model Transfer** on the transmitter before
starting a transfer in Companion. Companion can export or restore the active
model as a validated `SOURCETX_MODEL:` envelope (`.stx` or `.txt`). This path
also supports older firmware: use the on-radio Export or Import action when
Companion prompts for it.

Current SourceTX firmware additionally supports:

- acknowledged direct restore to an existing slot or the next new slot;
- **Export Everything**, which saves every configured model as one `.stxb`
  bundle;
- complete-bundle restore, with schema, payload, FNV-1a, and SHA-256 checks
  before model data is accepted.

A complete restore overwrites the bundled slots and restores the saved logical
model count. It intentionally keeps whichever slot is currently active on the
transmitter; the bundle records the original active slot for reference.

## Build

Install the **.NET Framework 4.8 Developer Pack** before building. The normal
.NET runtime is enough to run Companion, but it does not include the reference
assemblies required for a clean developer build.

Run `build.bat` or `build.ps1`. The script starts with a clean output directory
and writes the complete distribution to `bin/Release`. Distribute that folder
as one unit; it includes the firmware, flashing tool, project license, and all
required third-party notices.
