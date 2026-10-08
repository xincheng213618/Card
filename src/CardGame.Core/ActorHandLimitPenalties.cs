namespace CardGame.Core;

/// <summary>An issued contribution with the card actor's next actual turn end as its lifetime.</summary>
public sealed record ActorHandLimitPenalty(
    long GrantSequence, long ProgramFrameId, int EffectIndex, CardConversionSource Source,
    string GameplayHash, long CardUseFrameId, long ActionId, int ActorSeat, int TargetSeat,
    int IssuedActualTurnNumber, int Amount);

public sealed record ProgramActorHandLimitPenaltyGrantedEvent(ActorHandLimitPenalty Penalty) : IGameEvent;

// Null end identity denotes terminal cleanup, never a fabricated actual turn.
public sealed record ProgramActorHandLimitPenaltyExpiredEvent(
    ActorHandLimitPenalty Penalty, int? EndedTurnNumber, int? EndedActorSeat, string Reason) : IGameEvent;

internal static class ActorHandLimitPenaltyContract
{
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantActorHandLimitPenalty))
            throw new InvalidOperationException($"Invalid skill program at {path}: an actor hand-limit penalty requires a mandatory designated-target CardUseTargetsFinalized trigger, not an activation.");
    }

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        var penalties = trigger.Effects.Where(effect => effect.Op == SkillProgramEffectOp.GrantActorHandLimitPenalty).ToArray();
        if (penalties.Length == 0) return;
        if (trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Target || trigger.Optional ||
            !trigger.SingleActionInstance || !trigger.OnlyDesignatedCardTargets || trigger.IncludeResponseUses ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null ||
            trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null || trigger.ChoiceGroup is not null ||
            trigger.EvaluateConditionAtResolution || trigger.AllowNoEventTarget ||
            trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null ||
            trigger.Subject is not null || trigger.SourceZones.Count != 0 || trigger.DestinationZones.Count != 0 ||
            trigger.MovementOccurrence is not null || trigger.DamageOccurrence is not null || penalties.Length != 1 ||
            penalties[0] is not { Target: SkillProgramEffectTarget.Owner, Amount: > 0,
                Condition.Kind: SkillProgramConditionKind.Always, TargetReference: null })
            throw new InvalidOperationException($"Invalid skill program at {path}: an actor hand-limit penalty requires one unconditional positive owner penalty in a mandatory, single-action, designated-target CardUseTargetsFinalized trigger without usage costs.");
    }
}

internal interface IActorHandLimitPenaltyHost
{
    void GrantActorHandLimitPenalty(ProgramSkillFrame frame, int amount);
}

internal sealed record RequireActorHandLimitPenaltyTarget : ProgramResourceOperation;

internal sealed class GrantActorHandLimitPenaltyDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantActorHandLimitPenalty;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantActorHandLimitPenaltyHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        var amount = reader.RequiredInt("amount");
        if (amount <= 0)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.amount: an actor hand-limit penalty must be a positive integer.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), amount, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireActorHandLimitPenaltyTarget()];
}

internal sealed class GrantActorHandLimitPenaltyHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantActorHandLimitPenalty;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        if (targetSeat != frame.OwnerSeat || host is not IActorHandLimitPenaltyHost penaltyHost)
            throw new InvalidOperationException("An actor hand-limit penalty requires its exact owner program host.");
        penaltyHost.GrantActorHandLimitPenalty(frame, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}
