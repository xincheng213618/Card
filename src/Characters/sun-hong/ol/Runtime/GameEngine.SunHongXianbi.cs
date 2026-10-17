namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XianbiDiscardReason = "skill-program.sun-hong.xianbi.discard";
    private const string XianbiDrawReason = "skill-program.sun-hong.xianbi.draw";
    private const string XianbiObtainReason = "skill-program.sun-hong.xianbi.obtain";
    private CardConversionSource XianbiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool SunHongTargetBlocked(int owner, int target) => ProgramEventHistory<ZenrunTargetBlockedEvent>()
        .Any(e => e.OwnerSeat == owner && e.TargetSeat == target && e.Source.OwnerSeat == owner && e.Source.SkillId == "ol:zenrun");
    private bool XianbiCanDiscard(int owner, Card card) => !card.IsGeneralWeapon &&
        !IsSelfHandCategoryDiscardForbidden(owner, card, CardLocation.Hand(owner), OwnedCardMoveIntent.Discard);
    private bool XianbiTargetLegal(int owner, int target)
    {
        if (!IsValidPlayerSeat(owner) || !IsValidPlayerSeat(target) || !_players[owner].IsAlive ||
            !_players[target].IsAlive || SunHongTargetBlocked(owner, target)) return false;
        var hand = GetHand(_players[owner]); var equipment = GetEquipment(_players[target]).Count;
        return equipment != hand.Count && (equipment > hand.Count || hand.Count(c => XianbiCanDiscard(owner, c)) >= hand.Count - equipment);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 27570,
        SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns)]
    private bool CanActivateXianbi(CharacterState owner) => _winner == Winner.None && owner.IsAlive &&
        _phase == TurnPhase.Play && _currentSeat == owner.Seat && _players.Any(p => XianbiTargetLegal(owner.Seat, p.Seat));
    [ProgramCapability]
    private sealed class SunHongXianbiCapability(GameEngine engine) : ISunHongXianbiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginXianbi(frame); }
    private SkillProgramStepOutcome BeginXianbi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Xianbi is not null || f.InstructionIndex != 1 || f.SkillId != "ol:xianbi" ||
            GetProgramBindingId(f) != "adjust-and-return" || f.TriggerId is not null || f.WindowContext is not null ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !CanActivateXianbi(_players[f.OwnerSeat]) ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Xianbi needs its own real live Play activation and an unequal legal role.");
        var r = new XianbiReceipt(XianbiSource(f), f.GameplayHash, _turnNumber, _turnProgression.OwnerSeat,
            _cardUseDebitPhaseInstanceId, XianbiStage.Target) { TargetSeat = f.OwnerSeat };
        ReplaceRuntimeTop(f = f with { Xianbi = r });
        AdvanceEventRulesAndQueueFact(new XianbiStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner, r.PlayPhase));
        PublishXianbi(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice XianbiChoice(ProgramSkillFrame f, string branch, string description, int? card = null, int? target = null) =>
        new(new($"xianbi.{f.Id}.{f.Xianbi!.Stage}.{f.Xianbi.Revision}.{branch}.{card ?? target ?? -1}"), description,
            card is { } id ? [id] : [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "sun-hong-xianbi", ["branch"] = branch });
    private IReadOnlyList<PromptChoice> XianbiChoices(ProgramSkillFrame f)
    {
        var r = f.Xianbi!; var choices = new List<PromptChoice>();
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return choices;
        if (r.Stage == XianbiStage.Target)
            choices.AddRange(_players.Where(p => XianbiTargetLegal(f.OwnerSeat, p.Seat))
                .Select(p => XianbiChoice(f, "target", $"将手牌调整至{p.Name}当前装备数（{GetEquipment(p).Count}张）", target: p.Seat)));
        if (r.Stage == XianbiStage.DiscardChoice)
        {
            if (r.Selected.Count < r.Required)
                foreach (var card in r.Hand.Where(c => !r.Selected.Contains(c.CardId) &&
                    _cardZones.Contains(CardLocation.Hand(f.OwnerSeat), c.CardId) && XianbiCanDiscard(f.OwnerSeat, GetAdvancedCard(c.CardId))))
                    choices.Add(XianbiChoice(f, "pick", "选择弃置【" + GetAdvancedCard(card.CardId).DisplayName + "】", card.CardId));
            foreach (var id in r.Selected) choices.Add(XianbiChoice(f, "unpick", "撤回【" + GetAdvancedCard(id).DisplayName + "】", id));
            if (r.Selected.Count == r.Required) choices.Add(XianbiChoice(f, "pay", $"整次弃置这{r.Required}张手牌"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishXianbi(ProgramSkillFrame f)
    {
        var choices = XianbiChoices(f);
        if (choices.Count == 0) { FinishXianbi(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "险诐：选择角色或本人的整次弃牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("sun-hong-xianbi")]
    private void AnswerXianbi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xianbi: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !XianbiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Xianbi needs its current exact own private choice.");
        AssertXianbi(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == XianbiStage.Target)
        {
            var target = choice.Targets.Single();
            if (branch != "target" || !XianbiTargetLegal(f.OwnerSeat, target)) throw new InvalidOperationException("Xianbi cannot change the accepted target.");
            var hand = GetHand(_players[f.OwnerSeat]).Select(c => new XianbiEntity(c.Id, c.Kind, AdvancedEffectiveHandKind(_players[f.OwnerSeat], c))).ToArray();
            var equipment = GetEquipment(_players[target]).Count;
            r = r with { Issued = true, TargetSeat = target, HandBefore = hand.Length, EquipmentCount = equipment,
                Required = Math.Abs(hand.Length - equipment), Hand = hand,
                Stage = hand.Length > equipment ? XianbiStage.DiscardChoice : XianbiStage.DrawRequest };
            ReplaceRuntimeTop(f = f with { Xianbi = r });
            AdvanceEventRulesAndQueueFact(new XianbiTargetFrozenEvent(f.Id, target, hand.Length, equipment));
            for (var i = 0; i < hand.Length; i++) AdvanceEventRulesAndQueueFact(new XianbiHandEntityEvent(f.Id, i, hand[i].CardId, hand[i].Kind, hand[i].EffectiveKind));
            if (hand.Length > equipment) PublishXianbi(f); else DrawXianbiIncrease(f);
            return;
        }
        if (r.Stage != XianbiStage.DiscardChoice) throw new InvalidOperationException("Xianbi is not selecting a discard.");
        if (branch is "pick" or "unpick")
        {
            var id = choice.Cards.Single(); var selected = branch == "pick" ? r.Selected.Append(id).ToArray() : r.Selected.Where(c => c != id).ToArray();
            ReplaceRuntimeTop(f = f with { Xianbi = r with { Selected = selected, Revision = r.Revision + 1 } });
            AdvanceEventRulesAndQueueFact(new XianbiSelectionChangedEvent(f.Id, r.Revision + 1, id, branch == "pick"));
            PublishXianbi(f); return;
        }
        if (branch != "pay" || r.Selected.Count != r.Required || r.Selected.Any(id =>
            !_cardZones.Contains(CardLocation.Hand(f.OwnerSeat), id) || !XianbiCanDiscard(f.OwnerSeat, GetAdvancedCard(id))))
            throw new InvalidOperationException("Xianbi must pay the frozen complete Hand difference exactly once.");
        var primary = r.Selected.Select(id => r.Hand.Single(c => c.CardId == id)).ToArray();
        ReplaceRuntimeTop(f = f with { Xianbi = r with { Primary = primary } });
        for (var i = 0; i < primary.Length; i++) AdvanceEventRulesAndQueueFact(new XianbiPrimaryCommittedEvent(f.Id, i, primary[i].CardId, primary[i].Kind, primary[i].EffectiveKind));
        f = PrepareXianbiMovement(f, XianbiStage.DiscardChildren);
        var frameId = f.Id; var step = f.Xianbi!.MovementStep;
        MoveProgramLogicalDiscard(f, f.OwnerSeat, primary.Select(c => c.CardId).ToArray(), new(XianbiDiscardReason),
            (batch, moves) => { foreach (var move in moves) RecordXianbiMove(frameId, step, batch, move); });
        CompleteXianbiMovement(frameId, primary.Length); ContinueXianbi(frameId);
    }
    private ProgramSkillFrame PrepareXianbiMovement(ProgramSkillFrame f, XianbiStage stage)
    {
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var r = f.Xianbi!;
        ReplaceRuntimeTop(f = f with { Xianbi = r with { Stage = stage, MovementStep = r.MovementStep + 1,
            Before = _movementSequence, After = _movementSequence, ActualCount = 0 },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        return f;
    }
    private void CompleteXianbiMovement(long id, int count)
    {
        var f = GetActiveProgramFrame(id); var r = f.Xianbi!;
        ReplaceRuntimeTop(f = f with { Xianbi = r with { After = _movementSequence, ActualCount = count } });
        AdvanceEventRulesAndQueueFact(new XianbiMovementIssuedEvent(id, r.MovementStep, r.Stage, r.Cursor, r.Before, _movementSequence, count));
    }
    private void RecordXianbiMove(long frameId, int step, long batch, CardMovementRecord move) =>
        AdvanceEventRulesAndQueueFact(new XianbiEntityMovedEvent(frameId, step, batch, move.CardId, move.CardKind,
            move.From, move.To, move.Reason, move.Sequence));
    private void DrawXianbiIncrease(ProgramSkillFrame f)
    {
        var r = f.Xianbi!;
        if (r.Stage != XianbiStage.DrawRequest || f.NativeDraw is not null || r.Required <= 0)
            throw new InvalidOperationException("Xianbi cannot repeat a frozen native draw request.");
        ReplaceRuntimeTop(f = f with { Xianbi = r with { Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new XianbiDrawRequestedEvent(f.Id, f.OwnerSeat, r.Required, _movementSequence));
        BeginOperationProgramDraw(f, f.OwnerSeat, r.Required, new(XianbiDrawReason));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns, SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement)]
    private void ReturnSunHongDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.Zenrun is not null) { ReturnZenrunCompensation(f, outcome); return; }
        if (f.Xianbi is not { Stage: XianbiStage.DrawRequest, Issued: true } r || f.NativeDraw is not null ||
            _resolutionStack.LastOrDefault()?.Id != f.Id || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Required ||
            outcome.SequenceBefore < r.Before || outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > _movementSequence ||
            outcome.ActualDrawIds.Count > r.Required || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            ProgramEventHistory<XianbiDrawRequestedEvent>().Count(e => e.FrameId == f.Id && e.RecipientSeat == f.OwnerSeat &&
                e.RequestedCount == r.Required && e.SequenceBefore == r.Before) != 1 ||
            ProgramEventHistory<XianbiDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Xianbi lost its exact pending draw request or ordinary-versus-replaced native return.");
        if (outcome.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
            e.AttemptFrameId == attempt && e.ParentFrameId == f.Id && e.RecipientSeat == f.OwnerSeat &&
            e.RequestedCount == r.Required && e.Replaced == (outcome.Replacement is not null)) != 1)
            throw new InvalidOperationException("Xianbi cannot accept a different native draw attempt.");
        ReplaceRuntimeTop(f = f with { Xianbi = r with { Stage = XianbiStage.DrawChildren,
            Before = checked((int)outcome.SequenceBefore), After = checked((int)outcome.SequenceAfter),
            MovementStep = r.MovementStep + 1, ActualCount = outcome.ActualDrawIds.Count }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < outcome.ActualDrawIds.Count; i++) AdvanceEventRulesAndQueueFact(new XianbiDrawnEntityEvent(f.Id, i, outcome.ActualDrawIds[i]));
        AdvanceEventRulesAndQueueFact(new XianbiDrawReturnedEvent(f.Id, outcome.AttemptFrameId, outcome.SequenceBefore,
            outcome.SequenceAfter, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count));
        AdvanceEventRulesAndQueueFact(new XianbiMovementIssuedEvent(f.Id, f.Xianbi!.MovementStep, XianbiStage.DrawChildren,
            r.Cursor, f.Xianbi.Before, f.Xianbi.After, outcome.ActualDrawIds.Count));
        ContinueXianbi(f.Id);
    }
    private bool DrainXianbi(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginAdvancedSkillsChanged(f.Id) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueXianbi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xianbi: { } r } f || f.Id != id) return;
        AssertXianbi(f);
        if (r.Stage == XianbiStage.DrawRequest) return;
        if (r.Stage is XianbiStage.Target or XianbiStage.DiscardChoice) { if (_pendingDecision is null) PublishXianbi(f); return; }
        if (XianbiChildren(r.Stage))
        {
            if (DrainXianbi(f)) return;
            f = GetActiveProgramFrame(id); r = f.Xianbi!;
            if (r.Stage == XianbiStage.DrawChildren) { FinishXianbi(f); return; }
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null,
                Xianbi = r with { Stage = XianbiStage.NextReturn, Cursor = r.Cursor + (r.Stage == XianbiStage.ObtainChildren ? 1 : 0),
                    ChosenCard = null, Eligible = [] } });
            r = f.Xianbi!;
        }
        while (r.Stage == XianbiStage.NextReturn && r.Cursor < r.Primary.Count && _winner == Winner.None && _players[f.OwnerSeat].IsAlive)
        {
            var original = r.Primary[r.Cursor]; var category = CardCatalog.Get(original.EffectiveKind).CategoryName;
            var pool = _cardZones.CardsAt(CardLocation.DiscardPile).Where(c => c.Id != original.CardId &&
                CardCatalog.Get(c.Kind).CategoryName == category).OrderBy(c => c.Id).ToArray();
            if (pool.Length == 0)
            {
                AdvanceEventRulesAndQueueFact(new XianbiReturnSkippedEvent(id, r.Cursor, original.CardId));
                ReplaceRuntimeTop(f = f with { Xianbi = r with { Cursor = r.Cursor + 1 } }); r = f.Xianbi!; continue;
            }
            var before = _random.State; var index = _random.Next(pool.Length); var card = pool[index];
            ReplaceRuntimeTop(f = f with { Xianbi = r with { Eligible = pool.Select(c => c.Id).ToArray(),
                ChosenCard = card.Id, RandomBefore = before, RandomAfter = _random.State, RandomIndex = index } });
            for (var i = 0; i < pool.Length; i++) AdvanceEventRulesAndQueueFact(new XianbiRandomCandidateEvent(id, r.Cursor, i, pool[i].Id));
            AdvanceEventRulesAndQueueFact(new XianbiRandomIssuedEvent(id, r.Cursor, original.CardId, card.Id, index, pool.Length, before, _random.State));
            f = PrepareXianbiMovement(f, XianbiStage.ObtainChildren); var step = f.Xianbi!.MovementStep;
            MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), new(XianbiObtainReason),
                (batch, moves) => { foreach (var move in moves) RecordXianbiMove(id, step, batch, move); });
            CompleteXianbiMovement(id, 1); ContinueXianbi(id); return;
        }
        FinishXianbi(f);
    }
    private void FinishXianbi(ProgramSkillFrame f)
    {
        var r = f.Xianbi!;
        AdvanceEventRulesAndQueueFact(new XianbiCompletedEvent(f.Id, r.Issued, r.Primary.Count, r.Cursor));
        ReplaceRuntimeTop(f = f with { Xianbi = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, r.Issued);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27570)]
    private bool ResumeXianbi(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Xianbi: not null, NativeDraw: null } f || f.Id != id) return false; ContinueXianbi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27570)]
    private bool ReturnXianbiMovement(ProgramSkillFrame f)
    { if (f.Xianbi is null || f.NativeDraw is not null) return false; ContinueXianbi(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27570)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27570)]
    private bool ContinueIssuedXianbi(ProgramSkillFrame f) => f.Xianbi is { Issued: true } && ValidXianbi(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns)]
    private PromptChoice ChooseXianbiAi(PendingDecision prompt, ProgramSkillFrame f)
    {
        if (f.Xianbi!.Stage == XianbiStage.Target) return prompt.Choices.OrderByDescending(c =>
            GetEquipment(_players[c.Targets.Single()]).Count > GetHand(_players[f.OwnerSeat]).Count ? 100 + GetEquipment(_players[c.Targets.Single()]).Count : GetHand(_players[f.OwnerSeat]).Count - GetEquipment(_players[c.Targets.Single()]).Count).First();
        return prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "pay") ??
            prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "pick")
                .OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).First();
    }
}
