using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NEON.Trader.Desktop.Models;
using NEON.Trader.Desktop.Services;

namespace NEON.Trader.Desktop.Views;

public sealed partial class PortfolioPage : Page
{
    private readonly AppServices _services = App.Services;
    private List<string> _watchlist = new();

    public PortfolioPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var status = await _services.Api.GetStatusAsync(cts.Token);
            _watchlist = status.Watchlist ?? new();

            TotalValueText.Text = Money(status.Summary?.TotalValue);
            CashText.Text       = Money(status.Summary?.Cash);
            ReturnText.Text     = Pct(status.Summary?.TotalReturnPct);
            ReturnText.Foreground = (status.Summary?.TotalReturnPct ?? 0) >= 0
                ? (Brush)Application.Current.Resources["UpBrush"]
                : (Brush)Application.Current.Resources["DownBrush"];
            PosCountText.Text = (status.Positions?.Count ?? 0).ToString(CultureInfo.InvariantCulture);

            PositionsList.ItemsSource = status.Positions;
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private static string Money(double? v) =>
        v is null ? "—" : "₹" + v.Value.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));

    private static string Pct(double? v) =>
        v is null ? "—" : (v.Value >= 0 ? "+" : "") + v.Value.ToString("F2") + "%";

    private void Symbol_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var q = sender.Text.Trim().ToUpperInvariant();
            sender.ItemsSource = _watchlist
                .Where(s => s.ToUpperInvariant().Contains(q))
                .Take(12)
                .ToList();
        }
    }

    private async void Refresh_Click(object sender, object e) => await RefreshAsync();

    private async void Buy_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await PlaceOrderAsync("BUY", args.QueryText ?? BuySymbol.Text, BuyQty.Text, BuyPrice.Text);

    private async void Buy_Click(object sender, object e) =>
        await PlaceOrderAsync("BUY", BuySymbol.Text, BuyQty.Text, BuyPrice.Text);

    private async void Sell_Click(object sender, object e)
    {
        if (sender is not Button btn || btn.Tag is not string sym) return;
        // The Tag-matched quantity TextBox is the sibling to the left (column 6)
        string qtyText = "";
        if (btn.Parent is Grid g)
        {
            var qtyBox = g.Children.OfType<TextBox>()
                .FirstOrDefault(t => (t.Tag as string) == sym);
            qtyText = qtyBox?.Text ?? "";
        }
        await PlaceOrderAsync("SELL", sym, qtyText, "");
    }

    private async void SellAll_Click(object sender, object e)
    {
        if (sender is Button btn && btn.Tag is string sym)
            await PlaceOrderAsync("SELL", sym, "", "");
    }

    private async System.Threading.Tasks.Task PlaceOrderAsync(
        string side, string? symbol, string qtyText, string priceText)
    {
        var sym = (symbol ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sym))
        {
            (App.MainWindow as MainWindow)?.Toast("Symbol required", error: true);
            return;
        }
        int? qty = null;
        if (int.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) && q > 0)
            qty = q;
        double? price = null;
        if (double.TryParse(priceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 0)
            price = p;

        OrderState.Text = $"{side} {sym} …";
        BuyButton.IsEnabled = false;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var res = await _services.Api.PlaceOrderAsync(
                new OrderRequest { Symbol = sym, Side = side, Quantity = qty, Price = price },
                cts.Token);

            var note = $"{res.Action} {res.Quantity}×{res.Symbol} @ ₹{res.Price:F2}";
            OrderState.Text = note;
            (App.MainWindow as MainWindow)?.Toast(note);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            OrderState.Text = "failed";
            ErrorText.Text = ex.Message;
            (App.MainWindow as MainWindow)?.Toast(ex.Message, error: true);
        }
        finally
        {
            BuyButton.IsEnabled = true;
        }
    }
}
