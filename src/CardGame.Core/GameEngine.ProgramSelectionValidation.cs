namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramSelectionValidationCatalog> ProgramSelectionValidationMetadata = new(() =>
        new(typeof(ProgramSelectionValidationCatalog).Assembly.GetTypes()), true);
    private ProgramSelectionValidationRegistry? _programSelectionValidators;
    private ProgramSelectionValidationRegistry ProgramSelectionValidators =>
        _programSelectionValidators ??= new(ProgramSelectionValidationMetadata.Value, ProgramSelectionQueries);

    // Each adapter exposes only the original mechanism's read-only predicate.
    // Selection policy, branching, ordering and errors belong to the independent validators.
    [ProgramCapability]
    private sealed class MatchingRecastSelectionQuery(GameEngine engine) : IMatchingRecastSelectionQuery
    {
        public bool IsValid(int ownerSeat, IReadOnlyList<int> cards) => engine.ValidMatchingRecastSelection(ownerSeat, cards);
    }

    [ProgramCapability]
    private sealed class OwnedRankSumSelectionQuery(GameEngine engine) : IOwnedRankSumSelectionQuery
    {
        public bool CanStart(int ownerSeat, ProgramExecutionPlan plan) => engine.CanStartOwnedHandRankSum(engine._players[ownerSeat], plan);
    }

    [ProgramCapability]
    private sealed class ExactOwnedCountSelectionQuery(GameEngine engine) : IExactOwnedCountSelectionQuery
    {
        public bool CanStart(int ownerSeat, ProgramExecutionPlan plan) => engine.CanStartExactOwnedCardCount(engine._players[ownerSeat], plan);
    }

    [ProgramCapability]
    private sealed class EquipmentDonationSelectionQuery(GameEngine engine) : IEquipmentDonationSelectionQuery
    {
        public bool CanPay(int ownerSeat, string skillId, string stateId) => engine.HasPayableAllEquipmentDonation(ownerSeat, skillId, stateId);
    }

    [ProgramCapability]
    private sealed class PairObtainSelectionQuery(GameEngine engine) : IPairObtainSelectionQuery
    {
        public bool CanSelect(int ownerSeat, string skillId, SkillProgramActivation activation, IReadOnlyList<int> targets) =>
            engine.CanActivatePairObtain(engine._players[ownerSeat], skillId,
                engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId), activation, targets);
    }

    [ProgramCapability]
    private sealed class DeferredHandPairSelectionQuery(GameEngine engine) : IDeferredHandPairSelectionQuery
    {
        public bool CanSelect(int ownerSeat, int first, int second) => engine.IsPayableDeferredHandPair(ownerSeat, first, second);
    }

    [ProgramCapability]
    private sealed class ForeignSlashSelectionQuery(GameEngine engine) : IForeignSlashSelectionQuery
    {
        public bool CanSelect(int ownerSeat, int targetSeat) => engine.CanStartSelectedForeignCardSlash(engine._players[ownerSeat], targetSeat);
    }

    [ProgramCapability]
    private sealed class SelectedActorDuelSelectionQuery(GameEngine engine) : ISelectedActorDuelSelectionQuery
    {
        public bool CanSelect(int ownerSeat, int targetSeat) => engine.CanIssueSelectedActorDuel(ownerSeat, targetSeat);
    }

    [ProgramCapability]
    private sealed class EquipmentPlacementSelectionQuery(GameEngine engine) : IEquipmentPlacementSelectionQuery
    {
        public bool CanPlace(int ownerSeat, int targetSeat, int cardId) => engine.CanPlaceActivationEquipment(ownerSeat, targetSeat, cardId);
    }

    [ProgramCapability]
    private sealed class CapturedEquipmentPlacementSelectionQuery(GameEngine engine) : ICapturedEquipmentPlacementSelectionQuery
    {
        public bool CanPlace(int ownerSeat, int targetSeat, int cardId, string skillId) =>
            engine.CanPlaceCapturedEquipmentActivation(ownerSeat, targetSeat, cardId, skillId);
    }

    [ProgramCapability]
    private sealed class BlackGiftSelectionQuery(GameEngine engine) : IBlackGiftSelectionQuery
    {
        public bool WasIssued(int ownerSeat, string skillId) => engine.BlackGiftGameIssued(ownerSeat, skillId);
    }

    [ProgramCapability]
    private sealed class DynamicDiscardDamageSelectionQuery(GameEngine engine) : IDynamicDiscardDamageSelectionQuery
    {
        public bool CanStart(int ownerSeat, string skillId) => engine.CanStartDynamicDiscardDamage(engine._players[ownerSeat], skillId);
        public bool CanTarget(int ownerSeat, string skillId, int targetSeat) =>
            engine.CanSelectDynamicDiscardDamageTarget(engine._players[ownerSeat], skillId, targetSeat);
    }

    [ProgramCapability]
    private sealed class GameHandHpSelectionQuery(GameEngine engine) : IGameHandHpSelectionQuery
    {
        public bool CanTarget(int ownerSeat, string skillId, string stateId, int targetSeat) =>
            engine.CanSelectGameHandHpTarget(ownerSeat, skillId, stateId, targetSeat);
    }

    [ProgramCapability]
    private sealed class CategoryRefundSelectionQuery(GameEngine engine) : ICategoryRefundSelectionQuery
    {
        public bool CanTarget(int ownerSeat, string skillId, string stateId, int targetSeat) =>
            engine.DrawDiscardCategoryTargetAvailable(ownerSeat, skillId, stateId, targetSeat);
    }

    [ProgramCapability]
    private sealed class NextActualUseSelectionQuery(GameEngine engine) : INextActualUseSelectionQuery
    {
        public bool HasHpLossTargets(int ownerSeat) => engine.HasHpLossSlashTargets(engine._players[ownerSeat]);
        public SkillProgramEffect? HpLossProducer(ProgramExecutionPlan plan) => HpLossSlashProgramProducer(plan);
        public int HpLossMaximum(int ownerSeat, CardKind kind) => engine.HpLossSlashMaximum(engine._players[ownerSeat], kind);
        public SkillProgramEffect? SelectedProducer(ProgramExecutionPlan plan) => NextActualUseSelectedProducer(plan);
        public string SkillInstance(int ownerSeat, string skillId) => engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId);
        public ProgramMultiCardViewAsSelection? FindSelection(int ownerSeat, IReadOnlyList<int> cards, CardKind kind, CardConversionSource source) =>
            ProgramSelectionProjection.Freeze(engine.FindProgramMultiCardViewAsSelection(engine._players[ownerSeat], cards, kind, false, source));
        public bool HasAdjustment(int ownerSeat) => engine.CurrentNextActualUseAdjustment(ownerSeat) is not null;
        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public bool CanUseSlash(int ownerSeat, int targetSeat, ProgramMultiCardViewAsSelection selection) =>
            engine.CanUseNextActualUseSelectedSlash(engine._players[ownerSeat], engine._players[targetSeat], selection);
        public IReadOnlyList<int> Targets(int ownerSeat, ProgramExecutionPlan plan, ProgramMultiCardViewAsSelection selection) =>
            Array.AsReadOnly(AdjustedUseSelection.SelectTargets(ownerSeat, plan, [], [selection],
                engine.ProgramSelectionQueries.Get<IProgramSelectionSourceQuery>()).ToArray());
    }

    [ProgramCapability]
    private sealed class ProvenanceSelectionQuery(GameEngine engine) : IProvenanceSelectionQuery
    {
        public bool TracksProvenance => engine.TracksDiscardedEntityProvenance;
        public bool IsFaceDown(int ownerSeat) => engine._players[ownerSeat].IsFaceDown;
        public bool HasClaimedEntityPolicy(int ownerSeat) =>
            engine.CardPolicies(engine._players[ownerSeat], SkillProgramCardPolicyKind.ClaimedEntitiesFaceDownUse).Any();
        public string SkillInstance(int ownerSeat, string skillId) => engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId);
        public ProgramMultiCardViewAsSelection? FindSelection(int ownerSeat, IReadOnlyList<int> cards, CardKind kind, CardConversionSource source) =>
            ProgramSelectionProjection.Freeze(engine.FindProgramMultiCardViewAsSelection(engine._players[ownerSeat], cards, kind, false, source));
        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public bool CanUseSlash(int ownerSeat, int targetSeat, ProgramMultiCardViewAsSelection selection) =>
            engine.CanUseVirtualSlashTarget(engine._players[ownerSeat], engine._players[targetSeat],
                physicalSuit: engine.ProgramMultiCardSuit(engine._players[ownerSeat], selection),
                effectiveColor: engine.ProgramMultiCardColor(engine._players[ownerSeat], selection),
                physicalCardIds: selection.Cards.Select(card => card.Id).ToArray());
    }

    [ProgramCapability]
    private sealed class WholeHandSuitSelectionQuery(GameEngine engine) : IWholeHandSuitSelectionQuery
    {
        public string SkillInstance(int ownerSeat, string skillId) => engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId);
        public bool IsValid(int ownerSeat, IReadOnlyList<int> cards, CardConversionSource source) =>
            engine.ValidWholeHandSuitConversion(engine._players[ownerSeat], cards.Select(engine.GetAttackCard).ToArray(), source);
    }

    [ProgramCapability]
    private sealed class DistinctFactionSelectionQuery(GameEngine engine) : IDistinctFactionSelectionQuery
    {
        public bool CanPay(int ownerSeat, string skillId) =>
            engine.HasPayableDistinctFactionOwnerHe(ownerSeat, skillId, engine.GetRuntimeSkillInstanceId(engine._players[ownerSeat], skillId));
    }
}
