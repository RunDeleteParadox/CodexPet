// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.IO.Ports;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal sealed class SerialPetTransport(Action<string>? log = null, Action<Exception>? cleanupError = null) : IPetTransport
{
    private SerialPort? port;
    public bool IsConnected => port?.IsOpen == true;
    public string? Endpoint => port?.PortName;
    public DateTimeOffset? LastReplyAt { get; private set; }
    private readonly StringBuilder partialLine = new();
    private bool droppingLine;

    public static string[] CandidatePorts()
    {
        using var search = new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID FROM Win32_SerialPort");
        using var devices = search.Get();
        var result = new List<string>();
        foreach (ManagementObject device in devices)
        {
            using (device)
            {
                var id = device["PNPDeviceID"]?.ToString() ?? "";
                var name = device["DeviceID"]?.ToString() ?? "";
                if (Regex.IsMatch(id, @"VID_303A&PID_1001(?:&|\\)", RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(name, @"^COM[1-9][0-9]*$")) result.Add(name);
            }
        }
        return result.Distinct().Order().ToArray();
    }

    public DeviceInfo Connect(HubConfig config)
    {
        Disconnect();
        var candidates = config.SerialPort == "auto" ? CandidatePorts() : [config.SerialPort];
        var identified = new List<(SerialPort Port, DeviceInfo Info)>();
        var failures = new List<UiMessage>();
        try
        {
            foreach (var endpoint in candidates)
            {
                try
                {
                    port = new SerialPort(endpoint, 115200, Parity.None, 8, StopBits.One)
                    {
                        Encoding = Encoding.ASCII, NewLine = "\n", ReadTimeout = 100, WriteTimeout = 700,
                        DtrEnable = false, RtsEnable = false, Handshake = Handshake.None
                    };
                    port.Open();
                    port.DiscardInBuffer();
                    var info = SerialProtocol.ReadInfo(Send("INFO", "info"));
                    if (config.CurrentPet != null && info.DeviceId != config.CurrentPet.DeviceId)
                        throw UiMessage.Invalid("ErrorDifferentPet");
                    identified.Add((port, info));
                    port = null;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or ArgumentException)
                {
                    failures.Add(new("ErrorPortFailure", endpoint, UiMessage.From(ex)));
                    Disconnect();
                }
            }
            if (identified.Count == 0)
            {
                if (failures.Count == 0) throw UiMessage.Io("ErrorNoCandidate");
                // Keep each localized failure, with a stable English log message.
                var failure = failures.Aggregate((left, right) => new UiMessage("ErrorMultiplePorts", left, right));
                throw failure.Attach(new IOException(failure.Render(new("en"))));
            }
            if (identified.Count > 1) throw UiMessage.Io("ErrorSeveralPets");
            port = identified[0].Port;
            return identified[0].Info;
        }
        finally
        {
            foreach (var item in identified) if (!ReferenceEquals(item.Port, port)) Release(item.Port);
        }
    }

    public JsonElement Send(string command, string responseType)
    {
        try { return Exchange(command, responseType); }
        catch (TimeoutException) when (command == "STATUS")
        {
            // A read-only query can be retried without replaying a visual transition.
            // Real USB runs showed occasional isolated missing STATUS replies.
            log?.Invoke("STATUS reply timed out; retrying once before reconnect.");
            return Exchange(command, responseType);
        }
    }

    private JsonElement Exchange(string command, string responseType)
    {
        if (port == null || !port.IsOpen) throw UiMessage.Io("ErrorPetDisconnected");
        port.WriteLine(command);
        var clock = Stopwatch.StartNew();
        // Retain incomplete replies across exchanges/timeouts until their newline.
        var line = partialLine;
        while (clock.ElapsedMilliseconds < 1800)
        {
            int value;
            try { value = port.ReadChar(); }
            catch (TimeoutException) { continue; }
            if (value < 0) throw UiMessage.Io("ErrorSerialClosed");
            if (value == '\r') continue;
            if (value != '\n')
            {
                if (line.Length >= 1024) { droppingLine = true; line.Clear(); }
                if (!droppingLine) line.Append((char)value);
                continue;
            }
            var text = line.ToString();
            line.Clear();
            if (droppingLine) { droppingLine = false; continue; }
            if (!text.StartsWith("CP ", StringComparison.Ordinal)) continue; // Upstream performance/IMU chatter.
            var response = SerialProtocol.ReadReply(text, command, responseType);
            if (response == null) continue;
            LastReplyAt = DateTimeOffset.UtcNow;
            return response.Value;
        }
        var timeout = new UiMessage("ErrorTimeout", responseType.ToUpperInvariant(), command.Split(' ')[0],
            new UiMessage(command == "INFO" ? "ErrorTimeoutInfo" : "ErrorTimeoutRetry"));
        throw timeout.Attach(new TimeoutException(timeout.Render(new("en"))));
    }

    public void Disconnect()
    {
        var detached = port;
        port = null;
        partialLine.Clear();
        droppingLine = false;
        if (detached != null) Release(detached);
    }
    private void Release(SerialPort detached)
    {
        try { detached.Dispose(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { cleanupError?.Invoke(ex); }
    }
    public void Dispose() => Disconnect();
}
