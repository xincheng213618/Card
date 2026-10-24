using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiTfGanggeng = "mobile:shi-ganggeng", ShiTfSijian = "mobile:shi-sijian";
    private const string ShiTfDiscard = "skill.mobile-shi-tian-feng.discard", ShiTfDraw = "skill.mobile-shi-tian-feng.draw";
    private static CardConversionSource ShiTfSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private bool ShiTfNobodyDying() => !_players.Any(p => p.IsAlive && p.HasHpValue && p.Hp <= 0);
    private int ShiTfBackwaters(int owner) => ProgramEventHistory<MobileShiTianFengBackwaterEvent>().Count(e => e.Source.OwnerSeat == owner);
    private IEnumerable<MobileShiTianFengGiftEvent> ShiTfOutstandingGifts(int owner, string instance, string hash) =>
        ProgramEventHistory<MobileShiTianFengGiftEvent>().Where(gift => gift.Source.OwnerSeat == owner &&
            gift.Source.SkillInstanceId == instance && gift.GameplayHash == hash && gift.ActualTurn == _turnNumber &&
            !ProgramEventHistory<MobileShiTianFengGiftSettledEvent>().Any(done => done.GiftFrameId == gift.FrameId));
    private IEnumerable<MobileShiTianFengDebtIssuedEvent> ShiTfUseDebts(int owner, string instance, string hash, long action, int actor) =>
        ProgramEventHistory<MobileShiTianFengDebtIssuedEvent>().Where(debt => debt.Source.OwnerSeat == owner &&
            debt.Source.SkillInstanceId == instance && debt.GameplayHash == hash && debt.TargetSeat == actor &&
            !ProgramEventHistory<MobileShiTianFengDebtClaimedEvent>().Any(done => done.IssuingFrameId == debt.FrameId) &&
            ProgramEventHistory<CardActionAcceptedEvent>().Select(e => e.Action).Where(a => a.ActionId > debt.AfterActionId &&
                a.ActorSeat == actor && IsAlternateColorActualUse(a)).OrderBy(a => a.ActionId).FirstOrDefault()?.ActionId == action);

    [ProgramCapability]
    private sealed class MobileShiTianFengCapability(GameEngine engine) : IMobileShiTianFengHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginMobileShiTianFeng(frame); }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47500)]
    private bool CanOfferMobileShiTianFeng(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == ShiTfGanggeng)
            return trigger.Id == "gift-turn-ending" && ShiTfOutstandingGifts(candidate.OwnerSeat, candidate.SkillInstanceId, candidate.GameplayHash).Any();
        if (candidate.SkillId != ShiTfSijian) return true;
        if (trigger.Id != "next-use-debt") return _players[candidate.OwnerSeat].IsAlive &&
            (_players[_currentSeat].IsAlive || _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat));
        return context is { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } use } &&
            _players[use.ActorSeat].IsAlive && ShiTfUseDebts(candidate.OwnerSeat, candidate.SkillInstanceId, candidate.GameplayHash,
                use.CardActionId, use.ActorSeat).Any();
    }

    private SkillProgramStepOutcome BeginMobileShiTianFeng(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.MobileShiTianFeng is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("Shi Tian Feng requires his original unpaid instruction.");
        if (f.ActivationId == "gift-at-least-two" && f.TriggerId is null && f.SkillId == ShiTfGanggeng &&
            f.InstructionIndex == 2 && f.SelectedTargetSeats is [var recipient] && f.SelectedCardIds.Count >= 2)
        {
            AdvanceEventRulesAndQueueFact(new MobileShiTianFengGiftEvent(f.Id, ShiTfSource(f), f.GameplayHash,
                _turnNumber, recipient, f.SelectedCardIds.Count));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.TriggerId is null || f.WindowContext is null || f.InstructionIndex != 1)
            throw new InvalidOperationException("Shi Tian Feng lost his native trigger or paid gift.");
        var operation = f.SkillId == ShiTfGanggeng && f.TriggerId == "gift-turn-ending" ? "ending" :
            f.SkillId == ShiTfSijian && f.TriggerId == "next-use-debt" ? "debt" :
            f.SkillId == ShiTfSijian && f.TriggerId is "last-hand" or "enter-dying" ? "sijian" :
            throw new InvalidOperationException("Shi Tian Feng instruction has an incompatible owning skill.");
        ReplaceRuntimeTop(f = f with { MobileShiTianFeng = new(operation, _turnNumber) });
        ContinueMobileShiTianFeng(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> ShiTfChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShiTianFeng!; var choices = new List<PromptChoice>();
        void Add(string branch, string text, int? target = null) => choices.Add(new(new($"shi-tian-feng.{f.Id}.{branch}.{target}"), text,
            [], target is { } seat ? [seat] : [], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-tian-feng", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch }));
        if (r.Operation == "sijian" && r.Stage == 0)
        {
            if (_players[_currentSeat].IsAlive) Add("draw", $"令当前回合角色 {_players[_currentSeat].Name} 摸两张牌");
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat))
            {
                Add("debt", $"令 {p.Name} 使用下一张牌后弃置一张牌", p.Seat);
                if (ShiTfNobodyDying() && _players[_currentSeat].IsAlive)
                    Add("backwater", $"背水：令 {p.Name} 下次用牌后弃牌、当前回合角色摸两张，然后失去 {ShiTfBackwaters(f.OwnerSeat) + 1} 点体力", p.Seat);
            }
        }
        else if (r.Stage == 3 && _players[r.TargetSeat].IsAlive)
        {
            var chooser = r.Operation == "debt" ? r.TargetSeat : f.OwnerSeat;
            foreach (var old in BuildOwnedCardPaymentChoices(f.Id, chooser, r.TargetSeat,
                r.Operation == "debt" ? [CardZoneKind.Hand, CardZoneKind.Equipment] : [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment], OwnedCardMoveIntent.Discard))
                choices.Add(old with { Parameters = new Dictionary<string, string>(old.Parameters)
                    { ["program-action"] = "mobile-shi-tian-feng", ["branch"] = "discard" } });
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShiTf(ProgramSkillFrame f)
    {
        var r = f.MobileShiTianFeng!; var choices = ShiTfChoices(f);
        if (choices.Count == 0) { FinishShiTfStep(f); return; }
        var chooser = r.Operation == "debt" ? r.TargetSeat : f.OwnerSeat;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            r.Stage == 3 ? r.Operation == "debt" ? "死谏：使用下一张牌后，弃置一张手牌或装备牌。" : "刚鲠：受赠者手牌未达到全场最多，弃置其区域内的一张牌。" :
                $"死谏：选择一项效果；背水累计 {ShiTfBackwaters(f.OwnerSeat)} 次，下次失去 {ShiTfBackwaters(f.OwnerSeat) + 1} 点体力。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.Stage == 3 ? r.TargetSeat : chooser, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("mobile-shi-tian-feng")]
    private void AnswerMobileShiTianFeng(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiTianFeng: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Operation == "debt" ? r.TargetSeat : f.OwnerSeat) ||
            !ShiTfChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Shi Tian Feng choice lost its current original frame, chooser or legal branch.");
        ClearPendingDecision(); var branch = selected.Parameters["branch"];
        if (branch == "discard")
        {
            var zone = Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]);
            var cards = zone == CardZoneKind.Hand ? GetHand(_players[r.TargetSeat]) : zone == CardZoneKind.Equipment ? GetEquipment(_players[r.TargetSeat]).ToArray() : GetJudgment(_players[r.TargetSeat]);
            var card = cards[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)].Id;
            ReplaceRuntimeTop(f = f with { MobileShiTianFeng = r with { Stage = 9, CardId = card, Before = _movementSequence },
                PendingMovementContinuation = new(r.TargetSeat, 0, null) });
            MoveProgramLogicalDiscard(f, r.Operation == "debt" ? r.TargetSeat : f.OwnerSeat, [card], new(ShiTfDiscard));
            ContinueMobileShiTianFeng(f.Id); return;
        }
        var backwater = branch == "backwater";
        var target = selected.Targets.Count == 1 ? selected.Targets[0] : -1;
        var hpLoss = backwater ? ShiTfBackwaters(f.OwnerSeat) + 1 : 0;
        ReplaceRuntimeTop(f = f with { MobileShiTianFeng = r with { Stage = branch == "debt" ? 9 : 4, Backwater = backwater, HpLoss = hpLoss, TargetSeat = target } });
        if (target >= 0)
        {
            var after = ProgramEventHistory<CardActionAcceptedEvent>().Select(e => e.Action.ActionId).DefaultIfEmpty().Max();
            AdvanceEventRulesAndQueueFact(new MobileShiTianFengDebtIssuedEvent(f.Id, ShiTfSource(f), f.GameplayHash, target, after));
        }
        if (backwater) AdvanceEventRulesAndQueueFact(new MobileShiTianFengBackwaterEvent(f.Id, ShiTfSource(f), hpLoss));
        ContinueMobileShiTianFeng(f.Id);
    }

    private bool DrainShiTf(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueMobileShiTianFeng(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiTianFeng: { } r } f || f.Id != id) return;
        if (r.Stage is 6 or 8 or 9)
        {
            if (DrainShiTf(f)) return;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.MobileShiTianFeng!;
            if (r.Stage == 6 && r.Backwater && _players[f.OwnerSeat].IsAlive)
            {
                ReplaceRuntimeTop(f = f with { MobileShiTianFeng = r with { Stage = 8 } });
                if (ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, r.HpLoss) == SkillProgramStepOutcome.AwaitChild) return;
                ContinueMobileShiTianFeng(id); return;
            }
            FinishShiTfStep(f); return;
        }
        if (r.Operation == "sijian" && r.Stage == 0 || r.Stage == 3) { PublishShiTf(f); return; }
        if (r.Stage == 4)
        {
            if (!_players[_currentSeat].IsAlive) { FinishShiTfStep(f); return; }
            BeginOperationProgramDraw(f, _currentSeat, 2, new(ShiTfDraw)); return;
        }
        if (r.Stage == 2) { BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(ShiTfDraw)); return; }
        if (r.Operation == "ending")
        {
            var gift = ShiTfOutstandingGifts(f.OwnerSeat, f.SkillInstanceId, f.GameplayHash).FirstOrDefault();
            if (gift is null) { FinishProgramSkill(f, true); return; }
            var alive = _players[gift.TargetSeat].IsAlive;
            var maximum = alive && GetHand(_players[gift.TargetSeat]).Count == _players.Where(p => p.IsAlive).Max(p => GetHand(p).Count);
            ReplaceRuntimeTop(f = f with { MobileShiTianFeng = r with { ClaimId = gift.FrameId, TargetSeat = gift.TargetSeat, Stage = maximum ? 2 : 3 } });
            AdvanceEventRulesAndQueueFact(new MobileShiTianFengGiftSettledEvent(gift.FrameId, f.Id, gift.TargetSeat, maximum));
            if (!alive) { FinishShiTfStep(f); return; }
            ContinueMobileShiTianFeng(id); return;
        }
        if (r.Operation == "debt")
        {
            var use = f.WindowContext!.CardUse!;
            var debt = ShiTfUseDebts(f.OwnerSeat, f.SkillInstanceId, f.GameplayHash, use.CardActionId, use.ActorSeat).FirstOrDefault();
            if (debt is null) { FinishProgramSkill(f, true); return; }
            ReplaceRuntimeTop(f = f with { MobileShiTianFeng = r with { ClaimId = debt.FrameId, TargetSeat = use.ActorSeat, ActionId = use.CardActionId, Stage = 3 } });
            AdvanceEventRulesAndQueueFact(new MobileShiTianFengDebtClaimedEvent(debt.FrameId, f.Id, use.CardActionId, use.ActorSeat));
            PublishShiTf(f); return;
        }
        throw new InvalidOperationException("Shi Tian Feng has an unknown native continuation stage.");
    }

    private void FinishShiTfStep(ProgramSkillFrame f)
    {
        var r = f.MobileShiTianFeng!;
        if (r.Operation == "sijian") { FinishProgramSkill(f, true); return; }
        ReplaceRuntimeTop(f = f with { MobileShiTianFeng = new(r.Operation, r.ActualTurn) });
        ContinueMobileShiTianFeng(f.Id);
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiTianFeng)]
    private void ReturnShiTfDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MobileShiTianFeng is not { Stage: 2 or 4, Draw: null } r ||
            outcome.RecipientSeat != (r.Stage == 2 ? f.OwnerSeat : _currentSeat) || outcome.RequestedCount != (r.Stage == 2 ? 1 : 2))
            throw new InvalidOperationException("Shi Tian Feng lost his original once-issued native draw.");
        ReplaceRuntimeTop(f = f with { MobileShiTianFeng = r with { Stage = 6, Draw = outcome, Before = outcome.SequenceBefore },
            PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        ContinueMobileShiTianFeng(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 47500)]
    private bool ResumeShiTf(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiTianFeng: not null } f || f.Id != id) return false; ContinueMobileShiTianFeng(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47500)]
    private bool ReturnShiTfMovement(ProgramSkillFrame f)
    { if (f.MobileShiTianFeng is null) return false; ContinueMobileShiTianFeng(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47500)]
    private bool ContinuePaidShiTf(ProgramSkillFrame f) => f.MobileShiTianFeng is { Stage: > 0 };
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 47500)]
    private bool ShiTfMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.MobileShiTianFeng is { Stage: 6 or 8 or 9 } r && effect?.Op == SkillProgramEffectOp.ResolveMobileShiTianFeng &&
        p.CoverageResultBind is null && p.BeforeCount == 0 && p.SubjectSeat ==
            (r.Stage == 6 ? r.Operation == "ending" ? f.OwnerSeat : _currentSeat : r.TargetSeat);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47500)]
    private void AssertShiTf(ProgramSkillFrame f)
    {
        if (f.MobileShiTianFeng is not { } r) return;
        if (f.TriggerId is null || f.WindowContext is null || f.InstructionIndex != 1 || r.ActualTurn != _turnNumber ||
            r.Stage is not (0 or 2 or 3 or 4 or 6 or 8 or 9) || r.HpLoss < 0 || r.Before > _movementSequence ||
            r.Operation == "ending" && (f.SkillId != ShiTfGanggeng || f.TriggerId != "gift-turn-ending") ||
            r.Operation == "debt" && (f.SkillId != ShiTfSijian || f.TriggerId != "next-use-debt") ||
            r.Operation == "sijian" && (f.SkillId != ShiTfSijian || f.TriggerId is not ("last-hand" or "enter-dying")) ||
            r.Operation is not ("ending" or "debt" or "sijian") ||
            r.Backwater && (r.Operation != "sijian" || r.HpLoss < 1 ||
                !ProgramEventHistory<MobileShiTianFengBackwaterEvent>().Any(e => e.FrameId == f.Id && e.Source == ShiTfSource(f) && e.Ordinal == r.HpLoss)))
            throw new InvalidOperationException("Shi Tian Feng lost his original typed trigger, paid branch or monotone backwater receipt.");
        if (r.Operation == "ending" && r.Stage > 0 &&
            (!IsValidPlayerSeat(r.TargetSeat) || !ProgramEventHistory<MobileShiTianFengGiftEvent>().Any(g =>
                g.FrameId == r.ClaimId && g.Source.OwnerSeat == f.OwnerSeat && g.Source.SkillInstanceId == f.SkillInstanceId &&
                g.GameplayHash == f.GameplayHash && g.ActualTurn == r.ActualTurn && g.TargetSeat == r.TargetSeat) ||
             !ProgramEventHistory<MobileShiTianFengGiftSettledEvent>().Any(g => g.GiftFrameId == r.ClaimId && g.FrameId == f.Id && g.TargetSeat == r.TargetSeat)) ||
            r.Operation == "debt" && r.Stage > 0 &&
            (!IsValidPlayerSeat(r.TargetSeat) || f.WindowContext.CardUse is not { } use || use.CardActionId != r.ActionId || use.ActorSeat != r.TargetSeat ||
             !ProgramEventHistory<MobileShiTianFengDebtIssuedEvent>().Any(d => d.FrameId == r.ClaimId &&
                d.Source.OwnerSeat == f.OwnerSeat && d.Source.SkillInstanceId == f.SkillInstanceId && d.GameplayHash == f.GameplayHash && d.TargetSeat == r.TargetSeat) ||
             !ProgramEventHistory<MobileShiTianFengDebtClaimedEvent>().Any(d => d.IssuingFrameId == r.ClaimId && d.FrameId == f.Id && d.ActionId == r.ActionId && d.TargetSeat == r.TargetSeat)) ||
            r.Operation == "sijian" && r.Stage > 0 && r.TargetSeat >= 0 &&
            !ProgramEventHistory<MobileShiTianFengDebtIssuedEvent>().Any(d => d.FrameId == f.Id && d.Source == ShiTfSource(f) && d.GameplayHash == f.GameplayHash && d.TargetSeat == r.TargetSeat))
            throw new InvalidOperationException("Shi Tian Feng lost his issued gift, exact next actual use or original target obligation.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiTianFeng)]
    private PromptChoice ChooseShiTf(PendingDecision prompt) => prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "draw") ?? prompt.Choices[0];

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiTianFengPublicStates(CharacterState owner, string skillId)
    {
        if (skillId == ShiTfGanggeng)
            return Array.AsReadOnly(GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i =>
                ShiTfOutstandingGifts(owner.Seat, i.SkillInstanceId, i.Program.GameplayHash).Select(g => new ProgramPublicRuleStateSnapshot(
                    i.SkillInstanceId, "ganggeng-target-" + g.TargetSeat.ToString(CultureInfo.InvariantCulture),
                    ProgramPublicRuleStateKind.GameActivationProgress, g.CardCount, 0, SkillUsageScope.Turn))).ToArray());
        if (skillId != ShiTfSijian) return [];
        return Array.AsReadOnly(GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i => new[] {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "sijian", ProgramPublicRuleStateKind.ActivationLimit, 2,
                _skillRuntimeState.GetUsage(owner.Seat, skillId, "named-group:sijian", SkillUsageScope.Turn), SkillUsageScope.Turn),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "sijian-backwater", ProgramPublicRuleStateKind.GameActivationProgress,
                ShiTfBackwaters(owner.Seat) + 1, ShiTfBackwaters(owner.Seat), SkillUsageScope.Game),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "sijian-debts", ProgramPublicRuleStateKind.GameActivationProgress,
                ProgramEventHistory<MobileShiTianFengDebtIssuedEvent>().Count(d => d.Source.OwnerSeat == owner.Seat && _players[d.TargetSeat].IsAlive &&
                    !ProgramEventHistory<MobileShiTianFengDebtClaimedEvent>().Any(c => c.IssuingFrameId == d.FrameId)), 0, SkillUsageScope.Game)
        }).ToArray());
    }
}
