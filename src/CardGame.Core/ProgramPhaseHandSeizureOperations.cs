namespace CardGame.Core;

public enum PhaseHandSeizureStage { PaymentChildren = 8002, FlipChildren = 8003, TakeChildren = 8004 }
public enum PhaseHandDebtReturnStage { Choosing = 8005, MovementChildren = 8006 }

public sealed record ProgramPhaseHandSeizureReceipt(int InstructionIndex, int TargetSeat, string ContinuationId,
    int ActualTurnNumber, int PhaseInstanceId, int CostCardId, CardLocation CostFrom, bool WasFaceDown,
    PhaseHandSeizureStage Stage, long CostBefore, long CostAfter, IReadOnlyList<int> TakenCardIds,
    long TakeBefore = 0, long TakeAfter = 0)
{
    private readonly IReadOnlyList<int> _taken = Array.AsReadOnly(TakenCardIds.ToArray());
    public IReadOnlyList<int> TakenCardIds { get => _taken; init => _taken = Array.AsReadOnly(value.ToArray()); }
}

public sealed record ProgramPhaseHandDebtReturnReceipt(int InstructionIndex, long SeizureFrameId, int TargetSeat,
    int FrozenHp, int RequiredCount, PhaseHandDebtReturnStage Stage, IReadOnlyList<int> CardIds,
    IReadOnlyList<CardLocation> Locations, long SequenceBefore = 0, long SequenceAfter = 0)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _locations = Array.AsReadOnly(Locations.ToArray());
    public IReadOnlyList<CardLocation> Locations { get => _locations; init => _locations = Array.AsReadOnly(value.ToArray()); }
}

// These committed facts contain scalars and scalar-only source identities. Hand identities remain on trusted frames.
public sealed record PhaseHandSeizurePaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int ActualTurnNumber, int PhaseInstanceId, int CostCardId, CardLocation CostFrom,
    long Before, long After) : IGameEvent;
public sealed record PhaseHandSeizureTurnedEvent(long FrameId, int OwnerSeat, bool WasFaceDown, bool IsFaceDown) : IGameEvent;
public sealed record PhaseHandSeizureIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string ContinuationId, int TargetSeat, int ActualTurnNumber, int PhaseInstanceId, int ObtainedCount, bool SameHand,
    long Before, long After) : IGameEvent;
public sealed record PhaseHandDebtReturnStartedEvent(long FrameId, long SeizureFrameId, int OwnerSeat,
    int TargetSeat, int ActualTurnNumber, int PhaseInstanceId, int FrozenHp, int RequiredCount) : IGameEvent;
public sealed record PhaseHandDebtSettledEvent(long SeizureFrameId, long ReturnFrameId, int OwnerSeat,
    int TargetSeat, string Reason, int RequiredCount, int ActualCount, long Before, long After) : IGameEvent;

internal interface IPhaseHandSeizureProgramHost
{
    SkillProgramStepOutcome DiscardTurnOverAndTakeHand(ProgramSkillFrame frame, string continuationId);
    SkillProgramStepOutcome ReturnIssuedPhaseHandDebt(ProgramSkillFrame frame);
}

internal sealed class DiscardTurnOverAndTakeHandDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardTurnOverAndTakeHand;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardTurnOverAndTakeHandHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TakeRandomHandCards,
        static (_, context) => context.PricePhaseHandSeizure());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), stateId: reader.RequiredIdentifier("stateId"));
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new ConsumeSelectedCards(1)];
}
internal sealed class ReturnIssuedPhaseHandDebtDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReturnIssuedPhaseHandDebt;
    public override ISkillProgramEffectHandler Handler { get; } = new ReturnIssuedPhaseHandDebtHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding), new RequireOwnTurnBoundary()];
}
public sealed class DiscardTurnOverAndTakeHandHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardTurnOverAndTakeHand;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        ((IPhaseHandSeizureProgramHost)host).DiscardTurnOverAndTakeHand(frame, effect.StateId!);
}
public sealed class ReturnIssuedPhaseHandDebtHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReturnIssuedPhaseHandDebt;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        ((IPhaseHandSeizureProgramHost)host).ReturnIssuedPhaseHandDebt(frame);
}

internal static class PhaseHandSeizureComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int selectedCardCount, bool initialSelectedTarget, SkillProgramTargetKind? targetKind,
        IReadOnlyList<CardZoneKind>? sourceZones, int? minimumCards, SkillProgramTurnOwnerScope? turnOwnerScope)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardTurnOverAndTakeHand) &&
            (window is not null || selectedCardCount != 1 || minimumCards != 1 || !initialSelectedTarget ||
             targetKind != SkillProgramTargetKind.AnyLivingMale || sourceZones is null ||
             !sourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
             effects is not [{ Op: SkillProgramEffectOp.DiscardTurnOverAndTakeHand }]))
            throw new InvalidOperationException($"Invalid phase hand seizure at {path}: one owned HE cost and original living male target are required.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ReturnIssuedPhaseHandDebt) &&
            (window != SkillProgramTriggerWindow.PlayEnding || selectedCardCount != 0 || initialSelectedTarget ||
             (turnOwnerScope ?? SkillProgramTurnOwnerScope.Own) != SkillProgramTurnOwnerScope.Own ||
             effects is not [{ Op: SkillProgramEffectOp.ReturnIssuedPhaseHandDebt }]))
            throw new InvalidOperationException($"Invalid phase hand debt at {path}: one mandatory actual owner PlayEnding return is required.");
    }
    internal static void ValidateBindings(string path, IReadOnlyList<SkillProgramActivation> activations, IReadOnlyList<SkillProgramTrigger> triggers)
    {
        var starts = activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardTurnOverAndTakeHand)).ToArray();
        var returns = triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ReturnIssuedPhaseHandDebt)).ToArray();
        if (starts.Length == 0 && returns.Length == 0) return;
        if (starts is not [var start] || returns is not [var due] || start.UsesPerPhase != 1 || start.UsesPerTurn is not null ||
            start.UsesPerGame is not null || start.MarkerCost is not null || start.CardCountExpression is not null ||
            start.Condition.Kind != SkillProgramConditionKind.Always || start.Effects.Single().StateId != due.Id ||
            due.Condition.Kind != SkillProgramTriggerConditionKind.Always || due.MarkerCost is not null || due.UsageScope is not null ||
            due.UsageLimit is not null || due.DynamicUsageLimit is not null || due.ChoiceGroup is not null || due.DeferredTurnEndOnly)
            throw new InvalidOperationException($"Invalid phase hand bindings at {path}: one phase-limited paid producer must name one uncharged mandatory return.");
    }
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerSubject? subject, bool optional)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ReturnIssuedPhaseHandDebt) &&
            (subject != SkillProgramTriggerSubject.Owner || optional))
            throw new InvalidOperationException($"Invalid phase hand debt trigger at {path}: the original owner return is mandatory.");
    }
}
