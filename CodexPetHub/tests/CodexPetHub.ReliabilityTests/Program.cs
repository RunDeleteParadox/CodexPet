// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using CodexPetHub.Core;
using CodexPetHub.Windows;

if (args.Length == 4 && args[0] == "--device" && args[2] == "--device-id")
    return await DeviceChecks.Run(args[1], args[3]);

var passed = 0;
var failed = 0;
var root = Path.Combine(Path.GetTempPath(), "CodexPetReliability-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Await(Func<bool> condition, string message, int milliseconds = 8000)
{
    var end = Environment.TickCount64 + milliseconds;
    while (Environment.TickCount64 < end) { if (condition()) return; await Task.Delay(25); }
    throw new Exception("Timeout: " + message);
}
string Data(string name)
{
    var path = Path.Combine(root, name);
    Directory.CreateDirectory(path);
    new ConfigStore(Path.Combine(path, "config.json")).Save(new HubConfig { ReconnectInterval = 1, Language = "fr" });
    return path;
}
HubService Hub(string data, IPetTransport fake) => new(data, pipeOverride: "CodexPetReliability." + Guid.NewGuid().ToString("N"), transport: fake);
IpcEvent Event(long seq, BaseState state = BaseState.Working, ReactionKind? reaction = null) =>
    new("state", state, DateTimeOffset.UtcNow, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", seq, reaction);
JsonElement Read(string path) { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return doc.RootElement.Clone(); }
async Task Test(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}

await Test("I/O failure plus Dispose failure cannot kill processing or retain stale connection", async () =>
{
    var data = Data("double-failure");
    using var fake = new FaultTransport { FailNextSend = true, FailNextDisconnect = true };
    using var hub = Hub(data, fake);
    await Await(() => fake.Connects >= 2 && hub.Snapshot.Displayed != null, "reconnect after double failure");
    hub.Receive(Event(1, BaseState.WaitingForUser));
    await Await(() => hub.Snapshot.Displayed?.State == "waiting", "state after reconnect");
    Check(hub.Snapshot.Health.Loop == "running" && hub.Snapshot.LastError == null, "false fault/stale error after recovery");
    var log = File.ReadAllText(Path.Combine(data, "hub.jsonl"));
    Check(log.Contains("serial-failed") && log.Contains("serial-cleanup-failed") && log.Contains("0x800701B1") && log.Contains("FaultTransport.Disconnect"), "missing error provenance");
});

foreach (var scenario in new[] { "rejected-info", "malformed-info", "malformed-status" })
await Test(scenario + " cannot terminate processing and the latest base is restored", async () =>
{
    var data = Data(scenario);
    using var fake = new FaultTransport { ProtocolFailure = scenario };
    using var hub = Hub(data, fake);
    hub.Receive(Event(1, BaseState.WaitingForUser));
    await Await(() => hub.HasFault || (fake.Connects >= 2 && hub.Snapshot.Displayed?.State == "waiting"), "protocol error recovery");
    Check(!hub.HasFault && hub.Snapshot.Health.Loop == "running", "protocol response killed processing");
    Check(hub.Snapshot.LastError == null, "stale error after recovery");
    var log = File.ReadAllText(Path.Combine(data, "hub.jsonl"));
    Check(log.Contains("serial-failed") && !log.Contains("task-fault"), "protocol error not classified as recoverable");
});

await Test("connection clears state before a throwing cleanup", () =>
{
    using var fake = new FaultTransport();
    var errors = 0;
    using var pet = new PetConnection(fake, _ => errors++);
    pet.Connect(new()); pet.Synchronize(new("working", false, 50));
    fake.FailNextDisconnect = true;
    pet.Disconnect();
    Check(errors == 1 && pet.Device == null && pet.Displayed == null && pet.Endpoint == null && !pet.Connected, "stale connection after cleanup exception");
    pet.Connect(new()); Check(pet.Synchronize(new("waiting", false, 50)), "failed to reapply presentation");
    return Task.CompletedTask;
});

await Test("unexpected task exception is observed and persisted while the process remains alive", async () =>
{
    var data = Data("fatal");
    using var fake = new FaultTransport { FatalNextSend = true };
    using var hub = Hub(data, fake);
    await Await(() => hub.Snapshot.Health.Loop == "faulted", "fault observed");
    await Await(() => File.Exists(Path.Combine(data, "status.json")) && Read(Path.Combine(data, "status.json")).GetProperty("health").GetProperty("loop").GetString() == "faulted", "persistent fault status");
    Check(HubService.HealthLabel(hub.Snapshot).Contains("arrêté"), "UI hid processing fault");
    Check(File.ReadAllText(Path.Combine(data, "hub.jsonl")).Contains("task-fault"), "missing task fault log");
});

await Test("blocked serial is detected independently; inputs stay bounded and stale reactions are discarded", async () =>
{
    var data = Data("blocked");
    using var fake = new FaultTransport { BlockSend = true };
    using var hub = new HubService(data, pipeOverride: "CodexPetReliability." + Guid.NewGuid().ToString("N"), transport: fake) { StallTimeout = TimeSpan.FromMilliseconds(300) };
    try
    {
        await Await(() => fake.InSend.IsSet, "blocked serial operation");
        for (var n = 0; n < 2000; n++) { hub.Receive(Event(n)); hub.Receive(new("heartbeat", null, DateTimeOffset.UtcNow)); }
        hub.Receive(Event(2001, BaseState.WaitingForUser, ReactionKind.Success));
        await Await(() => hub.Snapshot.Health.Loop == "stalled", "watchdog stall");
        Check(hub.Snapshot.Health.PendingInputs == 2, "expected one latest base plus one pending completion, no heartbeat");
        Check(HubService.HealthLabel(hub.Snapshot).Contains("périmé"), "UI claims healthy connection");
        await Task.Delay(2100);
    }
    finally { fake.Release.Set(); }
    await Await(() => hub.Snapshot.Displayed?.State == "waiting" && hub.Snapshot.Health.Loop == "running", "latest base after stalled operation");
    Check(!fake.Commands.Any(x => x == "REACT success"), "replayed old reaction");
});

await Test("suspend and duplicate resume preserve latest base and perform one reconnect", async () =>
{
    var data = Data("resume");
    using var fake = new FaultTransport(); using var hub = Hub(data, fake);
    await Await(() => hub.Snapshot.Displayed != null, "initial state");
    hub.WindowsLock(true); hub.Suspend(true);
    await Await(() => hub.Snapshot.Suspended && hub.Snapshot.Displayed?.Sleeping == true, "suspended state");
    Check(hub.Snapshot.Health.Loop == "suspended", "sleep misclassified as a stall");
    hub.Receive(Event(1, BaseState.WaitingForUser));
    var count = fake.Connects;
    hub.Suspend(false); hub.Suspend(false); hub.WindowsLock(false);
    await Await(() => hub.Snapshot.Displayed is { Sleeping: false, State: "waiting" }, "resume current state");
    Check(fake.Connects == count + 1, "duplicate resume reopened serial twice");
});

await Test("a locked status file does not stop presentation and publication recovers", async () =>
{
    var data = Data("locked-file");
    using var fake = new FaultTransport(); using var hub = Hub(data, fake);
    var path = Path.Combine(data, "status.json");
    await Await(() => File.Exists(path) && hub.Snapshot.Displayed != null, "status published");
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        hub.Receive(Event(1, BaseState.Thinking));
        await Await(() => hub.Snapshot.Health.DiagnosticsError != null && hub.Snapshot.Displayed?.State == "thinking", "presentation while status locked");
        Check(hub.Snapshot.Health.Loop == "running", "diagnostic file killed loop");
    }
    await Await(() => hub.Snapshot.Health.DiagnosticsError == null && Read(path).GetProperty("codexState").GetInt32() == 1, "status publication recovers");
});

await Test("session records distinguish forced/unclosed, user exit and Windows exit", async () =>
{
    var data = Data("session");
    _ = new HubJournal(data); // Models an interrupted instance; no clean-stop marker.
    var current = new HubJournal(data);
    Check(File.ReadAllText(Path.Combine(data, "hub.jsonl")).Contains("previous-session-unclosed"), "lost unclean previous session");
    using (var fake = new FaultTransport())
    using (var hub = Hub(data, fake)) { await Await(() => hub.Snapshot.Displayed != null, "running"); hub.RequestStop("user-quit"); }
    var end = Read(Path.Combine(data, "session.json"));
    Check(end.GetProperty("cleanStop").GetBoolean() && end.GetProperty("stopReason").GetString() == "user-quit", "user exit missing");
    using (var fake = new FaultTransport())
    using (var hub = Hub(data, fake)) { await Await(() => hub.Snapshot.Displayed != null, "running"); hub.RequestStop("windows-session-end"); }
    end = Read(Path.Combine(data, "session.json"));
    Check(end.GetProperty("cleanStop").GetBoolean() && end.GetProperty("stopReason").GetString() == "windows-session-end", "Windows exit missing");
});

await Test("log rotation is bounded and exported diagnostics exclude dumps", async () =>
{
    var data = Data("export");
    var journal = new HubJournal(data);
    for (var i = 0; i < 17; i++) { File.WriteAllText(Path.Combine(data, "hub.jsonl"), new string('x', 1_000_001)); journal.Write("test-rotation"); }
    Check(Directory.GetFiles(data, "hub.jsonl*").Length <= 15, "rotation unbounded");
    File.WriteAllText(Path.Combine(data, "private.dmp"), "DO_NOT_EXPORT");
    using var fake = new FaultTransport(); using var hub = Hub(data, fake);
    await Await(() => hub.Snapshot.Displayed != null, "running");
    var zipPath = Path.Combine(root, "diagnostics.zip"); hub.ExportDiagnostics(zipPath);
    using var zip = ZipFile.OpenRead(zipPath);
    Check(zip.Entries.Any(x => x.FullName == "status.json") && !zip.Entries.Any(x => x.FullName.EndsWith(".dmp")), "export allowlist failed");
});

await Test("optional firmware diagnostics are validated and legacy STATUS stays compatible", () =>
{
    const string basic = """{"type":"status","state":"idle","requestedState":"idle","sleeping":false,"brightness":50,"diagnostic":false""";
    var old = SerialProtocol.ReadStatus(SerialProtocol.Parse("CP " + basic + "}"));
    Check(old.BootId == null && old.Charging == null, "unknown diagnostics became values");
    var rich = SerialProtocol.ReadStatus(SerialProtocol.Parse("CP " + basic + ""","bootId":"ABCD1234ABCD1234","uptimeMs":4294967396,"resetReason":"brownout","panelAsleep":false,"lastPowerCommand":"wake","batteryMillivolts":4200,"usbMillivolts":5000,"usbPresent":true,"charging":null}"""));
    Check(rich.UptimeMs > uint.MaxValue && rich.BatteryMillivolts == 4200 && rich.UsbPresent == true && rich.Charging == null, "diagnostic decode wrong");
    foreach (var fragment in new[] { ",\"batteryMillivolts\":-1}", ",\"bootId\":\"arbitrary text\"}", ",\"usbPresent\":\"yes\"}" })
    {
        try { SerialProtocol.ReadStatus(SerialProtocol.Parse("CP " + basic + fragment)); throw new Exception("invalid diagnostic accepted"); }
        catch (InvalidDataException) { }
    }
    return Task.CompletedTask;
});

await Test("firmware restart on an unchanged COM endpoint reapplies the base and wake", () =>
{
    using var fake = new FaultTransport { BootId = "AAAAAAAAAAAAAAAA" };
    using var pet = new PetConnection(fake);
    pet.Connect(new()); pet.Synchronize(new("working", false, 50));
    fake.BootId = "BBBBBBBBBBBBBBBB";
    pet.RefreshStatus(); fake.Commands.Clear();
    Check(pet.Synchronize(new("working", false, 50)), "reset did not invalidate applied state");
    Check(fake.Commands.Contains("STATE working") && fake.Commands.Contains("WAKE") && fake.Commands.Contains("BRIGHTNESS 50"), "incomplete restore after reset");
    return Task.CompletedTask;
});

Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
Console.WriteLine("Evidence: " + root);
return failed == 0 ? 0 : 1;

sealed class FaultTransport : IPetTransport
{
    public bool IsConnected { get; private set; }
    public string? Endpoint => IsConnected ? "COM99" : null;
    public DateTimeOffset? LastReplyAt { get; private set; }
    public int Connects;
    public string? BootId;
    public string? ProtocolFailure;
    public bool FailNextSend, FatalNextSend, FailNextDisconnect, BlockSend;
    public readonly ManualResetEventSlim InSend = new(), Release = new();
    public readonly ConcurrentQueue<string> Commands = new();
    private string state = "idle";
    private bool sleeping;
    private int brightness = 50;
    public DeviceInfo Connect(HubConfig config)
    {
        Interlocked.Increment(ref Connects); IsConnected = true;
        if (ProtocolFailure == "rejected-info")
        {
            ProtocolFailure = null;
            SerialProtocol.ReadReply("CP {\"type\":\"error\",\"code\":\"unknown_command\"}", "INFO", "info");
            throw new Exception("Error reply was accepted");
        }
        if (ProtocolFailure == "malformed-info")
        {
            ProtocolFailure = null;
            return SerialProtocol.ReadInfo(SerialProtocol.Parse("CP {\"type\":\"info\"}"));
        }
        return new(1, "CodexPet", "1234-5678-9ABC", "1.1.4");
    }
    public JsonElement Send(string command, string responseType)
    {
        Commands.Enqueue(command);
        if (command == "STATUS" && ProtocolFailure == "malformed-status")
        {
            ProtocolFailure = null;
            return SerialProtocol.Parse("CP {\"type\":\"status\",\"state\":\"not-a-state\"}");
        }
        if (BlockSend) { InSend.Set(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test serial wait expired"); }
        if (FatalNextSend) { FatalNextSend = false; throw new NotSupportedException("Injected unexpected service exception"); }
        if (FailNextSend) { FailNextSend = false; throw new IOException("Injected unplug", unchecked((int)0x800701B1)); }
        if (command.StartsWith("STATE ")) state = command[6..];
        if (command.StartsWith("BRIGHTNESS ")) brightness = int.Parse(command[11..]);
        if (command == "SLEEP") sleeping = true;
        if (command == "WAKE") sleeping = false;
        LastReplyAt = DateTimeOffset.UtcNow;
        return command == "STATUS"
            ? JsonSerializer.SerializeToElement(new { type = "status", state = sleeping ? "sleep" : state, requestedState = state, sleeping, brightness, diagnostic = false, bootId = BootId })
            : JsonSerializer.SerializeToElement(new { type = "ok", command = command.Split(' ')[0] });
    }
    public void Disconnect()
    {
        var wasConnected = IsConnected; IsConnected = false;
        if (wasConnected && FailNextDisconnect) { FailNextDisconnect = false; throw new IOException("Injected cleanup failure", unchecked((int)0x800701B1)); }
    }
    public void Dispose() => Disconnect();
}
