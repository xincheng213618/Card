using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardMoveReason BaoGainReason = new("skill-program.converted-nonbasic-slash.obtain");
    private static readonly CardMoveReason BaoCostReason = new("skill-program.chosen-assault.cost");
    private static readonly CardMoveReason BaoOutcomeReason = new("skill-program.chosen-assault.random-discard");
    private static bool IsBaoOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill or
        SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes or SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover;
    private CardConversionSource BaoSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private bool BaoSourceCurrent(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 320)]
    private bool CanOfferBao2025(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c)
    {
        if (trigger.Effects is not [var e] || !IsBaoOperation(e.Op)) return true;
        var owner = _players[candidate.OwnerSeat];
        if (e.Op == SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes)
            return c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && c.OwnerSeat == owner.Seat &&
                BaoCostCards(owner).Count >= 2 && !IsCardUseForbidden(owner.Seat, CardKind.BarbarianAssault, CardActionType.Use);
        if (e.Op == SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover)
            return c.Window == SkillProgramTriggerWindow.DyingEntering && c.TargetSeat == owner.Seat && owner.Hp <= 0 &&
                _resolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == owner.Seat) && _players.Any(p => p.IsAlive && p.Seat != owner.Seat);
        if (c.Window != SkillProgramTriggerWindow.CardUseCompleted || c.CardUse is not { } cc ||
            LifecycleCardUse(cc.ParentCardUseFrameId) is not { Step: ResolutionFrameStep.Completed, Action: { } action } use ||
            action.ActionId != cc.CardActionId ||
            !HasIssuedNonBasicSlashDistance(use.Id) || !action.ConversionChain.Any(s => s.SkillId == candidate.SkillId && s.SkillInstanceId == candidate.SkillInstanceId)) return false;
        return use.TargetSeats.Any(t => IsValidPlayerSeat(t) && _players[t].IsAlive && HasTargetCard(_players[t]));
    }
    private IReadOnlyList<Card> BaoCostCards(CharacterState owner) => Array.AsReadOnly(GetHand(owner).Concat(GetEquipment(owner))
        .Where(c => !c.IsGeneralWeapon).OrderBy(c => c.Id).ToArray());
    private SkillProgramStepOutcome BeginBao2025(ProgramSkillFrame input, SkillProgramEffect e)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.Bao2025 is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context ||
            GetProgramTrigger(f).Effects is not [var configured] || configured != e ||
            !CanOfferBao2025(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), GetProgramTrigger(f), context))
            throw new InvalidOperationException("The converted Slash, chosen assault or Dying choice lost its exact unpaid native boundary.");
        var use = context.CardUse is { } cc ? LifecycleCardUse(cc.ParentCardUseFrameId) : null;
        var receipt = new ProgramBao2025Receipt { Operation = e.Op, Source = BaoSource(f), GameplayHash = f.GameplayHash,
            WindowId = context.ParentFrameId, OriginalUseId = use?.Id, OriginalActionId = use?.Action?.ActionId,
            ActualTurn = _turnNumber, ActualTurnOwner = _currentSeat, Targets = use?.TargetSeats ?? [],
            Stage = e.Op == SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill ? ProgramBao2025Stage.TakeTarget :
                e.Op == SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes ? ProgramBao2025Stage.AssaultCost : ProgramBao2025Stage.DyingTarget };
        ReplaceRuntimeTop(f = f with { Bao2025 = receipt });
        AdvanceEventRulesAndQueueFact(new ProgramBao2025StartedEvent(f.Id, receipt));
        PublishBaoChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice BaoChoice(ProgramSkillFrame f, string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        new(new($"bao2025.{f.Id}.{f.Bao2025!.Stage}.{branch}"), label, cards ?? [], targets ?? [],
            new Dictionary<string, string> { ["program-action"] = "bao2025", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> BaoChoices(ProgramSkillFrame f)
    {
        var r = f.Bao2025!; var choices = new List<PromptChoice>();
        switch (r.Stage)
        {
            case ProgramBao2025Stage.TakeTarget:
                choices.AddRange(r.Targets.Where(t => _players[t].IsAlive && HasTargetCard(_players[t]))
                    .Select(t => BaoChoice(f, $"target-{t}", $"获得{_players[t].Name}的一张牌", targets: [t]))); break;
            case ProgramBao2025Stage.TakeCard:
                choices.AddRange(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.Recipient!.Value,
                    [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment], OwnedCardMoveIntent.Obtain)
                    .Select(c => PromptChoiceProjection.Freeze(c with { Parameters = new Dictionary<string, string>(c.Parameters)
                        { ["program-action"] = "bao2025", ["branch"] = "take" } }))); break;
            case ProgramBao2025Stage.ResetChoice:
                choices.Add(BaoChoice(f, "reset", "失去1点体力，所有角色的镇南视为未发动"));
                choices.Add(BaoChoice(f, "decline", "不失去体力")); break;
            case ProgramBao2025Stage.AssaultCost:
                var costs = BaoCostCards(_players[f.OwnerSeat]);
                for (var i = 0; i < costs.Count; i++) for (var j = i + 1; j < costs.Count; j++)
                    choices.Add(BaoChoice(f, $"cost-{costs[i].Id}-{costs[j].Id}", $"弃置{costs[i].DisplayName}、{costs[j].DisplayName}", [costs[i].Id, costs[j].Id]));
                choices.Add(BaoChoice(f, "decline", "取消发动")); break;
            case ProgramBao2025Stage.AssaultTargets:
                foreach (var target in BaoAssaultLegalTargets(f).Where(t => !r.SelectedTargets.Contains(t)))
                    choices.Add(BaoChoice(f, $"add-{target}", $"加入{_players[target].Name}", targets: [target]));
                choices.Add(BaoChoice(f, "confirm", $"确定对{r.SelectedTargets.Count}名角色使用南蛮入侵", targets: r.SelectedTargets));
                choices.Add(BaoChoice(f, "decline", "取消发动")); break;
            case ProgramBao2025Stage.DyingTarget:
                choices.AddRange(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)
                    .Select(p => BaoChoice(f, $"recipient-{p.Seat}", $"令{p.Name}选择", targets: [p.Seat])));
                choices.Add(BaoChoice(f, "decline", "取消发动")); break;
            case ProgramBao2025Stage.DyingRecipientChoice:
                choices.Add(BaoChoice(f, "replace", "失去所有技能，获得征南"));
                choices.Add(BaoChoice(f, "grant", "获得镇南")); break;
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private int[] BaoAssaultLegalTargets(ProgramSkillFrame f) => _players.Where(p => p.IsAlive &&
        !HasCardPolicy(p, SkillProgramCardPolicyKind.ExcludeGlobalTarget, CardKind.BarbarianAssault) &&
        !IsCardTargetProhibited(p, CardKind.BarbarianAssault, Suit.None, false) &&
        !IsDirectedCardTargetProhibited(f.OwnerSeat, p.Seat, CardKind.BarbarianAssault) &&
        !HasBeneficiarySuitShield(f.OwnerSeat, p.Seat, Suit.None)).Select(p => p.Seat).ToArray();
    private void PublishBaoChoice(ProgramSkillFrame f)
    {
        var choices = BaoChoices(f);
        if (choices.Count == 0) { FinishBao2025(f, false); return; }
        ReplaceRuntimeTop(f = f with { Bao2025 = f.Bao2025! with { Choices = choices }, Step = ResolutionFrameStep.AwaitingResponse });
        var chooser = f.Bao2025.Stage == ProgramBao2025Stage.DyingRecipientChoice ? f.Bao2025.Recipient!.Value : f.OwnerSeat;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"【{skill.Name}】请选择。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("bao2025")]
    private void ResolveBao2025Choice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The choice lost its owning frame.");
        RequireBao2025(f); var r = f.Bao2025!;
        var chooser = r.Stage == ProgramBao2025Stage.DyingRecipientChoice ? r.Recipient!.Value : f.OwnerSeat;
        if (_pendingDecision?.PlayerSeat != chooser || !AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], BaoChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("The choice lost its exact chooser or current published opaque/card/target selection.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "decline") { FinishBao2025(f, r.GainedCardId is not null); return; }
        if (!BaoSourceCurrent(f) && r.GainedCardId is null && r.Stage != ProgramBao2025Stage.DyingRecipientChoice)
        { FinishBao2025(f, false); return; }
        if (r.Stage == ProgramBao2025Stage.TakeTarget)
        { ReplaceRuntimeTop(f = f with { Bao2025 = r with { Recipient = choice.Targets.Single(), Stage = ProgramBao2025Stage.TakeCard, Choices = [] } }); PublishBaoChoice(f); return; }
        if (r.Stage == ProgramBao2025Stage.TakeCard) { PayBaoGain(f, choice); return; }
        if (r.Stage == ProgramBao2025Stage.ResetChoice)
        {
            var before = _players[f.OwnerSeat].Hp;
            ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.ResetHpChildren, Choices = [], HpBefore = before,
                HpAfter = Math.Max(0, before - 1) }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            var result = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, 1);
            AdvanceEventRulesAndQueueFact(new ProgramBao2025HpPaidEvent(f.Id, r.Source, before, _players[f.OwnerSeat].Hp));
            if (result != SkillProgramStepOutcome.AwaitChild) AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage == ProgramBao2025Stage.AssaultCost)
        {
            ReplaceRuntimeTop(f = f with { Bao2025 = r with { Cards = choice.Cards, From = choice.Cards.Select(id => _cardZones.GetLocation(id)).ToArray(),
                Stage = ProgramBao2025Stage.AssaultTargets, Choices = [] } }); PublishBaoChoice(f); return;
        }
        if (r.Stage == ProgramBao2025Stage.AssaultTargets)
        {
            if (branch != "confirm")
            { ReplaceRuntimeTop(f = f with { Bao2025 = r with { SelectedTargets = r.SelectedTargets.Append(choice.Targets.Single()).ToArray(), Choices = [] } }); PublishBaoChoice(f); return; }
            PayBaoAssault(f); return;
        }
        if (r.Stage == ProgramBao2025Stage.DyingTarget)
        {
            ConsumeProgramTriggerUsage(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0));
            ReplaceRuntimeTop(f = f with { Bao2025 = r with { Recipient = choice.Targets.Single(), Stage = ProgramBao2025Stage.DyingRecipientChoice, Choices = [] } });
            PublishBaoChoice(f); return;
        }
        if (r.Stage != ProgramBao2025Stage.DyingRecipientChoice) throw new InvalidOperationException("The selected instruction has already been paid.");
        var recipient = _players[r.Recipient!.Value]; var replace = branch == "replace";
        var grants = replace ? recipient.SkillGrants.Grants.ToArray() : [];
        foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
        foreach (var grant in grants) if (!recipient.SkillGrants.RemoveGrant(grant.GrantId)) throw new InvalidOperationException("A recipient's skill grant changed before replacement.");
        var granted = GetProgramTrigger(f).Effects.Single().SkillIds[replace ? 0 : 1];
        ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.DyingSkillChildren, GrantedSkillId = granted,
            ReplacedSkills = replace, LostGrantIds = grants.Select(g => g.GrantId).ToArray(), Choices = [] }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AcquireRuntimeSkills(recipient, $"dying-recipient:{f.OwnerSeat}:{f.SkillInstanceId}:{f.Id}", [granted]);
        AdvanceEventRulesAndQueueFact(new DyingRecipientSkillChoiceEvent(f.Id, r.Source, recipient.Seat, replace, granted, f.Bao2025!.LostGrantIds));
        AdvanceRuntimeProgram(f.Id);
    }
    private void PayBaoGain(ProgramSkillFrame f, PromptChoice choice)
    {
        var r = f.Bao2025!; var target = r.Recipient!.Value;
        var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"], true);
        var from = new CardLocation(zone, target); var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var card = _cardZones.CardsAt(from)[slot];
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.GainChildren, GainedCardId = card.Id, GainedCardKind = card.Kind,
            GainedFrom = from, MovementBefore = before, MovementAfter = before, Choices = [] }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, card.IsGeneralWeapon && zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat), BaoGainReason);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { Bao2025 = f.Bao2025! with { MovementAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ProgramBao2025MovementIssuedEvent(f.Id, f.Bao2025!)); AdvanceRuntimeProgram(f.Id);
    }
    private void PayBaoAssault(ProgramSkillFrame f)
    {
        var r = f.Bao2025!;
        if (r.Cards.Count != 2 || r.Cards.Distinct().Count() != 2 || r.Cards.Select((id, i) => _cardZones.GetLocation(id) != r.From[i]).Any(v => v) ||
            r.Cards.Any(id => !BaoCostCards(_players[f.OwnerSeat]).Any(c => c.Id == id)) ||
            r.SelectedTargets.Any(t => !BaoAssaultLegalTargets(f).Contains(t)) || !BaoSourceCurrent(f))
            throw new InvalidOperationException("Chosen assault lost its two actual unpaid HE costs or explicit legal targets.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.AssaultCostChildren, MovementBefore = before, MovementAfter = before, Choices = [] },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(r.Cards, CardLocation.DiscardPile, BaoCostReason);
        ConsumeProgramTriggerUsage(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { Bao2025 = f.Bao2025! with { MovementAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ProgramBao2025MovementIssuedEvent(f.Id, f.Bao2025!)); AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainBaoChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 21600)]
    private bool ResumeBao2025(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Bao2025: { } r } f || f.Id != id) return false;
        RequireBao2025(f);
        if (r.Choices.Count > 0) { if (_pendingDecision is null) PublishBaoChoice(f); return true; }
        if (r.Stage == ProgramBao2025Stage.AssaultUse) throw new InvalidOperationException("The native chosen assault must return through its exact typed Use.");
        if (DrainBaoChildren(f)) return true;
        f = GetActiveProgramFrame(id); r = f.Bao2025!;
        if (r.Stage == ProgramBao2025Stage.GainChildren)
        {
            if (_players[f.OwnerSeat].IsAlive && CardUseCategoryCatalog.Get(r.GainedCardKind!.Value) != CardUseCategories.Basic)
            { ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.ResetChoice }, PendingMovementContinuation = null }); PublishBaoChoice(f); }
            else FinishBao2025(f, true); return true;
        }
        if (r.Stage == ProgramBao2025Stage.ResetHpChildren) { ResetBaoLimitedInstances(f); FinishBao2025(f, true); return true; }
        if (r.Stage == ProgramBao2025Stage.AssaultCostChildren) { IssueBaoAssault(f); return true; }
        if (r.Stage == ProgramBao2025Stage.AssaultOutcomes || r.Stage == ProgramBao2025Stage.AssaultOutcomeChildren) { AdvanceBaoOutcomes(f); return true; }
        if (r.Stage == ProgramBao2025Stage.DyingSkillChildren)
        {
            ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.DyingRecoveryChildren, RecoveryIssued = true } });
            if (_players[f.OwnerSeat].IsAlive && _players[f.OwnerSeat].Hp < 1)
                new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1 - _players[f.OwnerSeat].Hp, null, null);
            AdvanceRuntimeProgram(f.Id); return true;
        }
        if (r.Stage == ProgramBao2025Stage.DyingRecoveryChildren) { FinishBao2025(f, true); return true; }
        return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 21600)]
    private bool ReturnBaoMovement(ProgramSkillFrame f) => f.Bao2025 is not null && ResumeBao2025(f.Id);
    private void ResetBaoLimitedInstances(ProgramSkillFrame f)
    {
        var resetId = GetProgramTrigger(f).Effects.Single().SourceBind!; var entries = new List<LimitedSkillResetEntry>();
        foreach (var player in _players)
        foreach (var instance in GetSkillBindingShard(player).ProgramInstances.Where(i => i.SkillId == resetId))
        foreach (var trigger in instance.Program.Triggers.Where(t => t.UsageScope == SkillUsageScope.Game && t.UsageLimit == 1 &&
            t.Effects is [{ Op: SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes }]))
        {
            var candidate = new ProgramTriggerCandidate(player.Seat, resetId, trigger.Id, instance.SkillInstanceId, instance.Program.GameplayHash, trigger.Priority);
            var usage = ProgramTriggerUsageId(candidate); var before = _skillRuntimeState.GetUsage(player.Seat, resetId, usage, SkillUsageScope.Game);
            entries.Add(new(new(resetId, trigger.Id, player.Seat, instance.SkillInstanceId), instance.Program.GameplayHash, usage, before));
            _skillRuntimeState.ClearUsage(player.Seat, resetId, usage, SkillUsageScope.Game);
        }
        AdvanceEventRulesAndQueueFact(new ProgramLimitedSkillInstancesResetEvent(f.Id, BaoSource(f), Array.AsReadOnly(entries.ToArray())));
    }
    private void IssueBaoAssault(ProgramSkillFrame f)
    {
        var r = f.Bao2025!;
        if (!_players[f.OwnerSeat].IsAlive) { FinishBao2025(f, true); return; }
        var targets = r.SelectedTargets.Where(t => _players[t].IsAlive).ToArray();
        var reserved = _resolutionSequence + 1;
        ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.AssaultUse, AssaultUseId = reserved }, PendingMovementContinuation = null });
        var card = new Card(0, CardKind.BarbarianAssault, Suit.None, 0);
        var useId = BeginCardUse(card, f.OwnerSeat, targets, CardKind.BarbarianAssault, physicalCardIds: [], chosenAssault: new(f.Id, f.InstructionIndex));
        var use = LifecycleCardUse(useId)!;
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == use.ChosenAssault!.ProgramFrameId);
        if (parent.Id != f.Id || parent.InstructionIndex != use.ChosenAssault!.InstructionIndex || parent.Bao2025?.AssaultUseId != useId)
            throw new InvalidOperationException("Chosen assault lost its reserved typed producer while its native Use was issued.");
        ReplaceRuntimeFrame(parent.Id, parent with { Bao2025 = parent.Bao2025! with { AssaultActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new ChosenAssaultUseIssuedEvent(f.Id, r.Source, useId, use.Action!.ActionId, r.SelectedTargets));
        BeginJizhiOrNullificationWindow(useId, card, f.OwnerSeat, targets, LegalActionKind.BarbarianAssault, requiredCardKind: CardKind.Slash, playedCardKind: CardKind.BarbarianAssault);
    }
    [NativeUseReturn(220)]
    private void ReturnBaoAssault(CardUseFrame use)
    {
        if (use.ChosenAssault is not { } ret) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != ret.ProgramFrameId || f.InstructionIndex != ret.InstructionIndex ||
            f.Bao2025 is not { Stage: ProgramBao2025Stage.AssaultUse } r || r.AssaultUseId != use.Id || r.AssaultActionId != use.Action?.ActionId ||
            !CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e => e.ResolutionId == use.Id))
            throw new InvalidOperationException("The native assault lost its once-issued typed parent return.");
        ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.AssaultOutcomes }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceRuntimeProgram(f.Id);
    }
    private void AdvanceBaoOutcomes(ProgramSkillFrame f)
    {
        var r = f.Bao2025!;
        while (r.OutcomeCursor < r.SelectedTargets.Count)
        {
            var target = r.SelectedTargets[r.OutcomeCursor];
            var damaged = r.DamagedTargets.Contains(target);
            int? discarded = null; var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { Bao2025 = r with { Stage = ProgramBao2025Stage.AssaultOutcomeChildren, OutcomeCursor = r.OutcomeCursor + 1,
                MovementBefore = before, MovementAfter = before } });
            if (damaged && _players[target].IsAlive)
            {
                var cards = GetHand(_players[target]).Concat(GetEquipment(_players[target])).Where(c => !c.IsGeneralWeapon).ToArray();
                if (cards.Length > 0) { var card = cards[_random.Next(cards.Length)]; discarded = card.Id;
                    MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.DiscardPile, BaoOutcomeReason); }
            }
            else if (!damaged && _players[f.OwnerSeat].IsAlive && _players[target].IsAlive)
            {
                var grant = _turnCardUseEffects.GrantTargetCardQuotaAllowance(r.ActualTurn, r.ActualTurnOwner, f.Id, r.OutcomeCursor,
                    new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId), target, r.AssaultUseId!.Value);
                AdvanceEventRulesAndQueueFact(new TurnTargetCardQuotaAllowanceGrantedEvent(grant));
            }
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { Bao2025 = f.Bao2025! with { MovementAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new ChosenAssaultTargetOutcomeEvent(f.Id, r.AssaultUseId!.Value, target, damaged, discarded));
            if (DrainBaoChildren(f)) return;
            f = GetActiveProgramFrame(f.Id); r = f.Bao2025!;
        }
        FinishBao2025(f, true);
    }
    private void FinishBao2025(ProgramSkillFrame f, bool completed)
    {
        AdvanceEventRulesAndQueueFact(new ProgramBao2025CompletedEvent(f.Id, BaoSource(f), completed));
        ReplaceRuntimeTop(f = f with { Bao2025 = null, PendingMovementContinuation = null }); FinishProgramSkill(f, completed);
    }
    [ProgramCapability]
    private sealed class Bao2025ProgramCapability(GameEngine engine) : IBao2025ProgramHost
    { public SkillProgramStepOutcome BeginBao2025(ProgramSkillFrame f, SkillProgramEffect e) => engine.BeginBao2025(f, e); }
}
