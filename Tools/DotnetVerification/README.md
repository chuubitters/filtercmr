# .NET verification harness (optional)

Runs the UnityEngine-free FILTER CMR sources outside Unity, together with their Unity EditMode test files and end-to-end UDP checks against the real Python sender:

* `Networking/HandTrackingData.cs`, `HandDataParser.cs`, `UdpReceiverCore.cs`, `VideoStreamCore.cs`
* `Gestures/GestureType.cs`, `GestureStateMachine.cs`
* `Tests/Editor/NetworkingTests.cs`, `GestureTests.cs` (via a minimal NUnit-compatible shim)
* `Integration.cs`: spawns `HandTracking/tools/fake_tracker.py` and `main.py` and receives their packets over real sockets

```bash
cd Tools/DotnetVerification
FILTERCMR_PYTHON=../../HandTracking/.venv/bin/python dotnet run          # all
FILTERCMR_PYTHON=../../HandTracking/.venv/bin/python dotnet run -- Integration
```

Requires the .NET 8 SDK. `nuget.config` clears package sources because the harness needs no NuGet packages. You don't need any of this to use the project; the same unit tests also run in Unity's Test Runner.

`../generate_meta.py` creates any missing Unity `.meta` files with deterministic GUIDs.
