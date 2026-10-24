namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiHjGongmou = "mobile:shi-gongmou", ShiHjZhengshuo = "mobile:shi-zhengshuo";
    private const string ShiHjExchange = "skill-program.mobile-shi-huan-jie.exchange",
        ShiHjDiscard = "skill.mobile-shi-huan-jie.discard", ShiHjDraw = "skill-program.mobile-shi-huan-jie.draw";
    private static CardConversionSource ShiHjSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class MobileShiHuanJieHost(GameEngine engine) : IMobileShiHuanJieHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.BeginShiHj(frame, op); }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 48300)]
    private bool CanOfferShiHj(ProgramTriggerLegalityContext q) => q.SkillId != ShiHjGongmou ||
        _players.Any(p => p.IsAlive && p.Seat != q.OwnerSeat);

    private SkillProgramStepOutcome BeginShiHj(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        var exchange = op == SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou;
        if (f.InstructionIndex != 1 || f.MobileShiHuanJie is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.SkillId != (exchange ? ShiHjGongmou : ShiHjZhengshuo) || f.OwnerSeat != _currentSeat ||
            exchange && (f.TriggerId != "preparation-exchange" || f.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) ||
            !exchange && (f.TriggerId is not null || f.ActivationId != "reset-all-hands" || _phase != TurnPhase.Play))
            throw new InvalidOperationException("Shi Huan Jie lost its original preparation or once-paid limited Play activation.");
        var r = new MobileShiHuanJieReceipt(ShiHjSource(f), f.GameplayHash, _turnNumber, _currentSeat,
            f.WindowContext?.ParentFrameId ?? f.ParentFrameId ?? 0, op,
            exchange ? MobileShiHuanJieStage.Target : MobileShiHuanJieStage.DiscardChildren)
            { Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r });
        AdvanceEventRulesAndQueueFact(new MobileShiHuanJieStartedEvent(f.Id, r.Source, r.GameplayHash,
            r.ActualTurn, r.ActualTurnOwner, r.ParentId, op));
        if (exchange) { PublishShiHj(f); return SkillProgramStepOutcome.AwaitChoice; }
        var recipients = _players.Where(p => p.IsAlive).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)).Select(p => p.Seat).ToArray();
        var materials = recipients.SelectMany(seat => GetSelfDiscardableOwnedHand(_players[seat]).Select(card =>
            new MobileShiHuanJieMaterial(card.Id, card.Kind, CardLocation.Hand(seat), CardLocation.DiscardPile))).ToArray();
        ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Recipients = recipients, Materials = materials }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        // All players lose their original hand in one physical rules batch. Each
        // owner still receives its own native logical-discard invoice.
        var drafts = recipients.Select(seat => CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, seat,
            materials.Where(m => m.From.OwnerSeat == seat).Select(m => m.CardId).ToArray(), new(ShiHjDiscard), f.Id)).ToArray();
        if (materials.Length > 0) MoveProgramCardsFromMultipleSources(materials.Select(m => m.CardId).ToArray(),
            CardLocation.DiscardPile, new(ShiHjDiscard), disclosure: CardMovementDisclosure.FaceUp);
        foreach (var draft in drafts) CompleteNativeLogicalDiscardInvocation(draft);
        SealShiHj(f.Id); ContinueShiHj(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> ShiHjChoices(ProgramSkillFrame f) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)
        .Select(p => new PromptChoice(new($"shi-huan-jie.{f.Id}.target.{p.Seat}"), $"与 {p.Name} 交换全部手牌（其有{GetHand(p).Count}张）",
            [], [p.Seat], new Dictionary<string, string> { ["program-action"] = "mobile-shi-huan-jie", ["branch"] = "target" })).ToArray());
    private void PublishShiHj(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = ShiHjChoices(f);
        if (choices.Count == 0) { FinishShiHj(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "共谋：选择交换全部手牌的其他角色。", [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mobile-shi-huan-jie")]
    private void AnswerShiHj(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiHuanJie.Stage: MobileShiHuanJieStage.Target } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShiHjChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Gongmou requires its exact current living target choice.");
        RequireShiHj(f); ClearPendingDecision();
        var target = choice.Targets.Single(); var ownerHand = GetHand(_players[f.OwnerSeat]).ToArray(); var targetHand = GetHand(_players[target]).ToArray();
        var materials = ownerHand.Select(c => new MobileShiHuanJieMaterial(c.Id, c.Kind, CardLocation.Hand(f.OwnerSeat), CardLocation.Hand(target)))
            .Concat(targetHand.Select(c => new MobileShiHuanJieMaterial(c.Id, c.Kind, CardLocation.Hand(target), CardLocation.Hand(f.OwnerSeat)))).ToArray();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MobileShiHuanJie = f.MobileShiHuanJie! with
            { Stage = MobileShiHuanJieStage.ExchangeChildren, Target = target, Materials = materials, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MobileShiHuanJieTargetChosenEvent(f.Id, target, ownerHand.Length, targetHand.Length));
        if (materials.Length > 0) MoveProgramCardsFromMultipleSources(materials.Select(m => m.CardId).ToArray(), CardLocation.Hand(target),
            new(ShiHjExchange), destinationForCard: (_, source) => CardLocation.Hand(source.OwnerSeat == f.OwnerSeat ? target : f.OwnerSeat),
            disclosure: CardMovementDisclosure.FaceDown);
        SealShiHj(f.Id); ContinueShiHj(f.Id); AdvanceRulesAndPublishState();
    }

    private void SealShiHj(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MobileShiHuanJie!;
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= _movementSequence).ToArray();
        var primary = moves.Where(m => r.Materials.Any(p => p.CardId == m.CardId && p.From == m.From)).ToArray();
        foreach (var move in primary) AdvanceEventRulesAndQueueFact(new MobileShiHuanJieMaterialPaidEvent(id, r.Stage,
            r.Materials.Single(m => m.CardId == move.CardId), move.Sequence));
        var batch = _pendingCardsMovedBatches.SingleOrDefault(b => b.ParentFrameId == id && b.ParentBatchId is null &&
            primary.Any(m => b.Movements.Contains(m)))?.Id;
        ReplaceRuntimeTop(f with { MobileShiHuanJie = r with { After = _movementSequence, BatchId = batch } });
        AdvanceEventRulesAndQueueFact(new MobileShiHuanJieMovementPaidEvent(id, r.Stage, r.Cursor, batch, r.Before, _movementSequence, primary.Length));
    }

    private void GrantShiHjPaidTurnSkills(ProgramSkillFrame f)
    {
        RequireShiHj(f); var r = f.MobileShiHuanJie!;
        if (r.Operation != SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou || r.Stage != MobileShiHuanJieStage.GrantChildren ||
            f.PendingMovementContinuation is not null || !IsValidPlayerSeat(r.Target) ||
            ProgramEventHistory<MobileShiHuanJieTargetChosenEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == r.Target) != 1 ||
            ProgramEventHistory<MobileShiHuanJieMovementPaidEvent>().Count(e => e.FrameId == f.Id && e.Stage == MobileShiHuanJieStage.ExchangeChildren &&
                e.Before == r.Before && e.After == r.After && e.BatchId == r.BatchId) != 1 ||
            ProgramEventHistory<ProgramTurnSkillsGrantedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Gongmou cannot grant before its exact complete real-hand exchange or repeat its paid grants.");
        var expiry = new SkillGrantTurnExpiry(r.ActualTurn, r.ActualTurnOwner);
        GrantIssuedProgramTurnSkills(f, f.OwnerSeat, ["classic:qice"], expiry);
        GrantIssuedProgramTurnSkills(GetActiveProgramFrame(f.Id), r.Target, ["classic:kanpo"], expiry);
    }

    private bool DrainShiHj(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueShiHj(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiHuanJie: { } r } f || f.Id != id || _pendingDecision is not null) return;
        RequireShiHj(f);
        if (r.Stage == MobileShiHuanJieStage.Target) { PublishShiHj(f); return; }
        if (r.Stage is MobileShiHuanJieStage.Drawing or MobileShiHuanJieStage.Complete) return;
        if (DrainShiHj(id)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.MobileShiHuanJie!;
        if (r.Stage == MobileShiHuanJieStage.ExchangeChildren)
        {
            // The exchange already issued both grants. Losing Gongmou during
            // its paid child does not revoke that original turn obligation.
            ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Stage = MobileShiHuanJieStage.GrantChildren } });
            GrantShiHjPaidTurnSkills(f);
            ContinueShiHj(id); return;
        }
        if (r.Stage == MobileShiHuanJieStage.GrantChildren) { FinishShiHj(f); return; }
        if (r.Stage == MobileShiHuanJieStage.DiscardChildren)
        {
            var materials = _cardZones.CardsAt(CardLocation.DiscardPile).Select(c => new MobileShiHuanJieMaterial(c.Id, c.Kind, CardLocation.DiscardPile, CardLocation.DrawPile)).ToArray();
            ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Stage = MobileShiHuanJieStage.ShuffleChildren,
                Materials = materials, Before = _movementSequence, After = _movementSequence, BatchId = null }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveAllCards(CardLocation.DiscardPile, CardLocation.DrawPile, CardMoveReasons.Reshuffle);
            var before = _cardZones.CardsAt(CardLocation.DrawPile).Select(c => c.Id).ToArray(); var random = _random.State;
            _cardZones.Shuffle(CardLocation.DrawPile, _random);
            var after = _cardZones.CardsAt(CardLocation.DrawPile).Select(c => c.Id).ToArray();
            f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f = f with { MobileShiHuanJie = f.MobileShiHuanJie! with
                { ShuffleBefore = before, ShuffleAfter = after, RandomBefore = random, RandomAfter = _random.State } });
            AdvanceEventRulesAndQueueFact(new MobileShiHuanJieShuffledEvent(id, random, _random.State, before.Length)); SealShiHj(id);
            ContinueShiHj(id); return;
        }
        if (r.Stage == MobileShiHuanJieStage.DrawChildren)
        {
            ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Cursor = r.Cursor + 1, Draw = null } }); r = f.MobileShiHuanJie!;
        }
        if (r.Stage is MobileShiHuanJieStage.ShuffleChildren or MobileShiHuanJieStage.DrawChildren)
        {
            while (r.Cursor < r.Recipients.Count && !_players[r.Recipients[r.Cursor]].IsAlive)
            { ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Cursor = r.Cursor + 1 } }); r = f.MobileShiHuanJie!; }
            if (r.Cursor >= r.Recipients.Count || _winner != Winner.None) { FinishShiHj(f); return; }
            ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Stage = MobileShiHuanJieStage.Drawing, Draw = null,
                Materials = [], Before = _movementSequence, After = _movementSequence, BatchId = null } });
            BeginOperationProgramDraw(f, r.Recipients[r.Cursor], 4, new(ShiHjDraw));
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiHuanJieZhengshuo)]
    private void ReturnShiHjDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MobileShiHuanJie is not { Stage: MobileShiHuanJieStage.Drawing, Draw: null } r || r.Cursor >= r.Recipients.Count ||
            outcome.RecipientSeat != r.Recipients[r.Cursor] || outcome.RequestedCount != 4)
            throw new InvalidOperationException("Zhengshuo draw lost its once-issued original recipient and four-card request.");
        ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Stage = MobileShiHuanJieStage.DrawChildren, Draw = outcome,
            Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MobileShiHuanJieDrawCompletedEvent(f.Id, r.Cursor, outcome.RecipientSeat, outcome.ActualDrawIds.Count, outcome.Replacement is not null));
        ContinueShiHj(f.Id);
    }
    private void FinishShiHj(ProgramSkillFrame f)
    {
        var r = f.MobileShiHuanJie!;
        ReplaceRuntimeTop(f = f with { MobileShiHuanJie = r with { Stage = MobileShiHuanJieStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new MobileShiHuanJieCompletedEvent(f.Id, r.Operation, r.Target)); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 48300)]
    private bool ResumeShiHj(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiHuanJie: not null } f || f.Id != id) return false; ContinueShiHj(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 48300)]
    private bool ReturnShiHjMovement(ProgramSkillFrame f)
    { if (f.MobileShiHuanJie is null) return false; ContinueShiHj(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 48300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 48300)]
    private bool KeepPaidShiHj(ProgramSkillFrame f) => f.MobileShiHuanJie is { Stage: not (MobileShiHuanJieStage.Target or MobileShiHuanJieStage.Complete) };
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 48300)]
    private bool ShiHjMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.MobileShiHuanJie is { } r &&
        effect?.Op == r.Operation && r.Stage is MobileShiHuanJieStage.ExchangeChildren or MobileShiHuanJieStage.DiscardChildren or MobileShiHuanJieStage.ShuffleChildren or MobileShiHuanJieStage.DrawChildren &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou)]
    private PromptChoice ChooseShiHjAi(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderByDescending(c => GetHand(_players[c.Targets.Single()]).Count).First();

    private void RequireShiHj(ProgramSkillFrame f)
    {
        var r = f.MobileShiHuanJie ?? throw new InvalidOperationException("Shi Huan Jie lost its owning receipt.");
        if (r.Source != ShiHjSource(f) || r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            f.InstructionIndex != 1 || r.ActualTurn != _turnNumber || r.ActualTurnOwner != _currentSeat || f.OwnerSeat != _currentSeat ||
            r.ParentId != (f.WindowContext?.ParentFrameId ?? f.ParentFrameId ?? 0) || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<MobileShiHuanJieStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.ActualTurn == r.ActualTurn && e.ActualTurnOwner == r.ActualTurnOwner && e.ParentId == r.ParentId && e.Operation == r.Operation) != 1 ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Cursor < 0 || r.Cursor > r.Recipients.Count ||
            r.Recipients.Distinct().Count() != r.Recipients.Count || r.Recipients.Any(seat => !IsValidPlayerSeat(seat)) ||
            r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count ||
            r.Operation == SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou && r.Stage != MobileShiHuanJieStage.Target && (!IsValidPlayerSeat(r.Target) || r.Target == f.OwnerSeat))
            throw new InvalidOperationException("Shi Huan Jie changed its original source, paid instruction, actual turn or native material cursor.");
        if (r.ShuffleBefore.Count > 0 || ProgramEventHistory<MobileShiHuanJieShuffledEvent>().Any(e => e.FrameId == f.Id))
        {
            var order = r.ShuffleBefore.ToList(); var random = new DeterministicRandom(unchecked((int)r.RandomBefore)); random.Shuffle(order);
            if (!order.SequenceEqual(r.ShuffleAfter) || random.State != r.RandomAfter ||
                ProgramEventHistory<MobileShiHuanJieShuffledEvent>().Count(e => e.FrameId == f.Id && e.RandomBefore == r.RandomBefore && e.RandomAfter == r.RandomAfter && e.DeckCount == order.Count) != 1)
                throw new InvalidOperationException("Zhengshuo lost its once-issued real complete-deck shuffle and deterministic RNG.");
        }
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 48300)]
    private void AssertShiHj(ProgramSkillFrame f) { if (f.MobileShiHuanJie is not null) RequireShiHj(f); }
}
