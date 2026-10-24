using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShiZyFeijing = "mobile:shi-feijing", ShiZyXiaoge = "mobile:shi-xiaoge";
    private const string ShiZyDiscard = "skill.mobile-shi-zhang-yan.feijing.discard", ShiZyObtain = "skill.mobile-shi-zhang-yan.xiaoge.obtain";
    private static CardConversionSource ShiZySource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class ShiZhangYanHost(GameEngine e) : IShiZhangYanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShiZy(f, op); }

    private CardUseFrame? ShiZyCardWindow(ProgramTriggerCandidate c, ProgramSkillWindowContext w, SkillProgramTriggerWindow window)
    {
        if (w.Window != window || w.CardUse is not { } info || info.ActorSeat != c.OwnerSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame p || GetCardActionWindow(p) != window ||
            p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || ToSharedCandidate(p.Candidates[p.CandidateIndex]) != c ||
            LifecycleCardUse(info.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } a } use || a.ActionId != info.CardActionId ||
            p.ParentFrameId != use.Id || p.Action.ActionId != a.ActionId || use.SourceSeat != c.OwnerSeat || a.ActorSeat != c.OwnerSeat || !IsSlashCard(use.CardKind) ||
            a.EffectiveDesignatedTargetSeats is not [var target] || target == c.OwnerSeat || !IsValidPlayerSeat(target)) return null;
        return window == SkillProgramTriggerWindow.CardUseCompleted && use.Step != ResolutionFrameStep.Completed ? null : use;
    }
    private CardUseFrame? ShiZyDamageUse(int owner, ProgramSkillWindowContext w)
    {
        if (w is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, SourceSeat: { } source, TargetSeat: { } victim, Amount: > 0 } || source != owner ||
            CurrentDamageAttempt is not { } attack || attack.SourceSeat != owner || attack.CardUserSeat != owner || attack.TargetSeat != victim ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { Type: CardActionType.Use } action, ShiZhangYanPath: { } path } use ||
            !IsSlashCard(use.CardKind) || action.ActorSeat != owner || path.OriginalActor != owner || path.UseId != use.Id || path.ActionId != action.ActionId ||
            !path.Extra.Contains(victim) || !path.Payments.Any(p => p.Seat == victim && p.DiscardSequence > 0)) return null;
        return use;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 50500)]
    private bool AllowsShiZy(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (c.SkillId == ShiZyFeijing && t.Id == "sole-slash-path") return ShiZyCardWindow(c, w, SkillProgramTriggerWindow.CardUseTargetsFinalized) is not null;
        if (c.SkillId != ShiZyXiaoge) return true;
        if (t.Id == "extra-target-prevent") return ShiZyDamageUse(c.OwnerSeat, w) is not null;
        return t.Id != "sole-slash-duel" || ShiZyCardWindow(c, w, SkillProgramTriggerWindow.CardUseCompleted) is { } use &&
            _players[use.Action!.EffectiveDesignatedTargetSeats.Single()].IsAlive && CanIssueSelectedActorDuel(use.Action.EffectiveDesignatedTargetSeats.Single(), c.OwnerSeat);
    }
    private int[] ShiZyPath(int owner, int target, int direction)
    {
        var order = CurrentTableOrder().Where(seat => _players[seat].IsAlive).ToArray();
        var at = Array.IndexOf(order, owner); var path = new List<int>();
        for (var n = 1; n < order.Length; n++)
        {
            var seat = order[((at + direction * n) % order.Length + order.Length) % order.Length];
            if (seat == target) break; path.Add(seat);
        }
        return path.ToArray();
    }
    private IReadOnlyList<PromptChoice> ShiZyHandChoices(ProgramSkillFrame f, ShiZyHandPlan plan) => BuildOwnedCardPaymentChoices(f.Id, plan.Seat, plan.Seat,
        [CardZoneKind.Hand], OwnedCardMoveIntent.Discard, canSelect: (_, card) => plan.Hand.Contains(card.Id) &&
            !IsSelfHandCategoryDiscardForbidden(plan.Seat, card, CardLocation.Hand(plan.Seat), OwnedCardMoveIntent.Discard));
    private int ShiZyChooser(ProgramSkillFrame f) => f.ShiZhangYan is { Stage: ShiZyStage.Commit } r ? r.Plans[r.Cursor].Seat : f.OwnerSeat;
    private SkillProgramStepOutcome BeginShiZy(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiZhangYan is not null || f.InstructionIndex != 1 || f.WindowContext is not { } w || f.TriggerId is null) throw new InvalidOperationException("Shi Zhang Yan requires the exact original unissued native trigger.");
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0);
        if (op == SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogePrevent)
        {
            var use = ShiZyDamageUse(f.OwnerSeat, w) ?? throw new InvalidOperationException("Xiaoge requires the original Feijing-added victim and live native Slash damage.");
            var paid = use.ShiZhangYanPath!.Payments.Single(p => p.Seat == w.TargetSeat && p.DiscardSequence > 0);
            var r = new ShiZyReceipt(ShiZySource(f), f.GameplayHash, op, _turnNumber, w.ParentFrameId, use.Id, use.Action!.ActionId, w.TargetSeat!.Value, ShiZyStage.RecoverChildren)
                { CardId = paid.Selected, PaymentSequence = paid.DiscardSequence, Before = _movementSequence, After = _movementSequence };
            ReplaceRuntimeTop(f = f with { ShiZhangYan = r }); StartShiZyFact(f);
            PreventProgramCurrentDamage(f);
            AdvanceEventRulesAndQueueFact(new ShiZyPreventedEvent(f.Id, use.Id, r.ActionId, r.Target, r.CardId, r.PaymentSequence));
            new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
            ContinueShiZy(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        var window = op == SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.CardUseCompleted;
        var original = ShiZyCardWindow(c, w, window) ?? throw new InvalidOperationException("Shi Zhang Yan lost the exact sole-target Slash window.");
        var target = original.Action!.EffectiveDesignatedTargetSeats.Single();
        var receipt = new ShiZyReceipt(ShiZySource(f), f.GameplayHash, op, _turnNumber, w.ParentFrameId, original.Id, original.Action.ActionId, target,
            op == SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing ? ShiZyStage.Path : ShiZyStage.DuelIssued)
        { OriginalAttackActive = op == SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel ? original.CardAttack?.Active ??
            throw new InvalidOperationException("Xiaoge cannot freeze a completed Slash without its original attack cursor.") : null,
          OriginalTargetIndex = op == SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel ? original.TargetIndex : null };
        ReplaceRuntimeTop(f = f with { ShiZhangYan = receipt }); StartShiZyFact(f);
        if (receipt.Stage == ShiZyStage.Path) PublishShiZy(f); else IssueShiZyVirtualDuel(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void StartShiZyFact(ProgramSkillFrame f)
    { var r = f.ShiZhangYan!; AdvanceEventRulesAndQueueFact(new ShiZyStartedEvent(f.Id, r.Source, r.Hash, r.Op, r.Turn, r.WindowId, r.UseId, r.ActionId, r.Target, r.OriginalAttackActive, r.OriginalTargetIndex)); }
    private bool ShiZyExtraLegal(CardUseFrame use, int seat)
    {
        var a = use.Action!; var target = _players[seat];
        return target.IsAlive && seat != a.ActorSeat && !use.TargetSeats.Contains(seat) && !IsSlashProhibited(target) &&
            !IsDirectedCardTargetProhibited(a.ActorSeat, seat, use.CardKind, use.Id) &&
            !IsCardTargetProhibited(target, use.CardKind, a.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(a)) && !HasBeneficiarySuitShield(a.ActorSeat, seat, a.EffectiveSuit);
    }
    private IReadOnlyList<PromptChoice> ShiZyChoices(ProgramSkillFrame f)
    {
        var r = f.ShiZhangYan!;
        PromptChoice C(string branch, string token, string label) => new(new($"shi-zhang-yan.{f.Id}.{r.Stage}.{branch}.{token}"), label, [], [],
            new Dictionary<string, string> { ["program-action"] = "mobile-shi-zhang-yan", ["branch"] = branch, ["token"] = token });
        if (r.Stage == ShiZyStage.Path) return new[] { 1, -1 }.Select(direction => C("path", direction.ToString(CultureInfo.InvariantCulture),
            (direction == 1 ? "顺序路径：" : "反向路径：") + (ShiZyPath(f.OwnerSeat, r.Target, direction) is { Length: > 0 } path ? string.Join(" → ", path.Select(s => _players[s].Name)) : "无中间角色"))).ToArray();
        if (r.Stage == ShiZyStage.Commit) return ShiZyHandChoices(f, r.Plans[r.Cursor]).Select(choice => choice with { Parameters =
            new Dictionary<string, string>(choice.Parameters) { ["program-action"] = "mobile-shi-zhang-yan", ["branch"] = "commit", ["token"] = choice.Cards.Single().ToString(CultureInfo.InvariantCulture) } }).ToArray();
        if (r.Stage != ShiZyStage.Color) return [];
        var use = LifecycleCardUse(r.UseId)!; var colors = r.Plans.Where(p => p.DiscardSequence > 0 && ShiZyExtraLegal(use, p.Seat)).Select(p => p.Suit is Suit.Heart or Suit.Diamond ? "red" : "black").Distinct().ToArray();
        return colors.Select(color => C("color", color, "令弃置" + (color == "red" ? "红色" : "黑色") + "牌的角色成为此杀的额外目标")).Concat([C("finish", "finish", "不增加额外目标")]).ToArray();
    }
    private void PublishShiZy(ProgramSkillFrame f)
    {
        var r = f.ShiZhangYan!; var choices = ShiZyChoices(f); var chooser = ShiZyChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == ShiZyStage.Path ? "飞径：选择你与原目标之间的一条路径，首尾不参与。" :
            r.Stage == ShiZyStage.Commit ? "飞径：私下选择一张自己的手牌；全部承诺完成前不公开也不弃置。" : "飞径：可选择一种实际弃牌颜色增加额外目标。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage == ShiZyStage.Commit, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mobile-shi-zhang-yan")]
    private void AnswerShiZy(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhangYan: { } r } f || _pendingDecision?.PlayerSeat != ShiZyChooser(f) ||
            !ShiZyChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Feijing lost the actual path, independent Hand chooser or public color option.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == ShiZyStage.Path)
        {
            var direction = int.Parse(choice.Parameters["token"], CultureInfo.InvariantCulture);
            var plans = ShiZyPath(f.OwnerSeat, r.Target, direction).Select(seat => new ShiZyHandPlan(seat) { Hand = GetHand(_players[seat]).Select(c => c.Id).ToArray() }).ToArray();
            ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { Direction = direction, Plans = plans, Stage = ShiZyStage.Commit, Cursor = 0 } });
            for (var i = 0; i < plans.Length; i++) AdvanceEventRulesAndQueueFact(new ShiZyPathSeatEvent(f.Id, i, plans[i].Seat));
            AdvanceShiZyCommit(f); return;
        }
        if (r.Stage == ShiZyStage.Commit)
        {
            var plan = r.Plans[r.Cursor];
            if (!GetHand(_players[plan.Seat]).Select(c => c.Id).SequenceEqual(plan.Hand)) throw new InvalidOperationException("Feijing lost the exact pre-reveal original Hand commitment.");
            var id = choice.Cards.Single();
            var plans = r.Plans.Select((p, i) => i == r.Cursor ? p with { Selected = id, Suit = EffectiveSuit(_players[p.Seat], GetAdvancedCard(id)), Committed = true } : p).ToArray();
            ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { Plans = plans, Cursor = r.Cursor + 1 } });
            AdvanceEventRulesAndQueueFact(new ShiZyHandCommittedEvent(f.Id, r.Cursor, plan.Seat)); AdvanceShiZyCommit(f); return;
        }
        if (r.Stage != ShiZyStage.Color) throw new InvalidOperationException("Feijing cannot change a completed commitment.");
        CompleteShiZyTargets(f, choice.Parameters["branch"] == "color" ? choice.Parameters["token"] : null);
    }
    private void AdvanceShiZyCommit(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var r = f.ShiZhangYan!;
        while (r.Cursor < r.Plans.Count && (!_players[r.Plans[r.Cursor].Seat].IsAlive || ShiZyHandChoices(f, r.Plans[r.Cursor]).Count == 0))
        {
            var index = r.Cursor; var p = r.Plans[index];
            r = r with { Plans = r.Plans.Select((plan, i) => i == index ? plan with { Committed = true } : plan).ToArray(), Cursor = index + 1 };
            ReplaceRuntimeTop(f = f with { ShiZhangYan = r }); AdvanceEventRulesAndQueueFact(new ShiZyHandCommittedEvent(f.Id, index, p.Seat));
        }
        if (r.Cursor < r.Plans.Count) { PublishShiZy(f); return; }
        ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { Stage = ShiZyStage.RevealChildren, Cursor = 0 } });
        for (var i = 0; i < r.Plans.Count; i++) { var p = r.Plans[i]; AdvanceEventRulesAndQueueFact(new ShiZyHandRevealedEvent(f.Id, i, p.Seat, p.Selected, p.Suit)); }
        var cards = r.Plans.Where(p => p.Selected > 0 && _cardZones.GetLocation(p.Selected) == CardLocation.Hand(p.Seat)).Select(p => ToSnapshot(GetAdvancedCard(p.Selected)) with { Suit = p.Suit }).ToArray();
        if (cards.Length > 0) AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId!, f.OwnerSeat, "feijing-path", Array.AsReadOnly(cards)));
        if (!TryBeginPublicHandRevealWindow(f.Id, "mobile-shi-zhang-yan-path")) AfterShiZyReveal(f);
    }
    [PublicHandRevealReturn("mobile-shi-zhang-yan-path")]
    private void AfterShiZyReveal(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { ShiZhangYan = f.ShiZhangYan! with { Stage = ShiZyStage.DiscardChildren, Cursor = 0 } }); PayShiZyNextHand(f); }
    private void PayShiZyNextHand(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var r = f.ShiZhangYan!;
        if (r.Cursor >= r.Plans.Count) { ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { Stage = ShiZyStage.Color }, PendingMovementContinuation = null }); PublishShiZy(f); return; }
        var index = r.Cursor; var plan = r.Plans[index];
        ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { Cursor = index + 1, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (plan.Selected > 0 && _players[plan.Seat].IsAlive && _cardZones.GetLocation(plan.Selected) == CardLocation.Hand(plan.Seat) && ShiZyHandChoices(f, plan).Any(c => c.Cards.SequenceEqual([plan.Selected])))
            MoveProgramLogicalDiscard(f, plan.Seat, [plan.Selected], new(ShiZyDiscard), (batch, moves) =>
            {
                var move = moves.SingleOrDefault(m => m.CardId == plan.Selected && m.From == CardLocation.Hand(plan.Seat));
                if (move is null) return;
                var current = GetActiveProgramFrame(f.Id); var now = current.ShiZhangYan!;
                ReplaceRuntimeFrame(f.Id, current with { ShiZhangYan = now with { Plans = now.Plans.Select((p, i) => i == index ? p with { DiscardSequence = move.Sequence } : p).ToArray() } });
                AdvanceEventRulesAndQueueFact(new ShiZyHandDiscardedEvent(f.Id, index, plan.Seat, plan.Selected, move.Sequence));
            });
        SealShiZy(f.Id); ContinueShiZy(f.Id);
    }
    private void CompleteShiZyTargets(ProgramSkillFrame f, string? color)
    {
        var r = f.ShiZhangYan!; var use = LifecycleCardUse(r.UseId) ?? throw new InvalidOperationException("Feijing lost the original paid Slash use.");
        int[] added = color is null ? [] : r.Plans.Where(p => p.DiscardSequence > 0 && (p.Suit is Suit.Heart or Suit.Diamond ? "red" : "black") == color && ShiZyExtraLegal(use, p.Seat)).Select(p => p.Seat).ToArray();
        var marker = new ShiZySlashPlan(f.Id, r.Source, r.Hash, use.Id, r.ActionId, f.OwnerSeat, r.Target) { Payments = r.Plans.Where(p => p.DiscardSequence > 0).ToArray(), Extra = added };
        var targets = use.TargetSeats.Concat(added).ToArray(); var action = CloneRoleAction(use.Action!, use.SourceSeat, targets);
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, ShiZhangYanPath = marker,
            TargetsAdjusted = added.Length > 0 || use.TargetsAdjusted, Enhancements = use.Enhancements | (added.Length > 0 ? CurrentCardEnhancement.ExtraTarget : CurrentCardEnhancement.None) }, action);
        foreach (var seat in added)
        {
            var paid = r.Plans.Single(p => p.Seat == seat);
            AdvanceEventRulesAndQueueFact(new ShiZyTargetAddedEvent(f.Id, use.Id, r.ActionId, seat, paid.Selected, paid.DiscardSequence));
            AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id, f.SkillId, f.OwnerSeat, use.Id, seat));
        }
        if (added.Length > 0) RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!, added);
        FinishShiZy(GetActiveProgramFrame(f.Id));
    }
    private bool DrainShiZy(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void SealShiZy(long id) { var f = GetActiveProgramFrame(id); ReplaceRuntimeFrame(id, f with { ShiZhangYan = f.ShiZhangYan! with { After = _movementSequence } }); }
    private void ContinueShiZy(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhangYan: { } r } f || f.Id != id) return;
        if (r.Stage is ShiZyStage.Path or ShiZyStage.Commit or ShiZyStage.Color) { PublishShiZy(f); return; }
        if (r.Stage == ShiZyStage.RevealChildren || r.Stage == ShiZyStage.DuelIssued) return;
        if (DrainShiZy(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.ShiZhangYan!;
        if (r.Stage == ShiZyStage.DiscardChildren) { PayShiZyNextHand(f); return; }
        if (r.Stage == ShiZyStage.RecoverChildren)
        {
            ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { Stage = ShiZyStage.ObtainChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (_players[f.OwnerSeat].IsAlive && r.CardId > 0 && _cardZones.GetLocation(r.CardId) == CardLocation.DiscardPile)
                MoveProgramCardsFromMultipleSources([r.CardId], CardLocation.Hand(f.OwnerSeat), new(ShiZyObtain));
            SealShiZy(id); var current = GetActiveProgramFrame(id).ShiZhangYan!;
            AdvanceEventRulesAndQueueFact(new ShiZyCardObtainedEvent(id, r.CardId, current.Before, current.After)); ContinueShiZy(id); return;
        }
        if (r.Stage == ShiZyStage.DuelChildren) RestoreShiZyCompletedSlash(f);
        FinishShiZy(f);
    }
    private void FinishShiZy(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShiZhangYan = f.ShiZhangYan! with { Stage = ShiZyStage.Finished }, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 50500)] private bool ResumeShiZy(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhangYan: not null } f || f.Id != id) return false; ContinueShiZy(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 50500)] private bool ReturnShiZyMovement(ProgramSkillFrame f)
    { if (f.ShiZhangYan is null) return false; ContinueShiZy(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing)] private PromptChoice ChooseShiZy(PendingDecision p) => p.Choices[0];
    private PendingDecision? ProjectShiZyCardSelection(PendingDecision decision, ProgramSkillFrame f) => f.ShiZhangYan is { Stage: ShiZyStage.Commit } r && decision.PlayerSeat == r.Plans[r.Cursor].Seat ?
        NativeOwnedSelection(decision, f, "shi-zhang-yan:commit:" + r.Cursor, "branch", ["commit"], [], [], 1, 1) : null;
    [ProgramViewContributor(ProgramViewContribution.ProgramPublicCards, 50500)]
    private IEnumerable<CardSnapshot> ShiZyPublicCommittedHands() => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.ShiZhangYan is { Stage: ShiZyStage.RevealChildren or ShiZyStage.DiscardChildren or ShiZyStage.Color })
        .SelectMany(f => f.ShiZhangYan!.Plans.Where(p => p.Selected > 0 && _cardZones.GetLocation(p.Selected) == CardLocation.Hand(p.Seat)).Select(p => ToSnapshot(GetAdvancedCard(p.Selected)) with { Suit = p.Suit }));
}
