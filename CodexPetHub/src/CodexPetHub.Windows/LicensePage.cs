// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal sealed class LicensePage : UserControl
{
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Label summary = new() { Name = "LicenseSummary", Dock = DockStyle.Top, Height = 90 };
    private readonly string credits;
    public LicensePage(UiText text)
    {
        Name = "LicensePage";
        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        string Read(string file)
        {
            try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return text.Format("LegalMissing", file); }
        }
        void Page(string key, string contents)
        {
            var page = new TabPage { Name = key + "Page", Tag = key };
            page.Controls.Add(new TextBox { Name = key, Dock = DockStyle.Fill, Multiline = true,
                ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Text = contents.ReplaceLineEndings(Environment.NewLine) });
            tabs.TabPages.Add(page);
        }
        credits = string.Join(Environment.NewLine + Environment.NewLine, new[] { "NOTICE", "THIRD_PARTY_NOTICES.md", "SOURCE.md" }.Select(Read));
        Page("LicenseTab", Read("LICENSE"));
        Page("CreditsTab", credits);
        Controls.Add(tabs);
        Controls.Add(summary);
        ApplyLanguage(text);
    }
    internal void ApplyLanguage(UiText text)
    {
        Text = text["LicenseTitle"];
        summary.Text = text["LicenseSummary"];
        foreach (TabPage page in tabs.TabPages) page.Text = text[(string)page.Tag!];
        var box = (TextBox)tabs.TabPages[1].Controls[0];
        box.Text = (text["FirmwareCredit"] + Environment.NewLine + Environment.NewLine + credits).ReplaceLineEndings(Environment.NewLine);
        box.Select(0, 0);
    }
}
