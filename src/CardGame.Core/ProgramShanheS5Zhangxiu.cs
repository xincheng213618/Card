using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShanheS5XiongluanPayment(CardConversionSource Source, string GameplayHash, int TargetSeat,
    int ActualTurnNumber, long PhaseInstanceId, EquipmentSlot Slot, int CandidateCount,
    uint RandomBefore, uint RandomAfter, bool JudgmentWasAbolished,
    long MovementBefore, long MovementAfter, bool Paid, bool ChildrenCompleted = false);
public sealed record ShanheS5XiongluanCostPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int ActualTurnNumber, long PhaseInstanceId, EquipmentSlot Slot, int CandidateCount,
    bool JudgmentWasAbolished, long MovementBefore, long MovementAfter) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5XiongluanPayment? ShanheS5Xiongluan { get; init; }
}

public sealed record ShanheLeizhenHandUseCountedEvent(long ActionId, int OwnerSeat, int WeaponCardId,
    long EquipmentEntrySequence, int MaterialCount, int RemainderBefore, int RemainderAfter) : IGameEvent;
public sealed record ShanheLeizhenChargeEarnedEvent(long ActionId, int OwnerSeat, int WeaponCardId,
    long EquipmentEntrySequence) : IGameEvent;
public sealed record ShanheLeizhenChargeSpentEvent(long EarningActionId, int OwnerSeat, int WeaponCardId,
    long UseFrameId) : IGameEvent;
public sealed record ShanheLeizhenSlashReceipt(long EarningActionId, int OwnerSeat, int WeaponCardId,
    long UseFrameId, bool BaseApplied = false, int? BaseBefore = null, int? BaseAfter = null);
public sealed record ShanheLeizhenBaseAppliedEvent(long UseFrameId, int OwnerSeat, int BaseBefore, int BaseAfter) : IGameEvent;
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheLeizhenSlashReceipt? ShanheLeizhen { get; init; }
}

internal interface IShanheS5ZhangxiuHost
{
    SkillProgramStepOutcome PayShanheS5Xiongluan(ProgramSkillFrame frame);
    void ResolveShanheLeizhenUse(ProgramSkillFrame frame);
}
internal sealed class PayShanheS5XiongluanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayShanheS5Xiongluan;
    public override ISkillProgramEffectHandler Handler { get; } = new PayShanheS5XiongluanHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardOwnedZoneCards,
        static (_, c) => c.PublicControlValue(-3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
internal sealed class ResolveShanheLeizhenUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheLeizhenUse;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheLeizhenUseHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireCardActionActor()];
}
public sealed class PayShanheS5XiongluanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PayShanheS5Xiongluan;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IShanheS5ZhangxiuHost>().PayShanheS5Xiongluan(f);
}
public sealed class ResolveShanheLeizhenUseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheLeizhenUse;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    { h.GetCapability<IShanheS5ZhangxiuHost>().ResolveShanheLeizhenUse(f); return SkillProgramStepOutcome.Continue; }
}
internal static class ShanheS5ZhangxiuContract
{
    [ProgramCatalogValidation(59401)]
    internal static void Validate(string path, SkillProgram p)
    {
        var cost = SkillProgramEffectOp.PayShanheS5Xiongluan;
        var counter = SkillProgramEffectOp.ResolveShanheLeizhenUse;
        if (p.Activations.Any(a => a.Effects.Any(e => e.Op == counter)) ||
            p.Triggers.Any(t => t.Effects.Any(e => e.Op == cost)))
            throw new InvalidOperationException(path + ": Zhang Xiu's area cost is an activation; Leizhen counting is a real committed-use trigger.");
        foreach (var a in p.Activations.Where(a => a.Effects.Any(e => e.Op == cost)))
            if (p.Id != "shanhe:s5-xiongluan-14116" || a.MinCards != 0 || a.MaxCards != 0 ||
                a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
                a.UsesPerPhase != 1 || a.UsesPerGame is not null || a.Effects is not
                    [{ Op: SkillProgramEffectOp.PayShanheS5Xiongluan },
                     { Op: SkillProgramEffectOp.GrantDirectedTurnCardPolicy, ActorReference.Kind: ProgramParticipantRef.Owner,
                       TargetReference.Kind: ProgramParticipantRef.SelectedTarget } directed,
                     { Op: SkillProgramEffectOp.GrantTurnHandCardProhibition, Target: SkillProgramEffectTarget.SelectedTarget }] ||
                directed.DirectedPolicyEffects != (DirectedTurnCardPolicyEffect.IgnoreDistance | DirectedTurnCardPolicyEffect.BypassSlashLimit))
                throw new InvalidOperationException(path + ": S5 Xiongluan requires its complete once-per-Play exact paid area cost and directed policies.");
        foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => e.Op == counter)))
            if (p.Id != "shanhe:leizhen" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
                t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses || t.Optional ||
                t.Effects is not [{ Op: SkillProgramEffectOp.ResolveShanheLeizhenUse }])
                throw new InvalidOperationException(path + ": Leizhen requires its actual committed-use actor trigger.");
    }
}

internal sealed class ShanheLeizhenCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "shanhe:leizhen";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses ||
            t.Optional || t.IsSkillInvocation || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.UsageLimit is not null || t.MarkerCost is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveShanheLeizhenUse, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Leizhen requires the exact native committed Use actor contract, including genuine response Uses.");
    }
}
