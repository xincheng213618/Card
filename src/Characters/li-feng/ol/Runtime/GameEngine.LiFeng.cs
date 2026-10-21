using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class LiFengCapability(GameEngine e) : ILiFengHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginLiFeng(f, op); }
    private SkillProgramStepOutcome BeginLiFeng(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.LiFeng is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } c || !LiFengComposition.Handles(op)) throw new InvalidOperationException("Li Feng requires his exact unissued native trigger.");
        LiFengComposition.ValidateProgram(f.SkillId, _contentRegistry.GetSkill(f.SkillId).Program!);
        var expected = op switch { SkillProgramEffectOp.InitializeJiyunCategoryGrain => SkillProgramTriggerWindow.GameStarting, SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.CollectJiyunCompletedUseEntities => SkillProgramTriggerWindow.CardUseCompleted, _ => SkillProgramTriggerWindow.TurnEnding };
        if (c.Window != expected || c.OwnerSeat != f.OwnerSeat) throw new InvalidOperationException("Li Feng lost his original lifecycle window.");
        PublicPersistentPileSource? pile;
        if (op == SkillProgramEffectOp.CollectJiyunCompletedUseEntities)
        {
            var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
            if (!IsIssuedJiyunCompletedUseCandidate(candidate, c) || c.Jiyun is not { } issued) throw new InvalidOperationException("Jiyun lost its exact completed-use entitlement.");
            pile = issued.Pile;
        }
        else if (op == SkillProgramEffectOp.DistributeShuliangEndingGrain)
            pile = ReferencedPublicPileSources(f.OwnerSeat, "ol:jiyun", f.SkillInstanceId).Where(IsLiveJiyunPile).Where(p => PublicPileCards(p).Count > 0).OrderBy(p => p.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        else pile = EnsurePublicPileSource(f, 3);
        if (pile is null || !_players[f.OwnerSeat].IsAlive) return SkillProgramStepOutcome.Continue;
        if (op != SkillProgramEffectOp.CollectJiyunCompletedUseEntities && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        if (op == SkillProgramEffectOp.InitializeJiyunCategoryGrain && ProgramEventHistory<LiFengStartedEvent>().Any(e => e.Op == op && e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId)) throw new InvalidOperationException("Jiyun cannot initialize one source twice.");
        var stage = op switch { SkillProgramEffectOp.InitializeJiyunCategoryGrain => LiFengStage.InitialChildren, SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases => LiFengStage.PreparationTargets, SkillProgramEffectOp.CollectJiyunCompletedUseEntities => LiFengStage.CollectNext, _ => LiFengStage.DistributionChoice };
        var r = new LiFengReceipt(LiFengSource(f), f.GameplayHash, op, c.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat, pile, stage)
        { MaximumRecipients = op == SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases ? Math.Max(1, PublicPileCards(pile).Count) : 0, CompletedUse = c.Jiyun, CompletedEntities = c.Jiyun is { } completed ? JiyunCompletedEntities(completed) : [],
            Entities = op == SkillProgramEffectOp.DistributeShuliangEndingGrain ? ReferencedPublicPileSources(f.OwnerSeat, "ol:jiyun", f.SkillInstanceId).Where(IsLiveJiyunPile).OrderBy(p => p.SkillInstanceId, StringComparer.Ordinal).SelectMany(p => PublicPileCards(p).Select(card => new LiFengEntity(card.Id, card.Kind, p.Location))).ToArray() : [] };
        ReplaceRuntimeTop(f = f with { LiFeng = r });
        AdvanceEventRulesAndQueueFact(new LiFengStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.TurnOwner, pile));
        if (op == SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases) AdvanceEventRulesAndQueueFact(new JiyunRecipientLimitFrozenEvent(f.Id, r.MaximumRecipients));
        if (op == SkillProgramEffectOp.DistributeShuliangEndingGrain) for (var i = 0; i < r.Entities.Count; i++) AdvanceEventRulesAndQueueFact(new ShuliangDistributionEntityFrozenEvent(f.Id, i, r.Entities[i].CardId, r.Entities[i].Kind, r.Entities[i].From));
        if (op is SkillProgramEffectOp.InitializeJiyunCategoryGrain or SkillProgramEffectOp.CollectJiyunCompletedUseEntities) { ReplaceRuntimeTop(f = f with { LiFeng = r = r with { Paid = true } }); AdvanceEventRulesAndQueueFact(new LiFengPaidEvent(f.Id, op)); }
        if (op == SkillProgramEffectOp.InitializeJiyunCategoryGrain)
        {
            var entities = new List<LiFengEntity>();
            foreach (var category in new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment })
            {
                var cards = _cardZones.CardsAt(CardLocation.DrawPile).Where(card => GetProgramCardCategory(card.Kind) == category && !entities.Any(e => e.CardId == card.Id)).OrderBy(card => card.Id).ToArray();
                if (cards.Length > 0) { var card = cards[_random.Next(cards.Length)]; entities.Add(new(card.Id, card.Kind, CardLocation.DrawPile)); }
            }
            ReplaceRuntimeTop(f = f with { LiFeng = r with { Entities = entities, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (entities.Count > 0) MoveProgramCardsFromMultipleSources(entities.Select(e => e.CardId).ToArray(), pile.Location, new(JiyunInitialReason), (_, movements) =>
            {
                var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { LiFeng = current.LiFeng! with { After = _movementSequence } });
                for (var i = 0; i < entities.Count; i++) { var m = movements.Single(m => m.CardId == entities[i].CardId && m.From == CardLocation.DrawPile && m.To == pile.Location); AdvanceEventRulesAndQueueFact(new JiyunInitialEntityStoredEvent(f.Id, i, m.CardId, m.CardKind, m.Sequence)); }
            });
        }
        ContinueLiFeng(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> LiFengChoices(ProgramSkillFrame f)
    {
        var r = f.LiFeng!; var choices = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => new(new($"li-feng.{f.Id}.{branch}.{string.Join('-', cards ?? [])}.{string.Join('-', targets ?? [])}"), label, cards ?? [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "li-feng", ["branch"] = branch });
        if (r.Stage == LiFengStage.PreparationTargets)
        {
            if (_players[f.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            {
                if (r.Recipients.Count < r.MaximumRecipients) foreach (var p in _players.Where(p => p.IsAlive && !r.Recipients.Contains(p.Seat))) choices.Add(Choice("recipient", $"令{p.Name}摸一张牌", targets: [p.Seat]));
                if (r.Recipients.Count > 0) choices.Add(Choice("start", $"令已选的{r.Recipients.Count}名角色依次摸牌"));
            }
            choices.Add(Choice("cancel", "不发动集运"));
        }
        else if (r.Stage == LiFengStage.DistributionChoice)
        {
            if (_players[f.OwnerSeat].IsAlive && (r.Paid || HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)))
                foreach (var entity in r.Entities.Where(e => !r.DistributedIds.Contains(e.CardId) && _cardZones.GetLocation(e.CardId) == e.From))
                foreach (var p in _players.Where(p => p.IsAlive)) choices.Add(Choice("give", $"将粮【{CardCatalog.Get(entity.Kind).DisplayName}】交给{p.Name}", [entity.CardId], [p.Seat]));
            choices.Add(Choice("finish", "结束分配（其余粮保留）"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishLiFeng(ProgramSkillFrame f)
    {
        AssertLiFeng(f); var choices = LiFengChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) { FinishLiFeng(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.LiFeng!.Stage == LiFengStage.PreparationTargets ? "集运：选择至多指定数量的不同角色，逐人摸一张牌。" : "输粮：将任意粮分配给任意存活角色。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("li-feng")]
    private void AnswerLiFeng(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LiFeng: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !LiFengChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Li Feng requires his current published recipient or exact grain choice.");
        AssertLiFeng(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = selected.Parameters["branch"];
        if (branch is "cancel" or "finish") { FinishLiFeng(f); return; }
        if (branch == "recipient") { ReplaceRuntimeTop(f = f with { LiFeng = r with { Recipients = r.Recipients.Append(selected.Targets.Single()).ToArray() } }); PublishLiFeng(f); return; }
        if (branch == "start")
        {
            if (r.Stage != LiFengStage.PreparationTargets || r.Paid || r.Recipients.Count == 0 || r.Recipients.Count > r.MaximumRecipients || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) throw new InvalidOperationException("Jiyun cannot issue a canceled recipient queue.");
            ReplaceRuntimeTop(f = f with { LiFeng = r with { Paid = true, Stage = LiFengStage.PreparationDraw } });
            for (var i = 0; i < r.Recipients.Count; i++) AdvanceEventRulesAndQueueFact(new JiyunRecipientFrozenEvent(f.Id, i, r.Recipients[i]));
            AdvanceEventRulesAndQueueFact(new LiFengPaidEvent(f.Id, r.Op)); ContinueLiFeng(f.Id); return;
        }
        if (branch != "give" || r.Stage != LiFengStage.DistributionChoice || !_players[f.OwnerSeat].IsAlive || !r.Paid && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) throw new InvalidOperationException("Shuliang cannot issue a new unqualified grain distribution.");
        var card = selected.Cards.Single(); var target = selected.Targets.Single(); var from = r.Entities.Single(e => e.CardId == card).From;
        if (!r.Paid) { ReplaceRuntimeTop(f = f with { LiFeng = r = r with { Paid = true } }); AdvanceEventRulesAndQueueFact(new LiFengPaidEvent(f.Id, r.Op)); }
        ReplaceRuntimeTop(f = f with { LiFeng = r with { Stage = LiFengStage.DistributionChildren, ActiveCardId = card, ActiveRecipient = target, ActiveFrom = from, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAdvancedCard(card), from, CardLocation.Hand(target), new(ShuliangGiftReason), NativeGiveCompletion(f.Id,f.OwnerSeat,target,[card],_movementSequence,movement =>
        {
            var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { LiFeng = current.LiFeng! with { After = movement.Sequence, DistributedIds = current.LiFeng.DistributedIds.Append(card).ToArray() } });
            AdvanceEventRulesAndQueueFact(new ShuliangGrainDistributedEvent(f.Id, card, target, from, movement.Sequence));
        }));
        ContinueLiFeng(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases)]
    private void ReturnJiyunPreparationDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.LiFeng is not { Paid: true, Stage: LiFengStage.PreparationDraw } r || r.Cursor < 0 || r.Cursor >= r.Recipients.Count || outcome.RecipientSeat != r.Recipients[r.Cursor] || outcome.RequestedCount != 1 || ProgramEventHistory<JiyunPreparationDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Index == r.Cursor)) throw new InvalidOperationException("Jiyun draw lost its exact frozen recipient cursor.");
        ReplaceRuntimeTop(f = f with { LiFeng = r with { Stage = LiFengStage.PreparationChildren, DrawOutcome = outcome, ActualDrawIds = outcome.ActualDrawIds, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        foreach (var id in outcome.ActualDrawIds)
        {
            var m = _cardMovements.Single(m => m.CardId == id && m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(outcome.RecipientSeat) && m.Reason.Value == JiyunDrawReason);
            AdvanceEventRulesAndQueueFact(new JiyunLeaseIssuedEvent(m.Sequence, f.Id, r.Cursor, r.Source, r.GameplayHash, r.Pile, outcome.RecipientSeat, id, m.CardKind, m.Sequence));
        }
        AdvanceEventRulesAndQueueFact(new JiyunPreparationDrawReturnedEvent(f.Id, r.Cursor, outcome.RecipientSeat, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueLiFeng(f.Id);
    }
    private bool DrainLiFeng(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueLiFeng(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { LiFeng: { } r } f && f.Id == id)
        {
            AssertLiFeng(f);
            if (r.Stage is LiFengStage.InitialChildren or LiFengStage.PreparationChildren or LiFengStage.CollectChildren or LiFengStage.DistributionChildren)
            {
                if (DrainLiFeng(f)) return;
                f = GetActiveProgramFrame(id); r = f.LiFeng!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage == LiFengStage.InitialChildren || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishLiFeng(f); return; }
                r = r.Stage switch
                {
                    LiFengStage.PreparationChildren => r with { Stage = LiFengStage.PreparationDraw, Cursor = r.Cursor + 1, DrawOutcome = null, ActualDrawIds = [] },
                    LiFengStage.CollectChildren => r with { Stage = LiFengStage.CollectNext, Cursor = r.Cursor + 1, ActiveCardId = 0, ActiveFrom = null },
                    _ => r with { Stage = LiFengStage.DistributionChoice, ActiveCardId = 0, ActiveFrom = null }
                };
                ReplaceRuntimeTop(f = f with { LiFeng = r });
            }
            if (r.Stage is LiFengStage.PreparationTargets or LiFengStage.DistributionChoice)
            {
                if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !r.Paid && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishLiFeng(f); return; }
                if (_pendingDecision is null) PublishLiFeng(f); return;
            }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishLiFeng(f); return; }
            if (r.Stage == LiFengStage.PreparationDraw)
            {
                if (f.NativeDraw is not null) return;
                if (r.Cursor >= r.Recipients.Count) { FinishLiFeng(f); return; }
                if (!_players[r.Recipients[r.Cursor]].IsAlive) { ReplaceRuntimeTop(f = f with { LiFeng = r with { Cursor = r.Cursor + 1 } }); continue; }
                BeginOperationProgramDraw(f, r.Recipients[r.Cursor], 1, new(JiyunDrawReason)); return;
            }
            if (r.Stage != LiFengStage.CollectNext || r.CompletedUse is not { } completed) return;
            if (r.Cursor >= r.CompletedEntities.Count) { FinishLiFeng(f); return; }
            var entity = r.CompletedEntities[r.Cursor]; var lease = JiyunLeases().Single(l => l.LeaseId == entity.LeaseId);
            var parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == r.WindowId);
            if (!IsJiyunPile(r.Pile) || PublicPileCards(r.Pile).Count >= 3 || JiyunActualPayment(lease, parent.Action, completed.NativeUseId, completed.ResponseKind) is null || !JiyunTerminalEntity(lease, parent.Action, completed.NativeUseId, completed.ResponseKind))
            {
                AdvanceEventRulesAndQueueFact(new JiyunLeaseResolvedEvent(f.Id, completed.ActionId, entity.LeaseId, false)); ReplaceRuntimeTop(f = f with { LiFeng = r with { Cursor = r.Cursor + 1 } }); continue;
            }
            var from = _cardZones.GetLocation(entity.CardId);
            ReplaceRuntimeTop(f = f with { LiFeng = r with { Stage = LiFengStage.CollectChildren, ActiveCardId = entity.CardId, ActiveFrom = from, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(GetAdvancedCard(entity.CardId), from, r.Pile.Location, new(JiyunCollectReason), movement =>
            {
                var current = GetActiveProgramFrame(id); ReplaceRuntimeTop(current with { LiFeng = current.LiFeng! with { After = movement.Sequence } });
                AdvanceEventRulesAndQueueFact(new JiyunClaimedEntityEvent(id, completed.NativeUseId, completed.ActionId, completed.ResponseKind, entity.LeaseId, entity.CardId, entity.Kind, from, r.Pile.Location, movement.Sequence));
                AdvanceEventRulesAndQueueFact(new JiyunLeaseResolvedEvent(id, completed.ActionId, entity.LeaseId, true));
            });
            f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { LiFeng = f.LiFeng! with { After = _movementSequence } });
        }
    }
    private void FinishLiFeng(ProgramSkillFrame f)
    {
        var r = f.LiFeng!;
        if (r.CompletedUse is { } completed) foreach (var e in r.CompletedEntities.Where(e => !ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(s => s.LeaseId == e.LeaseId))) AdvanceEventRulesAndQueueFact(new JiyunLeaseResolvedEvent(f.Id, completed.ActionId, e.LeaseId, false));
        AdvanceEventRulesAndQueueFact(new LiFengCompletedEvent(f.Id, r.Op, r.Paid)); ReplaceRuntimeTop(f = f with { LiFeng = null, PendingMovementContinuation = null }); FinishProgramSkill(f, r.Paid);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27591)] private bool ResumeLiFeng(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LiFeng: not null } f || f.Id != id) return false; ContinueLiFeng(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27591)] private bool ReturnLiFengMovement(ProgramSkillFrame f) { if (f.LiFeng is null) return false; ContinueLiFeng(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.InitializeJiyunCategoryGrain, SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases, SkillProgramEffectOp.CollectJiyunCompletedUseEntities, SkillProgramEffectOp.DistributeShuliangEndingGrain)]
    private PromptChoice ChooseLiFengAi(PendingDecision prompt, ProgramSkillFrame f)
    {
        if (f.LiFeng!.Stage == LiFengStage.PreparationTargets)
            return prompt.Choices.Where(c => c.Parameters["branch"] == "recipient").OrderBy(c => c.Targets.Single() == f.OwnerSeat ? 0 : 1).FirstOrDefault() ?? prompt.Choices.FirstOrDefault(c => c.Parameters["branch"] == "start") ?? prompt.Choices.Single(c => c.Parameters["branch"] == "cancel");
        return prompt.Choices.Where(c => c.Parameters["branch"] == "give").OrderBy(c => c.Targets.Single() == f.OwnerSeat ? 0 : 1).FirstOrDefault() ?? prompt.Choices.Single(c => c.Parameters["branch"] == "finish");
    }
}
