# Included third-party license texts

These files are carried with the SourceTX repository so source and firmware
release packages can preserve the notices required by the applicable
third-party licenses.

The inventory was generated from the PlatformIO dependency graph used by the
current production environment. Before each release, compare it with the
final link map and update the versions and source revisions in
`THIRD_PARTY_NOTICES.md`.

| File | Applies to |
| --- | --- |
| `LVGL-MIT.txt` | LVGL 9.5.0 |
| `Adafruit-BusIO-MIT.txt` | Adafruit BusIO 1.17.4 |
| `Adafruit-INA219-BSD.txt` | Adafruit INA219 1.2.3 |
| `TFT_eSPI-license.txt` | TFT_eSPI 2.5.43 and its included Adafruit GFX notices |
| `OFL-1.1.txt` | Montserrat and Font Awesome font files |
| `Montserrat-NOTICE.txt` | Montserrat copyright and packaged-source identification |
| `Font-Awesome-5-NOTICE.txt` | Font Awesome Free 5 webfont copyright, format and license identification |

The Arduino-ESP32 framework, ESP-IDF components, PlatformIO platform, and
upload tools are build dependencies normally obtained by PlatformIO rather
than files shipped in the firmware source. Their versions and upstream links
remain recorded in `THIRD_PARTY_NOTICES.md`; include their notices in any
redistribution that actually packages those components.

