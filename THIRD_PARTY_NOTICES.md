# SourceTX Companion Third-Party Notices

SourceTX Companion distributes third-party components under their own
licenses. Those licenses apply only to their respective components and do not
replace the SourceTX Companion license in `LICENSE`.

## esptool 5.3.0

The `tools/esptool.exe` flashing utility is esptool 5.3.0 from the pioarduino
project. It is distributed under GPL-2.0-or-later. Its ESP32 stub-flasher
component also carries Apache-2.0 and MIT license notices.

- Source repository: <https://github.com/pioarduino/esptool> (version 5.3.0)
- Main license: `third_party/licenses/esptool-GPL-2.0-or-later.txt`
- Stub-flasher licenses:
  `third_party/licenses/esptool-stub-LICENSE-APACHE.txt` and
  `third_party/licenses/esptool-stub-LICENSE-MIT.txt`

## Bundled SourceTX firmware

The offline installation and recovery images contain the SourceTX firmware and
its third-party runtime libraries, fonts, and icons. Their component list,
copyright notices, source links, and license files are preserved under
`third_party/SourceTX-THIRD_PARTY_NOTICES.md` and `third_party/licenses/`.

Re-run the dependency and asset audit whenever the bundled firmware, flashing
tool, library, font, icon, or build target changes.
