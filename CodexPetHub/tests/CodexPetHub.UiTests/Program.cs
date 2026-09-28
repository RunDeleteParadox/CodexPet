// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using CodexPetHub.Core;
using CodexPetHub.Windows;

namespace CodexPetHub.UiTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var root = Path.Combine(Path.GetTempPath(), "CodexPetUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passed = 0;
        var failed = 0;
        var hosts = new List<Form>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        void Show(Control control)
        {
            var form = control as Form;
            if (form == null)
            {
                form = new Form { ClientSize = new Size(770, 630), Font = new Font("Segoe UI", 10) };
                form.Controls.Add(control);
                hosts.Add(form);
            }
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
            Application.DoEvents();
        }
        T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
        void Await(Func<bool> condition)
        {
            var end = Environment.TickCount64 + 5000;
            while (!condition() && Environment.TickCount64 < end) { Application.DoEvents(); Thread.Sleep(10); }
            Check(condition(), "Timed out waiting for a saved preference");
        }
        void Render(Control control, string file)
        {
            var form = control.FindForm()!;
            if (args.Length != 2 || args[0] != "--render") return;
            Directory.CreateDirectory(args[1]);
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(args[1], file));
        }
        try
        {
            var config = new HubConfig { Language = "en", Brightness = 42, SuccessSound = SuccessSound.Fanfare, CurrentPet = new("1234-5678-9ABC", "Béatrice") };
            Test("license notices are available in both interface languages", () =>
            {
                foreach (var language in UiText.Languages)
                {
                    var text = new UiText(language);
                    using var form = new LicensePage(text);
                    Show(form);
                    Check(Find<Label>(form, "LicenseSummary").Text.Contains("RunDeleteParadox"), "Copyright attribution missing");
                    Check(Find<TextBox>(form, "LicenseTab").Text.Contains("GNU AFFERO GENERAL PUBLIC LICENSE"), "License not shipped");
                    Check(Find<TextBox>(form, "CreditsTab").Text.Contains("AGPL-3.0-or-later"), "Credits missing");
                    var credits = Find<TextBox>(form, "CreditsTab");
                    Check(credits.Text.Contains("trentct") && credits.Text.Contains("https://github.com/Trentct/m5stack-stopwatch-avatar"), "Original firmware attribution missing");
                    Check(credits.Lines[1].Contains("trentct"), "Firmware credit line breaks missing");
                    Render(form, "license-" + language + ".png");
                    ((TabControl)credits.Parent!.Parent!).SelectedTab = (TabPage)credits.Parent;
                    credits.Select(0, 0);
                    Application.DoEvents();
                    Render(form, "credits-" + language + ".png");
                }
            });
            Test("language previews preserve edits; Save returns the chosen language", () =>
            {
                using var form = new SettingsPage(config);
                Show(form);
                var languages = Find<ComboBox>(form, "Language");
                var name = Find<TextBox>(form, "PetName");
                var port = Find<ComboBox>(form, "SerialPort");
                Check(languages.Items.Count == 2 && form.Text == "CodexPet · Settings", "English settings missing");
                name.Text = "Mon Pet";
                port.Text = "COM27";
                languages.SelectedIndex = 1;
                Check(form.Text == "CodexPet · Paramètres" && name.Text == "Mon Pet" && port.Text == "COM27", "Preview lost edited fields");
                Check(config.Language == "en" && form.Result == null, "Preview changed saved config");
                Render(form, "settings-fr.png");
                languages.SelectedIndex = 0;
                Check(port.Text == "COM27" && name.Text == "Mon Pet", "Switching back lost fields");
                Render(form, "settings-en.png");
                languages.SelectedIndex = 1;
                Find<Button>(form, "Save").PerformClick();
                Check(form.Result is { Language: "fr", SerialPort: "COM27", Brightness: 42, SuccessSound: SuccessSound.Fanfare }, "Save lost a preference");
                Check(form.Result!.CurrentPet?.FriendlyName == "Mon Pet", "Save lost the edited name");
            });
            Test("Cancel discards language preview; automatic port survives translation", () =>
            {
                using var form = new SettingsPage(config);
                Show(form);
                Find<ComboBox>(form, "Language").SelectedIndex = 1;
                Check(Find<ComboBox>(form, "SerialPort").Text == "Automatique", "Automatic port untranslated");
                Find<Button>(form, "Cancel").PerformClick();
                Check(form.Result == null && config.Language == "en", "Cancel saved the preview");
                using var saved = new SettingsPage(config);
                Show(saved);
                Find<ComboBox>(saved, "Language").SelectedIndex = 1;
                Find<Button>(saved, "Save").PerformClick();
                Check(saved.Result?.SerialPort == "auto", "Localized port leaked into protocol setting");
            });
            Test("open tray and diagnostics translate after save, and survive restart", () =>
            {
                var data = Path.Combine(root, "hub");
                var store = new ConfigStore(Path.Combine(data, "config.json"));
                store.Save(config);
                var pipe = "CodexPetUi." + Guid.NewGuid().ToString("N");
                using (var hub = new HubService(data, noSerial: true, pipeOverride: pipe))
                using (var tray = new TrayContext(hub))
                using (var diagnostics = new DiagnosticsPage(hub))
                {
                    Show(diagnostics);
                    tray.RefreshStatus();
                    Check(tray.Menu.Items.Cast<ToolStripItem>().Any(i => i.Text == "Settings…"), "English tray missing");
                    Check(Find<TextBox>(diagnostics, "Details").Text.Contains("Last Codex state: Idle"), "English diagnostics missing");
                    Render(diagnostics, "diagnostics-en.png");
                    hub.Save(config with { Language = "fr" });
                    Await(() => hub.Snapshot.Config.Language == "fr");
                    tray.RefreshStatus(); diagnostics.RefreshDetails();
                    Check(tray.Menu.Items.Cast<ToolStripItem>().Any(i => i.Text == "Paramètres…"), "Tray did not translate after save");
                    Check(Find<TextBox>(diagnostics, "Details").Text.Contains("Dernier état Codex : Au repos"), "Open diagnostics did not translate");
                    Check(Find<TextBox>(diagnostics, "Details").Text.Contains("Liaison série désactivée"), "Captured error did not translate");
                    Render(diagnostics, "diagnostics-fr.png");
                    var testMenu = tray.Menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(i => i.Text == "Tester un état");
                    Check(testMenu.DropDownItems.Cast<ToolStripItem>().Any(i => i.Text == "Réponse terminée"), "Reactions did not translate");
                    var keep = testMenu.DropDownItems.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(i => i.Text == "Conserver le test actif");
                    keep.Checked = true;
                    hub.Save(hub.Snapshot.Config with { Language = "en" });
                    Await(() => hub.Snapshot.Config.Language == "en");
                    tray.RefreshStatus(); diagnostics.RefreshDetails();
                    Check(keep.Checked && keep.Text == "Keep test active", "Language change reset test mode");
                    Check(store.Load() == config, "Saving language changed unrelated preferences");
                    hub.Save(config with { Language = "fr" });
                    Await(() => hub.Snapshot.Config.Language == "fr");
                    hub.RequestStop("test-complete");
                }
                using var restarted = new HubService(data, noSerial: true, pipeOverride: pipe);
                Check(restarted.Snapshot.Config == config with { Language = "fr" }, "Restart did not load the saved French preference");
                restarted.RequestStop("test-complete");
            });
            Test("unpaired settings fit both languages", () =>
            {
                foreach (var language in UiText.Languages)
                {
                    using var form = new SettingsPage(new HubConfig { Language = language }, successAudio: false);
                    Show(form);
                    var save = Find<Button>(form, "Save");
                    var bounds = form.RectangleToClient(save.RectangleToScreen(save.ClientRectangle));
                    Check(form.ClientRectangle.Contains(bounds), "Save button clipped");
                    Check(!Find<TextBox>(form, "PetName").Enabled, "Unpaired device can be renamed");
                    Render(form, "settings-unpaired-" + language + ".png");
                }
            });
            Test("tray navigation reuses one window, restores it, and preserves drafts", () =>
            {
                var data = Path.Combine(root, "navigation");
                var store = new ConfigStore(Path.Combine(data, "config.json"));
                store.Save(config);
                using var hub = new HubService(data, noSerial: true, pipeOverride: "CodexPetUi." + Guid.NewGuid().ToString("N"));
                using var tray = new TrayContext(hub);
                var settingsItem = tray.Menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(i => i.Text == "Settings…");
                Check(settingsItem.Font.Bold, "Default tray action is not bold");
                settingsItem.PerformClick();
                var window = tray.Window!;
                Show(window);
                Check(window.SelectedPage == HubPage.Settings, "Settings entry opened the wrong page");
                Find<TextBox>(window, "PetName").Text = "Edited name";
                Find<ComboBox>(window, "Language").SelectedIndex = 1;
                tray.Menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(i => i.Text == "Diagnostics…").PerformClick();
                Check(ReferenceEquals(window, tray.Window) && window.SelectedPage == HubPage.Diagnostics, "Diagnostics opened another window");
                Check(Find<Label>(window, "PageTitle").Text == "Diagnostic", "Window did not preview language");
                Check(Find<TextBox>(window, "Details").Lines[1] == new UiText("fr")["Disconnected"], "Connection status did not preview language");
                tray.Menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(i => i.Text == new UiText("en")["LicenseMenu"]).PerformClick();
                Check(ReferenceEquals(window, tray.Window) && window.SelectedPage == HubPage.License, "Credits opened another window");
                tray.HandleDoubleClick(MouseButtons.Right);
                Check(window.SelectedPage == HubPage.License, "Right double-click changed the selected page");
                window.WindowState = FormWindowState.Minimized;
                tray.HandleDoubleClick(MouseButtons.Left);
                Check(window.WindowState == FormWindowState.Normal && Find<TextBox>(window, "PetName").Text == "Edited name", "Restoring lost draft");
                Check(store.Load() == config, "Changing pages saved a draft");
                Find<Button>(window, "Cancel").PerformClick();
                Check(Find<TextBox>(window, "PetName").Text == "Béatrice" && Find<Label>(window, "PageTitle").Text == "Settings", "Cancel did not reset window language and fields");
                Find<ComboBox>(window, "Language").SelectedIndex = 1;
                Find<Button>(window, "Save").PerformClick();
                Await(() => hub.Snapshot.Config.Language == "fr");
                window.RefreshContent(); tray.RefreshStatus();
                Check(window.Visible && settingsItem.Text == "Paramètres…" && settingsItem.Font.Bold, "Save closed window or changed default action");
                foreach (var language in UiText.Languages)
                {
                    Find<ComboBox>(window, "Language").SelectedIndex = language == "fr" ? 1 : 0;
                    foreach (var page in Enum.GetValues<HubPage>())
                    {
                        Find<Button>(window, "Navigate" + page).PerformClick();
                        Application.DoEvents();
                        Render(window, "window-" + page.ToString().ToLowerInvariant() + "-" + language + ".png");
                    }
                }
                window.Close();
                tray.ShowPage(HubPage.Settings);
                Check(!ReferenceEquals(window, tray.Window), "Closed window was not recreated");
                Check(Find<ComboBox>(tray.Window!, "Language").SelectedIndex == 1, "Reopening lost persisted language");
                hub.RequestStop("test-complete");
            });
            Test("saving a draft preserves newer tray preferences and device identity", () =>
            {
                var current = config;
                using var page = new SettingsPage(config, currentConfig: () => current);
                Show(page);
                Find<ComboBox>(page, "Language").SelectedIndex = 1;
                Find<TextBox>(page, "PetName").Text = "Old device rename";
                current = config with { Brightness = 80, CurrentPet = new("9876-5432-ABCD", "New device") };
                Find<Button>(page, "Save").PerformClick();
                Check(page.Result is { Language: "fr", Brightness: 80 } && page.Result.CurrentPet == current.CurrentPet, "Save overwrote a newer setting or device");
            });
        }
        finally { foreach (var host in hosts) host.Dispose(); Directory.Delete(root, true); }
        Console.WriteLine($"{passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}
