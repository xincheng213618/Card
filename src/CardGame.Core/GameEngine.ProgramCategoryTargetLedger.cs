namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IControlProgramEffectHost
    {
        public void ConsumeCategoryTargetLedger(ProgramSkillFrame frame, string usageId) => engine.ConsumeProgramCategoryTargetLedger(frame, usageId);
        public void ReplaceCurrentCardUseActor(ProgramSkillFrame frame, int actorSeat) => engine.ReplaceCurrentProgramCardUseActor(frame, actorSeat);
        public SkillProgramStepOutcome AddCurrentCardUseTarget(ProgramSkillFrame frame, int targetSeat) => engine.AddCurrentProgramCardUseTarget(frame, targetSeat);
        public void ReduceCurrentDamage(ProgramSkillFrame frame, int amount) => engine.ReduceProgramCurrentDamage(frame, amount);
    }

    private bool CanActivateCategoryTargetLedger(int ownerSeat, string skillId, string usageId,
        IReadOnlyList<int> cardIds, IReadOnlyList<int> targetSeats)
    {
        if (cardIds.Count != 1 || targetSeats.Count != 1 || targetSeats[0] == ownerSeat ||
            !_players[targetSeats[0]].IsAlive) return false;
        var location = _cardZones.GetLocation(cardIds[0]);
        if (location.OwnerSeat != ownerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) return false;
        var card = _cardZones.CardsAt(location).SingleOrDefault(card => card.Id == cardIds[0]);
        if (card is null) return false;
        var category = GetProgramCardCategory(card.Kind);
        return _skillRuntimeState.GetUsage(ownerSeat, skillId, $"{usageId}:category:{category}", SkillUsageScope.Phase) == 0 &&
            _skillRuntimeState.GetUsage(ownerSeat, skillId, $"{usageId}:target:{targetSeats[0]}", SkillUsageScope.Phase) == 0;
    }

    private void ConsumeProgramCategoryTargetLedger(ProgramSkillFrame program, string usageId)
    {
        var active = GetActiveProgramFrame(program.Id);
        if (active.TriggerId is not null || !CanActivateCategoryTargetLedger(active.OwnerSeat, active.SkillId,
            usageId, active.SelectedCardIds, active.SelectedTargetSeats))
            throw new InvalidOperationException("Category/target usage must be validated before any activation payment.");
        var location = _cardZones.GetLocation(active.SelectedCardIds[0]);
        var category = GetProgramCardCategory(_cardZones.CardsAt(location).Single(card => card.Id == active.SelectedCardIds[0]).Kind);
        Consume($"{usageId}:category:{category}", SkillUsageScope.Phase, 1);
        Consume($"{usageId}:target:{active.SelectedTargetSeats[0]}", SkillUsageScope.Phase, 1);
        Consume(usageId, SkillUsageScope.Turn, int.MaxValue);

        void Consume(string key, SkillUsageScope scope, int limit)
        {
            if (!_skillRuntimeState.TryConsumeUsage(active.OwnerSeat, active.SkillId, key, scope, limit))
                throw new InvalidOperationException("A validated category/target ledger could not advance.");
            AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(active.OwnerSeat, active.SkillId, key, scope,
                _skillRuntimeState.GetUsage(active.OwnerSeat, active.SkillId, key, scope)));
        }
    }

    private int GetProgramCategoryTargetTurnUsage(ProgramSkillFrame frame) =>
        _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId,
            GetProgramBindingId(frame), SkillUsageScope.Turn);

    internal int GetProgramPhaseSkillUsage(ProgramSkillFrame frame, string usageId) =>
        _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId, usageId, SkillUsageScope.Phase);
}
