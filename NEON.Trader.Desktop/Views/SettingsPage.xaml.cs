using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using NEON.Trader.Desktop.Models;
using NEON.Trader.Desktop.Services;

namespace NEON.Trader.Desktop.Views;

public sealed partial class SettingsPage : Page
{
    private readonly AppServices _services = App.Services;

    public SettingsPage()
    {
        InitializeComponent();
        RefreshList();
        ProfileList.SelectedItem = _services.Settings.ActiveProfile;
    }

    private void RefreshList()
    {
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = _services.Settings.Profiles.ToList();
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is BackendProfile p)
        {
            NameBox.Text = p.Name;
            UrlBox.Text  = p.BaseUrl;
            KeyBox.Password = p.ApiKey;
            foreach (ComboBoxItem item in KindBox.Items)
            {
                if ((string)item.Tag == p.Kind.ToString())
                {
                    KindBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void AddProfile_Click(object sender, object e)
    {
        var id = $"profile-{Guid.NewGuid():N}".Substring(0, 16);
        var p = new BackendProfile
        {
            Id = id,
            Name = "NEW PROFILE",
            Kind = BackendKind.NseMain,
            BaseUrl = "https://",
            ApiKey = "",
            Portfolio = "main",
        };
        _services.Settings.UpsertProfile(p);
        RefreshList();
        ProfileList.SelectedItem = p;
    }

    private void Save_Click(object sender, object e)
    {
        if (ProfileList.SelectedItem is not BackendProfile p) return;

        p.Name = NameBox.Text.Trim();
        p.BaseUrl = UrlBox.Text.Trim().TrimEnd('/');
        p.ApiKey = KeyBox.Password;
        if (KindBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<BackendKind>(tag, out var kind))
        {
            p.Kind = kind;
            p.Portfolio = kind switch
            {
                BackendKind.NseMain => "main",
                BackendKind.NseEval => "eval",
                _ => null,
            };
        }

        _services.Settings.UpsertProfile(p);
        _services.Settings.Save();
        StatusText.Text = "Saved.";
        RefreshList();
        ProfileList.SelectedItem = p;

        if (_services.Settings.ActiveProfileId == p.Id)
            _services.Settings.SetActive(p.Id); // fire event to refresh UI
    }

    private void SetActive_Click(object sender, object e)
    {
        if (ProfileList.SelectedItem is BackendProfile p)
        {
            _services.Settings.SetActive(p.Id);
            StatusText.Text = $"Active: {p.Name}";
            if (App.MainWindow is MainWindow main)
                main.Toast($"Active backend: {p.Name}");
        }
    }

    private void Delete_Click(object sender, object e)
    {
        if (ProfileList.SelectedItem is not BackendProfile p) return;
        _services.Settings.Profiles.Remove(p);
        _services.Settings.Save();
        if (_services.Settings.ActiveProfileId == p.Id)
            _services.Settings.ActiveProfileId = _services.Settings.Profiles.FirstOrDefault()?.Id;
        RefreshList();
        StatusText.Text = "Deleted.";
    }

    private async void Test_Click(object sender, object e)
    {
        if (ProfileList.SelectedItem is not BackendProfile p)
        {
            StatusText.Text = "Select a profile first.";
            return;
        }
        StatusText.Text = "Testing...";
        // Use a scoped active profile just for the test — restore after.
        var original = _services.Settings.ActiveProfileId;
        try
        {
            _services.Settings.ActiveProfileId = p.Id;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var s = await _services.Api.GetStatusAsync(cts.Token);
            StatusText.Text = $"OK — portfolio={s.Portfolio ?? "—"}, positions={s.Positions?.Count ?? 0}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed: " + ex.Message;
        }
        finally
        {
            _services.Settings.ActiveProfileId = original;
        }
    }
}
