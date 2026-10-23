namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhangWenChoice = "anniversary-zhang-wen", ZhangWenReveal = "sibian-four", ZhangWenExtrema = "sibian-extrema";
    private static CardMoveReason ZhangWenGain => new("skill-program.anniversary:sibian.extrema");
    private static CardMoveReason ZhangWenGift => new("skill-program.anniversary:sibian.gift");
    private static CardMoveReason ZhangWenCleanup => new("skill-program.anniversary:sibian.cleanup");
    private static CardConversionSource ZhangWenSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryZhangWenHost(GameEngine engine) : IAnniversaryZhangWenHost
    {
        public void Refund(ProgramSkillFrame frame) => engine.RefundZhangWenSongshu(frame);
        public SkillProgramStepOutcome Sibian(ProgramSkillFrame frame) => engine.BeginZhangWenSibian(frame);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 45100)]
    private void CaptureZhangWenSongshu(ProgramSkillStartedEvent started)
    {
        if (started.SkillId != AnniversaryZhangWenComposition.Songshu || started.ActivationId != AnniversaryZhangWenComposition.Contest) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != started.FrameId || f.OwnerSeat != started.OwnerSeat ||
            f.TriggerId is not null || f.InstructionIndex != 0 || f.ZhangWenSongshu is not null || f.SelectedCardIds.Count != 1 || f.SelectedTargetSeats.Count != 1 ||
            _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat)
            throw new InvalidOperationException("Songshu must freeze its own accepted Play debit before Pindian starts.");
        var activation = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Activation!;
        var key = (f.OwnerSeat, f.SkillId, activation.UsageGroup);
        var r = new ZhangWenSongshuReceipt(ZhangWenSource(f), f.GameplayHash, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId,
            activation.UsageGroup, _programUses.GetValueOrDefault(key), _programPhaseUses.GetValueOrDefault(key));
        if (r.TurnUses < 1 || r.PhaseUses != 1) throw new InvalidOperationException("Songshu requires its actually consumed one-per-Play use.");
        ReplaceRuntimeTop(f with { ZhangWenSongshu = r });
        AdvanceEventRulesAndQueueFact(new ZhangWenSongshuAcceptedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.PlayPhase, r.UsageGroup, r.TurnUses, r.PhaseUses));
    }
    private void RefundZhangWenSongshu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ZhangWenSongshu is not { Refunded: false } r || r.Source != ZhangWenSource(f) || r.Hash != f.GameplayHash ||
            f.InstructionIndex != 2 || f.SkillId != AnniversaryZhangWenComposition.Songshu || f.ActivationId != AnniversaryZhangWenComposition.Contest ||
            f.TriggerId is not null || r.Turn != _turnNumber || r.TurnOwner != _currentSeat || r.TurnOwner != f.OwnerSeat ||
            _phase != TurnPhase.Play || r.PlayPhase != _cardUseDebitPhaseInstanceId ||
            f.PindianResultBindings is not [{ Name: "__active-pindian-result", SourceWon: true } result] ||
            result.SourceSeat != f.OwnerSeat || f.SelectedTargetSeats is not [var opponent] || result.OpponentSeat != opponent ||
            ProgramEventHistory<ZhangWenSongshuAcceptedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Hash == r.Hash && e.PlayPhase == r.PlayPhase) != 1 ||
            ProgramEventHistory<ZhangWenSongshuRefundedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Songshu cannot refund another frame, phase or a non-winning Pindian.");
        var key = (f.OwnerSeat, f.SkillId, r.UsageGroup);
        var turnBefore = _programUses.GetValueOrDefault(key); var phaseBefore = _programPhaseUses.GetValueOrDefault(key);
        if (turnBefore != r.TurnUses || phaseBefore != r.PhaseUses) throw new InvalidOperationException("Songshu's original debit changed before its winning refund.");
        _programUses[key] = turnBefore - 1; _programPhaseUses[key] = phaseBefore - 1;
        ReplaceRuntimeTop(f with { ZhangWenSongshu = r with { Refunded = true } });
        AdvanceEventRulesAndQueueFact(new ZhangWenSongshuRefundedEvent(f.Id, r.Turn, r.PlayPhase, turnBefore, turnBefore - 1, phaseBefore, phaseBefore - 1));
    }
    private SkillProgramStepOutcome BeginZhangWenSibian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ZhangWenSibian is not null || f.SkillId != AnniversaryZhangWenComposition.Sibian || f.TriggerId != AnniversaryZhangWenComposition.Replacement ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } context)
            throw new InvalidOperationException("Sibian requires its accepted original normal-draw replacement.");
        var r = new ZhangWenSibianReceipt(ZhangWenSource(f), f.GameplayHash, context.ParentFrameId, _turnNumber, ZhangWenSibianStage.RevealChildren);
        ReplaceRuntimeTop(f = f with { ZhangWenSibian = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ZhangWenSibianStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn));
        var before = _movementSequence;
        RevealProgramTopCards(f.Id, f.OwnerSeat, 4, null, ZhangWenReveal, SkillProgramCardSetVisibility.Public);
        f = GetActiveProgramFrame(f.Id);
        var entities = GetProgramCardSet(f, ZhangWenReveal).CardIds.Select(id => new ZhangWenSibianEntity(id, GetAdvancedCard(id).Kind, GetAdvancedCard(id).Rank,
            _cardMovements.Last(m => m.CardId == id).Sequence)).ToArray();
        var extrema = entities.Length == 0 ? [] : entities.Where(e => e.Rank == entities.Min(c => c.Rank) || e.Rank == entities.Max(c => c.Rank)).Select(e => e.CardId).ToArray();
        ReplaceRuntimeTop(f with { ZhangWenSibian = r with { Entities = entities, Extrema = extrema } });
        foreach (var (entity, index) in entities.Select((e, i) => (e, i))) AdvanceEventRulesAndQueueFact(new ZhangWenSibianEntityEvent(f.Id, index, entity, extrema.Contains(entity.CardId)));
        AddZhangWenSibianStep(f.Id, ZhangWenSibianStage.RevealChildren, before);
        ContinueZhangWenSibian(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ZhangWenEntityAvailable(ZhangWenSibianEntity entity) => _cardZones.GetLocation(entity.CardId) == CardLocation.Processing &&
        _cardMovements.Last(m => m.CardId == entity.CardId).Sequence == entity.RevealSequence;
    private int[] ZhangWenRemainder(ProgramSkillFrame f) => f.ZhangWenSibian!.Entities.Where(e => !f.ZhangWenSibian.Extrema.Contains(e.CardId) && ZhangWenEntityAvailable(e)).Select(e => e.CardId).ToArray();
    private IReadOnlyList<PromptChoice> ZhangWenSibianChoices(ProgramSkillFrame f)
    {
        if (f.ZhangWenSibian?.Stage != ZhangWenSibianStage.Recipient || ZhangWenRemainder(f).Length == 0) return [];
        var living = _players.Where(p => p.IsAlive).ToArray(); var minimum = living.Min(p => GetHand(p).Count);
        var choices = living.Where(p => GetHand(p).Count == minimum).Select(p => new PromptChoice(new($"zhang-wen.{f.Id}.give.{p.Seat}"),
            "将剩余的牌全部交给" + p.Name, [], [p.Seat], new Dictionary<string, string> { ["program-action"] = ZhangWenChoice, ["mode"] = "give" })).ToList();
        choices.Add(new(new($"zhang-wen.{f.Id}.skip"), "不交给任何角色，将余牌置入弃牌堆", [], [], new Dictionary<string, string> { ["program-action"] = ZhangWenChoice, ["mode"] = "skip" }));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishZhangWenSibian(ProgramSkillFrame f)
    {
        RequireZhangWenSibian(f); var choices = ZhangWenSibianChoices(f);
        if (choices.Count == 0) { FinishProgramSkill(f, true); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, "思辩：可将全部余牌交给当前手牌最少的一名角色。", f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(ZhangWenChoice)] private void AnswerZhangWenSibian(PromptChoice answer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangWenSibian: { Stage: ZhangWenSibianStage.Recipient } } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([answer], ZhangWenSibianChoices(f).Where(c => c.Id == answer.Id).ToArray()))
            throw new InvalidOperationException("Sibian requires the original owner's current whole-remainder choice.");
        RequireZhangWenSibian(f); ClearPendingDecision();
        var target = answer.Parameters["mode"] == "give" ? answer.Targets.Single() : -1;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, ZhangWenSibian = f.ZhangWenSibian! with { Recipient = target } });
        AdvanceEventRulesAndQueueFact(new ZhangWenSibianRecipientEvent(f.Id, target));
        MoveZhangWenSibian(f, ZhangWenSibianStage.FinishChildren, ZhangWenRemainder(f), target < 0 ? CardLocation.DiscardPile : CardLocation.Hand(target), target < 0 ? ZhangWenCleanup : ZhangWenGift);
        ContinueZhangWenSibian(f.Id);
    }
    private void AddZhangWenSibianStep(long id, ZhangWenSibianStage stage, int before)
    {
        var f = GetActiveProgramFrame(id); var step = new ZhangWenSibianStep(stage, before, _movementSequence);
        ReplaceRuntimeTop(f with { ZhangWenSibian = f.ZhangWenSibian! with { Steps = f.ZhangWenSibian.Steps.Append(step).ToArray() } });
        AdvanceEventRulesAndQueueFact(new ZhangWenSibianStepEvent(id, step));
    }
    private void MoveZhangWenSibian(ProgramSkillFrame f, ZhangWenSibianStage stage, IReadOnlyList<int> ids, CardLocation destination, CardMoveReason reason)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { ZhangWenSibian = f.ZhangWenSibian! with { Stage = stage }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (ids.Count > 0)
            MoveProgramCardsFromMultipleSources(ids, destination, reason,
                stage == ZhangWenSibianStage.FinishChildren && destination.OwnerSeat is { } recipient
                    ? NativeGiveCompletionRecords(f.Id, f.OwnerSeat, recipient, ids, before) : null, disclosure: CardMovementDisclosure.FaceUp);
        AddZhangWenSibianStep(f.Id, stage, before);
    }
    private bool DrainZhangWenSibian(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 45100)] private bool ContinueZhangWenSibian(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangWenSibian: not null } own || own.Id != id) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhangWenSibian: { } r } f && f.Id == id)
        {
            RequireZhangWenSibian(f);
            if (r.Stage != ZhangWenSibianStage.Recipient)
            {
                if (DrainZhangWenSibian(id)) return true;
                f = GetActiveProgramFrame(id); r = f.ZhangWenSibian!;
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage == ZhangWenSibianStage.FinishChildren) { FinishProgramSkill(f, true); return true; }
            }
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
            {
                var cleanup = r.Entities.Where(ZhangWenEntityAvailable).Select(e => e.CardId).ToArray();
                MoveZhangWenSibian(f, ZhangWenSibianStage.FinishChildren, cleanup, CardLocation.DiscardPile, ZhangWenCleanup); continue;
            }
            if (r.Stage == ZhangWenSibianStage.RevealChildren)
            {
                var ids = r.Entities.Where(e => r.Extrema.Contains(e.CardId) && ZhangWenEntityAvailable(e)).Select(e => e.CardId).ToArray();
                SetProgramCardSet(f.Id, ZhangWenExtrema, ids, SkillProgramCardSetVisibility.Public);
                MoveZhangWenSibian(GetActiveProgramFrame(id), ZhangWenSibianStage.GainChildren, ids, CardLocation.Hand(f.OwnerSeat), ZhangWenGain); continue;
            }
            if (r.Stage == ZhangWenSibianStage.GainChildren)
                ReplaceRuntimeTop(f = f with { ZhangWenSibian = r with { Stage = ZhangWenSibianStage.Recipient } });
            PublishZhangWenSibian(f); return true;
        }
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 45100)] private bool ReturnZhangWenSibianMovement(ProgramSkillFrame f) => f.ZhangWenSibian is not null && ContinueZhangWenSibian(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryZhangWenSibian)] private PromptChoice ChooseZhangWenSibian(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Targets.Contains(f.OwnerSeat)) ?? p.Choices[0];
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 45100)]
    private bool KeepZhangWenSibianPayment(ProgramSkillFrame f) => f.ZhangWenSibian is not null && ValidZhangWenSibian(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 45100)]
    private bool KeepZhangWenAcceptedAfterSkillLoss(ProgramSkillFrame f)
    {
        if (f.ZhangWenSibian is not null) return ValidZhangWenSibian(f);
        if (f.ZhangWenSongshu is not { } r || !_players[f.OwnerSeat].IsAlive || f.SkillId != AnniversaryZhangWenComposition.Songshu ||
            f.TriggerId is not null || f.ActivationId != AnniversaryZhangWenComposition.Contest || f.InstructionIndex is < 1 or > 4 ||
            r.Source != ZhangWenSource(f) || r.Hash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash ||
            f.SelectedCardIds is not [var selectedCard] || f.SelectedTargetSeats is not [var opponent] ||
            f.PindianResultBindings is not [{ Name: "__active-pindian-result" } result] || result.SourceSeat != f.OwnerSeat || result.OpponentSeat != opponent ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId && e.OwnerSeat == f.OwnerSeat) != 1 ||
            ProgramEventHistory<ZhangWenSongshuAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var accepted] ||
            accepted != new ZhangWenSongshuAcceptedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.PlayPhase, r.UsageGroup, r.TurnUses, r.PhaseUses)) return false;
        return CompleteProgramEventHistory().SkipWhile(e => e is not ZhangWenSongshuAcceptedEvent a || a.FrameId != f.Id).OfType<PindianResultDeterminedEvent>()
            .Count(e => e.SkillId == f.SkillId && e.Result.SourceSeat == f.OwnerSeat && e.Result.OpponentSeat == opponent && e.Result.SourceCardId == selectedCard &&
                e.Result.SourceRank == result.SourceRank && e.Result.OpponentRank == result.OpponentRank && e.Result.SourceWon == result.SourceWon) == 1;
    }
}
