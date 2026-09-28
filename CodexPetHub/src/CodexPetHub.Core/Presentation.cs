// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CodexPetHub.Core;

public enum BaseState { Idle, Thinking, Working, WaitingForUser, Sleeping }
public enum ReactionKind { Success, Error, Stop, Wake }
// Expression is the graphical BASE. The Hub owns reaction priority and timing.
public record Presentation(string Expression, bool Sleeping, int Brightness, string? Reaction = null, long Revision = 0,
    SuccessSound SuccessSound = SuccessSound.None);

public sealed class PresentationState
{
    public BaseState Codex { get; private set; } = BaseState.Idle;
    public bool PluginConnected { get; private set; }
    public DateTimeOffset? LastEventAt { get; private set; }
    public bool Locked { get; set; }
    public bool DisplayOff { get; set; }
    public bool Suspended { get; set; }
    public bool ManualSleep { get; set; }
    public ReactionKind? Reaction { get; private set; }
    private DateTimeOffset reactionUntil;
    private string? successBase;
    private string? producer;
    private long sequence = -1, revision;
    private bool wasSleeping;
    private string? testExpression;
    private DateTimeOffset testUntil;
    public bool ManualRecipe { get; set; }

    public static string Map(BaseState state) => state switch
    {
        BaseState.Idle => "idle", BaseState.Thinking => "thinking",
        BaseState.Working => "working", BaseState.WaitingForUser => "waiting",
        BaseState.Sleeping => "sleepy", _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
    public static int Duration(ReactionKind value) => value switch
    { ReactionKind.Success => 5000, ReactionKind.Error => 3000, ReactionKind.Stop => 900, ReactionKind.Wake => 2000, _ => 0 };
    public void Receive(IpcEvent message, DateTimeOffset now)
    {
        if (message.State is not { } value) return;
        PluginConnected = true;
        if (producer == message.ProducerId && message.Sequence <= sequence) return;
        producer = message.ProducerId; sequence = message.Sequence;
        Codex = value; LastEventAt = message.Timestamp;
        if (ManualRecipe) return; // Still track live Codex while the user judges a scene.
        // Success is a complete performance. Keep tracking the latest base,
        // but neither restart the animation nor enqueue incoming reactions.
        if (Reaction == ReactionKind.Success && now < reactionUntil) return;
        testExpression = null; Reaction = null; revision++;
        successBase = null;
        if (message.Reaction is { } reaction) StartReaction(reaction, now);
    }
    public void DisconnectPlugin()
    {
        PluginConnected = false; Codex = BaseState.Idle; producer = null; sequence = -1;
        if (!ManualRecipe && Reaction != ReactionKind.Success) { Reaction = null; testExpression = null; revision++; }
    }
    private void StartReaction(ReactionKind reaction, DateTimeOffset now)
    {
        if (Reaction == ReactionKind.Success && now < reactionUntil) return;
        successBase = reaction == ReactionKind.Success ? testExpression ?? Map(Codex) : null;
        Reaction = reaction; reactionUntil = now.AddMilliseconds(Duration(reaction)); revision++;
    }
    public void TestReaction(ReactionKind reaction, DateTimeOffset now) => StartReaction(reaction, now);
    public void Test(string expression, DateTimeOffset now)
    {
        if (!SerialProtocol.Expressions.Contains(expression)) throw new ArgumentException("Unknown expression.");
        testExpression = expression; testUntil = now.AddSeconds(10); Reaction = null; revision++;
    }
    public void EndRecipe() { ManualRecipe = false; testExpression = null; Reaction = null; revision++; }
    public void CancelReaction() { if (Reaction != null) { Reaction = null; revision++; } }
    public Presentation Resolve(HubConfig config, DateTimeOffset now)
    {
        bool sleeping = ManualSleep || Suspended || (config.SleepOnWindowsLock && Locked) || (config.SleepOnDisplayOff && DisplayOff);
        if (sleeping) { if (!wasSleeping) revision++; Reaction = null; }
        else if (wasSleeping) StartReaction(ReactionKind.Wake, now);
        wasSleeping = sleeping;
        if (Reaction != null && now >= reactionUntil) { Reaction = null; successBase = null; revision++; }
        if (testExpression != null && !ManualRecipe && now >= testUntil) { testExpression = null; revision++; }
        return new(Reaction == ReactionKind.Success ? successBase! : testExpression ?? Map(Codex), sleeping, config.Brightness, Reaction?.ToString().ToLowerInvariant(), revision, config.SuccessSound);
    }
}
