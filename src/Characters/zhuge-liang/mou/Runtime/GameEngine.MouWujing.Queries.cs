using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouWujingAllowance(CharacterState owner) => owner.Seat == _turnProgression.OwnerSeat &&
        _mouWujingAllowances.Contains(owner.Seat) && HasMouOperation(owner, SkillProgramEffectOp.MouWujingDraw);
    private bool MouDiscardNotFromUse(CardMovementRecord move)
    {
        if (move.Reason == CardMoveReasons.UseFinished || move.Reason == CardMoveReasons.FireAttackFinished ||
            move.Reason == CardMoveReasons.IronChainFinished || move.Reason == CardMoveReasons.DelayedCardFinish ||
            move.Reason == CardMoveReasons.NullificationFinished) return false;
        if (move.Reason == CardMoveReasons.ResponseFinished && SettledActionContext(move.CardId) is { } action &&
            (action.Type == CardActionType.Use || action.Type == CardActionType.Response &&
                (action.EffectiveKind == CardKind.Nullification || IsProgramResponseCardUse(_players[action.ActorSeat], action.EffectiveKind)))) return false;
        return true;
    }
    private void CaptureMouWujingAllowance(long batchId, IReadOnlyList<CardMovementRecord> movements)
    {
        var seat = _turnProgression.OwnerSeat;
        if (!IsValidPlayerSeat(seat) || !_players[seat].IsAlive ||
            !HasMouOperation(_players[seat], SkillProgramEffectOp.MouWujingDraw) ||
            !movements.Any(move => move.To == CardLocation.DiscardPile && MouDiscardNotFromUse(move))) return;
        _mouWujingAllowances.Add(seat);
        AdvanceEventRulesAndQueueFact(new MouWujingAllowanceGrantedEvent(seat, batchId));
    }
    private int[] MatchingMouWujingIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate) =>
        _skillRuntimeState.GetUsage(candidate.OwnerSeat, candidate.SkillId, MouWujingUsage, SkillUsageScope.Turn) != 0 ? [] :
        batch.Movements.Select((move, index) => (move, index))
            .Where(item => item.move.To == CardLocation.DiscardPile && item.move.From.Zone != CardZoneKind.Hand)
            .Select(item => item.index).ToArray();
}
