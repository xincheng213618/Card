namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SilvEntityMarkedEvent? SilvMark(int id) => ProgramEventHistory<SilvEntityMarkedEvent>().FirstOrDefault(e => e.CardId == id && ValidSilvMark(e));
    private bool HasSilvEntityMark(int cardId) => HasLiWanCapability && SilvMark(cardId) is not null;
    private bool ValidSilvMark(SilvEntityMarkedEvent e) => e.CardId > 0 && e.Source.SkillId == "ol:silv" &&
        _contentRegistry.Skills.GetValueOrDefault(e.Source.SkillId)?.Program is { } program && program.GameplayHash == e.GameplayHash &&
        _cardMovements.Any(m => m.Sequence == e.GainMovementSequence && m.CardId == e.CardId && m.To.Zone == CardZoneKind.Hand &&
            m.Reason.Value is LianjuGainReason or SilvDrawReason) && ProgramEventHistory<LiWanPaidEvent>().Any(p => p.FrameId == e.ProducerFrameId &&
                e.GainMovementSequence > p.Before && e.GainMovementSequence <= p.After);
    private bool IsRefundableSilvMovement(CardMovementRecord m) => m.To == CardLocation.DiscardPile && GetProgramDiscardSource(m)?.OwnerSeat is { } loser &&
        IsValidPlayerSeat(loser) && _players[loser].IsAlive && SilvMark(m.CardId) is not null && _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile &&
        _cardMovements.Last(x => x.CardId == m.CardId).Sequence == m.Sequence;
    private IReadOnlyList<ProgramTriggerCandidate> CollectSilvRefundCandidates(CardMovementBatchContext batch)
    {
        if (!HasLiWanCapability) return [];
        var result = new List<ProgramTriggerCandidate>();
        for (var i = 0; i < batch.Movements.Count; i++)
        {
            var move = batch.Movements[i];
            if (!IsRefundableSilvMovement(move) || ProgramEventHistory<LiWanStartedEvent>().Any(e => e.RefundSequence == move.Sequence)) continue;
            var mark = SilvMark(move.CardId)!; var p = _contentRegistry.GetSkill(mark.Source.SkillId).Program!;
            var t = p.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ReturnDiscardedSilvEntity }]);
            result.Add(new(mark.Source.OwnerSeat, mark.Source.SkillId, t.Id, mark.Source.SkillInstanceId, mark.GameplayHash, t.Priority, i));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
