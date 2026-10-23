using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IShenZhangFeiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShenZhangFeiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new ShenZhangFeiHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class ResolveShenZhangFeiShencaiDescriptor() : ShenZhangFeiDescriptor(SkillProgramEffectOp.ResolveShenZhangFeiShencai);
internal sealed class ResolveShenZhangFeiPunishmentDescriptor() : ShenZhangFeiDescriptor(SkillProgramEffectOp.ResolveShenZhangFeiPunishment);
internal sealed class ResolveShenZhangFeiXunshiDescriptor() : ShenZhangFeiDescriptor(SkillProgramEffectOp.ResolveShenZhangFeiXunshi);
internal sealed class ShenZhangFeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IShenZhangFeiHost>().Execute(f, op);
}
internal interface IShenZhangFeiSelection : IProgramSelectionQuery { int Limit(int owner); }
[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 38600, SkillProgramEffectOp.ResolveShenZhangFeiShencai)]
internal sealed class ShenZhangFeiPhaseLimit : IProgramSelectionPhaseLimitProvider
{ public int? GetLimit(ProgramSelectionContext c, IProgramSelectionQueries q) => q.Get<IShenZhangFeiSelection>().Limit(c.OwnerSeat); }
internal sealed class ShenZhangFeiCommittedContract : IProgramCommittedUseContract
{
    public string SkillId => ShenZhangFeiComposition.Xunshi;
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if(t.Id!="colorless-use"||t.Window!=SkillProgramTriggerWindow.CardUseCommitted||t.Optional||!t.IncludeResponseUses||
            t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Subject is not null||t.SingleActionInstance||t.SourceSkillId is not null||t.SourceViewAsId is not null||
            t.UsageLimit is not null||t.UsageScope is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.CardKinds.Count!=0||t.CardCategories.Count!=0||
            t.Effects is not[{Op:SkillProgramEffectOp.ResolveShenZhangFeiXunshi,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])
            throw new InvalidOperationException("Xunshi requires the exact native committed true Use, including real defensive Uses.");
    }
}

public enum ShenZhangFeiStage { Judging, JudgmentReturned, GainChildren, MarkChildren, TakeChoice, TakeChildren, PenaltyChildren, JudgmentCleanupChildren }
public sealed record ShenZhangFeiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int TargetSeat, int Amount, ShenZhangFeiStage Stage)
{
    public long JudgmentId { get; init; }
    public long RootId { get; init; }
    public int? ResultCardId { get; init; }
    public CardKind? ResultKind { get; init; }
    public int CardId { get; init; }
    public CardLocation? From { get; init; }
    public CardLocation? To { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long BatchId { get; init; }
    public bool Paid { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? TakeBeforeDeath { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenZhangFeiReceipt? ShenZhangFei { get; init; } }
public sealed record ShenZhangFeiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int TargetSeat, int Amount) : IGameEvent;
public sealed record ShenZhangFeiSentenceEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat, PrintedCardTerms Terms, int DeathCount) : IGameEvent;
public sealed record ShenZhangFeiJudgmentGainEvent(long RootId, long ClaimId, long JudgmentId, int OwnerSeat, int TargetSeat, int CardId, long Sequence) : IGameEvent;
public sealed record ShenZhangFeiJudgmentReturnedEvent(long FrameId, long JudgmentId, int? CardId, CardKind? Kind) : IGameEvent;
public sealed record ShenZhangFeiMovementPaidEvent(long FrameId, ShenZhangFeiStage Stage, int CardId, CardLocation From, CardLocation To, long Before, long After) : IGameEvent;
public sealed record ShenZhangFeiPunishmentPaidEvent(long FrameId, string Binding, int TargetSeat, int Amount, long BatchId) : IGameEvent;
public sealed record ShenZhangFeiTuLossEvent(long BatchId,int TargetSeat,string GameplayHash) : IGameEvent;
public sealed record ShenZhangFeiXunshiEvent(CardConversionSource Source, string GameplayHash, long ActionId, int Limit) : IGameEvent;

internal static class ShenZhangFeiComposition
{
    internal const string Shencai = "anniversary:shencai", HappyShencai = "happy:shencai", Xunshi = "anniversary:xunshi";
    internal static bool IsShencai(string id) => id is Shencai or HappyShencai;
    internal static readonly CardKind[] MultiTricks = [CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FiveGrains, CardKind.PeachGarden, CardKind.IronChain];
    [ProgramCatalogValidation(38600)] internal static void Validate(string path, SkillProgram p)
    {
        static bool Owned(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveShenZhangFeiShencai or SkillProgramEffectOp.ResolveShenZhangFeiPunishment or SkillProgramEffectOp.ResolveShenZhangFeiXunshi;
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Owned(e.Op))) return;
        static bool Effect(IReadOnlyList<SkillProgramEffect> es, SkillProgramEffectOp op) => es is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        static bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow w, SkillProgramEffectOp op) => t.Id == id && t.Window == w && !t.Optional && t.UsageLimit is null && t.UsageScope is null && Effect(t.Effects, op);
        var valid = p.Id switch
        {
            Shencai or HappyShencai => p.Activations is [{ Id: "sentence", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, UsesPerPhase: 5, UsesPerGame: null } a] &&
                Effect(a.Effects, SkillProgramEffectOp.ResolveShenZhangFeiShencai) && p.Triggers is [var gain, var chi, var tu, var liu, var si] &&
                Trigger(gain,"judgment-first-gain",SkillProgramTriggerWindow.JudgmentFinalized,SkillProgramEffectOp.ResolveShenZhangFeiShencai) && gain.Subject == SkillProgramTriggerSubject.Any && gain.JudgmentReasons.SequenceEqual(["anniversary-shencai"]) &&
                Trigger(chi,"chi-damage",SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramEffectOp.ResolveShenZhangFeiPunishment) && chi.Subject == SkillProgramTriggerSubject.Owner && chi.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                Trigger(tu,"tu-hand-loss",SkillProgramTriggerWindow.CardsMoved,SkillProgramEffectOp.ResolveShenZhangFeiPunishment) && tu.Subject == SkillProgramTriggerSubject.Owner && tu.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && tu.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                Trigger(liu,"liu-turn-ending",SkillProgramTriggerWindow.TurnEnding,SkillProgramEffectOp.ResolveShenZhangFeiPunishment) && liu.Subject == SkillProgramTriggerSubject.Owner &&
                Trigger(si,"si-turn-ended",SkillProgramTriggerWindow.AfterTurnEnded,SkillProgramEffectOp.ResolveShenZhangFeiPunishment) && si.Subject == SkillProgramTriggerSubject.Owner &&
                p.CardIdentities.Count == 0 && p.Modifiers.Count == 0,
            Xunshi => p.Activations.Count == 0 && p.Triggers is [var t] && Trigger(t,"colorless-use",SkillProgramTriggerWindow.CardUseCommitted,SkillProgramEffectOp.ResolveShenZhangFeiXunshi) &&
                t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.CardKinds.Count == 0 && t.IncludeResponseUses &&
                p.CardIdentities is [{ Id: "multi-trick-slash", OutputKind: CardKind.Slash } i] && i.InputKinds.SequenceEqual(MultiTricks) && i.Zones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.WoodenOxGrain]) && p.Modifiers.Count == 0,
            _ => false
        };
        if (!valid || p.ViewAs.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": Shen Zhang Fei requires his exact formal sentence, issued punishments, forced identity and native colorless-use bindings.");
    }
}
