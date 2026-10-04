namespace CardGame.Core;

// Scalar origin attached only to the new real-discard conversion. Older turn
// conversions retain their historical hand-only boolean matching contract.
public sealed record ProgramPaidColorConversionOrigin(int ActualTurnNumber, int TurnOwnerSeat,
    string GameplayHash, int CardId, CardKind CardKind, CardLocation From, Suit EffectiveSuit,
    int MovementSequence, int SequenceAfter, bool GeneratedMaterial = false);
public sealed record ProgramPaidColorConversionReceipt(int InstructionIndex, string SourceBind,
    ProgramPaidColorConversionOrigin Origin, bool Granted = false);
public sealed record ProgramPaidColorConversionPaidEvent(long ProgramFrameId, CardConversionSource Source,
    int EffectIndex, ProgramPaidColorConversionOrigin Origin) : IGameEvent;

// One public scalar per original physical entity. This records causal card
// damage, never the Slash responses used to answer a Duel or a judgment card.
public sealed record ActualTurnCardDamageEntityEvent(int ActualTurnNumber, int TurnOwnerSeat,
    long DamageFrameId, long CausalFrameId, long? CardActionId, int CardActorSeat, int ProviderSeat,
    int DamageSourceSeat, int VictimSeat, CardKind EffectiveKind, int Amount, DamageNature Nature,
    bool SourceLess, int CardId, int MaterialIndex, int MaterialCount, bool DelayedJudgment) : IGameEvent;
public sealed record ActualTurnCardDamageEmptyEvent(int ActualTurnNumber, int TurnOwnerSeat,
    long DamageFrameId, long CausalFrameId, long? CardActionId, int CardActorSeat,
    int DamageSourceSeat, int VictimSeat, CardKind EffectiveKind, int Amount,
    DamageNature Nature, bool SourceLess) : IGameEvent;
public sealed record ProgramActualTurnDamageClaimReceipt(int InstructionIndex, int ActualTurnNumber,
    int TurnOwnerSeat, long EndingFrameId, int OccurrenceIndex, IReadOnlyList<int> CardIds,
    int SequenceBefore, int SequenceAfter)
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ActualTurnDamageEntityClaimedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int TurnOwnerSeat, int CardId,
    int ClaimIndex, long OriginalDamageFrameId, int MovementSequence) : IGameEvent;

internal interface IPaidColorDamageClaimProgramHost
{
    SkillProgramStepOutcome DiscardBoundCardForOppositeTurnDuel(ProgramSkillFrame frame, string sourceBind);
    SkillProgramStepOutcome ClaimActualTurnDamageEntities(ProgramSkillFrame frame);
}
internal sealed class DiscardBoundCardForOppositeTurnDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardBoundCardForOppositeTurnDuelHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (e, c) => { c.Move(new(SkillProgramEffectOp.MoveBoundCards, SkillProgramEffectTarget.Owner, 0,
            new(SkillProgramConditionKind.Always, 0, []), sourceBind: e.SourceBind,
            destination: SkillProgramCardDestination.DiscardPile)); c.PublicControlValue(14d); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded),
         new RequireOwnedCardSet(e.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand, CardZoneKind.Equipment]),
         new ReadSingleCardSet(e.SourceBind!), new MoveCardSet(e.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}
internal sealed class ClaimActualTurnDamageEntitiesDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimActualTurnDamageEntities;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimActualTurnDamageEntitiesHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(6d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class DiscardBoundCardForOppositeTurnDuelHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) =>
        ((IPaidColorDamageClaimProgramHost)h).DiscardBoundCardForOppositeTurnDuel(f, e.SourceBind!);
}
public sealed class ClaimActualTurnDamageEntitiesHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimActualTurnDamageEntities;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) =>
        ((IPaidColorDamageClaimProgramHost)h).ClaimActualTurnDamageEntities(f);
}
internal static class PaidColorDamageClaimComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        SkillProgramTurnOwnerScope scope, bool optional)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel) &&
            (window != SkillProgramTriggerWindow.DrawPhaseEnded || subject != SkillProgramTriggerSubject.Owner ||
             scope != SkillProgramTurnOwnerScope.Own || !optional || effects.Count != 2 ||
             effects[0] is not { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner,
                 Amount: 1, NumberExpression: null, MinimumCards: 0, MaximumCards: 0, AllowDecline: false } selection || selection.Zones.Count != 2 ||
             !selection.Zones.Contains(CardZoneKind.Hand) || !selection.Zones.Contains(CardZoneKind.Equipment) ||
             selection.CardKinds.Count != 0 || selection.Suits.Count != 0 || selection.TargetReference is not null ||
             selection.Condition.Kind != SkillProgramConditionKind.Always ||
             effects[1].Op != SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel || effects[1].SourceBind != selection.ResultBind))
            throw new InvalidOperationException($"Invalid skill program at {path}: paid opposite-color Duel requires one optional own completed-draw HE selection and its exact discard.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ClaimActualTurnDamageEntities) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner ||
             scope != SkillProgramTurnOwnerScope.Own || optional || effects.Count != 1 ||
             effects[0].Op != SkillProgramEffectOp.ClaimActualTurnDamageEntities))
            throw new InvalidOperationException($"Invalid skill program at {path}: causal card-damage entities require one mandatory own actual Ending binding.");
    }
}
