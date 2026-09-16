# MchoseBattery Design

## Objective

Build a Windows 10/11 x64 .NET 8 tray-only application that reads MCHOSE V9 Pro headset battery state over its 2.4 GHz USB dongle without MCHOSE HUB. The application must be publishable as a self-contained single executable and remain alive across device removal, headset power-off, timeouts, and malformed HID replies.

## Evidence-based protocol scope

The V9 Pro dongle is identified as VID `0x291D`, PID `0x385D`. The battery query uses 64-byte buffers, writes an output report, then reads an input report with a timeout, and accepts only replies beginning `0x55 0x65`.

Confirmed implementation profile:

```text
Transport: HID output report / interrupt OUT followed by input report
Report frame size: 64 bytes
Request: 55 65 01 00 ... 00
Reply signature: 55 65
Battery: reply byte offset 2, direct percentage 0..100
Status: reply byte offset 3, preserved for diagnostics only
Timeout: 500 ms
```

Opening by VID/PID alone does not establish the HID collection path, Usage Page, Usage, or report ID semantics, so this profile is V9 Pro-only and must be gated by a validated response. The normal V9 is not assumed compatible.

The application sends only the read-only battery request above, and only after an explicit protocol profile match. No firmware, EQ, RGB, volume, or unknown command is sent. Firmware-related Feature Report commands are out of scope and excluded.

## Architecture

`MchoseBattery.Core` owns native Windows HID discovery, device handles, protocol frames, safe response parsing, diagnostics, logging, and polling state. It exposes an immutable battery snapshot to the UI. It uses P/Invoke directly against SetupAPI, hid.dll, and Kernel32 rather than adding a HID NuGet dependency.

`MchoseBattery.Tray` is a WinForms executable with no main window. It maps snapshots to a `NotifyIcon` tooltip and context menu, triggers immediate refreshes after device-change notifications or manual selection, and persists the optional startup value in the current-user Run registry key.

The command line is handled before the tray starts:

```text
--diagnose / --list-hid    enumerate HID collections without writing
--inspect-device <path>    show report capabilities for one collection without writing
--probe-battery <path>     explicitly send the confirmed V9 Pro read-only command and print raw input
```

`--probe-battery` requires a full device interface path, prints the raw request/reply, and is the only diagnostic mode allowed to write. It must refuse non-`291D:385D` devices.

## HID selection and state model

Discovery enumerates every HID device interface and gathers path, VID/PID, manufacturer, product, serial (when permissions permit), Usage Page, Usage, input/output/feature report lengths, and attributes. The normal profile filters `291D:385D` collections with 64-byte input and output capability. It tries candidates one at a time, considering a headset connected only when a response passes protocol validation and reports a percentage in 0..100.

States are: `DongleNotFound` (no candidate VID/PID), `HeadsetDisconnected` (dongle candidate but no valid reply), `Connected` (valid reply), and `Error` (transient detail for logs; UI falls back to the relevant nonfatal state). Polling occurs every 30 seconds, with a serialized refresh gate. A device notification and menu refresh request an immediate refresh without concurrent HID I/O.

## Error handling, privacy, and logs

Every native call returns a result rather than propagating an exception to the tray loop. Handles are closed deterministically. Errors are logged to `%LocalAppData%\\MchoseBattery\\mchose-battery.log`, capped at 256 KiB by retaining only the newest data. Normal polls do not log successes; state transitions and failures log a short message.

## Tests and verification

Unit tests cover frame construction, valid/invalid battery replies, percentage bounds, selection predicate, state mapping, and startup command construction. They do not require hardware. Hardware validation is done with `--diagnose`, then an explicit `--probe-battery` capture, then tray reconnection testing.

## Build and compatibility

Target `net8.0-windows`, `win-x64` by default; source remains architecture-neutral so `win-arm64` publish works. Release publishing uses `SelfContained`, `PublishSingleFile`, and `IncludeNativeLibrariesForSelfExtract` properties. The project contains `.gitignore`, README, solution, source projects, and tests.
