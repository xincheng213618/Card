namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 67600)]
    private void ObserveShiTzDiscardedSlashes(IGameEvent payload)
    {
        if (!HasShiTzCapability || _turnNumber <= 0 || _winner != Winner.None) return;
        var range = ProgramEventHistory<ShiTzRangeRecordedEvent>().LastOrDefault(e => e.TurnNumber == _turnNumber)?.Range ?? 0;
        if (range <= 0) return;
        foreach (var movement in _cardMovements.Skip(_shiTzLieScanSequence).ToArray())
        {
            _shiTzLieScanSequence = movement.Sequence;
            if (movement.TurnNumber != _turnNumber || movement.To != CardLocation.DiscardPile ||
                !ShiTaiShiCiComposition.IsSlashKind(movement.CardKind)) continue;
            var before = ProgramEventHistory<ShiTzLieGainedEvent>().Count(e => e.TurnNumber == _turnNumber);
            if (before >= range || before >= ShiTaiShiCiComposition.MaxLieCount ||
                ProgramEventHistory<ShiTzLieGainedEvent>().Any(e => e.TurnNumber == _turnNumber && e.CardId == movement.CardId)) continue;
            var total = before + 1;
            AdvanceEventRulesAndQueueFact(new ShiTzLieGainedEvent(_turnNumber, _currentSeat, movement.CardId, total, total));
        }
    }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> ShiTaiShiCiPublicStates(CharacterState owner, string skillId)
    {
        if (!HasShiTzCapability || skillId != ShiTzZhanlie) return [];
        var lies = ShiTzLieCount();
        return Array.AsReadOnly(GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == ShiTzZhanlie).SelectMany(i => new[]
        {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "zhanlie-lies", ProgramPublicRuleStateKind.GameActivationProgress,
                ShiTaiShiCiComposition.MaxLieCount, lies, SkillUsageScope.Game),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "zhanlie-y", ProgramPublicRuleStateKind.GameActivationProgress,
                2, lies / 3, SkillUsageScope.Game),
        }).ToArray());
    }
}
