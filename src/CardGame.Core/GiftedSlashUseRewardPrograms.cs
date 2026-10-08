namespace CardGame.Core;

/// <summary>One physical hand interval, issued by a real, paid gift. No turn expiry is implied.</summary>
public sealed record GiftedSlashHandPolicy(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, int RecipientSeat, int CardId,
    long GiftMovementSequence, long GiftBatchId, int ActualTurnNumber, int ActualPlayPhaseSerial);

public sealed record ProgramGiftedSlashGiftReceipt(int InstructionIndex, GiftedSlashHandPolicy Policy,
    CardActionCost Material, long Before, long After);

/// <summary>Frozen before the recipient's real Use leaves Hand; ordinary responses issue no benefit.</summary>
public sealed record GiftedSlashUseBenefit(GiftedSlashHandPolicy Policy, long CardUseFrameId,
    long ActionId, int OriginalActorSeat, int ProviderSeat, CardActionCost Material);

public sealed record ProgramGiftedSlashRewardReceipt(int InstructionIndex, GiftedSlashUseBenefit Benefit,
    int FrozenDrawCount, int DrawActual, long Before, long After);

// All new facts are scalar; the native movement and action events freeze their own collections.
public sealed record GiftedSlashGiftStartedEvent(long FrameId, GiftedSlashHandPolicy Policy,
    CardActionCost Material, long SequenceBefore) : IGameEvent;
public sealed record GiftedSlashHandPolicyGrantedEvent(GiftedSlashHandPolicy Policy,
    CardActionCost Material) : IGameEvent;
public sealed record GiftedSlashGiftResolvedEvent(long FrameId, GiftedSlashHandPolicy Policy) : IGameEvent;
public sealed record GiftedSlashUseBenefitIssuedEvent(GiftedSlashUseBenefit Benefit) : IGameEvent;
public sealed record GiftedSlashRewardDrawIssuedEvent(long FrameId, GiftedSlashUseBenefit Benefit,
    int RequestedCount, int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record GiftedSlashRewardResolvedEvent(long FrameId, GiftedSlashUseBenefit Benefit,
    int RequestedCount, int ActualCount) : IGameEvent;

internal static class GiftedSlashRewardContract
{
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (!activation.Effects.Any(e => e.Op is SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward or SkillProgramEffectOp.RewardGiftedSlashUse)) return;
        if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving || activation.TargetPhaseLedgerId != activation.Id ||
            activation.UsesPerTurn is not null || activation.UsesPerPhase is not null || activation.UsesPerGame is not null ||
            activation.MarkerCost is not null || activation.CardCountExpression is not null || activation.ContinueAfterOwnerDeath ||
            activation.Condition.Kind != SkillProgramConditionKind.Always || activation.Effects is not [var ledger, var select, var gift] ||
            ledger.Op != SkillProgramEffectOp.ConsumeTargetPhaseLedger || ledger.Target != SkillProgramEffectTarget.Owner || ledger.StateId != activation.Id ||
            ledger.Condition.Kind != SkillProgramConditionKind.Always ||
            select.Op != SkillProgramEffectOp.SelectOwnedCards || select.Target != SkillProgramEffectTarget.Owner || select.TargetReference is not null ||
            select.Condition.Kind != SkillProgramConditionKind.Always || select.Amount != 0 || select.NumberExpression is not null ||
            select.MinimumCards != 1 || select.MaximumCards != 1 || select.Zones is not [CardZoneKind.Hand] ||
            select.CardKinds.Count != 0 || select.Suits.Count != 0 || select.AllowDecline || string.IsNullOrWhiteSpace(select.ResultBind) ||
            gift.Op != SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward || gift.Target != SkillProgramEffectTarget.SelectedTarget ||
            gift.SourceBind != select.ResultBind || gift.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: gifted Slash reward requires a zero-card, one-other-target activation with its phase target ledger, exact private one-Hand selection and terminal gifted Slash transfer.");
    }

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op is SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward or SkillProgramEffectOp.RewardGiftedSlashUse)) return;
        if (trigger.Window != SkillProgramTriggerWindow.CardUseCompleted || trigger.Subject is not null ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || trigger.Optional || !trigger.SingleActionInstance || trigger.IncludeResponseUses ||
            trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null || trigger.UsageScope is not null || trigger.UsageLimit is not null ||
            trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null || trigger.ChoiceGroup is not null ||
            trigger.EvaluateConditionAtResolution || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.CardKinds.Count != 3 || !trigger.CardKinds.Contains(CardKind.Slash) || !trigger.CardKinds.Contains(CardKind.FireSlash) ||
            !trigger.CardKinds.Contains(CardKind.ThunderSlash) || trigger.CardCategories.Count != 0 || trigger.Suits.Count != 0 ||
            trigger.SourceZones.Count != 0 || trigger.DamageOccurrence is not null || trigger.MovementOccurrence is not null ||
            trigger.OnlyDesignatedCardTargets || trigger.AllowNoEventTarget ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.RewardGiftedSlashUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"Invalid skill program at {path}: gifted Slash reward requires one mandatory observer CardUseCompleted binding for all three Slash kinds, a single actual action and no response uses or usage costs.");
    }

    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var program in programs.Values)
        {
            var gifts = program.Activations.Count(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward));
            var rewards = program.Triggers.Count(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardGiftedSlashUse));
            if (gifts == 0 && rewards == 0) continue;
            if (gifts != 1 || rewards != 1)
                throw new InvalidOperationException($"Invalid skill program {program.Id}: gifted Slash reward requires exactly one paired gift activation and completion reward binding.");
            foreach (var activation in program.Activations) ValidateActivation($"{program.Id}.activations.{activation.Id}", activation);
            foreach (var trigger in program.Triggers) ValidateTrigger($"{program.Id}.triggers.{trigger.Id}", trigger);
        }
    }
}

internal sealed class GiveBoundHandAsSlashWithUseRewardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward;
    public override ProgramOperationLegalityPolicy LegalityPolicy => new(RequiresOwnerHand: true, ExcludesOwnerAsTarget: true);
    public override ISkillProgramEffectHandler Handler { get; } = new GiftedSlashUseRewardHandler(SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (effect, context) => context.GiftedSlashUseReward(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: gifted Slash transfer requires target selectedTarget.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(),
            new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand]),
            new ReadSingleCardSet(effect.SourceBind!), new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.SelectedTargetHand)];
}

internal sealed class RewardGiftedSlashUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardGiftedSlashUse;
    public override ISkillProgramEffectHandler Handler { get; } = new GiftedSlashUseRewardHandler(SkillProgramEffectOp.RewardGiftedSlashUse);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted), new RequireContext(ProgramContextCapability.CardAction)];
}

internal interface IGiftedSlashUseRewardHost
{
    SkillProgramStepOutcome BeginGiftedSlashGift(SkillProgramEffect effect, ProgramSkillFrame frame, int recipientSeat);
    SkillProgramStepOutcome BeginGiftedSlashUseReward(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal sealed class GiftedSlashUseRewardHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host is not IGiftedSlashUseRewardHost gift ? throw new InvalidOperationException("The program host does not support gifted Slash rewards.") :
            effect.Op == SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward ? gift.BeginGiftedSlashGift(effect, frame, targetSeat) :
            gift.BeginGiftedSlashUseReward(effect, frame);
}
