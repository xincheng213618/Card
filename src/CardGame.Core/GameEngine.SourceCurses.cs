namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly List<SourceCurseDeposit> _sourceCurses = [];
    private readonly List<SourceCurseLoss> _sourceCurseLosses = [];
    private bool HasSourceCurseCapability =>
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.DepositSelectedSourceCurse);
    private bool HasSourceCurseActionColors => HasSourceCurseCapability && _sourceCurses.Count > 0;
    private SourceCurseDeposit? SourceCurseAt(int seat) =>
        _sourceCurses.SingleOrDefault(c => c.TargetSeat == seat && _cardZones.GetLocation(c.CardId) == c.Location);
    private long SourceCurseMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static bool SameSourceCurse(SourceCurseDeposit a, SourceCurseDeposit b) =>
        (a with { ActualDrawCount = 0 }) == (b with { ActualDrawCount = 0 });
    private IReadOnlyList<SourceCurseSnapshot>? CreateSourceCurseSnapshots(int seat)
    {
        var cards = _sourceCurses.Where(c => c.TargetSeat == seat).Select(c =>
            new SourceCurseSnapshot(c.Source.OwnerSeat, c.ActualDrawCount, ToSnapshot(_cardZones.CardsAt(c.Location).Single(card => card.Id == c.CardId)))).ToArray();
        return cards.Length == 0 ? null : Array.AsReadOnly(cards);
    }
    private void ObserveSourceCurseMovement(CardMovementRecord movement)
    {
        if (_sourceCurses.SingleOrDefault(c => c.CardId == movement.CardId && c.Location == movement.From) is not { } curse ||
            movement.To == movement.From) return;
        _sourceCurses.Remove(curse);
        var loss = new SourceCurseLoss(curse, movement.TurnNumber, movement.Sequence, movement.To, movement.Reason.Value);
        _sourceCurseLosses.Add(loss);
        AdvanceEventRulesAndQueueFact(new SourceCurseLostEvent(loss));
    }
    private Card? TakeSourceCurseForJudgment(JudgmentFrame frame)
    {
        var curse = SourceCurseAt(frame.TargetSeat);
        if (curse is null) return null;
        var card = _cardZones.CardsAt(curse.Location).Single(c => c.Id == curse.CardId);
        ReplaceJudgmentFrame(frame with { SourceCurseOrigin = curse });
        // The issued foreign entity survives loss of its issuer. It is a real
        // initial judgment card, before any standard replacement opportunity.
        MoveCard(card, curse.Location, CardLocation.Processing, new("program.source-curse.judgment"));
        AdvanceEventRulesAndQueueFact(new SourceCurseJudgmentIssuedEvent(frame.Id, curse));
        return card;
    }
    private SourceCurseDeposit? EligibleSourceCurseUse(int owner, string skill, string instance,
        CardActionContext action, SkillProgramEffect effect)
    {
        if (action.Type != CardActionType.Use || action.EffectiveIsRed is not { } color ||
            !_players[owner].IsAlive || !HasRuntimeSkillInstance(_players[owner], skill, instance)) return null;
        var curse = SourceCurseAt(action.ActorSeat);
        if (curse is null || curse.Source.OwnerSeat != owner || curse.Source.SkillId != effect.SkillIds.Single() ||
            curse.BenefitSkillId != skill || curse.BenefitSkillInstanceId != instance ||
            curse.BenefitGameplayHash != _contentRegistry.GetSkill(skill).Program?.GameplayHash ||
            SuitColor(curse.PrintedSuit) != color || curse.ActualDrawCount >= 2) return null;
        // The locked reward has its own captured source. Suppressing nonlocked
        // Zhoufu must not disable this already issued curse's Yingbing reward.
        return curse;
    }
    private SourceCurseLoss[] EligibleSourceCurseLosses(int owner, string skill, string instance, string hash) =>
        _sourceCurseLosses.Where(l => l.ActualTurn == _turnNumber && l.Deposit.Source.OwnerSeat == owner &&
            l.Deposit.Source.SkillId == skill && l.Deposit.Source.SkillInstanceId == instance &&
            l.Deposit.GameplayHash == hash).DistinctBy(l => l.Deposit.TargetSeat)
            .OrderBy(l => (l.Deposit.TargetSeat - _currentSeat + _players.Count) % _players.Count).ToArray();
    private bool SourceCurseHasFinalGameEnd()
    {
        if (_status != EngineStatus.Completed) return false;
        if (_winner == Winner.None || !CompleteProgramEventHistory().OfType<GameEndedEvent>().Any(e => e.Winner == _winner))
            throw new InvalidOperationException("A terminal curse diagnostic requires the actual final game-end fact.");
        return true;
    }
    private bool CanRunSourceCurseTrigger(ProgramTriggerCandidate candidate,
        SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        // This shared hook must leave existing non-curse trigger behavior unchanged.
        if (!trigger.Effects.Any(e => e.Op is SkillProgramEffectOp.DrawForSourceCurseUse or SkillProgramEffectOp.LoseHpForLostSourceCurses)) return true;
        if (_winner != Winner.None || _status == EngineStatus.Completed) return false;
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawForSourceCurseUse))
        {
            if (context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.SourceCurseUse is not { } frozen ||
                _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
                parent.Action.ActionId != frozen.ActionId || parent.Action.ActorSeat != frozen.ActorSeat ||
                parent.Action.EffectiveIsRed != frozen.EffectiveIsRed || parent.Action.Type != CardActionType.Use ||
                parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
                ToSharedCandidate(parent.Candidates[parent.CandidateIndex]) != candidate) return false;
            var current = EligibleSourceCurseUse(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId,
                parent.Action, trigger.Effects.Single());
            return current is not null && SameSourceCurse(current, frozen.Deposit) &&
                !CompleteProgramEventHistory().OfType<SourceCurseDrawIssuedEvent>().Any(e =>
                    e.Deposit.DepositFrameId == current.DepositFrameId && e.ActionId == frozen.ActionId);
        }
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.LoseHpForLostSourceCurses)) return true;
        return context.Window == SkillProgramTriggerWindow.AfterTurnEnded &&
            _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is { } actual &&
            IsActualAfterTurnEndedParent(actual) && AfterTurnEndedCandidate(actual) == candidate &&
            EligibleSourceCurseLosses(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, candidate.GameplayHash).Length > 0 &&
            !CompleteProgramEventHistory().OfType<SourceCurseLossRosterIssuedEvent>().Any(e =>
                e.ActualTurn == actual.TurnNumber && e.Losses.Any(l => l.Deposit.Source.OwnerSeat == candidate.OwnerSeat &&
                    l.Deposit.Source.SkillId == candidate.SkillId && l.Deposit.Source.SkillInstanceId == candidate.SkillInstanceId));
    }
    private SkillProgramStepOutcome ExecuteSourceCurse(SkillProgramEffect effect, ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SourceCurseReceipt is not null) throw new InvalidOperationException("A curse cannot issue a second paid receipt.");
        if (_winner != Winner.None || _status == EngineStatus.Completed ||
            !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (effect.Op == SkillProgramEffectOp.DepositSelectedSourceCurse) return DepositSourceCurse(f, effect);
        if (effect.Op == SkillProgramEffectOp.DrawForSourceCurseUse) return DrawSourceCurse(f, effect);
        if (effect.Op != SkillProgramEffectOp.LoseHpForLostSourceCurses || f.WindowContext is not { } context ||
            _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            !CanRunSourceCurseTrigger(AfterTurnEndedCandidate(parent), GetProgramTrigger(f), context))
            throw new InvalidOperationException("A curse loss roster requires its exact actual turn-end candidate.");
        var losses = EligibleSourceCurseLosses(f.OwnerSeat, f.SkillId, f.SkillInstanceId, f.GameplayHash);
        ReplaceRuntimeTop(f = f with { SourceCurseReceipt = new()
        { InstructionIndex = f.InstructionIndex, Stage = SourceCurseStage.LossHpChildren,
          OriginalParentId = parent.Id, ActualTurn = parent.TurnNumber, Losses = losses } });
        AdvanceEventRulesAndQueueFact(new SourceCurseLossRosterIssuedEvent(f.Id, parent.Id,
            parent.TurnNumber, Array.AsReadOnly(losses)));
        ResumeSourceCurse(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome DepositSourceCurse(ProgramSkillFrame f, SkillProgramEffect effect)
    {
        if (f.TriggerId is not null || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play ||
            f.SelectedCardIds is not [var id] || f.SelectedTargetSeats is not [var target] ||
            target == f.OwnerSeat || !_players[target].IsAlive || SourceCurseAt(target) is not null)
            throw new InvalidOperationException("A curse deposit lost its exact one-card other-target Play selection.");
        var from = _cardZones.GetLocation(id);
        var card = _cardZones.CardsAt(from).Single(c => c.Id == id);
        if (from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || card.IsGeneralWeapon)
            throw new InvalidOperationException("A curse requires one transferable owner HE entity.");
        var sourceGrant = EnabledRuntimeSkillGrants(_players[f.OwnerSeat]).Single(g =>
            g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId);
        var benefitSkill = effect.SkillIds.Single();
        var benefit = _players[f.OwnerSeat].SkillGrants.Grants.Where(g =>
            g.SkillId == benefitSkill && g.SourceId == sourceGrant.SourceId)
            .OrderBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        var deposit = new SourceCurseDeposit(f.Id, new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, target, card.Id, card.Kind, card.Suit, _turnNumber, benefitSkill,
            benefit?.SkillInstanceId, benefit is null ? null : _contentRegistry.GetSkill(benefitSkill).Program!.GameplayHash);
        _cardZones.EnsurePublicPersistentPile(deposit.Location);
        _sourceCurses.Add(deposit);
        var before = SourceCurseMovementSequence;
        ReplaceRuntimeTop(f = f with { SourceCurseReceipt = new()
        { InstructionIndex = f.InstructionIndex, Stage = SourceCurseStage.DepositChildren, Deposit = deposit,
          PaymentFrom = from, Before = before, After = before, ActualTurn = _turnNumber },
          PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, deposit.Location, new("program.source-curse.deposit"));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { SourceCurseReceipt = f.SourceCurseReceipt! with { After = SourceCurseMovementSequence } });
        AdvanceEventRulesAndQueueFact(new SourceCurseDepositedEvent(deposit, from, _cardMovements.Single(m =>
            m.Sequence > before && m.Sequence <= SourceCurseMovementSequence && m.CardId == id && m.From == from && m.To == deposit.Location).Sequence));
        if (!TryDrainFireTargetMovement(f)) ReturnSourceCurseMovement(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome DrawSourceCurse(ProgramSkillFrame f, SkillProgramEffect effect)
    {
        if (f.WindowContext is not { SourceCurseUse: { } identity } context ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !CanRunSourceCurseTrigger(ToSharedCandidate(parent.Candidates[parent.CandidateIndex]), GetProgramTrigger(f), context) ||
            EligibleSourceCurseUse(f.OwnerSeat, f.SkillId, f.SkillInstanceId, parent.Action, effect) is not { } deposit)
            throw new InvalidOperationException("Yingbing lost its original genuine use, exact curse and independent locked source.");
        var before = SourceCurseMovementSequence;
        ReplaceRuntimeTop(f = f with { SourceCurseReceipt = new()
        { InstructionIndex = f.InstructionIndex, Stage = SourceCurseStage.DrawChildren, Deposit = deposit,
          ActionId = identity.ActionId, OriginalParentId = parent.Id, Before = before, After = before,
          ActualTurn = _turnNumber }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var drawn = DrawCards(_players[f.OwnerSeat], 1, true, new("program.source-curse.draw"));
        // Count actual physical draws, before draining their paid child subtree.
        var updated = deposit with { ActualDrawCount = deposit.ActualDrawCount + drawn.Count };
        if (_sourceCurses.Contains(deposit)) _sourceCurses[_sourceCurses.IndexOf(deposit)] = updated;
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { SourceCurseReceipt = f.SourceCurseReceipt! with
        { Deposit = updated, After = SourceCurseMovementSequence, DrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new SourceCurseDrawIssuedEvent(f.Id, parent.Id, updated,
            identity.ActionId, drawn.Count, before, SourceCurseMovementSequence));
        if (!TryDrainFireTargetMovement(f)) ReturnSourceCurseMovement(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeSourceCurse(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SourceCurseReceipt: { } r } f || f.Id != id) return false;
        AssertSourceCurseReceipt(f);
        // Genuine global termination leaves paid frames as diagnostics. In
        // particular do not push a queued recovery attempt after GameEnded.
        if (SourceCurseHasFinalGameEnd()) return true;
        if (f.PendingMovementContinuation is not null)
        { if (!TryDrainFireTargetMovement(f)) ReturnSourceCurseMovement(f); return true; }
        if (r.Stage != SourceCurseStage.LossHpChildren)
            throw new InvalidOperationException("A curse movement receipt resumed without its paid movement return.");
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program)) return true;
        while (r.LossIndex < r.Losses.Count && _winner == Winner.None)
        {
            var target = r.Losses[r.LossIndex].Deposit.TargetSeat;
            var index = r.LossIndex;
            ReplaceRuntimeTop(f = f with { SourceCurseReceipt = r = r with
            { LossIndex = index + 1, CurrentHpTarget = target } });
            if (!_players[target].IsAlive || _players[target].Hp <= 0) continue;
            AdvanceEventRulesAndQueueFact(new SourceCurseHpLossIssuedEvent(f.Id, r.ActualTurn, target, index));
            if (new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, target, 1) == SkillProgramStepOutcome.AwaitChild) return true;
            if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) ||
                TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program)) return true;
        }
        FinishSourceCurse(f); return true;
    }
    private void FinishSourceCurse(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { SourceCurseReceipt = null });
        FinishProgramSkill(f, true);
    }
    private bool ReturnSourceCurseMovement(ProgramSkillFrame f)
    {
        if (f.SourceCurseReceipt is not { } r) return false;
        if (f.PendingMovementContinuation is not { } pending || !IsSourceCurseMovement(f, SourceCursePausedEffect(f), pending))
            throw new InvalidOperationException("A curse cannot consume a different movement return.");
        AssertSourceCurseReceipt(f);
        if (SourceCurseHasFinalGameEnd()) return true;
        if (TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == SourceCurseStage.DrawChildren && r.DrawCount == 1 && r.Deposit is { ActualDrawCount: 2 } due &&
            _players[f.OwnerSeat].IsAlive && _winner == Winner.None &&
            SourceCurseAt(due.TargetSeat) is { } live && SameSourceCurse(live, due))
        {
            var before = SourceCurseMovementSequence;
            ReplaceRuntimeTop(f = f with { SourceCurseReceipt = r with
            { Stage = SourceCurseStage.ObtainChildren, Before = before, After = before },
              PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(_cardZones.CardsAt(due.Location).Single(c => c.Id == due.CardId),
                due.Location, CardLocation.Hand(f.OwnerSeat), new("program.source-curse.obtain"));
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { SourceCurseReceipt = f.SourceCurseReceipt! with { After = SourceCurseMovementSequence } });
            AdvanceEventRulesAndQueueFact(new SourceCurseObtainedEvent(f.Id, due, before, SourceCurseMovementSequence));
            if (!TryDrainFireTargetMovement(f)) ReturnSourceCurseMovement(f);
            return true;
        }
        FinishSourceCurse(f); return true;
    }
    private SkillProgramEffect? SourceCursePausedEffect(ProgramSkillFrame f) => f.InstructionIndex < 1 ? null :
        ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool IsSourceCurseMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.SourceCurseReceipt is { Stage: not SourceCurseStage.LossHpChildren } r &&
        r.InstructionIndex == f.InstructionIndex && effect == SourceCursePausedEffect(f) &&
        effect?.Op == (r.Stage == SourceCurseStage.DepositChildren ? SkillProgramEffectOp.DepositSelectedSourceCurse : SkillProgramEffectOp.DrawForSourceCurseUse) &&
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private sealed partial class ProgramSkillHost : ISourceCurseProgramHost
    {
        public SkillProgramStepOutcome ExecuteSourceCurse(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.ExecuteSourceCurse(effect, frame);
    }
}
