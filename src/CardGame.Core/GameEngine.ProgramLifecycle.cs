namespace CardGame.Core;

/// <summary>Generic lifecycle-program host. It contains no character or skill ids.</summary>
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome StartProgramPindian(
        ProgramSkillFrame frame,
        ProgramParticipantReference opponentReference,
        string resultBind,
        SkillProgramCardSetVisibility visibility)
    {
        var eventOpponent = opponentReference.Kind == ProgramParticipantRef.EventTarget &&
            frame.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } };
        if (!eventOpponent && (frame.TriggerId is not null ||
                opponentReference.Kind != ProgramParticipantRef.SelectedTarget ||
                frame.SelectedTargetSeats.Count != 1 ||
                _cardZones.CardsAt(CardLocation.Processing).Count != 0) ||
            frame.PindianResultBindings.Any(item => item.Name == resultBind) || HasPendingProgramBoundCards)
            throw new InvalidOperationException(
                "Program Pindian requires an active selected opponent or a damage-event opponent and no pending program cards.");
        var opponentSeat = eventOpponent ? frame.WindowContext!.TargetSeat!.Value :
            frame.SelectedTargetSeats.Single();
        var source = _players[frame.OwnerSeat];
        var opponent = _players[opponentSeat];
        if (!source.IsAlive || !opponent.IsAlive || opponentSeat == frame.OwnerSeat ||
            GetHand(source).Count == 0 || GetHand(opponent).Count == 0)
            throw new InvalidOperationException("Program Pindian participants no longer have legal hand cards.");
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        BeginSharedPindian(
            frame.Id,
            new SkillPromptPresentation(frame.SkillId, skill.Name, $"{skill.Name} · 拼点", skill.Description),
            frame.OwnerSeat,
            opponentSeat,
            programResultBind: resultBind,
            programResultVisibility: visibility);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private ProgramPhaseSchedule? _programPhaseSchedule;

    private bool HasProgramLifecycleBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not ProgramLifecycleTriggerWindowFrame frame)
            return false;
        if (frame.OwnerSeat != _currentSeat || frame.CandidateIndex < 0 ||
            frame.CandidateIndex >= frame.Candidates.Count)
            throw new InvalidOperationException("The lifecycle boundary frame lost its owner or candidate cursor.");
        if (frame.Window == SkillProgramTriggerWindow.PlayEnding && _phase != TurnPhase.Play)
            throw new InvalidOperationException("A PlayEnding lifecycle frame must remain in its Play phase.");
        if (frame.Window is (SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.AfterNormalDraw) && _phase != TurnPhase.Draw)
            throw new InvalidOperationException("A DrawPhaseStarting lifecycle frame must remain in its Draw phase.");
        if (frame.Window == SkillProgramTriggerWindow.DiscardPhaseStarting && _phase != TurnPhase.Discard)
            throw new InvalidOperationException("A DiscardPhaseStarting lifecycle frame must remain in its Discard phase.");
        if (frame.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && _phase != TurnPhase.Discard)
            throw new InvalidOperationException("A DiscardPhaseEnded lifecycle frame must remain in its Discard phase.");
        if (frame.Window != SkillProgramTriggerWindow.DrawPhaseStarting && frame.NormalDrawAdjustment != 0)
            throw new InvalidOperationException("Only a DrawPhaseStarting lifecycle frame may adjust normal draws.");
        if (frame.Window is not (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.AfterNormalDraw or
            SkillProgramTriggerWindow.PlayEnding or
            SkillProgramTriggerWindow.DiscardPhaseStarting or
            SkillProgramTriggerWindow.DiscardPhaseEnded))
            throw new InvalidOperationException("This lifecycle window cannot own a clean phase boundary.");
        return true;
    }

    private bool HasTurnEndingBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not TurnEndingBoundaryFrame frame)
            return false;
        if (frame.OwnerSeat != _currentSeat || frame.TurnNumber != _turnNumber ||
            frame.Facts is null || frame.ItemIndex < 0 || frame.ItemIndex >= frame.Items.Count)
            throw new InvalidOperationException("The turn-ending boundary lost its owner, turn or item cursor.");
        if (_resolutionStack.Count == 1 && frame.Step == ResolutionFrameStep.AwaitingResponse)
        {
            var decisionKind = frame.Items[frame.ItemIndex].Kind switch
            {
                TurnEndingBoundaryItemKind.Program => DecisionKind.ProgramTrigger,
                _ => throw new InvalidOperationException("Unsupported turn-ending item kind.")
            };
            if (_pendingDecision?.Kind != decisionKind)
                throw new InvalidOperationException("The turn-ending boundary is waiting without its matching prompt.");
        }
        return true;
    }

    private bool HasPlayPhaseStartingBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not PlayPhaseStartingBoundaryFrame frame)
            return false;
        if (frame.OwnerSeat != _currentSeat || frame.ItemIndex < 0 ||
            frame.ItemIndex > frame.Items.Count)
            throw new InvalidOperationException("The play-phase-starting boundary lost its owner or item cursor.");
        if (_phase != TurnPhase.Play)
            throw new InvalidOperationException("A play-phase-starting boundary must remain in its Play phase.");
        if (_resolutionStack.Count == 1 && frame.Step == ResolutionFrameStep.AwaitingResponse)
        {
            var decisionKind = frame.Items[frame.ItemIndex].Kind switch
            {
                TurnEndingBoundaryItemKind.Program => DecisionKind.ProgramTrigger,
                _ => throw new InvalidOperationException("Unsupported play-phase-starting item kind.")
            };
            if (_pendingDecision?.Kind != decisionKind)
                throw new InvalidOperationException("The play-phase-starting boundary is waiting without its matching prompt.");
        }
        return true;
    }

    private void BeginNormalTurnStartAfterProgramBindings(CharacterState current)
    {
        BeginTurnStartAfterProgramLifecycle(current);
    }

    private void CompleteCurrentPlayPhase()
    {
        if (TryBeginPlayEndingProgramWindow(_players[_currentSeat])) return;
        CompletePlayPhaseAfterProgramWindow();
    }

    private void CompletePlayPhaseAfterProgramWindow()
    {
        if (ResumeScheduledProgramPhase()) return;
        BeginDiscardPhase();
    }

    private ProgramSkillFrame GetActiveProgramFrame(long frameId) =>
        _resolutionStack.LastOrDefault() is ProgramSkillFrame frame && frame.Id == frameId
            ? frame
            : throw new InvalidOperationException($"Program frame {frameId} is not active.");

    private SkillProgramTrigger GetProgramTrigger(ProgramSkillFrame frame)
    {
        var triggerId = frame.TriggerId ??
            throw new InvalidOperationException("The active program frame is not a trigger binding.");
        var program = _contentRegistry.GetSkill(frame.SkillId).Program ??
            throw new InvalidOperationException("The active program binding has no definition.");
        if (program.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("The active program binding definition changed.");
        return program.Triggers.Single(trigger => trigger.Id == triggerId);
    }

    private static string GetProgramBindingId(ProgramSkillFrame frame) =>
        frame.TriggerId ?? frame.ActivationId;

    private void SetProgramCardSet(
        long frameId,
        string bind,
        IReadOnlyList<int> cardIds,
        SkillProgramCardSetVisibility visibility,
        IReadOnlyList<CardLocation>? sourceLocations = null,
        Suit? frozenRevealedSuit = null)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.CardSetBindings.Any(binding => binding.Name == bind))
            throw new InvalidOperationException($"Program card-set binding '{bind}' already exists.");
        var ids = cardIds.ToArray();
        var locations = (sourceLocations ?? ids
            .Select(cardId => _cardZones.GetLocation(cardId)).ToArray()).ToArray();
        if (ids.Distinct().Count() != ids.Length || ids.Length != locations.Length)
            throw new InvalidOperationException("A program card-set binding requires unique cards and aligned source locations.");
        _resolutionStack[^1] = frame with
        {
            CardSetBindings = Array.AsReadOnly(frame.CardSetBindings
                .Append(new ProgramSkillCardSetBinding(
                    bind,
                    Array.AsReadOnly(ids),
                    visibility,
                    Array.AsReadOnly(locations))
                { FrozenRevealedSuit = frozenRevealedSuit })
                .ToArray())
        };
    }

    private ProgramSkillCardSetBinding GetProgramCardSet(ProgramSkillFrame frame, string bind) =>
        frame.CardSetBindings.SingleOrDefault(binding => binding.Name == bind) ??
        throw new InvalidOperationException($"Program card-set binding '{bind}' is unavailable.");

    private SkillProgramStepOutcome ScheduleProgramPhase(
        ProgramSkillFrame frame,
        TurnPhase phase,
        SkillProgramPhaseContinuation continuation)
    {
        if (phase != TurnPhase.Play || continuation != SkillProgramPhaseContinuation.BeforeNormalPreparation ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
            context.OwnerSeat != frame.OwnerSeat || _programPhaseSchedule is not null ||
            GetActiveProgramFrame(frame.Id) != frame)
            throw new InvalidOperationException("The configured phase insertion does not match a clean turn-start boundary.");

        if (_resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId)
            throw new InvalidOperationException("The inserted phase lost its lifecycle parent window.");
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        PopResolutionFrame(parent.Id, ResolutionFrameKind.ProgramLifecycleTriggerWindow);
        _programPhaseSchedule = new ProgramPhaseSchedule(frame, parent, phase, continuation);
        QueueGameEvent(new ProgramPhaseScheduledEvent(
            frame.Id, frame.SkillId, frame.TriggerId!, frame.OwnerSeat, phase, Started: true));
        EnterPlayPhase(_players[frame.OwnerSeat]);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeScheduledProgramPhase()
    {
        if (_programPhaseSchedule is not { } schedule) return false;
        var frame = schedule.Frame;
        _programPhaseSchedule = null;
        QueueGameEvent(new ProgramPhaseScheduledEvent(
            frame.Id, frame.SkillId, frame.TriggerId!, frame.OwnerSeat, schedule.Phase, Started: false));
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive)
        {
            CompleteDetachedProgramBinding(frame, completed: false);
            return true;
        }
        _resolutionStack.Add(schedule.ParentFrame);
        if (!HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CompleteDetachedProgramBinding(frame, completed: false);
            AdvanceProgramLifecycleCursor(schedule.ParentFrame);
            ContinueProgramLifecycleWindow();
            return true;
        }
        _resolutionStack.Add(frame);
        ContinueProgramSkill(frame.Id);
        return true;
    }

    private void CancelScheduledProgramPhase()
    {
        if (_programPhaseSchedule is not { } schedule) return;
        _programPhaseSchedule = null;
        QueueGameEvent(new ProgramPhaseScheduledEvent(
            schedule.Frame.Id,
            schedule.Frame.SkillId,
            schedule.Frame.TriggerId!,
            schedule.Frame.OwnerSeat,
            schedule.Phase,
            Started: false));
        CompleteDetachedProgramBinding(schedule.Frame, completed: false);
    }

    private void RecoverProgramTargetTo(
        long frameId,
        int ownerSeat,
        int targetSeat,
        SkillProgramNumberExpression expression,
        int minimumValue,
        bool clampToMaxHp)
    {
        _ = GetActiveProgramFrame(frameId);
        var target = _players[targetSeat];
        var value = expression switch
        {
            SkillProgramNumberExpression.IntegerConstant => minimumValue,
            SkillProgramNumberExpression.LivingFactionCount => GetLivingFactionCount(),
            SkillProgramNumberExpression.LivingPlayersMinHp => GetLivingPlayersMinHp(),
            _ => throw new InvalidOperationException($"Unsupported numeric expression '{expression}'.")
        };
        value = Math.Max(minimumValue, value);
        // Recovery never raises HP above MaxHp. The flag remains part of the
        // serialized primitive contract for forward-compatible numeric policies.
        value = Math.Min(target.MaxHp, value);
        var amount = Math.Max(0, value - target.Hp);
        if (amount == 0) return;
        var recovery = BeginRecovery(frameId, ownerSeat, targetSeat, amount);
        target.Hp += amount;
        QueueGameEvent(new RecoveryAppliedEvent(ownerSeat, targetSeat, amount, target.Hp));
        PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
    }

    private void DiscardProgramOwnedZoneCards(
        ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.ActivationId != frame.ActivationId)
        {
            throw new InvalidOperationException("Owned-zone discard requires the active program binding.");
        }

        var owner = _players[frame.OwnerSeat];
        var discarded = 0;
        foreach (var zone in zones)
        {
            var (cards, location) = zone switch
            {
                CardZoneKind.Hand => (GetHand(owner).ToArray(), CardLocation.Hand(owner.Seat)),
                CardZoneKind.Equipment => (GetEquipment(owner).ToArray(), CardLocation.Equipment(owner.Seat)),
                CardZoneKind.Judgment => (GetJudgment(owner).ToArray(), CardLocation.Judgment(owner.Seat)),
                _ => throw new InvalidOperationException($"Unsupported owned discard zone '{zone}'.")
            };
            foreach (var card in cards)
            {
                MoveCard(card, location, CardLocation.DiscardPile, reason);
                discarded++;
            }
        }

        QueueGameEvent(new ProgramOwnedZoneCardsDiscardedEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            frame.OwnerSeat,
            Array.AsReadOnly(zones.ToArray()),
            discarded));
    }

    private void SetProgramChainedState(ProgramSkillFrame frame, bool chained, int targetSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.ActivationId != frame.ActivationId)
        {
            throw new InvalidOperationException("Chained-state mutation requires the active program binding.");
        }

        _players[targetSeat].IsChained = chained;
        QueueGameEvent(new ProgramChainedStateSetEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            frame.OwnerSeat,
            chained,
            targetSeat));
    }

    private void ChangeProgramMaximumHp(ProgramSkillFrame frame, int amount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.TriggerId != frame.TriggerId)
        {
            throw new InvalidOperationException("Maximum-HP mutation requires the active program binding.");
        }

        var owner = _players[frame.OwnerSeat];
        var previous = owner.MaxHp;
        owner.MaxHp = Math.Max(1, checked(owner.MaxHp + amount));
        owner.Hp = Math.Min(owner.Hp, owner.MaxHp);
        QueueGameEvent(new MaximumHpChangedEvent(
            owner.Seat,
            owner.MaxHp - previous,
            owner.MaxHp,
            frame.SkillId));
    }

    private void GrantProgramSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.TriggerId != frame.TriggerId)
        {
            throw new InvalidOperationException("Runtime-skill grants require the active program binding.");
        }

        var owner = _players[frame.OwnerSeat];
        var acquired = AcquireRuntimeSkills(owner, frame.SkillId, skillIds);
        if ((_contentRegistry!.GetSkill(frame.SkillId).Tags & SkillTag.Awakening) != 0)
        {
            QueueGameEvent(new SkillAwakenedEvent(
                owner.Seat,
                frame.SkillId,
                owner.MaxHp,
                acquired));
        }
    }

    private void TurnOverProgramTarget(long frameId, int ownerSeat, int targetSeat)
    {
        _ = GetActiveProgramFrame(frameId);
        var target = _players[targetSeat];
        target.IsFaceDown = !target.IsFaceDown;
        AddLog("SkillEffect", $"{target.Name} 的武将牌翻面。", ownerSeat, targetSeat);
    }

    private void SetProgramTargetFaceState(long frameId, int ownerSeat, int targetSeat, bool faceDown)
    {
        _ = GetActiveProgramFrame(frameId);
        var target = _players[targetSeat];
        target.IsFaceDown = faceDown;
        AddLog("SkillEffect",
            faceDown ? $"{target.Name} 的武将牌保持或变为背面。" : $"{target.Name} 的武将牌保持或变为正面。",
            ownerSeat,
            targetSeat);
    }

    private SkillProgramStepOutcome StartProgramJudgment(
        ProgramSkillFrame frame,
        int targetSeat,
        string reason,
        string resultBind,
        SkillProgramCardSetVisibility visibility,
        int sourceSeat)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            !MatchesProgramJudgmentInstruction(frame, targetSeat, reason, resultBind, visibility))
        {
            throw new InvalidOperationException(
                "A program judgment requires an active program target and public result.");
        }

        _ = BeginJudgment(
            attack: _pendingAttack,
            targetSeat,
            reason,
            frame.Id,
            sourceCard: frame.WindowContext?.CardUse?.EffectiveKind,
            JudgmentContinuationKind.ProgramSkill,
            sourceSeat: sourceSeat,
            programResultBind: resultBind,
            programResultVisibility: visibility);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool IsValidProgramJudgmentContinuation(JudgmentResolution pending,
        ProgramSkillFrame? frame)
    {
        if (pending.Continuation != JudgmentContinuationKind.ProgramSkill || frame is null ||
            pending.ParentFrameId != frame.Id ||
            pending.ProgramResultBind is not { } bind ||
            pending.ProgramResultVisibility is not { } visibility)
            return false;
        if (!MatchesProgramJudgmentInstruction(frame, pending.TargetSeat, pending.Reason,
                bind, visibility))
            return false;
        var judged = ProgramInstructionResolver.Default.Resolve(frame,
                _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .Instructions;
        if (frame.InstructionIndex < 1 || frame.InstructionIndex > judged.Count ||
            judged[frame.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.StartJudgment } effect)
            return true;
        var expectedSourceSeat = effect.SourceRef is { } sourceRef
            ? ResolveProgramParticipant(frame, sourceRef)
            : frame.OwnerSeat;
        return pending.SourceSeat == expectedSourceSeat;
    }

    private bool MatchesProgramJudgmentInstruction(ProgramSkillFrame frame, int targetSeat,
        string reason, string resultBind, SkillProgramCardSetVisibility visibility)
    {
        if (visibility != SkillProgramCardSetVisibility.Public || string.IsNullOrWhiteSpace(reason) ||
            string.IsNullOrWhiteSpace(resultBind) ||
            (ProgramEntryCapabilities.For(frame.WindowContext?.Window) & ProgramContextCapability.Judgment) == 0 ||
            !_contentRegistry.Skills.TryGetValue(frame.SkillId, out var definition) ||
            definition.Program is not { } program || program.GameplayHash != frame.GameplayHash)
            return false;
        var instructions = ProgramInstructionResolver.Default.Resolve(frame, program).Instructions;
        if (frame.InstructionIndex < 1 || frame.InstructionIndex > instructions.Count ||
            instructions[frame.InstructionIndex - 1] is not
            { Op: SkillProgramEffectOp.StartJudgment or SkillProgramEffectOp.RepeatJudgment } effect ||
            reason != effect.JudgmentReason || resultBind != effect.ResultBind)
            return false;
        if (effect.Op == SkillProgramEffectOp.RepeatJudgment &&
            (frame.RepeatedJudgment is not { } repeated ||
             repeated.Reason != reason || repeated.ResultBind != resultBind))
            return false;
        return effect.Target switch
        {
            SkillProgramEffectTarget.Owner => targetSeat == frame.OwnerSeat,
            SkillProgramEffectTarget.SelectedTarget =>
                frame.SelectedTargetSeats is [var selectedSeat] && targetSeat == selectedSeat,
            _ => false
        };
    }

    private void RevealProgramTopCards(
        long frameId,
        int ownerSeat,
        int amount,
        SkillProgramNumberExpression? numberExpression,
        string resultBind,
        SkillProgramCardSetVisibility visibility)
    {
        var frame = GetActiveProgramFrame(frameId);
        var revealCount = numberExpression switch
        {
            null => amount,
            SkillProgramNumberExpression.OwnerLostHp =>
                Math.Max(0, _players[ownerSeat].MaxHp - _players[ownerSeat].Hp),
            _ => throw new InvalidOperationException(
                $"Unsupported reveal number expression '{numberExpression}'.")
        };
        var revealed = new List<Card>(revealCount);
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{GetProgramBindingId(frame)}.reveal");
        for (var index = 0; index < revealCount; index++)
        {
            if (DrawOneToProcessing(reason) is not { } card) break;
            revealed.Add(card);
        }
        SetProgramCardSet(frameId, resultBind, revealed.Select(card => card.Id).ToArray(), visibility);
        if (visibility == SkillProgramCardSetVisibility.Public)
            QueueGameEvent(new ProgramCardsRevealedEvent(
                frameId, frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, resultBind,
                Array.AsReadOnly(revealed.Select(ToSnapshot).ToArray())));
    }

    private void FilterProgramBoundCards(
        long frameId,
        string sourceBind,
        string resultBind,
        IReadOnlyList<Suit> suits,
        ProgramParticipantReference? effectiveSuitFor,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<EquipmentSlot> equipmentSlots,
        IReadOnlyList<CardKind> cardKinds,
        string? matchSuitOfBind)
    {
        var frame = GetActiveProgramFrame(frameId);
        var source = GetProgramCardSet(frame, sourceBind);
        var matchedSuit = matchSuitOfBind is null ? (Suit?)null :
            GetProgramCardSet(frame, matchSuitOfBind).FrozenRevealedSuit ??
            throw new InvalidOperationException("The referenced public suit is unavailable.");
        var effectiveSuitSeat = effectiveSuitFor is null ? (int?)null : ResolveProgramParticipant(frame, effectiveSuitFor);
        var selected = source.CardIds.Select((cardId, index) =>
            {
                var location = source.SourceLocations[index];
                if (_cardZones.GetLocation(cardId) != location)
                    throw new InvalidOperationException(
                        "A bound card left its frozen source before its configured filter.");
                var card = _cardZones.CardsAt(location).Single(current => current.Id == cardId);
                return (Card: card, Location: location);
            })
            .Where(item =>
            {
                var suit = effectiveSuitSeat is { } seat ? EffectiveSuit(_players[seat], item.Card) : item.Card.Suit;
                return matchedSuit is { } required
                    ? suit == required
                    : ProgramCardSetFilter.Matches(item.Card.Kind, suit, suits, categories, equipmentSlots, cardKinds);
            })
            .ToArray();
        SetProgramCardSet(
            frameId,
            resultBind,
            selected.Select(item => item.Card.Id).ToArray(),
            source.Visibility,
            selected.Select(item => item.Location).ToArray());
    }

    private void DrawProgramCards(
        long frameId,
        int targetSeat,
        int amount,
        SkillProgramNumberExpression? numberExpression,
        string? resultBind,
        SkillProgramCardSetVisibility visibility,
        CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        var target = _players[targetSeat];
        var drawCount = numberExpression switch
        {
            null => amount,
            SkillProgramNumberExpression.OwnerLostHp => GetProgramOwnerLostHp(frame),
            SkillProgramNumberExpression.LivingFactionCount => GetLivingFactionCount(),
            SkillProgramNumberExpression.LivingPlayersMinHp => GetLivingPlayersMinHp(),
            SkillProgramNumberExpression.TargetMaxHpMinusHandCount =>
                Math.Max(0, target.MaxHp - GetHand(target).Count),
            SkillProgramNumberExpression.CurrentAttackRange =>
                frame.WindowContext?.Facts is { } facts && facts.CurrentAttackRange > 0
                    ? facts.CurrentAttackRange
                    : GetAttackRange(target.Seat),
            SkillProgramNumberExpression.HandLimitMinusHandCount =>
                Math.Max(0, GetHandLimit(target) - GetHand(target).Count),
            _ => throw new InvalidOperationException(
                $"Unsupported draw number expression '{numberExpression}'.")
        };
        var drawn = DrawCards(target, drawCount, log: true, reason);
        if (frame.WindowContext?.JudgmentReplacement is { } replacement)
        {
            var active = GetActiveProgramFrame(frameId);
            _resolutionStack[^1] = active with
            {
                WindowContext = active.WindowContext! with
                {
                    JudgmentReplacement = replacement with { DrawnCards = replacement.DrawnCards + drawn.Count }
                }
            };
        }
        if (resultBind is not null)
        {
            SetProgramCardSet(
                frameId,
                resultBind,
                drawn,
                visibility,
                drawn.Select(_ => CardLocation.Hand(targetSeat)).ToArray());
        }
    }

    private void DrawProgramSelectedTargets(long frameId, int amount, CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (amount < 1 || frame.SelectedTargetSeats.Count == 0 ||
            frame.SelectedTargetSeats.Distinct().Count() != frame.SelectedTargetSeats.Count)
            throw new InvalidOperationException("The selected-target draw has invalid participants.");
        foreach (var seat in frame.SelectedTargetSeats)
        {
            if (_players[seat].IsAlive)
                DrawProgramCards(frameId, seat, amount, null, null,
                    SkillProgramCardSetVisibility.Private, reason);
        }
    }

    private IReadOnlyList<int> GetProgramTargetSeats(
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        ProgramSkillWindowContext? windowContext = null,
        PlayerMarkerKind? marker = null,
        ProgramParticipantReference? actorReference = null)
    {
        windowContext ??= _resolutionStack.LastOrDefault() switch
        {
            ProgramSkillFrame program => program.WindowContext,
            ProgramCardTriggerWindowFrame cardWindow when cardWindow.CandidateIndex < cardWindow.Candidates.Count =>
                CreateCardActionProgramContext(cardWindow, cardWindow.Candidates[cardWindow.CandidateIndex]),
            TurnEndingBoundaryFrame ending when ending.ItemIndex < ending.Items.Count &&
                ending.Items[ending.ItemIndex].Candidate is { } candidate =>
                CreateTurnEndingProgramContext(ending, candidate, ending.Items[ending.ItemIndex].Facts),
            _ => null
        };
        if (targetKind == SkillProgramTargetKind.EventTarget)
        {
            if (windowContext is { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: { } endingSeat } &&
                _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(item =>
                    item.Id == windowContext.ParentFrameId && item.OwnerSeat == endingSeat) is not null)
                return _players[endingSeat].IsAlive ? [endingSeat] : [];
            if (windowContext is
                {
                    Window: SkillProgramTriggerWindow.AfterDamageApplied or
                        SkillProgramTriggerWindow.DamageAppliedBeforeDying,
                    TargetSeat: { } damageSeat
                })
                return _players[damageSeat].IsAlive ? [damageSeat] : [];
            var actionId = windowContext?.CardUse?.CardActionId ??
                throw new InvalidOperationException("Event-target selection requires card-action context.");
            var parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                .SingleOrDefault(item => item.Id == windowContext.ParentFrameId && item.Action.ActionId == actionId) ??
                throw new InvalidOperationException("Event-target selection lost its parent card action.");
            var frozenTargets = parent.Action.TargetSeats.Count > 0
                ? parent.Action.TargetSeats
                : parent.Candidates.Select(candidate => candidate.OpponentSeat)
                    .Where(seat => seat >= 0).Distinct().ToArray();
            return frozenTargets.Distinct().Where(seat => _players[seat].IsAlive).Order().ToArray();
        }
        if (targetKind == SkillProgramTargetKind.CurrentCardUseTargets)
        {
            if (windowContext is not
                {
                    Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                    CardUse: { } cardUse
                } ||
                _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is not { } window ||
                window.Id != windowContext.ParentFrameId ||
                window.Action.ActionId != cardUse.CardActionId ||
                window.ParentFrameId != cardUse.ParentCardUseFrameId ||
                window.Action.Type != CardActionType.Use ||
                _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(item => item.Id == window.ParentFrameId) is not { } parent ||
                parent.Action?.ActionId != cardUse.CardActionId)
                throw new InvalidOperationException("Current card-use targets require their frozen active card action.");
            return (cardUse.DesignatedTargetSeats ?? window.Action.EffectiveDesignatedTargetSeats)
                .Distinct().Where(seat => _players[seat].IsAlive &&
                    parent.IneffectiveTargetSeats?.Contains(seat) != true).ToArray();
        }
        if (targetKind == SkillProgramTargetKind.OtherLivingInBoundParticipantAttackRange)
        {
            if (actorReference is not { } reference ||
                _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame)
                throw new InvalidOperationException("Attack-range target selection lost its bound participant.");
            var actorSeat = ResolveProgramParticipant(frame, reference);
            if (!_players[actorSeat].IsAlive) return [];
            return _players.Where(target => target.IsAlive && target.Seat != actorSeat &&
                    GetCombatDistance(actorSeat, target.Seat) <= GetAttackRange(actorSeat))
                .Select(target => target.Seat).Order().ToArray();
        }
        if (targetKind == SkillProgramTargetKind.MaximumAttributedMarker)
        {
            if (marker is not { } markerKind)
                throw new InvalidOperationException("Maximum attributed-marker selection requires a marker kind.");
            return GameRules.GetMaximumMarkerCandidates(_players.Select(player =>
                new PlayerMarkerCandidateState(
                    player.Seat,
                    player.IsAlive,
                    GetMarkerSourceCount(player, markerKind, ownerSeat))));
        }
        return _players
            .Where(target => target.IsAlive && targetKind switch
            {
                SkillProgramTargetKind.OtherLiving => target.Seat != ownerSeat,
                SkillProgramTargetKind.OtherLivingMale =>
                    target.Seat != ownerSeat && target.Gender == GeneralGender.Male,
                SkillProgramTargetKind.OtherWoundedMale =>
                    target.Seat != ownerSeat && target.Gender == GeneralGender.Male &&
                    target.Hp < target.MaxHp,
                SkillProgramTargetKind.OtherLivingInAttackRange =>
                    target.Seat != ownerSeat &&
                    GetCombatDistance(ownerSeat, target.Seat) <= GetAttackRange(ownerSeat),
                SkillProgramTargetKind.OtherLivingWhoseAttackRangeIncludesOwner =>
                    target.Seat != ownerSeat &&
                    GetCombatDistance(target.Seat, ownerSeat) <= GetAttackRange(target.Seat),
                SkillProgramTargetKind.OtherLivingWithQinggangSword =>
                    target.Seat != ownerSeat &&
                    GetEquipment(target).Any(card => card.Kind == CardKind.QinggangSword),
                SkillProgramTargetKind.OtherLivingSlashable =>
                    CanUseProvidedSlashTarget(_players[ownerSeat], target),
                SkillProgramTargetKind.SlashRedirectable =>
                    _pendingAttack is { } pendingSlash &&
                    pendingSlash.TargetSeat == ownerSeat &&
                    IsProgramSlashRedirectTarget(pendingSlash, ownerSeat, target.Seat),
                SkillProgramTargetKind.OtherLivingVirtualSlashTarget =>
                    target.Seat != ownerSeat &&
                    !IsDirectedCardTargetProhibited(ownerSeat, target.Seat, CardKind.Slash) &&
                    !IsSlashProhibited(target),
                SkillProgramTargetKind.OtherLivingWithHand =>
                    target.Seat != ownerSeat && GetHand(target).Count > 0,
                SkillProgramTargetKind.OtherLivingWithHandHpGreaterThanOwner =>
                                    target.Seat != ownerSeat && GetHand(target).Count > 0 &&
                                    target.Hp > _players[ownerSeat].Hp,
                SkillProgramTargetKind.OtherLivingHandAtLeastOwner =>
                                    target.Seat != ownerSeat && GetHand(target).Count > 0 &&
                                    GetHand(target).Count >= GetHand(_players[ownerSeat]).Count,
                SkillProgramTargetKind.OtherLivingUnequalHandPair => target.Seat != ownerSeat &&
                    _players.Any(peer => peer.IsAlive && peer.Seat != ownerSeat && peer.Seat != target.Seat &&
                        GetHand(peer).Count != GetHand(target).Count),
                SkillProgramTargetKind.OtherLivingPair => target.Seat != ownerSeat &&
                    _players.Count(peer => peer.IsAlive && peer.Seat != ownerSeat) >= 2,
                SkillProgramTargetKind.OtherLivingRangeOrderedPair => target.Seat != ownerSeat,
                SkillProgramTargetKind.LivingPairDistinct =>
                    _players.Count(peer => peer.IsAlive) >= 2,
                SkillProgramTargetKind.OtherLivingLeastHandCount => target.Seat != ownerSeat &&
                    GetHand(target).Count ==
                    _players.Where(peer => peer.IsAlive && peer.Seat != ownerSeat)
                        .Select(peer => GetHand(peer).Count).Min(),
                SkillProgramTargetKind.OtherLivingAtDistanceOne =>
                    target.Seat != ownerSeat && GetCombatDistance(ownerSeat, target.Seat) == 1,
                SkillProgramTargetKind.AnyLiving => true,
                SkillProgramTargetKind.OtherWounded =>
                    target.Seat != ownerSeat && target.Hp < target.MaxHp,
                SkillProgramTargetKind.AnyWounded => target.Hp < target.MaxHp,
                SkillProgramTargetKind.AnyLivingHandBelowMaxHp =>
                    GetHand(target).Count < target.MaxHp,
                SkillProgramTargetKind.OtherLivingExceptSource =>
                    target.Seat != ownerSeat && target.Seat != windowContext?.SourceSeat,
                SkillProgramTargetKind.EventSource =>
                    target.Seat == windowContext?.SourceSeat,
                _ => false
            })
            .Select(target => target.Seat)
            .Order()
            .ToArray();
    }

    private bool IsProgramTargetEligible(int ownerSeat, SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones, int targetSeat, PlayerMarkerKind? marker = null,
        ProgramSkillWindowContext? windowContext = null,
        ProgramParticipantReference? actorReference = null)
    {
        if (!GetProgramTargetSeats(ownerSeat, targetKind, windowContext, marker, actorReference).Contains(targetSeat)) return false;
        if (zones.Count == 0) return true;
        var target = _players[targetSeat];
        return zones.Any(zone => zone switch
        {
            CardZoneKind.Hand => GetHand(target).Count > 0,
            CardZoneKind.Equipment => GetEquipment(target).Count > 0,
            CardZoneKind.Judgment => GetJudgment(target).Count > 0,
            _ => false
        });
    }

    private SkillProgramStepOutcome SelectProgramTargets(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        int minimumTargets,
        int maximumTargets,
        SkillProgramNumberExpression? numberExpression,
        SkillProgramTargetAiOrder aiOrder)
    {
        var frame = GetActiveProgramFrame(frameId);
        var targetSeats = GetProgramTargetSeats(ownerSeat, targetKind, frame.WindowContext);
        var expressionMaximum = numberExpression switch
        {
            SkillProgramNumberExpression.CurrentHandCount => GetHand(_players[ownerSeat]).Count,
            SkillProgramNumberExpression.PlannedNormalDrawCount =>
                GetProgramPlannedNormalDrawCount(frame),
            _ => maximumTargets
        };
        var cappedMaximum = Math.Min(Math.Min(maximumTargets, expressionMaximum), targetSeats.Count);
        if (minimumTargets < 1 || cappedMaximum < minimumTargets)
        {
            CancelProgramBindingAndCleanup(frame, "没有足够的合法技能目标，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var selections = new List<IReadOnlyList<int>>();
        var current = new List<int>();
        AddSelections(0);

        void AddSelections(int next)
        {
            if (current.Count >= minimumTargets)
            {
                var seats = current.ToArray();
                if (targetKind is SkillProgramTargetKind.OtherLivingUnequalHandPair or
                    SkillProgramTargetKind.OtherLivingPair)
                {
                    if (targetKind == SkillProgramTargetKind.OtherLivingUnequalHandPair &&
                        GetHand(_players[seats[0]]).Count == GetHand(_players[seats[1]]).Count)
                        return;
                    Array.Sort(seats, (left, right) =>
                        GetHand(_players[left]).Count.CompareTo(GetHand(_players[right]).Count));
                }
                if (targetKind == SkillProgramTargetKind.LivingPairDistinct)
                {
                    // Ordered pair: the first pick is the card source, the second the
                    // destination, so both permutations are exposed as distinct options.
                    selections.Add(Array.AsReadOnly(seats));
                    selections.Add(Array.AsReadOnly(new[] { seats[1], seats[0] }));
                }
                else if (targetKind == SkillProgramTargetKind.OtherLivingRangeOrderedPair)
                {
                    // Ordered pair where the second participant stands inside the first
                    // one's attack range; only directions that satisfy the range bound
                    // are exposed as distinct options.
                    foreach (var ordered in new[] { seats, new[] { seats[1], seats[0] } })
                        if (GetCombatDistance(ordered[0], ordered[1]) <= GetAttackRange(ordered[0]))
                            selections.Add(Array.AsReadOnly(ordered));
                }
                else
                {
                    selections.Add(Array.AsReadOnly(seats));
                    if (targetKind == SkillProgramTargetKind.OtherLivingMale)
                        selections.Add(Array.AsReadOnly(new[] { seats[1], seats[0] }));
                }
            }
            if (current.Count == cappedMaximum) return;
            for (var index = next; index < targetSeats.Count; index++)
            {
                current.Add(targetSeats[index]);
                AddSelections(index + 1);
                current.RemoveAt(current.Count - 1);
            }
        }

        if (selections.Count == 0)
        {
            CancelProgramBindingAndCleanup(frame, "没有符合目标关系的角色组合，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = selections.Select(selection =>
        {
            var seats = selection.ToArray();
            var names = string.Join("、", seats.Select(seat => _players[seat].Name));
            var id = string.Join("-", seats.Select(seat =>
                seat.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return new PromptChoice(
                new ChoiceId($"program-targets.frame-{frameId}.seats-{id}"),
                $"选择 {names}。",
                [],
                seats,
                new Dictionary<string, string>
                {
                    ["program-action"] = "select-targets",
                    ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-kind"] = targetKind.ToString(),
                    ["minimum-targets"] = minimumTargets.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["maximum-targets"] = (numberExpression == SkillProgramNumberExpression.PlannedNormalDrawCount ||
                        targetKind == SkillProgramTargetKind.CurrentCardUseTargets
                        ? cappedMaximum : maximumTargets).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-ai-order"] = aiOrder.ToString()
                });
        }).ToArray();
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        var displayedMaximum = numberExpression == SkillProgramNumberExpression.PlannedNormalDrawCount ||
            targetKind == SkillProgramTargetKind.CurrentCardUseTargets
            ? cappedMaximum : maximumTargets;
        var targetCountText = minimumTargets == displayedMaximum
            ? minimumTargets.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{minimumTargets} 至 {displayedMaximum}";
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            ownerSeat,
            $"【{presentation.Name}】请选择 {targetCountText} 名目标角色。",
            [],
            targetSeats,
            frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = ownerSeat,
            SkillPrompt = new SkillPromptPresentation(
                frame.SkillId,
                presentation.Name,
                $"{presentation.Name} · 选择目标",
                presentation.Description),
            Choices = Array.AsReadOnly(choices)
        };
        _status = _players[ownerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private int GetProgramPlannedNormalDrawCount(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not
            { Window: SkillProgramTriggerWindow.DrawPhaseStarting, ParentFrameId: var parentId } ||
            _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != parentId || parent.NormalDrawReplaced)
            throw new InvalidOperationException(
                "A planned normal-draw target limit requires its active draw-phase parent.");
        var baseDrawCount = parent.FrozenBaseDrawCount ?? GetTurnDrawCount(_players[frame.OwnerSeat]);
        if (parent.FrozenBaseDrawCount is null)
            _resolutionStack[^2] = parent with { FrozenBaseDrawCount = baseDrawCount };
        return Math.Max(0, checked(baseDrawCount + parent.NormalDrawAdjustment));
    }

    private void TakeProgramRandomHandCards(
        long frameId,
        int ownerSeat,
        int amountPerTarget,
        CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (amountPerTarget != 1 || frame.SelectedTargetSeats.Count == 0 ||
            frame.SelectedTargetSeats.Distinct().Count() != frame.SelectedTargetSeats.Count)
            throw new InvalidOperationException("The configured random hand-card transfer has invalid targets or amount.");

        var selection = _contentRegistry!.GetSkill(frame.SkillId).Program!.Triggers
            .Single(trigger => trigger.Id == frame.TriggerId).Effects
            .Single(effect => effect.Op == SkillProgramEffectOp.SelectTargets);
        var usesFrozenEligibility = selection.TargetKind == SkillProgramTargetKind.OtherLivingHandAtLeastOwner;
        var validTargets = usesFrozenEligibility ? [] :
            GetProgramTargetSeats(ownerSeat, SkillProgramTargetKind.OtherLivingWithHand);
        var takenSeats = new List<int>();
        foreach (var targetSeat in frame.SelectedTargetSeats)
        {
            if (!usesFrozenEligibility && !validTargets.Contains(targetSeat))
                throw new InvalidOperationException("A selected random hand-card target is no longer legal.");
            var target = _players[targetSeat];
            if (usesFrozenEligibility && (!target.IsAlive || GetHand(target).Count == 0))
                continue;
            var hand = GetHand(target);
            var card = hand[_random.Next(hand.Count)];
            MoveCard(card, CardLocation.Hand(targetSeat), CardLocation.Processing, reason);
            MoveCard(card, CardLocation.Processing, CardLocation.Hand(ownerSeat), reason);
            takenSeats.Add(targetSeat);
        }
        QueueGameEvent(new ProgramRandomHandCardsTakenEvent(
            frame.Id,
            frame.SkillId,
            frame.TriggerId!,
            ownerSeat,
            Array.AsReadOnly(takenSeats.ToArray()),
            takenSeats.Count));
        AddLog("SkillEffect", takenSeats.Count == 0
            ? $"{_players[ownerSeat].Name} 未从所选目标获得手牌。"
            : $"{_players[ownerSeat].Name} 从 {string.Join("、", takenSeats.Select(seat => _players[seat].Name))} 各获得一张手牌。",
            ownerSeat);
    }

    /// <summary>
    /// Takes one random card from every other living character in turn order, drawing the
    /// candidate pool from the declared areas. Hand cards stay opaque: only the participating
    /// seats and the total count reach the public event stream.
    /// </summary>
    private void TakeProgramRandomCardFromEveryOtherCharacter(
        long frameId,
        int ownerSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.OwnerSeat != ownerSeat || zones.Count == 0 ||
            zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)) ||
            !_players[ownerSeat].IsAlive)
            throw new InvalidOperationException(
                "A multi-character random transfer requires the active living owner and declared areas.");

        var takenSeats = new List<int>();
        foreach (var target in _players
                     .Where(player => player.IsAlive && player.Seat != ownerSeat)
                     .OrderBy(player => (player.Seat - ownerSeat + _playerCount) % _playerCount))
        {
            var candidates = zones
                .SelectMany(zone => zone switch
                {
                    CardZoneKind.Hand => GetHand(target).Select(card =>
                        (Card: card, Location: CardLocation.Hand(target.Seat))),
                    CardZoneKind.Equipment => GetEquipment(target).Select(card =>
                        (Card: card, Location: CardLocation.Equipment(target.Seat))),
                    CardZoneKind.Judgment => GetJudgment(target).Select(card =>
                        (Card: card, Location: CardLocation.Judgment(target.Seat))),
                    _ => throw new InvalidOperationException($"Unsupported random-transfer area '{zone}'.")
                })
                .OrderBy(entry => entry.Card.Id)
                .ToArray();
            if (candidates.Length == 0) continue;
            var pick = candidates[_random.Next(candidates.Length)];
            MoveCard(pick.Card, pick.Location, CardLocation.Processing, reason);
            MoveCard(pick.Card, CardLocation.Processing, CardLocation.Hand(ownerSeat), reason);
            takenSeats.Add(target.Seat);
        }

        QueueGameEvent(new ProgramRandomCardsTakenFromCharactersEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            ownerSeat,
            Array.AsReadOnly(takenSeats.ToArray()),
            Array.AsReadOnly(zones.ToArray()),
            takenSeats.Count));
        AddLog("SkillEffect", takenSeats.Count == 0
            ? $"{_players[ownerSeat].Name} 未从其他角色处获得牌。"
            : $"{_players[ownerSeat].Name} 从 {string.Join("、", takenSeats.Select(seat => _players[seat].Name))} 各随机获得一张牌。",
            ownerSeat);
    }

    private SkillProgramStepOutcome SelectProgramTarget(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones,
        PlayerMarkerKind? marker,
        ProgramParticipantReference? actorReference = null,
        bool skipIfNoTarget = false)
    {
        var frame = GetActiveProgramFrame(frameId);
        var targetSeats = GetProgramTargetSeats(ownerSeat, targetKind, frame.WindowContext, marker, actorReference)
            .Where(seat => IsProgramTargetEligible(ownerSeat, targetKind, zones, seat, marker,
                frame.WindowContext, actorReference)).ToArray();
        if (targetSeats.Length == 0)
        {
            if (skipIfNoTarget)
            {
                _resolutionStack[^1] = frame with { SelectedTargetSeats = [] };
                return SkillProgramStepOutcome.Continue;
            }
            CancelProgramBindingAndCleanup(frame, "没有仍然合法的技能目标，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = targetSeats.Select(targetSeat =>
            new PromptChoice(
                new ChoiceId($"program-target.frame-{frameId}.seat-{targetSeat}"),
                $"选择 {_players[targetSeat].Name}（座位 {targetSeat + 1}）。",
                [],
                [targetSeat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "select-target",
                    ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-kind"] = targetKind.ToString(),
                    ["marker"] = marker?.ToString() ?? ""
                })).ToArray();
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            ownerSeat,
            $"【{presentation.Name}】请选择一名目标角色。",
            [],
            targetSeats,
            frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = ownerSeat,
            SkillPrompt = new SkillPromptPresentation(
                frame.SkillId,
                presentation.Name,
                $"{presentation.Name} · 选择目标",
                presentation.Description),
            Choices = Array.AsReadOnly(choices)
        };
        _status = _players[ownerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private SkillProgramStepOutcome SelectProgramSourceCard(
        long frameId,
        int ownerSeat,
        SkillProgramCardSource cardSource,
        IReadOnlyList<CardZoneKind> zones,
        string resultBind,
        IReadOnlyList<EquipmentSlot> equipmentSlots,
        bool skipIfNoCards,
        bool allowSameSource)
    {
        var frame = GetActiveProgramFrame(frameId);
        var sourceSeat = cardSource switch
        {
            SkillProgramCardSource.Owner => ownerSeat,
            SkillProgramCardSource.DamageSource => frame.WindowContext?.SourceSeat,
            SkillProgramCardSource.EventTarget => frame.WindowContext?.TargetSeat,
            _ => null
        };
        if (sourceSeat is null || !IsValidPlayerSeat(sourceSeat.Value) ||
            (cardSource == SkillProgramCardSource.DamageSource && sourceSeat == ownerSeat && !allowSameSource))
        {
            if (skipIfNoCards)
            {
                SetProgramCardSet(frameId, resultBind, [], SkillProgramCardSetVisibility.Private, []);
                return SkillProgramStepOutcome.Continue;
            }
            CancelProgramBindingAndCleanup(frame, "伤害来源不再可供选择牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = new List<PromptChoice>();
        if (zones.Contains(CardZoneKind.Hand))
        {
            var hand = GetHand(_players[sourceSeat.Value]);
            for (var slot = 0; slot < hand.Count; slot++)
            {
                choices.Add(new PromptChoice(
                    new ChoiceId($"program-source-card.frame-{frameId}.hand-slot-{slot}"),
                    $"选择 {_players[sourceSeat.Value].Name} 的第 {slot + 1} 张暗置手牌。",
                    [],
                    [sourceSeat.Value],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "select-source-card",
                        ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["result-bind"] = resultBind,
                        ["source-zone"] = CardZoneKind.Hand.ToString(),
                        ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
            }
        }
        if (zones.Contains(CardZoneKind.Equipment))
        {
            foreach (var card in GetEquipment(_players[sourceSeat.Value]).Where(card =>
                         equipmentSlots.Count == 0 || equipmentSlots.Contains(EquipmentCatalog.Get(card.Kind).Slot)))
            {
                choices.Add(new PromptChoice(
                    new ChoiceId($"program-source-card.frame-{frameId}.equipment-{card.Id}"),
                    $"选择 {_players[sourceSeat.Value].Name} 的装备【{card.DisplayName}】。",
                    [card.Id],
                    [sourceSeat.Value],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "select-source-card",
                        ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["result-bind"] = resultBind,
                        ["source-zone"] = CardZoneKind.Equipment.ToString()
                    }));
            }
        }
        foreach (var zone in zones.Where(zone => zone is
            CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or
            CardZoneKind.Authority or CardZoneKind.Chunlao))
        {
            if (cardSource != SkillProgramCardSource.Owner)
                throw new InvalidOperationException("A persistent source pile must belong to the program owner.");
            foreach (var card in _cardZones.CardsAt(new CardLocation(zone, ownerSeat)))
            {
                choices.Add(new PromptChoice(
                    new ChoiceId($"program-source-card.frame-{frameId}.{zone}-{card.Id}"),
                    $"选择 {_players[ownerSeat].Name} 的【{card.DisplayName}】。",
                    [card.Id],
                    [ownerSeat],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "select-source-card",
                        ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["result-bind"] = resultBind,
                        ["source-zone"] = zone.ToString()
                    }));
            }
        }
        if (choices.Count == 0)
        {
            if (skipIfNoCards)
            {
                SetProgramCardSet(frameId, resultBind, [], SkillProgramCardSetVisibility.Private, []);
                return SkillProgramStepOutcome.Continue;
            }
            CancelProgramBindingAndCleanup(frame, "伤害来源已没有可选择的牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            ownerSeat,
            cardSource == SkillProgramCardSource.Owner
                ? $"【{presentation.Name}】请选择自己的一张牌。"
                : $"【{presentation.Name}】请选择伤害来源的一张牌。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(),
            [sourceSeat.Value],
            sourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = sourceSeat,
            SkillPrompt = new SkillPromptPresentation(
                frame.SkillId,
                presentation.Name,
                $"{presentation.Name} · 选择来源牌",
                presentation.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[ownerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private SkillProgramStepOutcome GiveProgramBoundCard(
        long frameId,
        int ownerSeat,
        string sourceBind,
        SkillProgramTargetKind targetKind,
        CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        var source = GetProgramCardSet(frame, sourceBind);
        if (source.CardIds.Count != source.SourceLocations.Count)
            throw new InvalidOperationException("A program card-set binding lost its source locations.");
        var available = source.CardIds.Select((cardId, index) =>
                (CardId: cardId, Location: source.SourceLocations[index]))
            .Where(item => _cardZones.GetLocation(item.CardId) == item.Location)
            .ToArray();
        var targetSeats = GetProgramTargetSeats(ownerSeat, targetKind);
        if (available.Length == 0 || targetSeats.Count == 0)
            return SkillProgramStepOutcome.Continue;

        var choices =
            (from item in available
             let card = _cardZones.CardsAt(item.Location).Single(card => card.Id == item.CardId)
             from targetSeat in targetSeats
             select new PromptChoice(
                 new ChoiceId($"program-give.frame-{frameId}.card-{card.Id}.target-{targetSeat}"),
                 $"将【{card.DisplayName}】交给 {_players[targetSeat].Name}（座位 {targetSeat + 1}）。",
                 [card.Id],
                 [targetSeat],
                 new Dictionary<string, string>
                 {
                     ["program-action"] = "give-bound-card",
                     ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     ["source-bind"] = sourceBind,
                     ["target-kind"] = targetKind.ToString(),
                     ["move-reason"] = reason.Value
                 }))
            .Append(new PromptChoice(
                new ChoiceId($"program-give.frame-{frameId}.keep"),
                "不分配，保留这些牌。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "keep-bound-cards",
                    ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source-bind"] = sourceBind,
                    ["target-kind"] = targetKind.ToString(),
                    ["move-reason"] = reason.Value
                }))
            .ToArray();
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            ownerSeat,
            $"【{presentation.Name}】可将一张本次获得的牌交给一名角色。",
            available.Select(item => item.CardId).ToArray(),
            targetSeats,
            frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = ownerSeat,
            SkillPrompt = new SkillPromptPresentation(
                frame.SkillId,
                presentation.Name,
                $"{presentation.Name} · 分配牌",
                presentation.Description),
            Choices = Array.AsReadOnly(choices)
        };
        _status = _players[ownerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ClaimProgramDamageCards(long frameId, int ownerSeat, CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        var cards = GetClaimableProgramDamageCards(frame);
        if (cards.Length == 0) return;
        var damage = _pendingDamageTrigger!;
        MoveCards(cards, CardLocation.Processing, CardLocation.Hand(ownerSeat), reason);
        if (_pendingGroupCard is { } group)
        {
            foreach (var card in cards)
                RecordClaimedGroupPhysicalCard(
                    group.DamageClaimedPhysicalCardIds,
                    group.ResolutionId,
                    group.PhysicalCards,
                    damage.Attack.ResolutionId,
                    card.Id);
        }
        QueueGameEvent(new ProgramDamageCardsClaimedEvent(
            frame.Id,
            frame.SkillId,
            frame.TriggerId!,
            ownerSeat,
            Array.AsReadOnly(cards.Select(card => card.Id).ToArray())));
    }

    private Card[] GetClaimableProgramDamageCards(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.AfterDamageApplied,
                ParentFrameId: var parentFrameId,
                DamageFrameId: var damageFrameId
            } || _pendingDamageTrigger is not { } damage ||
            damage.FrameId != parentFrameId || damage.DamageFrameId != damageFrameId)
            throw new InvalidOperationException("The program damage-card claim lost its damage window.");
        return damage.Attack.PhysicalCards
            .Where(card => _cardZones.GetLocation(card.Id) == CardLocation.Processing)
            .ToArray();
    }

    private SkillProgramStepOutcome SelectProgramCardSubset(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string resultBind,
        int minimumCards,
        int maximumCards,
        int maximumRankSum,
        SkillProgramSubsetAiOrder aiOrder,
        bool allowFewerWhenInsufficient,
        bool onePerSuit)
    {
        var frame = GetActiveProgramFrame(frameId);
        var source = frame.CardSetBindings.SingleOrDefault(binding => binding.Name == sourceBind);
        if (source is null)
        {
            CancelProgramBindingAndCleanup(
                frame,
                $"选牌来源绑定“{sourceBind}”未生成，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (source.CardIds.Count != source.SourceLocations.Count)
            throw new InvalidOperationException("A program card-set binding lost its source locations.");
        var cards = source.CardIds.Select((cardId, index) =>
            _cardZones.CardsAt(source.SourceLocations[index]).SingleOrDefault(card => card.Id == cardId) ??
            throw new InvalidOperationException("A bound card left its frozen source before selection."))
            .ToArray();
        // 涉猎-style one-per-suit subsets must take exactly one card of every
        // distinct suit; the constraint leaves no room for partial takes.
        var constraintMinimum = minimumCards;
        var constraintMaximum = maximumCards;
        var promptMinimum = minimumCards;
        var promptMaximum = maximumCards;
        if (onePerSuit)
        {
            var distinctSuits = cards.Select(card => card.Suit).Distinct().Count();
            constraintMinimum = constraintMaximum = distinctSuits;
            promptMinimum = promptMaximum = distinctSuits;
        }
        else
        {
            // A program may explicitly allow a depleted revealed set to satisfy an
            // otherwise exact count. All existing exact-count programs remain strict.
            constraintMinimum = allowFewerWhenInsufficient ? Math.Min(minimumCards, cards.Length) : minimumCards;
            constraintMaximum = allowFewerWhenInsufficient ? Math.Min(maximumCards, cards.Length) : maximumCards;
        }
        var options = CardSubsetSelector.Enumerate(
            cards.Select(card => new CardSubsetCandidate(card.Id, card.Rank, card.Suit)).ToArray(),
            new CardSubsetConstraint(constraintMinimum, constraintMaximum, maximumRankSum, onePerSuit));
        if (options.Count == 0)
        {
            CancelProgramBindingAndCleanup(frame, "选牌约束没有合法结果，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = options.Select(option =>
            new PromptChoice(
                new ChoiceId($"program-subset.frame-{frameId}.mask-{option.SelectionMask}"),
                option.CardIds.Count == 0
                    ? "不选择牌。"
                    : $"选择 {string.Join("、", option.CardIds.Select(cardId =>
                        cards.Single(card => card.Id == cardId)).Select(card =>
                        $"【{card.DisplayName}】{card.RankText}"))}（点数和 {option.RankSum}）。",
                option.CardIds,
                [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "select-subset",
                    ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source-bind"] = sourceBind,
                    ["result-bind"] = resultBind,
                    ["rank-sum"] = option.RankSum.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ai-order"] = aiOrder.ToString()
                }))
            .ToArray();

        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            ownerSeat,
            $"【{presentation.Name}】请选择符合约束的牌。",
            source.CardIds,
            [],
            frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = ownerSeat,
            SkillPrompt = new SkillPromptPresentation(
                frame.SkillId,
                presentation.Name,
                $"{presentation.Name} · 选择牌",
                onePerSuit
                    ? $"获得其中不同花色的牌各一张（共 {promptMaximum} 张），点数和不超过 {maximumRankSum}。"
                    : $"选择 {promptMinimum} 至 {promptMaximum} 张牌，点数和不超过 {maximumRankSum}。"),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[ownerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void CancelProgramBindingAndCleanup(ProgramSkillFrame frame, string reason)
    {
        AddLog("SkillCancelled", reason, frame.OwnerSeat);
        FinishProgramSkill(frame, completed: false);
    }

    private void CleanupProgramBoundCards(ProgramSkillFrame frame, bool completed)
    {
        var boundIds = frame.CardSetBindings.SelectMany(binding => binding.CardIds)
            .Distinct().ToHashSet();
        var cards = _cardZones.CardsAt(CardLocation.Processing)
            .Where(card => boundIds.Contains(card.Id)).ToArray();
        if (cards.Length > 0)
            MoveCards(
                cards,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                new CardMoveReason($"skill-program.{frame.SkillId}.{GetProgramBindingId(frame)}." +
                    (completed ? "complete-cleanup" : "cancel-cleanup")));
    }

    private SkillProgramStepOutcome MoveProgramBoundCards(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string? exceptBind,
        SkillProgramCardDestination destination,
        CardZoneKind? destinationZone,
        CardMoveReason reason,
        IReadOnlyList<int>? bottomOrder = null)
    {
        var frame = GetActiveProgramFrame(frameId);
        var source = frame.CardSetBindings.SingleOrDefault(binding => binding.Name == sourceBind);
        if (source is null)
        {
            CancelProgramBindingAndCleanup(
                frame,
                $"移动来源绑定“{sourceBind}”未生成，技能结算已取消。");
            return SkillProgramStepOutcome.Continue;
        }
        var ids = source.CardIds.ToHashSet();
        if (exceptBind is { } excluded)
        {
            var except = frame.CardSetBindings.SingleOrDefault(binding => binding.Name == excluded);
            if (except is null)
            {
                CancelProgramBindingAndCleanup(
                    frame,
                    $"移动排除绑定“{excluded}”未生成，技能结算已取消。");
                return SkillProgramStepOutcome.Continue;
            }
            ids.ExceptWith(except.CardIds);
        }
        var selected = source.CardIds.Select((cardId, index) =>
                (CardId: cardId, Location: source.SourceLocations[index]))
            .Where(item => ids.Contains(item.CardId))
            .ToArray();
        if (selected.Any(item => _cardZones.GetLocation(item.CardId) != item.Location))
            throw new InvalidOperationException("A bound card left its frozen source before its configured move.");
        if (selected.Length == 0) return SkillProgramStepOutcome.Continue;
        if (destination == SkillProgramCardDestination.DrawPileBottom && selected.Length > 1 && bottomOrder is null)
        {
            if (selected.Length > 4)
                throw new InvalidOperationException("At most four revealed cards may be privately ordered at the draw-pile bottom.");
            var orders = EnumerateProgramCardOrders(selected.Select(item => item.CardId).ToArray()).ToArray();
            var choices = orders.Select((order, index) => new PromptChoice(
                new ChoiceId($"program-bottom.frame-{frameId}.order-{index}"),
                $"将 {string.Join("、", order.Select(cardId =>
                    _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == cardId).DisplayName))} 依次置于牌堆底（从底向上）。",
                order, [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "order-bound-cards",
                    ["source-bind"] = sourceBind,
                    ["except-bind"] = exceptBind ?? string.Empty
                })).ToArray();
            var skill = _contentRegistry!.GetSkill(frame.SkillId);
            _pendingDecision = new PendingDecision(
                DecisionKind.ProgramTrigger, ownerSeat,
                $"【{skill.Name}】请安排牌堆底牌顺序。",
                selected.Select(item => item.CardId).ToArray(), [], ownerSeat)
            {
                PromptId = CreatePromptId(),
                IsPrivate = true,
                TargetSeat = ownerSeat,
                SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                    $"{skill.Name} · 牌堆底顺序", "选择剩余牌从牌堆底向上的顺序。"),
                Choices = Array.AsReadOnly(choices)
            };
            _status = _players[ownerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (bottomOrder is not null)
        {
            if (destination != SkillProgramCardDestination.DrawPileBottom ||
                !bottomOrder.OrderBy(id => id).SequenceEqual(selected.Select(item => item.CardId).OrderBy(id => id)))
                throw new InvalidOperationException("The bottom order must be an exact permutation of the bound cards.");
            selected = bottomOrder.Select(cardId => selected.Single(item => item.CardId == cardId)).ToArray();
        }
        if (destination == SkillProgramCardDestination.DrawPileBottom &&
            selected.Any(item => item.Location != CardLocation.Processing))
            throw new InvalidOperationException("Draw-pile bottom placement requires revealed processing cards.");
        var target = destination switch
        {
            SkillProgramCardDestination.OwnerHand => CardLocation.Hand(ownerSeat),
            SkillProgramCardDestination.DiscardPile => CardLocation.DiscardPile,
            SkillProgramCardDestination.DrawPileBottom => CardLocation.DrawPile,
            SkillProgramCardDestination.DrawPileTop => CardLocation.DrawPile,
            SkillProgramCardDestination.SelectedTargetHand when frame.SelectedTargetSeats.Count == 1 =>
                CardLocation.Hand(frame.SelectedTargetSeats.Single()),
            SkillProgramCardDestination.PhaseOwnerHand when
                frame.WindowContext is { Window: SkillProgramTriggerWindow.PlayPhaseStarting, SourceSeat: { } phaseSeat } &&
                IsValidPlayerSeat(phaseSeat) => CardLocation.Hand(phaseSeat),
            SkillProgramCardDestination.OwnerPersistentZone when destinationZone is
                CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or
                CardZoneKind.Chunlao => new CardLocation(destinationZone.Value, ownerSeat),
            _ => throw new InvalidOperationException($"Unsupported program card destination '{destination}'.")
        };
        foreach (var group in selected.GroupBy(item => item.Location))
        {
            if (group.Key == target) continue;
            var cards = group.Select(item => _cardZones.CardsAt(group.Key)
                .Single(card => card.Id == item.CardId)).ToArray();
            MoveCards(cards, group.Key, target, reason);
            switch (destination)
            {
                case SkillProgramCardDestination.DrawPileBottom:
                    if (group.Key != CardLocation.Processing)
                        throw new InvalidOperationException("Draw-pile bottom placement requires revealed processing cards.");
                    _cardZones.PlaceDrawPileCardsAtBottom(cards.Select(card => card.Id).ToArray());
                    break;
                case SkillProgramCardDestination.DrawPileTop:
                    // The moved segment sits at the pile's top; restore the
                    // binding order so its first id is drawn next.
                    _cardZones.PlaceDrawPileCardsAtTop(cards.Select(card => card.Id).ToArray());
                    break;
            }
        }
        return SkillProgramStepOutcome.Continue;
    }

    private static IEnumerable<int[]> EnumerateProgramCardOrders(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) { yield return []; yield break; }
        for (var index = 0; index < ids.Count; index++)
        {
            var rest = ids.Where((_, current) => current != index).ToArray();
            foreach (var suffix in EnumerateProgramCardOrders(rest))
                yield return [ids[index], .. suffix];
        }
    }

    private IReadOnlyList<CardSnapshot> GetProgramPublicCards()
    {
        var publicIds = _resolutionStack.OfType<ProgramSkillFrame>()
            .SelectMany(frame => frame.CardSetBindings)
            .Where(binding => binding.Visibility == SkillProgramCardSetVisibility.Public)
            .SelectMany(binding => binding.CardIds)
            .Distinct()
            .Order()
            .ToArray();
        return publicIds
            .Select(cardId =>
            {
                var location = _cardZones.GetLocation(cardId);
                return _cardZones.CardsAt(location).Single(card => card.Id == cardId);
            })
            .Select(ToSnapshot)
            .ToArray();
    }

    private void GrantProgramTurnSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.TriggerId != frame.TriggerId)
            throw new InvalidOperationException("Turn-scoped skill grants require the active program binding.");

        var owner = _players[frame.OwnerSeat];
        var granted = skillIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var skillId in granted) _ = _contentRegistry!.GetSkill(skillId);
        var sourceId = $"turn:{_turnNumber}:{frame.SkillId}:{GetProgramBindingId(frame)}";
        foreach (var skillId in granted)
        {
            var grantId = $"{sourceId}:{skillId}";
            if (!owner.SkillGrants.Grants.Any(grant => grant.GrantId == grantId))
                owner.SkillGrants.Grant(new SkillGrant(grantId, skillId, grantId, sourceId));
        }
        QueueGameEvent(new ProgramTurnSkillsGrantedEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            owner.Seat,
            Array.AsReadOnly(granted)));
    }

    private bool HasPendingProgramBoundCards =>
        _resolutionStack.OfType<ProgramSkillFrame>()
            .SelectMany(frame => frame.CardSetBindings)
            .SelectMany(binding => binding.CardIds)
            .Distinct()
            .Any(cardId => _cardZones.GetLocation(cardId) == CardLocation.Processing);

    private bool IsProgramProcessingConsistent(
        AttackResolution attack,
        IReadOnlyList<Card> processing)
    {
        var boundIds = _resolutionStack.OfType<ProgramSkillFrame>()
            .SelectMany(frame => frame.CardSetBindings)
            .SelectMany(binding => binding.CardIds)
            .Distinct()
            .Where(cardId => _cardZones.GetLocation(cardId) == CardLocation.Processing)
            .ToHashSet();
        if (boundIds.Count == 0 || boundIds.Any(cardId => processing.All(card => card.Id != cardId)))
            return false;

        var allowedParentIds = attack.PhysicalCards.Select(card => card.Id).ToHashSet();
        if (_pendingJudgment?.Attack is { } judgmentAttack)
            allowedParentIds.UnionWith(judgmentAttack.PhysicalCards.Select(card => card.Id));
        if (_pendingBorrowedSword is { } borrowedSword)
            allowedParentIds.Add(borrowedSword.Card.Id);
        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is { } programFrame)
            allowedParentIds.UnionWith(programFrame.Action.PhysicalCards.Select(cost => cost.CardId));

        return processing.Where(card => !boundIds.Contains(card.Id))
            .All(card => allowedParentIds.Contains(card.Id));
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectProgramTriggerCandidates(
        CharacterState owner,
        SkillProgramTriggerWindow window,
        int occurrenceIndex = 0) =>
        GetSkillBindingShard(owner).GetInstanceTriggers(window)
            .Select(binding => new ProgramTriggerCandidate(
                owner.Seat,
                binding.SkillId,
                binding.Trigger.Id,
                binding.SkillInstanceId,
                binding.Program.GameplayHash,
                binding.Trigger.Priority,
                occurrenceIndex))
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => GetProgramTrigger(candidate).ChoiceGroup ?? candidate.BindingId,
                StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal)
            .ToArray();

    private SkillProgramTrigger GetProgramTrigger(ProgramTriggerCandidate candidate) =>
        _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
            .Single(trigger => trigger.Id == candidate.BindingId);

    private IReadOnlyList<ProgramTriggerCandidate> CollectEligibleProgramTriggerCandidates(
        CharacterState owner,
        SkillProgramTriggerWindow window,
        SkillProgramTriggerFacts facts)
    {
        return CollectProgramTriggerCandidates(owner, window)
            .Where(candidate =>
            {
                var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                    .Single(item => item.Id == candidate.BindingId);
                return trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId) &&
                    HasInitialOwnedCardSelectionCandidates(owner, trigger);
            })
            .ToArray();
    }

    private bool HasInitialOwnedCardSelectionCandidates(CharacterState owner, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.SkipWhile(effect => effect.Op == SkillProgramEffectOp.SelectTarget)
                .FirstOrDefault() is not
                {
                    Op: SkillProgramEffectOp.SelectOwnedCards,
                    Target: SkillProgramEffectTarget.Owner,
                    Condition.Kind: SkillProgramConditionKind.Always
                } selection)
            return true;
        var required = selection.MinimumCards > 0 ? selection.MinimumCards : selection.Amount;
        if (required <= 0) return true;
        var available = selection.Zones.Sum(zone =>
            _cardZones.CardsAt(new CardLocation(zone, owner.Seat)).Count(card =>
                (selection.CardKinds.Count == 0 || selection.CardKinds.Contains(card.Kind)) &&
                (selection.Suits.Count == 0 || selection.Suits.Contains(GetProgramEffectiveSuit(owner, card)))));
        return available >= required;
    }

    private bool CanRunProgramTrigger(
        ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context)
    {
        if (!IsValidPlayerSeat(candidate.OwnerSeat) || candidate.OwnerSeat != context.OwnerSeat) return false;
        var owner = _players[candidate.OwnerSeat];
        if (context.Window != SkillProgramTriggerWindow.OwnerDied && !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, candidate.SkillId, candidate.SkillInstanceId) ||
            _contentRegistry.Skills.GetValueOrDefault(candidate.SkillId)?.Program is not { } program ||
            program.GameplayHash != candidate.GameplayHash)
            return false;
        var trigger = program.Triggers.SingleOrDefault(item => item.Id == candidate.BindingId);
        if (trigger is null || trigger.Window != context.Window)
            return false;
        if (!trigger.Condition.Evaluate(context.Facts ?? CaptureProgramTriggerFacts(owner),
                candidate.SkillId, candidate.SkillInstanceId))
            return false;
        if (!HasInitialOwnedCardSelectionCandidates(owner, trigger)) return false;
        if (trigger.UsageScope is { } scope && trigger.UsageLimit is { } limit &&
            _skillRuntimeState.GetUsage(
                owner.Seat, candidate.SkillId, ProgramTriggerUsageId(candidate), scope) >= limit)
            return false;
        // Only an initial, unconditional payment is a prerequisite. A later payment
        // may intentionally use cards or participants produced by earlier nodes.
        var initialPayments = trigger.Effects.SkipWhile(effect => effect.Op == SkillProgramEffectOp.SelectTarget)
            .TakeWhile(effect => effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
                                 effect.Condition.Kind == SkillProgramConditionKind.Always &&
                                 effect.CardOwnerRef?.Kind == ProgramParticipantRef.Owner &&
                                 effect.Destination == SkillProgramCardDestination.DiscardPile)
            .ToArray();
        if (initialPayments.Length > 1)
        {
            var available = initialPayments.SelectMany(effect => effect.Zones)
                .Distinct().SelectMany(zone => _cardZones.CardsAt(new CardLocation(zone, owner.Seat))
                    .Where(card => zone != CardZoneKind.Equipment ||
                        !IsActiveProgramSourceEquipmentCard(owner.Seat, candidate.SkillId,
                            candidate.SkillInstanceId, card)))
                .Select(card => card.Id).Distinct().Count();
            if (available < initialPayments.Sum(effect => effect.Amount)) return false;
        }
        if (trigger.Effects.SkipWhile(effect => effect.Op == SkillProgramEffectOp.SelectTarget).FirstOrDefault() is
            {
                Op: SkillProgramEffectOp.SelectAndMoveOwnedCard,
                Condition.Kind: SkillProgramConditionKind.Always
            } payment)
        {
            var payer = payment.CardOwnerRef?.Kind switch
            {
                ProgramParticipantRef.Owner => owner.Seat,
                ProgramParticipantRef.Actor => context.CardUse?.ActorSeat,
                ProgramParticipantRef.EventTarget => context.TargetSeat,
                _ => null
            };
            if (payer is { } payerSeat && (!IsValidPlayerSeat(payerSeat) ||
                !payment.Zones.Any(zone => _cardZones.CardsAt(new CardLocation(zone, payerSeat)).Any(card =>
                    (payment.CardCategories.Count == 0 ||
                     MatchesProgramCardCategory(card.Kind, payment.CardCategories)) &&
                    (zone != CardZoneKind.Equipment || payerSeat != owner.Seat ||
                     !IsActiveProgramSourceEquipmentCard(owner.Seat, candidate.SkillId,
                         candidate.SkillInstanceId, card))))))
                return false;
        }
        if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectTarget &&
            effect.TargetKind is { } kind && !GetProgramTargetSeats(owner.Seat, kind, context, effect.Marker).Any(seat =>
                effect.Zones.Count == 0 || effect.Zones.Any(zone => zone switch
                {
                    CardZoneKind.Hand => GetHand(_players[seat]).Count > 0,
                    CardZoneKind.Equipment => GetEquipment(_players[seat]).Count > 0,
                    CardZoneKind.Judgment => GetJudgment(_players[seat]).Count > 0,
                    _ => false
                }))))
            return false;
        if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectTargets &&
            effect.TargetKind == SkillProgramTargetKind.CurrentCardUseTargets &&
            GetProgramTargetSeats(owner.Seat, SkillProgramTargetKind.CurrentCardUseTargets, context).Count <
                effect.MinimumTargets))
            return false;
        return context.Window switch
        {
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat,
            SkillProgramTriggerWindow.DrawPhaseStarting =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat &&
                _phase == TurnPhase.Draw && CanRunDrawPhaseProgramTrigger(owner, trigger),
            SkillProgramTriggerWindow.AfterNormalDraw =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat &&
                _phase == TurnPhase.Draw,
            SkillProgramTriggerWindow.PlayEnding =>
                context.SourceSeat == _currentSeat && _phase == TurnPhase.Play &&
                (trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own
                    ? owner.Seat == _currentSeat : owner.Seat != _currentSeat),
            SkillProgramTriggerWindow.PlayPhaseStarting =>
                context.SourceSeat == _currentSeat && _phase == TurnPhase.Play &&
                (trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own
                    ? owner.Seat == _currentSeat
                    : owner.Seat != _currentSeat && _players[_currentSeat].IsAlive) &&
                _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().LastOrDefault() is { } starting &&
                starting.Id == context.ParentFrameId && starting.OwnerSeat == _currentSeat,
            SkillProgramTriggerWindow.TurnEnding =>
                context.SourceSeat == _currentSeat && context.TargetSeat == _currentSeat &&
                (trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own
                    ? owner.Seat == _currentSeat
                    : owner.Seat != _currentSeat && _players[_currentSeat].IsAlive) &&
                _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault() is { } ending &&
                ending.Id == context.ParentFrameId && ending.OwnerSeat == _currentSeat,
            SkillProgramTriggerWindow.SelfDyingResponse =>
                _pendingDying is { } dying && dying.FrameId == context.ParentFrameId &&
                dying.VictimSeat == owner.Seat && dying.ResponderSeat == owner.Seat && owner.Hp <= 0,
            SkillProgramTriggerWindow.DyingResponse =>
                _pendingDying is { } currentDying && currentDying.FrameId == context.ParentFrameId &&
                currentDying.VictimSeat == context.TargetSeat && currentDying.ResponderSeat == owner.Seat &&
                _players[currentDying.VictimSeat].Hp <= 0,
            SkillProgramTriggerWindow.BeforeDamageApplied =>
                _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().LastOrDefault() is { } beforeDamage &&
                beforeDamage.Id == context.ParentFrameId &&
                beforeDamage.TargetSeat == context.TargetSeat &&
                beforeDamage.SourceSeat == context.SourceSeat &&
                beforeDamage.Amount == context.Amount &&
                !beforeDamage.Prevented && beforeDamage.RedirectedTargetSeat is null &&
                (trigger.Subject == SkillProgramTriggerSubject.DamageTarget
                    ? owner.Seat == beforeDamage.TargetSeat &&
                      !(trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.RedirectCurrentDamage) &&
                        _pendingAttack?.DamageRedirected == true)
                    : owner.Seat != beforeDamage.TargetSeat),
            SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                SkillProgramTriggerWindow.AfterDamageApplied =>
                context.Amount > 0 && trigger.Subject switch
                {
                    SkillProgramTriggerSubject.Owner => context.TargetSeat == owner.Seat,
                    SkillProgramTriggerSubject.Source =>
                        context.SourceSeat == owner.Seat &&
                        _pendingDamageTrigger is { } sourceDamage &&
                        MatchesAfterDamageProgramSource(sourceDamage.Attack, trigger),
                    SkillProgramTriggerSubject.DamageSource => context.SourceSeat == owner.Seat,
                    SkillProgramTriggerSubject.Any => true,
                    _ => false
                } &&
                CanRunAfterDamageProgramTrigger(owner, trigger, context),
            SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHpRecovered =>
                context.HpChange is { } change && change.TargetSeat == owner.Seat && change.Amount > 0 &&
                _resolutionStack.OfType<HpChangedTriggerWindowFrame>().LastOrDefault() is { } hpWindow &&
                hpWindow.Id == context.ParentFrameId && hpWindow.Change == change &&
                hpWindow.Candidates[hpWindow.CandidateIndex] == candidate,
            SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained =>
                context.MovementBatch is { } batch &&
                batch.Id == context.ParentFrameId &&
                _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault()?.Id == batch.Id &&
                (context.Window == SkillProgramTriggerWindow.CardsGained ? batch.DestinationCounts ?? [] : batch.SourceCounts)
                    .Any(item => item.Location.OwnerSeat == owner.Seat),
            SkillProgramTriggerWindow.DiscardPileReceived =>
                context.MovementBatch is { } batch &&
                batch.Id == context.ParentFrameId &&
                _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault()?.Id == batch.Id &&
                batch.Movements.Any(item => item.To == CardLocation.DiscardPile &&
                    item.From.OwnerSeat is { } source && source != owner.Seat),
            SkillProgramTriggerWindow.OwnerDied =>
                !owner.IsAlive &&
                _pendingDeath is { } death &&
                death.VictimSeat == owner.Seat &&
                death.KillerSeat == context.SourceSeat &&
                _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().LastOrDefault() is { } deathWindow &&
                deathWindow.Id == context.ParentFrameId &&
                deathWindow.DeathFrameId == death.FrameId &&
                deathWindow.OwnerSeat == owner.Seat &&
                deathWindow.Candidates[deathWindow.CandidateIndex] == candidate,
            SkillProgramTriggerWindow.CharacterDied =>
                owner.IsAlive &&
                _pendingDeath is { } death &&
                death.KillerSeat == context.SourceSeat &&
                death.VictimSeat == context.TargetSeat &&
                _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().LastOrDefault() is { } killWindow &&
                killWindow.Id == context.ParentFrameId &&
                killWindow.DeathFrameId == death.FrameId &&
                killWindow.CandidateIndex < killWindow.Candidates.Count &&
                killWindow.Candidates[killWindow.CandidateIndex] == candidate &&
                killWindow.Contexts[killWindow.CandidateIndex] == context,
            SkillProgramTriggerWindow.JudgmentFinalized =>
                context.Judgment is { } finalized &&
                _pendingJudgment is { } pendingJudgment &&
                finalized.JudgmentFrameId == pendingJudgment.FrameId &&
                _resolutionStack.LastOrDefault() is ProgramJudgmentTriggerWindowFrame judgmentWindow &&
                judgmentWindow.Id == context.ParentFrameId &&
                judgmentWindow.CandidateIndex < judgmentWindow.Candidates.Count &&
                judgmentWindow.Candidates[judgmentWindow.CandidateIndex] ==
                    new ProgramJudgmentTriggerCandidate(candidate.OwnerSeat, candidate.SkillId,
                        candidate.BindingId, candidate.SkillInstanceId, candidate.GameplayHash) &&
                CanRunProgramJudgmentTrigger(judgmentWindow.Candidates[judgmentWindow.CandidateIndex],
                    trigger, finalized),
            SkillProgramTriggerWindow.JudgmentReplacing =>
                context.JudgmentReplacement is { } replacement &&
                _pendingJudgment is { } currentJudgment &&
                currentJudgment.FrameId == context.ParentFrameId &&
                replacement.JudgmentFrameId == currentJudgment.FrameId &&
                currentJudgment.CurrentCandidate is { } replacementCandidate &&
                replacementCandidate.OwnerSeat == candidate.OwnerSeat &&
                replacementCandidate.ProgramId == candidate.SkillId &&
                replacementCandidate.ProgramTriggerId == candidate.BindingId &&
                replacementCandidate.SkillInstanceId == candidate.SkillInstanceId &&
                currentJudgment.CurrentCard?.Id == replacement.OldCardId &&
                _resolutionStack.LastOrDefault() is JudgmentFrame replacementFrame &&
                replacementFrame.Id == currentJudgment.FrameId,
            SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or
                SkillProgramTriggerWindow.CardResponseAccepted or
                SkillProgramTriggerWindow.CardUseCompleted =>
                context.CardUse is { } cardUse &&
                _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is { } cardFrame &&
                cardFrame.Id == context.ParentFrameId && cardFrame.Action.ActionId == cardUse.CardActionId &&
                cardFrame.Candidates.Any(item => item.OwnerSeat == candidate.OwnerSeat &&
                    item.SkillId == candidate.SkillId && item.SkillInstanceId == candidate.SkillInstanceId &&
                    item.TriggerId == candidate.BindingId && item.GameplayHash == candidate.GameplayHash),
            SkillProgramTriggerWindow.SlashTargetRedirecting or
                SkillProgramTriggerWindow.SlashBeforeResponse or
                SkillProgramTriggerWindow.SlashFullyDodged =>
                context.CardUse is { } slashUse &&
                _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is { } slashFrame &&
                slashFrame.Id == context.ParentFrameId && slashFrame.Action.ActionId == slashUse.CardActionId &&
                _pendingAttack is { } pendingSlash && pendingSlash.TargetSeat == context.TargetSeat &&
                slashFrame.Candidates.Any(item => item.OwnerSeat == candidate.OwnerSeat &&
                    item.SkillId == candidate.SkillId && item.SkillInstanceId == candidate.SkillInstanceId &&
                    item.TriggerId == candidate.BindingId && item.GameplayHash == candidate.GameplayHash),
            SkillProgramTriggerWindow.DiscardPhaseStarting =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat &&
                _phase == TurnPhase.Discard,
            SkillProgramTriggerWindow.DiscardPhaseEnded =>
                owner.Seat != _currentSeat && owner.IsAlive &&
                context.SourceSeat == _currentSeat && _phase == TurnPhase.Discard,
            _ => false
        };
    }

    private bool CanRunDrawPhaseProgramTrigger(CharacterState owner, SkillProgramTrigger trigger)
    {
        foreach (var effect in trigger.Effects.Where(effect =>
                     effect.Op is (SkillProgramEffectOp.SelectTargets or SkillProgramEffectOp.SelectTarget) &&
                     effect.Condition.Evaluate(CreateSkillContext(owner))))
        {
            if (effect.Op == SkillProgramEffectOp.SelectTargets &&
                (effect.TargetKind is not { } targetKind ||
                 GetProgramTargetSeats(owner.Seat, targetKind, marker: effect.Marker).Count < effect.MinimumTargets ||
                 effect.NumberExpression == SkillProgramNumberExpression.PlannedNormalDrawCount &&
                 (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPlan ||
                  drawPlan.NormalDrawReplaced ||
                  Math.Max(0, checked((drawPlan.FrozenBaseDrawCount ?? GetTurnDrawCount(owner)) +
                      drawPlan.NormalDrawAdjustment)) <
                  effect.MinimumTargets)))
                return false;
            if (effect.Op == SkillProgramEffectOp.SelectTarget && effect.TargetKind is { } singleKind &&
                !GetProgramTargetSeats(owner.Seat, singleKind, marker: effect.Marker).Any(seat =>
                    effect.Zones.Count == 0 || effect.Zones.Any(zone => zone switch
                    {
                        CardZoneKind.Hand => GetHand(_players[seat]).Count > 0,
                        CardZoneKind.Equipment => GetEquipment(_players[seat]).Count > 0,
                        CardZoneKind.Judgment => GetJudgment(_players[seat]).Count > 0,
                        _ => false
                    })))
                return false;
        }
        return true;
    }

    private bool CanRunAfterDamageProgramTrigger(
        CharacterState owner,
        SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (_pendingDamageTrigger is not { } damage ||
            damage.FrameId != context.ParentFrameId ||
            damage.DamageFrameId != context.DamageFrameId ||
            damage.Window != context.Window)
            return false;
        if (trigger.DamageCardKinds.Count > 0 &&
            (damage.Attack.EffectiveCardKind is not { } kind || !trigger.DamageCardKinds.Contains(kind)))
            return false;
        if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartPindian) &&
            (context.TargetSeat is not { } opponentSeat ||
             !IsValidPlayerSeat(opponentSeat) || !owner.IsAlive || !_players[opponentSeat].IsAlive ||
             GetHand(owner).Count == 0 || GetHand(_players[opponentSeat]).Count == 0))
            return false;
        foreach (var effect in trigger.Effects.Where(effect =>
                     effect.Op is (SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectSourceCard or
                         SkillProgramEffectOp.ClaimDamageCards) &&
                     effect.Condition.CanEvaluateWithoutProgramFrame() &&
                     effect.Condition.Evaluate(CreateSkillContext(owner))))
        {
            switch (effect.Op)
            {
                case SkillProgramEffectOp.SelectTarget:
                    if (effect.TargetKind is not { } targetKind ||
                        GetProgramTargetSeats(owner.Seat, targetKind, context, effect.Marker).Count == 0)
                        return false;
                    break;
                case SkillProgramEffectOp.SelectSourceCard:
                    if (effect.SkipIfNoCards) break;
                    var sourceSeat = effect.CardSource switch
                    {
                        SkillProgramCardSource.Owner => owner.Seat,
                        SkillProgramCardSource.DamageSource => context.SourceSeat,
                        SkillProgramCardSource.EventTarget => context.TargetSeat,
                        _ => null
                    };
                    if (sourceSeat is not { } resolvedSourceSeat ||
                        (effect.CardSource == SkillProgramCardSource.DamageSource && resolvedSourceSeat == owner.Seat &&
                         !effect.AllowSameSource) ||
                        !IsValidPlayerSeat(resolvedSourceSeat) ||
                        !effect.Zones.Any(zone => zone switch
                        {
                            CardZoneKind.Hand => GetHand(_players[resolvedSourceSeat]).Count > 0,
                            CardZoneKind.Equipment => GetEquipment(_players[resolvedSourceSeat]).Any(card =>
                                effect.EquipmentSlots.Count == 0 ||
                                effect.EquipmentSlots.Contains(EquipmentCatalog.Get(card.Kind).Slot)),
                            _ => false
                        }))
                        return false;
                    break;
                case SkillProgramEffectOp.ClaimDamageCards:
                    if (!damage.Attack.PhysicalCards.Any(card =>
                            _cardZones.GetLocation(card.Id) == CardLocation.Processing))
                        return false;
                    break;
            }
        }
        return true;
    }

    private void ConsumeProgramTriggerUsage(ProgramTriggerCandidate candidate)
    {
        var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
            .Single(item => item.Id == candidate.BindingId);
        if (trigger.UsageScope is not { } scope || trigger.UsageLimit is not { } limit) return;
        if (!_skillRuntimeState.TryConsumeUsage(
                candidate.OwnerSeat, candidate.SkillId, ProgramTriggerUsageId(candidate), scope, limit))
            throw new InvalidOperationException("The configured trigger usage was already consumed.");
        QueueGameEvent(new SkillUsageConsumedEvent(
            candidate.OwnerSeat, candidate.SkillId, candidate.BindingId, scope, 1));
    }

    private string ProgramTriggerUsageId(ProgramTriggerCandidate candidate)
    {
        var trigger = GetProgramTrigger(candidate);
        var usageBinding = trigger.ChoiceGroup is { } choiceGroup
            ? $"choice-group:{choiceGroup}"
            : candidate.BindingId;
        return $"{usageBinding}@{candidate.SkillInstanceId}";
    }

    private SkillProgramTriggerFacts CaptureProgramTriggerFacts(CharacterState owner)
    {
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
            foreach (var definition in instance.Program.BooleanStates)
            {
                states[SkillProgramTriggerFacts.BooleanStateKey(
                    instance.SkillId, instance.SkillInstanceId, definition.Id)] =
                    GetProgramBooleanState(owner.Seat, instance.SkillId, instance.SkillInstanceId, definition.Id);
            }
        return new(
            CountCardsUsedByCurrentPlayerThisTurn(owner.Seat),
            owner.Hp,
            IsClassicIdentityMode,
            CardsUsedOrRespondedThisTurn: CountCardsUsedOrRespondedByPlayerThisTurn(owner.Seat),
            OwnerIsTurnPlayer: owner.Seat == _currentSeat,
            CurrentAttackRange: GetAttackRange(owner.Seat),
            CurrentMaxHp: owner.MaxHp,
            CurrentHandCount: GetHand(owner).Count,
            LivingPlayersMinHp: GetLivingPlayersMinHp(),
            TurnOwnerDiscardPhaseHandDiscardCount: TurnOwnerDiscardPhaseHandDiscardCount,
            LordGeneralId: _players.SingleOrDefault(player => player.Role == Role.Lord)?.General.Id,
            OwnedZoneCounts: new SkillProgramOwnedZoneCounts(
                _cardZones.Count(CardLocation.WoodenOxGrain(owner.Seat)),
                _cardZones.Count(CardLocation.BuquWound(owner.Seat)),
                _cardZones.Count(CardLocation.Authority(owner.Seat)),
                _cardZones.Count(CardLocation.Chunlao(owner.Seat))),
            BooleanStates: states,
            PlayPhaseKillCountByTurnOwner: _playPhaseKillCountByCurrentPlayer,
            PlayPhaseDamageDealtByTurnOwner: _playPhaseDamageDealtByCurrentPlayer,
            MarkerCounts: owner.Markers.Count > 0
                ? new Dictionary<PlayerMarkerKind, int>(owner.Markers)
                : null,
            OwnerIsFaceDown: owner.IsFaceDown);
    }

    private SkillProgramTriggerFacts CaptureProgramTriggerFacts(CharacterState owner, CardActionContext action) =>
        CaptureProgramTriggerFacts(owner) with
        {
            CardActionActorIsCurrentTurn = action.ActorSeat == _currentSeat,
            CardActionActorIsOwner = action.ActorSeat == owner.Seat,
            CardActionPhaseIsPlay = _phase == TurnPhase.Play,
            CardActionCategory = GetProgramCardCategory(action.EffectiveKind),
            CardActionFromOwnerHand = action.PhysicalCards.Count > 0 &&
                action.PhysicalCards.All(cost =>
                    cost.From is { Zone: CardZoneKind.Hand, OwnerSeat: { } holder } &&
                    holder == action.ActorSeat),
            CardActionCardIsRed = action.PhysicalCards.Count > 0 &&
                action.PhysicalCards.All(cost =>
                    _cardZones.CardsAt(_cardZones.GetLocation(cost.CardId))
                        .Single(card => card.Id == cost.CardId).Suit is Suit.Heart or Suit.Diamond),
            CardUseDesignatedTargetCount = action.Type == CardActionType.Use
                ? action.EffectiveDesignatedTargetSeats.Distinct().Count()
                : 0,
            CardUseConversionSkillIds = Array.AsReadOnly(action.ConversionChain
                .Select(source => source.SkillId).Distinct(StringComparer.Ordinal).ToArray())
        };

    private void BeginProgramBinding(
        ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context)
    {
        if (!CanRunProgramTrigger(candidate, context))
            throw new InvalidOperationException("The configured program binding is no longer eligible.");
        ConsumeProgramTriggerUsage(candidate);
        ClearPendingDecision();
        if (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame cardAction &&
            cardAction.Id == context.ParentFrameId)
            _resolutionStack[^1] = cardAction with { Activated = true };
        var frame = new ProgramSkillFrame(
            ++_resolutionSequence,
            candidate.OwnerSeat,
            candidate.SkillId,
            candidate.BindingId,
            candidate.GameplayHash,
            0,
            [],
            [])
        {
            SkillInstanceId = candidate.SkillInstanceId,
            TriggerId = candidate.BindingId,
            WindowContext = context
        };
        _resolutionStack.Add(frame);
        QueueGameEvent(new ProgramBindingStartedEvent(
            frame.Id, frame.SkillId, candidate.BindingId, frame.SkillInstanceId,
            frame.OwnerSeat, context.Window));
        AddLog("SkillTriggered",
            $"{_players[frame.OwnerSeat].Name} 发动【{_contentRegistry!.GetSkill(frame.SkillId).Name}】。",
            frame.OwnerSeat);
        ContinueProgramSkill(frame.Id);
    }

    private void ExposeProgramTriggerDecision(
        ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context)
    {
        var skill = _contentRegistry!.GetSkill(candidate.SkillId);
        var facts = context.Facts;
        var prompt = context.Window == SkillProgramTriggerWindow.PlayEnding &&
            context.SourceSeat == candidate.OwnerSeat && facts is not null
            ? $"出牌阶段结束：本回合已使用 {facts.CardsUsedThisTurn} 张牌，当前体力为 {facts.CurrentHp}。是否发动【{skill.Name}】？"
            : $"是否发动【{skill.Name}】？";
        Dictionary<string, string> Parameters(string action)
        {
            var parameters = new Dictionary<string, string>
            {
                ["program-action"] = action,
                ["skill-id"] = candidate.SkillId,
                ["binding-id"] = candidate.BindingId,
                ["skill-instance-id"] = candidate.SkillInstanceId
            };
            if (facts is not null)
            {
                parameters["cards-used-this-turn"] = facts.CardsUsedThisTurn.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                parameters["current-hp"] = facts.CurrentHp.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                parameters["classic-identity-mode"] = facts.IsClassicIdentityMode ? "true" : "false";
            }
            return parameters;
        }
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            candidate.OwnerSeat,
            prompt,
            [],
            [],
            context.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = context.TargetSeat ?? candidate.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(
                candidate.SkillId,
                skill.Name,
                $"{skill.Name} · 是否发动",
                skill.Description),
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"program-trigger.activate.{candidate.SkillId}.{candidate.BindingId}.{candidate.SkillInstanceId}"),
                    $"发动【{skill.Name}】。",
                    [], [],
                    Parameters("activate")),
                new PromptChoice(
                    new ChoiceId($"program-trigger.skip.{candidate.SkillId}.{candidate.BindingId}.{candidate.SkillInstanceId}"),
                    $"不发动【{skill.Name}】。",
                    [], [],
                    Parameters("skip"))
            ]
        };
        _status = _players[candidate.OwnerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private bool TryBeginTurnStartProgramWindow(CharacterState owner)
    {
        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, facts);
        if (candidates.Count == 0) return false;
        if (_resolutionStack.Count != 0)
            throw new InvalidOperationException("Turn-start program bindings require a clean boundary.");
        var frame = new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence,
            owner.Seat,
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
            candidates,
            ProgramLifecycleContinuation.NormalTurnStart,
            facts);
        _resolutionStack.Add(frame);
        ContinueProgramLifecycleWindow();
        return true;
    }

    private bool TryBeginPlayEndingProgramWindow(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Play ||
            _pendingDecision is not null || _resolutionStack.Count != 0)
            return false;
        var participants = _players.Where(player => player.IsAlive).ToArray();
        var participantFacts = participants.ToDictionary(player => player.Seat, CaptureProgramTriggerFacts);
        var facts = participantFacts[owner.Seat];
        var candidates = participants.SelectMany(player =>
                CollectEligibleProgramTriggerCandidates(player, SkillProgramTriggerWindow.PlayEnding,
                    participantFacts[player.Seat])
                    .Where(candidate => GetProgramTrigger(candidate).TurnOwnerScope ==
                        (player.Seat == owner.Seat ? SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving)))
            .OrderBy(candidate => (candidate.OwnerSeat - owner.Seat + _players.Count) % _players.Count)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return false;
        var frame = new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence, owner.Seat, SkillProgramTriggerWindow.PlayEnding,
            candidates, ProgramLifecycleContinuation.CompletePlayPhase, facts)
        { ParticipantFacts = participantFacts };
        _resolutionStack.Add(frame);
        ContinueProgramLifecycleWindow();
        return true;
    }

    private bool TryBeginDrawPhaseProgramWindow(CharacterState owner, bool skipPlayPhaseAfterDraw)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Draw ||
            _pendingDecision is not null || _resolutionStack.Count != 0)
            return false;
        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner, SkillProgramTriggerWindow.DrawPhaseStarting, facts);
        if (candidates.Count == 0) return false;
        var frame = new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence,
            owner.Seat,
            SkillProgramTriggerWindow.DrawPhaseStarting,
            candidates,
            ProgramLifecycleContinuation.CompleteDrawPhase,
            facts,
            SkipPlayPhaseAfterDraw: skipPlayPhaseAfterDraw);
        _resolutionStack.Add(frame);
        ContinueProgramLifecycleWindow();
        return true;
    }

    private bool TryBeginAfterNormalDrawProgramWindow(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Draw ||
            _pendingDecision is not null || _resolutionStack.Count != 0)
            return false;
        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner, SkillProgramTriggerWindow.AfterNormalDraw, facts);
        if (candidates.Count == 0) return false;
        _resolutionStack.Add(new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence, owner.Seat, SkillProgramTriggerWindow.AfterNormalDraw,
            candidates, ProgramLifecycleContinuation.CompleteAfterNormalDraw, facts));
        ContinueProgramLifecycleWindow();
        return true;
    }

    private bool TryBeginDiscardPhaseProgramWindow(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Discard ||
            _pendingDecision is not null || _resolutionStack.Count != 0)
            return false;
        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner, SkillProgramTriggerWindow.DiscardPhaseStarting, facts);
        if (candidates.Count == 0) return false;
        _resolutionStack.Add(new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence, owner.Seat, SkillProgramTriggerWindow.DiscardPhaseStarting,
            candidates, ProgramLifecycleContinuation.CompleteDiscardPhase, facts));
        ContinueProgramLifecycleWindow();
        return true;
    }

    private bool TryBeginDiscardPhaseEndedProgramWindow(CharacterState phaseOwner)
    {
        if (!phaseOwner.IsAlive || phaseOwner.Seat != _currentSeat || _phase != TurnPhase.Discard ||
            _pendingDecision is not null || _resolutionStack.Count != 0)
            return false;
        var participants = _players.Where(player => player.IsAlive).ToArray();
        var participantFacts = participants.ToDictionary(player => player.Seat, CaptureProgramTriggerFacts);
        var candidates = participants.SelectMany(player =>
                CollectEligibleProgramTriggerCandidates(player, SkillProgramTriggerWindow.DiscardPhaseEnded,
                    participantFacts[player.Seat])
                    .Where(candidate => GetProgramTrigger(candidate).TurnOwnerScope ==
                        (player.Seat == phaseOwner.Seat ? SkillProgramTurnOwnerScope.Own :
                            SkillProgramTurnOwnerScope.OtherLiving)))
            .OrderBy(candidate => (candidate.OwnerSeat - phaseOwner.Seat + _players.Count) % _players.Count)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return false;
        var frame = new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence, phaseOwner.Seat, SkillProgramTriggerWindow.DiscardPhaseEnded,
            candidates, ProgramLifecycleContinuation.EndTurnAfterDiscardPhase,
            participantFacts[phaseOwner.Seat])
        { ParticipantFacts = participantFacts };
        _resolutionStack.Add(frame);
        ContinueProgramLifecycleWindow();
        return true;
    }

    private bool TryBeginTurnEndingBoundary(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _pendingDecision is not null ||
            _resolutionStack.Count != 0)
            return false;

        var facts = CaptureProgramTriggerFacts(owner);
        var items = _players.Where(player => player.IsAlive)
            .SelectMany(player =>
            {
                var playerFacts = player.Seat == owner.Seat ? facts : CaptureProgramTriggerFacts(player);
                return CollectEligibleProgramTriggerCandidates(
                        player, SkillProgramTriggerWindow.TurnEnding, playerFacts)
                    .Where(candidate => GetProgramTrigger(candidate).TurnOwnerScope ==
                        (player.Seat == owner.Seat ? SkillProgramTurnOwnerScope.Own :
                            SkillProgramTurnOwnerScope.OtherLiving))
                    .Select(candidate => new TurnEndingBoundaryItem(
                        TurnEndingBoundaryItemKind.Program,
                        candidate.Priority,
                        $"program:{candidate.SkillId}:{candidate.BindingId}:{candidate.SkillInstanceId}",
                        candidate, player.Seat == owner.Seat ? null : playerFacts));
            }).ToList();
        var ordered = items
            .OrderBy(item => ((item.Candidate!.OwnerSeat - owner.Seat + _players.Count) % _players.Count))
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.StableIdentity, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0) return false;

        _resolutionStack.Add(new TurnEndingBoundaryFrame(
            ++_resolutionSequence,
            owner.Seat,
            _turnNumber,
            Array.AsReadOnly(ordered),
            facts));
        ContinueTurnEndingBoundary();
        return true;
    }

    private ProgramSkillWindowContext CreateTurnEndingProgramContext(
        TurnEndingBoundaryFrame frame,
        ProgramTriggerCandidate candidate,
        SkillProgramTriggerFacts? candidateFacts = null) =>
        new(
            SkillProgramTriggerWindow.TurnEnding,
            frame.Id,
            candidate.OwnerSeat,
            SourceSeat: frame.OwnerSeat,
            TargetSeat: frame.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: candidateFacts ?? frame.Facts);

    private void ContinueTurnEndingBoundary()
    {
        while (_resolutionStack.LastOrDefault() is TurnEndingBoundaryFrame frame)
        {
            // A child may decide the winner without killing the turn owner (for
            // example, redirected damage). No later observer may start then.
            if (_winner != Winner.None)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.TurnEndingBoundary);
                if (_status != EngineStatus.Completed) CompleteGame();
                else PublishState();
                return;
            }
            if (frame.ItemIndex >= frame.Items.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.TurnEndingBoundary);
                FinalizeEndTurn(_players[frame.OwnerSeat]);
                return;
            }

            var item = frame.Items[frame.ItemIndex];
            switch (item.Kind)
            {
                case TurnEndingBoundaryItemKind.Program:
                    {
                        var candidate = item.Candidate ??
                            throw new InvalidOperationException("A program turn-ending item lost its candidate.");
                        var context = CreateTurnEndingProgramContext(frame, candidate, item.Facts);
                        if (!CanRunProgramTrigger(candidate, context))
                        {
                            AdvanceTurnEndingBoundaryCandidate(frame, candidate, activated: false, completed: false);
                            continue;
                        }
                        var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                            .Single(current => current.Id == candidate.BindingId);
                        if (trigger.Optional)
                        {
                            _resolutionStack[^1] = frame with { Step = ResolutionFrameStep.AwaitingResponse };
                            ExposeProgramTriggerDecision(candidate, context);
                            return;
                        }
                        BeginProgramBinding(candidate, context);
                        return;
                    }
                default:
                    throw new InvalidOperationException("Unsupported turn-ending boundary item.");
            }
        }
    }

    private void AdvanceTurnEndingBoundaryCandidate(
        TurnEndingBoundaryFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        QueueGameEvent(new ProgramBindingResolvedEvent(
            frame.Id, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
            candidate.OwnerSeat, SkillProgramTriggerWindow.TurnEnding, activated, completed));
        AdvanceTurnEndingBoundaryCursor(frame);
    }

    private void AdvanceTurnEndingBoundaryCursor(TurnEndingBoundaryFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not TurnEndingBoundaryFrame current ||
            current.Id != frame.Id || current.ItemIndex != frame.ItemIndex)
            throw new InvalidOperationException("The turn-ending item cursor is no longer current.");
        _resolutionStack[^1] = current with
        {
            ItemIndex = current.ItemIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        };
    }

    private bool TryBeginPlayPhaseStartingBoundary(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _pendingDecision is not null ||
            _resolutionStack.Count != 0)
            return false;

        var facts = CaptureProgramTriggerFacts(owner);
        var items = _players.Where(player => player.IsAlive)
            .SelectMany(player =>
            {
                var playerFacts = player.Seat == owner.Seat ? facts : CaptureProgramTriggerFacts(player);
                return CollectEligibleProgramTriggerCandidates(
                        player, SkillProgramTriggerWindow.PlayPhaseStarting, playerFacts)
                    .Where(candidate => GetProgramTrigger(candidate).TurnOwnerScope ==
                        (player.Seat == owner.Seat ? SkillProgramTurnOwnerScope.Own :
                            SkillProgramTurnOwnerScope.OtherLiving))
                    .Select(candidate => new TurnEndingBoundaryItem(
                        TurnEndingBoundaryItemKind.Program,
                        candidate.Priority,
                        $"program:{candidate.SkillId}:{candidate.BindingId}:{candidate.SkillInstanceId}",
                        candidate, player.Seat == owner.Seat ? null : playerFacts));
            }).ToList();
        var ordered = items
            .OrderBy(item => ((item.Candidate!.OwnerSeat - owner.Seat + _players.Count) % _players.Count))
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.StableIdentity, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0) return false;

        _resolutionStack.Add(new PlayPhaseStartingBoundaryFrame(
            ++_resolutionSequence, owner.Seat, Array.AsReadOnly(ordered), facts));
        ContinuePlayPhaseStartingBoundary();
        return true;
    }

    private ProgramSkillWindowContext CreatePlayPhaseStartingProgramContext(
        PlayPhaseStartingBoundaryFrame frame,
        ProgramTriggerCandidate candidate,
        SkillProgramTriggerFacts? candidateFacts = null) =>
        new(
            SkillProgramTriggerWindow.PlayPhaseStarting,
            frame.Id,
            candidate.OwnerSeat,
            SourceSeat: frame.OwnerSeat,
            TargetSeat: frame.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: candidateFacts ?? frame.Facts);

    private void ContinuePlayPhaseStartingBoundary()
    {
        while (_resolutionStack.LastOrDefault() is PlayPhaseStartingBoundaryFrame frame)
        {
            if (frame.ItemIndex >= frame.Items.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.PlayPhaseStartingBoundary);
                PublishState();
                return;
            }

            var item = frame.Items[frame.ItemIndex];
            if (item.Kind != TurnEndingBoundaryItemKind.Program)
                throw new InvalidOperationException("Unsupported play-phase-starting boundary item.");
            var candidate = item.Candidate ??
                throw new InvalidOperationException("A program play-phase-starting item lost its candidate.");
            var context = CreatePlayPhaseStartingProgramContext(frame, candidate, item.Facts);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvancePlayPhaseStartingCandidate(frame, candidate, activated: false, completed: false);
                continue;
            }
            var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                .Single(current => current.Id == candidate.BindingId);
            if (trigger.ChoiceGroup is { } choiceGroup)
            {
                var members = GetPlayPhaseStartingChoiceGroup(frame, candidate, choiceGroup);
                if (members.Count > 1 || trigger.Optional)
                {
                    _resolutionStack[^1] = frame with { Step = ResolutionFrameStep.AwaitingResponse };
                    ExposeProgramTriggerGroupDecision(members, candidate, context, choiceGroup);
                    return;
                }
                BeginProgramBinding(candidate, context);
                return;
            }
            if (trigger.Optional)
            {
                _resolutionStack[^1] = frame with { Step = ResolutionFrameStep.AwaitingResponse };
                ExposeProgramTriggerDecision(candidate, context);
                return;
            }
            BeginProgramBinding(candidate, context);
            return;
        }
    }

    private void AdvancePlayPhaseStartingCandidate(
        PlayPhaseStartingBoundaryFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        QueueGameEvent(new ProgramBindingResolvedEvent(
            frame.Id, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
            candidate.OwnerSeat, SkillProgramTriggerWindow.PlayPhaseStarting, activated, completed));
        AdvancePlayPhaseStartingCursor(frame);
    }

    private void AdvancePlayPhaseStartingCursor(PlayPhaseStartingBoundaryFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not PlayPhaseStartingBoundaryFrame current ||
            current.Id != frame.Id || current.ItemIndex != frame.ItemIndex)
            throw new InvalidOperationException("The play-phase-starting item cursor is no longer current.");
        _resolutionStack[^1] = current with
        {
            ItemIndex = current.ItemIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        };
    }

    private void ContinueProgramLifecycleWindow()
    {
        while (_resolutionStack.LastOrDefault() is ProgramLifecycleTriggerWindowFrame frame)
        {
            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramLifecycleTriggerWindow);
                switch (frame.Continuation)
                {
                    case ProgramLifecycleContinuation.NormalTurnStart:
                        BeginNormalTurnStartAfterProgramBindings(_players[frame.OwnerSeat]);
                        break;
                    case ProgramLifecycleContinuation.CompleteDrawPhase:
                        CompleteDrawPhaseAfterProgramWindow(
                            _players[frame.OwnerSeat], frame.SkipPlayPhaseAfterDraw,
                            frame.NormalDrawReplaced, frame.NormalDrawAdjustment,
                            frame.FrozenBaseDrawCount);
                        break;
                    case ProgramLifecycleContinuation.CompletePlayPhase:
                        CompletePlayPhaseAfterProgramWindow();
                        break;
                    case ProgramLifecycleContinuation.CompleteAfterNormalDraw:
                        CompleteTurnStartAfterDraw(_players[frame.OwnerSeat], _pendingTurnDelayedEffects,
                            afterNormalDrawProgramsCompleted: true);
                        break;
                    case ProgramLifecycleContinuation.CompleteDiscardPhase:
                        CompleteDiscardPhaseAfterProgramWindow(_players[frame.OwnerSeat]);
                        break;
                    case ProgramLifecycleContinuation.EndTurnAfterDiscardPhase:
                        EndTurn();
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported lifecycle continuation.");
                }
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = new ProgramSkillWindowContext(
                frame.Window, frame.Id, candidate.OwnerSeat,
                SourceSeat: frame.OwnerSeat,
                TargetSeat: frame.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: frame.ParticipantFacts?.GetValueOrDefault(candidate.OwnerSeat) ?? frame.Facts);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceProgramLifecycleCandidate(frame, activated: false, completed: false);
                continue;
            }
            var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                .Single(item => item.Id == candidate.BindingId);
            if (trigger.ChoiceGroup is { } choiceGroup)
            {
                var members = GetProgramTriggerChoiceGroup(frame, candidate, choiceGroup);
                if (members.Count > 1 || trigger.Optional)
                    ExposeProgramTriggerGroupDecision(members, candidate, context, choiceGroup);
                else
                    BeginProgramBinding(candidate, context);
                return;
            }
            if (trigger.Optional)
            {
                ExposeProgramTriggerDecision(candidate, context);
                return;
            }
            BeginProgramBinding(candidate, context);
            return;
        }
    }

    private void AdvanceProgramLifecycleCandidate(
        ProgramLifecycleTriggerWindowFrame frame,
        bool activated,
        bool completed)
    {
        var candidate = frame.Candidates[frame.CandidateIndex];
        QueueGameEvent(new ProgramBindingResolvedEvent(
            frame.Id, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
            candidate.OwnerSeat, frame.Window, activated, completed));
        AdvanceProgramLifecycleCursor(frame);
    }

    private void AdvanceProgramLifecycleCursor(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The lifecycle trigger cursor is no longer current.");
        _resolutionStack[^1] = current with { CandidateIndex = current.CandidateIndex + 1 };
    }

    private IReadOnlyList<ProgramTriggerCandidate> GetProgramTriggerChoiceGroup(
        ProgramLifecycleTriggerWindowFrame frame,
        ProgramTriggerCandidate first,
        string choiceGroup) =>
        GetTriggerChoiceGroupMembers(
            frame.Candidates.Skip(frame.CandidateIndex), first, choiceGroup);

    private IReadOnlyList<ProgramTriggerCandidate> GetPlayPhaseStartingChoiceGroup(
        PlayPhaseStartingBoundaryFrame frame,
        ProgramTriggerCandidate first,
        string choiceGroup) =>
        GetTriggerChoiceGroupMembers(
            frame.Items.Skip(frame.ItemIndex)
                .Select(item => item.Candidate)
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!), first, choiceGroup);

    private IReadOnlyList<ProgramTriggerCandidate> GetTriggerChoiceGroupMembers(
        IEnumerable<ProgramTriggerCandidate> candidatesFromCursor,
        ProgramTriggerCandidate first,
        string choiceGroup)
    {
        var members = candidatesFromCursor
            .TakeWhile(candidate =>
                candidate.OwnerSeat == first.OwnerSeat &&
                candidate.SkillId == first.SkillId &&
                candidate.SkillInstanceId == first.SkillInstanceId &&
                candidate.Priority == first.Priority &&
                GetProgramTrigger(candidate).ChoiceGroup == choiceGroup)
            .ToArray();
        if (members.Length == 0)
            throw new InvalidOperationException("A configured program choice group lost every eligible branch.");
        return members;
    }

    private void ExposeProgramTriggerGroupDecision(
        IReadOnlyList<ProgramTriggerCandidate> members,
        ProgramTriggerCandidate first,
        ProgramSkillWindowContext context,
        string choiceGroup)
    {
        var skill = _contentRegistry!.GetSkill(first.SkillId);
        Dictionary<string, string> Parameters(ProgramTriggerCandidate candidate, string action) => new()
        {
            ["program-action"] = action,
            ["skill-id"] = candidate.SkillId,
            ["binding-id"] = candidate.BindingId,
            ["skill-instance-id"] = candidate.SkillInstanceId,
            ["choice-group"] = choiceGroup
        };
        var choices = members.Select(candidate =>
        {
            var trigger = GetProgramTrigger(candidate);
            return new PromptChoice(
                new ChoiceId($"program-trigger.activate.{candidate.SkillId}.{candidate.BindingId}.{candidate.SkillInstanceId}"),
                trigger.ChoiceLabel ?? throw new InvalidOperationException(
                    "A configured program choice branch lost its presentation label."),
                [], [], Parameters(candidate, "activate"));
        }).ToList();
        if (GetProgramTrigger(first).Optional)
            choices.Add(new PromptChoice(
                new ChoiceId($"program-trigger.skip-group.{first.SkillId}.{choiceGroup}.{first.SkillInstanceId}"),
                $"不发动【{skill.Name}】。",
                [], [], Parameters(first, "skip")));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            first.OwnerSeat,
            $"请选择【{skill.Name}】的发动方式。",
            [], [], context.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = context.TargetSeat ?? first.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(
                first.SkillId, skill.Name, $"{skill.Name} · 选择方式", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[first.OwnerSeat].IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private void AdjustProgramNormalDraw(ProgramSkillFrame frame, int amount)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                ParentFrameId: var parentFrameId
            } ||
            frame.TriggerId is null || amount == 0 || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != parentFrameId ||
            parent.Continuation != ProgramLifecycleContinuation.CompleteDrawPhase)
        {
            throw new InvalidOperationException(
                "A normal-draw adjustment requires the active draw-phase program and its parent window.");
        }

        var total = checked(parent.NormalDrawAdjustment + amount);
        _resolutionStack[^2] = parent with { NormalDrawAdjustment = total };
        QueueGameEvent(new ProgramNormalDrawAdjustedEvent(
            frame.Id,
            frame.SkillId,
            frame.TriggerId,
            frame.OwnerSeat,
            amount,
            total));
    }

    private CommandResult SubmitProgramTriggerAnswer(int actorSeat, PromptId promptId, ChoiceId choiceId)
    {
        var error = ValidateHumanPrompt(actorSeat, DecisionKind.ProgramTrigger, promptId,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var selected = _pendingDecision!.Choices.SingleOrDefault(choice => choice.Id == choiceId);
        if (selected is null) return Reject(CommandErrorCode.InvalidChoice, "The program choice is unavailable.");
        if (selected.Parameters.GetValueOrDefault("program-action") == "choose-option" &&
            !IsClaimableProgramOptionStillAvailable(selected))
            return Reject(CommandErrorCode.InvalidChoice, "The damage cards are no longer available for this choice.");
        return Accept(() =>
        {
            ResolveProgramTriggerChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveProgramTriggerChoice(PromptChoice selected)
    {
        var action = selected.Parameters.GetValueOrDefault("program-action");
        if (action == "choose-option")
        {
            ResolveProgramOptionChoice(selected);
            return;
        }
        if (action is "select-target" or "select-targets" or "select-source-card" or "select-and-move-owned-card" or
            "choose-other-owned-card-discard" or "choose-other-owned-card-decline" or
            "restore-phase-hand-discard" or "restore-phase-hand-decline" or
            "choose-own-card-discard" or
            "select-owned-cards" or "finish-owned-cards" or
            "give-bound-card" or "keep-bound-cards" or
            "distribute-owned-card" or "decline-owned-card-distribution" or
            "reveal-target-hand-card" or "reveal-target-hand-card-decline" or
            "request-slash" or "request-slash-decline" or
            "attack-range-aid-discard-weapon" or "attack-range-aid-draw")
        {
            ResolveProgramInstructionChoice(selected, action);
            return;
        }
        if (action is "different-category-discard" or "different-category-decline")
        {
            ResolveProgramDifferentCategoryDiscardChoice(selected);
            return;
        }
        if (action == "use-all-hand-as-ordinary-trick")
        {
            ResolveProgramOrdinaryTrickUseChoice(selected);
            return;
        }
        if (action == "order-bound-cards")
        {
            var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
                throw new InvalidOperationException("The bottom-order choice lost its program frame.");
            var paused = ProgramInstructionResolver.Default
                .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
                .GetPausedInstruction(frame.InstructionIndex);
            if (paused.Effect is not
                {
                    Op: SkillProgramEffectOp.MoveBoundCards,
                    Destination: SkillProgramCardDestination.DrawPileBottom
                } effect ||
                selected.Parameters.GetValueOrDefault("source-bind") != effect.SourceBind ||
                selected.Parameters.GetValueOrDefault("except-bind") != (effect.ExceptBind ?? string.Empty))
                throw new InvalidOperationException("The bottom-order choice does not match its suspended instruction.");
            ClearPendingDecision();
            _ = MoveProgramBoundCards(frame.Id, frame.OwnerSeat, effect.SourceBind!, effect.ExceptBind,
                SkillProgramCardDestination.DrawPileBottom, null,
                new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"), selected.Cards);
            ContinueProgramSkill(frame.Id);
            return;
        }
        if (action == "select-subset")
        {
            var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
                throw new InvalidOperationException("The subset choice lost its program frame.");
            var paused = ProgramInstructionResolver.Default
                .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
                .GetPausedInstruction(frame.InstructionIndex);
            if (paused.Effect is not
                { Op: SkillProgramEffectOp.SelectCardSubset } effect ||
                selected.Parameters.GetValueOrDefault("source-bind") != effect.SourceBind ||
                selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind)
                throw new InvalidOperationException("The subset choice does not match the suspended instruction.");
            var source = GetProgramCardSet(frame, effect.SourceBind!);
            var sourceIndexes = source.CardIds
                .Select((cardId, index) => (cardId, index))
                .ToDictionary(item => item.cardId, item => item.index);
            var cards = selected.Cards.Select(cardId =>
            {
                if (!sourceIndexes.TryGetValue(cardId, out var sourceIndex) ||
                    sourceIndex >= source.SourceLocations.Count)
                {
                    throw new InvalidOperationException("The selected program card lost its frozen source.");
                }
                return _cardZones.CardsAt(source.SourceLocations[sourceIndex])
                    .Single(card => card.Id == cardId);
            }).ToArray();
            var rankSum = cards.Sum(card => card.Rank);
            if (rankSum > effect.MaximumRankSum)
                throw new InvalidOperationException("The selected subset exceeds its rank-sum limit.");
            if (effect.OnePerSuit
                    ? cards.Length !=
                      source.CardIds.Select((cardId, index) =>
                              _cardZones.CardsAt(source.SourceLocations[index])
                                  .Single(card => card.Id == cardId).Suit)
                          .Distinct().Count() ||
                      cards.Select(card => card.Suit).Distinct().Count() != cards.Length
                    : selected.Cards.Distinct().Count() != selected.Cards.Count ||
                    selected.Cards.Any(cardId => !source.CardIds.Contains(cardId)) ||
                    selected.Cards.Count < (effect.AllowFewerWhenInsufficient
                        ? Math.Min(effect.MinimumCards, source.CardIds.Count) : effect.MinimumCards) ||
                    selected.Cards.Count > (effect.AllowFewerWhenInsufficient
                        ? Math.Min(effect.MaximumCards, source.CardIds.Count) : effect.MaximumCards))
                throw new InvalidOperationException("The selected subset is no longer legal.");
            ClearPendingDecision();
            SetProgramCardSet(frame.Id, effect.ResultBind!, selected.Cards,
                SkillProgramCardSetVisibility.Private);
            QueueGameEvent(new ProgramCardSubsetSelectedEvent(
                frame.Id, frame.SkillId, GetProgramBindingId(frame), effect.SourceBind!, effect.ResultBind!,
                Array.AsReadOnly(selected.Cards.ToArray()), rankSum));
            ContinueProgramSkill(frame.Id);
            return;
        }

        var (candidate, context) = GetPendingProgramTriggerCandidate();
        if (_resolutionStack.LastOrDefault() is ProgramLifecycleTriggerWindowFrame lifecycle &&
            GetProgramTrigger(candidate).ChoiceGroup is { } choiceGroup)
        {
            ResolveProgramTriggerGroupChoice(lifecycle, candidate, context, choiceGroup, selected, action);
            return;
        }
        if (_resolutionStack.LastOrDefault() is PlayPhaseStartingBoundaryFrame starting &&
            GetProgramTrigger(candidate).ChoiceGroup is { } startingGroup)
        {
            ResolvePlayPhaseStartingGroupChoice(starting, candidate, context, startingGroup, selected, action);
            return;
        }
        if (selected.Parameters.GetValueOrDefault("skill-id") != candidate.SkillId ||
            selected.Parameters.GetValueOrDefault("binding-id") != candidate.BindingId ||
            selected.Parameters.GetValueOrDefault("skill-instance-id") != candidate.SkillInstanceId)
            throw new InvalidOperationException("The program activation identity changed.");
        ClearPendingDecision();
        if (action == "activate")
        {
            if (!CanRunProgramTrigger(candidate, context))
            {
                CompleteSkippedProgramCandidate(candidate);
                return;
            }
            BeginProgramBinding(candidate, context);
            return;
        }
        if (action != "skip") throw new InvalidOperationException("Unsupported program trigger choice.");
        CompleteSkippedProgramCandidate(candidate);
    }

    private void ResolveProgramTriggerGroupChoice(
        ProgramLifecycleTriggerWindowFrame frame,
        ProgramTriggerCandidate first,
        ProgramSkillWindowContext context,
        string choiceGroup,
        PromptChoice selected,
        string? action)
    {
        var members = GetProgramTriggerChoiceGroup(frame, first, choiceGroup);
        if (selected.Parameters.GetValueOrDefault("choice-group") != choiceGroup ||
            selected.Cards.Count != 0 || selected.Targets.Count != 0)
            throw new InvalidOperationException("The program choice group identity changed.");
        ClearPendingDecision();
        if (action == "skip")
        {
            if (!GetProgramTrigger(first).Optional)
                throw new InvalidOperationException("A mandatory program choice group cannot be skipped.");
            foreach (var member in members)
                QueueGameEvent(new ProgramBindingResolvedEvent(
                    frame.Id, member.SkillId, member.BindingId, member.SkillInstanceId,
                    member.OwnerSeat, frame.Window, Activated: false, Completed: false));
            _resolutionStack[^1] = frame with { CandidateIndex = frame.CandidateIndex + members.Count };
            ContinueProgramLifecycleWindow();
            return;
        }
        if (action != "activate")
            throw new InvalidOperationException("Unsupported program choice-group action.");
        var selectedCandidate = members.SingleOrDefault(member =>
            selected.Parameters.GetValueOrDefault("skill-id") == member.SkillId &&
            selected.Parameters.GetValueOrDefault("binding-id") == member.BindingId &&
            selected.Parameters.GetValueOrDefault("skill-instance-id") == member.SkillInstanceId) ??
            throw new InvalidOperationException("The selected program branch is not in the pending choice group.");
        foreach (var member in members.Where(member => member != selectedCandidate))
            QueueGameEvent(new ProgramBindingResolvedEvent(
                frame.Id, member.SkillId, member.BindingId, member.SkillInstanceId,
                member.OwnerSeat, frame.Window, Activated: false, Completed: false));
        var selectedContext = context with
        {
            OccurrenceIndex = selectedCandidate.OccurrenceIndex,
            ResumeCandidateIndex = frame.CandidateIndex + members.Count
        };
        if (!CanRunProgramTrigger(selectedCandidate, selectedContext))
        {
            QueueGameEvent(new ProgramBindingResolvedEvent(
                frame.Id, selectedCandidate.SkillId, selectedCandidate.BindingId,
                selectedCandidate.SkillInstanceId, selectedCandidate.OwnerSeat, frame.Window,
                Activated: false, Completed: false));
            _resolutionStack[^1] = frame with { CandidateIndex = frame.CandidateIndex + members.Count };
            ContinueProgramLifecycleWindow();
            return;
        }
        BeginProgramBinding(selectedCandidate, selectedContext);
    }

    private void ResolvePlayPhaseStartingGroupChoice(
        PlayPhaseStartingBoundaryFrame frame,
        ProgramTriggerCandidate first,
        ProgramSkillWindowContext context,
        string choiceGroup,
        PromptChoice selected,
        string? action)
    {
        var members = GetPlayPhaseStartingChoiceGroup(frame, first, choiceGroup);
        if (selected.Parameters.GetValueOrDefault("choice-group") != choiceGroup ||
            selected.Cards.Count != 0 || selected.Targets.Count != 0)
            throw new InvalidOperationException("The program choice group identity changed.");
        ClearPendingDecision();
        if (action == "skip")
        {
            if (!GetProgramTrigger(first).Optional)
                throw new InvalidOperationException("A mandatory program choice group cannot be skipped.");
            foreach (var member in members)
                QueueGameEvent(new ProgramBindingResolvedEvent(
                    frame.Id, member.SkillId, member.BindingId, member.SkillInstanceId,
                    member.OwnerSeat, SkillProgramTriggerWindow.PlayPhaseStarting, Activated: false, Completed: false));
            _resolutionStack[^1] = frame with { ItemIndex = frame.ItemIndex + members.Count };
            ContinuePlayPhaseStartingBoundary();
            return;
        }
        if (action != "activate")
            throw new InvalidOperationException("Unsupported program choice-group action.");
        var selectedCandidate = members.SingleOrDefault(member =>
            selected.Parameters.GetValueOrDefault("skill-id") == member.SkillId &&
            selected.Parameters.GetValueOrDefault("binding-id") == member.BindingId &&
            selected.Parameters.GetValueOrDefault("skill-instance-id") == member.SkillInstanceId) ??
            throw new InvalidOperationException("The selected program branch is not in the pending choice group.");
        foreach (var member in members.Where(member => member != selectedCandidate))
            QueueGameEvent(new ProgramBindingResolvedEvent(
                frame.Id, member.SkillId, member.BindingId, member.SkillInstanceId,
                member.OwnerSeat, SkillProgramTriggerWindow.PlayPhaseStarting, Activated: false, Completed: false));
        var selectedContext = context with
        {
            OccurrenceIndex = selectedCandidate.OccurrenceIndex,
            ResumeCandidateIndex = frame.ItemIndex + members.Count
        };
        if (!CanRunProgramTrigger(selectedCandidate, selectedContext))
        {
            QueueGameEvent(new ProgramBindingResolvedEvent(
                frame.Id, selectedCandidate.SkillId, selectedCandidate.BindingId,
                selectedCandidate.SkillInstanceId, selectedCandidate.OwnerSeat,
                SkillProgramTriggerWindow.PlayPhaseStarting, Activated: false, Completed: false));
            _resolutionStack[^1] = frame with { ItemIndex = frame.ItemIndex + members.Count };
            ContinuePlayPhaseStartingBoundary();
            return;
        }
        BeginProgramBinding(selectedCandidate, selectedContext);
    }

    private void ResolveProgramInstructionChoice(PromptChoice selected, string action)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The program instruction choice lost its frame.");
        if (selected.Parameters.GetValueOrDefault("frame-id") !=
            frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            frame.InstructionIndex == 0)
            throw new InvalidOperationException("The program instruction choice has a stale frame identity.");
        var effect = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;

        switch (action)
        {
            case "select-target":
                {
                    if (effect is not
                        {
                            Op: SkillProgramEffectOp.SelectTarget,
                            TargetKind: { } targetKind
                        } || selected.Parameters.GetValueOrDefault("target-kind") != targetKind.ToString() ||
                        selected.Parameters.GetValueOrDefault("marker") != (effect.Marker?.ToString() ?? "") ||
                        selected.Targets.Count != 1 || selected.Cards.Count != 0)
                        throw new InvalidOperationException("The selected target does not match the suspended instruction.");
                    var targetSeat = selected.Targets.Single();
                    if (!IsProgramTargetEligible(frame.OwnerSeat, targetKind, effect.Zones, targetSeat,
                        effect.Marker, frame.WindowContext, effect.ActorReference))
                        throw new InvalidOperationException("The selected program target is no longer legal.");
                    ClearPendingDecision();
                    _resolutionStack[^1] = frame with
                    {
                        SelectedTargetSeats = Array.AsReadOnly(new[] { targetSeat })
                    };
                    if (frame.WindowContext?.Judgment is { } judgment)
                        QueueGameEvent(new ProgramJudgmentTargetSelectedEvent(
                            frame.WindowContext.ParentFrameId, judgment.JudgmentFrameId,
                            frame.SkillId, frame.TriggerId!, frame.OwnerSeat, targetSeat));
                    ContinueProgramSkill(frame.Id);
                    return;
                }
            case "select-targets":
                {
                    if (effect is not
                        {
                            Op: SkillProgramEffectOp.SelectTargets,
                            TargetKind: { } targetKind,
                            TargetAiOrder: { } targetAiOrder
                        } || selected.Parameters.GetValueOrDefault("target-kind") != targetKind.ToString() ||
                        selected.Parameters.GetValueOrDefault("target-ai-order") != targetAiOrder.ToString() ||
                        selected.Cards.Count != 0 ||
                        selected.Targets.Count < effect.MinimumTargets ||
                        selected.Targets.Count > effect.MaximumTargets ||
                        (effect.NumberExpression == SkillProgramNumberExpression.CurrentHandCount &&
                         selected.Targets.Count > GetHand(_players[frame.OwnerSeat]).Count) ||
                        selected.Targets.Distinct().Count() != selected.Targets.Count)
                        throw new InvalidOperationException("The selected targets do not match the suspended instruction.");
                    var legalTargets = GetProgramTargetSeats(frame.OwnerSeat, targetKind, frame.WindowContext);
                    if (selected.Targets.Any(targetSeat => !legalTargets.Contains(targetSeat)))
                        throw new InvalidOperationException("A selected program target is no longer legal.");
                    if (targetKind == SkillProgramTargetKind.OtherLivingUnequalHandPair &&
                        (selected.Targets.Count != 2 ||
                         GetHand(_players[selected.Targets[0]]).Count >= GetHand(_players[selected.Targets[1]]).Count))
                        throw new InvalidOperationException("The selected unequal-hand pair is no longer legal.");
                    if (targetKind == SkillProgramTargetKind.OtherLivingPair &&
                        (selected.Targets.Count != 2 ||
                         GetHand(_players[selected.Targets[0]]).Count > GetHand(_players[selected.Targets[1]]).Count))
                        throw new InvalidOperationException("The selected pair must be two players in ascending hand order.");
                    if (targetKind == SkillProgramTargetKind.LivingPairDistinct &&
                        (selected.Targets.Count != 2 ||
                         selected.Targets[0] == selected.Targets[1]))
                        throw new InvalidOperationException("The selected ordered pair must name two distinct players.");
                    if (targetKind == SkillProgramTargetKind.OtherLivingRangeOrderedPair &&
                        (selected.Targets.Count != 2 ||
                         selected.Targets[0] == selected.Targets[1] ||
                         GetCombatDistance(selected.Targets[0], selected.Targets[1]) >
                             GetAttackRange(selected.Targets[0])))
                        throw new InvalidOperationException(
                            "The selected range pair is no longer legal.");
                    ClearPendingDecision();
                    _resolutionStack[^1] = frame with
                    {
                        SelectedTargetSeats = Array.AsReadOnly(selected.Targets.ToArray())
                    };
                    ContinueProgramSkill(frame.Id);
                    return;
                }
            case "reveal-target-hand-card":
                {
                    ResolveProgramRevealCardSelection(selected);
                    return;
                }
            case "reveal-target-hand-card-decline":
                {
                    ResolveProgramRevealCardSelection(selected);
                    return;
                }
            case "select-source-card":
                {
                    if (effect.Op != SkillProgramEffectOp.SelectSourceCard ||
                        selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
                        !Enum.TryParse<CardZoneKind>(
                            selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
                        !effect.Zones.Contains(zone) ||
                        (effect.CardSource switch
                        {
                            SkillProgramCardSource.Owner => frame.OwnerSeat,
                            SkillProgramCardSource.DamageSource => frame.WindowContext?.SourceSeat,
                            SkillProgramCardSource.EventTarget => frame.WindowContext?.TargetSeat,
                            _ => null
                        }) is not { } sourceSeat ||
                        effect.CardSource == SkillProgramCardSource.DamageSource && sourceSeat == frame.OwnerSeat &&
                        !effect.AllowSameSource)
                        throw new InvalidOperationException("The source-card choice does not match the suspended instruction.");

                    Card card;
                    CardLocation location;
                    if (zone == CardZoneKind.Hand)
                    {
                        if (selected.Cards.Count != 0 ||
                            !int.TryParse(
                                selected.Parameters.GetValueOrDefault("slot-index"),
                                System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var slot) ||
                            slot < 0 || slot >= GetHand(_players[sourceSeat]).Count)
                            throw new InvalidOperationException("The selected opaque hand slot is no longer legal.");
                        card = GetHand(_players[sourceSeat])[slot];
                        location = CardLocation.Hand(sourceSeat);
                    }
                    else if (zone == CardZoneKind.Equipment)
                    {
                        if (selected.Cards.Count != 1)
                            throw new InvalidOperationException("The equipment selection is malformed.");
                        location = CardLocation.Equipment(sourceSeat);
                        card = GetEquipment(_players[sourceSeat])
                            .SingleOrDefault(item => item.Id == selected.Cards.Single()) ??
                            throw new InvalidOperationException("The selected equipment is no longer available.");
                        if (effect.EquipmentSlots.Count > 0 &&
                            !effect.EquipmentSlots.Contains(EquipmentCatalog.Get(card.Kind).Slot))
                            throw new InvalidOperationException("The selected equipment slot is no longer legal.");
                    }
                    else if (effect.CardSource == SkillProgramCardSource.Owner && zone is
                        CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or
                        CardZoneKind.Authority or CardZoneKind.Chunlao)
                    {
                        if (selected.Cards.Count != 1)
                            throw new InvalidOperationException("The owner-pile selection is malformed.");
                        location = new CardLocation(zone, sourceSeat);
                        card = _cardZones.CardsAt(location)
                            .SingleOrDefault(item => item.Id == selected.Cards.Single()) ??
                            throw new InvalidOperationException("The selected owner-pile card is no longer available.");
                    }
                    else
                    {
                        throw new InvalidOperationException("The configured source-card zone is unsupported.");
                    }

                    ClearPendingDecision();
                    SetProgramCardSet(
                        frame.Id,
                        effect.ResultBind!,
                        [card.Id],
                        SkillProgramCardSetVisibility.Private,
                        [location]);
                    ContinueProgramSkill(frame.Id);
                    return;
                }
            case "select-owned-cards":
            case "finish-owned-cards":
                if (effect.Op == SkillProgramEffectOp.HoldTargetCards)
                {
                    if (selected.Parameters.GetValueOrDefault("frame-id") !=
                        frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                        frame.InstructionIndex == 0)
                        throw new InvalidOperationException("The card-hold choice has a stale frame identity.");
                    ResolveProgramHoldCardSelection(frame, selected);
                    return;
                }
                ResolveProgramOwnedCardSelection(frame, effect, selected);
                return;
            case "distribute-owned-card":
            case "decline-owned-card-distribution":
                ResolveProgramOwnedCardDistribution(frame, effect, selected);
                return;
            case "attack-range-aid-discard-weapon":
            case "attack-range-aid-draw":
                ResolveProgramAttackRangeAid(frame, effect, selected);
                return;
            case "select-and-move-owned-card":
                {
                    if (effect.Op != SkillProgramEffectOp.SelectAndMoveOwnedCard)
                        throw new InvalidOperationException("The payment choice does not match the suspended instruction.");
                    ResolveSelectAndMoveOwnedCardChoice(frame, effect, selected);
                    return;
                }
            case "choose-other-owned-card-discard":
            case "choose-other-owned-card-decline":
                ResolveProgramOtherOwnedCardDiscardChoice(selected);
                return;
            case "restore-phase-hand-discard":
            case "restore-phase-hand-decline":
                ResolveProgramPhaseHandDiscardChoice(selected);
                return;
            case "request-slash":
            case "request-slash-decline":
                ResolveProgramRequestSlashChoice(selected);
                return;
            case "choose-own-card-discard":
                ResolveProgramOwnCardDiscardChoice(selected);
                return;
            case "give-bound-card":
            case "keep-bound-cards":
                {
                    if (effect is not
                        {
                            Op: SkillProgramEffectOp.GiveBoundCard,
                            SourceBind: { } sourceBind,
                            TargetKind: { } targetKind
                        } || selected.Parameters.GetValueOrDefault("source-bind") != sourceBind ||
                        selected.Parameters.GetValueOrDefault("target-kind") != targetKind.ToString())
                        throw new InvalidOperationException("The bound-card choice does not match the suspended instruction.");
                    var binding = GetProgramCardSet(frame, sourceBind);
                    if (action == "keep-bound-cards")
                    {
                        if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                            throw new InvalidOperationException("The retain-cards choice is malformed.");
                        ClearPendingDecision();
                        ContinueProgramSkill(frame.Id);
                        return;
                    }

                    if (selected.Cards.Count != 1 || selected.Targets.Count != 1)
                        throw new InvalidOperationException("The bound-card gift choice is malformed.");
                    var cardId = selected.Cards.Single();
                    var targetSeat = selected.Targets.Single();
                    var indexes = binding.CardIds
                        .Select((id, position) => (id, position))
                        .ToDictionary(item => item.id, item => item.position);
                    if (!indexes.TryGetValue(cardId, out var index) ||
                        index >= binding.SourceLocations.Count ||
                        !GetProgramTargetSeats(frame.OwnerSeat, targetKind).Contains(targetSeat))
                        throw new InvalidOperationException("The bound-card gift is no longer legal.");
                    var from = binding.SourceLocations[index];
                    if (_cardZones.GetLocation(cardId) != from)
                        throw new InvalidOperationException("The bound card left its frozen source before the gift.");
                    var card = _cardZones.CardsAt(from).Single(item => item.Id == cardId);
                    ClearPendingDecision();
                    MoveCard(
                        card,
                        from,
                        CardLocation.Hand(targetSeat),
                        new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
                    QueueGameEvent(new ProgramBoundCardGivenEvent(
                        frame.Id,
                        frame.SkillId,
                        GetProgramBindingId(frame),
                        frame.OwnerSeat,
                        targetSeat,
                        cardId));
                    ContinueProgramSkill(frame.Id);
                    return;
                }
            default:
                throw new InvalidOperationException("Unsupported program instruction choice.");
        }
    }

    private (ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)
        GetPendingProgramTriggerCandidate()
    {
        if (_resolutionStack.LastOrDefault() is TurnEndingBoundaryFrame turnEnding)
        {
            var candidate = turnEnding.Items[turnEnding.ItemIndex].Candidate ??
                throw new InvalidOperationException("The turn-ending program item lost its candidate.");
            return (candidate, CreateTurnEndingProgramContext(turnEnding, candidate,
                turnEnding.Items[turnEnding.ItemIndex].Facts));
        }
        if (_resolutionStack.LastOrDefault() is PlayPhaseStartingBoundaryFrame playStarting)
        {
            var candidate = playStarting.Items[playStarting.ItemIndex].Candidate ??
                throw new InvalidOperationException("The play-phase-starting item lost its candidate.");
            return (candidate, CreatePlayPhaseStartingProgramContext(playStarting, candidate,
                playStarting.Items[playStarting.ItemIndex].Facts));
        }
        if (_resolutionStack.LastOrDefault() is ProgramLifecycleTriggerWindowFrame lifecycle)
        {
            var candidate = lifecycle.Candidates[lifecycle.CandidateIndex];
            return (candidate, new ProgramSkillWindowContext(
                lifecycle.Window, lifecycle.Id, candidate.OwnerSeat,
                SourceSeat: lifecycle.OwnerSeat, TargetSeat: lifecycle.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: lifecycle.ParticipantFacts?.GetValueOrDefault(candidate.OwnerSeat) ?? lifecycle.Facts));
        }
        if (_resolutionStack.LastOrDefault() is HpChangedTriggerWindowFrame hpChanged)
            return (hpChanged.Candidates[hpChanged.CandidateIndex], hpChanged.Contexts[hpChanged.CandidateIndex]);
        if (_resolutionStack.LastOrDefault() is CardsMovedTriggerWindowFrame cardsMoved)
        {
            var candidate = cardsMoved.Candidates[cardsMoved.CandidateIndex];
            return (candidate, CreateCardsMovedProgramContext(cardsMoved, candidate));
        }
        if (_resolutionStack.LastOrDefault() is ProgramDeathTriggerWindowFrame deathWindow)
        {
            var candidate = deathWindow.Candidates[deathWindow.CandidateIndex];
            return (candidate, CreateOwnerDiedProgramContext(deathWindow, candidate));
        }
        if (_resolutionStack.LastOrDefault() is ProgramKillTriggerWindowFrame killWindow)
            return (killWindow.Candidates[killWindow.CandidateIndex],
                killWindow.Contexts[killWindow.CandidateIndex]);
        if (_resolutionStack.LastOrDefault() is BeforeDamageProgramWindowFrame beforeDamage)
        {
            var item = beforeDamage.Candidates[beforeDamage.CandidateIndex];
            return (item.Candidate, CreateBeforeDamageProgramContext(beforeDamage, item));
        }
        if (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame cardAction &&
            cardAction.Candidates[cardAction.CandidateIndex] is { } cardCandidate)
        {
            return (ToSharedCandidate(cardCandidate), CreateCardActionProgramContext(cardAction, cardCandidate));
        }
        if (_pendingDamageTrigger is { } damage &&
            damage.Candidates[damage.CandidateIndex].ToProgramCandidate() is { } damageCandidate)
            return (damageCandidate, CreateAfterDamageProgramContext(damage, damageCandidate));
        throw new InvalidOperationException("The program prompt has no parent trigger candidate.");
    }

    private void CompleteSkippedProgramCandidate(ProgramTriggerCandidate candidate)
    {
        if (_resolutionStack.LastOrDefault() is ProgramKillTriggerWindowFrame killWindow &&
            killWindow.Candidates[killWindow.CandidateIndex] == candidate)
        {
            AdvanceKillDiedProgramCandidate(killWindow, candidate, activated: false, completed: false);
            ContinueKillDiedProgramWindow();
            return;
        }
        if (_resolutionStack.LastOrDefault() is TurnEndingBoundaryFrame turnEnding &&
            turnEnding.Items[turnEnding.ItemIndex].Candidate == candidate)
        {
            AdvanceTurnEndingBoundaryCandidate(
                turnEnding, candidate, activated: false, completed: false);
            ContinueTurnEndingBoundary();
            return;
        }
        if (_resolutionStack.LastOrDefault() is PlayPhaseStartingBoundaryFrame playStarting &&
            playStarting.Items[playStarting.ItemIndex].Candidate == candidate)
        {
            AdvancePlayPhaseStartingCandidate(
                playStarting, candidate, activated: false, completed: false);
            ContinuePlayPhaseStartingBoundary();
            return;
        }
        if (_resolutionStack.LastOrDefault() is ProgramLifecycleTriggerWindowFrame lifecycle)
        {
            AdvanceProgramLifecycleCandidate(lifecycle, activated: false, completed: false);
            ContinueProgramLifecycleWindow();
            return;
        }
        if (_resolutionStack.LastOrDefault() is HpChangedTriggerWindowFrame hpChanged &&
            hpChanged.Candidates[hpChanged.CandidateIndex] == candidate)
        {
            AdvanceHpChangedProgramCandidate(hpChanged, activated: false, completed: false);
            ContinueHpChangedProgramWindow();
            return;
        }
        if (_resolutionStack.LastOrDefault() is CardsMovedTriggerWindowFrame cardsMoved &&
            cardsMoved.Candidates[cardsMoved.CandidateIndex] == candidate)
        {
            AdvanceCardsMovedProgramCandidate(
                cardsMoved, candidate, activated: false, completed: false);
            ContinueCardsMovedProgramWindow();
            return;
        }
        if (_resolutionStack.LastOrDefault() is ProgramDeathTriggerWindowFrame deathWindow &&
            deathWindow.Candidates[deathWindow.CandidateIndex] == candidate)
        {
            AdvanceOwnerDiedProgramCandidate(deathWindow, candidate, activated: false, completed: false);
            ContinueOwnerDiedProgramWindow();
            return;
        }
        if (_resolutionStack.LastOrDefault() is BeforeDamageProgramWindowFrame beforeDamage &&
            beforeDamage.Candidates[beforeDamage.CandidateIndex].Candidate == candidate)
        {
            AdvanceBeforeDamageProgramCandidate(beforeDamage, activated: false, completed: false);
            ContinueBeforeDamageProgramWindow();
            return;
        }
        if (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame cardAction &&
            cardAction.Candidates[cardAction.CandidateIndex] is { } cardCandidate &&
            ToSharedCandidate(cardCandidate) == candidate)
        {
            AdvanceProgramCardCandidate(cardAction);
            ContinueProgramCardWindow();
            return;
        }
        if (_pendingDamageTrigger is { } damage &&
            damage.Candidates[damage.CandidateIndex].ToProgramCandidate() == candidate)
        {
            QueueGameEvent(new ProgramBindingResolvedEvent(
                damage.FrameId, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
                candidate.OwnerSeat, damage.Window,
                Activated: false, Completed: false));
            AdvanceDamageTriggerCandidate(damage);
            return;
        }
        throw new InvalidOperationException("The skipped program candidate lost its parent.");
    }

    private bool IsAiProgramTriggerPending() =>
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: var seat } &&
        !_players[seat].IsHuman;

    private void ResolvePendingAiProgramTrigger()
    {
        var decision = _pendingDecision is { Kind: DecisionKind.ProgramTrigger } current
            ? current
            : throw new InvalidOperationException("The AI program prompt is unavailable.");
        PromptChoice selected;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame frame)
        {
            var paused = ProgramInstructionResolver.Default
                .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
                .GetPausedInstruction(frame.InstructionIndex).Effect;
            selected = paused.Op switch
            {
                SkillProgramEffectOp.ChooseOption => SelectAiProgramOption(decision, frame),
                SkillProgramEffectOp.ChooseDifferentCategoryDiscard =>
                    SelectAiProgramCategoryDiscard(decision, frame),
                SkillProgramEffectOp.ChooseOtherOwnedCardDiscard =>
                    SelectAiProgramOtherOwnedCardDiscard(decision, frame),
                SkillProgramEffectOp.RestorePhaseHandDiscards =>
                    SelectAiProgramPhaseHandDiscardRestore(decision, frame),
                SkillProgramEffectOp.ChooseOwnCardDiscard =>
                    SelectAiProgramOwnCardDiscard(decision, frame),
                SkillProgramEffectOp.SelectOwnedCards => SelectAiProgramOwnedCards(decision, frame),
                SkillProgramEffectOp.HoldTargetCards => SelectAiProgramHoldCards(decision, frame),
                SkillProgramEffectOp.RequestSlashByTarget => SelectAiProgramRequestSlash(decision, frame),
                SkillProgramEffectOp.RevealTargetHandCard => decision.Choices
                    .OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First(),
                SkillProgramEffectOp.DistributeOwnedCards =>
                    SelectAiProgramOwnedCardDistribution(decision, frame),
                SkillProgramEffectOp.RequestAttackRangeAid =>
                    SelectAiProgramAttackRangeAid(decision, frame),
                SkillProgramEffectOp.SelectCardSubset
                    when paused.AiOrder == SkillProgramSubsetAiOrder.MostCardsThenRankSum =>
                    decision.Choices
                        .OrderByDescending(choice => choice.Cards.Count)
                        .ThenByDescending(choice => int.Parse(
                            choice.Parameters.GetValueOrDefault("rank-sum", "0"),
                            System.Globalization.CultureInfo.InvariantCulture))
                        .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
                        .First(),
                SkillProgramEffectOp.MoveBoundCards
                    when paused.Destination == SkillProgramCardDestination.DrawPileBottom =>
                    decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First(),
                SkillProgramEffectOp.SelectTargets when paused.TargetAiOrder is { } targetAiOrder =>
                    SelectAiProgramTargets(decision, targetAiOrder),
                SkillProgramEffectOp.SelectTarget =>
                    SelectAiCompositionTarget(decision, frame),
                SkillProgramEffectOp.SelectAndMoveOwnedCard when paused.CoverageResultBind is not null =>
                    decision.Choices.OrderByDescending(choice => choice.Cards.Count == 1 &&
                        WouldEquipmentRemovalReduceCoverage(
                            ResolveProgramParticipant(frame, paused.CardOwnerRef!), choice.Cards[0]))
                        .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First(),
                SkillProgramEffectOp.SelectSourceCard or SkillProgramEffectOp.SelectAndMoveOwnedCard or
                    SkillProgramEffectOp.GiveBoundCard => decision.Choices[0],
                SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick => decision.Choices
                    .OrderBy(choice => choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo) ? 0 : 1)
                    .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
                    .First(),
                _ => throw new InvalidOperationException(
                    $"The AI does not support suspended program instruction '{paused.Op}'.")
            };
        }
        else
        {
            selected = SelectAiProgramActivation(decision);
        }
        ResolveProgramTriggerChoice(selected);
        PublishState();
    }

    private PromptChoice SelectAiProgramActivation(PendingDecision decision)
    {
        var activateChoices = decision.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate").ToArray();
        var skip = decision.Choices.SingleOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip");
        if (activateChoices.Length > 1)
            return SelectAiProgramChoiceGroup(decision, activateChoices, skip);
        var activate = activateChoices.Single();
        if (skip is null) return activate;

        var skillId = activate.Parameters["skill-id"];
        var bindingId = activate.Parameters["binding-id"];
        var skillInstanceId = activate.Parameters["skill-instance-id"];
        var skill = _contentRegistry!.GetSkill(skillId);
        var trigger = skill.Program!.Triggers.Single(item => item.Id == bindingId);
        var owner = _players[decision.PlayerSeat];
        var estimate = EstimateCompositionForAi(owner, trigger.Effects,
            WithProgramConditionFacts(CreateProgramAiPublicContext(trigger, owner, decision.PlayerSeat),
                owner, skillId, skillInstanceId),
            windowContext: GetPendingProgramTriggerCandidate().Context).Estimate;
        var shouldActivate = !estimate.IsSelfLethal && estimate.Score > 0d;
        var activateAction = new LegalAction(
            LegalActionKind.UseProgramSkill, null, null, $"发动【{skill.Name}】");
        var skipAction = new LegalAction(
            LegalActionKind.UseProgramSkill, null, null, $"跳过【{skill.Name}】");
        var candidates = new[]
        {
            new AiCandidateScore(activateAction,
                shouldActivate ? estimate.Score : Math.Min(estimate.Score, -1000d),
                estimate.IsSelfLethal ? "公开效果将令自身失去全部体力。" : estimate.Approximation),
            new AiCandidateScore(skipAction, 0d, "保留当前状态。")
        };
        AddThought(new AiThoughtRecord(
            _thoughtSequence++, _turnNumber, decision.PlayerSeat,
            shouldActivate ? activateAction.Description : skipAction.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"组合估值 {estimate.Score:0.##}；{estimate.Approximation}"));
        return shouldActivate ? activate : skip;
    }

    private PromptChoice SelectAiProgramChoiceGroup(
        PendingDecision decision,
        IReadOnlyList<PromptChoice> activateChoices,
        PromptChoice? skip)
    {
        var parentOwnerSeat = _resolutionStack.LastOrDefault() switch
        {
            ProgramLifecycleTriggerWindowFrame lifecycle => lifecycle.Candidates[lifecycle.CandidateIndex].OwnerSeat,
            PlayPhaseStartingBoundaryFrame starting when starting.Items
                .Skip(starting.ItemIndex)
                .Any(item => item.Candidate?.OwnerSeat == decision.PlayerSeat) => decision.PlayerSeat,
            _ => -1
        };
        if (parentOwnerSeat != decision.PlayerSeat)
            throw new InvalidOperationException("A program choice group lost its lifecycle parent.");
        var owner = _players[decision.PlayerSeat];
        var estimated = activateChoices.Select(choice =>
        {
            var skill = _contentRegistry!.GetSkill(choice.Parameters["skill-id"]);
            var trigger = skill.Program!.Triggers.Single(item => item.Id == choice.Parameters["binding-id"]);
            var estimate = EstimateCompositionForAi(owner, trigger.Effects,
                WithProgramConditionFacts(CreateProgramAiPublicContext(trigger, owner, decision.PlayerSeat),
                    owner, skill.Id, choice.Parameters["skill-instance-id"]),
                windowContext: GetPendingProgramTriggerCandidate().Context).Estimate;
            var action = new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                $"发动【{skill.Name}】");
            return (Choice: choice, Action: action, Estimate: estimate);
        }).OrderByDescending(item => item.Estimate.IsSelfLethal ? double.NegativeInfinity : item.Estimate.Score)
            .ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal).ToArray();
        var best = estimated[0];
        var activate = skip is null || !best.Estimate.IsSelfLethal && best.Estimate.Score > 0d;
        var skipAction = new LegalAction(LegalActionKind.UseProgramSkill, null, null, "跳过组合技能分支");
        var candidates = estimated.Select(item => new AiCandidateScore(
            item.Action, item.Estimate.IsSelfLethal ? -1000d : item.Estimate.Score,
            item.Estimate.IsSelfLethal ? "公开效果将令自身失去全部体力。" : item.Estimate.Approximation));
        if (skip is not null)
            candidates = candidates.Append(new AiCandidateScore(skipAction, 0d, "保留当前状态。"));
        AddThought(new AiThoughtRecord(
            _thoughtSequence++, _turnNumber, decision.PlayerSeat,
            activate ? best.Action.Description : skipAction.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            "互斥组使用同一操作目录与公开上下文估值。"));
        return activate ? best.Choice : skip!;
    }

    private ProgramAiPublicContext CreateProgramAiPublicContext(
        SkillProgramTrigger trigger,
        CharacterState owner,
        int ownerSeat)
    {
        if (trigger.Window is SkillProgramTriggerWindow.CardUseCommitted or
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
            SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted or
            SkillProgramTriggerWindow.CardUseCompleted or
            SkillProgramTriggerWindow.SlashTargetRedirecting or
            SkillProgramTriggerWindow.SlashBeforeResponse or
            SkillProgramTriggerWindow.SlashFullyDodged)
        {
            var parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() ??
                throw new InvalidOperationException("A card-action estimate lost its frozen parent action.");
            var suits = parent.Action.PhysicalCards.Select(cost =>
            {
                var location = _cardZones.GetLocation(cost.CardId);
                return _cardZones.CardsAt(location).Single(card => card.Id == cost.CardId).Suit;
            }).ToArray();
            return CreateProgramAiPublicContext(owner) with
            {
                CardUseIsRed = suits.Length == 0 ? null : suits.All(suit => suit is Suit.Heart or Suit.Diamond),
                Actor = CreateSkillContext(_players[parent.Action.ActorSeat]),
                CardActionActorIsOwner = parent.Action.ActorSeat == ownerSeat,
                CardUseEffectiveKind = parent.Action.EffectiveKind,
                CardEffectInterventionScore = parent.Action.Type == CardActionType.Use &&
                    trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.NullifySelectedCardEffects)
                    ? parent.Action.EffectiveDesignatedTargetSeats.Distinct()
                        .Where(seat => _players[seat].IsAlive &&
                            _resolutionStack.OfType<CardUseFrame>()
                                .Single(item => item.Id == parent.ParentFrameId)
                                .IneffectiveTargetSeats?.Contains(seat) != true)
                        .Sum(seat => Math.Max(0d, _aiBrains[ownerSeat].ScoreCardEffectIntervention(
                            CreateSnapshot(ownerSeat), parent.Action.EffectiveKind, seat)))
                    : 0d,
                CardUseDebitActive = IsCardUseDebitActive(parent.Action.ActionId)
            };
        }
        if (trigger.Window != SkillProgramTriggerWindow.DrawPhaseStarting)
            return CreateProgramAiPublicContext(owner);
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
            drawPhase.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
            drawPhase.OwnerSeat != ownerSeat)
            throw new InvalidOperationException("A draw-phase estimate lost its lifecycle parent.");
        return CreateProgramAiPublicContext(owner) with
        {
            NormalDrawCount = checked((drawPhase.FrozenBaseDrawCount ?? GetTurnDrawCount(owner)) +
                drawPhase.NormalDrawAdjustment),
            EligibleTargetCount = trigger.Effects.FirstOrDefault(effect =>
                effect.Op == SkillProgramEffectOp.SelectTargets)?.TargetKind is { } targetKind
                ? GetProgramTargetSeats(ownerSeat, targetKind).Count : 0,
            ReplacesNormalDraw = trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement
        };
    }

    private ProgramAiPublicContext CreateProgramAiPublicContext(CharacterState owner) =>
        new(GetLivingFactionCount(), CanUseSlashOnOther:
            !HasTurnCardTargetRestriction(owner.Seat, SkillProgramCardTargetRestriction.SelfOnly) &&
            new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }
                .Any(kind => !IsCardUseForbidden(owner.Seat, kind, CardActionType.Use)),
            AttackRange: GetAttackRange(owner.Seat),
            AttackRangeCoverageDecreased: _ => _players.Where(player => player.IsAlive && player.Seat != owner.Seat)
                .Any(player => GetEquipment(player).Any(card =>
                    WouldEquipmentRemovalReduceCoverage(player.Seat, card.Id))),
            LivingPlayersMinHp: GetLivingPlayersMinHp(),
            TurnOwnerDiscardPhaseHandDiscardCount: TurnOwnerDiscardPhaseHandDiscardCount);

    private ProgramAiPublicContext WithProgramConditionFacts(ProgramAiPublicContext context,
        CharacterState owner, string skillId, string skillInstanceId)
    {
        var cardFrame = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault();
        var cardFacts = cardFrame is not null && cardFrame.CandidateIndex < cardFrame.Candidates.Count
            ? cardFrame.Candidates[cardFrame.CandidateIndex].FrozenContext?.Facts
            : null;
        var ending = _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault();
        var endingFacts = ending is not null && ending.ItemIndex < ending.Items.Count
            ? ending.Items[ending.ItemIndex].Facts ?? ending.Facts : null;
        var facts = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault()?.Facts ??
            endingFacts ?? cardFacts;
        return context with
        {
            BooleanState = stateId => facts is null
                ? GetProgramBooleanState(owner.Seat, skillId, skillInstanceId, stateId)
                : facts.GetBooleanState(skillId, skillInstanceId, stateId)
        };
    }

    private (ProgramAiEstimate Estimate, int? TargetSeat) EstimateCompositionForAi(
        CharacterState owner, IEnumerable<SkillProgramEffect> sourceEffects,
        ProgramAiPublicContext publicContext, IReadOnlyList<int>? publishedTargets = null,
        ProgramSkillWindowContext? windowContext = null)
    {
        var effects = sourceEffects.ToArray();
        var selection = effects.FirstOrDefault(effect => effect.Op == SkillProgramEffectOp.SelectTarget);
        var targets = publishedTargets ?? (selection?.TargetKind is { } kind
            ? GetProgramTargetSeats(owner.Seat, kind, windowContext, selection.Marker) : Array.Empty<int>());
        var context = CreateSkillContext(owner);
        if (targets.Count == 0)
            return (ProgramCompositionAi.Estimate(effects, context, owner.IsFaceDown, publicContext), null);
        var snapshot = CreateSnapshot(owner.Seat);
        return targets.Select(targetSeat =>
            {
                var estimate = ProgramCompositionAi.Estimate(effects, context, owner.IsFaceDown,
                    publicContext with
                    {
                        SelectedTarget = CreateSkillContext(_players[targetSeat]),
                        HasOwnedCardCategory = (zones, categories) =>
                            zones.Contains(CardZoneKind.Equipment) &&
                            GetEquipment(_players[targetSeat]).Any(card =>
                                MatchesProgramCardCategory(card.Kind, categories)),
                        AttackRangeCoverageDecreased = _ => GetEquipment(_players[targetSeat])
                            .Any(card => WouldEquipmentRemovalReduceCoverage(targetSeat, card.Id))
                    });
                var targetValue = _aiBrains[owner.Seat].ScoreProgramTarget(snapshot, targetSeat, estimate.Hint);
                return (Estimate: estimate with { Score = estimate.Score + targetValue }, TargetSeat: (int?)targetSeat);
            })
            .OrderByDescending(candidate => candidate.Estimate.Score)
            .ThenBy(candidate => candidate.TargetSeat)
            .First();
    }

    private PromptChoice SelectAiCompositionTarget(PendingDecision decision, ProgramSkillFrame frame)
    {
        var owner = _players[decision.PlayerSeat];
        var program = _contentRegistry!.GetSkill(frame.SkillId).Program!;
        var effects = ProgramInstructionResolver.Default.Resolve(frame, program).Instructions;
        var targets = decision.Choices.SelectMany(choice => choice.Targets).Distinct().ToArray();
        var selected = EstimateCompositionForAi(owner, effects,
            CreateProgramAiPublicContext(owner) with
            {
                BooleanState = stateId => GetProgramBooleanState(frame.OwnerSeat, frame.SkillId,
                    frame.SkillInstanceId, stateId),
                PindianWon = bind => frame.PindianResultBindings.SingleOrDefault(item => item.Name == bind)?.SourceWon ?? false,
                ChoiceResult = bind => frame.ChoiceBindings.SingleOrDefault(item => item.Name == bind)?.OptionId
            }, targets);
        return decision.Choices.First(choice => choice.Targets.Contains(selected.TargetSeat ??
            throw new InvalidOperationException("A configured target selection has no published candidate.")));
    }

    private PromptChoice SelectAiProgramTargets(
        PendingDecision decision,
        SkillProgramTargetAiOrder aiOrder)
    {
        if (aiOrder == SkillProgramTargetAiOrder.Stable)
        {
            return decision.Choices
                .OrderByDescending(choice => choice.Targets.Count)
                .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
                .First();
        }
        var (choiceId, thought) = aiOrder switch
        {
            SkillProgramTargetAiOrder.HostileThenHandCount =>
                _aiBrains[decision.PlayerSeat].ChooseHostileHandTargets(
                    CreateSnapshot(decision.PlayerSeat), decision.Choices, _thoughtSequence++),
            SkillProgramTargetAiOrder.SupportFirstThenOpposeSecond =>
                _aiBrains[decision.PlayerSeat].ChooseSupportFirstTransferTargets(
                    CreateSnapshot(decision.PlayerSeat), decision.Choices, _thoughtSequence++),
            SkillProgramTargetAiOrder.SupportDraw =>
                _aiBrains[decision.PlayerSeat].ChooseSupportDrawTargets(
                    CreateSnapshot(decision.PlayerSeat), decision.Choices, _thoughtSequence++),
            SkillProgramTargetAiOrder.CardEffectIntervention =>
                _aiBrains[decision.PlayerSeat].ChooseCardEffectInterventionTargets(
                    CreateSnapshot(decision.PlayerSeat),
                    _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action.EffectiveKind,
                    decision.Choices, _thoughtSequence++),
            _ => throw new InvalidOperationException("Unsupported configured target AI order.")
        };
        AddThought(thought);
        return decision.Choices.Single(choice => choice.Id == choiceId);
    }

    private PromptChoice SelectAiProgramOwnedCards(PendingDecision decision, ProgramSkillFrame frame)
    {
        var draft = frame.OwnedCardSelection ??
            throw new InvalidOperationException("The configured owned-card choice lost its draft.");
        if (draft.MinimumCount > 0 && draft.SelectedCardIds.Count >= draft.MinimumCount)
            return decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        return decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")
            .OrderBy(choice => _cardZones.CardsAt(_cardZones.GetLocation(choice.Cards.Single()))
                .Single(card => card.Id == choice.Cards[0]).Kind is CardKind.Peach ? 1 : 0)
            .ThenBy(choice => choice.Cards[0]).First();
    }

    private PromptChoice SelectAiProgramHoldCards(PendingDecision decision, ProgramSkillFrame frame)
    {
        var draft = frame.HoldCardSelection ??
            throw new InvalidOperationException("The card-hold choice lost its draft.");
        if (draft.SelectedCardIds.Count >= draft.RequiredCount)
            return decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        // Public equipment is held first; hidden hand slots follow in fixed order.
        return decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")
            .OrderByDescending(choice =>
                choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment))
            .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .First();
    }

    private void CompleteProgramBinding(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext ??
            throw new InvalidOperationException("A trigger program frame lost its window context.");
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        QueueGameEvent(new ProgramBindingResolvedEvent(
            frame.Id, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId,
            frame.OwnerSeat, context.Window, Activated: true, Completed: completed));
        switch (context.Window)
        {
            case SkillProgramTriggerWindow.TurnStartBeforeNormalFlow:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame lifecycle ||
                    lifecycle.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The turn-start program lost its parent window.");
                AdvanceProgramLifecycleCursor(lifecycle);
                ContinueProgramLifecycleWindow();
                break;
            case SkillProgramTriggerWindow.AfterNormalDraw:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame afterDraw ||
                    afterDraw.Id != context.ParentFrameId ||
                    afterDraw.Continuation != ProgramLifecycleContinuation.CompleteAfterNormalDraw)
                    throw new InvalidOperationException("The after-draw program lost its parent window.");
                AdvanceProgramLifecycleCursor(afterDraw);
                ContinueProgramLifecycleWindow();
                break;
            case SkillProgramTriggerWindow.DrawPhaseStarting:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
                    drawPhase.Id != context.ParentFrameId ||
                    drawPhase.Continuation != ProgramLifecycleContinuation.CompleteDrawPhase)
                    throw new InvalidOperationException("The draw-phase program lost its parent window.");
                var drawTrigger = GetProgramTrigger(frame);
                if (completed && drawTrigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement)
                {
                    _resolutionStack[^1] = drawPhase with
                    {
                        NormalDrawReplaced = true,
                        CandidateIndex = drawPhase.Candidates.Count
                    };
                }
                else
                {
                    if (context.ResumeCandidateIndex is { } resumeCandidateIndex)
                        _resolutionStack[^1] = drawPhase with { CandidateIndex = resumeCandidateIndex };
                    else
                        AdvanceProgramLifecycleCursor(drawPhase);
                }
                ContinueProgramLifecycleWindow();
                break;
            case SkillProgramTriggerWindow.PlayEnding:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame playEnding ||
                    playEnding.Id != context.ParentFrameId ||
                    playEnding.Continuation != ProgramLifecycleContinuation.CompletePlayPhase)
                    throw new InvalidOperationException("The play-ending program lost its parent window.");
                AdvanceProgramLifecycleCursor(playEnding);
                ContinueProgramLifecycleWindow();
                break;
            case SkillProgramTriggerWindow.DiscardPhaseStarting:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame discardPhase ||
                    discardPhase.Id != context.ParentFrameId ||
                    discardPhase.Continuation != ProgramLifecycleContinuation.CompleteDiscardPhase)
                    throw new InvalidOperationException("The discard-phase program lost its parent window.");
                AdvanceProgramLifecycleCursor(discardPhase);
                ContinueProgramLifecycleWindow();
                break;
            case SkillProgramTriggerWindow.DiscardPhaseEnded:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame discardEnded ||
                    discardEnded.Id != context.ParentFrameId ||
                    discardEnded.Continuation != ProgramLifecycleContinuation.EndTurnAfterDiscardPhase)
                    throw new InvalidOperationException("The discard-phase-ended program lost its parent window.");
                AdvanceProgramLifecycleCursor(discardEnded);
                ContinueProgramLifecycleWindow();
                break;
            case SkillProgramTriggerWindow.TurnEnding:
                if (_resolutionStack.LastOrDefault() is not TurnEndingBoundaryFrame turnEnding ||
                    turnEnding.Id != context.ParentFrameId ||
                    turnEnding.Items[turnEnding.ItemIndex].Candidate is not { } expected ||
                    expected != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        turnEnding.Items[turnEnding.ItemIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The turn-ending program lost its parent item.");
                AdvanceTurnEndingBoundaryCursor(turnEnding);
                ContinueTurnEndingBoundary();
                break;
            case SkillProgramTriggerWindow.PlayPhaseStarting:
                if (_resolutionStack.LastOrDefault() is not PlayPhaseStartingBoundaryFrame playStarting ||
                    playStarting.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The play-phase-starting program lost its parent item.");
                if (context.ResumeCandidateIndex is { } resumeItemIndex)
                {
                    if (resumeItemIndex > playStarting.Items.Count)
                        throw new InvalidOperationException("The play-phase-starting resume cursor left the boundary.");
                    _resolutionStack[^1] = playStarting with { ItemIndex = resumeItemIndex };
                }
                else
                {
                    if (playStarting.ItemIndex >= playStarting.Items.Count ||
                        playStarting.Items[playStarting.ItemIndex].Candidate is not { } startingExpected ||
                        startingExpected != new ProgramTriggerCandidate(
                            frame.OwnerSeat,
                            frame.SkillId,
                            frame.TriggerId!,
                            frame.SkillInstanceId,
                            frame.GameplayHash,
                            playStarting.Items[playStarting.ItemIndex].Priority,
                            context.OccurrenceIndex))
                        throw new InvalidOperationException("The play-phase-starting program lost its parent item.");
                    AdvancePlayPhaseStartingCursor(playStarting);
                }
                ContinuePlayPhaseStartingBoundary();
                break;
            case SkillProgramTriggerWindow.SelfDyingResponse:
            case SkillProgramTriggerWindow.DyingResponse:
                CompleteDyingProgramBinding(frame, completed);
                break;
            case SkillProgramTriggerWindow.BeforeDamageApplied:
                if (_resolutionStack.LastOrDefault() is not BeforeDamageProgramWindowFrame beforeDamage ||
                    beforeDamage.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The before-damage program lost its parent window.");
                AdvanceBeforeDamageProgramCandidate(beforeDamage, activated: true, completed: completed);
                ContinueBeforeDamageProgramWindow();
                break;
            case SkillProgramTriggerWindow.DamageAppliedBeforeDying:
            case SkillProgramTriggerWindow.AfterDamageApplied:
                if (_pendingDamageTrigger is not { } damage || damage.FrameId != context.ParentFrameId)
                    throw new InvalidOperationException("The damage program lost its parent window.");
                AdvanceDamageTriggerCandidate(damage);
                break;
            case SkillProgramTriggerWindow.AfterHpLost:
            case SkillProgramTriggerWindow.AfterHpRecovered:
                if (_resolutionStack.LastOrDefault() is not HpChangedTriggerWindowFrame hpChanged ||
                    hpChanged.Id != context.ParentFrameId || hpChanged.Contexts[hpChanged.CandidateIndex] != context)
                    throw new InvalidOperationException("The HP-change program lost its parent event cursor.");
                AdvanceHpChangedProgramCursor(hpChanged);
                ContinueHpChangedProgramWindow();
                break;
            case SkillProgramTriggerWindow.CardsGained:
            case SkillProgramTriggerWindow.CardsMoved:
            case SkillProgramTriggerWindow.DiscardPileReceived:
                if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame cardsMoved ||
                    cardsMoved.Id != context.ParentFrameId ||
                    cardsMoved.Candidates[cardsMoved.CandidateIndex] != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        cardsMoved.Candidates[cardsMoved.CandidateIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The cards-moved program lost its parent batch cursor.");
                AdvanceCardsMovedProgramCursor(cardsMoved);
                ContinueCardsMovedProgramWindow();
                break;
            case SkillProgramTriggerWindow.OwnerDied:
                if (_resolutionStack.LastOrDefault() is not ProgramDeathTriggerWindowFrame deathWindow ||
                    deathWindow.Id != context.ParentFrameId ||
                    deathWindow.Candidates[deathWindow.CandidateIndex] != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        deathWindow.Candidates[deathWindow.CandidateIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The owner-death program lost its parent cursor.");
                AdvanceOwnerDiedProgramCursor(deathWindow);
                ContinueOwnerDiedProgramWindow();
                break;
            case SkillProgramTriggerWindow.CharacterDied:
                if (_resolutionStack.LastOrDefault() is not ProgramKillTriggerWindowFrame killWindow ||
                    killWindow.Id != context.ParentFrameId ||
                    killWindow.CandidateIndex >= killWindow.Candidates.Count ||
                    killWindow.Candidates[killWindow.CandidateIndex] != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        killWindow.Candidates[killWindow.CandidateIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The killer-death program lost its parent cursor.");
                _resolutionStack[^1] = killWindow with { CandidateIndex = killWindow.CandidateIndex + 1 };
                ContinueKillDiedProgramWindow();
                break;
            case SkillProgramTriggerWindow.CardUseCommitted:
            case SkillProgramTriggerWindow.CardUseBeforeTargetEffects:
            case SkillProgramTriggerWindow.CardUseTargetsFinalized:
            case SkillProgramTriggerWindow.CardResponseAccepted:
            case SkillProgramTriggerWindow.CardUseCompleted:
                if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame cardAction ||
                    cardAction.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The card-action program lost its parent cursor.");
                AdvanceProgramCardCandidate(cardAction);
                ContinueProgramCardWindow();
                break;
            case SkillProgramTriggerWindow.SlashTargetRedirecting:
            case SkillProgramTriggerWindow.SlashBeforeResponse:
            case SkillProgramTriggerWindow.SlashFullyDodged:
                if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame slashStage ||
                    slashStage.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The Slash program lost its parent cursor.");
                AdvanceProgramCardCandidate(slashStage);
                ContinueProgramCardWindow();
                break;
            case SkillProgramTriggerWindow.JudgmentFinalized:
                if (_resolutionStack.LastOrDefault() is not ProgramJudgmentTriggerWindowFrame finalizedWindow ||
                    finalizedWindow.Id != context.ParentFrameId ||
                    finalizedWindow.CandidateIndex >= finalizedWindow.Candidates.Count)
                    throw new InvalidOperationException("The finalized judgment program lost its parent window.");
                AdvanceProgramJudgmentCandidate(finalizedWindow);
                ContinueProgramJudgmentWindow();
                break;
            case SkillProgramTriggerWindow.JudgmentReplacing:
                CompleteProgramJudgmentReplacementBinding(frame, completed);
                break;
            default:
                throw new InvalidOperationException("Unsupported lifecycle program continuation.");
        }
    }

    private void CompleteDetachedProgramBinding(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext ??
            throw new InvalidOperationException("A detached program frame lost its window context.");
        CleanupProgramBoundCards(frame, completed);
        QueueGameEvent(new ProgramBindingResolvedEvent(
            frame.Id, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId,
            frame.OwnerSeat, context.Window, Activated: true, Completed: completed));
    }

    private void CompleteDyingProgramBinding(ProgramSkillFrame frame, bool completed)
    {
        var dying = _pendingDying ??
            throw new InvalidOperationException("The dying program lost its dying resolution.");
        if (dying.FrameId != frame.WindowContext!.ParentFrameId ||
            dying.VictimSeat != frame.WindowContext.TargetSeat ||
            dying.ResponderSeat != frame.OwnerSeat)
            throw new InvalidOperationException("The dying program returned to the wrong responder or victim.");
        var victim = _players[dying.VictimSeat];
        if (frame.WindowContext.Window == SkillProgramTriggerWindow.SelfDyingResponse)
        {
            dying.AttemptedSelfDyingBindings.Add(SelfDyingBindingKey(
                frame.SkillId, frame.TriggerId!, frame.SkillInstanceId));
            if (victim.Hp > 0)
                CompleteDying(dying, survived: true);
            else if (!TryBeginMandatorySelfDyingProgram(dying))
            {
                SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.AwaitingResponse);
                _status = EngineStatus.Running;
                ExposeHumanDyingPrompt();
            }
            return;
        }
        if (victim.Hp > 0)
        {
            CompleteDying(dying, survived: true);
            return;
        }
        dying.ResponderIndex++;
        if (dying.ResponderIndex >= dying.ResponderSeats.Count)
            CompleteDying(dying, survived: false);
        else
        {
            SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.AwaitingResponse);
            _status = EngineStatus.Running;
            ExposeHumanDyingPrompt();
        }
    }

    private ProgramSkillWindowContext CreateDyingProgramContext(
        DyingResolution dying,
        ProgramTriggerCandidate candidate) =>
        new(
            GetProgramTrigger(candidate).Window,
            dying.FrameId,
            candidate.OwnerSeat,
            SourceSeat: dying.Attack?.SourceSeat,
            TargetSeat: dying.VictimSeat,
            DamageFrameId: dying.DamageFrameId,
            OccurrenceIndex: candidate.OccurrenceIndex);

    private IReadOnlyList<ProgramTriggerCandidate> GetDyingProgramCandidates(
        CharacterState responder,
        DyingResolution dying) =>
        new[] { SkillProgramTriggerWindow.SelfDyingResponse, SkillProgramTriggerWindow.DyingResponse }
            .Where(window => window != SkillProgramTriggerWindow.SelfDyingResponse ||
                responder.Seat == dying.VictimSeat)
            .SelectMany(window => CollectProgramTriggerCandidates(responder, window))
                .Where(candidate => GetProgramTrigger(candidate).Window !=
                    SkillProgramTriggerWindow.SelfDyingResponse ||
                    !dying.AttemptedSelfDyingBindings.Contains(SelfDyingBindingKey(
                        candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId)))
                .Where(candidate => CanRunProgramTrigger(
                    candidate,
                    CreateDyingProgramContext(dying, candidate)))
                .ToArray();

    private static string SelfDyingBindingKey(string skillId, string bindingId, string skillInstanceId) =>
        $"{skillId}\u001f{bindingId}\u001f{skillInstanceId}";

    private bool TryBeginMandatorySelfDyingProgram(DyingResolution dying)
    {
        if (dying.ResponderIndex >= dying.ResponderSeats.Count ||
            dying.ResponderSeat != dying.VictimSeat) return false;
        var victim = _players[dying.VictimSeat];
        var candidate = GetDyingProgramCandidates(victim, dying).FirstOrDefault(item =>
            GetProgramTrigger(item) is
            {
                Window: SkillProgramTriggerWindow.SelfDyingResponse,
                Optional: false
            });
        if (candidate is null) return false;
        BeginDyingProgramBinding(candidate, dying);
        return true;
    }

    private void BeginDyingProgramBinding(ProgramTriggerCandidate candidate, DyingResolution dying) =>
        BeginProgramBinding(candidate, CreateDyingProgramContext(dying, candidate));

    private ProgramSkillWindowContext CreateAfterDamageProgramContext(
        DamageTriggerResolution damage,
        ProgramTriggerCandidate candidate)
    {
        var owner = _players[candidate.OwnerSeat];
        var facts = CaptureProgramTriggerFacts(owner) with
        {
            CardActionActorIsCurrentTurn = damage.Attack.SourceSeat == _currentSeat,
            CardActionPhaseIsPlay = _phase == TurnPhase.Play,
            OtherDamageParticipantAlive = IsValidPlayerSeat(damage.Attack.SourceSeat) &&
                IsValidPlayerSeat(damage.Attack.TargetSeat) &&
                damage.Attack.SourceSeat != damage.Attack.TargetSeat &&
                _players[damage.Attack.SourceSeat].IsAlive &&
                _players[damage.Attack.TargetSeat].IsAlive,
            DirectCardUseDamage = !damage.Attack.IsChainPropagation &&
                damage.Attack.Card is not null &&
                damage.Attack.CardUserSeat == damage.Attack.SourceSeat,
            DamageCardIsRed = damage.Attack.Card?.Suit is Suit.Heart or Suit.Diamond,
            DamageCardIsSlash = damage.Attack.EffectiveCardKind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash,
            SourceToTargetDistanceAtDamage = damage.Attack.SourceToTargetDistanceAtDamage,
            EventTargetHp = _players[damage.Attack.TargetSeat].Hp,
            EventTargetMaxHp = _players[damage.Attack.TargetSeat].MaxHp,
            DamageTargetIsOther = candidate.OwnerSeat != damage.Attack.TargetSeat,
            DamageSourceIsOwner = candidate.OwnerSeat == damage.Attack.SourceSeat,
            DamageSourceFactionId = GetEffectiveFactionId(_players[damage.Attack.SourceSeat])
        };
        return new(
            damage.Window,
            damage.FrameId,
            candidate.OwnerSeat,
            SourceSeat: damage.Attack.SourceSeat,
            TargetSeat: damage.Attack.TargetSeat,
            DamageFrameId: damage.DamageFrameId,
            Amount: damage.Attack.DamageAmount,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: facts);
    }
}
