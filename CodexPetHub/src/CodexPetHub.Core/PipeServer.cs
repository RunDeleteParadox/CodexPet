// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Pipes;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CodexPetHub.Core;

public sealed class PipeServer(string pipeName, Action<IpcEvent> receive, Action disconnected, Action<string> log)
{
    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            bool hadState = false;
            try
            {
                using var server = CreateLocalPipe(pipeName);
                await server.WaitForConnectionAsync(stop);
                log("Plugin pipe opened.");
                using var reader = new StreamReader(server, new UTF8Encoding(false, true), false, 1024, leaveOpen: true);
                while (!stop.IsCancellationRequested)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    var line = await ReadBoundedLineAsync(reader, deadline.Token);
                    if (line == null) break;
                    var message = IpcProtocol.Parse(line);
                    if (message.Kind == "state") hadState = true;
                    if (message.Kind == "heartbeat" && !hadState) throw new InvalidDataException("A new connection requires a state snapshot.");
                    receive(message);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { log("Plugin heartbeat timed out."); }
            catch (Exception ex) when (ex is IOException or DecoderFallbackException or UnauthorizedAccessException)
            { log(ex is InvalidDataException ? ex.Message : "Plugin pipe closed or unavailable."); }
            finally { if (hadState) { disconnected(); log("Plugin disconnected; logical state returned to Idle."); } }
            try { await Task.Delay(100, stop); } catch (OperationCanceledException) { break; }
        }
    }

    private static NamedPipeServerStream CreateLocalPipe(string name)
    {
        if (!OperatingSystem.IsWindows())
            return new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // Same account only, explicitly deny network logons (including SMB pipe access).
        var security = new PipeSecurity();
        var current = WindowsIdentity.GetCurrent().User!;
        security.SetOwner(current);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(current, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 2048, 0, security);
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var line = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), token) != 0)
        {
            if (buffer[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= IpcProtocol.MaxLineLength) throw new InvalidDataException("IPC line too long.");
            line.Append(buffer[0]);
        }
        if (line.Length != 0) throw new InvalidDataException("Incomplete IPC message.");
        return null;
    }
}
