namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void CompleteShiXxyJiejie(ProgramSkillFrame f)
    {
        var r = f.ShiXinXianYing!;
        var count = ShiXxySuitCount(ShiXxyRoundMask(f.OwnerSeat, r.Actor));
        if (_winner == Winner.None && _players[f.OwnerSeat].IsAlive && _players[r.Actor].IsAlive && ShiXxyRoundUsed(f.OwnerSeat) < 2 &&
            _players.Where(p => p.Seat != r.Actor).All(p => ShiXxySuitCount(ShiXxyRoundMask(f.OwnerSeat, p.Seat)) < count))
        { IssueShiXxyQingshi(f, r.Actor); return; }
        FinishShiXxy(f);
    }
    private void IssueShiXxyQingshi(ProgramSkillFrame f, int target)
    {
        var r = f.ShiXinXianYing!;
        if (r.QingIssued || !_players[target].IsAlive) throw new InvalidOperationException("Qingshi cannot replace or repeat its original target.");
        var same = ShiXxySameCamp(f.OwnerSeat, target);
        ReplaceRuntimeTop(f = f with { ShiXinXianYing = r with { Target = target, SameCamp = same, QingIssued = true,
            Stage = r.Jiejie ? ShiXinXianYingStage.QingshiInvocation : same ? ShiXinXianYingStage.DrawChildren : ShiXinXianYingStage.OwnerDiscard, DrawCursor = 0,
            QingshiInstance = GetSkillBindingShard(_players[f.OwnerSeat]).ProgramInstances.FirstOrDefault(i => i.SkillId == ShiXxyQingshi)?.SkillInstanceId ?? "" }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new ShiXinXianYingQingshiIssuedEvent(f.Id, r.Source, target, _roundNumber, r.Jiejie, same));
        ContinueShiXxy(f.Id);
    }
    private void SealShiXxy(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeFrame(id, f with { ShiXinXianYing = f.ShiXinXianYing! with { After = _movementSequence } }); }
    private bool DrainShiXxy(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueShiXxy(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ShiXinXianYing: { } r } f && f.Id == id && _pendingDecision is null)
        {
            if (r.Stage is ShiXinXianYingStage.Suit or ShiXinXianYingStage.Target) { PublishShiXxy(f); return; }
            if (r.Stage == ShiXinXianYingStage.Drawing) return;
            if (r.Stage == ShiXinXianYingStage.QingshiInvocation)
            {
                if (TryBeginSkillInvocation(f.Id, f.OwnerSeat, ShiXxyQingshi, "shi-jiejie-qingshi:" + f.Id, r.QingshiInstance)) return;
                ReplaceRuntimeTop(GetActiveProgramFrame(id) with { ShiXinXianYing = r with { Stage = r.SameCamp ? ShiXinXianYingStage.DrawChildren : ShiXinXianYingStage.OwnerDiscard } }); continue;
            }
            if (r.Stage == ShiXinXianYingStage.OwnerDiscard)
            {
                if (ShiXxyChoices(f).Count > 0) { PublishShiXxy(f); return; }
                ReplaceRuntimeTop(f with { ShiXinXianYing = r with { Stage = ShiXinXianYingStage.OtherDiscard } }); continue;
            }
            if (r.Stage == ShiXinXianYingStage.OtherDiscard)
            {
                if (ShiXxyChoices(f).Count > 0) { PublishShiXxy(f); return; }
                ReplaceRuntimeTop(f with { ShiXinXianYing = r with { Stage = ShiXinXianYingStage.QingDiscardChildren } }); continue;
            }
            if (r.Stage == ShiXinXianYingStage.QingDiscardChildren && !r.DiscardPaid)
            {
                var ids = r.Selected.Where(m => _cardZones.GetLocation(m.CardId) == m.From &&
                    !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, GetAdvancedCard(m.CardId), m.From, OwnedCardMoveIntent.Discard)).Select(m => m.CardId).ToArray();
                ReplaceRuntimeTop(f = f with { ShiXinXianYing = r = r with { DiscardPaid = true, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                if (ids.Length > 0) MoveProgramLogicalDiscard(f, f.OwnerSeat, ids, new("skill-program.mobile-shi-qingshi.discard"));
                SealShiXxy(id); continue;
            }
            if (DrainShiXxy(f)) return;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.ShiXinXianYing!;
            if (r.Stage == ShiXinXianYingStage.JiejieChildren)
            {
                if (_players[r.Actor].IsAlive && ProgramEventHistory<ShiXinXianYingSuitChosenEvent>().Single(e => e.FrameId == id).Included)
                    AdvanceEventRulesAndQueueFact(new ShiXinXianYingSuitAllowanceEvent(id, r.Source, r.GameplayHash, r.Actor, r.ActualTurn, r.ChosenSuit));
                CompleteShiXxyJiejie(f); return;
            }
            if (r.Stage == ShiXinXianYingStage.DrawChildren)
            {
                var maximum = r.Target == f.OwnerSeat ? 1 : 2;
                if (r.DrawCursor >= maximum || _winner != Winner.None) { FinishShiXxy(f); return; }
                var target = r.DrawCursor == 0 ? f.OwnerSeat : r.Target;
                var requested = r.Target == f.OwnerSeat ? 2 : 1;
                if (!_players[target].IsAlive) { ReplaceRuntimeTop(f with { ShiXinXianYing = r with { DrawCursor = r.DrawCursor + 1 } }); continue; }
                ReplaceRuntimeTop(f = f with { ShiXinXianYing = r with { Stage = ShiXinXianYingStage.Drawing, Before = _movementSequence, After = _movementSequence } });
                AdvanceEventRulesAndQueueFact(new ShiXinXianYingDrawIssuedEvent(id, r.DrawCursor, target, requested));
                BeginOperationProgramDraw(f, target, requested, new("skill-program.mobile-shi-qingshi.draw")); return;
            }
            FinishShiXxy(f); return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie, SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi)]
    private void ReturnShiXxyDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        var r = f.ShiXinXianYing ?? throw new InvalidOperationException("Qingshi draw lost its owning frame.");
        var request = ProgramEventHistory<ShiXinXianYingDrawIssuedEvent>().SingleOrDefault(e => e.FrameId == f.Id && e.Cursor == r.DrawCursor);
        if (r.Stage != ShiXinXianYingStage.Drawing || request is null || request.Recipient != outcome.RecipientSeat || request.Requested != outcome.RequestedCount ||
            ProgramEventHistory<ShiXinXianYingDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Cursor == r.DrawCursor))
            throw new InvalidOperationException("Qingshi rejects a repeated or foreign draw return.");
        ReplaceRuntimeTop(f = f with { ShiXinXianYing = r with { Stage = ShiXinXianYingStage.DrawChildren, DrawCursor = r.DrawCursor + 1,
            Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiXinXianYingDrawReturnedEvent(f.Id, r.DrawCursor, outcome.RecipientSeat, outcome.ActualDrawIds.Count));
        ContinueShiXxy(f.Id);
    }
    private void FinishShiXxy(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { ShiXinXianYing = f.ShiXinXianYing! with { Stage = ShiXinXianYingStage.Finished }, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 48200)] private bool ResumeShiXxy(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXinXianYing: not null } f || f.Id != id) return false; ContinueShiXxy(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 48200)] private bool ReturnShiXxyMovement(ProgramSkillFrame f)
    { if (f.ShiXinXianYing is null) return false; ContinueShiXxy(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 48200)] private bool ShiXxyMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.ShiXinXianYing is { Stage: ShiXinXianYingStage.JiejieChildren or ShiXinXianYingStage.QingDiscardChildren or ShiXinXianYingStage.DrawChildren } &&
        p.BeforeCount == 0 && p.CoverageResultBind is null && effect?.Op is SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie or SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 48200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 48200)]
    private bool PaidShiXxy(ProgramSkillFrame f) => f.ShiXinXianYing is { Stage: ShiXinXianYingStage.JiejieChildren or ShiXinXianYingStage.QingshiInvocation or ShiXinXianYingStage.QingDiscardChildren or ShiXinXianYingStage.Drawing or ShiXinXianYingStage.DrawChildren };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 48200)] private void AssertShiXxyFrame(ProgramSkillFrame f)
    {
        if (f.ShiXinXianYing is not { } r) return;
        if (r.Source != ShiXxySource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || r.Round != _roundNumber ||
            !IsValidPlayerSeat(r.Actor) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Viewed.Select(c => c.CardId).Distinct().Count() != r.Viewed.Count || r.Selected.Select(c => c.CardId).Distinct().Count() != r.Selected.Count || r.Selected.Count > 2 ||
            r.Jiejie != (f.SkillId == ShiXxyJiejie) || f.SkillId is not (ShiXxyJiejie or ShiXxyQingshi) ||
            r.QingIssued && (ProgramEventHistory<ShiXinXianYingQingshiIssuedEvent>().SingleOrDefault(e => e.FrameId == f.Id) is not { } q || q.Target != r.Target || q.SameCamp != r.SameCamp || q.FromJiejie != r.Jiejie))
            throw new InvalidOperationException("Shi Xin Xian Ying lost her exact source, round, private view, actual participant or once-issued Qingshi.");
        if (r.Stage == ShiXinXianYingStage.Suit && (!r.Viewed.Select(c => c.CardId).SequenceEqual(GetHand(_players[r.Actor]).Select(c => c.Id)) ||
            ProgramEventHistory<ProgramPrivateHandViewedEvent>().Count(e => e.FrameId == f.Id && e.ViewerSeat == f.OwnerSeat && e.SourceSeat == r.Actor && e.Count == r.Viewed.Count) != 1))
            throw new InvalidOperationException("Jiejie cannot substitute new hand entities for its original private view.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie, SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi)]
    private PromptChoice ShiXxyAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.ShiXinXianYing!;
        if (r.Stage == ShiXinXianYingStage.Suit)
        {
            var same = ShiXxySameCamp(f.OwnerSeat, r.Actor);
            var missing = ShiXxySuits.FirstOrDefault(s => r.Viewed.All(c => c.Suit != s));
            var suit = same && missing != Suit.None ? missing : r.Viewed.GroupBy(c => c.Suit).OrderBy(g => g.Count()).FirstOrDefault()?.Key ?? Suit.Spade;
            return p.Choices.First(c => c.Parameters["branch"] == suit.ToString());
        }
        if (r.Stage == ShiXinXianYingStage.Target) return p.Choices.First(c => c.Targets.SequenceEqual([f.OwnerSeat]));
        return p.Choices.First();
    }
    private bool HasShiXxySuitAllowance(int actor, Suit? suit) => suit is { } s && s != Suit.None && _winner == Winner.None &&
        ProgramEventHistory<ShiXinXianYingSuitAllowanceEvent>().Any(e => e.Actor == actor && e.ActualTurn == _turnNumber && e.Suit == s);
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 48200)]
    private bool ShiXxySlashQuota(ProgramSlashQuotaQuery q)
    {
        var cards = q.PhysicalCardIds is { Count: > 0 } ids ? ids.Select(GetAdvancedCard).ToArray() : q.PhysicalCard is { } card ? [card] : [];
        return cards.Length > 0 && HasShiXxySuitAllowance(q.Actor.Seat, PhysicalGroupSuit(q.Actor, cards));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit, 48200)]
    private bool ShiXxyAlcoholDebit(long id, int actor) => LifecycleCardUse(id) is { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.Alcohol } action } &&
        action.ActorSeat == actor && HasShiXxySuitAllowance(actor, action.EffectiveSuit);
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> ShiXinXianYingPublicStates(CharacterState owner, string skillId)
    {
        if (skillId != ShiXxyJiejie) return [];
        return GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i => new[] {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "qingshi-round", ProgramPublicRuleStateKind.GameActivationProgress, ShiXxyRoundUsed(owner.Seat)),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "jiejie-active-suits", ProgramPublicRuleStateKind.GameActivationProgress,
                ShiXxyMask(ProgramEventHistory<ShiXinXianYingSuitAllowanceEvent>().Where(e => e.Actor == owner.Seat && e.ActualTurn == _turnNumber).Select(e => e.Suit))) }).ToArray();
    }
}
