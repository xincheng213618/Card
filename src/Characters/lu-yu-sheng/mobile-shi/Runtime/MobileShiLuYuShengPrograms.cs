using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MobileShiRunweiGrantIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TargetSeat, int RevealedCount, int GainedCount, bool RedGift) : IGameEvent
{
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public long SequenceAfter { get; init; }
}
public sealed record MobileShiRunweiCardGrantedEvent(long FrameId, int OwnerSeat, int TargetSeat, int CardId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;

public sealed record MobileShiRunweiDebtRefundedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int Required, int Counted) : IGameEvent
{ public ActualDiscardRecoveryPhaseKey? Phase { get; init; } }

public sealed record MobileShiRunweiPhaseEndDiscardedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int DiscardedCount, int LeftHandCount) : IGameEvent
{ public ActualDiscardRecoveryPhaseKey? Phase { get; init; } }
public sealed record MobileShiRunweiPhaseEndConsumedEvent(long FrameId, long GrantId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MobileShiShuanghuaiDrawReturnedEvent(long FrameId, int Cursor, int RecipientSeat, int ActualCount) : IGameEvent;
public sealed record MobileShiShuanghuaiHpPaidEvent(long FrameId, int OwnerSeat, int Before, int After) : IGameEvent;

public sealed record MobileShiShuanghuaiGuardIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int VictimSeat, string Option, bool SameAsLast) : IGameEvent;

public sealed record MobileShiLuYuShengReceipt(string Operation, int ActualTurn, int Stage = 0, int TargetSeat = -1,
    int RevealedCount = 0, long CardId = 0, int LostCount = 0, int Discarded = 0)
{
    public CardConversionSource? Source { get; init; }
    public string GameplayHash { get; init; } = "";
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public long GrantId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool SameAsLast { get; init; }
    public bool HpPaid { get; init; }
    public bool HpAppearance { get; init; }
    public int DrawCursor { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiLuYuShengReceipt? MobileShiLuYuSheng { get; init; }
}

internal static class MobileShiLuYuShengComposition
{
    internal const string RunweiSkill = "mobile:shi-runwei";
    internal const string ShuanghuaiSkill = "mobile:shi-shuanghuai";
    internal const string RunweiActivation = "reveal-grant";
    internal const string RunweiLossDebtTrigger = "loss-debt";
    internal const string RunweiPhaseEndTrigger = "phase-end-return";
    internal const string ShuanghuaiGuardTrigger = "shuanghuai-guard";
    internal const string RunweiRevealBind = "runwei-revealed";
    internal const string RunweiGrantMoveReason = "skill-program.mobile:shi-runwei.grant";
    internal const string RunweiLeftoverMoveReason = "skill-program.mobile:shi-runwei.leftover";
    internal const string RunweiPhaseEndMoveReason = "skill-program.mobile:shi-runwei.phase-end";
    internal const string ShuanghuaiPeachMoveReason = "skill-program.mobile:shi-shuanghuai.peach";
    internal const string ShuanghuaiDrawMoveReason = "skill-program.mobile:shi-shuanghuai.draw";

    [ProgramCatalogValidation(67400)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == RunweiSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.BooleanStates.Count != 0 ||
                program.Activations is not [{ Id: RunweiActivation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                    TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null }] ||
                program.Activations[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiRunweiGrant, Target: SkillProgramEffectTarget.Owner }] ||
                program.Triggers is not
                [
                    { Id: RunweiLossDebtTrigger, Window: SkillProgramTriggerWindow.CardsMoved, Subject: SkillProgramTriggerSubject.Owner,
                        Optional: false, MovementOccurrence: SkillProgramMovementOccurrence.PerOwnerBatch },
                    { Id: RunweiPhaseEndTrigger, Window: SkillProgramTriggerWindow.ActualPhaseEnded, Subject: SkillProgramTriggerSubject.Owner,
                        Optional: false }
                ] ||
                program.Triggers[0].SourceZones is not [CardZoneKind.Hand, CardZoneKind.Equipment] ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiRunweiLossDebt, Target: SkillProgramEffectTarget.Owner }] ||
                program.Triggers[1].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiRunweiPhaseEnd, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Runwei requires exactly its reveal-grant activation plus the loss-debt and phase-end locked triggers.");
            return;
        }
        if (program.Id != ShuanghuaiSkill) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.Contributions.Count != 0 || program.BooleanStates.Count != 0 || program.Activations.Count != 0 ||
            program.Triggers is not [{ Id: ShuanghuaiGuardTrigger }] ||
            program.Triggers[0] is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, Subject: SkillProgramTriggerSubject.Any,
                Optional: true, UsageScope: SkillUsageScope.Turn, UsageLimit: 1 } ||
            program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path + ": Shuanghuai requires exactly its once-per-turn before-damage guard trigger.");
    }
}

internal interface IMobileShiLuYuShengProgramHost
{
    SkillProgramStepOutcome ResolveRunweiGrant(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveRunweiLossDebt(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveRunweiPhaseEnd(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveShuanghuaiGuard(ProgramSkillFrame frame);
}

internal sealed class ResolveMobileShiRunweiGrantDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiRunweiGrant;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiLuYuShengHandler(SkillProgramEffectOp.ResolveMobileShiRunweiGrant);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard, Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveMobileShiRunweiLossDebtDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiRunweiLossDebt;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiLuYuShengHandler(SkillProgramEffectOp.ResolveMobileShiRunweiLossDebt);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard, Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveMobileShiRunweiPhaseEndDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiRunweiPhaseEnd;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiLuYuShengHandler(SkillProgramEffectOp.ResolveMobileShiRunweiPhaseEnd);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard, Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.ActualPhaseEnded)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
}

internal sealed class ResolveMobileShiShuanghuaiGuardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiLuYuShengHandler(SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard, Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class MobileShiLuYuShengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiRunweiLossDebt => host.GetCapability<IMobileShiLuYuShengProgramHost>().ResolveRunweiLossDebt(frame),
        SkillProgramEffectOp.ResolveMobileShiRunweiPhaseEnd => host.GetCapability<IMobileShiLuYuShengProgramHost>().ResolveRunweiPhaseEnd(frame),
        SkillProgramEffectOp.ResolveMobileShiShuanghuaiGuard => host.GetCapability<IMobileShiLuYuShengProgramHost>().ResolveShuanghuaiGuard(frame),
        _ => host.GetCapability<IMobileShiLuYuShengProgramHost>().ResolveRunweiGrant(frame)
    };
}
