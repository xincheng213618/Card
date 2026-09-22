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
        if (frame.TriggerId is not null || opponentReference.Kind != ProgramParticipantRef.SelectedTarget ||
            frame.SelectedTargetSeats.Count != 1 || frame.PindianResultBindings.Any(item => item.Name == resultBind) ||
            HasPendingProgramBoundCards ||
            _cardZones.CardsAt(CardLocation.Processing).Count != 0)
            throw new InvalidOperationException(
                "Program Pindian requires a clean active entry, one selected opponent and no pending program cards.");
        var opponentSeat = frame.SelectedTargetSeats.Single();
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
            legacySkill: null,
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
        if (frame.Window == SkillProgramTriggerWindow.DrawPhaseStarting && _phase != TurnPhase.Draw)
            throw new InvalidOperationException("A DrawPhaseStarting lifecycle frame must remain in its Draw phase.");
        if (frame.Window != SkillProgramTriggerWindow.DrawPhaseStarting && frame.NormalDrawAdjustment != 0)
            throw new InvalidOperationException("Only a DrawPhaseStarting lifecycle frame may adjust normal draws.");
        if (frame.Window is not (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
            SkillProgramTriggerWindow.DrawPhaseStarting or
            SkillProgramTriggerWindow.PlayEnding))
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
                TurnEndingBoundaryItemKind.LegacyJujian => DecisionKind.Jujian,
                _ => throw new InvalidOperationException("Unsupported turn-ending item kind.")
            };
            if (_pendingDecision?.Kind != decisionKind)
                throw new InvalidOperationException("The turn-ending boundary is waiting without its matching prompt.");
        }
        return true;
    }

    private void BeginNormalTurnStartAfterProgramBindings(CharacterState current)
    {
        if (TryBeginZiliAwakening(current)) return;
        BeginTurnStartAfterZili(current);
    }

    private void CompleteCurrentPlayPhase()
    {
        if (TryBeginPlayEndingProgramWindow(_players[_currentSeat])) return;
        CompletePlayPhaseAfterProgramWindow();
    }

    private void CompletePlayPhaseAfterProgramWindow()
    {
        if (TryBeginPhaseSkill(PhaseSkillWindow.PlayEnding, _players[_currentSeat])) return;
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
        var program = _contentRegistry?.GetSkill(frame.SkillId).Program ??
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
        IReadOnlyList<CardLocation>? sourceLocations = null)
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
                    Array.AsReadOnly(locations)))
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

    private void SetProgramChainedState(ProgramSkillFrame frame, bool chained)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.ActivationId != frame.ActivationId)
        {
            throw new InvalidOperationException("Chained-state mutation requires the active program binding.");
        }

        _players[frame.OwnerSeat].IsChained = chained;
        QueueGameEvent(new ProgramChainedStateSetEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            frame.OwnerSeat,
            chained));
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
        SkillProgramCardSetVisibility visibility)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.OwnerSeat != targetSeat || string.IsNullOrWhiteSpace(reason) ||
            string.IsNullOrWhiteSpace(resultBind) || visibility != SkillProgramCardSetVisibility.Public)
        {
            throw new InvalidOperationException(
                "A program judgment requires an active owner program frame and public result.");
        }

        _ = BeginJudgment(
            attack: null,
            targetSeat,
            reason,
            frame.Id,
            sourceCard: null,
            JudgmentContinuationKind.ProgramSkill,
            damageSkill: null,
            sourceSeat: frame.OwnerSeat,
            programResultBind: resultBind,
            programResultVisibility: visibility);
        return SkillProgramStepOutcome.AwaitChild;
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
                frameId, frame.SkillId, GetProgramBindingId(frame), resultBind,
                Array.AsReadOnly(revealed.Select(ToSnapshot).ToArray())));
    }

    private void FilterProgramBoundCards(
        long frameId,
        string sourceBind,
        string resultBind,
        IReadOnlyList<Suit> suits)
    {
        var frame = GetActiveProgramFrame(frameId);
        var source = GetProgramCardSet(frame, sourceBind);
        var acceptedSuits = suits.ToHashSet();
        var selected = source.CardIds.Select((cardId, index) =>
            {
                var location = source.SourceLocations[index];
                if (_cardZones.GetLocation(cardId) != location)
                    throw new InvalidOperationException(
                        "A bound card left its frozen source before its configured filter.");
                var card = _cardZones.CardsAt(location).Single(current => current.Id == cardId);
                return (Card: card, Location: location);
            })
            .Where(item => acceptedSuits.Contains(item.Card.Suit))
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
        _ = GetActiveProgramFrame(frameId);
        var target = _players[targetSeat];
        var drawCount = numberExpression switch
        {
            null => amount,
            SkillProgramNumberExpression.LivingFactionCount => GetLivingFactionCount(),
            SkillProgramNumberExpression.TargetMaxHpMinusHandCount =>
                Math.Max(0, target.MaxHp - GetHand(target).Count),
            _ => throw new InvalidOperationException(
                $"Unsupported draw number expression '{numberExpression}'.")
        };
        var drawn = DrawCards(target, drawCount, log: true, reason);
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

    private IReadOnlyList<int> GetProgramTargetSeats(
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        ProgramSkillWindowContext? windowContext = null)
    {
        if (targetKind == SkillProgramTargetKind.EventTarget)
        {
            windowContext ??= (_resolutionStack.LastOrDefault() as ProgramSkillFrame)?.WindowContext;
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
        return _players
            .Where(target => target.IsAlive && targetKind switch
            {
                SkillProgramTargetKind.OtherLiving => target.Seat != ownerSeat,
                SkillProgramTargetKind.OtherLivingWithHand =>
                    target.Seat != ownerSeat && GetHand(target).Count > 0,
                SkillProgramTargetKind.AnyLiving => true,
                SkillProgramTargetKind.OtherWounded =>
                    target.Seat != ownerSeat && target.Hp < target.MaxHp,
                SkillProgramTargetKind.AnyWounded => target.Hp < target.MaxHp,
                SkillProgramTargetKind.AnyLivingHandBelowMaxHp =>
                    GetHand(target).Count < target.MaxHp,
                _ => false
            })
            .Select(target => target.Seat)
            .Order()
            .ToArray();
    }

    private bool IsProgramTargetEligible(int ownerSeat, SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones, int targetSeat)
    {
        if (!GetProgramTargetSeats(ownerSeat, targetKind).Contains(targetSeat)) return false;
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
        SkillProgramTargetAiOrder aiOrder)
    {
        var frame = GetActiveProgramFrame(frameId);
        var targetSeats = GetProgramTargetSeats(ownerSeat, targetKind);
        var cappedMaximum = Math.Min(maximumTargets, targetSeats.Count);
        if (minimumTargets < 1 || cappedMaximum < minimumTargets)
        {
            CancelProgramBindingAndCleanup(frame, "没有足够的合法技能目标，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var selections = new List<IReadOnlyList<int>>();
        for (var first = 0; first < targetSeats.Count; first++)
        {
            if (minimumTargets <= 1)
                selections.Add(Array.AsReadOnly(new[] { targetSeats[first] }));
            if (cappedMaximum < 2) continue;
            for (var second = first + 1; second < targetSeats.Count; second++)
                selections.Add(Array.AsReadOnly(new[] { targetSeats[first], targetSeats[second] }));
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
                    ["maximum-targets"] = maximumTargets.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-ai-order"] = aiOrder.ToString()
                });
        }).ToArray();
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        var targetCountText = minimumTargets == maximumTargets
            ? minimumTargets.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{minimumTargets} 至 {maximumTargets}";
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

        var validTargets = GetProgramTargetSeats(ownerSeat, SkillProgramTargetKind.OtherLivingWithHand);
        foreach (var targetSeat in frame.SelectedTargetSeats)
        {
            if (!validTargets.Contains(targetSeat))
                throw new InvalidOperationException("A selected random hand-card target is no longer legal.");
            var target = _players[targetSeat];
            var hand = GetHand(target);
            var card = hand[_random.Next(hand.Count)];
            MoveCard(card, CardLocation.Hand(targetSeat), CardLocation.Processing, reason);
            MoveCard(card, CardLocation.Processing, CardLocation.Hand(ownerSeat), reason);
        }
        QueueGameEvent(new ProgramRandomHandCardsTakenEvent(
            frame.Id,
            frame.SkillId,
            frame.TriggerId!,
            ownerSeat,
            Array.AsReadOnly(frame.SelectedTargetSeats.ToArray()),
            frame.SelectedTargetSeats.Count));
        AddLog("SkillEffect",
            $"{_players[ownerSeat].Name} 从 {string.Join("、", frame.SelectedTargetSeats.Select(seat => _players[seat].Name))} 各获得一张手牌。",
            ownerSeat);
    }

    private SkillProgramStepOutcome SelectProgramTarget(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones)
    {
        var frame = GetActiveProgramFrame(frameId);
        var targetSeats = GetProgramTargetSeats(ownerSeat, targetKind)
            .Where(seat => IsProgramTargetEligible(ownerSeat, targetKind, zones, seat)).ToArray();
        if (targetSeats.Length == 0)
        {
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
                    ["target-kind"] = targetKind.ToString()
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
        IReadOnlyList<CardZoneKind> zones,
        string resultBind)
    {
        var frame = GetActiveProgramFrame(frameId);
        var sourceSeat = frame.WindowContext?.SourceSeat;
        if (sourceSeat is null || !IsValidPlayerSeat(sourceSeat.Value) || sourceSeat == ownerSeat)
        {
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
            foreach (var card in GetEquipment(_players[sourceSeat.Value]))
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
        if (choices.Count == 0)
        {
            CancelProgramBindingAndCleanup(frame, "伤害来源已没有可选择的牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            ownerSeat,
            $"【{presentation.Name}】请选择伤害来源的一张牌。",
            zones.Contains(CardZoneKind.Equipment)
                ? GetEquipment(_players[sourceSeat.Value]).Select(card => card.Id).ToArray()
                : [],
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
        if (frame.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.AfterDamageApplied,
                ParentFrameId: var parentFrameId
            } || _pendingDamageTrigger is not { } damage || damage.FrameId != parentFrameId)
            throw new InvalidOperationException("The program damage-card claim lost its damage window.");
        var cards = damage.Attack.PhysicalCards
            .Where(card => _cardZones.GetLocation(card.Id) == CardLocation.Processing)
            .ToArray();
        if (cards.Length == 0) return;
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

    private SkillProgramStepOutcome SelectProgramCardSubset(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string resultBind,
        int minimumCards,
        int maximumCards,
        int maximumRankSum,
        SkillProgramSubsetAiOrder aiOrder)
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
        var options = CardSubsetSelector.Enumerate(
            cards.Select(card => new CardSubsetCandidate(card.Id, card.Rank)).ToArray(),
            new CardSubsetConstraint(minimumCards, maximumCards, maximumRankSum));
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
                $"选择 {minimumCards} 至 {maximumCards} 张牌，点数和不超过 {maximumRankSum}。"),
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

    private void MoveProgramBoundCards(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string? exceptBind,
        SkillProgramCardDestination destination,
        CardMoveReason reason)
    {
        var frame = GetActiveProgramFrame(frameId);
        var source = frame.CardSetBindings.SingleOrDefault(binding => binding.Name == sourceBind);
        if (source is null)
        {
            CancelProgramBindingAndCleanup(
                frame,
                $"移动来源绑定“{sourceBind}”未生成，技能结算已取消。");
            return;
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
                return;
            }
            ids.ExceptWith(except.CardIds);
        }
        var selected = source.CardIds.Select((cardId, index) =>
                (CardId: cardId, Location: source.SourceLocations[index]))
            .Where(item => ids.Contains(item.CardId))
            .ToArray();
        if (selected.Any(item => _cardZones.GetLocation(item.CardId) != item.Location))
            throw new InvalidOperationException("A bound card left its frozen source before its configured move.");
        if (selected.Length == 0) return;
        var target = destination switch
        {
            SkillProgramCardDestination.OwnerHand => CardLocation.Hand(ownerSeat),
            SkillProgramCardDestination.DiscardPile => CardLocation.DiscardPile,
            SkillProgramCardDestination.SelectedTargetHand when frame.SelectedTargetSeats.Count == 1 =>
                CardLocation.Hand(frame.SelectedTargetSeats.Single()),
            _ => throw new InvalidOperationException($"Unsupported program card destination '{destination}'.")
        };
        foreach (var group in selected.GroupBy(item => item.Location))
        {
            if (group.Key == target) continue;
            var cards = group.Select(item => _cardZones.CardsAt(group.Key)
                .Single(card => card.Id == item.CardId)).ToArray();
            MoveCards(cards, group.Key, target, reason);
        }
    }

    private IReadOnlyList<CardSnapshot> GetProgramPublicCards()
    {
        var publicIds = _resolutionStack.OfType<ProgramSkillFrame>()
            .SelectMany(frame => frame.CardSetBindings)
            .Where(binding => binding.Visibility == SkillProgramCardSetVisibility.Public)
            .SelectMany(binding => binding.CardIds)
            .Distinct()
            .ToHashSet();
        return _cardZones.CardsAt(CardLocation.Processing)
            .Where(card => publicIds.Contains(card.Id))
            .Select(ToSnapshot)
            .ToArray();
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
        if (_pendingLeiji is { } leiji)
            allowedParentIds.UnionWith(leiji.OriginalAttack.PhysicalCards.Select(card => card.Id));
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
        (GetSkillBindingShard(owner)?.GetInstanceTriggers(window) ?? [])
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
                return trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId);
            })
            .ToArray();
    }

    private bool CanRunProgramTrigger(
        ProgramTriggerCandidate candidate,
        ProgramSkillWindowContext context)
    {
        if (!IsValidPlayerSeat(candidate.OwnerSeat) || candidate.OwnerSeat != context.OwnerSeat) return false;
        var owner = _players[candidate.OwnerSeat];
        if (!owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, candidate.SkillId, candidate.SkillInstanceId) ||
            _contentRegistry?.Skills.GetValueOrDefault(candidate.SkillId)?.Program is not { } program ||
            program.GameplayHash != candidate.GameplayHash)
            return false;
        var trigger = program.Triggers.SingleOrDefault(item => item.Id == candidate.BindingId);
        if (trigger is null || trigger.Window != context.Window || !trigger.UsesSharedExecutor)
            return false;
        if (!trigger.Condition.Evaluate(context.Facts ?? CaptureProgramTriggerFacts(owner),
                candidate.SkillId, candidate.SkillInstanceId))
            return false;
        if (trigger.UsageScope is { } scope && trigger.UsageLimit is { } limit &&
            _skillRuntimeState.GetUsage(
                owner.Seat, candidate.SkillId, ProgramTriggerUsageId(candidate), scope) >= limit)
            return false;
        if (trigger.Effects.Any(effect => effect.Op == SkillProgramTriggerEffectOp.SelectTarget &&
            effect.TargetKind is { } kind && !GetProgramTargetSeats(owner.Seat, kind, context).Any(seat =>
                effect.Zones.Count == 0 || effect.Zones.Any(zone => zone switch
                {
                    CardZoneKind.Hand => GetHand(_players[seat]).Count > 0,
                    CardZoneKind.Equipment => GetEquipment(_players[seat]).Count > 0,
                    CardZoneKind.Judgment => GetJudgment(_players[seat]).Count > 0,
                    _ => false
                }))))
            return false;
        return context.Window switch
        {
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat,
            SkillProgramTriggerWindow.DrawPhaseStarting =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat &&
                _phase == TurnPhase.Draw && CanRunDrawPhaseProgramTrigger(owner, trigger),
            SkillProgramTriggerWindow.PlayEnding =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat &&
                _phase == TurnPhase.Play,
            SkillProgramTriggerWindow.TurnEnding =>
                owner.Seat == _currentSeat && context.SourceSeat == owner.Seat &&
                _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault()?.Id == context.ParentFrameId,
            SkillProgramTriggerWindow.SelfDyingResponse =>
                _pendingDying is { } dying && dying.FrameId == context.ParentFrameId &&
                dying.VictimSeat == owner.Seat && dying.ResponderSeat == owner.Seat && owner.Hp <= 0,
            SkillProgramTriggerWindow.AfterDamageApplied =>
                context.TargetSeat == owner.Seat && context.Amount > 0 &&
                CanRunAfterDamageProgramTrigger(owner, trigger, context),
            SkillProgramTriggerWindow.CardsMoved =>
                context.MovementBatch is { } batch &&
                batch.Id == context.ParentFrameId &&
                _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault()?.Id == batch.Id &&
                batch.SourceCounts.Any(item => item.Location.OwnerSeat == owner.Seat),
            SkillProgramTriggerWindow.CardUseCommitted or
                SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
                SkillProgramTriggerWindow.CardUseTargetsFinalized or
                SkillProgramTriggerWindow.CardResponseAccepted =>
                context.CardUse is { } cardUse &&
                _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is { } cardFrame &&
                cardFrame.Id == context.ParentFrameId && cardFrame.Action.ActionId == cardUse.CardActionId &&
                cardFrame.Candidates.Any(item => item.OwnerSeat == candidate.OwnerSeat &&
                    item.SkillId == candidate.SkillId && item.SkillInstanceId == candidate.SkillInstanceId &&
                    item.TriggerId == candidate.BindingId && item.GameplayHash == candidate.GameplayHash),
            _ => false
        };
    }

    private bool CanRunDrawPhaseProgramTrigger(CharacterState owner, SkillProgramTrigger trigger)
    {
        foreach (var effect in trigger.Effects.Where(effect =>
                     effect.Condition.Evaluate(CreateSkillContext(owner))))
        {
            if (effect.Op == SkillProgramTriggerEffectOp.SelectTargets &&
                (effect.TargetKind is not { } targetKind ||
                 GetProgramTargetSeats(owner.Seat, targetKind).Count < effect.MinimumTargets))
                return false;
            if (effect.Op == SkillProgramTriggerEffectOp.SelectTarget && effect.TargetKind is { } singleKind &&
                !GetProgramTargetSeats(owner.Seat, singleKind).Any(seat =>
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
            damage.DamageFrameId != context.DamageFrameId)
            return false;
        foreach (var effect in trigger.Effects.Where(effect =>
                     effect.Condition.Evaluate(CreateSkillContext(owner))))
        {
            switch (effect.Op)
            {
                case SkillProgramTriggerEffectOp.SelectTarget:
                    if (effect.TargetKind is not { } targetKind ||
                        GetProgramTargetSeats(owner.Seat, targetKind).Count == 0)
                        return false;
                    break;
                case SkillProgramTriggerEffectOp.SelectSourceCard:
                    if (context.SourceSeat is not { } sourceSeat || sourceSeat == owner.Seat ||
                        !IsValidPlayerSeat(sourceSeat) ||
                        !effect.Zones.Any(zone => zone switch
                        {
                            CardZoneKind.Hand => GetHand(_players[sourceSeat]).Count > 0,
                            CardZoneKind.Equipment => GetEquipment(_players[sourceSeat]).Count > 0,
                            _ => false
                        }))
                        return false;
                    break;
                case SkillProgramTriggerEffectOp.ClaimDamageCards:
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

    private static string ProgramTriggerUsageId(ProgramTriggerCandidate candidate) =>
        $"{candidate.BindingId}@{candidate.SkillInstanceId}";

    private SkillProgramTriggerFacts CaptureProgramTriggerFacts(CharacterState owner)
    {
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var instance in GetSkillBindingShard(owner)?.ProgramInstances ?? [])
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
            CurrentMaxHp: owner.MaxHp,
            BooleanStates: states);
    }

    private SkillProgramTriggerFacts CaptureProgramTriggerFacts(CharacterState owner, CardActionContext action) =>
        CaptureProgramTriggerFacts(owner) with
        {
            CardActionActorIsCurrentTurn = action.ActorSeat == _currentSeat,
            CardActionPhaseIsPlay = _phase == TurnPhase.Play
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
        var prompt = context.Window == SkillProgramTriggerWindow.PlayEnding && facts is not null
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
        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(
            owner, SkillProgramTriggerWindow.PlayEnding, facts);
        if (candidates.Count == 0) return false;
        var frame = new ProgramLifecycleTriggerWindowFrame(
            ++_resolutionSequence,
            owner.Seat,
            SkillProgramTriggerWindow.PlayEnding,
            candidates,
            ProgramLifecycleContinuation.CompletePlayPhase,
            facts);
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

    private bool TryBeginTurnEndingBoundary(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _pendingDecision is not null ||
            _resolutionStack.Count != 0)
            return false;

        var facts = CaptureProgramTriggerFacts(owner);
        var items = CollectEligibleProgramTriggerCandidates(
                owner, SkillProgramTriggerWindow.TurnEnding, facts)
            .Select(candidate => new TurnEndingBoundaryItem(
                TurnEndingBoundaryItemKind.Program,
                candidate.Priority,
                $"program:{candidate.SkillId}:{candidate.BindingId}:{candidate.SkillInstanceId}",
                candidate))
            .ToList();
        if (UsesFormalXuShu && !_jujianResolvedThisTurn && HasRuntimeSkill(owner, SkillKind.Jujian))
        {
            items.Add(new TurnEndingBoundaryItem(
                TurnEndingBoundaryItemKind.LegacyJujian,
                0,
                "legacy:classic:jujian"));
        }
        var ordered = items
            .OrderByDescending(item => item.Priority)
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
        ProgramTriggerCandidate candidate) =>
        new(
            SkillProgramTriggerWindow.TurnEnding,
            frame.Id,
            frame.OwnerSeat,
            SourceSeat: frame.OwnerSeat,
            TargetSeat: frame.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: frame.Facts);

    private void ContinueTurnEndingBoundary()
    {
        while (_resolutionStack.LastOrDefault() is TurnEndingBoundaryFrame frame)
        {
            if (frame.ItemIndex >= frame.Items.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.TurnEndingBoundary);
                if (_winner != Winner.None)
                {
                    if (_status != EngineStatus.Completed) CompleteGame();
                    else PublishState();
                    return;
                }
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
                    var context = CreateTurnEndingProgramContext(frame, candidate);
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
                case TurnEndingBoundaryItemKind.LegacyJujian:
                    _resolutionStack[^1] = frame with { Step = ResolutionFrameStep.AwaitingResponse };
                    if (CanRunLegacyJujianBridge(frame) && TryBeginJujianChoice(_players[frame.OwnerSeat]))
                        return;
                    AdvanceTurnEndingBoundaryCursor((TurnEndingBoundaryFrame)_resolutionStack[^1]);
                    continue;
                default:
                    throw new InvalidOperationException("Unsupported turn-ending boundary item.");
            }
        }
    }

    private bool CanRunLegacyJujianBridge(TurnEndingBoundaryFrame frame) =>
        frame.OwnerSeat == _currentSeat && frame.TurnNumber == _turnNumber &&
        _winner == Winner.None && _players[frame.OwnerSeat].IsAlive && UsesFormalXuShu &&
        !_jujianResolvedThisTurn && HasRuntimeSkill(_players[frame.OwnerSeat], SkillKind.Jujian);

    private bool ResumeTurnEndingBoundaryAfterLegacyJujian()
    {
        if (_resolutionStack.LastOrDefault() is not TurnEndingBoundaryFrame
            {
                Step: ResolutionFrameStep.AwaitingResponse
            } frame || frame.ItemIndex >= frame.Items.Count ||
            frame.Items[frame.ItemIndex].Kind != TurnEndingBoundaryItemKind.LegacyJujian)
            return false;
        if (_pendingJujian is not null || _pendingDecision?.Kind == DecisionKind.Jujian)
            throw new InvalidOperationException("Jujian attempted to resume before its legacy choice completed.");
        AdvanceTurnEndingBoundaryCursor(frame);
        ContinueTurnEndingBoundary();
        return true;
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
                            frame.NormalDrawReplaced, frame.NormalDrawAdjustment);
                        break;
                    case ProgramLifecycleContinuation.CompletePlayPhase:
                        CompletePlayPhaseAfterProgramWindow();
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported lifecycle continuation.");
                }
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = new ProgramSkillWindowContext(
                frame.Window, frame.Id, frame.OwnerSeat,
                SourceSeat: frame.OwnerSeat,
                TargetSeat: frame.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: frame.Facts);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceProgramLifecycleCandidate(frame, activated: false, completed: false);
                continue;
            }
            var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                .Single(item => item.Id == candidate.BindingId);
            if (trigger.Optional)
            {
                if (trigger.ChoiceGroup is not null)
                    ExposeProgramTriggerGroupDecision(frame, candidate, context, trigger.ChoiceGroup);
                else
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
        string choiceGroup)
    {
        var members = frame.Candidates.Skip(frame.CandidateIndex)
            .TakeWhile(candidate =>
                candidate.OwnerSeat == first.OwnerSeat &&
                candidate.SkillId == first.SkillId &&
                candidate.SkillInstanceId == first.SkillInstanceId &&
                candidate.Priority == first.Priority &&
                GetProgramTrigger(candidate).ChoiceGroup == choiceGroup)
            .ToArray();
        if (members.Length < 2)
            throw new InvalidOperationException("A configured program choice group lost its branches.");
        return members;
    }

    private void ExposeProgramTriggerGroupDecision(
        ProgramLifecycleTriggerWindowFrame frame,
        ProgramTriggerCandidate first,
        ProgramSkillWindowContext context,
        string choiceGroup)
    {
        var skill = _contentRegistry!.GetSkill(first.SkillId);
        var members = GetProgramTriggerChoiceGroup(frame, first, choiceGroup);
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
        }).Append(new PromptChoice(
            new ChoiceId($"program-trigger.skip-group.{first.SkillId}.{choiceGroup}.{first.SkillInstanceId}"),
            $"不发动【{skill.Name}】。",
            [], [], Parameters(first, "skip"))).ToArray();
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
            Choices = Array.AsReadOnly(choices)
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
        if (action is "select-target" or "select-targets" or "select-source-card" or "select-and-move-owned-card" or
            "give-bound-card" or "keep-bound-cards")
        {
            ResolveProgramInstructionChoice(selected, action);
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
            if (selected.Cards.Distinct().Count() != selected.Cards.Count ||
                selected.Cards.Any(cardId => !source.CardIds.Contains(cardId)) ||
                selected.Cards.Count < effect.MinimumCards || selected.Cards.Count > effect.MaximumCards)
                throw new InvalidOperationException("The selected subset is no longer legal.");
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
                    selected.Targets.Count != 1 || selected.Cards.Count != 0)
                    throw new InvalidOperationException("The selected target does not match the suspended instruction.");
                var targetSeat = selected.Targets.Single();
                if (!IsProgramTargetEligible(frame.OwnerSeat, targetKind, effect.Zones, targetSeat))
                    throw new InvalidOperationException("The selected program target is no longer legal.");
                ClearPendingDecision();
                _resolutionStack[^1] = frame with
                {
                    SelectedTargetSeats = Array.AsReadOnly(new[] { targetSeat })
                };
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
                    selected.Targets.Distinct().Count() != selected.Targets.Count)
                    throw new InvalidOperationException("The selected targets do not match the suspended instruction.");
                var legalTargets = GetProgramTargetSeats(frame.OwnerSeat, targetKind);
                if (selected.Targets.Any(targetSeat => !legalTargets.Contains(targetSeat)))
                    throw new InvalidOperationException("A selected program target is no longer legal.");
                ClearPendingDecision();
                _resolutionStack[^1] = frame with
                {
                    SelectedTargetSeats = Array.AsReadOnly(selected.Targets.ToArray())
                };
                ContinueProgramSkill(frame.Id);
                return;
            }
            case "select-source-card":
            {
                if (effect.Op != SkillProgramEffectOp.SelectSourceCard ||
                    selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
                    !Enum.TryParse<CardZoneKind>(
                        selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
                    !effect.Zones.Contains(zone) ||
                    frame.WindowContext?.SourceSeat is not { } sourceSeat)
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
            case "select-and-move-owned-card":
            {
                if (effect.Op != SkillProgramEffectOp.SelectAndMoveOwnedCard)
                    throw new InvalidOperationException("The payment choice does not match the suspended instruction.");
                ResolveSelectAndMoveOwnedCardChoice(frame, effect, selected);
                return;
            }
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
            return (candidate, CreateTurnEndingProgramContext(turnEnding, candidate));
        }
        if (_resolutionStack.LastOrDefault() is ProgramLifecycleTriggerWindowFrame lifecycle)
        {
            var candidate = lifecycle.Candidates[lifecycle.CandidateIndex];
            return (candidate, new ProgramSkillWindowContext(
                lifecycle.Window, lifecycle.Id, lifecycle.OwnerSeat,
                SourceSeat: lifecycle.OwnerSeat, TargetSeat: lifecycle.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: lifecycle.Facts));
        }
        if (_resolutionStack.LastOrDefault() is CardsMovedTriggerWindowFrame cardsMoved)
        {
            var candidate = cardsMoved.Candidates[cardsMoved.CandidateIndex];
            return (candidate, CreateCardsMovedProgramContext(cardsMoved, candidate));
        }
        if (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame cardAction &&
            cardAction.Candidates[cardAction.CandidateIndex] is { UsesSharedExecutor: true } cardCandidate)
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
        if (_resolutionStack.LastOrDefault() is TurnEndingBoundaryFrame turnEnding &&
            turnEnding.Items[turnEnding.ItemIndex].Candidate == candidate)
        {
            AdvanceTurnEndingBoundaryCandidate(
                turnEnding, candidate, activated: false, completed: false);
            ContinueTurnEndingBoundary();
            return;
        }
        if (_resolutionStack.LastOrDefault() is ProgramLifecycleTriggerWindowFrame lifecycle)
        {
            AdvanceProgramLifecycleCandidate(lifecycle, activated: false, completed: false);
            ContinueProgramLifecycleWindow();
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
        if (_resolutionStack.LastOrDefault() is ProgramCardTriggerWindowFrame cardAction &&
            cardAction.Candidates[cardAction.CandidateIndex] is { UsesSharedExecutor: true } cardCandidate &&
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
                candidate.OwnerSeat, SkillProgramTriggerWindow.AfterDamageApplied,
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
                SkillProgramEffectOp.SelectCardSubset
                    when paused.AiOrder == SkillProgramSubsetAiOrder.MostCardsThenRankSum =>
                    decision.Choices
                        .OrderByDescending(choice => choice.Cards.Count)
                        .ThenByDescending(choice => int.Parse(
                            choice.Parameters.GetValueOrDefault("rank-sum", "0"),
                            System.Globalization.CultureInfo.InvariantCulture))
                        .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal)
                        .First(),
                SkillProgramEffectOp.SelectTargets when paused.TargetAiOrder is { } targetAiOrder =>
                    SelectAiProgramTargets(decision, targetAiOrder),
                SkillProgramEffectOp.SelectTarget when _contentRegistry!.GetSkill(frame.SkillId).Program!.UsesCompositionKernel =>
                    SelectAiCompositionTarget(decision, frame),
                SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectSourceCard or SkillProgramEffectOp.SelectAndMoveOwnedCard or
                    SkillProgramEffectOp.GiveBoundCard => decision.Choices[0],
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
            return SelectAiProgramChoiceGroup(decision, activateChoices, skip ??
                throw new InvalidOperationException("A program choice group lost its skip branch."));
        var activate = activateChoices.Single();
        if (skip is null) return activate;

        var skillId = activate.Parameters.GetValueOrDefault("skill-id");
        var bindingId = activate.Parameters.GetValueOrDefault("binding-id");
        var skillInstanceId = activate.Parameters.GetValueOrDefault("skill-instance-id");
        if (string.IsNullOrEmpty(skillId) || string.IsNullOrEmpty(bindingId)) return activate;
        var skill = _contentRegistry!.GetSkill(skillId);
        var trigger = skill.Program!.Triggers.Single(item => item.Id == bindingId);
        if (skill.Program.UsesCompositionKernel)
        {
            var owner = _players[decision.PlayerSeat];
            var estimate = EstimateCompositionForAi(owner,
                trigger.Effects.Select(effect => effect.ToExecutionEffect()),
                WithProgramConditionFacts(CreateProgramAiPublicContext(trigger, owner, decision.PlayerSeat),
                    owner, skillId, skillInstanceId!)).Estimate;
            var compositionShouldActivate = !estimate.IsSelfLethal && estimate.Score > 0d;
            var activateAction = new LegalAction(
                LegalActionKind.UseProgramSkill, null, null, $"发动【{skill.Name}】");
            var skipAction = new LegalAction(
                LegalActionKind.UseProgramSkill, null, null, $"跳过【{skill.Name}】");
            var candidates = new[]
            {
                new AiCandidateScore(activateAction,
                    compositionShouldActivate ? estimate.Score : Math.Min(estimate.Score, -1000d),
                    estimate.IsSelfLethal ? "公开效果将令自身失去全部体力。" : estimate.Approximation),
                new AiCandidateScore(skipAction, 0d, "保留当前状态。")
            };
            AddThought(new AiThoughtRecord(
                _thoughtSequence++, _turnNumber, decision.PlayerSeat,
                compositionShouldActivate ? activateAction.Description : skipAction.Description,
                candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
                $"schema23组合估值 {estimate.Score:0.##}；{estimate.Approximation}"));
            return compositionShouldActivate ? activate : skip;
        }
        // Schema 19+ owns the composable draw-phase heuristic. Other windows in
        // the same runtime keep their existing policies; a later schema needs
        // an explicit capability-version decision before sharing this branch.
        if (skill.Program.RuntimeVersion is "skill-program-v19" or "skill-program-v20" or
                "skill-program-v21" &&
            trigger.Window == SkillProgramTriggerWindow.DrawPhaseStarting)
        {
            if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
                drawPhase.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                drawPhase.OwnerSeat != decision.PlayerSeat)
            {
                throw new InvalidOperationException(
                    "A composable draw-phase AI choice lost its lifecycle parent.");
            }
            var owner = _players[decision.PlayerSeat];
            var normalDrawCount = checked(GetTurnDrawCount(owner) + drawPhase.NormalDrawAdjustment);
            var (composedShouldActivate, composedThought) = _aiBrains[decision.PlayerSeat]
                .ChooseDrawPhaseProgramActivation(
                    CreateSnapshot(decision.PlayerSeat),
                    trigger,
                    CreateSkillContext(owner),
                    normalDrawCount,
                    skill.Name,
                    _thoughtSequence++);
            AddThought(composedThought);
            return composedShouldActivate ? activate : skip;
        }
        if (trigger.DrawPhaseMode != SkillProgramDrawPhaseMode.Replacement)
            return activate;
        if (trigger.Effects.FirstOrDefault() is
            {
                Op: SkillProgramTriggerEffectOp.RevealTopCards,
                NumberExpression: SkillProgramNumberExpression.OwnerLostHp
            })
        {
            var (revealShouldActivate, revealThought) = _aiBrains[decision.PlayerSeat]
                .ChooseLostHpRevealReplacementActivation(
                    CreateSnapshot(decision.PlayerSeat), skill.Name, _thoughtSequence++);
            AddThought(revealThought);
            return revealShouldActivate ? activate : skip;
        }
        if (trigger.Effects.FirstOrDefault() is not
            {
                Op: SkillProgramTriggerEffectOp.SelectTargets,
                TargetKind: { } targetKind,
                TargetAiOrder: SkillProgramTargetAiOrder.HostileThenHandCount
            } selection)
            return activate;

        var targetSeats = GetProgramTargetSeats(decision.PlayerSeat, targetKind);
        var (shouldActivate, thought) = _aiBrains[decision.PlayerSeat]
            .ChooseHostileHandReplacementActivation(
                CreateSnapshot(decision.PlayerSeat),
                targetSeats,
                selection.MinimumTargets,
                selection.MaximumTargets,
                skill.Name,
                _thoughtSequence++);
        AddThought(thought);
        return shouldActivate ? activate : skip;
    }

    private PromptChoice SelectAiProgramChoiceGroup(
        PendingDecision decision,
        IReadOnlyList<PromptChoice> activateChoices,
        PromptChoice skip)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
            drawPhase.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
            drawPhase.OwnerSeat != decision.PlayerSeat)
            throw new InvalidOperationException("A draw-phase choice group lost its lifecycle parent.");
        var owner = _players[decision.PlayerSeat];
        var normalDrawCount = checked(GetTurnDrawCount(owner) + drawPhase.NormalDrawAdjustment);
        var choices = activateChoices
            .OrderBy(choice => choice.Id.Value, StringComparer.Ordinal)
            .Select(choice =>
            {
                var skillId = choice.Parameters.GetValueOrDefault("skill-id")!;
                var bindingId = choice.Parameters.GetValueOrDefault("binding-id")!;
                var skill = _contentRegistry!.GetSkill(skillId);
                var trigger = skill.Program!.Triggers.Single(item => item.Id == bindingId);
                return (Choice: choice, Skill: skill, Trigger: trigger);
            })
            .ToArray();
        if (choices.All(item => item.Skill.Program!.UsesCompositionKernel))
        {
            var estimated = choices.Select(item =>
            {
                var estimate = EstimateCompositionForAi(owner,
                    item.Trigger.Effects.Select(effect => effect.ToExecutionEffect()),
                    WithProgramConditionFacts(CreateProgramAiPublicContext(item.Trigger, owner, decision.PlayerSeat),
                        owner, item.Skill.Id,
                        item.Choice.Parameters.GetValueOrDefault("skill-instance-id")!)).Estimate;
                var action = new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                    $"发动【{item.Skill.Name}】");
                return (item.Choice, Action: action, Estimate: estimate);
            }).ToArray();
            var viable = estimated
                .Where(item => !item.Estimate.IsSelfLethal && item.Estimate.Score > 0d)
                .OrderByDescending(item => item.Estimate.Score)
                .ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal)
                .FirstOrDefault();
            var skipAction = new LegalAction(
                LegalActionKind.UseProgramSkill, null, null, "跳过组合技能分支");
            var selectedAction = viable.Choice is null ? skipAction : viable.Action;
            var candidates = estimated.Select(item => new AiCandidateScore(
                    item.Action,
                    item.Estimate.IsSelfLethal ? -1000d : item.Estimate.Score,
                    item.Estimate.IsSelfLethal
                        ? "公开效果将令自身失去全部体力。"
                        : item.Estimate.Approximation))
                .Append(new AiCandidateScore(skipAction, 0d, "保留当前状态。"))
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();
            AddThought(new AiThoughtRecord(
                _thoughtSequence++, _turnNumber, decision.PlayerSeat,
                selectedAction.Description, candidates,
                "schema23互斥组使用同一操作目录与公开上下文估值。"));
            return viable.Choice ?? skip;
        }
        foreach (var item in choices)
        {
            var (activate, thought) = _aiBrains[decision.PlayerSeat].ChooseDrawPhaseProgramActivation(
                CreateSnapshot(decision.PlayerSeat), item.Trigger, CreateSkillContext(owner), normalDrawCount,
                item.Skill.Name, _thoughtSequence++);
            AddThought(thought);
            if (activate) return item.Choice;
        }
        return skip;
    }

    private ProgramAiPublicContext CreateProgramAiPublicContext(
        SkillProgramTrigger trigger,
        CharacterState owner,
        int ownerSeat)
    {
        if (trigger.Window is SkillProgramTriggerWindow.CardUseCommitted or
            SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
            SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted)
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
                CardActionActorIsOwner = parent.Action.ActorSeat == ownerSeat,
                CardUseDebitActive = IsCardUseDebitActive(parent.Action.ActionId)
            };
        }
        if (trigger.Window != SkillProgramTriggerWindow.DrawPhaseStarting)
            return CreateProgramAiPublicContext(owner);
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
            drawPhase.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
            drawPhase.OwnerSeat != ownerSeat)
            throw new InvalidOperationException("A schema23 draw-phase estimate lost its lifecycle parent.");
        return CreateProgramAiPublicContext(owner) with
        {
            NormalDrawCount = checked(GetTurnDrawCount(owner) + drawPhase.NormalDrawAdjustment),
            ReplacesNormalDraw = trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement
        };
    }

    private ProgramAiPublicContext CreateProgramAiPublicContext(CharacterState owner) =>
        new(GetLivingFactionCount(), CanUseSlashOnOther:
            !HasTurnCardTargetRestriction(owner.Seat, SkillProgramCardTargetRestriction.SelfOnly) &&
            new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }
                .Any(kind => !IsCardUseForbidden(owner.Seat, kind, CardActionType.Use)));

    private ProgramAiPublicContext WithProgramConditionFacts(ProgramAiPublicContext context,
        CharacterState owner, string skillId, string skillInstanceId)
    {
        var cardFrame = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault();
        var cardFacts = cardFrame is not null && cardFrame.CandidateIndex < cardFrame.Candidates.Count
            ? cardFrame.Candidates[cardFrame.CandidateIndex].FrozenContext?.Facts
            : null;
        var facts = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault()?.Facts ??
                    _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault()?.Facts ??
                    cardFacts;
        return context with
        {
            BooleanState = stateId => facts is null
                ? GetProgramBooleanState(owner.Seat, skillId, skillInstanceId, stateId)
                : facts.GetBooleanState(skillId, skillInstanceId, stateId)
        };
    }

    private (ProgramAiEstimate Estimate, int? TargetSeat) EstimateCompositionForAi(
        CharacterState owner, IEnumerable<SkillProgramEffect> sourceEffects,
        ProgramAiPublicContext publicContext, IReadOnlyList<int>? publishedTargets = null)
    {
        var effects = sourceEffects.ToArray();
        var selection = effects.FirstOrDefault(effect => effect.Op == SkillProgramEffectOp.SelectTarget);
        var targets = publishedTargets ?? (selection?.TargetKind is { } kind
            ? GetProgramTargetSeats(owner.Seat, kind) : Array.Empty<int>());
        var context = CreateSkillContext(owner);
        if (targets.Count == 0)
            return (ProgramCompositionAi.Estimate(effects, context, owner.IsFaceDown, publicContext), null);
        var snapshot = CreateSnapshot(owner.Seat);
        return targets.Select(targetSeat =>
            {
                var estimate = ProgramCompositionAi.Estimate(effects, context, owner.IsFaceDown,
                    publicContext with { SelectedTarget = CreateSkillContext(_players[targetSeat]) });
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
                PindianWon = bind => frame.PindianResultBindings.Single(item => item.Name == bind).SourceWon
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
        if (aiOrder != SkillProgramTargetAiOrder.HostileThenHandCount)
            throw new InvalidOperationException("Unsupported configured target AI order.");
        var (choiceId, thought) = _aiBrains[decision.PlayerSeat].ChooseHostileHandTargets(
            CreateSnapshot(decision.PlayerSeat), decision.Choices, _thoughtSequence++);
        AddThought(thought);
        return decision.Choices.Single(choice => choice.Id == choiceId);
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
            case SkillProgramTriggerWindow.SelfDyingResponse:
                CompleteDyingProgramBinding(frame, completed);
                break;
            case SkillProgramTriggerWindow.AfterDamageApplied:
                if (_pendingDamageTrigger is not { } damage || damage.FrameId != context.ParentFrameId)
                    throw new InvalidOperationException("The damage program lost its parent window.");
                AdvanceDamageTriggerCandidate(damage);
                break;
            case SkillProgramTriggerWindow.CardsMoved:
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
            case SkillProgramTriggerWindow.CardUseCommitted:
            case SkillProgramTriggerWindow.CardUseBeforeTargetEffects:
            case SkillProgramTriggerWindow.CardUseTargetsFinalized:
            case SkillProgramTriggerWindow.CardResponseAccepted:
                if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame cardAction ||
                    cardAction.Id != context.ParentFrameId ||
                    !cardAction.Candidates[cardAction.CandidateIndex].UsesSharedExecutor)
                    throw new InvalidOperationException("The card-action program lost its parent cursor.");
                AdvanceProgramCardCandidate(cardAction);
                ContinueProgramCardWindow();
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
        if (dying.FrameId != frame.WindowContext!.ParentFrameId || dying.VictimSeat != frame.OwnerSeat)
            throw new InvalidOperationException("The dying program returned to the wrong victim.");
        var victim = _players[dying.VictimSeat];
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

    private ProgramSkillWindowContext CreateSelfDyingProgramContext(
        DyingResolution dying,
        ProgramTriggerCandidate candidate) =>
        new(
            SkillProgramTriggerWindow.SelfDyingResponse,
            dying.FrameId,
            candidate.OwnerSeat,
            SourceSeat: dying.Attack?.SourceSeat,
            TargetSeat: dying.VictimSeat,
            DamageFrameId: dying.DamageFrameId,
            OccurrenceIndex: candidate.OccurrenceIndex);

    private IReadOnlyList<ProgramTriggerCandidate> GetSelfDyingProgramCandidates(
        CharacterState responder,
        DyingResolution dying) =>
        responder.Seat != dying.VictimSeat
            ? []
            : CollectProgramTriggerCandidates(responder, SkillProgramTriggerWindow.SelfDyingResponse)
                .Where(candidate => CanRunProgramTrigger(
                    candidate,
                    CreateSelfDyingProgramContext(dying, candidate)))
                .ToArray();

    private void BeginSelfDyingProgramBinding(ProgramTriggerCandidate candidate, DyingResolution dying) =>
        BeginProgramBinding(candidate, CreateSelfDyingProgramContext(dying, candidate));

    private ProgramSkillWindowContext CreateAfterDamageProgramContext(
        DamageTriggerResolution damage,
        ProgramTriggerCandidate candidate) =>
        new(
            SkillProgramTriggerWindow.AfterDamageApplied,
            damage.FrameId,
            candidate.OwnerSeat,
            SourceSeat: damage.Attack.SourceSeat,
            TargetSeat: damage.Attack.TargetSeat,
            DamageFrameId: damage.DamageFrameId,
            Amount: damage.Attack.DamageAmount,
            OccurrenceIndex: candidate.OccurrenceIndex);
}
