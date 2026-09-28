// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text.Json;

namespace CodexPetHub.Core;

public record IpcEvent(string Kind, BaseState? State, DateTimeOffset Timestamp, string ProducerId = "", long Sequence = 0, ReactionKind? Reaction = null);

public static class IpcProtocol
{
    public const int Version = 2;
    public const int MaxLineLength = 1024;
    public static string PipeName(string userSid) => "CodexPetHub.v2." + userSid;
    private static readonly Dictionary<string, BaseState> States = new(StringComparer.Ordinal)
    {
        ["idle"] = BaseState.Idle, ["thinking"] = BaseState.Thinking,
        ["working"] = BaseState.Working, ["waitingForUser"] = BaseState.WaitingForUser
    };

    public static IpcEvent Parse(string line)
    {
        if (line.Length > MaxLineLength) throw new InvalidDataException("IPC line too long.");
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("IPC object required.");
            var seen = new HashSet<string>();
            foreach (var item in root.EnumerateObject())
                if (!seen.Add(item.Name)) throw new InvalidDataException("Duplicate IPC field.");
            if (root.GetProperty("version").GetInt32() != Version) throw new InvalidDataException("Unsupported IPC version (expected 2).");
            var timestampText = root.GetProperty("timestamp").GetString();
            if (timestampText == null || !(timestampText.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(timestampText, @"[+-]\d{2}:\d{2}$")) ||
                !DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                throw new InvalidDataException("IPC timestamp requires an explicit time zone.");
            var kind = root.TryGetProperty("type", out var type) ? type.GetString() : "state";
            if (kind == "heartbeat") return new(kind, null, timestamp);
            if (kind != "state") throw new InvalidDataException("Unknown IPC event type.");
            if (!States.TryGetValue(root.GetProperty("baseState").GetString() ?? "", out var state))
                throw new InvalidDataException("Unknown Codex state.");
            var producer = root.GetProperty("producerId").GetString() ?? "";
            var sequence = root.GetProperty("sequence").GetInt64();
            if (!Guid.TryParseExact(producer, "N", out _) || sequence < 0) throw new InvalidDataException("Invalid event identity.");
            ReactionKind? reaction = null;
            if (root.TryGetProperty("reaction", out var r)) reaction = r.GetString() switch
            { "success" => ReactionKind.Success, "error" => ReactionKind.Error, "stop" => ReactionKind.Stop, _ => throw new InvalidDataException("Unknown reaction.") };
            return new("state", state, timestamp, producer, sequence, reaction);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Do not echo rejected payloads into logs.
            throw new InvalidDataException("Invalid IPC message.");
        }
    }
}
