using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramJudgmentOutcomeClaimReceipt(long JudgmentFrameId, long RootProgramFrameId,
    string OutcomeBind, int RootInstructionIndex, long ClaimProgramFrameId, string SkillId, string BindingId,
    string SkillInstanceId, string GameplayHash, int ClaimInstructionIndex, int OwnerSeat, int CardId, int MovementSequence);

public sealed record ProgramJudgmentOutcomeClaimRecordedEvent(ProgramJudgmentOutcomeClaimReceipt Receipt) : IGameEvent;

public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramJudgmentOutcomeClaimReceipt? OutcomeClaim { get; init; }
}

public sealed partial class GameEngine
{
    private ProgramSkillFrame? JudgmentOutcomeRoot(JudgmentFrame judgment)
    {
        var root = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == judgment.ParentFrameId);
        return root is not null && JudgmentOutcomeInstruction(root, root.InstructionIndex) is not null &&
            IsValidProgramJudgmentContinuation(judgment, root) ? root : null;
    }

    private void CaptureProgramJudgmentOutcomeClaim(ProgramSkillFrame claimant, JudgmentFrame judgment, Card card, int sequenceBefore)
    {
        if (JudgmentOutcomeRoot(judgment) is not { } root) return;
        var pending = GetJudgmentFrame(judgment.Id);
        var moves = _cardMovements.Where(move => move.Sequence > sequenceBefore && move.CardId == card.Id).ToArray();
        if (pending.OutcomeClaim is not null || moves is not [var move] ||
            move.From != CardLocation.Judgment(judgment.TargetSeat) || move.To != CardLocation.Hand(judgment.TargetSeat) ||
            move.Reason.Value != $"skill-program.{claimant.SkillId}.claimJudgmentCard")
            throw new InvalidOperationException("A judgment outcome claim requires its one native hand transfer.");
        var effect = JudgmentOutcomeInstruction(root, root.InstructionIndex)!;
        var receipt = new ProgramJudgmentOutcomeClaimReceipt(judgment.Id, root.Id, effect.OutcomeBind!, root.InstructionIndex,
            claimant.Id, claimant.SkillId, claimant.TriggerId!, claimant.SkillInstanceId, claimant.GameplayHash, claimant.InstructionIndex,
            claimant.OwnerSeat, card.Id, move.Sequence);
        ReplaceJudgmentFrame(pending with { OutcomeClaim = receipt });
        AdvanceEventRulesAndQueueFact(new ProgramJudgmentOutcomeClaimRecordedEvent(receipt));
    }

    // A claimed card may move again in the still-owned gain subtree. Its
    // final value is retained, while every actual relocation must form one
    // continuous native movement history from the exact original claim.
    private bool HasExactJudgmentOutcomeClaimDisposition(JudgmentFrame judgment)
    {
        if (judgment.OutcomeClaim is not { } receipt || JudgmentOutcomeRoot(judgment) is not { } root ||
            receipt.JudgmentFrameId != judgment.Id || receipt.RootProgramFrameId != root.Id ||
            receipt.RootInstructionIndex != root.InstructionIndex || receipt.OwnerSeat != judgment.TargetSeat ||
            receipt.CardId != GetJudgmentCard(judgment)?.Id ||
            JudgmentOutcomeInstruction(root, root.InstructionIndex)?.OutcomeBind != receipt.OutcomeBind ||
            _contentRegistry.Skills.GetValueOrDefault(receipt.SkillId)?.Program is not { } claimProgram || claimProgram.GameplayHash != receipt.GameplayHash ||
            ProgramEventHistory<ProgramJudgmentOutcomeClaimRecordedEvent>().Count(fact => fact.Receipt == receipt) != 1)
            return false;
        var results = ProgramEventHistory<JudgmentResolvedEvent>().Where(fact => fact.ResolutionId == judgment.Id).ToArray();
        var claims = ProgramEventHistory<ProgramJudgmentCardClaimedEvent>().Where(fact => fact.JudgmentFrameId == judgment.Id && fact.CardId == receipt.CardId).ToArray();
        if (results is not [var result] || result.ParentResolutionId != root.Id || result.TargetSeat != receipt.OwnerSeat ||
            result.CardId != receipt.CardId || result.Reason != judgment.Reason || result.Suit is null ||
            claims is not [var claim] || claim.SkillId != receipt.SkillId || claim.OwnerSeat != receipt.OwnerSeat ||
            _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().SingleOrDefault(candidateWindow => candidateWindow.ParentFrameId == judgment.Id) is not { } window ||
            !window.Candidates.Take(window.CandidateIndex + 1).Any(candidate => candidate.OwnerSeat == receipt.OwnerSeat &&
                candidate.SkillId == receipt.SkillId && candidate.TriggerId == receipt.BindingId && candidate.SkillInstanceId == receipt.SkillInstanceId &&
                candidate.GameplayHash == receipt.GameplayHash && claimProgram.Triggers.Single(trigger => trigger.Id == candidate.TriggerId)
                    .Effects.ElementAtOrDefault(receipt.ClaimInstructionIndex - 1)?.Op == SkillProgramEffectOp.ClaimJudgmentCard))
            return false;
        var started = ProgramEventHistory<ProgramBindingStartedEvent>().Where(fact => fact.FrameId == receipt.ClaimProgramFrameId).ToArray();
        if (started is not [var issued] || issued.SkillId != receipt.SkillId || issued.BindingId != receipt.BindingId ||
            issued.SkillInstanceId != receipt.SkillInstanceId || issued.OwnerSeat != receipt.OwnerSeat ||
            issued.Window != SkillProgramTriggerWindow.JudgmentFinalized) return false;
        var claimant = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == receipt.ClaimProgramFrameId);
        if (claimant is not null)
        {
            if (claimant.SkillId != receipt.SkillId || claimant.TriggerId != receipt.BindingId ||
                claimant.SkillInstanceId != receipt.SkillInstanceId || claimant.GameplayHash != receipt.GameplayHash ||
                claimant.OwnerSeat != receipt.OwnerSeat || claimant.InstructionIndex < receipt.ClaimInstructionIndex ||
                claimant.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentFinalized } context ||
                context.ParentFrameId != window.Id || context.Judgment != window.Judgment)
                return false;
            if (claimant.InstructionIndex == receipt.ClaimInstructionIndex &&
                (claimant.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } movement ||
                 movement.SubjectSeat != receipt.OwnerSeat)) return false;
        }
        else if (!ProgramEventHistory<ProgramBindingResolvedEvent>().Any(fact => fact.FrameId == receipt.ClaimProgramFrameId &&
                     fact.SkillId == receipt.SkillId && fact.BindingId == receipt.BindingId && fact.SkillInstanceId == receipt.SkillInstanceId &&
                     fact.OwnerSeat == receipt.OwnerSeat && fact.Window == SkillProgramTriggerWindow.JudgmentFinalized && fact.Completed))
            return false;
        var rootIndex = _resolutionStack.FindIndex(frame => frame.Id == root.Id);
        if (rootIndex < 0 || rootIndex + 1 >= _resolutionStack.Count || _resolutionStack[rootIndex + 1].Id != judgment.Id) return false;
        for (var index = rootIndex + 1; index < _resolutionStack.Count; index++)
            if (!JudgmentOutcomeDamageEdge(index, root)) return false;
        var moves = _cardMovements.Where(move => move.CardId == receipt.CardId && move.Sequence >= receipt.MovementSequence).ToArray();
        if (moves.Length == 0 || moves[0].Sequence != receipt.MovementSequence ||
            moves[0].From != CardLocation.Judgment(receipt.OwnerSeat) || moves[0].To != CardLocation.Hand(receipt.OwnerSeat) ||
            moves[0].Reason.Value != $"skill-program.{receipt.SkillId}.claimJudgmentCard") return false;
        var location = moves[0].To;
        foreach (var move in moves.Skip(1))
        {
            if (move.From != location || !ProgramEventHistory<CardMovedEvent>().Any(fact => fact.CardId == move.CardId &&
                    fact.From == move.From && fact.To == move.To && fact.Reason == move.Reason)) return false;
            location = move.To;
        }
        return _cardZones.GetLocation(receipt.CardId) == location;
    }
}
