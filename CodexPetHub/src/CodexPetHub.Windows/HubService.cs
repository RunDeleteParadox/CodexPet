// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Principal;
using System.Text.Json;
using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal record HubHealth(string Loop, string Pipe, DateTimeOffset LastLoopAt, double LoopAgeSeconds,
    int PendingInputs, string? Fault, string? DiagnosticsError);
internal record HubSnapshot(HubConfig Config, bool PluginConnected, BaseState CodexState,
    DateTimeOffset? LastEventAt, Presentation Requested, DeviceInfo? Device, DeviceStatus? Displayed,
    string? Endpoint, string? LastError, bool Locked, bool DisplayOff, bool Suspended,
    DateTimeOffset UpdatedAt, DateTimeOffset? LastSerialReplyAt, DateTimeOffset? LastStatusAt,
    HubHealth Health, string RunId, int Pid)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public UiMessage? ErrorMessage { get; init; }
}

internal sealed class HubService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly LatestActions actions = new();
    private readonly PresentationState state = new();
    private readonly PetConnection pet;
    private readonly ConfigStore store;
    private readonly HubJournal journal;
    private readonly bool serialDisabled;
    private readonly Task run;
    private readonly Task pipe;
    private readonly System.Threading.Timer monitor;
    private HubConfig config;
    private HubSnapshot snapshot;
    private string? lastError, lastLoggedError, fatal, stopReason, statusError;
    private UiMessage? errorMessage;
    private string lastHealth = "starting";
    private DateTimeOffset nextAttempt, nextSessionWrite;
    private long progress = Stopwatch.GetTimestamp(), grace = Stopwatch.GetTimestamp();
    private long progressUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private int suspended, monitorBusy, disposed;
    private string? lastBootId;
    private bool? lastUsbPresent;
    private long connectCount, failureCount, coalescedCount;
    private DateTimeOffset nextSummary;
    internal TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public string DataDirectory { get; }
    public string PipeName { get; }
    public HubSnapshot Snapshot => Volatile.Read(ref snapshot) with { Health = GetHealth() };
    public bool HasFault => Volatile.Read(ref fatal) != null;

    public HubService(string dataDirectory, bool noSerial = false, string? pipeOverride = null,
        bool statusFile = true, IPetTransport? transport = null, HubJournal? journal = null)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(dataDirectory);
        this.journal = journal ?? new(dataDirectory);
        serialDisabled = noSerial;
        store = new(Path.Combine(dataDirectory, "config.json"));
        config = store.Load();
        // statusFile remains accepted for compatibility; health is always persisted.
        pet = new(transport ?? (noSerial ? new AbsentTransport() : new SerialPetTransport(Log, CleanupError)), CleanupError);
        PipeName = pipeOverride ?? IpcProtocol.PipeName(WindowsIdentity.GetCurrent().User!.Value);
        snapshot = MakeSnapshot();
        var server = new PipeServer(PipeName, Receive,
            () => Post(HubInput.Plugin, state.DisconnectPlugin), Log);
        pipe = Task.Run(() => server.RunAsync(stop.Token));
        run = Task.Run(RunAsync);
        Observe(run, "processing");
        Observe(pipe, "ipc");
        monitor = new(_ => Monitor(), null, 0, 500);
    }

    private void Observe(Task task, string name) => _ = task.ContinueWith(completed =>
    {
        var error = completed.Exception?.Flatten();
        if (error != null || !stop.IsCancellationRequested)
        {
            Interlocked.CompareExchange(ref fatal, name + " stopped unexpectedly.", null);
            journal.Write("task-fault", name + " stopped unexpectedly.", new { task = name, status = completed.Status.ToString() }, error);
        }
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    internal void Receive(IpcEvent message)
    {
        if (message.State == null) return;
        var receivedAt = Stopwatch.GetTimestamp();
        // Keep one pending completion separately: a following base update must
        // not erase Success before the presentation loop can start it.
        Post(message.Reaction == ReactionKind.Success ? HubInput.PluginSuccess : HubInput.Plugin, () =>
        {
            // A disconnected or stalled Hub restores only the current base.
            var current = (!serialDisabled && !pet.Connected) || Stopwatch.GetElapsedTime(receivedAt) > TimeSpan.FromSeconds(2)
                ? message with { Reaction = null } : message;
            state.Receive(current, DateTimeOffset.UtcNow);
            journal.Write("codex-state", "Codex state: " + current.State, new { state = current.State.ToString(), reaction = current.Reaction?.ToString() });
        });
    }
    private void Post(HubInput key, Action action)
    {
        if (stop.IsCancellationRequested) return;
        if (actions.Post(key, action)) Interlocked.Increment(ref coalescedCount);
    }
    public void Test(BaseState value) => TestExpression(PresentationState.Map(value));
    public void TestExpression(string expression) => Post(HubInput.Test, () => state.Test(expression, DateTimeOffset.UtcNow));
    public void TestReaction(ReactionKind value) => Post(HubInput.Test, () => state.TestReaction(value, DateTimeOffset.UtcNow));
    public void Recipe(bool enabled) => Post(HubInput.Recipe, () => { if (enabled) state.ManualRecipe = true; else state.EndRecipe(); });
    public void Sleep(bool sleep)
    {
        journal.Write("power-request", "Manual " + (sleep ? "sleep" : "wake") + " requested.", new { source = "manual", sleep });
        Post(HubInput.ManualSleep, () => state.ManualSleep = sleep);
    }
    public void WindowsLock(bool locked)
    {
        journal.Write("windows-lock", locked ? "Windows locked." : "Windows unlocked.", new { locked });
        Post(HubInput.Lock, () => state.Locked = locked);
    }
    public void Display(bool off)
    {
        journal.Write("windows-display", off ? "Session display off." : "Session display on/dimmed.", new { off });
        Post(HubInput.Display, () => state.DisplayOff = off);
    }
    public void Suspend(bool value)
    {
        var previous = Interlocked.Exchange(ref suspended, value ? 1 : 0);
        Interlocked.Exchange(ref grace, Stopwatch.GetTimestamp());
        journal.Write(value ? "suspend" : "resume", value ? "Windows suspend." : "Windows resume.");
        // Both resume broadcasts must not cause two serial reconnects.
        if (value || previous == 1)
            Post(HubInput.Suspend, () => { state.Suspended = value; if (!value) ReconnectNow(); });
    }
    public void Reconnect()
    {
        journal.Write("reconnect-request", "Reconnect requested.");
        Post(HubInput.Reconnect, ReconnectNow);
    }
    private void ReconnectNow() { Disconnect(); nextAttempt = default; }
    private void CleanupError(Exception ex) => journal.Write("serial-cleanup-failed", "Serial cleanup failed; connection state cleared.", error: ex);
    private void Disconnect()
    {
        var endpoint = pet.Endpoint;
        pet.Disconnect();
        if (endpoint != null) journal.Write("disconnect", "Pet disconnected.", new { endpoint });
    }

    public void Save(HubConfig value)
    {
        value.Validate();
        Post(HubInput.Settings, () =>
        {
            if (config.LaunchOnStartup != value.LaunchOnStartup) StartupRegistration.Apply(value.LaunchOnStartup);
            store.Save(value);
            var reconnect = config.SerialPort != value.SerialPort || config.CurrentPet?.DeviceId != value.CurrentPet?.DeviceId;
            config = value;
            if (reconnect) ReconnectNow();
            journal.Write("settings-saved", "Settings saved.");
        });
    }

    private async Task RunAsync()
    {
        var nextStatus = DateTimeOffset.MinValue;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    foreach (var action in actions.Take()) action();
                    var now = DateTimeOffset.UtcNow;
                    if (!pet.Connected && now >= nextAttempt && Volatile.Read(ref suspended) == 0)
                    {
                        nextAttempt = now.AddSeconds(config.ReconnectInterval);
                        if (!serialDisabled) state.CancelReaction();
                        pet.Connect(config);
                        Interlocked.Increment(ref connectCount);
                        if (config.CurrentPet == null)
                        {
                            config = config with { CurrentPet = new(pet.Device!.DeviceId, "CodexPet") };
                            store.Save(config);
                        }
                        journal.Write("connect", "Pet connected on " + pet.Endpoint + "; firmware " + pet.Device!.FirmwareVersion + ".",
                            new { endpoint = pet.Endpoint, firmware = pet.Device.FirmwareVersion });
                        lastError = null;
                        errorMessage = null;
                        lastLoggedError = null;
                    }
                    if (pet.Connected)
                    {
                        var desired = state.Resolve(config, now);
                        if (pet.Synchronize(desired)) journal.Write("presentation-ack", "Presentation: " + desired.Expression + (desired.Sleeping ? " (sleep)" : " (awake)"),
                            new { desired.Expression, desired.Sleeping, desired.Brightness, desired.Reaction,
                                causes = new { state.ManualSleep, state.Locked, state.DisplayOff, state.Suspended } });
                        if (now >= nextStatus) { pet.RefreshStatus(); nextStatus = now.AddSeconds(2); }
                    }
                    RecordDeviceHealth(now);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or System.Management.ManagementException)
                {
                    Interlocked.Increment(ref failureCount);
                    lastError = ex.Message;
                    errorMessage = UiMessage.From(ex);
                    if (lastError != lastLoggedError)
                    {
                        journal.Write("serial-failed", "Serial operation failed; retry scheduled.", new { endpoint = pet.Endpoint, retrySeconds = config.ReconnectInterval }, ex);
                        lastLoggedError = lastError;
                    }
                    Disconnect();
                    nextAttempt = DateTimeOffset.UtcNow.AddSeconds(config.ReconnectInterval);
                }
                Volatile.Write(ref snapshot, MakeSnapshot());
                Interlocked.Exchange(ref progressUtc, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Interlocked.Exchange(ref progress, Stopwatch.GetTimestamp());
                await Task.Delay(100, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { pet.Dispose(); }
    }

    private void RecordDeviceHealth(DateTimeOffset now)
    {
        var device = pet.Displayed;
        if (device?.BootId is { } boot && boot != lastBootId)
        {
            if (lastBootId != null) state.CancelReaction();
            journal.Write(lastBootId == null ? "device-boot-observed" : "device-restarted", "Device boot observed.",
                new { previousBootId = lastBootId, bootId = boot, device.ResetReason, device.UptimeMs });
            lastBootId = boot;
        }
        if (device?.UsbPresent is { } usb && usb != lastUsbPresent)
        {
            journal.Write("device-usb-power", "Device reports USB power change.", new { present = usb, device.UsbMillivolts, device.BatteryMillivolts });
            lastUsbPresent = usb;
        }
        if (now < nextSummary) return;
        nextSummary = now.AddMinutes(1);
        journal.Write("health-summary", "Periodic health summary.", new { connections = connectCount,
            serialFailures = failureCount, coalescedInputs = coalescedCount, status = device, lastStatusAt = pet.LastStatusAt });
    }

    private HubHealth GetHealth()
    {
        var age = Stopwatch.GetElapsedTime(Interlocked.Read(ref progress)).TotalSeconds;
        var activeAge = Stopwatch.GetElapsedTime(Math.Max(Interlocked.Read(ref progress), Interlocked.Read(ref grace)));
        var loop = HasFault || run?.IsFaulted == true ? "faulted" : stop.IsCancellationRequested ? "stopping" :
            Volatile.Read(ref suspended) != 0 ? "suspended" : activeAge > StallTimeout ? "stalled" : "running";
        return new(loop, pipe?.IsCompleted == true ? "stopped" : "running",
            DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref progressUtc)), age,
            actions.Count, Volatile.Read(ref fatal), Volatile.Read(ref statusError) ?? journal.LastError);
    }
    internal static string HealthLabel(HubSnapshot s, string? language = null)
    {
        var text = new UiText(language ?? s.Config.Language);
        return s.Health.Loop switch
        {
            "faulted" => text["HealthFaulted"],
            "stalled" => text["HealthStalled"],
            "stopping" => text["HealthStopping"],
            "stopped" => text["HealthStopped"],
            "suspended" => text["HealthSuspended"],
            _ => s.Device == null ? text["Disconnected"] : text.Format("ConnectedPort", s.Endpoint)
        };
    }
    private HubSnapshot MakeSnapshot() => new(config, state.PluginConnected, state.Codex, state.LastEventAt,
        state.Resolve(config, DateTimeOffset.UtcNow), pet.Device, pet.Displayed, pet.Endpoint, lastError,
        state.Locked, state.DisplayOff, state.Suspended, DateTimeOffset.UtcNow, pet.LastReplyAt, pet.LastStatusAt,
        GetHealth(), journal.RunId, Environment.ProcessId) { ErrorMessage = errorMessage };

    // This timer never calls serial: it can report a stalled/faulted processing loop.
    private void Monitor()
    {
        if (Interlocked.Exchange(ref monitorBusy, 1) != 0) return;
        try
        {
            var current = Snapshot;
            if (current.Health.Loop != lastHealth)
            {
                journal.Write("health-change", "Processing health: " + current.Health.Loop, current.Health);
                lastHealth = current.Health.Loop;
            }
            try
            {
                HubJournal.AtomicWrite(Path.Combine(DataDirectory, "status.json"), current with { UpdatedAt = DateTimeOffset.UtcNow });
                if (statusError != null) journal.Write("status-recovered", "Diagnostic status file available again.");
                statusError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (statusError == null) journal.Write("status-write-failed", "Diagnostic status file busy; will retry without interrupting presentation.", error: ex);
                statusError = ex.GetType().Name + ": " + ex.Message;
            }
            if (DateTimeOffset.UtcNow >= nextSessionWrite)
            {
                journal.Heartbeat(current.Health.Loop, stopReason);
                nextSessionWrite = DateTimeOffset.UtcNow.AddSeconds(5);
            }
        }
        catch (Exception ex)
        {
            statusError = "Health monitor failed: " + ex.GetType().Name;
            journal.Write("monitor-fault", "Health monitor failed.", error: ex);
        }
        finally { Volatile.Write(ref monitorBusy, 0); }
    }
    public void Log(string message) => journal.Write("transport", message);
    public void RequestStop(string reason)
    {
        if (Interlocked.CompareExchange(ref stopReason, reason, null) != null) return;
        journal.Write("stop-requested", "Hub stop requested.", new { reason });
        journal.Heartbeat("stopping", reason);
        stop.Cancel();
    }
    public void ExportDiagnostics(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("status.json");
        using (var writer = new StreamWriter(entry.Open())) writer.Write(JsonSerializer.Serialize(Snapshot, ConfigStore.Json));
        // Explicit allowlist: no memory dumps or plugin payloads.
        foreach (var file in Directory.EnumerateFiles(DataDirectory).Where(p =>
            Path.GetFileName(p) is "config.json" or "session.json" || Path.GetFileName(p).StartsWith("hub.jsonl", StringComparison.Ordinal)))
        {
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = zip.CreateEntry(Path.GetFileName(file)).Open();
            source.CopyTo(target);
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        RequestStop("application-exit");
        var ended = Task.WhenAll(run, pipe);
        bool completed;
        try { completed = ended.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException ex) { completed = true; journal.Write("shutdown-fault", "Shutdown observed a failed task.", error: ex); }
        using var drained = new ManualResetEvent(false);
        monitor.Dispose(drained);
        var monitorEnded = drained.WaitOne(TimeSpan.FromSeconds(2));
        var clean = completed && ended.IsCompletedSuccessfully && monitorEnded && !HasFault;
        if (monitorEnded)
        {
            var final = Snapshot;
            try { HubJournal.AtomicWrite(Path.Combine(DataDirectory, "status.json"), final with {
                UpdatedAt = DateTimeOffset.UtcNow, Device = null, Displayed = null, Endpoint = null,
                Health = final.Health with { Loop = clean ? "stopped" : "faulted", Pipe = "stopped" } }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { journal.Write("status-write-failed", "Final status unavailable.", error: ex); }
        }
        journal.Write(clean ? "clean-stop" : "unclean-stop", clean ? "Hub stopped." : "Hub shutdown incomplete or faulted.", new { reason = stopReason });
        journal.Heartbeat(clean ? "stopped" : "faulted", stopReason, clean);
        if (completed && monitorEnded) stop.Dispose();
    }

    private sealed class AbsentTransport : IPetTransport
    {
        public bool IsConnected => false;
        public string? Endpoint => null;
        public DeviceInfo Connect(HubConfig config) => throw UiMessage.Io("ErrorSerialDisabled");
        public JsonElement Send(string command, string responseType) => throw UiMessage.Io("ErrorNoDevice");
        public void Disconnect() { }
        public void Dispose() { }
    }
}
