using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BiguoCostReason = "skill-program.pang-ji.biguo.discard";
    private const string BiguoObtainReason = "skill-program.pang-ji.biguo.obtain";
    private const string BiguoResultBind = "biguo-contest", BiguoGainBind = "biguo-category-gain";
    private bool HasPangJiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw);
    private static CardConversionSource PangJiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pang_ji_ol_PangJi.PangJiSource(f);
    private bool BiguoTurnOpen(int turn, int owner) => turn > 0 && turn == _turnNumber && _turnProgression.TurnNumber == turn && _turnProgression.OwnerSeat == owner && !ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == turn && e.ActorSeat == owner) && !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == turn && e.OwnerSeat == owner);
    [ProgramCapability] private sealed class PangJiCapability(GameEngine engine) : IPangJiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginPangJi(f, op); }
    private IReadOnlyList<PromptChoice> BiguoCosts(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard);
    private bool CanBiguoPindian(int owner) => _players[owner].IsAlive && HasWuLuKangPindianInput(_players[owner]) && !_resolutionStack.Any(f => f is PindianFrame) && _players.Any(p => p.IsAlive && p.Seat != owner && CanBePindianTarget(owner, p.Seat));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28130)]
    private bool CanOfferPangJi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c)
    {
        if (trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian }]) return c.Window == SkillProgramTriggerWindow.DrawPhaseEnded && c.OwnerSeat == candidate.OwnerSeat && _players[candidate.OwnerSeat].IsAlive && GetHand(_players[candidate.OwnerSeat]).Concat(GetEquipment(_players[candidate.OwnerSeat])).Any();
        return trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan }] || IsIssuedBiguoCandidate(candidate, c);
    }
    private SkillProgramStepOutcome BeginPangJi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.PangJi is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } c || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) throw new InvalidOperationException("Pang Ji requires his exact unexecuted native binding.");
        var source = PangJiSource(f); var target = op == SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan;
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        if (target ? !IsIssuedBiguoCandidate(candidate, c) : c.Window != SkillProgramTriggerWindow.DrawPhaseEnded || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) throw new InvalidOperationException("Biguo lost his native draw-end or already-paid target entitlement.");
        var r = new PangJiReceipt(source, f.GameplayHash, op, c.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat, target ? PangJiStage.Pindian : PangJiStage.CostChoice) { Target = c.BiguoTarget, Paid = target, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { PangJi = r });
        AdvanceEventRulesAndQueueFact(new PangJiStartedEvent(f.Id, source, f.GameplayHash, op, r.WindowId, r.ActualTurn, r.TurnOwner));
        if (target)
        {
            AdvanceEventRulesAndQueueFact(new BiguoTargetAcceptedEvent(f.Id, r.Target!));
            var childId = _resolutionSequence + 1;
            ReplaceRuntimeTop(f = f with { PangJi = r = r with { PindianId = childId } });
            AdvanceEventRulesAndQueueFact(new BiguoPindianIssuedEvent(f.Id, childId, r.Target!.ArmFrameId, r.Target.UseId, r.Target.ActionId, f.OwnerSeat, r.Before));
            var skill = _contentRegistry.GetSkill(f.SkillId);
            BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, skill.Name + " · 拼点", skill.Description), f.OwnerSeat, programResultBind: BiguoResultBind);
        }
        else PublishPangJi(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> PangJiChoices(ProgramSkillFrame f)
    {
        if (f.PangJi is not { Paid: false, Stage: PangJiStage.CostChoice }) return [];
        PromptChoice Choice(string branch, string label, IReadOnlyList<int> ids) => new(new($"pang-ji.{f.Id}.{branch}.{string.Join('-', ids)}"), label, ids, [], new Dictionary<string, string> { ["program-action"] = "pang-ji", ["pang-ji"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        return Array.AsReadOnly(BiguoCosts(f).Select(c => Choice("cost-card", "弃置" + c.Label, c.Cards)).Append(Choice("cancel", "不发动愎果", [])).Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishPangJi(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishPangJi(f); return; }
        var choices = PangJiChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "愎果：弃置一张真实手牌或装备牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("pang-ji")]
    private void AnswerPangJi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PangJi: { Paid: false, Stage: PangJiStage.CostChoice } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !PangJiChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Biguo requires the exact original private HE cost choice.");
        AssertPangJi(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["pang-ji"] == "cancel") { FinishPangJi(f); return; }
        if (choice.Parameters["pang-ji"] != "cost-card") throw new InvalidOperationException("Unknown Pang Ji input.");
        var id = choice.Cards.Single(); var cost = new CardActionCost(id, GetAdvancedCard(id).Kind, _cardZones.GetLocation(id));
        ReplaceRuntimeTop(f = f with { PangJi = r with { Cost = cost, Paid = true, Stage = PangJiStage.CostChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new BiguoCostCommittedEvent(f.Id, id, cost.CardKind, cost.From, _movementSequence));
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new(BiguoCostReason), (batch, moves) =>
        {
            if (moves is not [var m] || m.CardId != id || m.From != cost.From || m.CardKind != cost.CardKind) throw new InvalidOperationException("Biguo cannot replace his original one-card logical discard invoice.");
            AdvanceEventRulesAndQueueFact(new BiguoCostPaidEvent(f.Id, batch, id, m.CardKind, m.From, m.To, m.Reason, m.Sequence));
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { PangJi = f.PangJi! with { After = _movementSequence } }); ContinuePangJi(f.Id);
    }
    private void ObtainBiguoCategories(ProgramSkillFrame f)
    {
        var r = f.PangJi!; var entities = new List<PangJiEntity>();
        foreach (var category in new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment })
        {
            var cards = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => GetProgramCardCategory(c.Kind) == category).OrderBy(c => c.Id).ToArray();
            if (cards.Length > 0) { var c = cards[_random.Next(cards.Length)]; entities.Add(new(c.Id, c.Kind, CardLocation.DrawPile)); }
        }
        ReplaceRuntimeTop(f = f with { PangJi = r with { Stage = PangJiStage.ObtainChildren, Obtained = entities, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < entities.Count; i++) AdvanceEventRulesAndQueueFact(new BiguoObtainEntityIssuedEvent(f.Id, i, entities[i].CardId, entities[i].PrintedKind));
        SetProgramCardSet(f.Id, BiguoGainBind, entities.Select(e => e.CardId).ToArray(), SkillProgramCardSetVisibility.Public, entities.Select(e => e.From).ToArray());
        RevealProgramBoundCards(GetActiveProgramFrame(f.Id), BiguoGainBind);
        if (entities.Count > 0) MoveProgramCardsFromMultipleSources(entities.Select(e => e.CardId).ToArray(), CardLocation.Hand(f.OwnerSeat), new CardMoveReason(BiguoObtainReason), (batch, moves) =>
        {
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i]; var m = moves.Single(m => m.CardId == entity.CardId && m.From == entity.From && m.To == CardLocation.Hand(f.OwnerSeat));
                AdvanceEventRulesAndQueueFact(new BiguoObtainEntityPaidEvent(f.Id, i, batch, m.CardId, m.CardKind, m.From, m.To, m.Sequence));
            }
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { PangJi = f.PangJi! with { After = _movementSequence } });
    }
    private bool DrainPangJi(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id);
    private void ContinuePangJi(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { PangJi: { } r } f && f.Id == id && _pendingDecision is null)
        {
            if (r.Stage == PangJiStage.Pindian)
            {
                var facts = ProgramEventHistory<PindianResultDeterminedEvent>().Where(e => e.FrameId == r.PindianId && e.SkillId == f.SkillId).ToArray();
                var binding = f.PindianResultBindings.SingleOrDefault(b => b.Name == BiguoResultBind);
                if (facts is not [var determined] || binding is null || determined.Result.SourceSeat != f.OwnerSeat || binding.SourceSeat != determined.Result.SourceSeat || binding.OpponentSeat != determined.Result.OpponentSeat || binding.SourceRank != determined.Result.SourceRank || binding.OpponentRank != determined.Result.OpponentRank || binding.SourceWon != determined.Result.SourceWon || _resolutionStack.Any(x => x.Id == r.PindianId)) throw new InvalidOperationException("Biguo lost his completed native pair, claims or final result binding.");
                var result = determined.Result;
                ReplaceRuntimeTop(f = f with { PangJi = r = r with { Stage = PangJiStage.PindianChildren, Result = result, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                AdvanceEventRulesAndQueueFact(new BiguoPindianReturnedEvent(f.Id, r.PindianId, result.SourceSeat, result.OpponentSeat, result.SourceCardId, result.OpponentCardId, result.SourceRank, result.OpponentRank));
            }
            AssertPangJi(f);
            if (r.Stage is PangJiStage.CostChildren or PangJiStage.ObtainChildren or PangJiStage.PindianChildren)
            { if (DrainPangJi(id)) return; f = GetActiveProgramFrame(id); r = f.PangJi!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); }
            if (_winner != Winner.None) { FinishPangJi(f); return; }
            if (r.Stage == PangJiStage.CostChildren)
            { if (!_players[f.OwnerSeat].IsAlive) { FinishPangJi(f); return; } ObtainBiguoCategories(f); continue; }
            if (r.Stage == PangJiStage.ObtainChildren)
            {
                if (_players[f.OwnerSeat].IsAlive && BiguoTurnOpen(r.ActualTurn, r.TurnOwner)) AdvanceEventRulesAndQueueFact(new BiguoArmedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner));
                FinishPangJi(f); return;
            }
            if (r.Stage == PangJiStage.PindianChildren) { FinishPangJi(f); return; }
            PublishPangJi(f); return;
        }
    }
    private void FinishPangJi(ProgramSkillFrame f)
    { var r = f.PangJi!; ClearPendingDecision(); AdvanceEventRulesAndQueueFact(new PangJiCompletedEvent(f.Id, r.Op, r.Paid)); ReplaceRuntimeTop(f = f with { PangJi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28130)] private bool ResumePangJi(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PangJi: not null } f || f.Id != id || f.InstructionIndex == 0) return false; ContinuePangJi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28130)] private bool ReturnPangJiMovement(ProgramSkillFrame f) { if (f.PangJi is null) return false; ContinuePangJi(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian)]
    private PromptChoice ChoosePangJiAi(PendingDecision p, ProgramSkillFrame f) => p.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[f.OwnerSeat])).ThenBy(c => c.Id.Value, StringComparer.Ordinal).FirstOrDefault() ?? p.Choices.Single();
}
