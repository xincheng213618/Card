namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuLuXunZhangcaiDrawReason = "skill-program.wu-lu-xun.zhangcai.draw";
    private int? CaptureWuLuXunResponseRank(CharacterState provider, CardKind effectiveKind,
        IReadOnlyList<CardActionCost> costs, CardConversionSource? source)
    {
        if (!HasWuLuXunZhangcaiCapability || costs is not [var cost]) return null;
        var card = GetAdvancedCard(cost.CardId);
        // Existing payment-aware Juewu/Sirui/Alcohol captures take precedence at the caller.
        // Appearance is otherwise read from the actual one-card conversion, never its output name.
        return CaptureAlcoholResponseRank(provider, effectiveKind, costs) ?? ApplyProgramUseAppearance(provider, card, source).Rank;
    }
    private static string WuLuXunZhangcaiBinding(SkillProgramTriggerWindow window) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_xun_wumiao_WuLuXunZhangcai.WuLuXunZhangcaiBinding(window);
    private void CaptureWuLuXunZhangcai(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (!HasWuLuXunZhangcaiCapability || action.ActionId <= 0 || !IsValidPlayerSeat(action.ActorSeat) ||
            window is not (SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted or SkillProgramTriggerWindow.CardSupplyCompleted) ||
            window == SkillProgramTriggerWindow.CardUseCommitted && action.Type != CardActionType.Use ||
            window == SkillProgramTriggerWindow.CardResponseAccepted && action.Type != CardActionType.Response) return;
        IEnumerable<int> owners = window == SkillProgramTriggerWindow.CardResponseAccepted
            ? new[] { action.ActorSeat, action.ProviderSeat }.Distinct() : [action.ActorSeat];
        foreach (var ownerSeat in owners)
        {
            var owner = _players[ownerSeat]; if (!owner.IsAlive) continue;
            var ruxian = WuLuXunActiveRuxian(ownerSeat);
            if (action.EffectiveRank != 8 && ruxian is null) continue;
            var paidHand = action.PhysicalCards.Where(c => c.From == CardLocation.Hand(ownerSeat)).Select(c => c.CardId).ToHashSet();
            var remaining = GetHand(owner).Count(c => !paidHand.Contains(c.Id) && EffectiveOwnedCardRank(owner, c) == 8);
            foreach (var binding in GetSkillBindingShard(owner).GetInstanceTriggers(window).Where(b => b.SkillId == "wumiao:zhangcai" &&
                b.Trigger.Id == WuLuXunZhangcaiBinding(window) && b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveWuLuXunZhangcai }]))
            {
                var source = new CardConversionSource(binding.SkillId, binding.Trigger.Id, ownerSeat, binding.SkillInstanceId);
                if (ProgramEventHistory<WuLuXunZhangcaiQualifiedEvent>().Any(e => e.Qualification.Source == source && e.Qualification.ActionId == action.ActionId)) continue;
                AdvanceEventRulesAndQueueFact(new WuLuXunZhangcaiQualifiedEvent(new(action.ActionId, window, source,
                    binding.Program.GameplayHash, action.ActorSeat, action.ProviderSeat, action.EffectiveKind, action.EffectiveRank,
                    remaining, Math.Max(1, remaining), ruxian?.FrameId, _turnNumber, _turnProgression.OwnerSeat, _movementSequence)));
            }
        }
    }
    private WuLuXunZhangcaiQualification? WuLuXunZhangcaiQualificationFor(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != "wumiao:zhangcai" || context.CardUse is not { } use ||
            candidate.BindingId != WuLuXunZhangcaiBinding(context.Window)) return null;
        return ProgramEventHistory<WuLuXunZhangcaiQualifiedEvent>().Select(e => e.Qualification).SingleOrDefault(q =>
            q.ActionId == use.CardActionId && q.Window == context.Window && q.Source.OwnerSeat == candidate.OwnerSeat &&
            q.Source.SkillId == candidate.SkillId && q.Source.BindingId == candidate.BindingId && q.Source.SkillInstanceId == candidate.SkillInstanceId &&
            q.GameplayHash == candidate.GameplayHash);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29002, SkillProgramEffectOp.ResolveWuLuXunZhangcai)]
    private bool CanOfferWuLuXunZhangcai(ProgramTriggerLegalityContext gate) => gate.Owner.IsAlive &&
        WuLuXunZhangcaiQualificationFor(gate.Candidate, gate.Context) is { } q &&
        !ProgramEventHistory<WuLuXunZhangcaiStartedEvent>().Any(e => e.Source.OwnerSeat == gate.OwnerSeat && e.Source.SkillInstanceId == q.Source.SkillInstanceId && e.ActionId == q.ActionId);

    private SkillProgramStepOutcome BeginWuLuXunZhangcai(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WuLuXunZhangcai is not null) throw new InvalidOperationException("Zhangcai cannot issue the same accepted action twice.");
        if (f.SkillId != "wumiao:zhangcai" || f.TriggerId != f.ActivationId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            WuLuXunZhangcaiQualificationFor(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) is not { } q ||
            !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            !WuLuXunZhangcaiWindowMatches(window, q) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context, window) ||
            ProgramEventHistory<WuLuXunZhangcaiStartedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId && e.ActionId == q.ActionId))
            throw new InvalidOperationException("Zhangcai needs its frozen actual Use/Played payment, eligible point rank and unique original actor/provider opportunity.");
        var r = new WuLuXunZhangcaiReceipt(q, window.Id, window.ParentFrameId, WuLuXunZhangcaiStage.Drawing);
        ReplaceRuntimeTop(f = f with { WuLuXunZhangcai = r });
        AdvanceEventRulesAndQueueFact(WuLuXunZhangcaiStart(f.Id, r));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【彰才】，摸 {q.DrawCount} 张牌。", f.OwnerSeat);
        ContinueWuLuXunZhangcai(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private static WuLuXunZhangcaiStartedEvent WuLuXunZhangcaiStart(long id, WuLuXunZhangcaiReceipt r) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_xun_wumiao_WuLuXunZhangcai.WuLuXunZhangcaiStart(id,r);
    private bool WuLuXunZhangcaiWindowMatches(ProgramCardTriggerWindowFrame w, WuLuXunZhangcaiQualification q)
    {
        var action = q.Window == SkillProgramTriggerWindow.CardSupplyCompleted && w.CardSupplyCompletion is not null
            ? CardSupplyCompletionActorAction(w) : w.Action;
        return GetCardActionWindow(w) == q.Window && action.ActionId == q.ActionId && action.ActorSeat == q.ActorSeat &&
            action.ProviderSeat == q.ProviderSeat && action.EffectiveKind == q.EffectiveKind && action.EffectiveRank == q.EffectiveRank &&
            (q.Source.OwnerSeat == action.ActorSeat || q.Window == SkillProgramTriggerWindow.CardResponseAccepted && q.Source.OwnerSeat == action.ProviderSeat);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWuLuXunZhangcai)]
    private void ReturnWuLuXunZhangcaiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.WuLuXunZhangcai is not { Stage: WuLuXunZhangcaiStage.Drawing } r || outcome.RecipientSeat != f.OwnerSeat ||
            outcome.RequestedCount != r.Qualification.DrawCount || ProgramEventHistory<WuLuXunZhangcaiDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Zhangcai cannot replace or repeat its issued native draw request.");
        ReplaceRuntimeTop(f = f with { WuLuXunZhangcai = r with { Stage = WuLuXunZhangcaiStage.DrawChildren, DrawOutcome = outcome },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuLuXunZhangcaiDrawReturnedEvent(f.Id, outcome.RequestedCount,
            outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueWuLuXunZhangcai(f.Id);
    }
    private void ContinueWuLuXunZhangcai(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuXunZhangcai: { } r } f || f.Id != id) return;
        AssertWuLuXunZhangcai(f);
        if (r.Stage == WuLuXunZhangcaiStage.Drawing)
        { if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, r.Qualification.DrawCount, new(WuLuXunZhangcaiDrawReason)); return; }
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id)) return;
        AdvanceEventRulesAndQueueFact(new WuLuXunZhangcaiCompletedEvent(f.Id, r.Qualification.ActionId));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { WuLuXunZhangcai = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29002)]
    private bool ResumeWuLuXunZhangcai(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuXunZhangcai: not null } f || f.Id != id) return false; ContinueWuLuXunZhangcai(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29002)]
    private bool ReturnWuLuXunZhangcaiMovement(ProgramSkillFrame f)
    { if (f.WuLuXunZhangcai is null || f.PendingMovementContinuation is null) return false; ContinueWuLuXunZhangcai(f.Id); return true; }
}
