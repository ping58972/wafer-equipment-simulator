# Wafer Equipment Simulator

A C#/.NET 10 portfolio project that simulates a batch of wafers moving through virtual processing chambers. An operator selects the batch size and number of chambers, optionally injects a sensor timeout, and inspects the state history and alarm for each wafer.

## Demo

Start the loopback-only ASP.NET Core API with the .NET 10 SDK.

```sh
dotnet run --project src/Equipment.Api
```

Submit 12 wafers to four chambers and inject a timeout on wafer 3 from another terminal.

```sh
curl --fail-with-body http://127.0.0.1:5078/batches \
  -H 'Content-Type: application/json' \
  --data '{"wafers":12,"chambers":4,"faultWafer":3}'
```

The response contains 12 ordered results and the peak number of active chambers. Wafer 3 has the stage history `Idle → Loaded → Processing → Faulted` and the alarm `SIM_SENSOR_TIMEOUT`. Each other wafer reaches `Completed`. Worker assignment can vary by run.

On Windows, start the API first, then launch the **Windows Forms operator console**.

```powershell
dotnet run --project src/Equipment.Operator
```

Enter 12 wafers, four chambers, and fault wafer 3, then click **Run batch**. The table shows chamber assignment, state transitions, and alarms. The form awaits HTTP calls so the UI message loop stays responsive during a batch.

## Design and skills

| Component | Implementation |
| --- | --- |
| [Equipment.Core](src/Equipment.Core/) | Reusable .NET class library with typed request/result contracts, state transition guards, and a batch simulator independent of HTTP and UI code. |
| [Equipment.Api](src/Equipment.Api/) | ASP.NET Core minimal HTTP API with JSON serialization, batch validation, cancellation, and a semaphore that admits one batch at a time. A competing request receives HTTP 409. |
| [Equipment.Operator](src/Equipment.Operator/) | Windows Forms client with asynchronous HTTP, input controls, a per-wafer results table, and operator-facing error messages. |
| [Checks](tests/) | Dependency-free C# checks and a Python standard-library HTTP smoke test. |

The scheduler uses a bounded `Channel<int>` as a producer-consumer queue. Let `W` be the wafer count and `C` the requested chamber count. The service creates `min(W, C)` worker tasks and gives the channel twice that many waiting slots. With 12 wafers and four chambers, up to four cycles run concurrently and up to eight wafer IDs wait in the queue. The producer asynchronously waits for room when the queue fills. Each worker owns its cycle state and writes its result to a unique index. A lock protects the shared count of active chambers. Cancellation propagates to blocked channel operations and simulated chamber delays.

The project uses C#, .NET 10, ASP.NET Core, Windows Forms, HTTP/JSON, async/await, cancellation tokens, synchronization, state machines, producer-consumer scheduling, and Python for API verification.

## Test procedures

```sh
dotnet run --project tests/Equipment.Checks -c Release
dotnet build src/Equipment.Operator -c Release
# With Equipment.Api running:
python3 tests/http_smoke.py
```

Run the build commands sequentially because the projects share a compiled class library. The core checks cover ordering, concurrency limits, fault isolation, invalid state transitions, input validation, and cancellation. The HTTP checks cover the health response, batch result contract, input rejection, overlapping submissions, and recovery after the active batch completes.

The core and HTTP checks passed on Apple Silicon with .NET SDK 10.0.404. The Windows Forms client cross-compiled with zero warnings and errors. Interactive UI verification requires a Windows machine.

The simulator uses synthetic wafer IDs and timer-based chamber cycles. The API uses HTTP/JSON and binds to the local loopback interface. Windows is required to run the operator console.
