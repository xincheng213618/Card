using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouFaZhengStage { Target, Gift, MovementChildren, NextMark, Repay, LosingHp, LossChildren, RecoveryChildren }
public sealed record MouFaZhengReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long ParentId)
{
    public MouFaZhengStage Stage { get; init; }
    public bool Accepted { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    private readonly IReadOnlyList<long> _marks = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> Marks { get => _marks; init => _marks = Array.AsReadOnly(value.ToArray()); }
    public int Index { get; init; }
    public int Target { get; init; } = -1;
    public long MarkId { get; init; }
    public int Remaining { get; init; }
    public string MovementPurpose { get; init; } = "";
    public int PaidCardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long LossId { get; init; }
    public int LossHpBefore { get; init; }
    public int LossHpAfter { get; init; }
    public int RecoveryHpBefore { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouFaZhengReceipt? MouFaZheng { get; init; } }
public sealed record MouFaZhengStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long ParentId) : IGameEvent;
public sealed record MouFaZhengMarkEvent(long MarkId, CardConversionSource Source, string GameplayHash, int TargetSeat, long GiftSequence) : IGameEvent;
public sealed record MouFaZhengRemovedEvent(long FrameId, long MarkId, int TargetSeat, int Obtained) : IGameEvent;
public sealed record MouFaZhengObtainedEvent(long FrameId, long MarkId, int TargetSeat, int Actual, long Sequence) : IGameEvent;
public sealed record MouFaZhengGainQualifiedEvent(long BatchId, CardConversionSource Source, string GameplayHash, long MarkId, int MovementIndex, long GainSequence, int TargetSeat) : IGameEvent;
public sealed record MouFaZhengMovementPaidEvent(long FrameId, string Purpose, long MarkId, int SourceSeat, int TargetSeat, long Sequence) : IGameEvent;
public sealed record MouFaZhengLossReturnedEvent(long FrameId, int TargetSeat, long ChangeId, int Amount, int HpBefore, int HpAfter) : IGameEvent;
public sealed record MouFaZhengRecoveryReturnedEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record MouFaZhengCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouFaZhengHost { SkillProgramStepOutcome Start(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouFaZhengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouFaZhengHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase, Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.IssueMouFaZhengXuanhuo ? [] :
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain ? SkillProgramTriggerWindow.CardsGained : SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.PaidReturnAccepted;
}
internal sealed class MouFaZhengIssueDescriptor : MouFaZhengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.IssueMouFaZhengXuanhuo; }
internal sealed class MouFaZhengTakeDescriptor : MouFaZhengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain; }
internal sealed class MouFaZhengEnyuanDescriptor : MouFaZhengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouFaZhengEnyuan; }
internal sealed class MouFaZhengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouFaZhengHost>().Start(f, op);
}
internal static class MouFaZhengComposition
{
    internal const string Xuanhuo = "mou-mobile:xuanhuo", Enyuan = "mou-mobile:enyuan";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.IssueMouFaZhengXuanhuo or SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain or SkillProgramEffectOp.ResolveMouFaZhengEnyuan;
    private static bool Own(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
    [ProgramCatalogValidation(1400)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Xuanhuo or Enyuan) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        var valid = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 &&
            (p.Id == Xuanhuo ? p.Activations is [var a] && a.Id == "give-xuan" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 &&
                a.UsesPerPhase == 1 && a.UsesPerTurn is null && a.UsesPerGame is null && Own(a.Effects, SkillProgramEffectOp.IssueMouFaZhengXuanhuo) &&
                p.Triggers is [var gain] && gain.Id == "marked-outside-draw-gain" && Trigger(gain, SkillProgramTriggerWindow.CardsGained) && gain.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                gain.DestinationZones.SequenceEqual([CardZoneKind.Hand]) && Own(gain.Effects, SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain) :
             p.Id == Enyuan && p.Activations.Count == 0 && p.Triggers is [var t] && t.Id == "settle-xuan" && Trigger(t, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) && Own(t.Effects, SkillProgramEffectOp.ResolveMouFaZhengEnyuan));
        if (!valid) throw new InvalidOperationException(path + ": Mou Fa Zheng requires complete attributed Xuanhuo and mandatory Enyuan.");
    }
    private static bool Trigger(SkillProgramTrigger t) => !t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.UsageLimit is null && t.UsageScope is null && t.SourceZones.Count == 0 && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.GainPhaseQualification is null;
    private static bool Trigger(SkillProgramTrigger t, SkillProgramTriggerWindow w) => t.Window == w && Trigger(t);
}
