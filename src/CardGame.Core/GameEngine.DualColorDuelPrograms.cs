using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardMoveReason DualColorDiscardReason = new("skill-program.dual-color.discard");

    private sealed partial class ProgramSkillHost : IDualColorDuelProgramHost
    {
        public SkillProgramStepOutcome ChoosePrivateColorsDiscardAndDuel(ProgramSkillFrame frame) =>
            engine.BeginDualColorDuel(frame);
    }

    private bool DualColorSourceCurrent(ProgramSkillFrame frame) =>
        _winner == Winner.None && _players[frame.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId);

    private bool DualColorDraftMatches(ProgramSkillFrame frame)
    {
        if (frame.DualColorDuel is not { } draft || frame.TriggerId is not null || frame.WindowContext is not null ||
            frame.InstructionIndex != draft.InstructionIndex || draft.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats is not [var other] || other != draft.OtherSeat ||
            other == frame.OwnerSeat || !IsValidPlayerSeat(other) || draft.TurnNumber != _turnNumber ||
            string.IsNullOrWhiteSpace(frame.SkillId) || string.IsNullOrWhiteSpace(frame.ActivationId) ||
            string.IsNullOrWhiteSpace(frame.SkillInstanceId) || string.IsNullOrWhiteSpace(frame.GameplayHash) ||
            _phase != TurnPhase.Play || _currentSeat != frame.OwnerSeat)
            return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        return plan.Instructions is [{ Op: SkillProgramEffectOp.ChoosePrivateColorsDiscardAndDuel }] &&
            plan.Activation is { MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
                TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerPhase: 1, UsesPerTurn: null };
    }

    private SkillProgramStepOutcome BeginDualColorDuel(ProgramSkillFrame input)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (frame.DualColorDuel is not null || frame.SelectedTargetSeats is not [var other])
            throw new InvalidOperationException("Private color Duel lost its single fresh activation target.");
        ReplaceRuntimeTop(frame with { DualColorDuel = new(frame.InstructionIndex, other, _turnNumber,
            ProgramDualColorDuelStage.ChoosingOwner, null, null, Array.AsReadOnly(Array.Empty<int>()),
            Array.AsReadOnly(Array.Empty<int>())) });
        frame = GetActiveProgramFrame(frame.Id);
        if (!DualColorDraftMatches(frame))
            throw new InvalidOperationException("Private color Duel requires its exact standalone phase-limited owner.");
        if (!DualColorSourceCurrent(frame) || !_players[other].IsAlive)
        {
            CancelDualColorDuel(frame);
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishDualColorChoice(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private int DualColorChooser(ProgramSkillFrame frame) => frame.DualColorDuel!.Stage switch
    {
        ProgramDualColorDuelStage.ChoosingOwner => frame.OwnerSeat,
        ProgramDualColorDuelStage.ChoosingOther => frame.DualColorDuel.OtherSeat,
        _ => throw new InvalidOperationException("Private color selection has already been committed.")
    };

    private void PublishDualColorChoice(ProgramSkillFrame frame)
    {
        var chooser = DualColorChooser(frame);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var choices = Array.AsReadOnly(new[] { true, false }.Select(red =>
            new PromptChoice(new ChoiceId($"dual-color.frame-{frame.Id}.seat-{chooser}.{(red ? "red" : "black")}"),
                red ? "红色" : "黑色", [], [], new Dictionary<string, string>
                {
                    ["program-action"] = "dual-color-choice", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
                    ["chooser-seat"] = chooser.ToString(CultureInfo.InvariantCulture), ["color"] = red ? "red" : "black"
                })).ToArray());
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"【{skill.Name}】私密选择要弃置的手牌颜色。", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
          SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 选择颜色", skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveDualColorChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("Private color selection lost its owning program.");
        if (!DualColorDraftMatches(frame) || frame.DualColorDuel is not { } draft ||
            draft.Stage is not (ProgramDualColorDuelStage.ChoosingOwner or ProgramDualColorDuelStage.ChoosingOther) ||
            _pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } prompt ||
            prompt.PlayerSeat != DualColorChooser(frame) || !prompt.Choices.Any(choice => choice.Id == selected.Id) ||
            selected.Cards.Count != 0 || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("chooser-seat") != prompt.PlayerSeat.ToString(CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("color") is not ("red" or "black"))
            throw new InvalidOperationException("Invalid private color commitment.");
        if (!DualColorSourceCurrent(frame) || !_players[draft.OtherSeat].IsAlive)
        {
            ClearPendingDecision(); CancelDualColorDuel(frame); return;
        }
        var red = selected.Parameters["color"] == "red";
        ClearPendingDecision();
        if (draft.Stage == ProgramDualColorDuelStage.ChoosingOwner)
        {
            ReplaceRuntimeTop(frame with { DualColorDuel = draft with
                { OwnerIsRed = red, Stage = ProgramDualColorDuelStage.ChoosingOther } });
            // No public option fact or log exposes the first commitment.
            PublishDualColorChoice(GetActiveProgramFrame(frame.Id));
            return;
        }
        var ownerCards = DualColorHandIds(frame.OwnerSeat, draft.OwnerIsRed!.Value);
        var otherCards = DualColorHandIds(draft.OtherSeat, red);
        draft = draft with { OtherIsRed = red, OwnerCardIds = ownerCards, OtherCardIds = otherCards,
            Stage = ProgramDualColorDuelStage.PaymentChildren, PaymentStartSequence = _movementSequence };
        ReplaceRuntimeTop(frame with { DualColorDuel = draft });
        AdvanceEventRulesAndQueueFact(new ProgramDualColorsCommittedEvent(frame.Id, frame.SkillId, frame.OwnerSeat,
            draft.OtherSeat, draft.OwnerIsRed.Value, red));
        PayDualColorHands(GetActiveProgramFrame(frame.Id));
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame with { DualColorDuel = frame.DualColorDuel! with { PaymentEndSequence = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ProgramDualColorCardsDiscardedEvent(frame.Id, frame.SkillId, frame.OwnerSeat,
            draft.OtherSeat, ownerCards.Count, otherCards.Count));
        AdvanceRuntimeProgram(frame.Id);
    }

    private IReadOnlyList<int> DualColorHandIds(int seat, bool red) => Array.AsReadOnly(GetHand(_players[seat])
        .Where(card => SuitColor(EffectiveSuit(_players[seat], card)) == red).Select(card => card.Id).ToArray());

    private void PayDualColorHands(ProgramSkillFrame frame)
    {
        var draft = frame.DualColorDuel!;
        var ownerFrom = CardLocation.Hand(frame.OwnerSeat); var otherFrom = CardLocation.Hand(draft.OtherSeat);
        var batch = BeginCardMovementBatch([ownerFrom, otherFrom], [CardLocation.DiscardPile]);
        var movements = new List<CardMovementRecord>(); var committed = false;
        try
        {
            // Both sets were frozen before either hand changes. Move both sets
            // before emitting rules facts, and expose one mixed-owner batch.
            // This producer accepts Hand -> Discard only. Equipment removal,
            // judgment cleanup and discard-phase collection cannot apply here.
            var ownerCards = _cardZones.MoveMany(draft.OwnerCardIds, ownerFrom, CardLocation.DiscardPile);
            var otherCards = _cardZones.MoveMany(draft.OtherCardIds, otherFrom, CardLocation.DiscardPile);
            foreach (var card in ownerCards) movements.Add(RecordMovement(card, ownerFrom, CardLocation.DiscardPile, DualColorDiscardReason));
            foreach (var card in otherCards) movements.Add(RecordMovement(card, otherFrom, CardLocation.DiscardPile, DualColorDiscardReason));
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, movements, committed); }
    }

    private bool DualColorPaymentMatches(ProgramSkillFrame frame)
    {
        var draft = frame.DualColorDuel!;
        if (draft.OwnerIsRed is null || draft.OtherIsRed is null || draft.PaymentStartSequence < 0 ||
            draft.PaymentEndSequence < draft.PaymentStartSequence ||
            draft.OwnerCardIds.Distinct().Count() != draft.OwnerCardIds.Count ||
            draft.OtherCardIds.Distinct().Count() != draft.OtherCardIds.Count || draft.OwnerCardIds.Intersect(draft.OtherCardIds).Any()) return false;
        bool Exact(int seat, IReadOnlyList<int> ids) => _cardMovements.Where(move =>
                move.Sequence > draft.PaymentStartSequence && move.Sequence <= draft.PaymentEndSequence &&
                move.From == CardLocation.Hand(seat) && move.To == CardLocation.DiscardPile && move.Reason == DualColorDiscardReason)
            .Select(move => move.CardId).Order().SequenceEqual(ids.Order());
        return Exact(frame.OwnerSeat, draft.OwnerCardIds) && Exact(draft.OtherSeat, draft.OtherCardIds);
    }

    private bool ResumeDualColorDuel(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.DualColorDuel is not { } draft)
            return false;
        if (!DualColorDraftMatches(frame)) throw new InvalidOperationException("Private color Duel lost its exact owning instruction.");
        if (draft.Stage is ProgramDualColorDuelStage.ChoosingOwner or ProgramDualColorDuelStage.ChoosingOther)
            return true;
        if (draft.Stage == ProgramDualColorDuelStage.DuelIssued)
            throw new InvalidOperationException("An issued private color Duel requires its typed card-use or attack return.");
        if (!DualColorPaymentMatches(frame)) throw new InvalidOperationException("Private color Duel lost its exact once-only hand payment ledger.");
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        if (!DualColorSourceCurrent(frame) || !_players[draft.OtherSeat].IsAlive)
        { CancelDualColorDuel(frame); return true; }
        if (draft.OwnerCardIds.Count == draft.OtherCardIds.Count)
        { FinishDualColorDuel(frame); return true; }
        var actor = draft.OwnerCardIds.Count > draft.OtherCardIds.Count ? frame.OwnerSeat : draft.OtherSeat;
        var target = actor == frame.OwnerSeat ? draft.OtherSeat : frame.OwnerSeat;
        if (!CanIssueSelectedActorDuel(target, actor)) { FinishDualColorDuel(frame); return true; }
        var useId = _resolutionSequence + 1;
        var origin = new ProgramDualColorDuelOrigin(frame.Id, draft.InstructionIndex, useId, frame.OwnerSeat, draft.OtherSeat,
            actor, target, frame.SkillId, frame.ActivationId, frame.SkillInstanceId, frame.GameplayHash, draft.TurnNumber,
            draft.OwnerCardIds.Count, draft.OtherCardIds.Count, draft.PaymentStartSequence, draft.PaymentEndSequence);
        ReplaceRuntimeTop(frame with { DualColorDuel = draft with { Stage = ProgramDualColorDuelStage.DuelIssued, CardUseFrameId = useId } });
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        var actualUseId = BeginCardUse(card, actor, [target], CardKind.Duel, physicalCardIds: [], dualColorDuelOrigin: origin);
        if (actualUseId != useId) throw new InvalidOperationException("Private color Duel lost its reserved exact Use identity.");
        AdvanceEventRulesAndQueueFact(new ProgramDualColorDuelIssuedEvent(frame.Id, useId, frame.SkillId, frame.OwnerSeat,
            actor, target, draft.OwnerCardIds.Count, draft.OtherCardIds.Count));
        BeginJizhiOrNullificationWindow(useId, card, actor, [target], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        return true;
    }

    private bool IsDualColorDuelUse(long id) => LifecycleCardUse(id) is
        { DualColorDuelOrigin: not null, SelectedActorDuelOrigin: null, DamageTargetDuelOrigin: null,
          CardId: 0, CardKind: CardKind.Duel, PhysicalCardIds.Count: 0 };

    private bool MatchesDualColorDuelParent(ProgramSkillFrame frame, ProgramDualColorDuelOrigin origin) =>
        DualColorDraftMatches(frame) && DualColorPaymentMatches(frame) && frame.DualColorDuel is
            { Stage: ProgramDualColorDuelStage.DuelIssued, CardUseFrameId: { } id } draft && id == origin.CardUseFrameId &&
        frame.Id == origin.ParentProgramFrameId && frame.InstructionIndex == origin.InstructionIndex &&
        frame.OwnerSeat == origin.OwnerSeat && draft.OtherSeat == origin.OtherSeat &&
        frame.SkillId == origin.SkillId && frame.ActivationId == origin.BindingId && frame.SkillInstanceId == origin.SkillInstanceId &&
        frame.GameplayHash == origin.GameplayHash && draft.TurnNumber == origin.TurnNumber &&
        draft.OwnerCardIds.Count == origin.OwnerDiscardCount && draft.OtherCardIds.Count == origin.OtherDiscardCount &&
        draft.PaymentStartSequence == origin.PaymentStartSequence && draft.PaymentEndSequence == origin.PaymentEndSequence &&
        origin.OwnerDiscardCount != origin.OtherDiscardCount &&
        origin.InitialActorSeat == (origin.OwnerDiscardCount > origin.OtherDiscardCount ? frame.OwnerSeat : draft.OtherSeat) &&
        origin.InitialTargetSeat == (origin.InitialActorSeat == frame.OwnerSeat ? draft.OtherSeat : frame.OwnerSeat);

    private void ReturnDualColorDuel(CardUseFrame use)
    {
        if (use.DualColorDuelOrigin is not { AttackStarted: false } origin) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || use.Id != origin.CardUseFrameId ||
            !MatchesDualColorDuelParent(frame, origin)) throw new InvalidOperationException("Private color Duel lost its exact no-attack typed return.");
        FinishDualColorDuel(frame);
    }

    private void CompleteDualColorDuelAttackReturn(ProgramSkillFrame frame, AttackCompletionReceipt completion)
    {
        if (completion.DualColorDuelReturn is not { AttackStarted: true } origin ||
            completion.ProgramFrameId != frame.Id || completion.ResolutionId != origin.CardUseFrameId ||
            !MatchesDualColorDuelParent(frame, origin)) throw new InvalidOperationException("Private color Duel lost its exact completed attack return.");
        FinishDualColorDuel(frame);
    }

    private void FinishDualColorDuel(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame with { DualColorDuel = null });
        if (!DualColorSourceCurrent(GetActiveProgramFrame(frame.Id)))
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "选色弃牌已结算，技能来源或游戏已结束。");
        else AdvanceRuntimeProgram(frame.Id);
    }

    private void CancelDualColorDuel(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame with { DualColorDuel = null });
        CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "选色结算的参与者或技能来源已失效，未发行的决斗取消。");
    }

    private PromptChoice SelectAiDualColorChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var chooser = decision.PlayerSeat; var other = chooser == frame.OwnerSeat ? frame.DualColorDuel!.OtherSeat : frame.OwnerSeat;
        var snapshot = CreateSnapshot(chooser);
        var hostility = _aiBrains[chooser].ScoreProgramTarget(snapshot, other, new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false));
        var expectedOtherCount = snapshot.Players.Single(player => player.Seat == other).HandCount / 2d;
        var own = GetHand(_players[chooser]);
        return decision.Choices.OrderByDescending(choice =>
        {
            var red = choice.Parameters["color"] == "red";
            var count = own.Count(card => SuitColor(EffectiveSuit(_players[chooser], card)) == red);
            var retainedSlash = own.Count(card => IsSlashCard(card.Kind) && SuitColor(EffectiveSuit(_players[chooser], card)) != red);
            return -count * 1.4d + (hostility > 0 ? count > expectedOtherCount ? 5d : -2d : 0d) + Math.Min(1, retainedSlash) * 2d;
        }).ThenBy(choice => choice.Parameters["color"], StringComparer.Ordinal).First();
    }

    private void AssertDualColorDuels()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(frame => frame.DualColorDuel is not null))
        {
            var draft = frame.DualColorDuel!;
            if (!DualColorDraftMatches(frame) || !Enum.IsDefined(draft.Stage) ||
                draft.Stage == ProgramDualColorDuelStage.ChoosingOwner && (draft.OwnerIsRed is not null || draft.OtherIsRed is not null) ||
                draft.Stage == ProgramDualColorDuelStage.ChoosingOther && (draft.OwnerIsRed is null || draft.OtherIsRed is not null) ||
                draft.Stage is ProgramDualColorDuelStage.ChoosingOwner or ProgramDualColorDuelStage.ChoosingOther &&
                    (draft.OwnerCardIds.Count != 0 || draft.OtherCardIds.Count != 0 || draft.CardUseFrameId is not null) ||
                draft.Stage == ProgramDualColorDuelStage.PaymentChildren && draft.CardUseFrameId is not null ||
                draft.Stage == ProgramDualColorDuelStage.DuelIssued && (draft.CardUseFrameId is not > 0 ||
                    !_resolutionStack.OfType<CardUseFrame>().Any(use => use.Id == draft.CardUseFrameId && use.DualColorDuelOrigin is not null)) ||
                draft.Stage is ProgramDualColorDuelStage.PaymentChildren or ProgramDualColorDuelStage.DuelIssued && !DualColorPaymentMatches(frame))
                throw new InvalidOperationException("Private color Duel has an invalid frozen stage, participant or payment.");
            if (draft.Stage is ProgramDualColorDuelStage.ChoosingOwner or ProgramDualColorDuelStage.ChoosingOther)
            {
                var chooser = DualColorChooser(frame);
                if (_resolutionStack.LastOrDefault()?.Id != frame.Id ||
                    _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
                    prompt.PlayerSeat != chooser || prompt.SkillPrompt?.SkillId != frame.SkillId || prompt.Choices.Count != 2 ||
                    prompt.ValidCardIds.Count != 0 || prompt.ValidTargetSeats.Count != 0 ||
                    !prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("color")).Order()
                        .SequenceEqual(new[] { "black", "red" }) ||
                    prompt.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
                        choice.Id != new ChoiceId($"dual-color.frame-{frame.Id}.seat-{chooser}.{choice.Parameters["color"]}") ||
                        choice.Parameters.GetValueOrDefault("program-action") != "dual-color-choice" ||
                        choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) ||
                        choice.Parameters.GetValueOrDefault("chooser-seat") != chooser.ToString(CultureInfo.InvariantCulture)))
                    throw new InvalidOperationException("Private color Duel lost its exact private chooser or published commitment.");
            }
        }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.DualColorDuelOrigin is not null))
        {
            var origin = use.DualColorDuelOrigin!; var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
            if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame frame || use.Id != origin.CardUseFrameId ||
                !MatchesDualColorDuelParent(frame, origin) || !IsDualColorDuelUse(use.Id) ||
                use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0,
                    ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveIsRed: false })
                throw new InvalidOperationException("Private color Duel has an invalid exact issued zero-entity Use.");
        }
    }
}
