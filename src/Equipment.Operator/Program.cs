using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Equipment.Core;

namespace Equipment.Operator;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new OperatorForm());
    }
}

internal sealed class OperatorForm : Form
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5078"),
        Timeout = TimeSpan.FromSeconds(30)
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<Stage>() }
    };

    private readonly NumericUpDown wafers = new() { Minimum = 1, Maximum = 200, Value = 12, Width = 70 };
    private readonly NumericUpDown chambers = new() { Minimum = 1, Maximum = 8, Value = 4, Width = 55 };
    private readonly NumericUpDown fault = new() { Minimum = 0, Maximum = 200, Width = 70 };
    private readonly Button run = new() { Text = "Run batch", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, Text = "Start the local API first." };
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView results = new()
    {
        View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true
    };

    public OperatorForm()
    {
        Text = "Wafer Equipment Simulator";
        MinimumSize = new Size(690, 330);
        Size = new Size(790, 450);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        controls.Controls.AddRange([
            Caption("Wafers"), wafers, Caption("Chambers"), chambers,
            Caption("Fault wafer (0 = off)"), fault, run
        ]);
        results.Columns.Add("Wafer", 68);
        results.Columns.Add("Chamber", 80);
        results.Columns.Add("Stage history", 350);
        results.Columns.Add("Alarm", 185);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(controls, 0, 0);
        layout.Controls.Add(results, 0, 1);
        layout.Controls.Add(status, 0, 2);
        Controls.Add(layout);
        run.Click += async (_, _) => await RunBatchAsync();
        FormClosed += (_, _) => lifetime.Cancel();
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 0) };

    private async Task RunBatchAsync()
    {
        run.Enabled = false;
        results.Items.Clear();
        status.Text = "Processing simulated wafers…";
        try
        {
            var request = new BatchRequest((int)wafers.Value, (int)chambers.Value,
                fault.Value == 0 ? null : (int)fault.Value);
            using var response = await Client.PostAsJsonAsync("/batches", request, lifetime.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>(Json, lifetime.Token);
                status.Text = error?.Error ?? $"Request failed ({(int)response.StatusCode}).";
                return;
            }

            var batch = await response.Content.ReadFromJsonAsync<BatchResult>(Json, lifetime.Token)
                ?? throw new JsonException("Empty batch response.");
            foreach (var wafer in batch.Wafers)
                results.Items.Add(new ListViewItem([
                    wafer.Wafer.ToString(), wafer.Chamber.ToString(),
                    string.Join(" → ", wafer.Stages), wafer.Alarm ?? "—"
                ]));
            status.Text = $"Completed {batch.Wafers.Length} cycles · peak active chambers {batch.PeakActiveChambers}";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            status.Text = $"Unable to read batch results. {ex.Message}";
        }
        finally
        {
            if (!IsDisposed)
                run.Enabled = true;
        }
    }

    private sealed record ApiError(string Error);
}
