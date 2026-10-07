using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string KuangfuEquipmentReason = "skill-program.kuangfu.equipment-cost";
    private const string KuangfuHandReason = "skill-program.kuangfu.no-damage-hand-cost";
    private const string KuangfuDrawReason = "skill-program.kuangfu.own-equipment-draw";
    private static CardConversionSource KuangfuSource(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId!, f.OwnerSeat, f.SkillInstanceId);
    private bool KuangfuGameEnded() => _winner != Winner.None || _status == EngineStatus.Completed;
    private bool KuangfuUnpaidSourceCurrent(ProgramSkillFrame f) => !KuangfuGameEnded() && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private bool CanIssueKuangfuSlash(int actor, int target, int? excludedEquipmentId = null) => IsValidPlayerSeat(actor) && IsValidPlayerSeat(target) &&
        actor != target && _players[actor].IsAlive && _players[target].IsAlive && !KuangfuGameEnded() &&
        !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsCardUseForbidden(actor, CardKind.Slash, CardActionType.Use) &&
        !IsDirectedCardTargetProhibited(actor, target, CardKind.Slash) && !IsSlashProhibited(_players[target]) &&
        !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null) && !HasBeneficiarySuitShield(actor, target, Suit.None) &&
        CanSpendSlashUse(_players[actor], _players[target], ignoresCount: false, CardKind.Slash, effectiveRank: null, excludedEquipmentId: excludedEquipmentId);
    private IEnumerable<(int Owner, Card Card)> KuangfuEquipment(int actor) => _players.Where(p => p.IsAlive)
        .SelectMany(p => GetEquipment(p).Select(c => (Owner: p.Seat, Card: c)))
        .Where(x => !x.Card.IsGeneralWeapon && !IsForeignEquipmentDiscardPrevented(actor, x.Card,
            CardLocation.Equipment(x.Owner), OwnedCardMoveIntent.Discard) &&
            _players.Any(t => CanIssueKuangfuSlash(actor, t.Seat, x.Owner == actor ? x.Card.Id : null)));
    private bool CanStartKuangfu(CharacterState owner) => KuangfuEquipment(owner.Seat).Any() &&
        _players.Any(p => CanIssueKuangfuSlash(owner.Seat, p.Seat));
    private SkillProgramStepOutcome BeginKuangfu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Kuangfu is not null || f.InstructionIndex != 1 || f.TriggerId is not null || f.WindowContext is not null ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("An equipment Slash needs its own clean zero-input activation.");
        if (!KuangfuUnpaidSourceCurrent(f) || !CanStartKuangfu(_players[f.OwnerSeat])) return SkillProgramStepOutcome.Continue;
        var source = KuangfuSource(f);
        ReplaceRuntimeTop(f = f with { Kuangfu = new() { InstructionIndex = f.InstructionIndex, Source = source,
            GameplayHash = f.GameplayHash, ActualTurn = _turnNumber, ActualTurnOwnerSeat = _currentSeat,
            Stage = KuangfuStage.EquipmentChoice } });
        AdvanceEventRulesAndQueueFact(new KuangfuStartedEvent(f.Id, source, f.GameplayHash, _turnNumber, _currentSeat));
        PublishKuangfu(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> KuangfuChoices(ProgramSkillFrame f)
    {
        var r = f.Kuangfu!; var choices = new List<PromptChoice>();
        void Add(string token, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => choices.Add(new(
            new($"kuangfu.{f.Id}.{token}"), label, cards, targets, new Dictionary<string, string>
            { ["program-action"] = "kuangfu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["kuangfu-action"] = token }));
        if (r.Stage == KuangfuStage.EquipmentChoice)
            foreach (var x in KuangfuEquipment(f.OwnerSeat).OrderBy(x => x.Owner).ThenBy(x => x.Card.Id))
                Add("equipment-" + x.Card.Id, $"弃置 {_players[x.Owner].Name} 装备区的【{x.Card.DisplayName}】", [x.Card.Id], [x.Owner]);
        else if (r.Stage == KuangfuStage.SlashTargetChoice)
            foreach (var target in _players.Where(p => CanIssueKuangfuSlash(f.OwnerSeat, p.Seat)))
                Add("slash-" + target.Seat, "对 " + target.Name + " 使用无距离限制的杀", [], [target.Seat]);
        else if (r.Stage == KuangfuStage.HandDiscardChoice)
            foreach (var c in GetHand(_players[f.OwnerSeat]).Where(c => !r.SelectedHandIds.Contains(c.Id)).OrderBy(c => c.Id))
                Add("hand-" + c.Id, "弃置手牌【" + c.DisplayName + "】", [c.Id], []);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishKuangfu(ProgramSkillFrame f)
    {
        if (KuangfuGameEnded()) return;
        if (!_players[f.OwnerSeat].IsAlive || f.Kuangfu!.Stage == KuangfuStage.EquipmentChoice && !KuangfuUnpaidSourceCurrent(f))
        { FinishKuangfu(f); return; }
        var choices = KuangfuChoices(f);
        if (choices.Count == 0) { FinishKuangfu(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId); var r = f.Kuangfu!;
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage == KuangfuStage.HandDiscardChoice
                ? $"狂斧：选择 {r.DiscardRequired} 张手牌弃置。" : skill.Description,
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage == KuangfuStage.HandDiscardChoice, TargetSeat = f.OwnerSeat,
            Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveKuangfuChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !ValidKuangfu(f) ||
            f.Kuangfu is not { Stage: KuangfuStage.EquipmentChoice or KuangfuStage.SlashTargetChoice or KuangfuStage.HandDiscardChoice } r ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p || p.PlayerSeat != f.OwnerSeat ||
            p.IsPrivate != (r.Stage == KuangfuStage.HandDiscardChoice) || !AssistedChoicesEqual([choice], KuangfuChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Equipment Slash choice lost its owning frame and exact published material.");
        ClearPendingDecision();
        if (KuangfuGameEnded()) return;
        if (!_players[f.OwnerSeat].IsAlive) { FinishKuangfu(f); return; }
        if (r.Stage == KuangfuStage.EquipmentChoice)
        {
            // Check an actual use before any irreversible cost, not merely a virtual card offer.
            if (!KuangfuUnpaidSourceCurrent(f) || !_players.Any(t => CanIssueKuangfuSlash(f.OwnerSeat, t.Seat)))
            { FinishKuangfu(f); return; }
            var entity = KuangfuEquipment(f.OwnerSeat).Single(x => x.Card.Id == choice.Cards.Single() && choice.Targets.SequenceEqual([x.Owner]));
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { Kuangfu = r with { Stage = KuangfuStage.EquipmentChildren,
                EquipmentOwnerSeat = entity.Owner, EquipmentCardId = entity.Card.Id, EquipmentKind = entity.Card.Kind,
                PaymentBefore = before, PaymentAfter = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources([entity.Card.Id], CardLocation.DiscardPile, new(KuangfuEquipmentReason));
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { Kuangfu = f.Kuangfu! with { PaymentAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new KuangfuEquipmentPaidEvent(f.Id, entity.Owner, entity.Card.Id, entity.Card.Kind, before, _movementSequence));
            AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage == KuangfuStage.SlashTargetChoice) { IssueKuangfuSlash(f, choice.Targets.Single()); return; }
        var ids = r.SelectedHandIds.Append(choice.Cards.Single()).ToArray();
        ReplaceRuntimeTop(f = f with { Kuangfu = r with { SelectedHandIds = ids } });
        if (ids.Length < r.DiscardRequired) { PublishKuangfu(f); return; }
        var costBefore = _movementSequence;
        ReplaceRuntimeTop(f = f with { Kuangfu = f.Kuangfu! with { Stage = KuangfuStage.OutcomeChildren, SelectedHandIds = [],
            DiscardedHandIds = ids, OutcomeBefore = costBefore, OutcomeAfter = costBefore }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(KuangfuHandReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { Kuangfu = f.Kuangfu! with { OutcomeAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new KuangfuHandDiscardPaidEvent(f.Id, ids, costBefore, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainKuangfu(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool ResumeKuangfu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.Kuangfu is not { } r) return false;
        if (!ValidKuangfu(f)) throw new InvalidOperationException("Equipment Slash lost its original paid instruction.");
        if (KuangfuGameEnded()) return true;
        if (r.Stage is KuangfuStage.EquipmentChoice or KuangfuStage.SlashTargetChoice or KuangfuStage.HandDiscardChoice)
        { if (_pendingDecision is null) PublishKuangfu(f); return true; }
        if (r.Stage == KuangfuStage.SlashIssued) throw new InvalidOperationException("An issued equipment Slash needs its exact typed completed-use return.");
        if (DrainKuangfu(f)) return true;
        f = GetActiveProgramFrame(id); r = f.Kuangfu!;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (!_players[f.OwnerSeat].IsAlive || r.Stage == KuangfuStage.OutcomeChildren) { FinishKuangfu(f); return true; }
        // A paid equipment's children may change all legal targets or quota. Preserve the paid receipt and close, never repay or leave a dead prompt.
        ReplaceRuntimeTop(f = f with { Kuangfu = r with { Stage = KuangfuStage.SlashTargetChoice } });
        PublishKuangfu(f); return true;
    }
    private bool ReturnKuangfuMovement(ProgramSkillFrame f)
    {
        if (f.Kuangfu is not { Stage: KuangfuStage.EquipmentChildren or KuangfuStage.OutcomeChildren }) return false;
        if (f.PendingMovementContinuation is null || !ValidKuangfu(f)) throw new InvalidOperationException("An equipment Slash movement returned to a different invoice.");
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private void IssueKuangfuSlash(ProgramSkillFrame f, int targetSeat)
    {
        var r = f.Kuangfu!;
        if (r.Stage != KuangfuStage.SlashTargetChoice || !ValidKuangfu(f)) throw new InvalidOperationException("A paid equipment Slash cannot issue twice.");
        if (!CanIssueKuangfuSlash(f.OwnerSeat, targetSeat)) { PublishKuangfu(f); return; }
        if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("An equipment Slash cannot overwrite its payment child.");
        var actor = _players[f.OwnerSeat]; var target = _players[targetSeat]; var useId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, actor.Seat, actor.Seat, null, null, null, CardKind.Slash, [targetSeat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var returned = new KuangfuSlashReturn(f.Id, f.InstructionIndex, r.Source, r.GameplayHash, r.ActualTurn, r.ActualTurnOwnerSeat,
            r.EquipmentOwnerSeat!.Value, r.EquipmentCardId!.Value, useId, action.ActionId, targetSeat);
        ReplaceRuntimeTop(f = f with { SelectedTargetSeats = [targetSeat], Kuangfu = r with { Stage = KuangfuStage.SlashIssued, SlashReturn = returned } });
        PushRuntimeFrame(new CardUseFrame(useId, actor.Seat, 0, CardKind.Slash, [targetSeat], PhysicalCardIds: [])
        { Action = action, KuangfuSlashReturn = returned, FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(actor) });
        AdvanceEventRulesAndQueueFact(new KuangfuSlashIssuedEvent(returned));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(useId, 0, CardKind.Slash, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(useId, [targetSeat]));
        RecordYingboCardUse(useId, actor.Seat, CardKind.Slash); RecordProgramUsedBasicCard(actor.Seat, CardKind.Slash);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash); RecordActualPlayPhaseUse(action);
        // This Play activation grants distance only. The native ordinary Slash ledger is debited exactly once.
        if (LifecycleCardUse(useId)?.UnlimitedUse != true) RecordSlashUseDebit(useId, actor.Seat);
        var attack = new CardAttackHandle(this, useId, actor.Seat, targetSeat, card: null,
            damageAmount: actor.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(useId, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, targetSeat));
        TryMarkProgramUseCommitted(useId);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats,
                ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void CompleteKuangfuSlash(AttackCompletionReceipt completion)
    {
        var returned = completion.KuangfuSlashReturn ?? throw new InvalidOperationException("Missing equipment Slash return.");
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != returned.ProgramFrameId ||
            completion.ProgramFrameId != f.Id || completion.ResolutionId != returned.CardUseFrameId || !ValidKuangfu(f) ||
            f.Kuangfu?.SlashReturn != returned || f.Kuangfu.Stage != KuangfuStage.SlashIssued ||
            _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == returned.CardUseFrameId) ||
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == returned.CardUseFrameId) != 1)
            throw new InvalidOperationException("The equipment Slash lost its exact retired Use and original paid parent.");
        var r = f.Kuangfu;
        AdvanceEventRulesAndQueueFact(new KuangfuSlashResolvedEvent(f.Id, returned.CardUseFrameId, returned.CardActionId, r.CausedDamage));
        if (KuangfuGameEnded() || !_players[f.OwnerSeat].IsAlive) { if (!KuangfuGameEnded()) FinishKuangfu(f); return; }
        if (r.EquipmentOwnerSeat != f.OwnerSeat && !r.CausedDamage)
        {
            var required = Math.Min(2, GetHand(_players[f.OwnerSeat]).Count);
            if (required == 0) { FinishKuangfu(f); return; }
            ReplaceRuntimeTop(f = f with { Kuangfu = r with { Stage = KuangfuStage.HandDiscardChoice, DiscardRequired = required } });
            PublishKuangfu(f); return;
        }
        if (r.EquipmentOwnerSeat == f.OwnerSeat && r.CausedDamage)
        {
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { Kuangfu = r with { Stage = KuangfuStage.OutcomeChildren, OutcomeBefore = before, OutcomeAfter = before },
                PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            var drawn = DrawCards(_players[f.OwnerSeat], 2, true, new(KuangfuDrawReason)).Count;
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { Kuangfu = f.Kuangfu! with { ActualDraw = drawn, OutcomeAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new KuangfuDrawIssuedEvent(f.Id, 2, drawn, before, _movementSequence));
            AdvanceRuntimeProgram(f.Id); return;
        }
        FinishKuangfu(f);
    }
    private void FinishKuangfu(ProgramSkillFrame f)
    {
        var r = f.Kuangfu!;
        AdvanceEventRulesAndQueueFact(new KuangfuFinishedEvent(f.Id, r.EquipmentCardId is not null, r.SlashReturn is not null));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { Kuangfu = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    private PromptChoice SelectAiKuangfu(PendingDecision p, ProgramSkillFrame f)
    {
        var view = CreateSnapshot(f.OwnerSeat); var own = view.Players.Single(x => x.Seat == f.OwnerSeat);
        if (f.Kuangfu!.Stage == KuangfuStage.HandDiscardChoice)
        {
            var hand = own.Hand.ToDictionary(c => c.Id);
            return p.Choices.OrderBy(c => CardCatalog.Get(hand[c.Cards.Single()].Kind).HandKeepValue).ThenBy(c => c.Cards.Single()).First();
        }
        var brain = _aiBrains[f.OwnerSeat];
        if (f.Kuangfu.Stage == KuangfuStage.SlashTargetChoice)
            return p.Choices.OrderByDescending(c => brain.ScoreProgramTarget(view, c.Targets.Single(), new(1, 0, 0, 0, 0, 0, false, false))).ThenBy(c => c.Targets.Single()).First();
        // Equipment identity is public. No opponent hand is inspected to guess a response.
        return p.Choices.OrderBy(c => c.Targets.Single() == f.OwnerSeat ? 1 : 0).ThenBy(c => c.Cards.Single()).First();
    }
    private sealed partial class ProgramSkillHost : IKuangfuProgramHost
    { public SkillProgramStepOutcome DiscardEquipmentThenSlashAndOwnershipOutcome(ProgramSkillFrame f) => engine.BeginKuangfu(f); }
}
