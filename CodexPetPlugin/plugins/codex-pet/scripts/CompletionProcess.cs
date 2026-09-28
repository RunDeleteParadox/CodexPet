// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

// .NET Framework redirected pipes can block even through ReadLineAsync.
// Two bounded background drains keep the PowerShell IPC loop responsive.
public sealed class CodexPetCompletionOutput {
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
    readonly Process process;
    volatile bool stopping;
    public volatile bool Closed;
    public volatile bool Started;
    public volatile bool Failed;
    public CodexPetCompletionOutput(Process process) {
        this.process = process;
        var launcher = new Thread(() => {
            try {
                process.Start(); Started = true;
                if (stopping) { Stop(); return; }
                Drain();
            } catch { Failed = true; }
        });
        launcher.IsBackground = true; launcher.Start();
    }
    void Drain() {
        var stdout = new Thread(() => {
            try {
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null) {
                    if (lines.Count < 32) lines.Enqueue(line);
                }
            } catch (IOException) { } catch (ObjectDisposedException) { }
            finally { Closed = true; }
        });
        var stderr = new Thread(() => {
            try {
                var buffer = new char[4096];
                while (process.StandardError.Read(buffer, 0, buffer.Length) > 0) { }
            } catch (IOException) { } catch (ObjectDisposedException) { }
        });
        stdout.IsBackground = true; stderr.IsBackground = true;
        stdout.Start(); stderr.Start();
    }
    public bool TryRead(out string line) { return lines.TryDequeue(out line); }
    public void Stop() {
        stopping = true;
        var cleanup = new Thread(() => {
            try { if (!process.HasExited) process.Kill(); } catch { }
            if (Started) {
                try { process.WaitForExit(1000); process.Dispose(); } catch { }
            }
        });
        cleanup.IsBackground = true; cleanup.Start();
    }
}
