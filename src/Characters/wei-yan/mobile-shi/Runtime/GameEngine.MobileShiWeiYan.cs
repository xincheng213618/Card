using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiWyZhuangshi = "mobile:shi-zhuangshi", ShiWyYinzhan = "mobile:shi-yinzhan",
        ShiWyZhongao = "mobile:shi-zhongao", ShiWyKuanggu = "mobile:shi-kuanggu",
        ShiWyKuanggu2 = "mobile:shi-kuanggu-upgraded", ShiWyKunfen = "mobile:shi-kunfen";
    private const string ShiWyDiscard = "skill.mobile-shi-wei-yan.discard", ShiWyObtain = "skill.mobile-shi-wei-yan.obtain";
    private bool ShiWyMissionOver(int seat) => ProgramEventHistory<MobileShiWeiYanMissionEvent>().Any(e => e.OwnerSeat == seat);
    private int ShiWyPhaseUses(int seat, int turn, int phase) => ProgramEventHistory<MobileShiWeiYanUseEvent>()
        .Count(e => e.OwnerSeat == seat && e.TurnNumber == turn && e.PhaseInstanceId == phase);
    private MobileShiWeiYanPhaseGrantEvent? ShiWyPhaseGrant(int seat) => _phase == TurnPhase.Play && seat == _currentSeat
        ? ProgramEventHistory<MobileShiWeiYanPhaseGrantEvent>().LastOrDefault(e => e.OwnerSeat == seat &&
            e.TurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId) : null;

    [ProgramCapability]
    private sealed class MobileShiWeiYanCapability(GameEngine engine) : IMobileShiWeiYanHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) =>
            engine.BeginMobileShiWeiYan(frame, operation);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47100)]
    private bool CanOfferMobileShiWeiYan(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId == ShiWyZhuangshi) return gate.Owner.IsAlive && gate.Owner.Seat == _currentSeat;
        if (gate.SkillId == ShiWyZhongao)
            return gate.Trigger.Id == "initialize" || !ShiWyMissionOver(gate.OwnerSeat) &&
                (gate.Trigger.Id == "failure" || gate.Context.SourceSeat == gate.OwnerSeat);
        if (gate.SkillId is ShiWyKuanggu or ShiWyKuanggu2)
            return gate.Context.SourceSeat == gate.OwnerSeat && gate.Owner.IsAlive;
        if (gate.SkillId != ShiWyYinzhan) return true;
        return gate.Context.CardUse is { } use && ProgramEventHistory<MobileShiYinzhanQualifiedEvent>()
            .Any(e => e.UseId == use.ParentCardUseFrameId && e.OwnerSeat == gate.OwnerSeat && e.HandQualified &&
                ProgramEventHistory<MobileShiYinzhanAppliedEvent>().Any(a => a.QualificationId == e.Id) &&
                !ProgramEventHistory<MobileShiYinzhanClaimedEvent>().Any(a => a.QualificationId == e.Id));
    }

    private SkillProgramStepOutcome BeginMobileShiWeiYan(ProgramSkillFrame input, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.MobileShiWeiYan is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("Shi Wei Yan requires a fresh instruction on its original native trigger.");
        var branch = operation switch
        {
            SkillProgramEffectOp.ResolveMobileShiZhuangshi => "zhuangshi",
            SkillProgramEffectOp.ResolveMobileShiYinzhan => "yinzhan",
            SkillProgramEffectOp.ResolveMobileShiZhongao => "zhongao",
            _ => "kuanggu"
        };
        if (branch == "zhuangshi" && f.SkillId != ShiWyZhuangshi || branch == "yinzhan" && f.SkillId != ShiWyYinzhan ||
            branch == "zhongao" && f.SkillId != ShiWyZhongao || branch == "kuanggu" && f.SkillId is not (ShiWyKuanggu or ShiWyKuanggu2))
            throw new InvalidOperationException("Shi Wei Yan instruction has an incompatible owning skill.");
        ReplaceRuntimeTop(f = f with { MobileShiWeiYan = new(branch, 0, _turnNumber, _cardUseDebitPhaseInstanceId) });
        if (branch == "zhongao")
        {
            CommitShiWyMission(f, f.TriggerId == "success");
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { MobileShiWeiYan = f.MobileShiWeiYan! with { Stage = 8 } });
            if (f.TriggerId == "success" && ShiWyPhaseGrant(f.OwnerSeat) is { } grant)
            {
                var used = ShiWyPhaseUses(f.OwnerSeat, grant.TurnNumber, grant.PhaseInstanceId);
                if (used < grant.DiscardedCards) DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw);
                if (used < grant.LostHp)
                {
                    if (IsRuleWounded(_players[f.OwnerSeat])) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
                    else DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw);
                }
            }
            ContinueMobileShiWeiYan(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (branch == "yinzhan") { ContinueMobileShiWeiYan(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        PublishMobileShiWeiYan(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private void CommitShiWyMission(ProgramSkillFrame source, bool success)
    {
        if (ShiWyMissionOver(source.OwnerSeat)) return;
        var instance = GetSkillBindingShard(_players[source.OwnerSeat]).ProgramInstances.SingleOrDefault(i => i.SkillId == ShiWyZhongao);
        if (instance is null) return;
        SetProgramBooleanState(source, ShiWyZhongao, instance.SkillInstanceId, success ? "succeeded" : "failed", true);
        AdvanceEventRulesAndQueueFact(new MobileShiWeiYanMissionEvent(source.Id, source.OwnerSeat, success));
        ReplaceProgramSkills(source, success ? [ShiWyKuanggu] : [ShiWyZhuangshi],
            success ? [ShiWyKuanggu2] : [ShiWyKunfen], ProgramSkillSourceRemoval.Enabled, acquireFirst: true);
    }

    private int[] ShiWyDiscardable(int seat, bool handOnly) => GetHand(_players[seat])
        .Concat(handOnly ? [] : GetEquipment(_players[seat])).Where(card =>
            !IsSelfHandCategoryDiscardForbidden(seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard) &&
            !IsForeignEquipmentDiscardPrevented(seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard))
        .Select(card => card.Id).ToArray();

    private IReadOnlyList<PromptChoice> MobileShiWeiYanChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShiWeiYan!; var choices = new List<PromptChoice>();
        void Add(string id, string description, int[]? cards = null, int[]? targets = null, string? branch = null, bool? selected = null)
        {
            var parameters = new Dictionary<string, string> { ["program-action"] = "mobile-shi-wei-yan", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch ?? id };
            if (selected is { } value) parameters["selected"] = value ? "true" : "false";
            choices.Add(new(new($"shi-wei-yan.{f.Id}.{id}"), description, cards ?? [], targets ?? [], parameters));
        }
        if (r.Operation == "zhuangshi")
        {
            foreach (var id in ShiWyDiscardable(f.OwnerSeat, true))
                Add("card-" + id, (r.Cards.Contains(id) ? "取消弃置" : "选择弃置") + GetAdvancedCard(id).DisplayName, [id], selected: r.Cards.Contains(id));
            for (var n = 0; n <= Math.Max(0, _players[f.OwnerSeat].Hp); n++) Add("hp-" + n, $"失去 {n} 点体力" + (r.HpLoss == n ? "（已选）" : ""), selected: r.HpLoss == n);
            if (r.Cards.Count + r.HpLoss > 0) Add("pay", $"确认：弃置 {r.Cards.Count} 张手牌，失去 {r.HpLoss} 点体力");
            Add("decline", "不执行壮誓" + (ShiWyMissionOver(f.OwnerSeat) ? "" : "（忠傲使命失败）"));
        }
        else if (r.Operation == "kuanggu")
        {
            if (r.Stage == 4)
            {
                foreach (var id in ShiWyDiscardable(f.OwnerSeat, false)) Add("kuanggu-cost-" + id, "背水：弃置" + GetAdvancedCard(id).DisplayName, [id], branch: "kuanggu-cost");
                return Array.AsReadOnly(choices.ToArray());
            }
            if (IsRuleWounded(_players[f.OwnerSeat])) Add("recover", "回复1点体力");
            Add("draw", "摸1张牌");
            if (f.SkillId == ShiWyKuanggu2) Add("backwater",
                "背水：回复1点体力并摸1张牌，然后弃置一张牌，本阶段可用杀次数+1");
            Add("cancel", "取消狂骨");
        }
        else if (r.Operation == "yinzhan")
            foreach (var old in BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.TargetSeat,
                [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
                destination: SkillProgramCardDestination.DiscardPile))
                choices.Add(old with { Parameters = new Dictionary<string, string>(old.Parameters)
                    { ["program-action"] = "mobile-shi-wei-yan", ["branch"] = "discard", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) } });
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishMobileShiWeiYan(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.MobileShiWeiYan!.Operation == "zhuangshi"
                ? $"壮誓已选：弃置 {f.MobileShiWeiYan.Cards.Count} 张手牌，失去 {f.MobileShiWeiYan.HpLoss} 点体力。"
                : $"【{skill.Name}】请选择。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description), Choices = MobileShiWeiYanChoices(f) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("mobile-shi-wei-yan")]
    private void AnswerMobileShiWeiYan(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiWeiYan: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !MobileShiWeiYanChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Shi Wei Yan lost its exact current private payment or choice.");
        var branch = selected.Parameters["branch"]; ClearPendingDecision();
        if (r.Operation == "zhuangshi")
        {
            if (branch.StartsWith("card-", StringComparison.Ordinal))
            {
                var id = selected.Cards.Single();
                ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Cards = r.Cards.Contains(id) ? r.Cards.Where(c => c != id).ToArray() : r.Cards.Append(id).ToArray() } });
                PublishMobileShiWeiYan(f); return;
            }
            if (branch.StartsWith("hp-", StringComparison.Ordinal))
            { ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { HpLoss = int.Parse(branch.AsSpan(3), CultureInfo.InvariantCulture) } }); PublishMobileShiWeiYan(f); return; }
            if (branch == "decline")
            {
                ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 8 } });
                CommitShiWyMission(f, false); ContinueMobileShiWeiYan(f.Id); return;
            }
            ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 1 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (r.Cards.Count > 0) MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Cards, new(ShiWyDiscard));
            ContinueMobileShiWeiYan(f.Id); return;
        }
        if (r.Operation == "kuanggu")
        {
            if (branch == "cancel") { FinishProgramSkill(f, true); return; }
            if (branch == "kuanggu-cost")
            {
                var paid = selected.Cards.Single();
                ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 5, CardId = paid }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                MoveProgramLogicalDiscard(f, f.OwnerSeat, [paid], new(ShiWyDiscard)); ContinueMobileShiWeiYan(f.Id); return;
            }
            var backwater = branch == "backwater";
            ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = backwater ? 4 : 8 } });
            if (branch == "recover" || backwater) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
            if (branch == "draw" || backwater) DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw);
            ContinueMobileShiWeiYan(f.Id); return;
        }
        var idToDiscard = selected.Cards.Count == 1 ? selected.Cards[0] :
            GetHand(_players[r.TargetSeat])[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)].Id;
        ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 6, CardId = idToDiscard }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [idToDiscard], new(ShiWyDiscard));
        ContinueMobileShiWeiYan(f.Id);
    }

    private bool DrainMobileShiWeiYan(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueMobileShiWeiYan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiWeiYan: { } r } f || f.Id != id || _pendingDecision is not null) return;
        if (r.Stage > 0 && DrainMobileShiWeiYan(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.MobileShiWeiYan!;
        if (r.Operation == "zhuangshi" && r.Stage is 1 or 2 or 3)
        {
            if (r.Stage == 1 && r.HpLoss > 0 && _players[f.OwnerSeat].IsAlive)
            {
                ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 3 } });
                var outcome = ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, r.HpLoss);
                if (outcome == SkillProgramStepOutcome.AwaitChild) return;
                ContinueMobileShiWeiYan(id); return;
            }
            AdvanceEventRulesAndQueueFact(new MobileShiWeiYanPhaseGrantEvent(f.Id, f.OwnerSeat, r.TurnNumber, r.PhaseInstanceId, r.Cards.Count, r.HpLoss)
                { Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) });
            FinishProgramSkill(GetActiveProgramFrame(id), true); return;
        }
        if (r.Operation == "kuanggu" && r.Stage == 4)
        {
            if (ShiWyDiscardable(f.OwnerSeat, false).Length > 0) { PublishMobileShiWeiYan(f); return; }
            FinishProgramSkill(f, true); return;
        }
        if (r.Operation == "kuanggu" && r.Stage == 5)
            AdvanceEventRulesAndQueueFact(new MobileShiKuangguSlashGrantedEvent(f.Id, f.OwnerSeat, r.TurnNumber, r.PhaseInstanceId));
        if (r.Operation == "yinzhan")
        {
            if (r.Stage == 6 && r.Chengshi)
            {
                ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 7 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
                if (r.CardId is { } discarded && _cardZones.GetLocation(discarded) == CardLocation.DiscardPile)
                    MoveProgramCardsFromMultipleSources([discarded], CardLocation.Hand(f.OwnerSeat), new(ShiWyObtain));
                ContinueMobileShiWeiYan(id); return;
            }
            var useId = f.WindowContext!.CardUse!.ParentCardUseFrameId;
            var next = ProgramEventHistory<MobileShiYinzhanQualifiedEvent>().FirstOrDefault(e => e.UseId == useId && e.OwnerSeat == f.OwnerSeat && e.HandQualified &&
                ProgramEventHistory<MobileShiYinzhanAppliedEvent>().Any(a => a.QualificationId == e.Id) &&
                !ProgramEventHistory<MobileShiYinzhanClaimedEvent>().Any(a => a.QualificationId == e.Id));
            if (next is not null)
            {
                AdvanceEventRulesAndQueueFact(new MobileShiYinzhanClaimedEvent(next.Id, f.Id));
                ReplaceRuntimeTop(f = f with { MobileShiWeiYan = r with { Stage = 0, TargetSeat = next.TargetSeat, CardId = null, ClaimId = next.Id, Chengshi = next.HpQualified } });
                if (_players[next.TargetSeat].IsAlive && MobileShiWeiYanChoices(f).Count > 0) { PublishMobileShiWeiYan(f); return; }
                ReplaceRuntimeTop(f with { MobileShiWeiYan = f.MobileShiWeiYan! with { Stage = 6 } });
                ContinueMobileShiWeiYan(id); return;
            }
        }
        FinishProgramSkill(GetActiveProgramFrame(id), true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 47100)]
    private bool ResumeMobileShiWeiYan(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiWeiYan: not null } f || f.Id != id) return false; ContinueMobileShiWeiYan(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47100)]
    private bool ReturnMobileShiWeiYan(ProgramSkillFrame f)
    { if (f.MobileShiWeiYan is null) return false; ContinueMobileShiWeiYan(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47100)]
    private bool ContinuePaidMobileShiWeiYan(ProgramSkillFrame f) => f.MobileShiWeiYan is { Stage: > 0 };
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 47100)]
    private bool MobileShiWeiYanMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.MobileShiWeiYan is { Stage: > 0 } && e?.Op is SkillProgramEffectOp.ResolveMobileShiZhuangshi or SkillProgramEffectOp.ResolveMobileShiYinzhan or SkillProgramEffectOp.ResolveMobileShiKuanggu;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47100)]
    private void AssertMobileShiWeiYanPending(ProgramSkillFrame f, ProgramExecutionPlan plan)
    {
        if (f.MobileShiWeiYan is not { } r) return;
        if (r.TurnNumber != _turnNumber || r.PhaseInstanceId < 0 || r.Stage is < 0 or > 8 || r.HpLoss < 0 ||
            r.Cards.Count != r.Cards.Distinct().Count() || f.TriggerId is null || f.WindowContext is null ||
            r.Operation == "zhuangshi" && (f.SkillId != ShiWyZhuangshi || f.WindowContext.Window != SkillProgramTriggerWindow.PlayPhaseStarting) ||
            r.Operation == "yinzhan" && (f.SkillId != ShiWyYinzhan || f.WindowContext.Window != SkillProgramTriggerWindow.CardUseCompleted) ||
            r.Operation == "zhongao" && f.SkillId != ShiWyZhongao || r.Operation == "kuanggu" && f.SkillId is not (ShiWyKuanggu or ShiWyKuanggu2))
            throw new InvalidOperationException("Shi Wei Yan pending state lost its owning actual trigger, phase, payment or finite stage.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiZhuangshi,
        SkillProgramEffectOp.ResolveMobileShiYinzhan, SkillProgramEffectOp.ResolveMobileShiKuanggu)]
    private PromptChoice SelectMobileShiWeiYanAi(PendingDecision decision)
    {
        var choices = decision.Choices;
        return choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "pay") ??
            choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "hp-1" && _players[decision.PlayerSeat].Hp > 1) ??
            choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "recover") ??
            choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "draw") ??
            choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "discard") ??
            choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "decline") ?? choices[0];
    }
}
