using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Janus.Desktop.Models;
using Janus.Desktop.Services;

namespace Janus.Desktop.Models
{
    public sealed class WatchTileVm
    {
        public string Symbol { get; set; } = "";
        public string PriceText { get; set; } = "";
        public Brush DirBrush { get; set; } = null!;
    }
}

namespace Janus.Desktop.Views
{
    public sealed partial class WatchlistPage : Page
    {
        private readonly AppServices _services = App.Services;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly Dictionary<string, double> _prevPrices = new();
        private readonly ObservableCollection<WatchTileVm> _tiles = new();

        public WatchlistPage()
        {
            InitializeComponent();
            TilesView.ItemsSource = _tiles;
            _timer.Tick += async (_, _) => await RefreshAsync();
            Loaded += async (_, _) =>
            {
                _timer.Start();
                await RefreshAsync();
            };
            Unloaded += (_, _) => _timer.Stop();
        }

        private async Task RefreshAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var status = await _services.Api.GetStatusAsync(cts.Token);
                var prices = await _services.Api.GetPricesAsync(cts.Token);

                var watch = status.Watchlist ?? new List<string>();
                var priceMap = prices.Prices ?? new Dictionary<string, double>();

                var upBrush    = (Brush)Application.Current.Resources["UpBrush"];
                var downBrush  = (Brush)Application.Current.Resources["DownBrush"];
                var borderBrsh = (Brush)Application.Current.Resources["BorderBrush"];

                _tiles.Clear();
                foreach (var sym in watch)
                {
                    priceMap.TryGetValue(sym, out var price);
                    Brush dir = borderBrsh;
                    if (_prevPrices.TryGetValue(sym, out var prev))
                    {
                        if (price > prev) dir = upBrush;
                        else if (price < prev) dir = downBrush;
                    }
                    _tiles.Add(new WatchTileVm
                    {
                        Symbol    = sym,
                        PriceText = price > 0 ? price.ToString("F2", CultureInfo.InvariantCulture) : "—",
                        DirBrush  = dir,
                    });
                    _prevPrices[sym] = price;
                }

                EmptyText.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdatedText.Text = "updated " + DateTime.Now.ToString("HH:mm:ss");
                ErrorText.Text = "";
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }
    }
}
