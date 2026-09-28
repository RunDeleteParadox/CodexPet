// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using CodexPetHub.Core;

namespace CodexPetHub.Windows;

internal sealed class SettingsPage : UserControl
{
    private HubConfig original;
    private readonly Func<HubConfig> currentConfig;
    private readonly ComboBox language = new() { Name = "Language", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox name = new() { Name = "PetName", MaxLength = 64, Dock = DockStyle.Fill };
    private readonly ComboBox port = new() { Name = "SerialPort", Dock = DockStyle.Fill };
    private readonly NumericUpDown brightness = new() { Minimum = 0, Maximum = 100, Dock = DockStyle.Fill };
    private readonly NumericUpDown reconnect = new() { Minimum = 1, Maximum = 60, Dock = DockStyle.Fill };
    private readonly ComboBox successSound = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox startup = new() { AutoSize = true };
    private readonly CheckBox lockSleep = new() { AutoSize = true };
    private readonly CheckBox displaySleep = new() { AutoSize = true };
    private readonly CheckBox forget = new() { AutoSize = true };
    private readonly List<Action<UiText>> translations = [];
    private UiText text;
    private bool applyingLanguage;
    public HubConfig? Result { get; private set; }
    public event Action<HubConfig>? SaveRequested;
    public event Action<string>? LanguagePreviewChanged;
    public string PreviewLanguage => text.Language;
    public bool HasChanges => ReadDraft() != original;
    private readonly Button save = new() { Name = "Save", AutoSize = true };
    private readonly Button cancel = new() { Name = "Cancel", AutoSize = true };

    public SettingsPage(HubConfig config, bool? successAudio = null, Func<HubConfig>? currentConfig = null)
    {
        original = config;
        this.currentConfig = currentConfig ?? (() => original);
        text = new(config.Language);
        Name = "SettingsPage";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 8, 8, 20), ColumnCount = 2 };
        layout.ColumnStyles.Add(new(SizeType.AutoSize));
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);
        void Bind(Control control, string key) => translations.Add(t => control.Text = t[key]);
        void Row(string key, Control control, int row)
        {
            var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 18, 8) };
            Bind(label, key);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }
        void Wide(Control control, int row)
        {
            control.Margin = new Padding(3, 8, 3, 8);
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }
        language.Items.AddRange([text["LanguageEnglish"], text["LanguageFrench"]]);
        language.SelectedIndex = config.Language == "fr" ? 1 : 0;
        Row("Language", language, 0);
        name.Text = config.CurrentPet?.FriendlyName ?? "CodexPet";
        name.Enabled = config.CurrentPet != null;
        Row("PetName", name, 1);
        port.Items.Add(text["AutomaticPort"]);
        port.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames().Order().Cast<object>().ToArray());
        port.Text = config.SerialPort == "auto" ? text["AutomaticPort"] : config.SerialPort;
        Row("SerialPort", port, 2);
        brightness.Value = config.Brightness;
        Row("BrightnessLabel", brightness, 3);
        reconnect.Value = config.ReconnectInterval;
        Row("ReconnectLabel", reconnect, 4);
        successSound.Items.AddRange(Enum.GetValues<SuccessSound>().Select(text.Sound).Cast<object>().ToArray());
        successSound.SelectedIndex = (int)config.SuccessSound;
        Row("SuccessSound", successSound, 5);
        var soundHint = new Label { AutoSize = true, MaximumSize = new Size(610, 0), ForeColor = SystemColors.GrayText };
        Bind(soundHint, successAudio == false ? "SoundUpdateHint" : "SoundHint");
        Wide(soundHint, 6);
        startup.Checked = config.LaunchOnStartup;
        lockSleep.Checked = config.SleepOnWindowsLock;
        displaySleep.Checked = config.SleepOnDisplayOff;
        var checks = new[] { (startup, "LaunchOnStartup"), (lockSleep, "SleepOnLock"), (displaySleep, "SleepOnDisplayOff"), (forget, "ForgetPet") };
        for (var i = 0; i < checks.Length; i++) { Bind(checks[i].Item1, checks[i].Item2); Wide(checks[i].Item1, i + 7); }
        forget.Enabled = config.CurrentPet != null;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        Bind(save, "Save"); Bind(cancel, "Cancel");
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Wide(buttons, 11);
        if (config.CurrentPet == null)
        {
            var hint = new Label { AutoSize = true, MaximumSize = new Size(610, 0), ForeColor = SystemColors.GrayText };
            Bind(hint, "NameHint"); Wide(hint, 12);
        }
        language.SelectedIndexChanged += (_, _) => ApplyLanguage();
        ApplyLanguage();
        save.Click += (_, _) =>
        {
            try
            {
                var candidate = MergeEdits(this.currentConfig());
                candidate.Validate();
                SaveRequested?.Invoke(candidate);
                LoadConfig(candidate);
                Result = candidate;
            }
            catch (InvalidDataException ex) { MessageBox.Show(this, UiMessage.From(ex).Render(text), text["InvalidSettings"], MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        cancel.Click += (_, _) => LoadConfig(this.currentConfig());
    }

    private HubConfig ReadDraft() => original with
    {
        Language = text.Language,
        SerialPort = IsAutomaticPort() ? "auto" : port.Text.Trim().ToUpperInvariant(),
        Brightness = (int)brightness.Value, ReconnectInterval = (int)reconnect.Value,
        SuccessSound = (SuccessSound)successSound.SelectedIndex,
        LaunchOnStartup = startup.Checked, SleepOnWindowsLock = lockSleep.Checked,
        SleepOnDisplayOff = displaySleep.Checked,
        CurrentPet = forget.Checked || original.CurrentPet == null ? null : original.CurrentPet with { FriendlyName = name.Text.Trim() }
    };

    // A tray action or newly paired device can change preferences while this page is open.
    private HubConfig MergeEdits(HubConfig current)
    {
        var draft = ReadDraft();
        return current with
        {
            Language = draft.Language != original.Language ? draft.Language : current.Language,
            SerialPort = draft.SerialPort != original.SerialPort ? draft.SerialPort : current.SerialPort,
            Brightness = draft.Brightness != original.Brightness ? draft.Brightness : current.Brightness,
            ReconnectInterval = draft.ReconnectInterval != original.ReconnectInterval ? draft.ReconnectInterval : current.ReconnectInterval,
            SuccessSound = draft.SuccessSound != original.SuccessSound ? draft.SuccessSound : current.SuccessSound,
            LaunchOnStartup = draft.LaunchOnStartup != original.LaunchOnStartup ? draft.LaunchOnStartup : current.LaunchOnStartup,
            SleepOnWindowsLock = draft.SleepOnWindowsLock != original.SleepOnWindowsLock ? draft.SleepOnWindowsLock : current.SleepOnWindowsLock,
            SleepOnDisplayOff = draft.SleepOnDisplayOff != original.SleepOnDisplayOff ? draft.SleepOnDisplayOff : current.SleepOnDisplayOff,
            CurrentPet = draft.CurrentPet != original.CurrentPet && current.CurrentPet?.DeviceId == original.CurrentPet?.DeviceId ? draft.CurrentPet : current.CurrentPet
        };
    }

    internal void Synchronize(HubConfig config)
    {
        if (Result == config) Result = null;
        if (!HasChanges && Result == null && config != original) LoadConfig(config);
    }

    private void LoadConfig(HubConfig config)
    {
        applyingLanguage = true;
        original = config;
        Result = null;
        language.SelectedIndex = config.Language == "fr" ? 1 : 0;
        name.Text = config.CurrentPet?.FriendlyName ?? "CodexPet";
        name.Enabled = forget.Enabled = config.CurrentPet != null;
        port.Text = config.SerialPort == "auto" ? text["AutomaticPort"] : config.SerialPort;
        brightness.Value = config.Brightness;
        reconnect.Value = config.ReconnectInterval;
        successSound.SelectedIndex = (int)config.SuccessSound;
        startup.Checked = config.LaunchOnStartup;
        lockSleep.Checked = config.SleepOnWindowsLock;
        displaySleep.Checked = config.SleepOnDisplayOff;
        forget.Checked = false;
        applyingLanguage = false;
        ApplyLanguage();
    }

    private bool IsAutomaticPort() => port.Text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) || port.Text == text["AutomaticPort"];

    private void ApplyLanguage()
    {
        if (applyingLanguage) return;
        applyingLanguage = true;
        SuspendLayout();
        try
        {
            var automatic = IsAutomaticPort();
            var savedPort = port.Text;
            var sound = successSound.SelectedIndex;
            text = new(language.SelectedIndex == 1 ? "fr" : "en");
            Text = text["SettingsTitle"];
            foreach (var translate in translations) translate(text);
            language.Items[0] = text["LanguageEnglish"];
            language.Items[1] = text["LanguageFrench"];
            port.Items[0] = text["AutomaticPort"];
            port.Text = automatic ? text["AutomaticPort"] : savedPort;
            foreach (var value in Enum.GetValues<SuccessSound>()) successSound.Items[(int)value] = text.Sound(value);
            successSound.SelectedIndex = sound;
        }
        finally { ResumeLayout(true); applyingLanguage = false; }
        LanguagePreviewChanged?.Invoke(text.Language);
    }
}
