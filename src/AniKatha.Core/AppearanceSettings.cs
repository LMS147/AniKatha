using System.Text.Json;

namespace AniKatha.Core;

public sealed record AccentPreset(string Name, string Dark, string Light);

public sealed record AppearanceSettings(string Mode = "system", string Accent = "Violet", string Density = "comfortable")
{
    public const string Key = "appearance-v1";
    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("Violet", "#b5a0ff", "#5636a0"), new("Ocean", "#83c8ff", "#125783"),
        new("Rose", "#ffa3c7", "#8c2855"), new("Emerald", "#8cdeb7", "#1b6348"),
        new("Amber", "#ffd28a", "#805314"), new("Graphite", "#f4f4fa", "#414652")
    ];
    public AccentPreset Palette => Presets.FirstOrDefault(item => item.Name == Accent) ?? Presets[0];
    public string Css => $"--accent-dark:{Palette.Dark};--accent-light:{Palette.Light}";

    public async Task SaveAsync(LocalStore store) => await store.SetSettingAsync(Key, JsonSerializer.Serialize(this));

    public static async Task<AppearanceSettings> LoadAsync(LocalStore store)
    {
        var json = await store.GetSettingAsync(Key);
        if (json is not null)
        {
            try
            {
                if (JsonSerializer.Deserialize<AppearanceSettings>(json) is { } settings)
                    return new(settings.Mode is "light" or "dark" ? settings.Mode : "system", settings.Palette.Name,
                        settings.Density is "compact" ? "compact" : "comfortable");
            }
            catch (JsonException) { }
        }
        var legacy = await store.GetSettingAsync("accent");
        return new(Accent: Presets.FirstOrDefault(item => item.Dark.Equals(legacy, StringComparison.OrdinalIgnoreCase))?.Name ?? "Violet");
    }
}

public interface IAppearancePlatform
{
    Task ApplyAsync(string mode);
}
