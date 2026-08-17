# SourceTX Third-Party Notices

Copyright (c) 2026 Viktoras Kolyšnicinas

This file records third-party software, fonts, and other materials identified
in the SourceTX repository and its current PlatformIO dependency graph. The
SourceTX Personal and Non-Commercial License does not replace or restrict the
licenses of these materials. Each component remains subject to its own license.

This is a release inventory, not a legal certification. Dependency versions
and the final link map must be frozen for each release. The repository carries
the main runtime license texts under `third_party/licenses/`; the final package
must also preserve any notice required by a dependency that is actually linked
or bundled.

## Runtime and application dependencies

### LVGL 9.5.0

- Use: UI framework and widgets.
- License: MIT.
- Project: <https://github.com/lvgl/lvgl>
- License text: <https://github.com/lvgl/lvgl/blob/master/LICENCE.txt>
- Repository copy: `third_party/licenses/LVGL-MIT.txt`

SourceTX enables LVGL's Montserrat fonts and selected LVGL widgets. LVGL also
contains optional third-party components; the current SourceTX configuration
does not enable every optional LVGL component. Re-check this when changing
`include/lv_conf.h`.

### TFT_eSPI 2.5.43

- Use: TFT display driver and low-level rendering interface.
- Project: <https://github.com/Bodmer/TFT_eSPI>
- License notices: <https://github.com/Bodmer/TFT_eSPI/blob/master/license.txt>
- Repository copy: `third_party/licenses/TFT_eSPI-license.txt`

The upstream license file describes a combination of original TFT_eSPI code
under a FreeBSD-style license and selected Adafruit GFX code under a BSD
license. Preserve the upstream license text and notices; do not describe this
dependency as MIT-only.

### Adafruit INA219 1.2.3

- Use: INA219 battery/current monitor driver.
- License: BSD-3-Clause-style Adafruit license.
- Project: <https://github.com/adafruit/Adafruit_INA219>
- License text: <https://github.com/adafruit/Adafruit_INA219/blob/master/license.txt>
- Repository copy: `third_party/licenses/Adafruit-INA219-BSD.txt`

### Adafruit BusIO 1.17.4

- Use: I2C register/device support required by Adafruit INA219.
- License: MIT.
- Project: <https://github.com/adafruit/Adafruit_BusIO>
- License text: <https://github.com/adafruit/Adafruit_BusIO/blob/master/LICENSE>
- Repository copy: `third_party/licenses/Adafruit-BusIO-MIT.txt`

## Resolved but unlinked PlatformIO dependencies

PlatformIO currently resolves these packages through the Adafruit INA219
dependency metadata. The repository directly includes INA219 and BusIO
headers. The following packages are installed because they are declared by the
INA219 package metadata, but the v1.98 production link map contains no objects
from these three libraries:

| Component | Version | License | Source |
| --- | ---: | --- | --- |
| Adafruit GFX Library | 1.12.6 | BSD-3-Clause-style | <https://github.com/adafruit/Adafruit-GFX-Library> |
| Adafruit SSD1306 | 2.5.17 | BSD-3-Clause-style | <https://github.com/adafruit/Adafruit_SSD1306> |
| Adafruit NeoPixel | 1.15.5 | LGPL v3 with additional permissions | <https://github.com/adafruit/Adafruit_NeoPixel> |

They remain pinned for deterministic dependency resolution and are recorded as
build-resolution dependencies rather than linked firmware components.

## Embedded fonts and generated assets

### Montserrat

SourceTX contains a generated eight-glyph Montserrat Medium subset in
`src/SourceTxLogoFont.c`. LVGL's generated Montserrat font files are also used
by the configured UI. The Montserrat font family is distributed under the SIL
Open Font License 1.1.

- Packaged source: LVGL 9.5.0 built-in Montserrat generated fonts
- Upstream project: <https://github.com/JulietaUla/Montserrat>
- Copyright notice: `third_party/licenses/Montserrat-NOTICE.txt`
- License text: `third_party/licenses/OFL-1.1.txt`
- License: <https://openfontlicense.org>

The generated C font sources are the reproducible build inputs. Regenerating
them from an upstream TTF is not required to build SourceTX v1.98.

### Font Awesome glyphs embedded by LVGL font generation

The generated LVGL Montserrat font headers identify the
`FontAwesome5-Solid+Brands+Regular.woff` input distributed by LVGL 9.5.0.
Font Awesome webfont files are licensed under the SIL Open Font License 1.1.

- Project: <https://github.com/FortAwesome/Font-Awesome>
- License information: <https://github.com/FortAwesome/Font-Awesome#license>
- Copyright and format notice:
  `third_party/licenses/Font-Awesome-5-NOTICE.txt`
- License text: `third_party/licenses/OFL-1.1.txt`

The embedded input is the webfont, not the separately distributed SVG or
JavaScript artwork to which Font Awesome applies CC BY 4.0. The exact glyph
ranges and conversion options are preserved in LVGL's generated font headers.

### SourceTX image assets

The repository contains PNG model artwork in `assets/` and generated LVGL
image data in `src/Dashboard*Image.cpp`. All vehicle artwork and UI illustration
assets are original generated assets created specifically for SourceTX and are
owned by the project under the project's copyright. They carry no third-party
licensing or royalty obligations.

## Framework and build dependencies

These packages are used to build the firmware or provide framework components;
they are not SourceTX-owned code:

| Component | Version observed | License / source |
| --- | ---: | --- |
| pioarduino `platform-espressif32` | 55.03.39 | Apache-2.0; <https://github.com/pioarduino/platform-espressif32> |
| Arduino-ESP32 framework | 3.3.9 | LGPL-2.1-or-later; <https://github.com/espressif/arduino-esp32> |
| Arduino-ESP32 precompiled libraries | 5.5.4+sha.735507283d | LGPL-2.1-or-later; <https://github.com/espressif/esp32-arduino-lib-builder> |
| ESP-IDF components | 5.5.4 via the Arduino framework | Component-specific upstream licenses shipped in the pinned framework package; <https://github.com/espressif/esp-idf> |
| esptool | 5.3.0 | GPL-2.0-or-later; <https://github.com/pioarduino/esptool> |

Toolchains and upload utilities are build tools and are not normally part of
the shipped transmitter firmware. Their licenses still apply to the tooling
distribution and should remain available to developers building SourceTX.

## Release packaging policy

- Keep all PlatformIO dependencies pinned exactly in `platformio.ini`.
- Preserve the complete license/notice text for every dependency actually
  linked into the firmware.
- Include this file and the dependency license texts with source releases and
  commercial firmware distribution documentation where required.
- Re-run the dependency and asset audit whenever a library, font, icon, image,
  framework, or build target changes.

