// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexPetHub.Core;

public record DeviceInfo(int ProtocolVersion, string DeviceType, string DeviceId, string FirmwareVersion, bool SuccessAudio = false);
public record DeviceStatus(string State, string RequestedState, bool Sleeping, int Brightness, bool Diagnostic,
    string? BootId = null, long? UptimeMs = null, string? ResetReason = null, bool? PanelAsleep = null,
    string? LastPowerCommand = null, long? LastPowerCommandAtMs = null,
    int? BatteryMillivolts = null, int? UsbMillivolts = null, bool? UsbPresent = null, bool? Charging = null,
    long? FreeHeap = null, long? MinFreeHeap = null, string? SuccessSound = null,
    bool? AudioReady = null, bool? AudioPlaying = null, long? AudioPlayCount = null, long? AudioErrorCount = null);

public static class SerialProtocol
{
    public const int Version = 1;
    public static readonly IReadOnlySet<string> Expressions = new HashSet<string>(StringComparer.Ordinal)
    { "idle", "listening", "thinking", "happy", "excited", "curious", "confused", "angry", "surprised", "sad", "sleepy", "dizzy", "working", "waiting", "success", "error", "stop", "wake" };
    public static bool ValidDeviceId(string? id) => id != null && Regex.IsMatch(id, @"^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$") &&
        id is not "0000-0000-0000" and not "FFFF-FFFF-FFFF";

    public static JsonElement Parse(string line)
    {
        if (line.Length > 1024 || !line.StartsWith("CP ", StringComparison.Ordinal)) throw UiMessage.Invalid("ErrorEnvelope");
        try { using var doc = JsonDocument.Parse(line[3..]); return doc.RootElement.Clone(); }
        catch (JsonException) { throw UiMessage.Invalid("ErrorMalformedReply"); }
    }

    // Match only the expected response. Firmware errors have no command id:
    // describe what was awaited without claiming which command caused them.
    public static JsonElement? ReadReply(string line, string command, string responseType)
    {
        var verb = command.Split(' ')[0];
        try
        {
            var response = Parse(line);
            var type = response.GetProperty("type").GetString();
            if (string.IsNullOrEmpty(type)) throw UiMessage.Invalid("ErrorMissingReplyType");
            if (type == "error")
            {
                var code = response.TryGetProperty("code", out var field) && field.ValueKind == JsonValueKind.String
                    ? field.GetString() : null;
                // Only protocol-defined labels enter diagnostics, never arbitrary serial text.
                var label = code switch { "unknown_command" or "line_too_long" => code, null => "missing-code", _ => "unrecognized-code" };
                throw UiMessage.Invalid("ErrorFirmware", label);
            }
            if (type != responseType) return null;
            if (responseType == "ok" && (!response.TryGetProperty("command", out var ack) || ack.GetString() != verb)) return null;
            return response;
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            var detail = ex is InvalidDataException ? UiMessage.From(ex) : new UiMessage("ErrorReplyShape");
            throw UiMessage.Invalid("ErrorAwaiting", verb, responseType.ToUpperInvariant(), detail);
        }
    }

    public static DeviceInfo ReadInfo(JsonElement json)
    {
        try
        {
            if (json.GetProperty("type").GetString() != "info") throw UiMessage.Invalid("ErrorInfoRequired");
            var info = new DeviceInfo(json.GetProperty("protocolVersion").GetInt32(), json.GetProperty("deviceType").GetString() ?? "",
                json.GetProperty("deviceId").GetString() ?? "", json.GetProperty("firmwareVersion").GetString() ?? "",
                OptionalBool(json, "successAudio") ?? false);
            if (info.DeviceType != "CodexPet") throw UiMessage.Invalid("ErrorNotPet");
            if (info.ProtocolVersion != Version) throw UiMessage.Invalid("ErrorProtocol", info.ProtocolVersion, Version);
            if (!ValidDeviceId(info.DeviceId) || !Regex.IsMatch(info.FirmwareVersion, @"^\d+\.\d+\.\d+([+-][a-zA-Z0-9.-]+)?$"))
                throw UiMessage.Invalid("ErrorFirmwareIdentity");
            if (!System.Version.TryParse(info.FirmwareVersion.Split('+','-')[0], out var firmware) || firmware < new System.Version(1, 1, 0))
                throw UiMessage.Invalid("ErrorFirmwareVersion");
            return info;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw UiMessage.Invalid("ErrorIncompleteIdentity"); }
    }

    public static DeviceStatus ReadStatus(JsonElement json)
    {
        try
        {
            var state = json.GetProperty("state").GetString() ?? "";
            var requested = json.GetProperty("requestedState").GetString() ?? "";
            var brightness = json.GetProperty("brightness").GetInt32();
            if ((state != "sleep" && !Expressions.Contains(state)) || !Expressions.Contains(requested) || brightness is < 0 or > 100)
                throw UiMessage.Invalid("ErrorStatus");
            return new(state, requested, json.GetProperty("sleeping").GetBoolean(), brightness, json.GetProperty("diagnostic").GetBoolean(),
                OptionalText(json, "bootId", @"^[0-9A-F]{16}$"), OptionalNumber(json, "uptimeMs", long.MaxValue),
                OptionalText(json, "resetReason", @"^[a-z-]{1,40}$"), OptionalBool(json, "panelAsleep"),
                OptionalText(json, "lastPowerCommand", @"^(boot|sleep|wake)$"), OptionalNumber(json, "lastPowerCommandAtMs", long.MaxValue),
                (int?)OptionalNumber(json, "batteryMillivolts", 5500), (int?)OptionalNumber(json, "usbMillivolts", 20000),
                OptionalBool(json, "usbPresent"), OptionalBool(json, "charging"),
                OptionalNumber(json, "freeHeap", int.MaxValue), OptionalNumber(json, "minFreeHeap", int.MaxValue),
                OptionalText(json, "successSound", @"^(none|fanfare|voice)$"), OptionalBool(json, "audioReady"),
                OptionalBool(json, "audioPlaying"), OptionalNumber(json, "audioPlayCount", uint.MaxValue), OptionalNumber(json, "audioErrorCount", uint.MaxValue));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw UiMessage.Invalid("ErrorIncompleteStatus"); }
    }

    private static long? OptionalNumber(JsonElement json, string name, long max)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetInt64(out var number) || number < 0 || number > max)
            throw UiMessage.Invalid("ErrorNumericDiagnostic");
        return number;
    }
    private static bool? OptionalBool(JsonElement json, string name) =>
        !json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null : value.GetBoolean();
    private static string? OptionalText(JsonElement json, string name, string pattern)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var text = value.GetString();
        if (text == null || !Regex.IsMatch(text, pattern)) throw UiMessage.Invalid("ErrorDiagnosticLabel");
        return text;
    }
}
