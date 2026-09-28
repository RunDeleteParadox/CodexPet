// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;

namespace CodexPetHub.Windows;

// Hidden top-level HWND receives broadcast suspend/resume and registered session events.
internal sealed class WindowsEvents : Form
{
    private readonly HubService hub;
    private nint displayRegistration;
    private bool sessionRegistered;
    public event Action? SessionEnding;
    private static readonly Guid DisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

    public WindowsEvents(HubService hub)
    {
        this.hub = hub;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        _ = Handle;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        sessionRegistered = WTSRegisterSessionNotification(Handle, 0);
        var guid = DisplayStatus;
        displayRegistration = RegisterPowerSettingNotification(Handle, ref guid, 0);
        if (!sessionRegistered || displayRegistration == 0)
            hub.Log("Some Windows notifications could not be registered; manual sleep remains available.");
        RefreshLockState();
    }

    private void RefreshLockState()
    {
        // Also reconcile on resume if Windows coalesced a session notification.
        if (WTSQuerySessionInformation(0, -1, 25, out var data, out var size))
        {
            try
            {
                // WTSINFOEX: DWORD Level, aligned WTSINFOEX_LEVEL1 (session id, state, flags).
                if (size >= 20 && Marshal.ReadInt32(data) == 1)
                    hub.WindowsLock(Marshal.ReadInt32(data, 16) == 0);
            }
            finally { WTSFreeMemory(data); }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x02B1) // WM_WTSSESSION_CHANGE
        {
            if ((int)m.WParam == 7) hub.WindowsLock(true);
            if ((int)m.WParam == 8) hub.WindowsLock(false);
        }
        if (m.Msg == 0x0218) // WM_POWERBROADCAST
        {
            if ((int)m.WParam == 4) hub.Suspend(true);
            if ((int)m.WParam is 7 or 18) { hub.Suspend(false); RefreshLockState(); }
            if ((int)m.WParam == 0x8013 && m.LParam != 0 &&
                Marshal.PtrToStructure<Guid>(m.LParam) == DisplayStatus && Marshal.ReadInt32(m.LParam, 16) == 4)
                hub.Display(Marshal.ReadInt32(m.LParam, 20) == 0);
        }
        if (m.Msg == 0x0011) hub.Log("Windows session shutdown queried."); // WM_QUERYENDSESSION can be cancelled.
        if (m.Msg == 0x0016 && m.WParam != 0) // WM_ENDSESSION confirmed.
        {
            hub.RequestStop("windows-session-end");
            SessionEnding?.Invoke();
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (sessionRegistered) WTSUnRegisterSessionNotification(Handle);
        if (displayRegistration != 0) UnregisterPowerSettingNotification(displayRegistration);
        base.OnHandleDestroyed(e);
    }

    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(nint hWnd, int flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint hWnd);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)] private static extern bool WTSQuerySessionInformation(nint server, int session, int infoClass, out nint data, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint memory);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid setting, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(nint handle);
}
