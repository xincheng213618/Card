namespace CardGame.Core;

public sealed record ProgramBlindHandTakeReceipt(int InstructionIndex,
    IReadOnlyList<int> TargetSeats, IReadOnlyList<int> CardIds, IReadOnlyList<CardLocation> SourceLocations);
public interface IAwaitedBlindHandTakeProgramHost
{
    SkillProgramStepOutcome TakeRandomHandCardsAndAwait(ProgramSkillFrame frame, int amountPerTarget, CardMoveReason reason);
}
public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IAwaitedBlindHandTakeProgramHost
    {
        public SkillProgramStepOutcome TakeRandomHandCardsAndAwait(ProgramSkillFrame frame, int amountPerTarget, CardMoveReason reason) =>
            engine.BeginAwaitedBlindHandTake(frame, amountPerTarget, reason);
    }
    private SkillProgramStepOutcome BeginAwaitedBlindHandTake(ProgramSkillFrame frame, int amountPerTarget, CardMoveReason reason)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (amountPerTarget != 1 || frame.BlindHandTake is not null || frame.SelectedTargetSeats.Count > 2 ||
            frame.SelectedTargetSeats.Distinct().Count() != frame.SelectedTargetSeats.Count ||
            frame.SelectedTargetSeats.Any(s => !IsValidPlayerSeat(s) || s == frame.OwnerSeat))
            throw new InvalidOperationException("An awaited blind take requires zero to two distinct other participants.");
        var cards = new List<(Card Card, CardLocation From)>(); var seats = new List<int>();
        foreach (var seat in frame.SelectedTargetSeats)
        {
            var hand = GetHand(_players[seat]);
            if (!_players[seat].IsAlive || hand.Count == 0) continue;
            cards.Add((hand[_random.Next(hand.Count)], CardLocation.Hand(seat))); seats.Add(seat);
        }
        ReplaceRuntimeTop(frame with { BlindHandTake = new(frame.InstructionIndex,
            Array.AsReadOnly(seats.ToArray()), Array.AsReadOnly(cards.Select(c => c.Card.Id).ToArray()),
            Array.AsReadOnly(cards.Select(c => c.From).ToArray())) });
        if (cards.Count > 0)
        {
            var destination = CardLocation.Hand(frame.OwnerSeat);
            var batch = BeginCardMovementBatch(cards.Select(c => c.From), [destination]);
            var movements = new List<CardMovementRecord>(); var committed = false;
            try
            {
                _cardZones.MoveBatch(cards.Select(c => new CardTransfer(c.Card.Id, c.From, destination)));
                foreach (var item in cards) movements.Add(RecordMovement(item.Card, item.From, destination, reason));
                committed = true;
            }
            finally { CompleteCardMovementBatch(batch, movements, committed); }
        }
        AdvanceEventRulesAndQueueFact(new ProgramRandomHandCardsTakenEvent(frame.Id, frame.SkillId,
            GetProgramBindingId(frame), frame.OwnerSeat, Array.AsReadOnly(seats.ToArray()), cards.Count));
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeAwaitedBlindHandTake(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.BlindHandTake is not { } receipt) return false;
        AssertAwaitedBlindHandTake(frame);
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        ReplaceRuntimeTop(frame with { BlindHandTake = null });
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "得牌子窗口结清后，来源失效或游戏结束，剩余结算取消。"); return true;
        }
        return false;
    }
    private void AssertAwaitedBlindHandTake(ProgramSkillFrame frame)
    {
        if (frame.BlindHandTake is not { } receipt) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (receipt.InstructionIndex != frame.InstructionIndex || receipt.InstructionIndex < 1 || receipt.InstructionIndex > plan.Instructions.Count ||
            plan.Instructions[receipt.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets, AwaitMovementTriggers: true } ||
            receipt.TargetSeats.Count != receipt.CardIds.Count || receipt.CardIds.Count != receipt.SourceLocations.Count ||
            receipt.CardIds.Count > 2 || receipt.CardIds.Distinct().Count() != receipt.CardIds.Count ||
            receipt.TargetSeats.Distinct().Count() != receipt.TargetSeats.Count ||
            receipt.SourceLocations.Where((from, i) => from != CardLocation.Hand(receipt.TargetSeats[i])).Any())
            throw new InvalidOperationException("An awaited blind hand take lost its frozen exact instruction or source batch.");
    }
}
