// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodexPetHub.Core;

public sealed record PetIdentity(string DeviceId, string FriendlyName);

[JsonConverter(typeof(JsonStringEnumConverter<SuccessSound>))]
public enum SuccessSound { None, Fanfare, Voice }

public sealed record HubConfig
{
    public string Language { get; init; } = UiText.DefaultLanguage;
    public bool LaunchOnStartup { get; init; }
    public int Brightness { get; init; } = 60;
    public SuccessSound SuccessSound { get; init; } = SuccessSound.Voice;
    public bool SleepOnWindowsLock { get; init; } = true;
    public bool SleepOnDisplayOff { get; init; } = true;
    public string SerialPort { get; init; } = "auto";
    public int ReconnectInterval { get; init; } = 3;
    public PetIdentity? CurrentPet { get; init; }

    public void Validate()
    {
        if (!UiText.IsSupported(Language)) throw UiMessage.Invalid("ErrorLanguage");
        if (Brightness is < 0 or > 100) throw UiMessage.Invalid("ErrorBrightness");
        if (!Enum.IsDefined(SuccessSound)) throw UiMessage.Invalid("ErrorSound");
        if (ReconnectInterval is < 1 or > 60) throw UiMessage.Invalid("ErrorReconnect");
        if (SerialPort == null || !Regex.IsMatch(SerialPort, @"^(auto|COM[1-9][0-9]{0,3})$", RegexOptions.CultureInvariant))
            throw UiMessage.Invalid("ErrorPort");
        if (CurrentPet is { } pet && (!SerialProtocol.ValidDeviceId(pet.DeviceId) ||
            string.IsNullOrWhiteSpace(pet.FriendlyName) || pet.FriendlyName.Length > 64 || pet.FriendlyName.Any(char.IsControl)))
            throw UiMessage.Invalid("ErrorIdentity");
    }
}

public sealed class ConfigStore(string path)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public HubConfig Load()
    {
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<HubConfig>(File.ReadAllText(path), Json) ?? throw UiMessage.Invalid("ErrorEmptyConfig");
        value = value with { Language = UiText.Normalize(value.Language) };
        value.Validate();
        return value;
    }

    public void Save(HubConfig config)
    {
        config.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(config, Json));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
