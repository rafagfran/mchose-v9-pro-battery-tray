# Task 1 Report: Solution Skeleton and Protocol Parser

## Status

Implementation files and the required MSTest cases were created. Test execution and compilation remain unverified because this workstation has no discoverable .NET SDK.

## RED Evidence

The supplied tests were written before `MchoseProtocol.cs` existed. The required focused command was then run:

```powershell
dotnet test tests/MchoseBattery.Core.Tests --filter MchoseProtocolTests
```

Actual output (exit code 1):

```text
dotnet : The term 'dotnet' is not recognized as the name of a cmdlet, function, script file, or operable program.
```

Therefore the expected compiler failure identifying missing `MchoseProtocol` could not be observed. A follow-up SDK lookup found no executable at `C:\Program Files\dotnet\dotnet.exe`, `C:\Program Files (x86)\dotnet\dotnet.exe`, or `C:\dotnet\dotnet.exe`.

## GREEN Implementation

After the red attempt, `MchoseProtocol` was added with the brief's exact behavior:

- `CreateBatteryRequest` returns a new 64-byte frame containing only `55 65 01` followed by zeros.
- `TryParseBatteryResponse` rejects frames shorter than four bytes, any non-`55 65` signature, and percentages above 100; valid replies preserve the percentage and status byte in `BatteryReading`.

The required malformed-frame test was added after the implementation.

## All-Core-Test Evidence

The required command was run:

```powershell
dotnet test tests/MchoseBattery.Core.Tests
```

It produced the same `dotnet` command-not-found error (exit code 1), before restore/build/test discovery. No passing-test claim is made.

## Other Verification

```powershell
git diff --check
```

Completed with exit code 0 and no output (no whitespace errors).

## Changed Files

- `.gitignore`
- `MchoseBattery.sln`
- `src/MchoseBattery.Core/MchoseBattery.Core.csproj`
- `src/MchoseBattery.Core/Protocol/MchoseProtocol.cs`
- `tests/MchoseBattery.Core.Tests/MchoseBattery.Core.Tests.csproj`
- `tests/MchoseBattery.Core.Tests/Protocol/MchoseProtocolTests.cs`
- `.superpowers/sdd/2026-09-15-mchose-battery-implementation/task-1-report.md`

## Self-Review

- Scope is limited to Task 1; no HID, transport, tray, logging, or later-task types were added.
- The test expectations use hand-derived protocol literals and exercise the public API without mocks.
- `BatteryReading` is an immutable `readonly record struct` with the required `Percentage` and `StatusCode` properties.
- The solution/project files and package versions have not been compiled or restored due to the missing SDK. Install a .NET 8 SDK, then rerun the focused command followed by the all-Core command to obtain the mandated red/green evidence.
