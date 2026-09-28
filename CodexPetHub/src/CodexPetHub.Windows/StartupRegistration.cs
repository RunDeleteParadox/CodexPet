// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Win32;

namespace CodexPetHub.Windows;

internal static class StartupRegistration
{
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key.SetValue("CodexPetHub", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("CodexPetHub", false);
    }
}
