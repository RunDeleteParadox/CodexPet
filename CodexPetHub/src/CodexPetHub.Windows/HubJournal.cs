// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using System.Text.Json;
using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal sealed record HubSession(string RunId, int Pid, string Version, DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt, bool CleanStop, string? StopReason, string Health);

internal sealed class HubJournal
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    public string? LastError { get; private set; }

    public HubJournal(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "session.json");
            if (File.Exists(path))
            {
                var previous = JsonSerializer.Deserialize<HubSession>(File.ReadAllText(path), ConfigStore.Json);
                if (previous is { CleanStop: false })
                    Write("previous-session-unclosed", "Previous session did not finish cleanly.", previous);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Write("session-read-failed", "Previous session marker unavailable.", error: ex); }
        Write("start", "Hub " + Version + " started.");
        Heartbeat("starting", null);
    }

    public void Write(string eventName, string? message = null, object? data = null, Exception? error = null)
    {
        lock (gate)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var line = JsonSerializer.Serialize(new { at = now, runId = RunId, pid = Environment.ProcessId,
                    version = Version, @event = eventName, message, data,
                    exception = error == null ? null : new { type = error.GetType().FullName,
                        hresult = $"0x{error.HResult:X8}", detail = Limit(error.ToString(), 16_000) } });
                Append("hub.jsonl", line);
                Append("hub.log", now.ToString("O") + " [" + eventName + "] " + message +
                    (error == null ? "" : " " + Limit(error.ToString(), 16_000)));
                LastError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LastError = ex.GetType().Name + ": " + ex.Message; }
        }
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private void Append(string name, string line)
    {
        var path = Path.Combine(directory, name);
        if (File.Exists(path) && (new FileInfo(path).Length >= 1_000_000 || File.GetLastWriteTimeUtc(path).Date != DateTime.UtcNow.Date))
        {
            for (var i = 13; i >= 1; i--)
                if (File.Exists(path + "." + i)) File.Move(path + "." + i, path + "." + (i + 1), true);
            File.Move(path, path + ".1", true);
        }
        File.AppendAllText(path, line + Environment.NewLine);
    }

    public void Heartbeat(string health, string? reason, bool cleanStop = false)
    {
        lock (gate)
        {
            try { AtomicWrite(Path.Combine(directory, "session.json"), new HubSession(RunId, Environment.ProcessId,
                Version, started, DateTimeOffset.UtcNow, cleanStop, reason, health)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LastError = ex.GetType().Name + ": " + ex.Message; }
        }
    }

    public static void AtomicWrite<T>(string path, T value)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, ConfigStore.Json));
        File.Move(temp, path, true);
    }
}
