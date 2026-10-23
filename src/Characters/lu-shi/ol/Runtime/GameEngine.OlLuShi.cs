namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasOlLuShi => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveOlLuShiZhuyan);
    private static CardConversionSource OlLuShiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 42700)]
    private void CaptureOlLuShiOpening(GameStartedEvent fact)
    {
        if (!HasOlLuShi) return;
        foreach (var player in _players) CaptureOlLuShiSnapshot(player, 0, true);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 42701)]
    private void CaptureOlLuShiEnding(ActualPhaseEndedEvent fact)
    {
        if (HasOlLuShi && fact.Phase.Kind == ActualDiscardRecoveryPhaseKind.Ending)
            CaptureOlLuShiSnapshot(_players[fact.Phase.ActorSeat], fact.BoundaryFrameId, false);
    }
    private void CaptureOlLuShiSnapshot(CharacterState player, long boundary, bool initial)
    {
        if (ProgramEventHistory<OlLuShiSnapshotEvent>().Any(e => e.Seat == player.Seat && e.Initial == initial && e.BoundaryId == boundary)) return;
        var dual = player.HasHpValue ? player.HappyQiaoHealth : null;
        AdvanceEventRulesAndQueueFact(new OlLuShiSnapshotEvent(boundary, _turnNumber, player.Seat, initial,
            dual is null && player.HasHpValue ? player.Hp : null, dual, GetHand(player).Count));
    }
    private OlLuShiSnapshotEvent? OlLuShiLastSnapshot(int target) => ProgramEventHistory<OlLuShiSnapshotEvent>().LastOrDefault(e => e.Seat == target);
    private bool OlLuShiUnused(int owner, int target, string item) => !ProgramEventHistory<OlLuShiAdjustmentIssuedEvent>().Any(e => e.Source.OwnerSeat == owner && e.TargetSeat == target && e.Item == item);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 42700, SkillProgramEffectOp.ResolveOlLuShiZhuyan)]
    private bool CanOfferOlLuShi(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) =>
        c.SkillId == "ol:zhuyan" && w.Window == SkillProgramTriggerWindow.DiscardPhaseEnded &&
        _players.Any(p => p.IsAlive && OlLuShiLastSnapshot(p.Seat) is { } snapshot &&
            (OlLuShiUnused(c.OwnerSeat, p.Seat, "hand") || (snapshot.Hp is not null || snapshot.Dual is not null) && p.HasHpValue && OlLuShiUnused(c.OwnerSeat, p.Seat, "hp")));
    [ProgramCapability] private sealed class OlLuShiHost(GameEngine engine) : IOlLuShiHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginOlLuShi(f, op); }
    private SkillProgramStepOutcome BeginOlLuShi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OlLuShi is not null) { ContinueOlLuShi(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (op == SkillProgramEffectOp.ResolveOlLuShiZhuyan ? f.SkillId != "ol:zhuyan" || f.InstructionIndex != 1 || f.WindowContext?.Window != SkillProgramTriggerWindow.DiscardPhaseEnded :
            f.SkillId != "ol:leijie" || f.InstructionIndex != 3 || f.ActivationId != "judge-target" || f.SelectedTargetSeats.Count != 1)
            throw new InvalidOperationException("Lu Shi lost her original adjustment or finalized judgment instruction.");
        var r = new OlLuShiReceipt(OlLuShiSource(f), f.GameplayHash, op, f.InstructionIndex, f.WindowContext?.ParentFrameId ?? 0,
            op == SkillProgramEffectOp.ResolveOlLuShiZhuyan ? OlLuShiStage.Choice : OlLuShiStage.Ready);
        if (op == SkillProgramEffectOp.ResolveOlLuShiLeijie)
        {
            var outcome = f.JudgmentOutcomeBindings?.SingleOrDefault(o => o.Name == "leijie-result") ?? throw new InvalidOperationException("Leijie requires its original finalized judgment result.");
            if (!IsValidProgramJudgmentOutcome(f, outcome) || outcome.TargetSeat != f.SelectedTargetSeats[0]) throw new InvalidOperationException("Leijie cannot replace its judged participant or final card value.");
            r = r with { TargetSeat = outcome.TargetSeat, Item = outcome is { Suit: Suit.Spade, Rank: >= 2 and <= 9 } ? "slashes" : "draw", Amount = 2 };
        }
        ReplaceRuntimeTop(f = f with { OlLuShi = r });
        AdvanceEventRulesAndQueueFact(new OlLuShiStartedEvent(f.Id, r.Source, r.Hash, r.InstructionIndex, r.Parent, op));
        if (r.Stage == OlLuShiStage.Choice) PublishOlLuShi(f); else ContinueOlLuShi(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private Card[] OlLuShiDiscardCandidates(ProgramSkillFrame f) => GetHand(_players[f.OlLuShi!.TargetSeat])
        .Where(c => !IsForeignEquipmentDiscardPrevented(f.OlLuShi.TargetSeat, c, CardLocation.Hand(f.OlLuShi.TargetSeat), OwnedCardMoveIntent.Discard)).ToArray();
    private IReadOnlyList<PromptChoice> OlLuShiChoices(ProgramSkillFrame f)
    {
        var r = f.OlLuShi!;
        Dictionary<string, string> Args(string item) => new() { ["program-action"] = "ol-lushi", ["item"] = item };
        if (r.Stage == OlLuShiStage.Pool) return Enum.GetValues<HappyQiaoPool>().Select(pool => new PromptChoice(new($"ol-lushi.{f.Id}.{pool}"), pool == HappyQiaoPool.DaQiao ? "大乔" : "小乔", [], [r.TargetSeat],
            new Dictionary<string, string>(Args("pool")) { ["pool"] = pool.ToString() })).ToArray();
        if (r.Stage == OlLuShiStage.Discard) return OlLuShiDiscardCandidates(f).Where(c => !r.Cards.Contains(c.Id)).Select(c => new PromptChoice(new($"ol-lushi.{f.Id}.discard.{c.Id}"),
            $"弃置【{c.DisplayName}】（{r.Cards.Count + 1}/{r.RequiredCount}）", [c.Id], [], Args("discard"))).ToArray();
        if (r.Stage != OlLuShiStage.Choice) return [];
        return _players.Where(p => p.IsAlive).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).SelectMany(p => new[] { "hp", "hand" }.Where(item =>
            OlLuShiUnused(f.OwnerSeat, p.Seat, item) && OlLuShiLastSnapshot(p.Seat) is { } snapshot && (item == "hand" || p.HasHpValue && (snapshot.Hp is not null || snapshot.Dual is not null)))
            .Select(item => new PromptChoice(new($"ol-lushi.{f.Id}.{p.Seat}.{item}"), $"{p.Name}：调整{(item == "hp" ? "体力" : "手牌数")}", [], [p.Seat], Args(item)))).ToArray();
    }
    private void PublishOlLuShi(ProgramSkillFrame f)
    {
        var r = f.OlLuShi!; var chooser = r.Stage is OlLuShiStage.Pool or OlLuShiStage.Discard ? r.TargetSeat : f.OwnerSeat;
        var choices = OlLuShiChoices(f);
        if (choices.Count == 0) { FinishProgramSkill(f, true); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, "驻颜：选择此次真实调整。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage == OlLuShiStage.Discard, Choices = Array.AsReadOnly(choices.ToArray()), TargetSeat = r.TargetSeat < 0 ? null : r.TargetSeat,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ol-lushi")] private void AnswerOlLuShi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlLuShi: { } r } f || !OlLuShiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Lu Shi rejects a stale participant, spent lifetime option, pool or private discard material.");
        RequireOlLuShi(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == OlLuShiStage.Choice)
        {
            var target = choice.Targets.Single(); var item = choice.Parameters["item"]; var snapshot = OlLuShiLastSnapshot(target)!;
            r = r with { TargetSeat = target, Item = item, Snapshot = snapshot, Stage = item == "hp" && HasHappyQiaoHealth(target) ? OlLuShiStage.Pool : OlLuShiStage.Ready };
            ReplaceRuntimeTop(f = f with { OlLuShi = r });
            AdvanceEventRulesAndQueueFact(new OlLuShiAdjustmentIssuedEvent(f.Id, r.Source, target, item, snapshot));
            if (r.Stage == OlLuShiStage.Pool) { PublishOlLuShi(f); return; }
        }
        else if (r.Stage == OlLuShiStage.Pool)
        {
            var pool = Enum.Parse<HappyQiaoPool>(choice.Parameters["pool"]); var before = _players[r.TargetSeat].HappyQiaoHealth!;
            var value = Math.Min(before.MaximumHp, r.Snapshot!.Dual?.Hp(pool) ?? r.Snapshot.Hp!.Value);
            ReplaceRuntimeTop(f = f with { OlLuShi = r = r with { Pool = pool, TargetValue = value, Stage = OlLuShiStage.Ready } });
            AdvanceEventRulesAndQueueFact(new OlLuShiPoolAcceptedEvent(f.Id, r.TargetSeat, pool, value, before));
            BindOlLuShiHealthIntent(f, before, pool, value);
        }
        else
        {
            var cards = r.Cards.Append(choice.Cards.Single()).ToArray();
            ReplaceRuntimeTop(f = f with { OlLuShi = r with { Cards = cards } });
            if (cards.Length < r.RequiredCount) { PublishOlLuShi(f); return; }
            if (cards.Length != r.RequiredCount || cards.Any(id => !OlLuShiDiscardCandidates(f).Any(c => c.Id == id))) throw new InvalidOperationException("Zhuyan's selected hand discard is no longer completely payable.");
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { OlLuShi = f.OlLuShi! with { Stage = OlLuShiStage.Children, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(f, r.TargetSeat, cards, new("skill.ol.zhuyan.discard"));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { OlLuShi = f.OlLuShi! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new OlLuShiMovementEvent(f.Id, r.TargetSeat, false, cards.Length, before, _movementSequence));
        }
        ContinueOlLuShi(f.Id);
    }
    private void BindOlLuShiHealthIntent(ProgramSkillFrame f, HappyQiaoDualHealth before, HappyQiaoPool pool, int targetHp)
    {
        var r = f.OlLuShi!; var recover = targetHp >= before.Hp(pool); var amount = recover ? targetHp : checked(before.Hp(pool) - targetHp);
        var kind = recover ? HpChangeKind.Recovery : HpChangeKind.Loss;
        var returnKind = recover ? HappyQiaoHealthReturnKind.RecoverTo : HappyQiaoHealthReturnKind.Program;
        var key = recover ? $"recover-to:{f.Id}:{f.InstructionIndex}:{r.TargetSeat}" : $"program:{f.Id}:{f.InstructionIndex}:{r.TargetSeat}:{kind}";
        var request = new HappyQiaoHealthReceipt(++_resolutionSequence, f.Id, f.Kind, r.TargetSeat, f.OwnerSeat, kind, amount, returnKind, key, f.InstructionIndex, before, Pool: pool);
        ReplaceRuntimeTop(f with { HappyQiaoHealth = request, HappyQiaoRecoverTo = recover ? new(f.Id, f.InstructionIndex, f.GameplayHash, f.OwnerSeat, r.TargetSeat, targetHp, request.Id, before) : null });
        AdvanceEventRulesAndQueueFact(new HappyQiaoHealthRequestedEvent(request.Id, f.Id, r.TargetSeat, kind, amount, returnKind, key));
        AdvanceEventRulesAndQueueFact(new HappyQiaoHealthPoolSelectedEvent(request.Id, f.Id, r.TargetSeat, pool, before.Hp(pool), before.MaximumHp));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 42700)] private bool ContinueOlLuShi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlLuShi: not null } root || root.Id != id) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { OlLuShi: { } r } f && f.Id == id)
        {
            RequireOlLuShi(f);
            if (f.ReexecuteParticipantInstruction) ReplaceRuntimeTop(f = f with { ReexecuteParticipantInstruction = false });
            if (r.Stage is OlLuShiStage.Choice or OlLuShiStage.Pool or OlLuShiStage.Discard) { PublishOlLuShi(f); return true; }
            if (r.Stage is OlLuShiStage.Drawing or OlLuShiStage.NativeUse) return true;
            if (r.Stage == OlLuShiStage.Children)
            {
                if (DrainOlLuShi(f)) return true;
                ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
                if (r.Item == "slashes") { ReplaceRuntimeTop(f with { OlLuShi = r with { Stage = OlLuShiStage.Ready } }); continue; }
                FinishProgramSkill(f, true); return true;
            }
            if (!_players[r.TargetSeat].IsAlive || _winner != Winner.None) { FinishProgramSkill(f, true); return true; }
            if (r.Stage == OlLuShiStage.Loss)
            {
                new ProgramSkillHost(this).LoseHp(id, f.SkillId, r.TargetSeat, r.Amount);
                if (_resolutionStack.LastOrDefault()?.Id != id || GetActiveProgramFrame(id).OlLuShi!.Stage == OlLuShiStage.Loss) return true;
                continue;
            }
            if (r.Item == "slashes")
            {
                if (r.SlashCount >= 2 || !CanOlLuShiLeijieSlash(f, r.TargetSeat)) { FinishProgramSkill(f, true); return true; }
                IssueOlLuShiLeijieSlash(f); return true;
            }
            if (r.Item == "hp")
            {
                var target = _players[r.TargetSeat]; var current = r.Pool is { } pool ? target.HappyQiaoHealth!.Hp(pool) : target.Hp;
                var desired = r.Pool is not null ? r.TargetValue : Math.Min(target.MaxHp, r.Snapshot!.Hp ?? throw new InvalidOperationException("A formerly dual-health target cannot silently become scalar."));
                if (desired < current) { ReplaceRuntimeTop(f with { OlLuShi = r with { Stage = OlLuShiStage.Loss, Amount = checked(current - desired) } }); continue; }
                ReplaceRuntimeTop(f = f with { OlLuShi = r with { Stage = OlLuShiStage.Children } });
                if (r.Pool is not null) _ = TryHandleHappyQiaoRecoverTo(id, f.OwnerSeat, r.TargetSeat, desired);
                else if (desired > current) new ProgramSkillHost(this).Recover(id, f.OwnerSeat, r.TargetSeat, desired - current, null, null);
                continue;
            }
            var hand = GetHand(_players[r.TargetSeat]).Count;
            var desiredHand = r.Item == "draw" ? hand + 2 : r.Snapshot!.HandCount;
            var count = r.Item == "draw" ? 2 : Math.Max(0, Math.Min(5, desiredHand) - hand);
            if (count > 0)
            {
                ReplaceRuntimeTop(f = f with { OlLuShi = r with { Stage = OlLuShiStage.Drawing, Amount = count } });
                BeginOperationProgramDraw(f, r.TargetSeat, count, new("skill.ol.lushi.draw")); return true;
            }
            var discard = r.Item == "hand" ? Math.Min(Math.Max(0, hand - desiredHand), OlLuShiDiscardCandidates(f).Length) : 0;
            if (discard == 0) { FinishProgramSkill(f, true); return true; }
            ReplaceRuntimeTop(f = f with { OlLuShi = r with { Stage = OlLuShiStage.Discard, RequiredCount = discard } }); PublishOlLuShi(f); return true;
        }
        return true;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42702)] private void CaptureOlLuShiLoss(ProgramSkillHpLostEvent fact)
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { OlLuShi: { Stage: OlLuShiStage.Loss } r } f && fact.FrameId == f.Id && fact.SkillId == f.SkillId && fact.TargetSeat == r.TargetSeat && fact.Amount == r.Amount)
            ReplaceRuntimeTop(f with { OlLuShi = r with { Stage = OlLuShiStage.Children } });
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42703)] private void CaptureOlLuShiPreventedLoss(IdentityHpDeductionPreventedEvent fact)
    {
        if (_resolutionStack.LastOrDefault() is IdentityAppearanceFrame appearance && appearance.Id == fact.AppearanceFrameId && fact.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
            _resolutionStack.SingleOrDefault(p => p.Id == appearance.Receipt.ParentFrameId) is ProgramSkillFrame { OlLuShi: { Stage: OlLuShiStage.Loss, Item: "hp" } r } f &&
            fact.RequestedDeduction == r.Amount && appearance.Receipt.OwnerSeat == r.TargetSeat && appearance.Receipt.OriginalInstructionIndex == f.InstructionIndex)
            ReplaceRuntimeFrame(f.Id, f with { OlLuShi = r with { Stage = OlLuShiStage.Children } });
    }
    private bool DrainOlLuShi(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveOlLuShiZhuyan)] private void ReturnOlLuShiZhuyanDraw(ProgramSkillFrame f, NativeDrawOutcome o) => ReturnOlLuShiDraw(f, o);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveOlLuShiLeijie)] private void ReturnOlLuShiLeijieDraw(ProgramSkillFrame f, NativeDrawOutcome o) => ReturnOlLuShiDraw(f, o);
    private void ReturnOlLuShiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.OlLuShi is not { Stage: OlLuShiStage.Drawing, Draw: null } r || outcome.RecipientSeat != r.TargetSeat || outcome.RequestedCount != r.Amount || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Lu Shi draw lost its original recipient, requested count or single native return.");
        ReplaceRuntimeTop(f with { OlLuShi = r with { Stage = OlLuShiStage.Children, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new OlLuShiMovementEvent(f.Id, r.TargetSeat, true, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter)); ContinueOlLuShi(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 42700)] private bool ReturnOlLuShiMovement(ProgramSkillFrame f) => f.OlLuShi is not null && ContinueOlLuShi(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 42700)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 42700)] private bool RetainOlLuShiIssued(ProgramSkillFrame f) => f.OlLuShi is { Item: not null };
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOlLuShiZhuyan)] private PromptChoice ChooseOlLuShiZhuyan(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOlLuShiLeijie)] private PromptChoice ChooseOlLuShiLeijie(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
}
