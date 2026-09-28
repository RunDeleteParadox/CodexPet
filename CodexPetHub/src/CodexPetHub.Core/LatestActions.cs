// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CodexPetHub.Core;

// One slot per authority. New snapshots replace pending snapshots; heartbeats
// never enter this inbox. Taking a batch cannot starve serial/health work.
public enum HubInput { Plugin, PluginSuccess, Lock, Display, Suspend, ManualSleep, Reconnect, Settings, Test, Recipe }

public sealed class LatestActions
{
    private readonly object gate = new();
    private readonly Dictionary<HubInput, (long Order, Action Action)> pending = [];
    private long sequence;
    public int Count { get { lock (gate) return pending.Count; } }
    public bool Post(HubInput key, Action action)
    {
        lock (gate)
        {
            var replaced = pending.ContainsKey(key);
            pending[key] = (++sequence, action);
            return replaced;
        }
    }
    public Action[] Take()
    {
        lock (gate)
        {
            var result = pending.Values.OrderBy(x => x.Order).Select(x => x.Action).ToArray();
            pending.Clear();
            return result;
        }
    }
}
