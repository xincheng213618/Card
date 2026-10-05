using System.Text.Json.Serialization;

namespace CardGame.Core;

// All fields are scalars or scalar-only source identities. The public view exposes only X and the actual turn.
public sealed record TurnJudgedRankSlashPolicy(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, string GameplayHash,
    long JudgmentFrameId, int JudgmentCardId, int Rank);
public sealed record TurnJudgedRankSlashPolicyGrantedEvent(TurnJudgedRankSlashPolicy Policy) : IGameEvent;
public sealed record JudgedRankSlashUseReceipt(long CardUseFrameId, long CardActionId, int ActorSeat,
    int? EffectiveRank, TurnJudgedRankSlashPolicy Policy, bool IgnoresDistance, bool IgnoresQuota);
public sealed record JudgedRankSlashUsePolicyAppliedEvent(JudgedRankSlashUseReceipt Receipt) : IGameEvent;
public sealed record OtherActualBasicDiscardDrawIssuedEvent(long FrameId, CardUseEffectSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, long MovementBatchId,
    long MovementSequence, int DiscardOwnerSeat, int CardId) : IGameEvent;
public sealed record JudgedRankSlashThresholdSnapshot(int ActualTurnNumber, int Rank);
public sealed partial record PlayerSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JudgedRankSlashThresholdSnapshot? JudgedRankSlashThreshold { get; init; }
}

internal interface IJudgedRankSlashProgramHost
{
    void GrantJudgedRankSplitSlashTurnPolicy(ProgramSkillFrame frame, string resultBind);
    void DrawFromOtherActualBasicDiscard(ProgramSkillFrame frame);
}
internal sealed class GrantJudgedRankSplitSlashTurnPolicyDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantJudgedRankSplitSlashTurnPolicyHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (_, context) => context.PriceJudgedRankSplitSlash());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!), new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}
internal sealed class DrawFromOtherActualBasicDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawFromOtherActualBasicDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawFromOtherActualBasicDiscardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}
public sealed class GrantJudgedRankSplitSlashTurnPolicyHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host)
    { ((IJudgedRankSlashProgramHost)host).GrantJudgedRankSplitSlashTurnPolicy(frame, effect.SourceBind!); return SkillProgramStepOutcome.Continue; }
}
public sealed class DrawFromOtherActualBasicDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawFromOtherActualBasicDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host)
    { ((IJudgedRankSlashProgramHost)host).DrawFromOtherActualBasicDiscard(frame); return SkillProgramStepOutcome.Continue; }
}
internal static class JudgedRankSlashComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCards, bool selectedTarget, int targetSetMaximum)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy) &&
            (window is not null || selectedCards != 0 || selectedTarget || targetSetMaximum != 0 ||
             effects is not [{ Op: SkillProgramEffectOp.StartJudgment, Target: SkillProgramEffectTarget.Owner,
                 Visibility: SkillProgramCardSetVisibility.Public, Condition.Kind: SkillProgramConditionKind.Always,
                 SourceRef: null, ResultBind: var bind },
                 { Op: SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy, SourceBind: var input }] || bind != input))
            throw new InvalidOperationException($"Invalid judged rank Slash at {path}: one owner public judgment and its exact scalar grant are required.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DrawFromOtherActualBasicDiscard) &&
            (window != SkillProgramTriggerWindow.DiscardPileReceived || effects is not [{ Op: SkillProgramEffectOp.DrawFromOtherActualBasicDiscard }]))
            throw new InvalidOperationException($"Invalid basic discard benefit at {path}: one original discard observer is required.");
    }
    internal static void ValidateBindings(string path, IReadOnlyList<SkillProgramActivation> activations,
        IReadOnlyList<SkillProgramTrigger> triggers)
    {
        foreach (var a in activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy)))
            if (a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
                a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.MarkerCost is not null ||
                a.Condition.Kind != SkillProgramConditionKind.Always || a.CardCountExpression is not null || a.ContinueAfterOwnerDeath)
                throw new InvalidOperationException($"Invalid judged rank activation at {path}: one uncharged use per real Play phase is required.");
        foreach (var t in triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawFromOtherActualBasicDiscard)))
            if (t.Window != SkillProgramTriggerWindow.DiscardPileReceived || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional ||
                t.UsageScope != SkillUsageScope.Turn || t.UsageLimit != 1 || t.DynamicUsageLimit is not null ||
                t.MarkerCost is not null || t.ChoiceGroup is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.SourceZones.Count != 0 || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 ||
                t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || !t.MovementDiscardOnly ||
                t.DiscardOwnerScope != SkillProgramDiscardOwnerScope.Other || t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch)
                throw new InvalidOperationException($"Invalid basic discard trigger at {path}: one optional unfiltered actual-turn quota observer is required.");
    }
}

internal sealed partial class ProgramAiEstimateContext
{
    // The unrevealed future judgment uses a public prior; no deck or opposing Hand is consulted.
    internal void PriceJudgedRankSplitSlash() => _otherAdjustment += _publicContext.CanUseSlashOnOther ? 8 : 0;
}
