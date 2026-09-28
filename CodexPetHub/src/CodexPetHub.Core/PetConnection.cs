// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace CodexPetHub.Core;

public interface IPetTransport : IDisposable
{
    bool IsConnected { get; }
    string? Endpoint { get; }
    DateTimeOffset? LastReplyAt => null;
    DeviceInfo Connect(HubConfig config);
    JsonElement Send(string command, string responseType);
    void Disconnect();
}

// Only this class produces presentation commands. Connect must complete INFO first.
public sealed class PetConnection(IPetTransport transport, Action<Exception>? cleanupError = null) : IDisposable
{
    public DeviceInfo? Device { get; private set; }
    public DeviceStatus? Displayed { get; private set; }
    public string? Endpoint => Device == null ? null : transport.Endpoint;
    public DateTimeOffset? LastReplyAt => transport.LastReplyAt;
    public DateTimeOffset? LastStatusAt { get; private set; }
    public bool Connected => Device != null && transport.IsConnected;
    private Presentation? applied;

    public void Connect(HubConfig config)
    {
        Disconnect();
        Device = transport.Connect(config);
        applied = null;
    }

    public bool Synchronize(Presentation desired)
    {
        if (!Connected) return false;
        var changed = applied != desired;
        if (!changed) return false;
        if (applied?.Brightness != desired.Brightness) transport.Send($"BRIGHTNESS {desired.Brightness}", "ok");
        // Capability-gated: older firmware keeps working without an unknown command.
        // Changing the sound never replays a reaction; None can silence it immediately.
        if (Device!.SuccessAudio && applied?.SuccessSound != desired.SuccessSound)
            transport.Send("SOUND " + desired.SuccessSound.ToString().ToLowerInvariant(), "ok");
        // Any relevant semantic revision cancels an older reaction, even when
        // the graphical base is unchanged. Establish the base before WAKE.
        if (applied?.Expression != desired.Expression || applied?.Revision != desired.Revision)
            transport.Send("STATE " + desired.Expression, "ok");
        if (applied?.Sleeping != desired.Sleeping) transport.Send(desired.Sleeping ? "SLEEP" : "WAKE", "ok");
        if (!desired.Sleeping && desired.Reaction != null &&
            (applied?.Revision != desired.Revision || applied?.Reaction != desired.Reaction))
            transport.Send("REACT " + desired.Reaction, "ok");
        applied = desired; // Only after every ACK, never after a partially applied transaction.
        RefreshStatus();
        return true;
    }

    public void RefreshStatus()
    {
        var next = SerialProtocol.ReadStatus(transport.Send("STATUS", "status"));
        if (Displayed?.BootId is { } boot && next.BootId != null && next.BootId != boot)
            applied = null; // A reset can keep the COM endpoint; reapply base/brightness/wake.
        Displayed = next;
        LastStatusAt = DateTimeOffset.UtcNow;
    }
    public void Disconnect()
    {
        // Clear stale connection claims even when driver cleanup itself fails.
        Device = null;
        Displayed = null;
        applied = null;
        LastStatusAt = null;
        try { transport.Disconnect(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { cleanupError?.Invoke(ex); }
    }
    public void Dispose()
    {
        Disconnect();
        try { transport.Dispose(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { cleanupError?.Invoke(ex); }
    }
}
