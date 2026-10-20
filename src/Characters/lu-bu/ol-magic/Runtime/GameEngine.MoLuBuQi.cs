using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMoLuBu => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeMoLuBuQiAndOpeningPlay);
    private static CardConversionSource MoLuBuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasMoLuBuQi(int id) => HasMoLuBu && id > 0 && ProgramEventHistory<MoLuBuQiMarkedEvent>().Any(e => e.CardId == id);
    private ProgramLifecycleTriggerWindowFrame CaptureMoLuBuInitialQi(ProgramLifecycleTriggerWindowFrame f)
    {
        if (!HasMoLuBu || f.Window != SkillProgramTriggerWindow.GameStarting || f.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return f;
        return f with { MoLuBuInitialQi = _players.SelectMany(p => GetHand(p).Select(card => new MoLuBuInitialQi(p.Seat, card.Id, card.Kind,
            _cardMovements.Last(m => m.CardId == card.Id && m.To == CardLocation.Hand(p.Seat)).Sequence))).ToArray() };
    }
    private bool MoLuBuQiZone(CardLocation location) => location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile or CardZoneKind.DiscardPile;
    private IReadOnlyList<MoLuBuQiCandidate> MoLuBuQiCandidates(int original) => Array.AsReadOnly(ProgramEventHistory<MoLuBuQiMarkedEvent>()
        .Where(e => e.OriginalOwnerSeat == original).DistinctBy(e => e.CardId).Select(e => new MoLuBuQiCandidate(e.CardId, _cardZones.GetLocation(e.CardId)))
        .Where(e => MoLuBuQiZone(e.From)).ToArray());
    private bool MoLuBuLastUseWasQi(int actor) => _events.Select(e => e.Payload).Concat(_pendingEvents).LastOrDefault(e =>
        e is MoLuBuActualUseQiEvent used && used.ActorSeat == actor || e is MoLuBuLegacyDuelUseEvent duel && duel.ActorSeat == actor)
        is MoLuBuActualUseQiEvent { IsQi: true };
    private void CaptureMoLuBuLegacyDuel(ProgramSkillFrame frame, int actor)
    {
        if (!HasMoLuBu) return;
        if (ProgramEventHistory<MoLuBuLegacyDuelUseEvent>().Any(e => e.ProducerFrameId == frame.Id && e.InstructionIndex == frame.InstructionIndex))
            throw new InvalidOperationException("A native direct Duel cannot update the Gangquan use prefix twice.");
        AdvanceEventRulesAndQueueFact(new MoLuBuLegacyDuelUseEvent(frame.Id, frame.InstructionIndex, actor));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 30800)]
    private void ObserveMoLuBuUse(IGameEvent fact)
    {
        if (!HasMoLuBu) return;
        var action = fact is CardUseDeclaredEvent declaration ? LifecycleCardUse(declaration.ResolutionId)?.Action : fact is CardActionAcceptedEvent accepted ? accepted.Action : null;
        if (action is null || !IsMoLuBuTrueUse(action) || ProgramEventHistory<MoLuBuActualUseQiEvent>().Any(e => e.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new MoLuBuActualUseQiEvent(action.ActionId, action.ActorSeat, action.PhysicalCards.Any(c => HasMoLuBuQi(c.CardId))));
    }
    private bool IsMoLuBuTrueUse(CardActionContext action) => action.Type == CardActionType.Use ||
        action.EffectiveKind == CardKind.Nullification && IsCompletedResponseUse(action, ProgramCardContinuation.NullificationResponse) ||
        IsCompletedResponseUse(action, ProgramCardContinuation.Dodge) && _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Action?.ActionId == action.ParentActionId)
            is { CardAttack: { EffectiveCardKind: { } incoming } attack } && IsSlashCard(incoming) && attack.TargetSeat == action.ActorSeat;
    private SkillProgramStepOutcome InitializeMoLuBu(ProgramSkillFrame f)
    {
        if (f.SkillId != MoLuBuComposition.Duoqi || f.TriggerId != "initial-qi-opening-play" || f.InstructionIndex != 1 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.GameStarting } c ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { MoLuBuInitialQi: { } initial } ||
            ProgramEventHistory<MoLuBuInitializedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat)) throw new InvalidOperationException("Duoqi needs its one actual opening-Hand snapshot.");
        if (!ProgramEventHistory<MoLuBuQiMarkedEvent>().Any()) foreach (var card in initial)
            AdvanceEventRulesAndQueueFact(new MoLuBuQiMarkedEvent(f.Id, card.OriginalOwnerSeat, card.CardId, card.PrintedKind, card.EntrySequence));
        AdvanceEventRulesAndQueueFact(new MoLuBuInitializedEvent(f.Id, MoLuBuSource(f), f.GameplayHash, c.ParentFrameId));
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30800, SkillProgramEffectOp.ResolveMoLuBuDuoqi)]
    private bool CanOfferMoDuoqi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c) =>
        c.Window == SkillProgramTriggerWindow.AfterDamageApplied && c.SourceSeat == candidate.OwnerSeat && c.TargetSeat is { } victim &&
        EventsSinceLastBoundary(e => e is TurnStartedEvent).OfType<DamageAppliedEvent>().Count(e => !e.SourceLess && e.SourceSeat == candidate.OwnerSeat && e.TargetSeat == victim) == 1 &&
        !ProgramEventHistory<MoLuBuStartedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.Operation == SkillProgramEffectOp.ResolveMoLuBuDuoqi && e.OriginalOwnerSeat == victim && e.ActualTurn == _turnNumber);
    private SkillProgramStepOutcome BeginMoDuoqi(ProgramSkillFrame f)
    {
        var c = f.WindowContext ?? throw new InvalidOperationException("Duoqi needs native damage.");
        var r = new MoLuBuReceipt(MoLuBuSource(f), f.GameplayHash, SkillProgramEffectOp.ResolveMoLuBuDuoqi, c.ParentFrameId, c.TargetSeat!.Value, _turnNumber, MoLuBuStage.QiChoice)
        { Candidates = MoLuBuQiCandidates(c.TargetSeat.Value) };
        StartMoLuBu(f, r); PublishMoLuBu(GetActiveProgramFrame(f.Id)); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void StartMoLuBu(ProgramSkillFrame f, MoLuBuReceipt r)
    {
        if (f.MoLuBu is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Lu Bu cannot issue its obligation twice.");
        ReplaceRuntimeTop(f with { MoLuBu = r });
        AdvanceEventRulesAndQueueFact(new MoLuBuStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.WindowId, r.OriginalOwnerSeat, r.ActualTurn, r.Reassign));
    }
    private IReadOnlyList<PromptChoice> MoLuBuChoices(ProgramSkillFrame f)
    {
        var r = f.MoLuBu!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Params(string verb, int index) => new() { ["program-action"] = "mo-lu-bu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = verb, ["index"] = index.ToString(CultureInfo.InvariantCulture) };
        if (r.Stage == MoLuBuStage.QiChoice)
            for (var n = 0; n < r.Candidates.Count; n++)
            {
                var item = r.Candidates[n]; if (_cardZones.GetLocation(item.CardId) != item.From || !MoLuBuQiZone(item.From) || item.From == CardLocation.Hand(f.OwnerSeat)) continue;
                var hidden = item.From.Zone == CardZoneKind.DrawPile || item.From.Zone == CardZoneKind.Hand && item.From.OwnerSeat != f.OwnerSeat;
                var card = _cardZones.CardsAt(item.From).Single(c => c.Id == item.CardId);
                choices.Add(new(new($"mo-lu-bu.{f.Id}.qi.{n}"), hidden ? $"取回第{n + 1}张初始炁" : $"取回【{card.DisplayName}】", hidden ? [] : [card.Id], [], Params("qi", n)));
            }
        if (r.Stage == MoLuBuStage.PairChoice)
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)) choices.Add(new(new($"mo-lu-bu.{f.Id}.pair.{p.Seat}"), $"令{p.Name}成为狂角色", [], [p.Seat], Params("pair", p.Seat)));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMoLuBu(ProgramSkillFrame f)
    {
        var choices = MoLuBuChoices(f); if (choices.Count == 0) { CompleteMoLuBu(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：请选择本次真实效果。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mo-lu-bu")]
    private void AnswerMoLuBu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoLuBu: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !MoLuBuChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Lu Bu lost its current canonical private choice.");
        ClearPendingDecision();
        if (r.Stage == MoLuBuStage.QiChoice)
        {
            var index = int.Parse(choice.Parameters["index"], CultureInfo.InvariantCulture);
            ReplaceRuntimeTop(f = f with { MoLuBu = r with { Candidates = [r.Candidates[index]], Index = 0, Stage = MoLuBuStage.Claiming } }); ContinueMoLuBu(f.Id); return;
        }
        if (r.Stage != MoLuBuStage.PairChoice) throw new InvalidOperationException("Unknown Lu Bu choice stage.");
        SetMoLuBuPair(f, choice.Targets.Single(), false); CompleteMoLuBu(f);
    }
    private void MoveMoLuBuQi(ProgramSkillFrame f, MoLuBuQiCandidate item)
    {
        var before = _movementSequence; var r = f.MoLuBu!;
        ReplaceRuntimeTop(f = f with { MoLuBu = r with { Stage = MoLuBuStage.MovementChildren, Before = before, After = before, Index = r.Index + 1 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var card = _cardZones.CardsAt(item.From).Single(c => c.Id == item.CardId);
        MoveCard(card, item.From, CardLocation.Hand(f.OwnerSeat), new("skill-program.ol-mo-lu-bu.qi"),
            moved => AdvanceEventRulesAndQueueFact(new MoLuBuQiMovedEvent(f.Id, moved.CardId, moved.From, moved.To, moved.Sequence)),
            disclosure: item.From.Zone == CardZoneKind.DrawPile || item.From.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MoLuBu = f.MoLuBu! with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > before)) AdvanceEventRulesAndQueueFact(new MoLuBuMovementSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence));
    }
    private bool DrainMoLuBu(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMoLuBu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoLuBu: { } r } f || f.Id != id || _pendingDecision is not null) return;
        if (r.Stage == MoLuBuStage.EntryChildren) { if (DrainMoLuBu(f)) return; CompleteMoLuBu(GetActiveProgramFrame(id)); return; }
        while (true)
        {
            if (f.MoLuBu!.Stage == MoLuBuStage.MovementChildren)
            { if (DrainMoLuBu(f)) return; ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null, MoLuBu = GetActiveProgramFrame(id).MoLuBu! with { Stage = MoLuBuStage.Claiming } }); }
            r = f.MoLuBu!;
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { CompleteMoLuBu(f); return; }
            if (r.Stage != MoLuBuStage.Claiming) { PublishMoLuBu(f); return; }
            if (r.Index < r.Candidates.Count)
            {
                var item = r.Candidates[r.Index]; var current = _cardZones.GetLocation(item.CardId);
                if (!MoLuBuQiZone(current) || current == CardLocation.Hand(f.OwnerSeat)) { ReplaceRuntimeTop(f = f with { MoLuBu = r with { Index = r.Index + 1 } }); continue; }
                MoveMoLuBuQi(f, item with { From = current }); f = GetActiveProgramFrame(id); continue;
            }
            if (r.Reassign) { ReplaceRuntimeTop(f = f with { MoLuBu = r with { Stage = MoLuBuStage.PairChoice } }); PublishMoLuBu(f); return; }
            CompleteMoLuBu(f); return;
        }
    }
    private void CompleteMoLuBu(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new MoLuBuCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { MoLuBu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 30800)] private bool ResumeMoLuBu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoLuBu: not null } f || f.Id != id) return false; ContinueMoLuBu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30800)] private bool ReturnMoLuBuMovement(ProgramSkillFrame f) => f.MoLuBu is not null && ResumeMoLuBu(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMoLuBuDuoqi, SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath)]
    private PromptChoice ChooseMoLuBuAi(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Targets is [var target] && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[target])) ?? p.Choices[0];
    [ProgramCapability] private sealed class MoLuBuCapability(GameEngine g) : IMoLuBuHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.InitializeMoLuBuQiAndOpeningPlay => g.InitializeMoLuBu(f),
            SkillProgramEffectOp.ResolveMoLuBuDuoqi => g.BeginMoDuoqi(f),
            SkillProgramEffectOp.EnterMoLuBuKuangmo => g.BeginMoKuangmo(f),
            _ => g.BeginMoKuangmoDeath(f)
        };
    }
}
