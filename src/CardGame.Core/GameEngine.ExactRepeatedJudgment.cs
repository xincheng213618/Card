namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IRepeatedJudgmentClaimPolicyProgramHost
    {
        public SkillProgramStepOutcome RepeatJudgmentWithClaimPolicy(ProgramSkillFrame frame, string reason, string bind,
            IReadOnlyList<Suit> suits, SkillProgramClaimHandLimitExemption policy) =>
            engine.BeginProgramExactRepeatedJudgment(frame, reason, bind, suits, policy);
    }

    private SkillProgramStepOutcome BeginProgramExactRepeatedJudgment(ProgramSkillFrame frame, string reason,
        string bind, IReadOnlyList<Suit> suits, SkillProgramClaimHandLimitExemption policy)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (policy != SkillProgramClaimHandLimitExemption.ActualTurn || active.RepeatedJudgment is not null || suits.Count == 0 ||
            frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || frame.OwnerSeat != _currentSeat ||
            frame.OwnerSeat != _turnProgression.OwnerSeat || _turnProgression.TurnNumber != _turnNumber || !_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Exact repeated judgment requires its actual owner preparation turn.");
        active = active with { RepeatedJudgment = new ProgramRepeatedJudgment(reason, bind, Array.AsReadOnly(suits.ToArray()), 0)
        { ClaimHandLimitExemption = policy, ClaimTurn = new(_turnNumber, _turnProgression.OwnerSeat) } };
        ReplaceRuntimeTop(active);
        return StartProgramJudgment(active, frame.OwnerSeat, reason, bind, SkillProgramCardSetVisibility.Public, frame.OwnerSeat);
    }

    private void CaptureProgramRepeatedJudgmentClaimOutcome(JudgmentFrame judgment, Card card, Suit suit)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == judgment.ParentFrameId) is not
            { RepeatedJudgment: { ClaimHandLimitExemption: not null } repeated } frame) return;
        if (judgment.Continuation != JudgmentContinuationKind.ProgramSkill || judgment.TargetSeat != frame.OwnerSeat ||
            judgment.SourceSeat != frame.OwnerSeat || judgment.Reason != repeated.Reason || judgment.ProgramResultBind != repeated.ResultBind ||
            repeated.LastMatched is not null || repeated.FinalOutcome is not null || repeated.LastClaimReceipt is not null)
            throw new InvalidOperationException("Exact repeated judgment final outcome lost its owning parent.");
        ReplaceRuntimeFrame(frame.Id, frame with { RepeatedJudgment = repeated with
        { FinalOutcome = new(judgment.Id, card.Id, card.Kind, suit, repeated.SuccessSuits.Contains(suit)) } });
    }

    private bool ExactRepeatedJudgmentSourceLive(ProgramSkillFrame frame, ProgramRepeatedJudgment state) =>
        state.ClaimTurn is { } turn && turn.TurnNumber == _turnNumber && turn.TurnOwnerSeat == _turnProgression.OwnerSeat &&
        _turnProgression.TurnNumber == _turnNumber && _players[frame.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId);

    private void ResumeProgramExactRepeatedJudgment(JudgmentFrame judgment, ProgramSkillFrame frame, ProgramRepeatedJudgment state)
    {
        if (GetJudgmentCard(judgment) is not { } card)
        {
            ReplaceRuntimeTop(frame with { RepeatedJudgment = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        var outcome = state.FinalOutcome ?? throw new InvalidOperationException("Exact repeated judgment lost its finalized outcome.");
        if (outcome.JudgmentFrameId != judgment.Id || outcome.CardId != card.Id || outcome.CardKind != card.Kind || state.LastClaimReceipt is not null)
            throw new InvalidOperationException("Exact repeated judgment returned a different final entity.");
        var live = ExactRepeatedJudgmentSourceLive(frame, state);
        var location = _cardZones.GetLocation(card.Id);
        var next = state with { CompletedCount = checked(state.CompletedCount + 1), LastMatched = live && outcome.Matched };
        ReplaceRuntimeTop(frame with { RepeatedJudgment = next });
        if (location != CardLocation.Judgment(judgment.TargetSeat))
        {
            // A different actual claimant keeps the entity; its final color can still permit another judgment.
            if (!live) CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "判定返回前技能来源或当前回合已失效。");
            else ContinueProgramRepeatedJudgmentAfterMovement(frame.Id);
            return;
        }
        var to = live && outcome.Matched ? CardLocation.Hand(frame.OwnerSeat) : CardLocation.DiscardPile;
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([card.Id], to, new($"skill-program.{frame.SkillId}.repeatJudgment"),
            (_, records) =>
            {
                if (to != CardLocation.Hand(frame.OwnerSeat)) return;
                var actual = records.SingleOrDefault(m => m.CardId == outcome.CardId && m.From == location && m.To == to && m.From != m.To);
                if (actual is null) return;
                var turn = next.ClaimTurn!;
                if (actual.TurnNumber != turn.TurnNumber || turn.TurnNumber != _turnNumber || turn.TurnOwnerSeat != _turnProgression.OwnerSeat)
                    throw new InvalidOperationException("An exact judgment receipt changed its actual turn.");
                var grant = _turnCardUseEffects.GrantHandLimitExemptCard(turn.TurnNumber, turn.TurnOwnerSeat, frame.Id,
                    frame.InstructionIndex - 1, judgment.Id, CreateProgramTurnEffectSource(frame), frame.OwnerSeat, card.Id, actual.Sequence);
                var active = GetActiveProgramFrame(frame.Id);
                ReplaceRuntimeTop(active with { RepeatedJudgment = active.RepeatedJudgment! with
                { LastClaimReceipt = new(frame.InstructionIndex - 1, judgment.Id, card.Id, actual.Sequence, actual.From, actual.To,
                    turn.TurnNumber, turn.TurnOwnerSeat, grant.GrantSequence) } });
                AdvanceEventRulesAndQueueFact(new ProgramJudgmentCardClaimedEvent(judgment.Id, frame.SkillId, frame.OwnerSeat, card.Id, card.Kind));
                AdvanceEventRulesAndQueueFact(new TurnHandLimitExemptCardGrantedEvent(grant));
            });
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private bool IsExactRepeatedJudgmentFinalizedChildSubtree(ProgramJudgmentTriggerWindowFrame window,
        JudgmentFrame? judgment, int windowIndex)
    {
        if (judgment is null || !window.Activated || window.ParentFrameId != judgment.Id ||
            windowIndex < 1 || _resolutionStack[windowIndex - 1] != judgment ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            windowIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[windowIndex + 1] is not ProgramSkillFrame child ||
            child.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentFinalized } context ||
            context.ParentFrameId != window.Id || context.Judgment != window.Judgment)
            return false;
        var candidate = window.Candidates[window.CandidateIndex];
        if (candidate.OwnerSeat != child.OwnerSeat || candidate.SkillId != child.SkillId ||
            candidate.TriggerId != child.TriggerId || candidate.SkillInstanceId != child.SkillInstanceId ||
            candidate.GameplayHash != child.GameplayHash) return false;
        var owner = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == judgment.ParentFrameId);
        if (owner is not { InstructionIndex: > 0, RepeatedJudgment:
            { ClaimHandLimitExemption: SkillProgramClaimHandLimitExemption.ActualTurn, FinalOutcome: { } outcome } state } ||
            owner.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            judgment.TargetSeat != owner.OwnerSeat || window.Judgment.SubjectSeat != owner.OwnerSeat ||
            outcome.JudgmentFrameId != judgment.Id || outcome.JudgmentFrameId != window.Judgment.JudgmentFrameId ||
            outcome.CardId != judgment.CardId || outcome.CardId != window.Judgment.CardId ||
            outcome.CardKind != judgment.CardKind || outcome.CardKind != window.Judgment.CardKind ||
            outcome.FinalSuit != judgment.Suit || outcome.FinalSuit != window.Judgment.Suit)
            return false;
        var plan = ProgramInstructionResolver.Default.Resolve(owner, _contentRegistry.GetSkill(owner.SkillId).Program!);
        if (owner.InstructionIndex > plan.Instructions.Count) return false;
        var effect = plan.Instructions[owner.InstructionIndex - 1];
        return effect.Op == SkillProgramEffectOp.RepeatJudgment &&
            effect.ClaimHandLimitExemption == SkillProgramClaimHandLimitExemption.ActualTurn &&
            effect.JudgmentReason == state.Reason && effect.ResultBind == state.ResultBind &&
            effect.Suits.SequenceEqual(state.SuccessSuits);
    }

    private void CleanupExactRepeatedJudgment(ProgramSkillFrame frame)
    {
        if (frame.RepeatedJudgment is not { ClaimHandLimitExemption: not null,
            FinalOutcome: { } outcome, LastClaimReceipt: null }) return;
        var from = CardLocation.Judgment(frame.OwnerSeat);
        if (_cardZones.GetLocation(outcome.CardId) != from) return;
        var card = _cardZones.CardsAt(from).Single(c => c.Id == outcome.CardId);
        if (card.Kind != outcome.CardKind)
            throw new InvalidOperationException("Cancelled exact judgment changed its physical entity.");
        // Cleanup follows the established non-awaiting bound-card cleanup boundary.
        // Only the still-unclaimed owning outcome is moved; a real claimant retains its card.
        MoveCard(card, from, CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{frame.SkillId}.repeatJudgment.cancel-cleanup"));
    }

    private void AssertExactRepeatedJudgmentReceipt(ProgramSkillFrame frame, IReadOnlyList<SkillProgramEffect> instructions)
    {
        if (frame.RepeatedJudgment is not { ClaimHandLimitExemption: not null } state) return;
        var index = frame.InstructionIndex - 1;
        if (index < 0 || index >= instructions.Count || instructions[index] is not
            { Op: SkillProgramEffectOp.RepeatJudgment, ClaimHandLimitExemption: SkillProgramClaimHandLimitExemption.ActualTurn } effect ||
            state.ClaimHandLimitExemption != effect.ClaimHandLimitExemption || state.Reason != effect.JudgmentReason ||
            state.ResultBind != effect.ResultBind || !state.SuccessSuits.SequenceEqual(effect.Suits) ||
            frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            state.ClaimTurn is not { } turn || turn.TurnNumber != _turnNumber || turn.TurnOwnerSeat != _turnProgression.OwnerSeat)
            throw new InvalidOperationException("Exact repeated judgment lost its instruction or actual turn.");
        if (state.FinalOutcome is { } outcome && (outcome.JudgmentFrameId <= 0 || outcome.CardId <= 0 ||
            !Enum.IsDefined(outcome.FinalSuit) || outcome.Matched != state.SuccessSuits.Contains(outcome.FinalSuit)))
            throw new InvalidOperationException("Exact repeated judgment has an invalid final outcome.");
        if (state.LastClaimReceipt is not { } receipt) return;
        var grant = _turnCardUseEffects.HandLimitExemptCards.SingleOrDefault(g => g.GrantSequence == receipt.GrantSequence);
        if (state.FinalOutcome is not { Matched: true } final || state.LastMatched != true || receipt.InstructionIndex != index ||
            final.JudgmentFrameId != receipt.JudgmentFrameId || final.CardId != receipt.CardId ||
            receipt.From != CardLocation.Judgment(frame.OwnerSeat) || receipt.To != CardLocation.Hand(frame.OwnerSeat) ||
            receipt.TurnNumber != turn.TurnNumber || receipt.TurnOwnerSeat != turn.TurnOwnerSeat ||
            !_cardMovements.Any(m => m.Sequence == receipt.MovementSequence && m.CardId == receipt.CardId && m.CardKind == final.CardKind &&
                m.From == receipt.From && m.To == receipt.To && m.TurnNumber == receipt.TurnNumber) || grant is null ||
            grant.ParentFrameId != frame.Id || grant.EffectIndex != index || grant.JudgmentFrameId != final.JudgmentFrameId ||
            grant.CardId != final.CardId || grant.MovementSequence != receipt.MovementSequence || grant.BeneficiarySeat != frame.OwnerSeat ||
            grant.TurnNumber != turn.TurnNumber || grant.TurnSeat != turn.TurnOwnerSeat || grant.Source != CreateProgramTurnEffectSource(frame))
            throw new InvalidOperationException("Exact repeated judgment receipt differs from its actual movement or grant.");
    }
}
