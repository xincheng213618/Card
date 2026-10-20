using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouDaQiaoGuoseStage { Selecting, Using, MovementChildren, Drawing, DrawChildren }
public sealed record MouDaQiaoGuoseReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, string Branch, int TargetSeat, MouDaQiaoGuoseStage Stage,
    int? CardId = null, CardLocation? From = null, long? UseId = null, long Before = 0, long After = 0, bool Paid = false);
public sealed record MouDaQiaoLiuliReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int ActualTurn, int OriginalActorSeat, int RedirectTargetSeat, int PaidCardId, Suit PaidSuit,
    bool Choosing, int? MarkTargetSeat = null);
public sealed record MouDaQiaoLiuliPhaseReceipt(long GrantId, CardConversionSource Source,
    string GameplayHash, int RecipientSeat, int ActualTurn, long WindowId);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDaQiaoGuoseReceipt? MouDaQiaoGuose { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDaQiaoLiuliReceipt? MouDaQiaoLiuli { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDaQiaoLiuliPhaseReceipt? MouDaQiaoLiuliPhase { get; init; }
}
public sealed record MouDaQiaoGuoseReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, string Branch, int TargetSeat, int PhaseLimit) : IGameEvent;
public sealed record MouDaQiaoGuosePaidEvent(long FrameId, string Branch, int CardId, CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record MouDaQiaoGuoseDrawReturnedEvent(long FrameId, long? AttemptFrameId, int ActualCount, int ReplacementCount) : IGameEvent;
public sealed record MouDaQiaoGuoseCompletedEvent(long FrameId, bool Paid) : IGameEvent;
public sealed record MouDaQiaoLiuliCostFrozenEvent(long FrameId, CardConversionSource Source, int ActualTurn,
    int OriginalActorSeat, int RedirectTargetSeat, int CardId, Suit Suit) : IGameEvent;
public sealed record MouDaQiaoLiuliMarkGrantedEvent(long GrantId, CardConversionSource Source, string GameplayHash,
    int RecipientSeat, int ActualTurn, long? PreviousGrantId) : IGameEvent;
public sealed record MouDaQiaoLiuliPhaseIssuedEvent(long FrameId, long GrantId, int RecipientSeat, int ActualTurn, long WindowId) : IGameEvent;
public sealed record MouDaQiaoLiuliPhaseReturnedEvent(long FrameId, long GrantId, int RecipientSeat, int ActualTurn) : IGameEvent;
public sealed record MouDaQiaoLiuliMarkConsumedEvent(long FrameId, long GrantId) : IGameEvent;

internal interface IMouDaQiaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouDaQiaoQuery : IProgramSelectionQuery
{
    int Limit();
    bool Allows(int owner, int? card, int target, string branch);
}
[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 31300, SkillProgramEffectOp.ResolveMouDaQiaoGuose, SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove)]
internal sealed class MouDaQiaoPhaseLimit : IProgramSelectionPhaseLimitProvider
{ public int? GetLimit(ProgramSelectionContext c, IProgramSelectionQueries q) => q.Get<IMouDaQiaoQuery>().Limit(); }
[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 31300, SkillProgramEffectOp.ResolveMouDaQiaoGuose, SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove)]
internal sealed class MouDaQiaoTargets : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates d, IProgramSelectionQueries q)
    {
        var query = q.Get<IMouDaQiaoQuery>();
        if (c.Activation.Id == "remove-delayed")
        { d.Targets = d.Targets.Where(t => query.Allows(c.OwnerSeat, null, t, c.Activation.Id)).ToArray(); return true; }
        var pairs = d.Cards.SelectMany(id => d.Targets.Where(t => query.Allows(c.OwnerSeat, id, t, c.Activation.Id)).Select(t => new ActiveSkillCardTarget(id, t))).ToArray();
        d.CardTargets = Array.AsReadOnly(pairs); d.Cards = pairs.Select(p => p.CardId).Distinct().ToArray();
        d.Targets = pairs.Select(p => p.TargetSeat).Distinct().ToArray(); return true;
    }
}
internal abstract class MouDaQiaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouDaQiaoHandler(Op);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveMouDaQiaoGuose => [new ReadSelectedTarget(), new ConsumeSelectedCards(1)],
        SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove => [new ReadSelectedTarget()],
        SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart => [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashTargetRedirecting), new ReadCardSet("liuli-cost")],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]
    };
}
internal sealed class ResolveMouDaQiaoGuoseDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDaQiaoGuose;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class ResolveMouDaQiaoGuoseRemoveDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove; }
internal sealed class ResolveMouDaQiaoLiuliHeartDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart; }
internal sealed class InsertMouDaQiaoLiuliPlayDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InsertMouDaQiaoLiuliPlay; }
internal sealed class MouDaQiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouDaQiaoHost>().Resolve(f, op);
}
internal static class MouDaQiaoComposition
{
    internal const string Guose = "mou-mobile:guose", Liuli = "mou-mobile:liuli";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouDaQiaoGuose or SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove or SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart or SkillProgramEffectOp.InsertMouDaQiaoLiuliPlay;
    [ProgramCatalogValidation(1350)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var operations = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects));
        if (p.Id is not (Guose or Liuli)) { if (operations.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": Da Qiao operations require their full original source."); return; }
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.Id == Guose ? p.Triggers.Count == 0 && p.Activations is [var use, var remove] &&
            use.Id == "diamond-use" && use.MinCards == 1 && use.MaxCards == 1 && use.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && use.CardSuits.SequenceEqual([Suit.Diamond]) && use.TargetKind == SkillProgramTargetKind.OtherLiving &&
            remove.Id == "remove-delayed" && remove.MinCards == 0 && remove.MaxCards == 0 && remove.TargetKind == SkillProgramTargetKind.AnyLiving &&
            p.Activations.All(a => a.MinTargets == 1 && a.MaxTargets == 1 && a.UsesPerTurn is null && a.UsesPerPhase == 4 && a.UsageGroup == "guose" && a.Effects.Count == 1 && Plain(a.Effects[0], a.Id == "diamond-use" ? SkillProgramEffectOp.ResolveMouDaQiaoGuose : SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove)) :
            p.Activations.Count == 0 && p.Triggers is [var redirect, var phase] && redirect.Id == "redirect-slash" && redirect.Window == SkillProgramTriggerWindow.SlashTargetRedirecting && redirect.Optional &&
            redirect.OwnerRelation == SkillProgramCardActionOwnerRelation.Target && redirect.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) && redirect.Effects is [var choose, var cost, var heart, var move, var grant] &&
            choose.Op == SkillProgramEffectOp.SelectTarget && choose.TargetKind == SkillProgramTargetKind.SlashRedirectable &&
            cost.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard && cost.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && cost.Destination == SkillProgramCardDestination.DiscardPile && cost.AwaitMovementTriggers && cost.FreezeMovedCardSuit && cost.ResultBind == "liuli-cost" &&
            Plain(heart, SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart) && move.Op == SkillProgramEffectOp.RedirectCurrentAttack && move.Target == SkillProgramEffectTarget.SelectedTarget && Plain(grant, SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart) &&
            phase.Id == "marked-turn-start" && phase.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && phase.Subject == SkillProgramTriggerSubject.Owner && !phase.Optional && phase.Effects.Count == 1 && Plain(phase.Effects[0], SkillProgramEffectOp.InsertMouDaQiaoLiuliPlay);
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Da Qiao requires both mode-limited Guose branches and the full paid native Liuli/Heart/inserted-Play sequence.");
    }
}
