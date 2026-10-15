namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class SelectionAiQueries(GameEngine engine) : IProgramSelectionAiState, IProgramSelectionRewardState
    {
        public int MarkerCount(int ownerSeat, PlayerMarkerKind marker) =>
            engine._players[ownerSeat].Markers.GetValueOrDefault(marker);
        public IReadOnlyList<Card> OwnerHand(int ownerSeat) => Array.AsReadOnly(engine.GetHand(engine._players[ownerSeat])
            .Select(ProgramSelectionProjection.Freeze).ToArray());
        public int EffectivePindianRank(int ownerSeat, Card card) => engine.EffectivePindianRank(engine._players[ownerSeat], card);
        public SkillProgramCardCategory CardCategory(CardKind kind) => GetProgramCardCategory(kind);
        public bool HasNextActualUseAdjustment(int ownerSeat) => engine.CurrentNextActualUseAdjustment(ownerSeat) is not null;
        public bool HasPindianClaimPolicy(int ownerSeat) =>
            engine.HasCardPolicy(engine._players[ownerSeat], SkillProgramCardPolicyKind.PindianClaim);
        public IEnumerable<ProgramTargetCommitContext> Declarations =>
            engine.CompleteProgramEventHistory().OfType<ProgramTargetCommittedEvent>().Select(receipt => receipt.Declaration);
        public IEnumerable<SkillProgramEffect> EligibleRewardEffects(int ownerSeat)
        {
            var owner = engine._players[ownerSeat];
            var facts = engine.CaptureProgramTriggerFacts(owner);
            return engine.CollectEligibleProgramTriggerCandidates(owner, SkillProgramTriggerWindow.ProgramTargetCommitted, facts)
                .Where(candidate => engine.EnabledSkillPrograms(owner).Any(program => program.Id == candidate.SkillId))
                .SelectMany(candidate => engine.GetProgramTrigger(candidate).Effects);
        }
    }
}
