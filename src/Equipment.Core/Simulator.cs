using System.Threading.Channels;

namespace Equipment.Core;

public enum Stage { Idle, Loaded, Processing, Completed, Faulted }

public sealed record BatchRequest(int Wafers, int Chambers, int? FaultWafer = null);
public sealed record WaferResult(int Wafer, int Chamber, Stage[] Stages, string? Alarm);
public sealed record BatchResult(WaferResult[] Wafers, int PeakActiveChambers);

public sealed class ChamberCycle
{
    private readonly List<Stage> stages = [Stage.Idle];
    public Stage Current => stages[^1];

    public void Advance(Stage next)
    {
        bool allowed = (Current, next) switch
        {
            (Stage.Idle, Stage.Loaded) => true,
            (Stage.Loaded, Stage.Processing) => true,
            (Stage.Processing, Stage.Completed or Stage.Faulted) => true,
            _ => false
        };
        if (!allowed)
            throw new InvalidOperationException($"Invalid transition from {Current} to {next}.");
        stages.Add(next);
    }

    public Stage[] Snapshot() => stages.ToArray();
}

public sealed class BatchSimulator
{
    private readonly TimeSpan processTime;

    public BatchSimulator(TimeSpan processTime)
    {
        if (processTime <= TimeSpan.Zero || processTime > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(processTime));
        this.processTime = processTime;
    }

    public static string? Validate(BatchRequest request)
    {
        if (request.Wafers is < 1 or > 200)
            return "Wafer count must be between 1 and 200.";
        if (request.Chambers is < 1 or > 8)
            return "Chamber count must be between 1 and 8.";
        if (request.FaultWafer is int fault && (fault < 1 || fault > request.Wafers))
            return "Fault wafer must refer to a wafer in this batch.";
        return null;
    }

    public async Task<BatchResult> RunAsync(BatchRequest request, CancellationToken cancellationToken)
    {
        if (Validate(request) is string error)
            throw new ArgumentException(error, nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        int workers = Math.Min(request.Chambers, request.Wafers);
        var queue = Channel.CreateBounded<int>(new BoundedChannelOptions(workers * 2)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var results = new WaferResult[request.Wafers];
        var metrics = new ConcurrencyMeter();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task ProduceAsync()
        {
            try
            {
                for (int wafer = 1; wafer <= request.Wafers; wafer++)
                    await queue.Writer.WriteAsync(wafer, stop.Token);
            }
            finally
            {
                queue.Writer.TryComplete();
            }
        }

        async Task ConsumeAsync(int chamber)
        {
            try
            {
                await foreach (int wafer in queue.Reader.ReadAllAsync(stop.Token))
                {
                    stop.Token.ThrowIfCancellationRequested();
                    metrics.Enter();
                    try
                    {
                        var cycle = new ChamberCycle();
                        cycle.Advance(Stage.Loaded);
                        cycle.Advance(Stage.Processing);
                        await Task.Delay(processTime, stop.Token);
                        bool faulted = wafer == request.FaultWafer;
                        cycle.Advance(faulted ? Stage.Faulted : Stage.Completed);
                        results[wafer - 1] = new WaferResult(
                            wafer, chamber, cycle.Snapshot(), faulted ? "SIM_SENSOR_TIMEOUT" : null);
                    }
                    finally
                    {
                        metrics.Leave();
                    }
                }
            }
            catch
            {
                // Stop the producer if it is blocked on a full queue.
                await stop.CancelAsync();
                throw;
            }
        }

        var consumers = Enumerable.Range(1, workers).Select(ConsumeAsync).ToArray();
        await Task.WhenAll(consumers.Append(ProduceAsync()));
        return new BatchResult(results, metrics.Peak);
    }

    private sealed class ConcurrencyMeter
    {
        private readonly object gate = new();
        private int active;
        public int Peak { get; private set; }

        public void Enter()
        {
            lock (gate)
            {
                active++;
                Peak = Math.Max(Peak, active);
            }
        }

        public void Leave()
        {
            lock (gate)
                active--;
        }
    }
}
