namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string JoinedTrickRewardReason = "skill-program.joined-trick.damage-reward";
    private long JoinedTrickMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;

    private bool HasJoinedTrickOwnerDamage(CardUseFrame use, int owner) =>
        use.Action is { } action && CompleteProgramEventHistory().OfType<CompletedUndamagedUseDamageRecordedEvent>().Any(e =>
            e.CardUseFrameId == use.Id && e.CardActionId == action.ActionId && e.EffectiveKind == use.CardKind && e.TargetSeat == owner && e.Amount > 0);

    private void AssertJoinedTrickDamageBenefits(CardUseFrame use)
    {
        if (use.JoinedTrickDamageBenefits is null && !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage)) return;
        var issued = CompleteProgramEventHistory().OfType<JoinedTrickDamageBenefitIssuedEvent>().Where(e => e.Benefit.CardUseFrameId == use.Id).Select(e => e.Benefit).ToArray();
        if (use.JoinedTrickDamageBenefits is not { } benefits)
        {
            if (issued.Length != 0) throw new InvalidOperationException("A joined trick cannot lose its issued owning benefit.");
            return;
        }
        if (!TracksUniqueLeaderTrickTargets || benefits.Count == 0 || use.Action is not { Type: CardActionType.Use } action ||
            !IsOrdinaryTrick(use.CardKind) || action.EffectiveKind != use.CardKind || !benefits.SequenceEqual(issued) ||
            benefits.Select(b => (b.Source.OwnerSeat, b.Source.SkillId)).Distinct().Count() != benefits.Count)
            throw new InvalidOperationException("A joined trick lost its actual Use or independent issued owner benefits.");
        foreach (var benefit in benefits)
        {
            var facts = CompleteProgramEventHistory().OfType<UniqueLeaderTrickTargetResolvedEvent>().Where(e => e.ProgramFrameId == benefit.ProgramFrameId).ToArray();
            if (benefit.CardUseFrameId != use.Id || benefit.ActionId != action.ActionId ||
                facts is not [var fact] || fact.Operation != SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage ||
                fact.Source != benefit.Source || fact.GameplayHash != benefit.GameplayHash ||
                !IsUniqueLeaderTrickTargetFact(use, fact, fact.OriginalTargetSeats) ||
                (use.CardKind == CardKind.BorrowedSword ? fact.AddedTargetSeats.Take(1) : fact.AddedTargetSeats).Single() != benefit.Source.OwnerSeat)
                throw new InvalidOperationException("A joined trick benefit lost its exact paid designation, source instance or original qualification.");
        }
    }

    private void CollectIssuedJoinedTrickDamageRewardCandidates(CardActionContext action, SkillProgramTriggerWindow window,
        List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCompleted || _winner != Winner.None ||
            _resolutionStack.LastOrDefault() is not CardUseFrame { JoinedTrickDamageBenefits: { } benefits } use ||
            use.Action?.ActionId != action.ActionId) return;
        AssertJoinedTrickDamageBenefits(use);
        foreach (var benefit in benefits)
        {
            var source = benefit.Source;
            if (!_players[source.OwnerSeat].IsAlive || !HasJoinedTrickOwnerDamage(use, source.OwnerSeat)) continue;
            var program = _contentRegistry.GetSkill(source.SkillId).Program!;
            foreach (var trigger in program.Triggers.Where(t => t.Window == window && t.Effects is [{ Op: SkillProgramEffectOp.DrawAfterJoinedTrickDamage }]))
            {
                // A newly acquired instance cannot replace the source that joined
                // this Use and issued its still-owed completion benefit.
                result.RemoveAll(c => c.OwnerSeat == source.OwnerSeat && c.SkillId == source.SkillId &&
                    c.TriggerId == trigger.Id && c.SkillInstanceId != source.SkillInstanceId);
                if (result.Any(c => c.OwnerSeat == source.OwnerSeat && c.SkillId == source.SkillId && c.SkillInstanceId == source.SkillInstanceId && c.TriggerId == trigger.Id)) continue;
                var eventTarget = action.TargetSeats.Count == 1 ? action.TargetSeats.Single() : -1;
                var context = CreateCardActionProgramContext(action, window, 0, source.OwnerSeat, eventTarget,
                    CaptureProgramTriggerFacts(_players[source.OwnerSeat], action) with { CardUseCausedDamage = use.CausedDamage });
                result.Add(new(source.OwnerSeat, eventTarget, source.SkillId, trigger.Id, program.GameplayHash, source.SkillInstanceId, trigger.Priority, context));
            }
        }
    }

    private JoinedTrickDamageBenefit? ExactIssuedJoinedTrickDamageRewardCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context, bool paid)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } card } ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || !paid && (!_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None) ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.Continuation is not (ProgramCardContinuation.CompletedCard or ProgramCardContinuation.CompletedSlash) || window.CompletedResponseReturn is not null ||
            window.ParentFrameId != card.ParentCardUseFrameId || window.Action.ActionId != card.CardActionId ||
            LifecycleCardUse(window.ParentFrameId) is not { JoinedTrickDamageBenefits: { } benefits, Action: { Type: CardActionType.Use } action } use ||
            use.Step != ResolutionFrameStep.Completed || action.ActionId != card.CardActionId || !IsOrdinaryTrick(use.CardKind) ||
            card.ActorSeat != action.ActorSeat || card.EffectiveKind != action.EffectiveKind || context.SourceSeat != action.ActorSeat ||
            context.OwnerSeat != candidate.OwnerSeat || !window.Action.TargetSeats.SequenceEqual(action.TargetSeats) ||
            !window.Action.PhysicalCards.SequenceEqual(action.PhysicalCards) || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count)
            return null;
        var benefit = benefits.SingleOrDefault(b => b.Source.OwnerSeat == candidate.OwnerSeat && b.Source.SkillId == candidate.SkillId &&
            b.Source.SkillInstanceId == candidate.SkillInstanceId && b.GameplayHash == candidate.GameplayHash);
        var current = window.Candidates[window.CandidateIndex];
        var program = _contentRegistry.GetSkill(candidate.SkillId).Program;
        var trigger = program?.Triggers.SingleOrDefault(t => t.Id == candidate.BindingId);
        var index = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (benefit is null || current.OwnerSeat != candidate.OwnerSeat || current.SkillId != candidate.SkillId || current.TriggerId != candidate.BindingId ||
            current.SkillInstanceId != candidate.SkillInstanceId || current.GameplayHash != candidate.GameplayHash ||
            CreateCardActionProgramContext(window, current) != context || program?.GameplayHash != candidate.GameplayHash ||
            trigger?.Effects is not [{ Op: SkillProgramEffectOp.DrawAfterJoinedTrickDamage }] || index < 1 || _resolutionStack[index - 1].Id != use.Id ||
            !HasJoinedTrickOwnerDamage(use, candidate.OwnerSeat) || !HasExactAcceptedActualHandGainUse(use, action) ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardKind == use.CardKind && e.CardId == use.CardId) != 1)
            return null;
        AssertJoinedTrickDamageBenefits(use);
        return benefit;
    }

    private bool HasIssuedJoinedTrickDamageRewardCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        ExactIssuedJoinedTrickDamageRewardCandidate(candidate, context, false) is not null;

    private bool CanOfferJoinedTrickDamageReward(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage) ||
        ExactIssuedJoinedTrickDamageRewardCandidate(candidate, context, false) is { } benefit &&
            !CompleteProgramEventHistory().OfType<JoinedTrickDamageRewardDrawIssuedEvent>().Any(e => e.Benefit == benefit);

    private bool CanContinueIssuedJoinedTrickDamageReward(ProgramSkillFrame frame) =>
        frame.JoinedTrickDamageRewardReceipt is not null && ValidJoinedTrickDamageReward(frame) ||
        frame.TriggerId is { } binding && frame.WindowContext is { } context &&
            HasIssuedJoinedTrickDamageRewardCandidate(new(frame.OwnerSeat, frame.SkillId, binding, frame.SkillInstanceId, frame.GameplayHash, 0), context);

    private JoinedTrickDamageBenefit? ExactJoinedTrickDamageRewardParent(ProgramSkillFrame frame)
    {
        if (frame.InstructionIndex != 1 || frame.TriggerId is null || frame.ActivationId != frame.TriggerId || frame.WindowContext is not { } context ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0) return null;
        var benefit = ExactIssuedJoinedTrickDamageRewardCandidate(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId, frame.GameplayHash, 0),
            context, frame.JoinedTrickDamageRewardReceipt is not null);
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        return benefit is not null && index > 0 && _resolutionStack[index - 1].Id == context.ParentFrameId &&
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat &&
                e.SkillId == frame.SkillId && e.BindingId == frame.TriggerId && e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) == 1 ? benefit : null;
    }

    private SkillProgramStepOutcome BeginJoinedTrickDamageReward(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        AssertJoinedTrickDamageReward(frame);
        var benefit = ExactJoinedTrickDamageRewardParent(frame) ?? throw new InvalidOperationException("A joined trick reward requires its exact completed Use and same-use owner damage.");
        if (frame.JoinedTrickDamageRewardReceipt is not null || frame.PendingMovementContinuation is not null ||
            CompleteProgramEventHistory().OfType<JoinedTrickDamageRewardDrawIssuedEvent>().Any(e => e.Benefit == benefit))
            throw new InvalidOperationException("A joined trick cannot issue its completed damage reward twice.");
        var before = JoinedTrickMovementSequence;
        ReplaceRuntimeTop(frame = frame with { JoinedTrickDamageRewardReceipt = new(1, benefit, 2, 0, before, before),
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        var actual = DrawCards(_players[frame.OwnerSeat], 2, true, new(JoinedTrickRewardReason)).Count;
        frame = GetActiveProgramFrame(supplied.Id);
        var receipt = frame.JoinedTrickDamageRewardReceipt! with { DrawActual = actual, MovementSequenceAfter = JoinedTrickMovementSequence };
        ReplaceRuntimeTop(frame with { JoinedTrickDamageRewardReceipt = receipt });
        AdvanceEventRulesAndQueueFact(new JoinedTrickDamageRewardDrawIssuedEvent(frame.Id, benefit, 2, actual, before, receipt.MovementSequenceAfter));
        AdvanceRuntimeProgram(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeJoinedTrickDamageReward(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id || frame.JoinedTrickDamageRewardReceipt is null) return false;
        AssertJoinedTrickDamageReward(frame);
        if (PreparationGameEnded()) return true;
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return true;
        frame = GetActiveProgramFrame(id);
        var receipt = frame.JoinedTrickDamageRewardReceipt!;
        AdvanceEventRulesAndQueueFact(new JoinedTrickDamageRewardResolvedEvent(id, receipt.Benefit, 2, receipt.DrawActual));
        ReplaceRuntimeTop(frame = frame with { JoinedTrickDamageRewardReceipt = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true); return true;
    }

    private bool ReturnJoinedTrickDamageRewardMovement(ProgramSkillFrame frame)
    {
        if (frame.JoinedTrickDamageRewardReceipt is null || frame.PendingMovementContinuation is null) return false;
        AssertJoinedTrickDamageReward(frame); AdvanceRuntimeProgram(frame.Id); return true;
    }

    private bool ValidJoinedTrickDamageReward(ProgramSkillFrame frame)
    {
        if (frame.JoinedTrickDamageRewardReceipt is not { } receipt || ExactJoinedTrickDamageRewardParent(frame) != receipt.Benefit ||
            receipt.InstructionIndex != frame.InstructionIndex || receipt.InstructionIndex != 1 || receipt.FrozenDrawCount != 2 ||
            receipt.DrawActual < 0 || receipt.DrawActual > 2 || receipt.MovementSequenceBefore < 0 || receipt.MovementSequenceAfter < receipt.MovementSequenceBefore ||
            receipt.MovementSequenceAfter > JoinedTrickMovementSequence ||
            frame.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != frame.OwnerSeat) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<JoinedTrickDamageRewardDrawIssuedEvent>().Where(e => e.Benefit == receipt.Benefit).ToArray() is not [var issued] ||
            issued != new JoinedTrickDamageRewardDrawIssuedEvent(frame.Id, receipt.Benefit, 2, receipt.DrawActual, receipt.MovementSequenceBefore, receipt.MovementSequenceAfter) ||
            history.OfType<JoinedTrickDamageRewardResolvedEvent>().Any(e => e.Benefit == receipt.Benefit)) return false;
        var invoice = _cardMovements.Where(m => m.Sequence > receipt.MovementSequenceBefore && m.Sequence <= receipt.MovementSequenceAfter &&
            m.Reason.Value == JoinedTrickRewardReason).ToArray();
        return invoice.Length == receipt.DrawActual && invoice.Select(m => m.CardId).Distinct().Count() == receipt.DrawActual &&
            invoice.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat)) &&
            (receipt.DrawActual != 0 || receipt.MovementSequenceBefore == receipt.MovementSequenceAfter);
    }

    private void AssertJoinedTrickDamageReward(ProgramSkillFrame frame)
    {
        if (frame.JoinedTrickDamageRewardReceipt is null && (frame.TriggerId is null ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == frame.TriggerId)?.Effects
                .Any(e => e.Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage) != true)) return;
        if (frame.JoinedTrickDamageRewardReceipt is null)
        {
            if (CompleteProgramEventHistory().OfType<JoinedTrickDamageRewardDrawIssuedEvent>().Any(e => e.ProgramFrameId == frame.Id) &&
                !CompleteProgramEventHistory().OfType<JoinedTrickDamageRewardResolvedEvent>().Any(e => e.ProgramFrameId == frame.Id))
                throw new InvalidOperationException("A paid joined trick reward cannot lose its owning draw receipt.");
            return;
        }
        if (!ValidJoinedTrickDamageReward(frame)) throw new InvalidOperationException("A joined trick reward lost its exact source, same-use damage or once-paid native draw invoice.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && !JoinedTrickDamageRewardFirstChild(frame, _resolutionStack[index + 1]))
            throw new InvalidOperationException("A joined trick reward retained an unrelated native child.");
    }

    private bool IsJoinedTrickDamageRewardAwaitedMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.PendingMovementContinuation == pending && effect?.Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage && ValidJoinedTrickDamageReward(frame);
}
