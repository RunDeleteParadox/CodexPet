// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

// Explicit migration bench ONLY. This binary is not part of the Hub distribution.
// The production transport requires INFO and never falls back to this protocol.
using System.IO.Ports;
using System.Text.RegularExpressions;
using CodexPetHub.Core;

if (args.Length != 2 || !Regex.IsMatch(args[0], @"^COM[1-9][0-9]*$") || !args[1].StartsWith("CodexPetHub.Test."))
{
    Console.Error.WriteLine("Usage: LegacySmoke COMx CodexPetHub.Test.<unique-name>. Stop the prototype worker first.");
    return 2;
}
var state = new TaskCompletionSource<IpcEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var server = new PipeServer(args[1], message => { if (message.State != null) state.TrySetResult(message); }, () => { }, Console.WriteLine);
var running = server.RunAsync(stop.Token);
try
{
    var received = await state.Task.WaitAsync(stop.Token);
    var expression = PresentationState.Map(received.State!.Value);
    using var serial = new SerialPort(args[0], 115200, Parity.None, 8, StopBits.One)
    { ReadTimeout = 100, WriteTimeout = 700, NewLine = "\n", DtrEnable = false, RtsEnable = false };
    serial.Open();
    serial.DiscardInBuffer();
    serial.WriteLine(expression);
    var expected = "Command accepted: " + expression.ToUpperInvariant();
    var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (DateTimeOffset.UtcNow < deadline)
    {
        string line;
        try { line = serial.ReadLine().Trim(); } catch (TimeoutException) { continue; }
        if (line == expected)
        {
            Console.WriteLine($"PASS Plugin -> Named Pipe -> Hub mapping -> USB -> existing firmware: {received.State} -> {expression}; {line}");
            Console.WriteLine("ACK verified; physical appearance still requires human observation.");
            return 0;
        }
    }
    throw new TimeoutException("No matching firmware ACK.");
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
finally { stop.Cancel(); await running; }
