// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Text.Json;
using CodexPetHub.Core;
using CodexPetHub.Windows;

internal static class DeviceChecks
{
    public static async Task<int> Run(string port, string deviceId)
    {
        if (Process.GetProcessesByName("CodexPetHub").Length != 0)
            throw new InvalidOperationException("Stop the production Hub before exclusive device validation.");
        var root = Path.Combine(Path.GetTempPath(), "CodexPetDeviceReliability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        new ConfigStore(Path.Combine(root, "config.json")).Save(new HubConfig {
            LaunchOnStartup = false, SerialPort = port, Brightness = 50, CurrentPet = new(deviceId, "Test Pet") });
        var passed = new List<string>();
        using var hub = new HubService(root, pipeOverride: "CodexPetDeviceTest." + Guid.NewGuid().ToString("N"));
        async Task Await(Func<HubSnapshot, bool> condition, string label)
        {
            var deadline = Environment.TickCount64 + 15000;
            while (Environment.TickCount64 < deadline)
            {
                if (condition(hub.Snapshot)) { passed.Add(label); Console.WriteLine("PASS " + label); return; }
                await Task.Delay(100);
            }
            throw new Exception("Device timeout: " + label + "; error=" + hub.Snapshot.LastError);
        }
        try
        {
            await Await(s => s.Device?.DeviceId == deviceId && s.Device.FirmwareVersion == "1.1.4" && s.Displayed?.BootId?.Length == 16, "paired firmware 1.1.4 and boot identity");
            var boot = hub.Snapshot.Displayed!.BootId;
            var uptime = hub.Snapshot.Displayed.UptimeMs;
            await Await(s => s.Displayed?.BatteryMillivolts is >= 2500 and <= 4500 && s.Displayed.UsbMillivolts is >= 4000 and <= 6000 && s.Displayed.UsbPresent == true && s.Displayed.Charging == null, "plausible USB and battery telemetry; charging explicitly unavailable");
            hub.Receive(new("state", BaseState.Thinking, DateTimeOffset.UtcNow, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1));
            await Await(s => s.Displayed is { State: "thinking", Sleeping: false }, "Thinking through Hub and serial");
            hub.Sleep(true);
            await Await(s => s.Displayed is { Sleeping: true, PanelAsleep: true, LastPowerCommand: "sleep" }, "panel asleep and last power command confirmed");
            hub.Receive(new("state", BaseState.WaitingForUser, DateTimeOffset.UtcNow, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 2));
            await Await(s => s.Displayed is { Sleeping: true, RequestedState: "waiting" }, "new base accepted while panel sleeps");
            hub.Sleep(false);
            await Await(s => s.Displayed is { Sleeping: false, PanelAsleep: false, State: "waiting", LastPowerCommand: "wake" }, "wake restores latest base");
            hub.Reconnect();
            hub.Receive(new("state", BaseState.Working, DateTimeOffset.UtcNow, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 3));
            await Await(s => s.Displayed is { State: "working", Sleeping: false } && s.Displayed.BootId == boot && s.Displayed.UptimeMs > uptime, "serial reopen preserves boot and restores Working");
            // Calls only the Hub's notification handler; does not suspend the user's PC.
            hub.Suspend(true);
            await Await(s => s.Suspended && s.Displayed?.Sleeping == true, "simulated Windows suspend reaches physical Pet");
            hub.Suspend(false); hub.Suspend(false);
            await Await(s => !s.Suspended && s.Displayed is { State: "working", Sleeping: false } && s.Health.Loop == "running", "duplicate resume recovers physical Pet");
            File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed, snapshot = hub.Snapshot }, ConfigStore.Json));
            Console.WriteLine($"RESULT: {passed.Count} physical protocol checks passed");
            return 0;
        }
        finally
        {
            hub.RequestStop("device-validation-complete");
            Console.WriteLine("Evidence: " + root);
        }
    }
}
