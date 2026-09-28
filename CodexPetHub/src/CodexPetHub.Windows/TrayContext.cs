// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal sealed class TrayContext : ApplicationContext
{
    private readonly HubService hub;
    private readonly NotifyIcon tray;
    private readonly WindowsEvents events;
    private readonly Icon icon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly ToolStripMenuItem title = new("CodexPet") { Enabled = false };
    private readonly ToolStripMenuItem connection = new() { Enabled = false };
    private readonly ToolStripMenuItem codex = new() { Enabled = false };
    private readonly ToolStripMenuItem pet = new() { Enabled = false };
    private HubWindow? window;
    internal HubWindow? Window => window;
    private readonly List<Action<UiText>> translations = [];
    private UiText text;
    internal ContextMenuStrip Menu => tray.ContextMenuStrip!;

    public TrayContext(HubService hub)
    {
        this.hub = hub;
        text = new(hub.Snapshot.Config.Language);
        events = new(hub);
        events.SessionEnding += ExitThread;
        icon = CreateIcon();
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([title, connection, codex, pet, new ToolStripSeparator()]);
        ToolStripMenuItem Item(ToolStripItemCollection items, string key, EventHandler? click = null)
        {
            var item = new ToolStripMenuItem(text[key], null, click);
            translations.Add(t => item.Text = t[key]);
            items.Add(item);
            return item;
        }
        var tests = Item(menu.Items, "TestState");
        foreach (var value in Enum.GetValues<BaseState>().Where(v => v != BaseState.Sleeping))
            Item(tests.DropDownItems, "State" + value, (_, _) => hub.Test(value));
        foreach (var value in Enum.GetValues<ReactionKind>())
            Item(tests.DropDownItems, "Reaction" + value, (_, _) => hub.TestReaction(value));
        var recipe = Item(tests.DropDownItems, "KeepTest");
        recipe.CheckOnClick = true;
        recipe.CheckedChanged += (_, _) => hub.Recipe(recipe.Checked);
        var expressions = Item(tests.DropDownItems, "AllExpressions");
        foreach (var value in SerialProtocol.Expressions)
            Item(expressions.DropDownItems, "Expression_" + value, (_, _) => hub.TestExpression(value));
        var brightness = Item(menu.Items, "Brightness");
        foreach (var value in new[] { 0, 20, 40, 60, 80, 100 })
            brightness.DropDownItems.Add(value + " %", null, (_, _) => hub.Save(hub.Snapshot.Config with { Brightness = value }));
        Item(menu.Items, "SleepPet", (_, _) => hub.Sleep(true));
        Item(menu.Items, "WakePet", (_, _) => hub.Sleep(false));
        Item(menu.Items, "Reconnect", (_, _) => hub.Reconnect());
        menu.Items.Add(new ToolStripSeparator());
        var settings = Item(menu.Items, "Settings", (_, _) => ShowPage(HubPage.Settings));
        settings.Font = new Font(settings.Font, FontStyle.Bold);
        Item(menu.Items, "Diagnostics", (_, _) => ShowPage(HubPage.Diagnostics));
        Item(menu.Items, "LicenseMenu", (_, _) => ShowPage(HubPage.License));
        menu.Items.Add(new ToolStripSeparator());
        Item(menu.Items, "Quit", (_, _) => { hub.RequestStop("user-quit"); ExitThread(); });
        tray = new NotifyIcon { Icon = icon, Text = "CodexPetHub", ContextMenuStrip = menu, Visible = true };
        tray.MouseDoubleClick += (_, e) => HandleDoubleClick(e.Button);
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();
        RefreshStatus();
    }

    internal void HandleDoubleClick(MouseButtons button)
    {
        if (button == MouseButtons.Left) ShowPage(HubPage.Settings);
    }
    internal void ShowPage(HubPage page)
    {
        if (window == null || window.IsDisposed) window = new(hub);
        window.RefreshContent();
        window.SelectPage(page);
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Show();
        window.Activate();
    }
    internal void RefreshStatus()
    {
        var s = hub.Snapshot;
        if (text.Language != s.Config.Language)
        {
            text = new(s.Config.Language);
            System.Globalization.CultureInfo.CurrentUICulture = text.Culture;
            foreach (var translate in translations) translate(text);
        }
        title.Text = s.Config.CurrentPet?.FriendlyName ?? "CodexPet";
        connection.Text = HubService.HealthLabel(s);
        codex.Text = text.Format("CodexStatus", text.State(s.CodexState));
        pet.Text = text.Format("PetStatus", s.Health.Loop is "faulted" or "stalled" ? text["StaleState"] : text.Expression(s.Displayed?.State));
        var tooltip = title.Text + " · " + connection.Text;
        tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillEllipse(Brushes.Black, 0, 0, 31, 31);
        g.FillEllipse(Brushes.White, 6, 10, 7, 12);
        g.FillEllipse(Brushes.White, 19, 10, 7, 12);
        var handle = bitmap.GetHicon();
        try { using var source = Icon.FromHandle(handle); return (Icon)source.Clone(); }
        finally { DestroyIcon(handle); }
    }
    protected override void ExitThreadCore()
    {
        timer.Stop();
        tray.Visible = false;
        window?.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tray.Dispose(); events.Dispose(); window?.Dispose(); icon.Dispose(); }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
