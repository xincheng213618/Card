using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum OlMoRumoStage { LossIssuing, LossChildren }

public sealed record OlMoRumoReceipt(CardConversionSource Source, string GameplayHash, long EntryId,
    long BoundaryId, int RoundNumber, int ActualTurn, int TurnOwner, int InstructionIndex,
    bool HadRoundDamage)
{
    public OlMoRumoStage Stage { get; init; }
    public bool LossReturned { get; init; }
    public int ActualLoss { get; init; }
    public int HpAfter { get; init; }
    public bool AppearancePrevented { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OlMoRumoReceipt? OlMoRumo { get; init; }
}

// The entered state is independent of the continued ownership of its issuing
// printed skill. Every field is scalar or an immutable source value.
public sealed record OlMoRumoEnteredEvent(long EntryId, int OwnerSeat, CardConversionSource Origin,
    string OriginGameplayHash, int EntryRound, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record OlMoRumoRoundStartedEvent(long FrameId, long EntryId, long BoundaryId,
    int OwnerSeat, int RoundNumber, int ActualTurn, int TurnOwner, bool HadRoundDamage) : IGameEvent;
public sealed record OlMoRumoHpLossAppliedEvent(long FrameId, int OwnerSeat, int RequestedAmount,
    int ActualAmount, int HpAfter, bool AppearancePrevented) : IGameEvent;
public sealed record OlMoRumoRoundCompletedEvent(long FrameId, long EntryId, long BoundaryId,
    int OwnerSeat, int RoundNumber, bool HadRoundDamage, int ActualLoss, bool AppearancePrevented) : IGameEvent;

internal interface IOlMoRumoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }

internal sealed class ResolveOlMoRumoRoundPenaltyDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOlMoRumoRoundPenalty;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.LoseHp, static (_, context) => context.PublicControlValue(-1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundEnding)];
    public override ISkillProgramEffectHandler Handler { get; } = new OlMoRumoHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}

internal sealed class OlMoRumoHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOlMoRumoRoundPenalty;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IOlMoRumoHost>().Resolve(frame);
}

internal static class OlMoRumoComposition
{
    [ProgramCatalogValidation(1240)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id != "ol:mo-rumo" && !program.Triggers.SelectMany(t => t.Effects)
                .Concat(program.Activations.SelectMany(a => a.Effects))
                .Any(e => e.Op == SkillProgramEffectOp.ResolveOlMoRumoRoundPenalty)) return;
        if (program.Id != "ol:mo-rumo" || program.Activations.Count != 0 || program.ViewAs.Count != 0 ||
            program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 ||
            program.HpSkillGrants.Count != 0 || program.DamageModifiers.Count != 0 || program.BooleanStates.Count != 0 ||
            program.Contributions.Count != 0 || program.Triggers is not [var trigger] || !Plain(trigger))
            throw new InvalidOperationException(path + ": OL Rumo requires its exact issued entered-state old-round penalty, without a printed or live-source substitute.");
    }

    private static bool Plain(SkillProgramTrigger t) =>
        t.Id == "no-damage-round-ending" && t.Window == SkillProgramTriggerWindow.RoundEnding &&
        t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional && t.Priority == 0 &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.GainPhaseQualification is null &&
        t.RequireDamageSource is null && t.RequireNoCardConversion is null && t.CardCategories.Count == 0 &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets &&
        t.MovementOccurrence is null && t.DamageOccurrence is null && !t.EvaluateConditionAtResolution &&
        t.ChoiceGroup is null && t.OrderGroup is null && t.Suits.Count == 0 && t.MinimumRank == 1 && t.MaximumRank == 13 &&
        t.ExcludedReasons.Count == 0 && t.JudgmentReasons.Count == 0 && t.JudgmentSource is null &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && !t.IgnoreOwnSkillMovements &&
        !t.MovementDiscardOnly && t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Other &&
        t.HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerEvent && t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive &&
        !t.NoDyingAtActivation && !t.DeferredTurnEndOnly && !t.AllowOwnDiscardPhaseEnded && t.MarkerCost is null &&
        t.OwnerRelation is null && t.SourceSkillId is null && t.SourceViewAsId is null && !t.IncludeResponseUses &&
        !t.SingleActionInstance && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.CardKinds.Count == 0 &&
        t.DamageCardKinds.Count == 0 && t.Effects is [{ Op: SkillProgramEffectOp.ResolveOlMoRumoRoundPenalty,
            Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
}
