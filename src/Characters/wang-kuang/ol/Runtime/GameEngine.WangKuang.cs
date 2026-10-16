using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RenxiaDrawReason = "skill-program.wang-kuang.renxia.draw", RenxiaDiscardReason = "skill-program.wang-kuang.renxia.discard";
    private bool HasWangKuangCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveRenxiaRepeatedBranch);
    private static CardConversionSource WangKuangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private WangKuangRenxiaPhaseKey WangKuangCurrentPhase(int owner) => new(_turnNumber, owner, _cardUseDebitPhaseInstanceId);
    private static bool IsWangKuangDamageCardKind(CardKind kind) => IsDamageCardKind(kind) || kind == CardKind.UnexpectedAssault;
    private bool WangKuangHandHasDamage(int owner) => GetHand(_players[owner]).Any(c => IsWangKuangDamageCardKind(AdvancedEffectiveHandKind(_players[owner], c)));
    private IReadOnlyList<WangKuangRenxiaCard> WangKuangDiscardable(int owner, string instance) => Array.AsReadOnly(GetHand(_players[owner]).Concat(GetEquipment(owner))
        .Where(c => !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
            !IsActiveProgramSourceEquipmentCard(owner, "ol:renxia", instance, c)).Select(c => new WangKuangRenxiaCard(c.Id, _cardZones.GetLocation(c.Id))).ToArray());
    [ProgramCapability]
    private sealed class WangKuangCapability(GameEngine e) : IWangKuangProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, bool ending) => e.BeginWangKuang(f, ending); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 26910)]
    private bool CollectWangKuang(SkillProgramTrigger t) => t.Effects is not [{ Op: SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding }];
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26910)]
    private bool QualifyWangKuang(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        t.Effects is not [{ Op: SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding }] || IsIssuedWangKuangRenxiaCandidate(c, context);
    private bool WangKuangObligationConsumed(long id) => ProgramEventHistory<WangKuangRenxiaEndingConsumedEvent>().Any(e => e.ActiveFrameId == id);
    private IEnumerable<WangKuangRenxiaObligationIssuedEvent> WangKuangOutstanding(WangKuangRenxiaPhaseKey phase) =>
        !HasWangKuangCapability ? [] : ProgramEventHistory<WangKuangRenxiaObligationIssuedEvent>().Where(e => e.Phase == phase && !WangKuangObligationConsumed(e.ActiveFrameId));
    private WangKuangRenxiaPhaseKey? CaptureWangKuangRenxiaEndingPhase(CharacterState owner) => owner.IsAlive && _phase == TurnPhase.Play && owner.Seat == _currentSeat &&
        WangKuangOutstanding(WangKuangCurrentPhase(owner.Seat)).Any() ? WangKuangCurrentPhase(owner.Seat) : null;
    private IEnumerable<ProgramTriggerCandidate> IssuedWangKuangRenxiaEndingCandidates(CharacterState owner) =>
        CaptureWangKuangRenxiaEndingPhase(owner) is { } phase ? WangKuangOutstanding(phase).Select(e => new ProgramTriggerCandidate(owner.Seat, e.Source.SkillId,
            "issued-opposite-at-play-end", e.Source.SkillInstanceId, e.GameplayHash, 0)) : [];
    private bool IsIssuedWangKuangRenxiaCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!HasWangKuangCapability || c.SkillId != "ol:renxia" || c.BindingId != "issued-opposite-at-play-end" || context.Window != SkillProgramTriggerWindow.PlayEnding ||
            context.OwnerSeat != c.OwnerSeat || c.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || !_players[c.OwnerSeat].IsAlive || _winner != Winner.None ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { WangKuangRenxiaEndingPhase: { } phase } parent ||
            phase.ActualTurn != _turnNumber || phase.OwnerSeat != c.OwnerSeat || parent.OwnerSeat != c.OwnerSeat || parent.Window != context.Window ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c) return false;
        return ProgramEventHistory<WangKuangRenxiaObligationIssuedEvent>().Any(e => e.Phase == phase && e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId &&
            e.Source.SkillInstanceId == c.SkillInstanceId && e.GameplayHash == c.GameplayHash && ValidWangKuangObligation(e) &&
            (!WangKuangObligationConsumed(e.ActiveFrameId) || _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WangKuangRenxia is { Ending: true } r &&
                r.ObligationId == e.ActiveFrameId && r.Phase == phase && f.WindowContext == context && MountObserverCandidateMatches(f, c))));
    }
    private SkillProgramStepOutcome BeginWangKuang(ProgramSkillFrame input, bool ending)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.WangKuangRenxia is not null || f.SkillId != "ol:renxia" || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || !_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Renxia requires its exact zero-input original Play activation or issued same-phase ending.");
        WangKuangRenxiaObligationIssuedEvent? promise = null;
        if (ending)
        {
            var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, f.WindowContext!.OccurrenceIndex);
            if (!IsIssuedWangKuangRenxiaCandidate(c, f.WindowContext)) throw new InvalidOperationException("Renxia lost the exact issued PlayEnding candidate.");
            var phase = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w => w.Id == f.WindowContext.ParentFrameId).WangKuangRenxiaEndingPhase!;
            promise = WangKuangOutstanding(phase).Single(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId && e.GameplayHash == f.GameplayHash);
        }
        else if (f.WindowContext is not null || f.TriggerId is not null || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("An unpaid Renxia activation cannot borrow an ending entitlement or lost source.");
        var receipt = new WangKuangRenxiaReceipt(WangKuangSource(f), f.GameplayHash, f.InstructionIndex, promise?.Phase ?? WangKuangCurrentPhase(f.OwnerSeat), ending,
            promise?.ActiveFrameId, WangKuangRenxiaStage.BranchChoice);
        ReplaceRuntimeTop(f = f with { WangKuangRenxia = receipt });
        AdvanceEventRulesAndQueueFact(new WangKuangRenxiaStartedEvent(f.Id, receipt.Source, receipt.GameplayHash, receipt.Phase, ending, receipt.ObligationId));
        if (promise is not null)
        {
            AdvanceEventRulesAndQueueFact(new WangKuangRenxiaEndingConsumedEvent(promise.ActiveFrameId, f.Id, f.WindowContext!.ParentFrameId, promise.Phase));
            ChooseWangKuangBranch(f, promise.FirstBranch == WangKuangRenxiaBranch.Draw ? WangKuangRenxiaBranch.Discard : WangKuangRenxiaBranch.Draw);
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishWangKuang(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> WangKuangChoices(ProgramSkillFrame f)
    {
        var r = f.WangKuangRenxia!;
        PromptChoice Choice(string branch, string label, int[]? cards = null) => new(new($"renxia.{f.Id}.{r.Cursor}.{branch}.{string.Join('.', cards ?? [])}"), label, cards ?? [], [],
            new Dictionary<string, string> { ["program-action"] = "wang-kuang-renxia", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == WangKuangRenxiaStage.BranchChoice)
        {
            var choices = new List<PromptChoice> { Choice("draw", "摸两张牌，重复至手牌中有杀或伤害锦囊；本阶段结束时执行弃牌项") };
            if (WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId).Count >= 2) choices.Add(Choice("discard", "弃置两张牌，重复至手牌中没有杀或伤害锦囊；本阶段结束时执行摸牌项"));
            return Array.AsReadOnly(choices.ToArray());
        }
        if (r.Stage != WangKuangRenxiaStage.DiscardChoice) return [];
        var legal = WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId);
        return Array.AsReadOnly(r.Candidates.Where(c => legal.Contains(c) && !r.Selected.Contains(c)).Select(c => Choice("card",
            $"弃置【{GetAttackCard(c.CardId).DisplayName}】（{r.Selected.Count + 1}/{r.RequiredCount}）", [c.CardId])).ToArray());
    }
    private void PublishWangKuang(ProgramSkillFrame f)
    {
        var choices = WangKuangChoices(f); if (choices.Count == 0) throw new InvalidOperationException("Renxia lost its frozen legal private choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.WangKuangRenxia!.Stage == WangKuangRenxiaStage.BranchChoice ? "任侠：选择先执行的一项。" : "任侠：选择本次一起弃置的牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wang-kuang-renxia")]
    private void AnswerWangKuang(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WangKuangRenxia: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], WangKuangChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Renxia lost its original private chooser.");
        AssertWangKuang(f); ClearPendingDecision();
        if (r.Stage == WangKuangRenxiaStage.BranchChoice)
        { ChooseWangKuangBranch(f, choice.Parameters["branch"] == "draw" ? WangKuangRenxiaBranch.Draw : WangKuangRenxiaBranch.Discard); return; }
        var card = r.Candidates.Single(c => c.CardId == choice.Cards.Single()); var selected = r.Selected.Append(card).ToArray();
        ReplaceRuntimeTop(f = f with { WangKuangRenxia = r with { Selected = selected } });
        if (selected.Length < r.RequiredCount) { PublishWangKuang(f); return; }
        PayWangKuangDiscard(f); ContinueWangKuang(f.Id);
    }
    private void ChooseWangKuangBranch(ProgramSkillFrame f, WangKuangRenxiaBranch branch)
    {
        var r = f.WangKuangRenxia!;
        if (r.Branch is not null || !r.Ending && branch == WangKuangRenxiaBranch.Discard && WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId).Count < 2)
            throw new InvalidOperationException("The initial voluntary discard requires two genuinely discardable owned cards.");
        ReplaceRuntimeTop(f = f with { WangKuangRenxia = r with { Branch = branch } });
        AdvanceEventRulesAndQueueFact(new WangKuangRenxiaBranchChosenEvent(f.Id, branch));
        if (branch == WangKuangRenxiaBranch.Draw) { PayWangKuangDraw(f); ContinueWangKuang(f.Id); }
        else PrepareWangKuangDiscard(f);
    }
    private void PrepareWangKuangDiscard(ProgramSkillFrame f)
    {
        var r = f.WangKuangRenxia!; var cards = WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId); var count = Math.Min(2, cards.Count);
        ReplaceRuntimeTop(f = f with { WangKuangRenxia = r with { Stage = WangKuangRenxiaStage.DiscardChoice, Candidates = cards, Selected = [], RequiredCount = count, WoodenOxGrainIds = [] } });
        if (count == 0) { PayWangKuangDiscard(f); ContinueWangKuang(f.Id); } else PublishWangKuang(f);
    }
    private void RecordWangKuangPayment(ProgramSkillFrame f)
    {
        var r = f.WangKuangRenxia!;
        AdvanceEventRulesAndQueueFact(new WangKuangRenxiaIterationPaidEvent(f.Id, r.Branch!.Value, r.Cursor, r.Before, r.After, r.ActualCount));
        if (!r.Ending && r.Cursor == 1) AdvanceEventRulesAndQueueFact(new WangKuangRenxiaObligationIssuedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.Branch.Value));
    }
    private void PayWangKuangDraw(ProgramSkillFrame f)
    {
        var r = f.WangKuangRenxia!; ReplaceRuntimeTop(f = f with { WangKuangRenxia = r with { Stage = WangKuangRenxiaStage.MovementChildren, Cursor = r.Cursor + 1,
            Before = _movementSequence, After = _movementSequence, ActualCount = 0, Candidates = [], Selected = [], WoodenOxGrainIds = [], RequiredCount = 0 } });
        var drawn = DrawCards(_players[f.OwnerSeat], 2, true, new(RenxiaDrawReason)); f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { WangKuangRenxia = f.WangKuangRenxia! with { After = _movementSequence, ActualCount = drawn.Count } }); RecordWangKuangPayment(f);
    }
    private void PayWangKuangDiscard(ProgramSkillFrame f)
    {
        var r = f.WangKuangRenxia!; var legal = WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId);
        if (r.Selected.Count != r.RequiredCount || r.Selected.Any(c => !legal.Contains(c))) throw new InvalidOperationException("Renxia changed a frozen unpaid physical discard set.");
        var grain = r.Selected.Any(c => c.From == CardLocation.Equipment(f.OwnerSeat) && GetAttackCard(c.CardId).Kind == CardKind.WoodenOx) ? GetWoodenOxGrain(_players[f.OwnerSeat]).Select(c => c.Id).ToArray() : [];
        ReplaceRuntimeTop(f = f with { WangKuangRenxia = r with { Stage = WangKuangRenxiaStage.MovementChildren, Cursor = r.Cursor + 1, Before = _movementSequence,
            After = _movementSequence, ActualCount = r.Selected.Count, WoodenOxGrainIds = grain } });
        if (r.Selected.Count == 0) { RecordWangKuangPayment(f); return; }
        MoveProgramCardsFromMultipleSources(r.Selected.Select(c => c.CardId).ToArray(), CardLocation.DiscardPile, new(RenxiaDiscardReason), (_, movements) =>
        {
            var active = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(active = active with { WangKuangRenxia = active.WangKuangRenxia! with { After = _movementSequence } });
            foreach (var m in movements) AdvanceEventRulesAndQueueFact(new WangKuangRenxiaDiscardEntityEvent(active.Id, active.WangKuangRenxia!.Cursor, m.CardId, m.From, m.Sequence));
            RecordWangKuangPayment(active);
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { WangKuangRenxia = f.WangKuangRenxia! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new WangKuangRenxiaTailClosedEvent(f.Id, f.WangKuangRenxia!.Cursor, _movementSequence));
    }
    private bool DrainWangKuangChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueWangKuang(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { WangKuangRenxia: { } r } f && f.Id == id)
        {
            AssertWangKuang(f);
            if (r.Stage is WangKuangRenxiaStage.BranchChoice or WangKuangRenxiaStage.DiscardChoice) { if (_pendingDecision is null) PublishWangKuang(f); return; }
            if (DrainWangKuangChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.WangKuangRenxia!;
            var has = WangKuangHandHasDamage(f.OwnerSeat); var alive = _players[f.OwnerSeat].IsAlive && _winner == Winner.None;
            var repeat = alive && r.ActualCount > 0 && (r.Branch == WangKuangRenxiaBranch.Draw ? !has : has && WangKuangDiscardable(f.OwnerSeat, f.SkillInstanceId).Count > 0);
            AdvanceEventRulesAndQueueFact(new WangKuangRenxiaComparedEvent(f.Id, r.Cursor, has, repeat));
            if (!repeat)
            { AdvanceEventRulesAndQueueFact(new WangKuangRenxiaCompletedEvent(f.Id, r.Ending, r.Branch, r.Cursor, !alive ? "owner-dead-or-game-ended" : r.ActualCount == 0 ? "no-payable-card" : "condition-satisfied-or-no-legal-payment")); ReplaceRuntimeTop(f = f with { WangKuangRenxia = null }); FinishProgramSkill(f, true); return; }
            if (r.Branch == WangKuangRenxiaBranch.Draw) PayWangKuangDraw(f); else { PrepareWangKuangDiscard(f); return; }
        }
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26910)]
    private bool ResumeWangKuang(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WangKuangRenxia: not null } f || f.Id != id) return false; ContinueWangKuang(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26910)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26910)]
    private bool ContinuePaidWangKuang(ProgramSkillFrame f) => f.WangKuangRenxia is { } r ? (r.Ending || r.Cursor > 0) && ValidWangKuang(f) :
        f.InstructionIndex is 0 or 1 && f.TriggerId == "issued-opposite-at-play-end" && f.WindowContext is { } context &&
        IsIssuedWangKuangRenxiaCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveRenxiaRepeatedBranch, SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding)]
    private PromptChoice ChooseWangKuangAi(PendingDecision p, ProgramSkillFrame f) => f.WangKuangRenxia?.Stage == WangKuangRenxiaStage.BranchChoice ? p.Choices.First(c => c.Parameters["branch"] == "draw") :
        p.Choices.OrderBy(c => IsWangKuangDamageCardKind(AdvancedEffectiveHandKind(_players[f.OwnerSeat], GetAttackCard(c.Cards.Single()))) ? 0 : 1).ThenBy(c => GetKeepValue(GetAttackCard(c.Cards.Single()), _players[f.OwnerSeat])).First();
}
