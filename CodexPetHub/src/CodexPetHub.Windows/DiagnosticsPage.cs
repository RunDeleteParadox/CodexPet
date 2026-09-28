// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using System.Text;
using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal sealed class DiagnosticsPage : UserControl
{
    private readonly HubService hub;
    private readonly TextBox details = new() { Name = "Details", Multiline = true, ReadOnly = true, TabStop = false, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Button logs = new() { AutoSize = true };
    private readonly Button reconnect = new() { AutoSize = true };
    private readonly Button export = new() { AutoSize = true };
    private UiText text;

    public DiagnosticsPage(HubService hub)
    {
        this.hub = hub;
        text = new(hub.Snapshot.Config.Language);
        Name = "DiagnosticsPage";
        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(0, 8, 0, 0);
        Controls.Add(details);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        logs.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hub.DataDirectory) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { MessageBox.Show(this, text.Format("OpenLogsFailed", UiMessage.From(ex).Render(text)), "CodexPet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        reconnect.Click += (_, _) => hub.Reconnect();
        footer.Controls.Add(logs);
        footer.Controls.Add(reconnect);
        export.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = text["ExportTitle"], Filter = text["ZipFilter"] + "|*.zip",
                FileName = "CodexPet-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { hub.ExportDiagnostics(dialog.FileName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, text.Format("ExportFailed", UiMessage.From(ex).Render(text)), "CodexPet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        footer.Controls.Add(export);
        Controls.Add(footer);
        RefreshDetails();
    }

    internal void RefreshDetails(string? language = null)
    {
        var s = hub.Snapshot;
        text = new(language ?? s.Config.Language);
        Text = text["DiagnosticsTitle"];
        logs.Text = text["OpenLogs"];
        reconnect.Text = text["Reconnect"];
        export.Text = text["ExportDiagnostics"];
        var content = new StringBuilder();
        void Line(string key, params object?[] values) => content.AppendLine(text.Format(key, values));
        content.AppendLine(s.Config.CurrentPet?.FriendlyName ?? "CodexPet");
        content.AppendLine(HubService.HealthLabel(s, text.Language));
        Line("ProcessingLine", text.Health(s.Health.Loop), text.Health(s.Health.Pipe), s.Health.PendingInputs);
        Line("LastProgressLine", text.Date(s.Health.LastLoopAt), s.Health.LoopAgeSeconds);
        if (s.Health.Fault != null) Line("FaultLine", s.Health.Fault switch
        {
            "processing stopped unexpectedly." => text["ErrorProcessingStopped"],
            "ipc stopped unexpectedly." => text["ErrorIpcStopped"],
            _ => text.Format("TechnicalDetails", s.Health.Fault)
        });
        if (s.Health.DiagnosticsError != null) Line("DiagnosticsErrorLine", text.Format("TechnicalDetails", s.Health.DiagnosticsError));
        content.AppendLine();
        Line("HubLine", Assembly.GetExecutingAssembly().GetName().Version);
        Line("ProtocolsLine", IpcProtocol.Version, SerialProtocol.Version);
        Line("PluginLine", text[s.PluginConnected ? "Connected" : "Disconnected"]);
        Line("CodexStateLine", text.State(s.CodexState));
        Line("LastEventLine", text.Date(s.LastEventAt));
        content.AppendLine();
        Line("PortLine", s.Endpoint ?? text["NotRecorded"], s.Config.SerialPort == "auto" ? text["AutomaticPort"] : s.Config.SerialPort);
        Line("FirmwareLine", s.Device?.FirmwareVersion ?? text["NotRecorded"]);
        Line("DeviceIdLine", s.Device?.DeviceId ?? s.Config.CurrentPet?.DeviceId ?? text["NotRecorded"]);
        Line("RequestedStateLine", text.Expression(s.Requested.Expression), text[s.Requested.Sleeping ? "Asleep" : "Awake"]);
        Line("RequestedReactionLine", s.Requested.Reaction == null ? text["None"] : text.Expression(s.Requested.Reaction));
        Line("ReportedStateLine", text.Expression(s.Displayed?.State));
        Line("FirmwareDiagnosticLine", text.Boolean(s.Displayed?.Diagnostic));
        Line("BrightnessLine", s.Config.Brightness);
        Line("SoundLine", text.Sound(s.Config.SuccessSound), text.Sound(s.Displayed?.SuccessSound));
        Line("AudioReadyLine", text.Boolean(s.Displayed?.AudioReady), text.Boolean(s.Displayed?.AudioPlaying));
        Line("AudioCountsLine", text.Number(s.Displayed?.AudioPlayCount), text.Number(s.Displayed?.AudioErrorCount));
        Line("LastSerialLine", text.Date(s.LastSerialReplyAt));
        Line("LastStatusLine", text.Date(s.LastStatusAt));
        Line("BootLine", s.Displayed?.BootId ?? text["Unavailable"], s.Displayed?.ResetReason ?? text["Unavailable"]);
        Line("UptimeLine", s.Displayed?.UptimeMs is { } ms ? TimeSpan.FromMilliseconds(ms).ToString(@"d\.hh\:mm\:ss", text.Culture) : text["Unavailable"]);
        Line("PanelLine", text.Boolean(s.Displayed?.PanelAsleep), text.Power(s.Displayed?.LastPowerCommand));
        Line("VoltageLine", Millivolts(s.Displayed?.BatteryMillivolts), Millivolts(s.Displayed?.UsbMillivolts));
        Line("UsbLine", text.Boolean(s.Displayed?.UsbPresent), text.Boolean(s.Displayed?.Charging));
        content.AppendLine();
        Line("WindowsLine", text.Boolean(s.Locked), text.Boolean(s.DisplayOff), text.Boolean(s.Suspended));
        Line("LastErrorLine", s.ErrorMessage?.Render(text) ?? (s.LastError == null ? text["None"] : text.Format("TechnicalDetails", s.LastError)));
        content.AppendLine();
        Line("PipeLine", hub.PipeName);
        Line("DataLine", hub.DataDirectory);
        content.AppendLine(text["PhysicalScreenHint"]);
        if (details.Text != content.ToString()) details.Text = content.ToString();
    }

    private string Millivolts(int? value) => value is { } mv ? (mv / 1000.0).ToString("F3", text.Culture) + " V" : text["Unavailable"];
}
