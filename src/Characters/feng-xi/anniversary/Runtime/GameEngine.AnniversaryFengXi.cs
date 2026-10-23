namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource FengXiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardMoveReason FengXiDrawReason => new("skill.anniversary.boyan.draw");
    private static CardMoveReason FengXiDiscardReason => new("skill.anniversary.yusui.discard");
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44500, SkillProgramEffectOp.ResolveAnniversaryFengXiYusui)]
    private bool CanOfferFengXi(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) =>
        w is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { IsPublicRed: false } card } && card.ActorSeat != c.OwnerSeat &&
        IsValidPlayerSeat(card.ActorSeat) && _players[card.ActorSeat].IsAlive && LifecycleCardUse(card.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
        action.ActionId == card.CardActionId && action.ActorSeat == card.ActorSeat && use.TargetSeats.Contains(c.OwnerSeat);
    [ProgramCapability] private sealed class AnniversaryFengXiHost(GameEngine engine) : IAnniversaryFengXiHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginFengXi(f, op); }
    private SkillProgramStepOutcome BeginFengXi(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryFengXi is not null) { ContinueFengXi(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        var y = operation == SkillProgramEffectOp.ResolveAnniversaryFengXiYusui;
        var target = y ? f.WindowContext?.CardUse?.ActorSeat ?? -1 : f.SelectedTargetSeats.Count == 1 ? f.SelectedTargetSeats[0] : -1;
        if (!IsValidPlayerSeat(target) || target == f.OwnerSeat || (y ? f.InstructionIndex != 2 || f.SkillId != AnniversaryFengXiComposition.Yusui ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse.IsPublicRed: false } :
            f.InstructionIndex != 1 || f.SkillId != AnniversaryFengXiComposition.Boyan || f.ActivationId != "fill-and-seal-hand" || f.TriggerId is not null ||
                f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play))
            throw new InvalidOperationException("Feng Xi requires his accepted sourced target after the native HP cost, or his own once-per-Play target.");
        var r = new AnniversaryFengXiReceipt(FengXiSource(f), f.GameplayHash, operation, f.InstructionIndex, _turnNumber, f.WindowContext?.ParentFrameId,
            f.WindowContext?.CardUse?.ParentCardUseFrameId, f.WindowContext?.CardUse?.CardActionId, target, y ? AnniversaryFengXiStage.Choice : AnniversaryFengXiStage.Drawing);
        ReplaceRuntimeTop(f = f with { AnniversaryFengXi = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryFengXiStartedEvent(f.Id, r.Source, r.Hash, operation, r.Instruction, r.Turn, r.ParentId, r.UseId, r.ActionId, target));
        if (!_players[target].IsAlive) { FinishFengXiOperation(f); return SkillProgramStepOutcome.AwaitChild; }
        if (y) PublishFengXi(f);
        else
        {
            if (!_players[target].HasHpValue) { CancelProgramBindingAndCleanup(f, "体力上限未定义，无法结算驳言。"); return SkillProgramStepOutcome.AwaitChild; }
            var amount = Math.Max(0, Math.Min(5, _players[target].MaxHp) - GetHand(_players[target]).Count);
            ReplaceRuntimeTop(f = f with { AnniversaryFengXi = r with { Amount = amount } }); BeginOperationProgramDraw(f, target, amount, FengXiDrawReason);
        }
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> FengXiChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryFengXi!;
        PromptChoice Choice(string branch, string label, IReadOnlyList<int>? cards = null) => new(new($"anniversary-feng-xi.{f.Id}.{r.Stage}.{branch}"), label, cards ?? [], [r.Target],
            new Dictionary<string, string> { ["program-action"] = "anniversary-feng-xi", ["branch"] = branch });
        if (r.Stage == AnniversaryFengXiStage.Choice)
        {
            var list = new List<PromptChoice> { Choice("hand", "令用牌者弃置手牌至与你相同") };
            if (_players[f.OwnerSeat].HasHpValue && _players[r.Target].HasHpValue) list.Add(Choice("hp", "令用牌者失去体力至与你相同"));
            return Array.AsReadOnly(list.ToArray());
        }
        if (r.Stage != AnniversaryFengXiStage.DiscardChoice) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, r.Target, r.Target, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard,
            canSelect: (_, c) => !r.Cards.Contains(c.Id)).Select(c => Choice("card-" + c.Cards.Single(), c.Label, c.Cards)).ToArray());
    }
    private void PublishFengXi(ProgramSkillFrame f)
    {
        var r = f.AnniversaryFengXi!; var chooser = r.Stage == AnniversaryFengXiStage.DiscardChoice ? r.Target : f.OwnerSeat;
        var choices = FengXiChoices(f);
        if (!_players[chooser].IsAlive || !_players[r.Target].IsAlive || choices.Count == 0) { FinishFengXiOperation(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, chooser, choices, "玉碎：结算已经失去1点体力后的选择。", r.Target); AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-feng-xi")] private void AnswerFengXi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryFengXi: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == AnniversaryFengXiStage.DiscardChoice ? r.Target : f.OwnerSeat) || !FengXiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Feng Xi rejects a stale branch or another participant's private discard.");
        RequireFengXi(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == AnniversaryFengXiStage.Choice)
        {
            AdvanceEventRulesAndQueueFact(new AnniversaryFengXiBranchEvent(f.Id, branch, r.Target));
            if (branch == "hp")
            { ReplaceRuntimeTop(f with { AnniversaryFengXi = r with { Stage = AnniversaryFengXiStage.HealthReady } }); ContinueFengXi(f.Id); return; }
            var available = BuildOwnedCardPaymentChoices(f.Id, r.Target, r.Target, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard).Count;
            var amount = Math.Min(Math.Max(0, GetHand(_players[r.Target]).Count - GetHand(_players[f.OwnerSeat]).Count), available);
            if (amount == 0) { FinishFengXiOperation(f); return; }
            ReplaceRuntimeTop(f = f with { AnniversaryFengXi = r with { Stage = AnniversaryFengXiStage.DiscardChoice, Amount = amount } }); PublishFengXi(f); return;
        }
        var ids = r.Cards.Append(choice.Cards.Single()).ToArray();
        ReplaceRuntimeTop(f = f with { AnniversaryFengXi = r = r with { Cards = ids } });
        if (ids.Length < r.Amount) { PublishFengXi(f); return; }
        if (ids.Length != r.Amount || ids.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(r.Target) || IsForeignEquipmentDiscardPrevented(r.Target, GetAdvancedCard(id), CardLocation.Hand(r.Target), OwnedCardMoveIntent.Discard)))
            throw new InvalidOperationException("Yusui requires the actual user's complete legal Hand payment.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryFengXi = r with { Stage = AnniversaryFengXiStage.MovementChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramLogicalDiscard(f, r.Target, ids, FengXiDiscardReason, (_, _) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = active.AnniversaryFengXi! with { After = _movementSequence };
            ReplaceRuntimeTop(active with { AnniversaryFengXi = paid }); AdvanceEventRulesAndQueueFact(new AnniversaryFengXiPaidEvent(f.Id, r.Target, ids.Length, before, paid.After));
        }); ContinueFengXi(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 44500)] private bool ContinueFengXi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryFengXi: not null } root || root.Id != id) return false;
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryFengXi: { } r } f && f.Id == id)
        {
            RequireFengXi(f);
            if (r.Stage is AnniversaryFengXiStage.Choice or AnniversaryFengXiStage.DiscardChoice) { PublishFengXi(f); return true; }
            if (r.Stage == AnniversaryFengXiStage.Drawing) return true;
            if (r.Stage == AnniversaryFengXiStage.HealthApplying)
            {
                new ProgramSkillHost(this).LoseHp(id, f.SkillId, r.Target, r.Amount);
                if (_resolutionStack.LastOrDefault()?.Id != id || GetActiveProgramFrame(id).AnniversaryFengXi!.Stage == AnniversaryFengXiStage.HealthApplying) return true;
                continue;
            }
            if (DrainFengXi(f)) return true;
            if (r.Stage == AnniversaryFengXiStage.HealthReady)
            {
                if (!_players[r.Target].IsAlive || !_players[r.Target].HasHpValue || !_players[f.OwnerSeat].HasHpValue) { FinishFengXiOperation(f); return true; }
                if (TryBeginHappyQiaoQuery(f, f.OwnerSeat, "feng-xi-owner")) return true;
                f = GetActiveProgramFrame(id);
                if (TryBeginHappyQiaoQuery(f, r.Target, "feng-xi-target")) return true;
                f = GetActiveProgramFrame(id);
                var own = HappyQiaoQueryValue(f, f.OwnerSeat, "feng-xi-owner"); var other = HappyQiaoQueryValue(f, r.Target, "feng-xi-target");
                var amount = Math.Max(0, checked(other - own));
                ReplaceRuntimeTop(f = f with { AnniversaryFengXi = r with { Stage = amount == 0 ? AnniversaryFengXiStage.HealthChildren : AnniversaryFengXiStage.HealthApplying, Amount = amount, OwnerHp = own, TargetHp = other } });
                AdvanceEventRulesAndQueueFact(new AnniversaryFengXiHealthIssuedEvent(f.Id, r.Target, amount, own, other));
                if (amount > 0 && HasHappyQiaoHealth(r.Target)) BindFengXiLossPool(f, r.Target, amount);
                continue;
            }
            FinishFengXiOperation(f); return true;
        }
        return true;
    }
    private void BindFengXiLossPool(ProgramSkillFrame f, int target, int amount)
    {
        var key = $"query:{f.Id}:{f.InstructionIndex}:feng-xi-target:{target}";
        var q = f.HappyQiaoQueries?.SingleOrDefault(q => q.QueryKey == key) ?? throw new InvalidOperationException("Yusui requires its exact target HP comparison query.");
        if (q.Pool is not { } pool || q.Before != _players[target].HappyQiaoHealth) throw new InvalidOperationException("Yusui's actual loss cannot reuse a changed or unselected health snapshot.");
        var occurrence = $"program:{f.Id}:{f.InstructionIndex}:{target}:{HpChangeKind.Loss}";
        var receipt = new HappyQiaoHealthReceipt(++_resolutionSequence, f.Id, f.Kind, target, f.OwnerSeat, HpChangeKind.Loss, amount, HappyQiaoHealthReturnKind.Program, occurrence, f.InstructionIndex, q.Before, Pool: pool);
        ReplaceRuntimeTop(f with { HappyQiaoHealth = receipt });
        AdvanceEventRulesAndQueueFact(new HappyQiaoHealthRequestedEvent(receipt.Id, f.Id, target, HpChangeKind.Loss, amount, HappyQiaoHealthReturnKind.Program, occurrence));
        AdvanceEventRulesAndQueueFact(new HappyQiaoHealthPoolSelectedEvent(receipt.Id, f.Id, target, pool, q.Before.Hp(pool), q.Before.MaximumHp));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44500)] private void CaptureFengXiLoss(ProgramSkillHpLostEvent fact)
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryFengXi: { Stage: AnniversaryFengXiStage.HealthApplying } r } f && fact.FrameId == f.Id && fact.SkillId == f.SkillId && fact.TargetSeat == r.Target && fact.Amount == r.Amount)
            ReplaceRuntimeTop(f with { AnniversaryFengXi = r with { Stage = AnniversaryFengXiStage.HealthChildren } });
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44501)] private void CapturePreventedFengXiLoss(IdentityHpDeductionPreventedEvent fact)
    {
        if (_resolutionStack.LastOrDefault() is IdentityAppearanceFrame a && a.Id == fact.AppearanceFrameId && fact.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
            _resolutionStack.SingleOrDefault(parent => parent.Id == a.Receipt.ParentFrameId) is ProgramSkillFrame { AnniversaryFengXi: { Stage: AnniversaryFengXiStage.HealthApplying } r } f && fact.RequestedDeduction == r.Amount && a.Receipt.OwnerSeat == r.Target)
            ReplaceRuntimeFrame(f.Id, f with { AnniversaryFengXi = r with { Stage = AnniversaryFengXiStage.HealthChildren } });
    }
    private bool DrainFengXi(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is not null ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is not null ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryFengXiBoyan)] private void ReturnFengXiDraw(ProgramSkillFrame f, NativeDrawOutcome d)
    {
        if (f.AnniversaryFengXi is not { Stage: AnniversaryFengXiStage.Drawing, Draw: null } r || d.RecipientSeat != r.Target || d.RequestedCount != r.Amount || d.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Boyan lost its actual capped-total-Hand native draw return.");
        ReplaceRuntimeTop(f with { AnniversaryFengXi = r with { Stage = AnniversaryFengXiStage.DrawChildren, Draw = d, Before = d.SequenceBefore, After = d.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryFengXiDrawReturnedEvent(f.Id, d.RecipientSeat, d.RequestedCount, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter, d.AttemptFrameId)); ContinueFengXi(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44500)] private bool ReturnFengXiMovement(ProgramSkillFrame f) => f.AnniversaryFengXi is not null && ContinueFengXi(f.Id);
    private void FinishFengXiOperation(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { AnniversaryFengXi = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryFengXiYusui)] private PromptChoice ChooseFengXi(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "hp") ?? p.Choices[0];
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44500)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44500)]
    private bool RetainIssuedFengXi(ProgramSkillFrame f) => f.AnniversaryFengXi is not null;
}
