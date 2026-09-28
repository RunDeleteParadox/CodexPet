// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Security.Principal;
using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        var data = Option("--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-pet-hub");
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var pipeOverride = Option("--pipe");
        using var singleton = new Mutex(false, "Local\\CodexPetHub.v1." + sid + (pipeOverride == null ? "" : "." + pipeOverride));
        bool owned;
        try { owned = singleton.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) return 0;
        HubJournal? journal = null;
        var text = new UiText(UiText.DefaultLanguage);
        try
        {
            journal = new HubJournal(data);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                journal.Write("process-fatal", "Unhandled process exception.", error: e.ExceptionObject as Exception);
                journal.Heartbeat("faulted", "unhandled-exception");
            };
            TaskScheduler.UnobservedTaskException += (_, e) => journal.Write("unobserved-task", "Unobserved background exception.", error: e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ApplicationConfiguration.Initialize();
            using var hub = new HubService(data, args.Contains("--no-serial"), pipeOverride, journal: journal);
            text = new(hub.Snapshot.Config.Language);
            System.Globalization.CultureInfo.CurrentUICulture = text.Culture;
            if (args.Contains("--headless"))
            {
                // Bounded diagnostic invocation; same IPC/state/connection service as the systray.
                var seconds = int.TryParse(Option("--seconds"), out var value) ? Math.Clamp(value, 1, 3600) : 30;
                using var ended = new ManualResetEventSlim();
                ended.Wait(TimeSpan.FromSeconds(seconds));
                hub.RequestStop("diagnostic-complete");
            }
            else if (Option("--render-preview") is { } preview)
            {
                Directory.CreateDirectory(preview);
                using var form = new HubWindow(hub);
                foreach (var page in Enum.GetValues<HubPage>())
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.Show();
                    form.SelectPage(page);
                    Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(preview, page.ToString().ToLowerInvariant() + ".png"));
                }
                form.Close();
            }
            else
            {
                using var context = new TrayContext(hub);
                using var testTimer = new System.Windows.Forms.Timer();
                if (args.Contains("--smoke-tray"))
                {
                    testTimer.Interval = 3000;
                    testTimer.Tick += (_, _) => context.ExitThread();
                    testTimer.Start();
                }
                Application.Run(context);
            }
            return hub.HasFault ? 2 : 0;
        }
        catch (Exception ex)
        {
            journal?.Write("process-fatal", "Hub startup or application failed.", error: ex);
            journal?.Heartbeat("faulted", "application-exception");
            if (!args.Contains("--headless")) MessageBox.Show(text.Format("StartupFailed", UiMessage.From(ex).Render(text)), "CodexPetHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
            {
                try { Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "startup-error.txt"), ex.ToString()); }
                catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
            }
            return 1;
        }
        finally { singleton.ReleaseMutex(); }
    }
}
