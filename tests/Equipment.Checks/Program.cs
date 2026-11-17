using Equipment.Core;

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var simulator = new BatchSimulator(TimeSpan.FromMilliseconds(40));
var parallel = await simulator.RunAsync(new BatchRequest(16, 4, 3), CancellationToken.None);
Check(parallel.Wafers.Select(result => result.Wafer).SequenceEqual(Enumerable.Range(1, 16)),
    "Each input wafer must appear once, in input order.");
Check(parallel.PeakActiveChambers is > 1 and <= 4, "Processing must overlap within the chamber limit.");
Check(parallel.Wafers[2].Stages.SequenceEqual(new[] { Stage.Idle, Stage.Loaded, Stage.Processing, Stage.Faulted }),
    "The injected fault must enter the Faulted state.");
Check(parallel.Wafers[2].Alarm == "SIM_SENSOR_TIMEOUT", "Expected the simulated alarm.");
Check(parallel.Wafers.Where(result => result.Wafer != 3).All(result =>
    result.Stages[^1] == Stage.Completed && result.Alarm is null), "Other wafers must complete.");
Console.WriteLine("PASS: ordering, concurrency bound, and fault isolation");

var serial = await simulator.RunAsync(new BatchRequest(3, 1), CancellationToken.None);
Check(serial.PeakActiveChambers == 1, "A single chamber must process sequentially.");
Console.WriteLine("PASS: one-chamber operation");

var cycle = new ChamberCycle();
try
{
    cycle.Advance(Stage.Completed);
    throw new Exception("Expected an invalid transition.");
}
catch (InvalidOperationException)
{
    Check(cycle.Current == Stage.Idle, "Invalid transitions must preserve state.");
}
Check(BatchSimulator.Validate(new BatchRequest(0, 1)) is not null, "Empty batches must be rejected.");
Check(BatchSimulator.Validate(new BatchRequest(1, 9)) is not null, "Oversized chamber counts must be rejected.");
Check(BatchSimulator.Validate(new BatchRequest(3, 1, 4)) is not null, "The fault must be in the batch.");
Console.WriteLine("PASS: transition guard and input validation");

using var cancellation = new CancellationTokenSource();
var running = simulator.RunAsync(new BatchRequest(100, 1), cancellation.Token);
cancellation.Cancel();
try
{
    await running.WaitAsync(TimeSpan.FromSeconds(3));
    throw new Exception("Expected cancellation.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("PASS: cancellation while submitting a batch");
}
