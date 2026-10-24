using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryLeiBoHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class AnniversaryLeiBoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        var e = ParseOwnerAlways(r);
        return Op == SkillProgramEffectOp.RequestAnniversaryLeiBoSileiSlash ? new(Op, e.Target, 0, e.Condition, booleanValue: false) : e;
    }
    public override ISkillProgramEffectHandler Handler => new AnniversaryLeiBoHandler(Op);
    public override ProgramOperationInteraction Interaction => Op is SkillProgramEffectOp.RequestAnniversaryLeiBoSileiSlash or SkillProgramEffectOp.SettleAnniversaryLeiBoSileiPenalty or SkillProgramEffectOp.FinishAnniversaryLeiBoShuaijie ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.InitializeAnniversaryLeiBoSilei => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.RecordAnniversaryLeiBoSileiLoot or SkillProgramEffectOp.RequestAnniversaryLeiBoSileiSlash or SkillProgramEffectOp.SettleAnniversaryLeiBoSileiPenalty => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        _ => []
    };
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeAnniversaryLeiBoSileiDescriptor : AnniversaryLeiBoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeAnniversaryLeiBoSilei; }
internal sealed class RecordAnniversaryLeiBoSileiLootDescriptor : AnniversaryLeiBoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordAnniversaryLeiBoSileiLoot; }
internal sealed class RequestAnniversaryLeiBoSileiSlashDescriptor : AnniversaryLeiBoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestAnniversaryLeiBoSileiSlash; }
internal sealed class SettleAnniversaryLeiBoSileiPenaltyDescriptor : AnniversaryLeiBoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleAnniversaryLeiBoSileiPenalty; }
internal sealed class BeginAnniversaryLeiBoShuaijieDescriptor : AnniversaryLeiBoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BeginAnniversaryLeiBoShuaijie; }
internal sealed class FinishAnniversaryLeiBoShuaijieDescriptor : AnniversaryLeiBoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.FinishAnniversaryLeiBoShuaijie; }
internal sealed class AnniversaryLeiBoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryLeiBoHost>().Execute(f, op);
}
internal static class AnniversaryLeiBoComposition
{
    internal const string Silei = "anniversary:silue", Shuaijie = "anniversary:shuaijie";
    [ProgramCatalogValidation(49800)] internal static void Validate(string path, SkillProgram p)
    {
        static bool O(SkillProgramEffect e) => e.Op is >= SkillProgramEffectOp.InitializeAnniversaryLeiBoSilei and <= SkillProgramEffectOp.FinishAnniversaryLeiBoShuaijie;
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(O)) return;
        var clean = p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.Contributions.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.BooleanStates.Count == 0 && p.DamageModifiers.Count == 0;
        var valid = false;
        if (p.Id == Silei && p.Activations.Count == 0 && p.Triggers is [var opening, var loot, var reply])
            valid = opening.Id == "opening-mark" && opening.Window == SkillProgramTriggerWindow.GameStarting && opening.Subject == SkillProgramTriggerSubject.Owner && !opening.Optional &&
                opening.Effects is [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLiving }, { Op: SkillProgramEffectOp.InitializeAnniversaryLeiBoSilei }] &&
                loot.Id == "marked-source-loot" && loot.Window == SkillProgramTriggerWindow.AfterDamageApplied && loot.Subject == SkillProgramTriggerSubject.Any && loot.Optional && loot.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                loot.Effects is [{ Op: SkillProgramEffectOp.RecordAnniversaryLeiBoSileiLoot }, { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Amount: 1, Destination: SkillProgramCardDestination.OwnerHand, AwaitMovementTriggers: true, SkipIfNoCards: true } move] && move.CardOwnerRef?.Kind == ProgramParticipantRef.EventTarget && move.ChooserRef?.Kind == ProgramParticipantRef.Owner && move.Zones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) &&
                reply.Id == "marked-victim-reply" && reply.Window == SkillProgramTriggerWindow.AfterDamageApplied && reply.Subject == SkillProgramTriggerSubject.Any && !reply.Optional && reply.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && reply.Effects is [{ Op: SkillProgramEffectOp.RequestAnniversaryLeiBoSileiSlash }, { Op: SkillProgramEffectOp.SettleAnniversaryLeiBoSileiPenalty }] &&
                p.Triggers.All(t => t.UsageScope is null && t.UsageLimit is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Effects.All(e => e.Condition.Kind == SkillProgramConditionKind.Always));
        else if (p.Id == Shuaijie && p.Triggers.Count == 0 && p.Activations is [var a])
            valid = a.Id == "seize-and-return-mark" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.UsesPerTurn is null && a.UsesPerGame == 1 && a.Condition.Kind == SkillProgramConditionKind.Always &&
                a.Effects is [{ Op: SkillProgramEffectOp.BeginAnniversaryLeiBoShuaijie }, { Op: SkillProgramEffectOp.ChangeMaximumHp, Target: SkillProgramEffectTarget.Owner, Amount: -1 }, { Op: SkillProgramEffectOp.FinishAnniversaryLeiBoShuaijie }] && a.Effects.All(e => e.Condition.Kind == SkillProgramConditionKind.Always);
        if (!clean || !valid) throw new InvalidOperationException(path + ": Lei Bo requires the opening attributed mark, actual damage once per victim per turn, native fixed Slash or hand discard and limited maximum-HP payment before seizure.");
    }
}
public enum AnniversaryLeiBoStage { SlashRequest, PenaltyChoice, BeforeMaximum, RewardChoice, PickReward, MovementChildren }
public sealed record AnniversaryLeiBoReceipt(CardConversionSource Source, string Hash, int Marked, long ParentId, long DamageId, int? DamageSource, int Turn, int TurnOwner, AnniversaryLeiBoStage Stage)
{
    public int MaximumBefore { get; init; }
    public int HpBefore { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public string? Branch { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public HappyWeiYanGainRequestSettlement? Gain { get; init; }
    private IReadOnlyList<int> _selected = [];
    private IReadOnlyList<CardMovementRecord> _moves = [];
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryLeiBoReceipt? AnniversaryLeiBo { get; init; } }
public sealed record AnniversaryLeiBoMarkMovedEvent(long FrameId, CardConversionSource Source, int PreviousSeat, int NewSeat) : IGameEvent;
public sealed record AnniversaryLeiBoLootInvokedEvent(long FrameId, CardConversionSource Source, string Hash, long WindowId, long DamageId, int Turn, int TurnOwner, int Victim) : IGameEvent;
public sealed record AnniversaryLeiBoReplyStartedEvent(long FrameId, CardConversionSource Source, string Hash, long WindowId, long DamageId, int Marked, int? DamageSource, int Turn, int TurnOwner) : IGameEvent;
public sealed record AnniversaryLeiBoSlashUsedEvent(long FrameId, long UseId, long ActionId, int Target) : IGameEvent;
public sealed record AnniversaryLeiBoShuaijieStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Marked, int Turn, int TurnOwner, int MaximumBefore, int HpBefore) : IGameEvent;
public sealed record AnniversaryLeiBoBranchChosenEvent(long FrameId, string Branch) : IGameEvent;
public sealed record AnniversaryLeiBoCardSelectedEvent(long FrameId, int Count) : IGameEvent;
public sealed record AnniversaryLeiBoMovementSettledEvent(long FrameId, string Branch, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryLeiBoCompletedEvent(long FrameId, bool UsedSlash) : IGameEvent;
