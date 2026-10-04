namespace CardGame.Core;

public enum ActualDiscardRecoveryPhaseKind { Preparation, Judgment, Draw, Play, Discard, Ending }
public sealed record ActualDiscardRecoveryPhaseKey(long Token, int TurnNumber, int ActualTurnOwnerSeat,
    int ActorSeat, ActualDiscardRecoveryPhaseKind Kind);
public sealed record ActualDiscardRecoveryPhaseStartedEvent(ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record ActualDiscardRecoveryBatchQualifiedEvent(long BatchId, ActualDiscardRecoveryPhaseKey Phase,
    int DiscardOwnerSeat, int OriginalCount) : IGameEvent;
public sealed record DiscardRecoveryEntity(int CardId, long MovementSequence, CardLocation OriginalFrom);
public enum ActualDiscardRecoveryStage { SelectingReturn, ReturnPaid, SelectingClaim, ClaimPaid }
public sealed record ProgramActualDiscardRecoveryReceipt(int InstructionIndex, long WindowFrameId, long BatchId,
    CardConversionSource Source, string GameplayHash, string StateId, ActualDiscardRecoveryPhaseKey Phase,
    int DiscardOwnerSeat, IReadOnlyList<DiscardRecoveryEntity> OriginalEntities, ActualDiscardRecoveryStage Stage,
    int? ReturnedCardId = null, long ReturnMovementSequence = 0, bool? ClaimRemaining = null,
    long ClaimSequenceBefore = 0, long ClaimSequenceAfter = 0, IReadOnlyList<int>? ClaimedCardIds = null)
{
    private readonly IReadOnlyList<DiscardRecoveryEntity> _originalEntities = Array.AsReadOnly(OriginalEntities.ToArray());
    public IReadOnlyList<DiscardRecoveryEntity> OriginalEntities { get => _originalEntities; init => _originalEntities = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int>? _claimedCardIds = ClaimedCardIds is null ? null : Array.AsReadOnly(ClaimedCardIds.ToArray());
    public IReadOnlyList<int>? ClaimedCardIds { get => _claimedCardIds; init => _claimedCardIds = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record ActualDiscardRecoveryReturnedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, string StateId, ActualDiscardRecoveryPhaseKey Phase, long BatchId, int DiscardOwnerSeat,
    int CardId, long MovementSequence) : IGameEvent;
public sealed record ActualDiscardRecoveryClaimedEvent(long ProgramFrameId, long BatchId,
    IReadOnlyList<int> CardIds, long SequenceBefore, long SequenceAfter) : IGameEvent
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
}
public enum CapturedEquipmentDrawStage { Placed, Drawn }
public sealed record ProgramCapturedEquipmentDrawReceipt(int InstructionIndex, string SourceBind,
    CardConversionSource Source, string GameplayHash, int TurnNumber, int RecipientSeat, int CardId,
    CardLocation OriginalFrom, EquipmentSlot Slot, int? ReplacedCardId, bool ReplacedGeneralWeapon,
    long SequenceBefore, long SequenceAfter, long EntryMovementSequence, CapturedEquipmentDrawStage Stage,
    long DrawSequenceBefore = 0, long DrawSequenceAfter = 0, int ActualDrawCount = 0);
public sealed record CapturedEquipmentPlacedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, int RecipientSeat, int CardId, CardLocation OriginalFrom, EquipmentSlot Slot,
    int? ReplacedCardId, bool ReplacedGeneralWeapon, long SequenceBefore, long SequenceAfter,
    long EntryMovementSequence) : IGameEvent;
public sealed record CapturedEquipmentRewardDrawnEvent(long ProgramFrameId, int OwnerSeat, int Requested,
    int Actual, long SequenceBefore, long SequenceAfter) : IGameEvent;

internal interface IActualDiscardRecoveryProgramHost
{
    SkillProgramStepOutcome PlaceCapturedEquipmentAndDraw(ProgramSkillFrame frame, int target, string bind, int draw);
    SkillProgramStepOutcome RestoreActualDiscardBatch(ProgramSkillFrame frame, string stateId);
}
internal sealed class PlaceCapturedEquipmentAndDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new ActualDiscardRecoveryHandler(SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, c) => c.PlaceSelectedEquipment());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "amount", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: equipment placement requires selectedTarget.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, DrawProgramOperationDescriptor.Amount(r, 20),
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadSelectedTarget(), new ReadCapturedPlacementCard(e.SourceBind!), new ReadSingleCardSet(e.SourceBind!),
         new MoveCardSet(e.SourceBind!, null, SkillProgramCardDestination.SelectedTargetEquipment)];
}
internal sealed class RestoreActualDiscardBatchDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RestoreActualDiscardBatch;
    public override ISkillProgramEffectHandler Handler { get; } = new ActualDiscardRecoveryHandler(SkillProgramEffectOp.RestoreActualDiscardBatch);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) =>
        c.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId:r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}
internal sealed class ActualDiscardRecoveryHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw
            ? ((IActualDiscardRecoveryProgramHost)host).PlaceCapturedEquipmentAndDraw(f,target,e.SourceBind!,e.Amount)
            : ((IActualDiscardRecoveryProgramHost)host).RestoreActualDiscardBatch(f,e.StateId!);
}
internal static class ActualDiscardRecoveryComposition
{
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw)) return;
        if (a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLiving || a.SourceZones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            a.SourceZones.Count == 0 || !a.CardCategories.SequenceEqual([SkillProgramCardCategory.Equipment]) ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null ||
            a.ContinueAfterOwnerDeath || a.Condition.Kind != SkillProgramConditionKind.Always ||
            a.Effects is not [{ Op:SkillProgramEffectOp.CaptureSelectedCards, Target:SkillProgramEffectTarget.Owner, ResultBind:{ } bind, Condition.Kind:SkillProgramConditionKind.Always },
                { Op:SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw, Condition.Kind:SkillProgramConditionKind.Always, SourceBind:{ } source }] || bind != source)
            throw new InvalidOperationException($"Invalid skill program at {path}: captured equipment placement owns one exact HE equipment/other target and its draw tail.");
    }
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, bool optional,
        SkillProgramDiscardOwnerScope discardScope, bool discardOnly, IReadOnlyList<Suit> suits,
        IReadOnlyList<CardKind> kinds, IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<string> reasons, IReadOnlyList<string> excludedReasons, SkillProgramMovementOccurrence? occurrence)
    {
        if (!effects.Any(e=>e.Op==SkillProgramEffectOp.RestoreActualDiscardBatch)) return;
        if (effects.Count != 1 || effects[0].Op != SkillProgramEffectOp.RestoreActualDiscardBatch ||
            window != SkillProgramTriggerWindow.DiscardPileReceived || subject != SkillProgramTriggerSubject.Owner ||
            !optional || discardScope != SkillProgramDiscardOwnerScope.Other || !discardOnly || occurrence is not null ||
            suits.Count != 0 || kinds.Count != 0 || categories.Count != 0 || reasons.Count != 0 || excludedReasons.Count != 0)
            throw new InvalidOperationException($"Invalid skill program at {path}: actual discard recovery requires an unfiltered optional other-player genuine-discard batch, with its own frozen same-source occurrence.");
    }
}
