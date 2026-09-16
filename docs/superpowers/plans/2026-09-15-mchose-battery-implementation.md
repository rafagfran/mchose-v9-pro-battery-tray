# MchoseBattery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a small Windows tray application that safely monitors an MCHOSE V9 Pro battery through its 2.4 GHz HID dongle.

**Architecture:** A `net8.0-windows` Core library wraps native Windows HID enumeration and I/O, validates the single known V9 Pro battery protocol, and produces snapshots. A WinForms tray executable consumes snapshots and exposes diagnostics, startup registration, logs, and fault-tolerant periodic refreshes.

**Tech Stack:** C# 12, .NET 8, WinForms, P/Invoke (`setupapi.dll`, `hid.dll`, `kernel32.dll`), MSTest. No HID NuGet package.

**Spec:** `docs/superpowers/specs/2026-09-15-mchose-battery-design.md`

## Global Constraints

- Support Windows 10/11 and publish `win-x64`; keep code architecture-neutral for `win-arm64`.
- Send only `55 65 01` padded to 64 bytes, and only to `VID 291D`, `PID 385D` candidates.
- Never issue firmware, EQ, RGB, volume, or unknown HID commands.
- Treat invalid responses, timeouts, access failures, removal, and headset power-off as nonfatal.
- Keep tray polling at 30 seconds; serialize all refreshes.
- Log only state transitions and failures to a capped file under `%LocalAppData%`.

---

### Task 1: Solution skeleton and protocol parser

**Files:**
- Create: `.gitignore`
- Create: `MchoseBattery.sln`
- Create: `src/MchoseBattery.Core/MchoseBattery.Core.csproj`
- Create: `src/MchoseBattery.Core/Protocol/MchoseProtocol.cs`
- Create: `tests/MchoseBattery.Core.Tests/MchoseBattery.Core.Tests.csproj`
- Create: `tests/MchoseBattery.Core.Tests/Protocol/MchoseProtocolTests.cs`

**Interfaces:**
- Produces `MchoseProtocol.CreateBatteryRequest(): byte[]` and `MchoseProtocol.TryParseBatteryResponse(ReadOnlySpan<byte>, out BatteryReading)`.
- `BatteryReading` contains `int Percentage` and `byte StatusCode`.

- [ ] **Step 1: Write failing parser tests**

```csharp
[TestMethod]
public void CreateBatteryRequest_Is64BytesAndContainsOnlyKnownCommand()
{
    var request = MchoseProtocol.CreateBatteryRequest();
    Assert.AreEqual(64, request.Length);
    CollectionAssert.AreEqual(new byte[] { 0x55, 0x65, 0x01 }, request[..3]);
    Assert.IsTrue(request[3..].All(value => value == 0));
}

[DataTestMethod]
[DataRow(0)] [DataRow(78)] [DataRow(100)]
public void TryParseBatteryResponse_AcceptsBoundedSignedReply(int percentage)
{
    var frame = new byte[64]; frame[0] = 0x55; frame[1] = 0x65; frame[2] = (byte)percentage; frame[3] = 0x10;
    Assert.IsTrue(MchoseProtocol.TryParseBatteryResponse(frame, out var reading));
    Assert.AreEqual(percentage, reading.Percentage);
}
```

- [ ] **Step 2: Run the parser test and confirm it fails because `MchoseProtocol` does not exist**

Run: `dotnet test tests/MchoseBattery.Core.Tests --filter MchoseProtocolTests`

- [ ] **Step 3: Implement the minimal immutable parser**

```csharp
public static byte[] CreateBatteryRequest()
{
    var frame = new byte[64]; frame[0] = 0x55; frame[1] = 0x65; frame[2] = 0x01; return frame;
}

public static bool TryParseBatteryResponse(ReadOnlySpan<byte> frame, out BatteryReading reading)
{
    reading = default;
    if (frame.Length < 4 || frame[0] != 0x55 || frame[1] != 0x65 || frame[2] > 100) return false;
    reading = new(frame[2], frame[3]); return true;
}
```

- [ ] **Step 4: Add malformed-frame tests and run all Core tests**

```csharp
[TestMethod]
public void TryParseBatteryResponse_RejectsWrongSignatureAndShortFrame()
{
    Assert.IsFalse(MchoseProtocol.TryParseBatteryResponse(new byte[] { 0x55, 0x64, 99, 0 }, out _));
    Assert.IsFalse(MchoseProtocol.TryParseBatteryResponse(new byte[] { 0x55, 0x65, 99 }, out _));
}
```

Run: `dotnet test tests/MchoseBattery.Core.Tests`

### Task 2: Native HID inventory and device selection

**Files:**
- Create: `src/MchoseBattery.Core/Interop/NativeHid.cs`
- Create: `src/MchoseBattery.Core/Devices/HidDeviceInfo.cs`
- Create: `src/MchoseBattery.Core/Devices/HidDeviceEnumerator.cs`
- Create: `src/MchoseBattery.Core/Devices/MchoseDevice.cs`
- Create: `tests/MchoseBattery.Core.Tests/Devices/MchoseDeviceTests.cs`

**Interfaces:**
- Consumes `MchoseProtocol`.
- Produces `IReadOnlyList<HidDeviceInfo> Enumerate()` and `MchoseDevice.FindDevice(...)`.
- `HidDeviceInfo` exposes path, VID/PID, strings, UsagePage, Usage, input/output/feature lengths.

- [ ] **Step 1: Write failing selection tests using in-memory `HidDeviceInfo` values**

```csharp
[TestMethod]
public void FindCandidates_RequiresKnownVidPidAnd64ByteInputOutputReports()
{
    var infos = new[] { new HidDeviceInfo("a", 0x291D, 0x385D, null, null, null, 0, 0, 64, 64, 0),
                        new HidDeviceInfo("b", 0x291D, 0x385D, null, null, null, 0, 0, 32, 64, 0) };
    CollectionAssert.AreEqual(new[] { infos[0] }, MchoseDevice.FindCandidates(infos).ToArray());
}
```

- [ ] **Step 2: Run the selection test and confirm it fails because `HidDeviceInfo` is unavailable**

Run: `dotnet test tests/MchoseBattery.Core.Tests --filter MchoseDeviceTests`

- [ ] **Step 3: Implement value types and the pure candidate predicate, then add safe P/Invoke enumeration**

```csharp
public static IEnumerable<HidDeviceInfo> FindCandidates(IEnumerable<HidDeviceInfo> devices) =>
    devices.Where(d => d.VendorId == 0x291D && d.ProductId == 0x385D && d.InputReportByteLength == 64 && d.OutputReportByteLength == 64);
```

Use `SetupDiGetClassDevs`, `SetupDiEnumDeviceInterfaces`, `SetupDiGetDeviceInterfaceDetail`, `HidD_GetAttributes`, `HidD_GetPreparsedData`, `HidP_GetCaps`, and guarded string queries. Ensure every unmanaged allocation and preparsed-data pointer is freed in `finally`.

- [ ] **Step 4: Run Core tests and manually validate read-only enumeration**

Run: `dotnet test tests/MchoseBattery.Core.Tests`

Run: `dotnet run --project src/MchoseBattery.Tray -- --diagnose`

Expected: each HID collection prints identity, strings when available, Usage Page/Usage, report lengths, and path; no device is opened for write.

### Task 3: Safe HID query, snapshots, logging, and polling

**Files:**
- Create: `src/MchoseBattery.Core/Devices/IHidTransport.cs`
- Create: `src/MchoseBattery.Core/Devices/WindowsHidTransport.cs`
- Create: `src/MchoseBattery.Core/Services/BatterySnapshot.cs`
- Create: `src/MchoseBattery.Core/Services/BatteryService.cs`
- Create: `src/MchoseBattery.Core/Diagnostics/AppLogger.cs`
- Create: `tests/MchoseBattery.Core.Tests/Services/BatteryServiceTests.cs`

**Interfaces:**
- Consumes candidate `HidDeviceInfo`, `IHidTransport.Exchange`, and `MchoseProtocol`.
- Produces `Task<BatterySnapshot> RefreshAsync(CancellationToken)` and `RequestRefresh()`.
- `BatterySnapshot` represents `DongleNotFound`, `HeadsetDisconnected`, or `Connected` with percentage.

- [ ] **Step 1: Write failing service tests with a fake transport**

```csharp
[TestMethod]
public async Task RefreshAsync_MapsTimeoutWithCandidateToHeadsetDisconnected()
{
    var service = CreateService(candidateExists: true, exchange: _ => null);
    var snapshot = await service.RefreshAsync(CancellationToken.None);
    Assert.AreEqual(BatteryConnectionState.HeadsetDisconnected, snapshot.State);
}
```

- [ ] **Step 2: Run the service test and confirm it fails because `BatteryService` is unavailable**

Run: `dotnet test tests/MchoseBattery.Core.Tests --filter BatteryServiceTests`

- [ ] **Step 3: Implement serialized refresh and the Windows transport**

`WindowsHidTransport.Exchange` must open only a path supplied by `FindCandidates`, write exactly the 64-byte request, wait at most 500 ms using overlapped I/O, and close the handle even when a native call fails. `BatteryService` must validate replies through `MchoseProtocol`, never throw into callers, and use `PeriodicTimer(TimeSpan.FromSeconds(30))` for background updates.

- [ ] **Step 4: Add state tests and run all Core tests**

```csharp
[TestMethod]
public async Task RefreshAsync_MapsValidatedReplyToConnected()
{
    var reply = new byte[64]; reply[0] = 0x55; reply[1] = 0x65; reply[2] = 78;
    var snapshot = await CreateService(true, _ => reply).RefreshAsync(CancellationToken.None);
    Assert.AreEqual(BatteryConnectionState.Connected, snapshot.State);
    Assert.AreEqual(78, snapshot.Percentage);
}
```

Run: `dotnet test tests/MchoseBattery.Core.Tests`

### Task 4: Diagnostics CLI and tray-only application

**Files:**
- Create: `src/MchoseBattery.Tray/MchoseBattery.Tray.csproj`
- Create: `src/MchoseBattery.Tray/Program.cs`
- Create: `src/MchoseBattery.Tray/DiagnosticsCommand.cs`
- Create: `src/MchoseBattery.Tray/TrayApplicationContext.cs`
- Create: `src/MchoseBattery.Tray/StartupRegistration.cs`
- Create: `src/MchoseBattery.Tray/Properties/PublishProfiles/win-x64.pubxml`
- Create: `tests/MchoseBattery.Core.Tests/Tray/StartupRegistrationTests.cs`

**Interfaces:**
- Consumes `HidDeviceEnumerator`, `MchoseDevice`, and `BatteryService`.
- Produces `MchoseBattery.exe --diagnose`, `--inspect-device <path>`, and `--probe-battery <path>`.

- [ ] **Step 1: Write failing startup command construction tests**

```csharp
[TestMethod]
public void StartupCommand_QuotesExecutablePath()
{
    Assert.AreEqual("\"C:\\Program Files\\MchoseBattery\\MchoseBattery.exe\"", StartupRegistration.BuildCommand("C:\\Program Files\\MchoseBattery\\MchoseBattery.exe"));
}
```

- [ ] **Step 2: Run the test and confirm it fails because `StartupRegistration` is unavailable**

Run: `dotnet test tests/MchoseBattery.Core.Tests --filter StartupRegistrationTests`

- [ ] **Step 3: Implement command modes and tray context**

The tray context must create no form, use a `NotifyIcon`, give its context menu an informational disabled status item plus `Atualizar agora`, `Abrir ao iniciar o Windows`, and `Sair`, and map states to the specified Portuguese labels. Subscribe to `DeviceChange` through a hidden `NativeWindow` and call `RequestRefresh`. `--probe-battery` must require one matching V9 Pro path and print request/reply hex.

- [ ] **Step 4: Run Core tests and diagnostics**

Run: `dotnet test tests/MchoseBattery.Core.Tests`

Run: `dotnet run --project src/MchoseBattery.Tray -- --diagnose`

### Task 5: Documentation, publishing, and hardware verification

**Files:**
- Create: `README.md`
- Modify: `src/MchoseBattery.Tray/MchoseBattery.Tray.csproj`

**Interfaces:**
- Documents every public command and the V9 Pro-only protocol profile.

- [ ] **Step 1: Document build, diagnostics, safety boundary, protocol evidence, logs, startup, known VID/PID, and V9 uncertainty**

Include exact commands:

```powershell
dotnet restore
dotnet build
dotnet run --project src/MchoseBattery.Tray
dotnet publish src/MchoseBattery.Tray -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- [ ] **Step 2: Build and publish**

Run: `dotnet restore; dotnet build -c Release; dotnet test -c Release; dotnet publish src/MchoseBattery.Tray -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`

Expected: no build or test failures and one published executable under `src/MchoseBattery.Tray/bin/Release/net8.0-windows/win-x64/publish/`.

- [ ] **Step 3: Perform hardware acceptance sequence**

Run: `MchoseBattery.exe --diagnose`, identify a `291D:385D` candidate with 64-byte input/output reports, then run `MchoseBattery.exe --probe-battery "<exact path>"` while the headset is on. Verify the tray shows the returned percentage; switch off and on the headset; verify `Desconectado` then automatic recovery without restarting.

## Plan Self-Review

- Spec coverage: Tasks 1–3 cover protocol safety, discovery, errors, polling, and logging; Task 4 covers diagnostics, tray, startup, and hot-plug; Task 5 covers release packaging and acceptance.
- Placeholder scan: no deferred implementation placeholders are used; the only hardware-dependent value is the path emitted by diagnostics.
- Type consistency: `BatteryService`, `BatterySnapshot`, `MchoseDevice.FindCandidates`, `MchoseProtocol`, and `StartupRegistration` are named consistently in their producer and consumer tasks.
