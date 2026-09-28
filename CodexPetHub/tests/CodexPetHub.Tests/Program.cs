// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CodexPetHub.Core;

var passed = 0;
var failed = 0;
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception("Invalid input was accepted.");
}
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; }
}
var now = DateTimeOffset.Parse("2026-09-25T10:00:00Z");
var config = new HubConfig();
Test("both catalogs have complete keys and matching format placeholders", () => {
    var en = UiText.Resources.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("en"), true, true)!;
    var fr = UiText.Resources.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("fr"), true, false)!;
    var keys = en.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).Order().ToArray();
    Check(keys.SequenceEqual(fr.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).Order()));
    foreach (var key in keys)
    {
        var english = en.GetString(key)!; var french = fr.GetString(key)!;
        Check(!string.IsNullOrWhiteSpace(english) && !string.IsNullOrWhiteSpace(french), key);
        Check(System.Text.CompositeFormat.Parse(english).MinimumArgumentCount == System.Text.CompositeFormat.Parse(french).MinimumArgumentCount, key);
        var placeholderPattern = @"\{[0-9]+(?:[^}]*)\}";
        Check(System.Text.RegularExpressions.Regex.Matches(english, placeholderPattern).Select(m => m.Value).Order()
            .SequenceEqual(System.Text.RegularExpressions.Regex.Matches(french, placeholderPattern).Select(m => m.Value).Order()), key);
    }
    foreach (var language in UiText.Languages)
    {
        var text = new UiText(language);
        foreach (var state in Enum.GetValues<BaseState>()) Check(!string.IsNullOrWhiteSpace(text.State(state)));
        foreach (var reaction in Enum.GetValues<ReactionKind>()) Check(!string.IsNullOrWhiteSpace(text.Reaction(reaction)));
        foreach (var expression in SerialProtocol.Expressions) Check(text.Expression(expression) != expression);
    }
});
Test("language defaults, migration and round trips preserve existing preferences", () => {
    Check(UiText.FromSystemCulture(System.Globalization.CultureInfo.GetCultureInfo("fr-CA")) == "fr");
    Check(UiText.FromSystemCulture(System.Globalization.CultureInfo.GetCultureInfo("de-DE")) == "en");
    var directory = Path.Combine(Path.GetTempPath(), "CodexPetLanguage-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "config.json");
        var store = new ConfigStore(path);
        File.WriteAllText(path, """{"brightness":42,"serialPort":"COM7","successSound":"Fanfare"}""");
        var previous = store.Load();
        Check(previous.Language == UiText.DefaultLanguage && previous.Brightness == 42 && previous.SerialPort == "COM7" && previous.SuccessSound == SuccessSound.Fanfare);
        foreach (var language in UiText.Languages)
        {
            var next = previous with { Language = language, CurrentPet = new("1234-5678-9ABC", "Béatrice") };
            store.Save(next); Check(store.Load() == next);
            Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("language").GetString() == language);
        }
        foreach (var invalid in new[] { "\"de\"", "null", "\"\"" })
        {
            File.WriteAllText(path, "{\"language\":" + invalid + ",\"brightness\":42}");
            Check(store.Load().Language == "en" && store.Load().Brightness == 42);
        }
        Reject(() => store.Save(previous with { Language = "de" }));
        Check(Directory.GetFiles(directory).Length == 1);
    }
    finally { Directory.Delete(directory, true); }
});
Test("errors translate after capture without changing technical logs or protocol", () => {
    var en = new UiText("en"); var fr = new UiText("fr");
    try { (config with { SerialPort = "USB" }).Validate(); throw new Exception("Invalid settings accepted"); }
    catch (InvalidDataException ex)
    {
        Check(ex.Message == en["ErrorPort"]);
        Check(UiMessage.From(ex).Render(fr) == fr["ErrorPort"]);
    }
    try { SerialProtocol.ReadReply("CP {\"type\":\"error\",\"code\":\"unknown_command\"}", "INFO", "info"); throw new Exception("Error accepted"); }
    catch (InvalidDataException ex)
    {
        Check(ex.Message.Contains("While awaiting INFO INFO"));
        Check(UiMessage.From(ex).Render(fr).Contains("En attente de INFO INFO"));
        Check(UiMessage.From(ex).Render(fr).Contains("unknown_command"));
    }
    Check(fr.Format("LastProgressLine", "date", 1.5).Contains("1,5"));
    Check(en.Format("LastProgressLine", "date", 1.5).Contains("1.5"));
});
Test("serial error replies preserve command context and bounded firmware code", () => {
    foreach (var code in new[] { "unknown_command", "line_too_long" })
    {
        try { SerialProtocol.ReadReply("CP {\"type\":\"error\",\"code\":\"" + code + "\"}", "INFO", "info"); throw new Exception("error accepted"); }
        catch (InvalidDataException ex) { Check(ex.Message.Contains("INFO") && ex.Message.Contains(code)); }
    }
    try { SerialProtocol.ReadReply("CP {\"type\":\"error\",\"code\":\"PRIVATE_SENTINEL\"}", "STATE working", "ok"); throw new Exception("error accepted"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("STATE") && !ex.ToString().Contains("PRIVATE_SENTINEL")); }
});
Test("malformed serial replies are recoverable protocol errors", () => {
    foreach (var line in new[] { "CP {", "CP []", "CP null", "CP {}", "CP {\"type\":123}", "CP {\"type\":null}", "CP {\"type\":\"ok\",\"command\":false}" })
        Reject(() => SerialProtocol.ReadReply(line, "WAKE", "ok"));
});
Test("serial matching skips unrelated replies and preserves expected ACK", () => {
    Check(SerialProtocol.ReadReply("CP {\"type\":\"status\"}", "WAKE", "ok") == null);
    Check(SerialProtocol.ReadReply("CP {\"type\":\"ok\",\"command\":\"SLEEP\"}", "WAKE", "ok") == null);
    Check(SerialProtocol.ReadReply("CP {\"type\":\"ok\",\"command\":\"WAKE\"}", "WAKE", "ok")?.GetProperty("command").GetString() == "WAKE");
});
string StateLine(string state = "working", int version = 2) => $$"""{"version":{{version}},"producerId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1,"baseState":"{{state}}","timestamp":"2026-09-25T10:00:00Z"}""";
IpcEvent Event(BaseState state, long sequence = 1, ReactionKind? reaction = null) => new("state", state, now, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", sequence, reaction);
Test("distinct base mappings", () => Check(Enum.GetValues<BaseState>().Select(PresentationState.Map).SequenceEqual(["idle", "thinking", "working", "waiting", "sleepy"])));
Test("success returns to current working base", () => {
    var s = new PresentationState(); s.Receive(Event(BaseState.Working, reaction: ReactionKind.Success), now);
    Check(s.Resolve(config, now).Reaction == "success");
    Check(s.Resolve(config, now.AddMilliseconds(4999)).Reaction == "success");
    Check(s.Resolve(config, now.AddMilliseconds(5000)).Expression == "working" && s.Reaction == null);
});
Test("error resumes Thinking, not Idle", () => {
    var s = new PresentationState(); s.Receive(Event(BaseState.Thinking, reaction: ReactionKind.Error), now);
    Check(s.Resolve(config, now).Reaction == "error");
    Check(s.Resolve(config, now.AddMilliseconds(2999)).Reaction == "error");
    Check(s.Resolve(config, now.AddMilliseconds(3000)).Expression == "thinking" && s.Reaction == null);
});
Test("Stop is neutral, Idle is its base", () => {
    var s = new PresentationState(); s.Receive(Event(BaseState.Idle, reaction: ReactionKind.Stop), now);
    Check(s.Resolve(config, now).Reaction == "stop");
    Check(s.Resolve(config, now.AddMilliseconds(900)).Expression == "idle" && s.Reaction == null);
});
Test("Success finishes before the latest resident state is displayed", () => {
    var s = new PresentationState(); s.Receive(Event(BaseState.Working, reaction: ReactionKind.Success), now);
    var first = s.Resolve(config, now);
    s.Receive(Event(BaseState.WaitingForUser, 2), now.AddMilliseconds(10));
    Check(s.Codex == BaseState.WaitingForUser && s.Resolve(config, now.AddMilliseconds(10)) == first);
    s.Receive(Event(BaseState.Thinking, 3), now.AddSeconds(1));
    s.Receive(Event(BaseState.WaitingForUser, 4), now.AddSeconds(2));
    Check(s.Resolve(config, now.AddMilliseconds(4999)) == first);
    Check(s.Resolve(config, now.AddSeconds(5)).Expression == "waiting" && s.Reaction == null);
});
Test("Success discards other reactions without extending its deadline", () => {
    var s = new PresentationState(); s.Receive(Event(BaseState.Idle, reaction: ReactionKind.Success), now);
    var first = s.Resolve(config, now);
    s.Receive(Event(BaseState.Working, 2, ReactionKind.Error), now.AddSeconds(1));
    s.Receive(Event(BaseState.Thinking, 3, ReactionKind.Stop), now.AddSeconds(2));
    s.Receive(Event(BaseState.Idle, 4, ReactionKind.Success), now.AddSeconds(4));
    Check(s.Resolve(config, now.AddMilliseconds(4999)) == first);
    Check(s.Resolve(config, now.AddSeconds(5)).Expression == "idle" && s.Reaction == null);
    Check(s.Resolve(config, now.AddSeconds(6)).Reaction == null);
});
Test("resident updates during Success produce no serial cancellation or replay", () => {
    var s = new PresentationState(); using var fake = new FakeTransport(); using var c = new PetConnection(fake); c.Connect(config);
    s.Receive(Event(BaseState.Idle, reaction: ReactionKind.Success), now); c.Synchronize(s.Resolve(config, now));
    fake.Commands.Clear(); s.Receive(Event(BaseState.WaitingForUser, 2, ReactionKind.Error), now.AddSeconds(1));
    Check(!c.Synchronize(s.Resolve(config, now.AddSeconds(1))) && fake.Commands.Count == 0);
    c.Synchronize(s.Resolve(config, now.AddSeconds(5)));
    Check(fake.Commands.SequenceEqual(["STATE waiting", "STATUS"]));
});
Test("sleep still overrides protected Success", () => {
    var s = new PresentationState(); s.Receive(Event(BaseState.Idle, reaction: ReactionKind.Success), now);
    s.ManualSleep = true; Check(s.Resolve(config, now.AddSeconds(1)).Sleeping && s.Reaction == null);
});
Test("pending Success survives coalesced resident updates", () => {
    var inbox = new LatestActions(); var s = new PresentationState();
    inbox.Post(HubInput.Plugin, () => s.Receive(Event(BaseState.Thinking), now));
    inbox.Post(HubInput.PluginSuccess, () => s.Receive(Event(BaseState.Idle, 2, ReactionKind.Success), now));
    inbox.Post(HubInput.Plugin, () => s.Receive(Event(BaseState.Working, 3), now));
    Check(inbox.Count == 2); foreach(var action in inbox.Take()) action();
    Check(s.Resolve(config, now).Reaction == "success" && s.Codex == BaseState.Working);
    Check(s.Resolve(config, now.AddSeconds(5)).Expression == "working");
});
Test("same-base activity preempts reaction, duplicate does not restart it", () => {
    var s = new PresentationState(); var e = Event(BaseState.Working, reaction: ReactionKind.Success);
    s.Receive(e, now); s.Receive(e, now.AddSeconds(1));
    Check(s.Resolve(config, now.AddSeconds(5)).Reaction == null);
    s.Receive(Event(BaseState.Working, 2, ReactionKind.Error), now.AddSeconds(6));
    s.Receive(Event(BaseState.Working, 3), now.AddSeconds(6)); Check(s.Reaction == null);
    s.Receive(e, now.AddSeconds(7)); Check(s.Reaction == null);
});
Test("sleep masks reactions; Wake restores latest waiting base", () => {
    var s = new PresentationState { Locked = true };
    s.Receive(Event(BaseState.Working, reaction: ReactionKind.Error), now);
    Check(s.Resolve(config, now).Sleeping && s.Reaction == null);
    s.Receive(Event(BaseState.WaitingForUser, 2), now);
    s.Locked = false;
    Check(s.Resolve(config, now).Reaction == "wake");
    Check(s.Resolve(config, now.AddSeconds(2)).Expression == "waiting" && s.Reaction == null);
});
Test("wake is preemptable by new semantic activity", () => {
    var s = new PresentationState { ManualSleep = true }; s.Resolve(config, now); s.ManualSleep = false; s.Resolve(config, now);
    s.Receive(Event(BaseState.Working), now); Check(s.Resolve(config, now).Reaction == null);
});
Test("sleep causes remain independent", () => {
    var s = new PresentationState { Locked=true, DisplayOff=true, ManualSleep=true };
    s.Locked=false; Check(s.Resolve(config,now).Sleeping); s.ManualSleep=false; Check(s.Resolve(config,now).Sleeping);
    s.DisplayOff=false; Check(!s.Resolve(config,now).Sleeping);
    s.Suspended=true; Check(s.Resolve(config with { SleepOnWindowsLock=false, SleepOnDisplayOff=false },now).Sleeping);
});
Test("manual recipe tracks live base and restores it", () => {
    var s = new PresentationState { ManualRecipe=true }; s.Test("working",now);
    s.Receive(Event(BaseState.WaitingForUser),now);
    Check(s.Resolve(config,now.AddMinutes(1)).Expression=="working"); s.EndRecipe();
    Check(s.Resolve(config,now).Expression=="waiting");
});
Test("disconnect and identical reconnect snapshot restore base without reaction", () => {
    var s = new PresentationState(); var e=Event(BaseState.Working); s.Receive(e,now); s.DisconnectPlugin();
    Check(!s.PluginConnected && s.Codex==BaseState.Idle); s.Receive(e,now);
    Check(s.Codex==BaseState.Working && s.Reaction==null);
});
Test("IPC parses bases and reactions", () => {
    foreach(var state in new[]{"idle","thinking","working","waitingForUser"}) Check(IpcProtocol.Parse(StateLine(state)).State!=null);
    foreach(var r in new[]{"success","error","stop"}) Check(IpcProtocol.Parse(StateLine().Replace("\"sequence\":1", "\"sequence\":1,\"reaction\":\""+r+"\"")).Reaction!=null);
});
Test("heartbeat has no semantic effect", () => Check(IpcProtocol.Parse("""{"version":2,"type":"heartbeat","timestamp":"2026-09-25T10:00:00Z"}""").State==null));
Test("IPC rejects legacy, invalid states, Wake injection and malformed input", () => {
    foreach(var line in new[]{StateLine(version:1),StateLine("sleeping"),StateLine("success"),"null","[1]","{bad}",new string('x',1025),StateLine().Replace("10:00:00Z","10:00:00"),StateLine().Replace("\"version\":2","\"version\":2,\"version\":2"),StateLine().Replace("\"sequence\":1","\"sequence\":1,\"reaction\":\"wake\"")}) Reject(()=>IpcProtocol.Parse(line));
});
Test("configuration atomic save and reload keep device name independently of port", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "CodexPetHub.Tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new ConfigStore(Path.Combine(directory, "config.json"));
        Check(store.Load() == config);
        var custom = config with { CurrentPet = new("1234-5678-9ABC", "Béééatrice"), Brightness = 42, SerialPort = "COM7" };
        store.Save(custom);
        Check(store.Load() == custom);
        store.Save(custom with { SerialPort = "COM11" });
        Check(store.Load().CurrentPet == custom.CurrentPet);
        Check(Directory.GetFiles(directory).Length == 1);
    }
    finally { Directory.Delete(directory, true); }
});
Test("config rejects invalid settings without accepting a COM identity", () =>
{
    foreach (var bad in new[] { config with { Brightness = 101 }, config with { ReconnectInterval = 0 }, config with { SerialPort = "COM0" }, config with { CurrentPet = new("COM7", "Pet") }, config with { CurrentPet = new("1234-5678-9ABC", "\n") } }) Reject(bad.Validate);
});
Test("sound preference persists and old configurations select the approved voice", () => {
    Check(JsonSerializer.Deserialize<HubConfig>("{}", ConfigStore.Json)!.SuccessSound == SuccessSound.Voice);
    var directory = Path.Combine(Path.GetTempPath(), "CodexPetSound-" + Guid.NewGuid().ToString("N"));
    try {
        var store = new ConfigStore(Path.Combine(directory, "config.json"));
        foreach (var sound in Enum.GetValues<SuccessSound>()) {
            store.Save(config with { SuccessSound = sound });
            Check(store.Load().SuccessSound == sound);
        }
        Reject((config with { SuccessSound = (SuccessSound)99 }).Validate);
    } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
Test("audio capability is explicit and optional on older firmware", () => {
    var legacy = SerialProtocol.ReadInfo(SerialProtocol.Parse(FakeTransport.InfoLine));
    Check(!legacy.SuccessAudio);
    var modern = SerialProtocol.ReadInfo(SerialProtocol.Parse(FakeTransport.InfoLine.Replace("\"firmwareVersion\":", "\"successAudio\":true,\"firmwareVersion\":")));
    Check(modern.SuccessAudio);
});
Test("sound setting precedes Success and each reaction plays only once", () => {
    using var fake = new FakeTransport { AudioCapable = true }; using var c = new PetConnection(fake); c.Connect(config);
    var desired = new Presentation("idle", false, 60, "success", 1, SuccessSound.Voice);
    c.Synchronize(desired);
    Check(fake.Commands.IndexOf("SOUND voice") < fake.Commands.IndexOf("REACT success"));
    Check(fake.Commands.Count(x => x == "REACT success") == 1);
    fake.Commands.Clear(); Check(!c.Synchronize(desired) && fake.Commands.Count == 0);
    c.Synchronize(desired with { SuccessSound = SuccessSound.None });
    Check(fake.Commands.SequenceEqual(["SOUND none", "STATUS"]));
    fake.Commands.Clear(); c.Synchronize(desired with { SuccessSound = SuccessSound.Fanfare });
    Check(fake.Commands.SequenceEqual(["SOUND fanfare", "STATUS"]));
});
Test("reconnection restores sound preference without replaying the previous cue", () => {
    using var fake = new FakeTransport { AudioCapable = true }; using var c = new PetConnection(fake); c.Connect(config);
    c.Synchronize(new("idle", false, 60, "success", 1, SuccessSound.Voice));
    c.Disconnect(); fake.Commands.Clear(); c.Connect(config);
    c.Synchronize(new("working", false, 60, null, 2, SuccessSound.Fanfare));
    Check(fake.Commands.Contains("SOUND fanfare") && !fake.Commands.Any(x => x.StartsWith("REACT ")));
});
Test("legacy firmware receives no audio command even when voice is selected", () => {
    using var fake = new FakeTransport(); using var c = new PetConnection(fake); c.Connect(config);
    c.Synchronize(new("idle", false, 60, "success", 1, SuccessSound.Voice));
    Check(!fake.Commands.Any(x => x.StartsWith("SOUND ")) && fake.Commands.Contains("REACT success"));
});
Test("firmware audio diagnostics are parsed without inferring audibility", () => {
    var status = SerialProtocol.ReadStatus(SerialProtocol.Parse("""CP {"state":"success","requestedState":"idle","brightness":60,"sleeping":false,"diagnostic":false,"successSound":"voice","audioReady":true,"audioPlaying":true,"audioPlayCount":3,"audioErrorCount":0}"""));
    Check(status.SuccessSound == "voice" && status.AudioReady == true && status.AudioPlaying == true && status.AudioPlayCount == 3 && status.AudioErrorCount == 0);
});
Test("firmware INFO checks type version and hardware identity", () =>
{
    var info = FakeTransport.InfoLine;
    var parsed = SerialProtocol.ReadInfo(SerialProtocol.Parse(info));
    Check(parsed.DeviceId == "1234-5678-9ABC");
    foreach (var bad in new[] { info.Replace("CodexPet", "OtherDevice"), info.Replace("Version\":1", "Version\":2"), info.Replace("1234-5678-9ABC", "COM7"), info.Replace("1234-5678-9ABC", "0000-0000-0000") })
        Reject(() => SerialProtocol.ReadInfo(SerialProtocol.Parse(bad)));
});
Test("firmware STATUS validation", () =>
{
    Reject(() => SerialProtocol.ReadStatus(SerialProtocol.Parse("""CP {"state":"unknown","requestedState":"idle","brightness":60,"sleeping":false,"diagnostic":false}""")));
    Reject(() => SerialProtocol.Parse("garbage"));
});
Test("initial synchronization happens after connect", () =>
{
    using var fake = new FakeTransport();
    using var connection = new PetConnection(fake);
    Check(!connection.Synchronize(new("thinking", false, 60)));
    connection.Connect(config);
    connection.Synchronize(new("thinking", false, 60));
    Check(fake.Commands.SequenceEqual(["INFO", "BRIGHTNESS 60", "STATE thinking", "WAKE", "STATUS"]));
});
Test("unchanged presentation sends no redundant commands", () =>
{
    using var fake = new FakeTransport();
    using var connection = new PetConnection(fake);
    connection.Connect(config);
    var desired = new Presentation("thinking", false, 60);
    connection.Synchronize(desired);
    var count = fake.Commands.Count;
    Check(!connection.Synchronize(desired) && fake.Commands.Count == count);
});
Test("reconnect restores current waiting state including brightness", () =>
{
    using var fake = new FakeTransport();
    using var connection = new PetConnection(fake);
    connection.Connect(config);
    connection.Synchronize(new("thinking", false, 60));
    connection.Disconnect();
    Check(connection.Displayed == null && connection.Device == null);
    fake.Commands.Clear();
    connection.Connect(config);
    connection.Synchronize(new("listening", false, 42));
    Check(fake.Commands.SequenceEqual(["INFO", "BRIGHTNESS 42", "STATE listening", "WAKE", "STATUS"]));
    Check(connection.Displayed?.State == "listening");
});
Test("wake installs latest base before waking", () =>
{
    using var fake = new FakeTransport();
    using var connection = new PetConnection(fake);
    connection.Connect(config);
    connection.Synchronize(new("thinking", true, 60));
    fake.Commands.Clear();
    connection.Synchronize(new("listening", false, 60));
    Check(fake.Commands.SequenceEqual(["STATE listening", "WAKE", "STATUS"]));
});
Test("failed ACK leaves transaction eligible for retry", () =>
{
    using var fake = new FakeTransport { FailOn = "STATE thinking" };
    using var connection = new PetConnection(fake);
    connection.Connect(config);
    try { connection.Synchronize(new("thinking", false, 60)); throw new Exception("No error"); } catch (IOException) { }
    fake.FailOn = null;
    fake.Commands.Clear();
    Check(connection.Synchronize(new("thinking", false, 60)));
    Check(fake.Commands.Contains("STATE thinking"));
});

Test("reaction transport preserves base and cancels on same-base revision", () => {
    using var fake=new FakeTransport(); using var c=new PetConnection(fake); c.Connect(config);
    c.Synchronize(new("working",false,60,"success",1));
    Check(fake.Commands.Contains("REACT success")); fake.Commands.Clear();
    c.Synchronize(new("working",false,60,null,2));
    Check(fake.Commands.SequenceEqual(["STATE working","STATUS"]));
});

try
{
    var pipeName = "CodexPetHub.Test." + Guid.NewGuid().ToString("N");
    var received = new TaskCompletionSource<IpcEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var server = new PipeServer(pipeName, value => received.TrySetResult(value), () => disconnected.TrySetResult(), _ => { });
    var run = server.RunAsync(stop.Token);
    using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous))
    {
        await client.ConnectAsync(stop.Token);
        var bytes = Encoding.UTF8.GetBytes(StateLine() + "\n");
        await client.WriteAsync(bytes, stop.Token);
        var value = await received.Task.WaitAsync(stop.Token);
        Check(value.State == BaseState.Working);
    }
    await disconnected.Task.WaitAsync(stop.Token);
    stop.Cancel();
    await run;
    Console.WriteLine("PASS real Named Pipe delivery and disconnect");
    passed++;
}
catch (Exception ex) { Console.WriteLine("FAIL real Named Pipe: " + ex); failed++; }

Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeTransport : IPetTransport
{
    public const string InfoLine = """CP {"type":"info","protocolVersion":1,"deviceType":"CodexPet","deviceId":"1234-5678-9ABC","firmwareVersion":"1.1.0"}""";
    public bool IsConnected { get; private set; }
    public string? Endpoint => IsConnected ? "COM7" : null;
    public List<string> Commands { get; } = [];
    public string? FailOn { get; set; }
    public bool AudioCapable { get; set; }
    private string state = "idle";
    private bool sleeping;
    private int brightness = 60;
    public DeviceInfo Connect(HubConfig config) { Commands.Add("INFO"); IsConnected = true; return SerialProtocol.ReadInfo(SerialProtocol.Parse(InfoLine)) with { SuccessAudio = AudioCapable }; }
    public JsonElement Send(string command, string responseType)
    {
        Commands.Add(command);
        if (command == FailOn) throw new IOException("Simulated disconnect during command.");
        if (command.StartsWith("STATE ")) state = command[6..];
        if (command.StartsWith("BRIGHTNESS ")) brightness = int.Parse(command[11..]);
        if (command == "SLEEP") sleeping = true;
        if (command == "WAKE") sleeping = false;
        if (command == "STATUS") return JsonSerializer.SerializeToElement(new { type = "status", state = sleeping ? "sleep" : state, requestedState = state, sleeping, brightness, diagnostic = false });
        return JsonSerializer.SerializeToElement(new { type = "ok", command = command.Split(' ')[0] });
    }
    public void Disconnect() => IsConnected = false;
    public void Dispose() => Disconnect();
}
