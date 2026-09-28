// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal enum HubPage { Settings, Diagnostics, License }

internal sealed class HubWindow : Form
{
    private readonly HubService hub;
    private readonly SettingsPage settings;
    private readonly DiagnosticsPage diagnostics;
    private readonly LicensePage license;
    private readonly Label heading = new() { Name = "PageTitle", AutoSize = true, Margin = new Padding(0, 0, 0, 20) };
    private readonly Label subtitle = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 4, 8, 22) };
    private readonly Dictionary<HubPage, Button> navigation = [];
    private readonly Dictionary<HubPage, Control> pages;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private UiText text;
    internal HubPage SelectedPage { get; private set; }

    public HubWindow(HubService hub)
    {
        this.hub = hub;
        text = new(hub.Snapshot.Config.Language);
        Text = "CodexPetHub";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(900, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 216));
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        var sidebar = new TableLayoutPanel { Name = "Navigation", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            BackColor = Color.FromArgb(244, 246, 249), Padding = new Padding(16, 24, 16, 16), Margin = Padding.Empty };
        sidebar.ColumnStyles.Add(new(SizeType.Percent, 100));
        var brand = new Label { Text = "CodexPetHub", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(8, 0, 8, 0) };
        sidebar.Controls.Add(brand, 0, 0);
        sidebar.Controls.Add(subtitle, 0, 1);
        foreach (var page in Enum.GetValues<HubPage>())
        {
            var selected = page;
            var button = new Button { Name = "Navigate" + page, Dock = DockStyle.Top, Height = 54, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 8, 0), Margin = new Padding(0, 3, 0, 3), UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectPage(selected);
            navigation.Add(page, button);
            sidebar.Controls.Add(button, 0, (int)page + 2);
        }
        sidebar.RowStyles.Add(new(SizeType.AutoSize));
        sidebar.RowStyles.Add(new(SizeType.AutoSize));
        foreach (var _ in navigation) sidebar.RowStyles.Add(new(SizeType.AutoSize));
        sidebar.RowStyles.Add(new(SizeType.Percent, 100));
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(28, 24, 24, 24), Margin = Padding.Empty };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.AutoSize));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        heading.Font = new Font(Font.FontFamily, 20, FontStyle.Bold);
        content.Controls.Add(heading, 0, 0);
        var pageHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        content.Controls.Add(pageHost, 0, 1);
        settings = new(hub.Snapshot.Config, hub.Snapshot.Device?.SuccessAudio, () => hub.Snapshot.Config);
        diagnostics = new(hub);
        license = new(text);
        pages = new() { [HubPage.Settings] = settings, [HubPage.Diagnostics] = diagnostics, [HubPage.License] = license };
        foreach (var page in pages.Values) pageHost.Controls.Add(page);
        settings.SaveRequested += hub.Save;
        settings.LanguagePreviewChanged += ApplyLanguage;
        layout.Controls.Add(sidebar, 0, 0);
        layout.Controls.Add(content, 1, 0);
        Controls.Add(layout);
        timer.Tick += (_, _) => RefreshContent();
        timer.Start();
        ApplyLanguage(text.Language);
        SelectPage(HubPage.Settings);
    }

    private static string Key(HubPage page) => page switch
    {
        HubPage.Settings => "SettingsPage", HubPage.Diagnostics => "DiagnosticsPage", _ => "LicensePage"
    };

    internal void SelectPage(HubPage selected)
    {
        SelectedPage = selected;
        foreach (var (page, control) in pages) control.Visible = page == selected;
        pages[selected].BringToFront();
        foreach (var (page, button) in navigation)
        {
            button.BackColor = page == selected ? Color.FromArgb(222, 235, 249) : Color.FromArgb(244, 246, 249);
            button.ForeColor = page == selected ? Color.FromArgb(0, 83, 155) : SystemColors.ControlText;
            button.AccessibleDescription = page == selected ? text["SelectedPage"] : null;
        }
        heading.Text = text[Key(selected)];
        Text = "CodexPetHub · " + heading.Text;
        AcceptButton = selected == HubPage.Settings ? (Button)settings.Controls.Find("Save", true).Single() : null;
        CancelButton = selected == HubPage.Settings ? (Button)settings.Controls.Find("Cancel", true).Single() : null;
    }

    private void ApplyLanguage(string language)
    {
        text = new(language);
        foreach (var (page, button) in navigation) button.Text = text[Key(page)];
        subtitle.Text = text["HubSubtitle"];
        license.ApplyLanguage(text);
        diagnostics.RefreshDetails(language);
        SelectPage(SelectedPage);
    }

    internal void RefreshContent()
    {
        settings.Synchronize(hub.Snapshot.Config);
        if (text.Language != settings.PreviewLanguage) ApplyLanguage(settings.PreviewLanguage);
        diagnostics.RefreshDetails(text.Language);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
