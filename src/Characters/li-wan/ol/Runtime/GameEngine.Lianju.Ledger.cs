namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int LianjuEndingSerial => ProgramEventHistory<LianjuEndingReachedEvent>().LastOrDefault()?.Serial ?? 0;
    private void RecordLianjuEntityUse(int actor, long? action, long nativeUse, int card)
    {
        if (card <= 0 || !IsValidPlayerSeat(actor) || _turnNumber < 1 || ProgramEventHistory<LianjuEntityUsedEvent>().Any(e =>
                e.ActualTurn == _turnNumber && e.ActionId == action && e.UseFrameId == nativeUse && e.CardId == card)) return;
        AdvanceEventRulesAndQueueFact(new LianjuEntityUsedEvent(_turnNumber, actor, action, nativeUse, card));
    }
    private IReadOnlyList<int> LianjuEligibleCards(int actor, bool red) => Array.AsReadOnly(
        ProgramEventHistory<LianjuEntityUsedEvent>().Where(e => e.ActualTurn == _turnNumber && LianjuFinalUseActor(e) == actor)
            .Select(e => e.CardId).Distinct().Where(id => _cardZones.GetLocation(id) == CardLocation.DiscardPile &&
                SuitColor(GetAdvancedCard(id).Suit) == red).Order().ToArray());

    private LianjuPromiseGrantedEvent[] LianjuDuePromises()
    {
        var endings = ProgramEventHistory<LianjuEndingReachedEvent>().ToArray();
        return ProgramEventHistory<LianjuPromiseGrantedEvent>().Where(p => endings.FirstOrDefault(e => e.Serial > p.EndingSerial && e.ActorSeat == p.RecipientSeat)?.Serial == LianjuEndingSerial)
            .OrderBy(p => p.FrameId).ToArray();
    }
    private void AppendLianjuEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if (!HasLiWanCapability) return;
        items.RemoveAll(item => item.Candidate is { } c && GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveLianjuOppositeColorReturn));
        var due = LianjuDuePromises();
        for (var i = 0; i < due.Length; i++)
        {
            var p = due[i];
            if (ProgramEventHistory<LianjuPromiseConsumedEvent>().Any(e => e.PromiseFrameId == p.FrameId) || !_players[p.Source.OwnerSeat].IsAlive ||
                _contentRegistry.GetSkill(p.Source.SkillId).Program is not { } program || program.GameplayHash != p.GameplayHash) continue;
            var trigger = program.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveLianjuOppositeColorReturn }]);
            items.Add(new(TurnEndingBoundaryItemKind.Program, trigger.Priority, $"lianju:{p.FrameId}",
                new(p.Source.OwnerSeat, p.Source.SkillId, trigger.Id, p.Source.SkillInstanceId, p.GameplayHash, trigger.Priority, i),
                CaptureProgramTriggerFacts(_players[p.Source.OwnerSeat])));
        }
    }
}
