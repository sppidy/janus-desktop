using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NEON.Trader.Desktop.Models;
using NEON.Trader.Desktop.Services;

namespace NEON.Trader.Desktop.Models
{
    public sealed class SignalRowVm
    {
        public string Symbol { get; set; } = "";
        public string SignalDir { get; set; } = "";
        public Brush  SignalBrush { get; set; } = null!;
        public string PriceText { get; set; } = "—";
        public string ConfText  { get; set; } = "—";
        public string SlText    { get; set; } = "—";
        public string TgtText   { get; set; } = "—";
        public string Reason    { get; set; } = "";
    }
}

namespace NEON.Trader.Desktop.Views
{
    public sealed partial class ScannerPage : Page
    {
        private readonly AppServices _services = App.Services;
        private readonly ObservableCollection<SignalRowVm> _rows = new();
        private bool _busy;

        public ScannerPage()
        {
            InitializeComponent();
            SignalsList.ItemsSource = _rows;
        }

        private async void RuleScan_Click(object sender, object e)
        {
            if (_busy) return;
            SetBusy(true, "running rule scan...");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var res = await _services.Api.RunRuleScanAsync(cts.Token);
                PopulateRows(res.Signals ?? new List<Signal>());
                StateText.Text = $"{_rows.Count} signals";
                ErrorText.Text = "";
            }
            catch (Exception ex)
            {
                StateText.Text = "error";
                ErrorText.Text = ex.Message;
                (App.MainWindow as MainWindow)?.Toast(ex.Message, error: true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void AiScan_Click(object sender, object e)
        {
            if (_busy) return;
            SetBusy(true, "starting ai scan...");
            try
            {
                using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var accepted = await _services.Api.StartAiScanAsync(startCts.Token);
                if (string.IsNullOrEmpty(accepted.JobId))
                    throw new Exception("No job id returned");

                StateText.Text = "ai scan running...";
                var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);
                ScanJob? job = null;
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(2500));
                    using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    job = await _services.Api.GetScanJobAsync(accepted.JobId!, pollCts.Token);
                    var s = (job.Status ?? "").ToLowerInvariant();
                    if (s == "completed" || s == "failed" || s == "error") break;
                }

                if (job is null) throw new Exception("AI scan timed out");

                if ((job.Status ?? "").Equals("completed", StringComparison.OrdinalIgnoreCase))
                {
                    PopulateRows(job.Signals ?? new List<Signal>());
                    StateText.Text = $"{_rows.Count} signals";
                    ErrorText.Text = "";
                }
                else
                {
                    var err = job.Error ?? job.Status ?? "unknown error";
                    StateText.Text = "error";
                    ErrorText.Text = err;
                    (App.MainWindow as MainWindow)?.Toast(err, error: true);
                }
            }
            catch (Exception ex)
            {
                StateText.Text = "error";
                ErrorText.Text = ex.Message;
                (App.MainWindow as MainWindow)?.Toast(ex.Message, error: true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void PopulateRows(List<Signal> signals)
        {
            var up    = (Brush)Application.Current.Resources["UpBrush"];
            var down  = (Brush)Application.Current.Resources["DownBrush"];
            var muted = (Brush)Application.Current.Resources["TextMuteBrush"];

            _rows.Clear();
            foreach (var s in signals)
            {
                var dir = (s.SignalDir ?? "").Trim().ToUpperInvariant();
                Brush brush = dir switch
                {
                    "BUY"  => up,
                    "SELL" => down,
                    _      => muted,
                };
                _rows.Add(new SignalRowVm
                {
                    Symbol      = s.Symbol,
                    SignalDir   = string.IsNullOrEmpty(dir) ? "HOLD" : dir,
                    SignalBrush = brush,
                    PriceText   = Fmt(s.Price),
                    ConfText    = s.Confidence is null
                        ? "—"
                        : ((int)Math.Round(s.Confidence.Value * 100)).ToString(CultureInfo.InvariantCulture) + "%",
                    SlText      = Fmt(s.StopLoss),
                    TgtText     = Fmt(s.Target),
                    Reason      = s.Reason ?? "",
                });
            }
        }

        private static string Fmt(double? v) =>
            v is null ? "—" : v.Value.ToString("F2", CultureInfo.InvariantCulture);

        private void SetBusy(bool busy, string? state = null)
        {
            _busy = busy;
            RuleBtn.IsEnabled = !busy;
            AiBtn.IsEnabled   = !busy;
            if (state is not null) StateText.Text = state;
        }
    }
}
