using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NEON.Trader.Desktop.Models;

namespace NEON.Trader.Desktop.Services;

/// <summary>
/// Persists backend profiles + active selection to a JSON file in LocalApplicationData.
/// LocalSettings API isn't available for unpackaged WinUI apps so we use a plain file.
/// </summary>
public sealed class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NEON.Trader");
    private static readonly string File = Path.Combine(Dir, "settings.json");

    public List<BackendProfile> Profiles { get; private set; } = new();
    public string? ActiveProfileId { get; set; }

    public BackendProfile? ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId)
        ?? Profiles.FirstOrDefault();

    public event EventHandler? ActiveProfileChanged;

    private sealed class PersistShape
    {
        public List<BackendProfile> Profiles { get; set; } = new();
        public string? ActiveProfileId { get; set; }
    }

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (System.IO.File.Exists(File))
            {
                var json = System.IO.File.ReadAllText(File);
                var data = JsonSerializer.Deserialize<PersistShape>(json);
                if (data is not null && data.Profiles.Count > 0)
                {
                    Profiles = data.Profiles;
                    ActiveProfileId = data.ActiveProfileId ?? Profiles[0].Id;
                    return;
                }
            }
        }
        catch { /* fall through to defaults */ }

        Profiles = BackendProfile.DefaultProfiles().ToList();
        ActiveProfileId = Profiles[0].Id;
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var data = new PersistShape
            {
                Profiles = Profiles,
                ActiveProfileId = ActiveProfileId,
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            System.IO.File.WriteAllText(File, json);
        }
        catch { /* best-effort persistence */ }
    }

    public void SetActive(string profileId)
    {
        if (Profiles.Any(p => p.Id == profileId))
        {
            ActiveProfileId = profileId;
            Save();
            ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UpsertProfile(BackendProfile profile)
    {
        var existing = Profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0) Profiles[existing] = profile;
        else               Profiles.Add(profile);
        Save();
    }
}
