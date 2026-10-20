using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouXiahouDunGanglie = "mou-mobile:ganglie", MouXiahouDunQingjian = "mou-mobile:qingjian";
    private const string MouXiahouDunCollectReason = "skill-program.mou-mobile:qingjian.collect", MouXiahouDunGiftReason = "skill-program.mou-mobile:qingjian.give";
    private bool HasMouXiahouDunCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.CollectMouXiahouDunQingjian) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouXiahouDunGanglie);
    private static CardConversionSource MouXiahouDunSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome StartMouXiahouDunMobile(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouXiahouDunMobile is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) throw new InvalidOperationException("Mou Xiahou Dun requires its live owning entry.");
        ActualDiscardRecoveryPhaseKey? phase = null;
        if (op == SkillProgramEffectOp.ResolveMouXiahouDunGanglie)
        {
            if (f.SkillId != MouXiahouDunGanglie || f.ActivationId != "retaliate-all" || f.TriggerId is not null || f.WindowContext is not null || !CanStartMouXiahouDunGanglie(_players[f.OwnerSeat]))
                throw new InvalidOperationException("Ganglie requires a genuine own Play and an unspent historical attacker.");
            phase = CurrentActualDiscardRecoveryPhase();
        }
        else if (f.SkillId != MouXiahouDunQingjian || f.ActivationId != f.TriggerId || f.WindowContext is not { } context || context.OwnerSeat != f.OwnerSeat ||
            (op == SkillProgramEffectOp.CollectMouXiahouDunQingjian ? context.Window != SkillProgramTriggerWindow.DiscardPileReceived || f.TriggerId != "collect-non-use" :
             op != SkillProgramEffectOp.DistributeMouXiahouDunQingjian || context.Window != SkillProgramTriggerWindow.PlayEnding || f.TriggerId != "distribute-qingjian"))
            throw new InvalidOperationException("Qingjian requires its exact native discard or own Play-end window.");
        var r = new MouXiahouDunMobileReceipt(MouXiahouDunSource(f), f.GameplayHash, op) { Phase = phase, Stage = op == SkillProgramEffectOp.ResolveMouXiahouDunGanglie
            ? MouXiahouDunMobileStage.SelectTargets : op == SkillProgramEffectOp.CollectMouXiahouDunQingjian ? MouXiahouDunMobileStage.MovementChildren : MouXiahouDunMobileStage.SelectRecipient,
            Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r });
        AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileStartedEvent(f.Id, r.Source, r.GameplayHash, op, phase));
        if (op == SkillProgramEffectOp.CollectMouXiahouDunQingjian)
        {
            var context = f.WindowContext!; var batch = context.MovementBatch!; var move = batch.Movements[context.MovementIndex!.Value];
            if (!MatchingMouXiahouDunDiscardIndexes(batch).Contains(context.MovementIndex.Value) || _cardZones.GetLocation(move.CardId) != CardLocation.DiscardPile ||
                MouXiahouDunQingjianCount(f.OwnerSeat) >= MouXiahouDunQingjianCapacity(f.OwnerSeat)) { FinishMouXiahouDun(f); return SkillProgramStepOutcome.AwaitChild; }
            var cap = MouXiahouDunQingjianCapacity(f.OwnerSeat); var pile = EnsurePublicPileSource(f, int.MaxValue); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Pile = pile, Cards = [move.CardId], Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(GetAdvancedCard(move.CardId), CardLocation.DiscardPile, pile.Location, new(MouXiahouDunCollectReason), disclosure: CardMovementDisclosure.FaceUp);
            AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileCollectedEvent(f.Id, r.Source, move.CardId, move.Sequence, _movementSequence, cap));
            SealMouXiahouDunMovements(f.Id, before); ContinueMouXiahouDunMovement(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.DistributeMouXiahouDunQingjian)
        {
            var pile = MouXiahouDunPile(f.OwnerSeat, f.SkillInstanceId);
            if (pile is null || PublicPileCards(pile).Count == 0) { FinishMouXiahouDun(f); return SkillProgramStepOutcome.AwaitChild; }
            var ids = PublicPileCards(pile).Select(c => c.Id).ToArray();
            ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Pile = pile, Cards = ids } });
            for (var i = 0; i < ids.Length; i++) AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileRosterEvent(f.Id, i, ids[i], pile.Location));
        }
        PublishMouXiahouDun(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice MouXiahouDunChoice(ProgramSkillFrame f, string branch, string label, int? target = null, int? card = null) => new(
        new($"mou-xiahou-dun.{f.Id}.{f.MouXiahouDunMobile!.Index}.{branch}.{target ?? card ?? 0}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [],
        new Dictionary<string, string> { ["program-action"] = "mou-xiahou-dun-mobile", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouXiahouDunChoices(ProgramSkillFrame f)
    {
        var r = f.MouXiahouDunMobile!;
        if (r.Stage == MouXiahouDunMobileStage.SelectTargets)
        {
            var choices = MouXiahouDunGanglieTargets(f.OwnerSeat).Select(s => MouXiahouDunChoice(f, "toggle", (r.Targets.Contains(s) ? "取消：" : "选择：") + _players[s].Name, target: s)).ToList();
            if (r.Targets.Count > 0) choices.Add(MouXiahouDunChoice(f, "confirm", $"确定对{r.Targets.Count}名角色各造成2点伤害"));
            return Array.AsReadOnly(choices.ToArray());
        }
        var card = GetAdvancedCard(r.Cards[r.Index]);
        return Array.AsReadOnly(_players.Where(p => p.IsAlive).Select(p => MouXiahouDunChoice(f, "give", $"将清俭【{card.DisplayName}】交给{p.Name}", p.Seat, card.Id)).ToArray());
    }
    private void PublishMouXiahouDun(ProgramSkillFrame f)
    {
        var choices = MouXiahouDunChoices(f); if (choices.Count == 0) { FinishMouXiahouDun(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-xiahou-dun-mobile")]
    private void AnswerMouXiahouDun(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouXiahouDunMobile!;
        if (!ValidMouXiahouDun(f) || !AssistedChoicesEqual([selected], MouXiahouDunChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Xiahou Dun requires its exact current choice.");
        ClearPendingDecision();
        if (r.Stage == MouXiahouDunMobileStage.SelectTargets)
        {
            if (selected.Parameters["branch"] == "toggle")
            {
                var target = selected.Targets.Single(); var targets = r.Targets.Contains(target) ? r.Targets.Where(s => s != target).ToArray() : r.Targets.Append(target).ToArray();
                ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Targets = targets } }); PublishMouXiahouDun(f); return;
            }
            var roster = r.Targets.OrderBy(s => (s - f.OwnerSeat + _playerCount) % _playerCount).ToArray();
            ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Targets = roster, Stage = MouXiahouDunMobileStage.Damaging } });
            for (var i = 0; i < roster.Length; i++) AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileGanglieSpentEvent(f.Id, f.OwnerSeat, roster[i], i));
            IssueMouXiahouDunDamage(f); return;
        }
        var id = r.Cards[r.Index]; var recipient = selected.Targets.Single(); var before = _movementSequence;
        if (_cardZones.GetLocation(id) != r.Pile!.Location) throw new InvalidOperationException("Qingjian distribution lost its current exact pile entity.");
        ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Stage = MouXiahouDunMobileStage.MovementChildren, RecipientSeat = recipient, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAdvancedCard(id), r.Pile.Location, CardLocation.Hand(recipient), new(MouXiahouDunGiftReason), disclosure: CardMovementDisclosure.FaceUp);
        AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileGiftEvent(f.Id, r.Index, id, recipient, _movementSequence));
        SealMouXiahouDunMovements(f.Id, before); ContinueMouXiahouDunMovement(f.Id);
    }
    private void ContinueMouXiahouDunMovement(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouXiahouDunMobile: { Stage: MouXiahouDunMobileStage.MovementChildren } r } f || f.Id != id) return;
        if (!ValidMouXiahouDun(f)) throw new InvalidOperationException("Qingjian lost its paid movement cursor.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id); r = f.MouXiahouDunMobile!;
        if (r.Op == SkillProgramEffectOp.CollectMouXiahouDunQingjian || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishMouXiahouDun(f); return; }
        var index = r.Index + 1;
        while (index < r.Cards.Count && _cardZones.GetLocation(r.Cards[index]) != r.Pile!.Location) index++;
        if (index >= r.Cards.Count) { FinishMouXiahouDun(f); return; }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MouXiahouDunMobile = r with { Index = index, Stage = MouXiahouDunMobileStage.SelectRecipient } }); PublishMouXiahouDun(f);
    }
    private void FinishMouXiahouDun(ProgramSkillFrame f) { ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouXiahouDunGanglie, SkillProgramEffectOp.DistributeMouXiahouDunQingjian)]
    private PromptChoice AiMouXiahouDun(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouXiahouDunMobile!;
        if (r.Stage == MouXiahouDunMobileStage.SelectTargets)
            return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "toggle" && !r.Targets.Contains(c.Targets.Single()) && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()])) ??
                p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "confirm") ?? p.Choices.First();
        return p.Choices.OrderByDescending(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()])).ThenBy(c => GetHand(_players[c.Targets.Single()]).Count).First();
    }
    [ProgramCapability] private sealed class MouXiahouDunMobileCapability(GameEngine g) : IMouXiahouDunMobileHost
    { public SkillProgramStepOutcome Start(ProgramSkillFrame f, SkillProgramEffectOp op) => g.StartMouXiahouDunMobile(f, op); }
}
