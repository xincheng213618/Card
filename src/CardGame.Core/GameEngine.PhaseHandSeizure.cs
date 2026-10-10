using System.Collections.ObjectModel;
using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PhaseHandSeizureTerminalState => _status == EngineStatus.Completed && _phase == TurnPhase.Finished && _winner != Winner.None &&
        CompleteProgramEventHistory().OfType<GameEndedEvent>().Any(e => e.Winner == _winner);
    private bool IsTerminalPhaseHandDebtBoundary(ProgramLifecycleTriggerWindowFrame parent) => PhaseHandSeizureTerminalState &&
        parent.Window == SkillProgramTriggerWindow.PlayEnding && parent.OwnerSeat == _currentSeat &&
        parent.Continuation is ProgramLifecycleContinuation.CompletePlayPhase or ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.PhaseHandDebtReturn is not null && frame.ParentFrameId == parent.Id &&
            MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]) && ValidPhaseHandDebtReturnReceipt(frame));
    private long PhaseHandSeizureSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private CardConversionSource PhaseHandSeizureSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private static string PhaseHandSeizureReason(ProgramSkillFrame frame, string part) =>
        $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.DiscardTurnOverAndTakeHand}.{part}";
    private static string PhaseHandDebtReturnReason(ProgramSkillFrame frame) =>
        $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.ReturnIssuedPhaseHandDebt}";
    private bool IsPhaseHandSeizureCost(int owner, string skill, Card card) =>
        _cardZones.GetLocation(card.Id) is { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } location && seat == owner &&
        !IsSelfHandCategoryDiscardForbidden(owner, card, location, OwnedCardMoveIntent.Discard) &&
        !IsForeignEquipmentDiscardPrevented(owner, card, location, OwnedCardMoveIntent.Discard) &&
        !(location.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(owner, skill, GetRuntimeSkillInstanceId(_players[owner], skill), card));
    private bool CanActivatePhaseHandSeizure(CharacterState owner, SkillProgram program) =>
        !program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardTurnOverAndTakeHand)) ||
        !CompleteProgramEventHistory().OfType<PhaseHandSeizurePaidEvent>().Any(e => e.Source.OwnerSeat == owner.Seat && e.Source.SkillId == program.Id &&
            e.ActualTurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId);

    private IEnumerable<PhaseHandSeizureIssuedEvent> UnsettledPhaseHandSeizures()
    {
        var issued = ProgramEventHistory<PhaseHandSeizureIssuedEvent>();
        if (issued.Count == 0) return Array.Empty<PhaseHandSeizureIssuedEvent>();
        var settlements = ProgramEventHistory<PhaseHandDebtSettledEvent>();
        if (settlements.Count == 0) return issued;
        var settled = settlements.Select(e => e.SeizureFrameId).ToHashSet();
        return issued.Where(e => !settled.Contains(e.FrameId));
    }
    private PhaseHandSeizureIssuedEvent? CurrentPhaseHandSeizureDebt(int owner, string skill, string instance, string hash, string binding) =>
        _phase == TurnPhase.Play && owner == _currentSeat ? UnsettledPhaseHandSeizures().SingleOrDefault(e =>
            e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Source.SkillInstanceId == instance && e.GameplayHash == hash &&
            e.ContinuationId == binding && e.ActualTurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId) : null;
    private bool IsPhaseHandDebtResolver(ProgramTriggerCandidate candidate) =>
        _contentRegistry.Skills.GetValueOrDefault(candidate.SkillId)?.Program is { } program && program.GameplayHash == candidate.GameplayHash &&
        ProgramInstructionResolver.Default.FindTrigger(program, candidate.BindingId)?.Effects is [{ Op: SkillProgramEffectOp.ReturnIssuedPhaseHandDebt }];
    private IEnumerable<ProgramTriggerCandidate> IssuedPhaseHandDebtCandidates(CharacterState owner, SkillProgramTriggerWindow window, int occurrence)
    {
        if (window != SkillProgramTriggerWindow.PlayEnding || owner.Seat != _currentSeat || _phase != TurnPhase.Play) yield break;
        foreach (var due in UnsettledPhaseHandSeizures().Where(e => e.Source.OwnerSeat == owner.Seat && e.ActualTurnNumber == _turnNumber &&
                     e.PhaseInstanceId == _cardUseDebitPhaseInstanceId).OrderBy(e => e.FrameId))
        {
            var program = _contentRegistry.Skills.GetValueOrDefault(due.Source.SkillId)?.Program;
            if (program is null || program.GameplayHash != due.GameplayHash ||
                ProgramInstructionResolver.Default.FindTrigger(program, due.ContinuationId) is not { } trigger ||
                trigger.Window != window || trigger.Optional || trigger.Subject != SkillProgramTriggerSubject.Owner ||
                trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || trigger.Effects is not [{ Op: SkillProgramEffectOp.ReturnIssuedPhaseHandDebt }])
                throw new InvalidOperationException("An issued phase-hand obligation lost its exact immutable continuation.");
            yield return new(owner.Seat, due.Source.SkillId, due.ContinuationId, due.Source.SkillInstanceId!, due.GameplayHash, trigger.Priority, occurrence);
        }
    }
    private bool CanRunIssuedPhaseHandDebt(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        IsPhaseHandDebtResolver(candidate) && context.Window == SkillProgramTriggerWindow.PlayEnding && context.OwnerSeat == candidate.OwnerSeat &&
        context.SourceSeat == _currentSeat && _players[candidate.OwnerSeat].IsAlive &&
        CurrentPhaseHandSeizureDebt(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, candidate.GameplayHash, candidate.BindingId) is not null;
    private PhaseHandSeizureIssuedEvent RequirePhaseHandDebtParent(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.PlayEnding } context || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play && !PhaseHandSeizureTerminalState ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 || context.OwnerSeat != frame.OwnerSeat || context.SourceSeat != frame.OwnerSeat ||
            _resolutionStack.FirstOrDefault(f => f.Id == context.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.OwnerSeat != frame.OwnerSeat || parent.Window != context.Window ||
            parent.Continuation is not (ProgramLifecycleContinuation.CompletePlayPhase or ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd) || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("A phase-hand return lost its actual owner PlayEnding parent and candidate.");
        var due = frame.PhaseHandDebtReturn is { } paid
            ? CompleteProgramEventHistory().OfType<PhaseHandSeizureIssuedEvent>().SingleOrDefault(e => e.FrameId == paid.SeizureFrameId)
            : CurrentPhaseHandSeizureDebt(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, frame.TriggerId!);
        if (due is null || due.Source.OwnerSeat != frame.OwnerSeat || due.Source.SkillId != frame.SkillId || due.Source.SkillInstanceId != frame.SkillInstanceId ||
            due.GameplayHash != frame.GameplayHash || due.ContinuationId != frame.TriggerId || due.ActualTurnNumber != _turnNumber ||
            due.PhaseInstanceId != _cardUseDebitPhaseInstanceId) throw new InvalidOperationException("A phase-hand return replaced its original issued identity.");
        return due;
    }

    private SkillProgramStepOutcome BeginPhaseHandSeizure(ProgramSkillFrame supplied, string continuation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.PhaseHandSeizure is not null || frame.PhaseHandDebtReturn is not null || frame.InstructionIndex != 1 || frame.TriggerId is not null ||
            frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || frame.SelectedCardIds is not [var cardId] || frame.SelectedTargetSeats is not [var target] ||
            !IsValidPlayerSeat(target) || !_players[target].IsAlive || _players[target].Gender != GeneralGender.Male ||
            !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !CanActivatePhaseHandSeizure(_players[frame.OwnerSeat], _contentRegistry.GetSkill(frame.SkillId).Program!) ||
            !IsPhaseHandSeizureCost(frame.OwnerSeat, frame.SkillId, GetAdvancedCard(cardId)))
            throw new InvalidOperationException("Phase hand seizure requires one payable original HE card and living male target in its actual Play phase.");
        var program = _contentRegistry.GetSkill(frame.SkillId).Program!;
        if (ProgramInstructionResolver.Default.FindTrigger(program, continuation) is not { Window: SkillProgramTriggerWindow.PlayEnding, Optional: false,
                Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.Own,
                Effects: [{ Op: SkillProgramEffectOp.ReturnIssuedPhaseHandDebt }] })
            throw new InvalidOperationException("Phase hand seizure requires its exact mandatory return continuation.");
        var before = PhaseHandSeizureSequence;
        var receipt = new ProgramPhaseHandSeizureReceipt(1, target, continuation, _turnNumber, _cardUseDebitPhaseInstanceId,
            cardId, _cardZones.GetLocation(cardId), _players[frame.OwnerSeat].IsFaceDown, PhaseHandSeizureStage.PaymentChildren, before, before, []);
        ReplaceRuntimeTop(frame = frame with { PhaseHandSeizure = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([cardId], CardLocation.DiscardPile, new(PhaseHandSeizureReason(frame, "payment")), (_, records) =>
        {
            var current = GetActiveProgramFrame(frame.Id); var after = records.Max(m => m.Sequence);
            ReplaceRuntimeTop(current with { PhaseHandSeizure = receipt with { CostAfter = after } });
            AdvanceEventRulesAndQueueFact(new PhaseHandSeizurePaidEvent(frame.Id, PhaseHandSeizureSource(frame), frame.GameplayHash,
                target, _turnNumber, _cardUseDebitPhaseInstanceId, cardId, receipt.CostFrom, before, after));
        });
        DrainPhaseHandSeizureMovement(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool TryDrainPhaseHandSeizureMovement(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) || TryBeginCardsMovedProgramWindow(frame.Id);
    private void DrainPhaseHandSeizureMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (PhaseHandSeizureTerminalState) { AssertPhaseHandSeizure(frame); return; }
        if (!TryDrainPhaseHandSeizureMovement(frame)) ReturnPhaseHandSeizureMovement(frame);
    }
    private bool ReturnPhaseHandSeizureMovement(ProgramSkillFrame frame)
    {
        if (frame.PhaseHandSeizure is null && frame.PhaseHandDebtReturn is null) return false;
        AssertPhaseHandSeizure(frame);
        if (PhaseHandSeizureTerminalState) return true;
        if (frame.PendingMovementContinuation is not { } pending || !IsPhaseHandSeizureMovement(frame, GetPausedPrivateOfferEffect(frame), pending))
            throw new InvalidOperationException("The phase-hand movement returned to a different owning instruction.");
        if (TryDrainPhaseHandSeizureMovement(frame)) return true;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null }); ResumePhaseHandSeizure(frame.Id); return true;
    }
    private bool ResumePhaseHandSeizure(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId ||
            frame.PhaseHandSeizure is null && frame.PhaseHandDebtReturn is null) return false;
        AssertPhaseHandSeizure(frame);
        if (PhaseHandSeizureTerminalState) return true;
        if (frame.PendingMovementContinuation is not null) { DrainPhaseHandSeizureMovement(frame.Id); return true; }
        if (frame.PhaseHandDebtReturn is { } returned)
        {
            if (returned.Stage == PhaseHandDebtReturnStage.Choosing) return true;
            ReplaceRuntimeTop(frame = frame with { PhaseHandDebtReturn = null }); FinishProgramSkill(frame, true); return true;
        }
        var receipt = frame.PhaseHandSeizure!;
        if (receipt.Stage == PhaseHandSeizureStage.TakeChildren)
        { ReplaceRuntimeTop(frame = frame with { PhaseHandSeizure = null }); FinishProgramSkill(frame, true); return true; }
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[receipt.TargetSeat].IsAlive)
        { ReplaceRuntimeTop(frame = frame with { PhaseHandSeizure = null }); FinishProgramSkill(frame, false); return true; }
        if (receipt.Stage == PhaseHandSeizureStage.PaymentChildren)
        {
            receipt = receipt with { Stage = PhaseHandSeizureStage.FlipChildren, WasFaceDown = _players[frame.OwnerSeat].IsFaceDown };
            ReplaceRuntimeTop(frame = frame with { PhaseHandSeizure = receipt });
            TurnOverProgramTarget(frame.Id, frame.OwnerSeat, frame.OwnerSeat);
            AdvanceEventRulesAndQueueFact(new PhaseHandSeizureTurnedEvent(frame.Id, frame.OwnerSeat, receipt.WasFaceDown, !receipt.WasFaceDown));
        }
        if (TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program)) return true;
        frame = GetActiveProgramFrame(frame.Id); receipt = frame.PhaseHandSeizure!;
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[receipt.TargetSeat].IsAlive)
        { ReplaceRuntimeTop(frame = frame with { PhaseHandSeizure = null }); FinishProgramSkill(frame, false); return true; }
        var cards = GetHand(_players[receipt.TargetSeat]).Select(c => c.Id).ToArray(); var before = PhaseHandSeizureSequence;
        var same = receipt.TargetSeat == frame.OwnerSeat;
        receipt = receipt with { Stage = PhaseHandSeizureStage.TakeChildren, TakenCardIds = cards, TakeBefore = before, TakeAfter = before };
        ReplaceRuntimeTop(frame = frame with { PhaseHandSeizure = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        void Issued(long after, int count)
        {
            var current = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(current with { PhaseHandSeizure = receipt with { TakeAfter = after } });
            AdvanceEventRulesAndQueueFact(new PhaseHandSeizureIssuedEvent(frame.Id, PhaseHandSeizureSource(frame), frame.GameplayHash,
                receipt.ContinuationId, receipt.TargetSeat, receipt.ActualTurnNumber, receipt.PhaseInstanceId, count, same, before, after));
        }
        if (same || cards.Length == 0) Issued(before, 0);
        else MoveProgramCardsFromMultipleSources(cards, CardLocation.Hand(frame.OwnerSeat), new(PhaseHandSeizureReason(frame, "take")),
            (_, records) => Issued(records.Max(m => m.Sequence), records.Count));
        DrainPhaseHandSeizureMovement(frame.Id); return true;
    }

    private Card[] PhaseHandDebtCards(ProgramSkillFrame frame) => GetHand(_players[frame.OwnerSeat]).Concat(GetEquipment(frame.OwnerSeat))
        .Where(card => !(card.IsGeneralWeapon && _cardZones.GetLocation(card.Id).Zone == CardZoneKind.Equipment) &&
            !IsForeignEquipmentDiscardPrevented(frame.OwnerSeat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Transfer)).ToArray();
    private SkillProgramStepOutcome BeginPhaseHandDebtReturn(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id); var due = RequirePhaseHandDebtParent(frame);
        if (frame.InstructionIndex != 1 || frame.PhaseHandDebtReturn is not null || frame.PhaseHandSeizure is not null)
            throw new InvalidOperationException("The phase-hand debt cannot freeze or pay twice.");
        if (!_players[due.TargetSeat].IsAlive)
        { SettlePhaseHandDebt(due, frame.Id, "target-death", 0, 0, 0, 0); FinishProgramSkill(frame, true); return SkillProgramStepOutcome.AwaitChild; }
        var hp = Math.Max(0, _players[due.TargetSeat].Hp); var count = Math.Min(hp, PhaseHandDebtCards(frame).Length);
        ReplaceRuntimeTop(frame = frame with { PhaseHandDebtReturn = new(1, due.FrameId, due.TargetSeat, hp, count,
            PhaseHandDebtReturnStage.Choosing, [], []) });
        AdvanceEventRulesAndQueueFact(new PhaseHandDebtReturnStartedEvent(frame.Id, due.FrameId, frame.OwnerSeat, due.TargetSeat,
            _turnNumber, _cardUseDebitPhaseInstanceId, hp, count));
        if (count == 0)
        {
            SettlePhaseHandDebt(due, frame.Id, "returned", 0, 0, PhaseHandSeizureSequence, PhaseHandSeizureSequence);
            ReplaceRuntimeTop(frame = frame with { PhaseHandDebtReturn = null }); FinishProgramSkill(frame, true);
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishPhaseHandDebtChoice(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> PhaseHandDebtChoices(ProgramSkillFrame frame) => Array.AsReadOnly(PhaseHandDebtCards(frame)
        .Where(c => !frame.PhaseHandDebtReturn!.CardIds.Contains(c.Id)).Select(c => new PromptChoice(new($"phase-hand-debt.{frame.Id}.{c.Id}"),
            $"交给目标【{c.DisplayName}】（{frame.PhaseHandDebtReturn!.CardIds.Count + 1}/{frame.PhaseHandDebtReturn.RequiredCount}）", [c.Id], [],
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            { ["program-action"] = "phase-hand-debt-return", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) }))).ToArray());
    private void PublishPhaseHandDebtChoice(ProgramSkillFrame frame)
    {
        var choices = PhaseHandDebtChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "交给原目标与其体力值等量张牌；不足时交给全部可交的牌。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).ToArray()), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.PhaseHandDebtReturn!.TargetSeat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolvePhaseHandDebtChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing phase-hand return owner.");
        AssertPhaseHandSeizure(frame); var receipt = frame.PhaseHandDebtReturn ?? throw new InvalidOperationException("Missing phase-hand return receipt.");
        var due = RequirePhaseHandDebtParent(frame);
        if (receipt.Stage != PhaseHandDebtReturnStage.Choosing || choice.Cards is not [var id] || choice.Targets.Count != 0 ||
            _pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([choice], PhaseHandDebtChoices(frame).Where(c => c.Id == choice.Id).ToArray()) ||
            !_players[frame.OwnerSeat].IsAlive || !_players[receipt.TargetSeat].IsAlive)
            throw new InvalidOperationException("The exact phase-hand return chooser or material changed.");
        var ids = receipt.CardIds.Append(id).ToArray(); var locations = receipt.Locations.Append(_cardZones.GetLocation(id)).ToArray(); ClearPendingDecision();
        if (ids.Length < receipt.RequiredCount)
        { ReplaceRuntimeTop(frame = frame with { PhaseHandDebtReturn = receipt with { CardIds = ids, Locations = locations } }); PublishPhaseHandDebtChoice(frame); return; }
        if (ids.Length != receipt.RequiredCount || ids.Where((card, index) => _cardZones.GetLocation(card) != locations[index] ||
                !PhaseHandDebtCards(frame).Any(c => c.Id == card)).Any()) throw new InvalidOperationException("The complete phase-hand return is no longer owned.");
        var before = PhaseHandSeizureSequence;
        var moving = ids.Where((card, index) => locations[index] != CardLocation.Hand(receipt.TargetSeat)).ToArray();
        receipt = receipt with { Stage = PhaseHandDebtReturnStage.MovementChildren, CardIds = ids, Locations = locations,
            SequenceBefore = before, SequenceAfter = before };
        ReplaceRuntimeTop(frame = frame with { PhaseHandDebtReturn = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        if (moving.Length == 0) SettlePhaseHandDebt(due, frame.Id, "same-hand", receipt.RequiredCount, 0, before, before);
        else MoveProgramCardsFromMultipleSources(moving, CardLocation.Hand(receipt.TargetSeat), new(PhaseHandDebtReturnReason(frame)), (_, records) =>
        {
            var current = GetActiveProgramFrame(frame.Id); var after = records.Max(m => m.Sequence);
            ReplaceRuntimeTop(current with { PhaseHandDebtReturn = receipt with { SequenceAfter = after } });
            SettlePhaseHandDebt(due, frame.Id, "returned", receipt.RequiredCount, records.Count, before, after);
        });
        DrainPhaseHandSeizureMovement(frame.Id);
    }
    private void SettlePhaseHandDebt(PhaseHandSeizureIssuedEvent due, long returnFrame, string reason, int required, int actual, long before, long after)
    {
        if (CompleteProgramEventHistory().OfType<PhaseHandDebtSettledEvent>().Any(e => e.SeizureFrameId == due.FrameId))
            throw new InvalidOperationException("One issued phase-hand debt cannot settle twice.");
        AdvanceEventRulesAndQueueFact(new PhaseHandDebtSettledEvent(due.FrameId, returnFrame, due.Source.OwnerSeat, due.TargetSeat, reason, required, actual, before, after));
    }
    private void ObservePhaseHandSeizureDeath(IGameEvent payload)
    {
        if (payload is GameEndedEvent ended && _status == EngineStatus.Completed && _phase == TurnPhase.Finished && ended.Winner == _winner && _winner != Winner.None)
        {
            foreach (var due in UnsettledPhaseHandSeizures().ToArray()) SettlePhaseHandDebt(due, 0, "game-end", 0, 0, 0, 0);
            return;
        }
        if (payload is not PlayerDiedEvent died) return;
        foreach (var due in UnsettledPhaseHandSeizures().Where(e => e.Source.OwnerSeat == died.VictimSeat || e.TargetSeat == died.VictimSeat).ToArray())
            SettlePhaseHandDebt(due, 0, due.Source.OwnerSeat == died.VictimSeat ? "owner-death" : "target-death", 0, 0, 0, 0);
    }
    private bool HasCurrentIssuedPhaseHandDebt() => _phase == TurnPhase.Play && UnsettledPhaseHandSeizures().Any(e =>
        e.Source.OwnerSeat == _currentSeat && e.ActualTurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId);
    private bool TryBeginForcedPhaseHandDebtReturn()
    {
        if (!HasCurrentIssuedPhaseHandDebt() || !_players[_currentSeat].IsAlive || _winner != Winner.None) return false;
        if (_pendingDecision is not null || _resolutionStack.Count != 0) throw new InvalidOperationException("A forced phase-hand end requires a quiet owning boundary.");
        var owner = _players[_currentSeat]; var facts = CaptureProgramTriggerFacts(owner);
        var candidates = Array.AsReadOnly(IssuedPhaseHandDebtCandidates(owner, SkillProgramTriggerWindow.PlayEnding, 0).ToArray());
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, owner.Seat, SkillProgramTriggerWindow.PlayEnding,
            candidates, ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd, facts)
        { ParticipantFacts = new ReadOnlyDictionary<int, SkillProgramTriggerFacts>(new Dictionary<int, SkillProgramTriggerFacts> { [owner.Seat] = facts }) });
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>(); return true;
    }
    private PromptChoice SelectAiPhaseHandDebt(PendingDecision decision, ProgramSkillFrame frame) => decision.Choices
        .OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[frame.OwnerSeat])).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    private sealed partial class ProgramSkillHost : IPhaseHandSeizureProgramHost
    {
        public SkillProgramStepOutcome DiscardTurnOverAndTakeHand(ProgramSkillFrame frame, string continuation) => engine.BeginPhaseHandSeizure(frame, continuation);
        public SkillProgramStepOutcome ReturnIssuedPhaseHandDebt(ProgramSkillFrame frame) => engine.BeginPhaseHandDebtReturn(frame);
        public bool CanContinueIssuedPhaseHandDebt(ProgramSkillFrame frame) => engine.CanContinuePhaseHandSeizure(frame);
    }
}
