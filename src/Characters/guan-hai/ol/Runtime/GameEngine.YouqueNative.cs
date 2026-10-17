namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardActionContext PrepareYouqueSlashAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanHai: { Paid: true, Stage: GuanHaiStage.SlashIssuing } r } f || r.Op != SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian) return action;
        if (action.Type != CardActionType.Use || action.EffectiveKind != CardKind.Slash || action.ActorSeat != r.LoserSeat || action.ProviderSeat != r.LoserSeat || action.RequesterSeat is not null || action.PhysicalCards.Count != 0 || action.ConversionChain is not [var source] || source != r.Source || !action.TargetSeats.SequenceEqual([r.WinnerSeat]) || r.NativeUseId != 0 || ProgramEventHistory<YouquePindianReturnedEvent>().Count(e => e.FrameId == f.Id) != 1) throw new InvalidOperationException("Youque cannot prepare a different actor, target, source or material group before native use recording.");
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, Suit.None, 0, null, action.FactionOrigin);
    }
    private void IssueYouqueSlash(ProgramSkillFrame f)
    {
        var r = f.GuanHai!; var actor = _players[r.LoserSeat]; var target = _players[r.WinnerSeat];
        if (!actor.IsAlive || !target.IsAlive || _winner != Winner.None || !CanUseVirtualSlashTarget(actor, target, CardKind.Slash, Suit.None, ignoreDistance: true, countsTowardLimit: false, effectiveColor: null)) { FinishGuanHai(f, true); return; }
        ReplaceRuntimeTop(f = f with { GuanHai = r with { Stage = GuanHaiStage.SlashIssuing, Before = _movementSequence, After = _movementSequence } });
        var id = BeginCardUse(new(0, CardKind.Slash, Suit.None, 0), actor.Seat, [target.Seat], CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), physicalCardIds: [], conversionSource: r.Source);
        var use = LifecycleCardUse(id)!;
        if (use.YouqueSlash is null) throw new InvalidOperationException("Youque's native Slash was not bound before its declaration.");
        var attack = new CardAttackHandle(this, id, actor.Seat, use.TargetSeats[0], card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, _players[use.TargetSeats[0]], CardKind.Slash), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, use.TargetSeats[0], IgnoresArmor: attack.IgnoresArmor)); NotifyAiOfSlash(actor, _players[use.TargetSeats[0]]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, LifecycleCardUse(id)!.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private void BindYouqueSlashUse(CardUseFrame supplied)
    {
        var at = _resolutionStack.FindIndex(f => f.Id == supplied.Id);
        if (at < 1 || _resolutionStack[at - 1] is not ProgramSkillFrame { GuanHai: { Stage: GuanHaiStage.SlashIssuing } r } parent) return;
        var use = LifecycleCardUse(supplied.Id)!;
        if (r.Op != SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian || !r.Paid || r.NativeUseId != 0 || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || a.ActorSeat != r.LoserSeat || a.ProviderSeat != r.LoserSeat || a.RequesterSeat is not null || a.ConversionChain is not [var source] || source != r.Source || !use.TargetSeats.SequenceEqual([r.WinnerSeat]) || !a.TargetSeats.SequenceEqual(use.TargetSeats) || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null || ProgramEventHistory<YouquePindianReturnedEvent>().Count(e => e.FrameId == parent.Id) != 1) throw new InvalidOperationException("Youque cannot change the real loser, winner, zero output or original Pindian payment.");
        var origin = new YouqueSlashOrigin(parent.Id, r.Source, r.GameplayHash, r.Phase!, r.LoserSeat, r.WinnerSeat, use.Id, a.ActionId);
        ReplaceRuntimeFrame(parent.Id, parent with { GuanHai = r with { Stage = GuanHaiStage.NativeSlash, NativeUseId = use.Id, ActionId = a.ActionId, NativeReturned = false } }); UpdateLifecycleCardUse(use.Id, u => u with { YouqueSlash = origin });
        AdvanceEventRulesAndQueueFact(new YouqueSlashIssuedEvent(origin));
    }
    private bool IsIssuedYouqueUse(CardUseFrame use)
    {
        if (use.YouqueSlash is not { } o || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } current || !IsSlashCard(use.CardKind) || o.UseId != use.Id || o.ActionId != current.ActionId || current.ProviderSeat != o.ActorSeat || current.RequesterSeat is not null || _contentRegistry.GetSkill(o.Source.SkillId).Program?.GameplayHash != o.GameplayHash || ProgramEventHistory<YouqueSlashIssuedEvent>().Count(e => e.Origin == o) != 1 || _resolutionStack.SingleOrDefault(f => f.Id == o.ProgramFrameId) is not ProgramSkillFrame { GuanHai: { Stage: GuanHaiStage.NativeSlash, Paid: true, NativeReturned: false } r } parent || r.NativeUseId != use.Id || r.ActionId != o.ActionId || r.Source != o.Source || r.GameplayHash != o.GameplayHash || r.LoserSeat != o.ActorSeat || r.WinnerSeat != o.TargetSeat || r.Phase != o.Phase) return false;
        var original = current; if (use.CurrentSlashFirePolicy is { } fire) { AssertCurrentSlashFirePolicy(use); original = fire.OriginalAction; } else if (use.CardKind != CardKind.Slash) return false;
        if (original.EffectiveKind != CardKind.Slash || original.EffectiveSuit != Suit.None || original.EffectiveRank != 0 || original.EffectiveIsRed is not null || original.ConversionChain is not [var source] || source != o.Source) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, o.ActorSeat, o.ActorSeat) : _resolutionStack.LastOrDefault()?.Id == use.Id && use.SourceSeat == o.ActorSeat && current.ActorSeat == o.ActorSeat;
    }
    private bool IsYouqueUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedYouqueUse(use);
    private Card YouqueRepresentation(long id) => LifecycleCardUse(id) is { } use && IsIssuedYouqueUse(use) ? new(0, use.CardKind, Suit.None, 0) : throw new InvalidOperationException("Youque requires its issued native zero-entity representation.");
    private bool FinishYouqueAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (attack.Card is not null || !IsYouqueUse(attack.ResolutionId)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(27579)] private void ValidateYouqueNative(CardUseFrame use) { if (use.YouqueSlash is not null && (!IsIssuedYouqueUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Youque cannot retire before its exact whole native Slash completes."); }
    [NativeUseReturn(27579)] private void ReturnYouqueNative(CardUseFrame use)
    {
        if (use.YouqueSlash is not { } o) return; var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.GuanHai!;
        if (r.Stage != GuanHaiStage.NativeSlash || r.NativeReturned || r.NativeUseId != use.Id || r.ActionId != o.ActionId || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1) throw new InvalidOperationException("Youque lost its unique typed native return.");
        ReplaceRuntimeTop(f with { GuanHai = r with { NativeReturned = true } }); if (use.CardAttack is not { Active: true }) SettleYouqueNative(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 27579)]
    private bool ReturnYouqueAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanHai: { NativeReturned: true, Stage: GuanHaiStage.NativeSlash } r } f || c.ResolutionId != r.NativeUseId) return false;
        if (c.ProgramFrameId != f.Id) throw new InvalidOperationException("Youque's native attack returned to a different program.");
        var o = ProgramEventHistory<YouqueSlashIssuedEvent>().Single(e => e.Origin.ProgramFrameId == f.Id).Origin; SettleYouqueNative(o); return true;
    }
    private void SettleYouqueNative(YouqueSlashOrigin o)
    {
        var f = GetActiveProgramFrame(o.ProgramFrameId); var r = f.GuanHai!;
        if (!r.NativeReturned || r.NativeUseId != o.UseId || r.ActionId != o.ActionId || LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<YouqueSlashReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Youque cannot settle its native Use twice.");
        ReplaceRuntimeTop(f with { GuanHai = r with { Stage = GuanHaiStage.NativeChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); AdvanceEventRulesAndQueueFact(new YouqueSlashReturnedEvent(f.Id, o.UseId, o.ActionId));
    }
    private CardUseFrame? YouqueRewardUse(ProgramSkillWindowContext c)
    {
        if (c.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.Amount <= 0 || c.DamageFrameId is not { } id || _resolutionStack.SingleOrDefault(f => f.Id == c.ParentFrameId) is not DamageTriggerWindowFrame w || w.ParentFrameId != id || _resolutionStack.SingleOrDefault(f => f.Id == id) is not DamageFrame damage || damage.TargetSeat != c.TargetSeat || damage.Amount != c.Amount || LifecycleCardUse(damage.ParentFrameId) is not { YouqueSlash: not null } use || !IsIssuedYouqueUse(use)) return null;
        return use;
    }
    private void AppendGuanHaiYouqueDamageCandidates(IDamageAttempt attack, SkillProgramTriggerWindow window, List<DamageTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.AfterDamageApplied || LifecycleCardUse(attack.ResolutionId) is not { YouqueSlash: { } o } use || !IsIssuedYouqueUse(use) || !_players[o.Source.OwnerSeat].IsAlive) return;
        var t = _contentRegistry.GetSkill(o.Source.SkillId).Program!.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit }]);
        result.RemoveAll(c => c.ProgramId == o.Source.SkillId && c.OwnerSeat == o.Source.OwnerSeat && c.ProgramTriggerId == t.Id);
        result.Add(new(o.Source.OwnerSeat, $"program:{o.Source.SkillId}:{t.Id}:{o.Source.SkillInstanceId}:occurrence-0", t.Priority, false, o.Source.SkillId, t.Id, o.Source.SkillInstanceId, o.GameplayHash, 0));
    }
    private bool IsIssuedGuanHaiYouqueCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (YouqueRewardUse(context) is not { YouqueSlash: { } o } || c.OwnerSeat != o.Source.OwnerSeat || c.SkillId != o.Source.SkillId || c.SkillInstanceId != o.Source.SkillInstanceId || c.GameplayHash != o.GameplayHash || GetProgramTrigger(c).Effects is not [{ Op: SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit }] || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not DamageTriggerWindowFrame w || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex].ToProgramCandidate() != c) return false;
        return !ProgramEventHistory<YouqueBenefitIssuedEvent>().Any(e => e.DamageFrameId == context.DamageFrameId && e.UseId == o.UseId);
    }
    private bool TryGetXieweiResponseCostMovement(CardActionContext action, int cardId, out CardMovementRecord movement)
    {
        movement = null!;
        var matches = _resolutionStack.OfType<CardUseFrame>().SelectMany(u => u.XieweiPayments ?? []).Where(p => p.ResponseUse && p.Paid && p.ActionId == action.ActionId && p.OwnerSeat == action.ProviderSeat && p.ActorSeat == action.ActorSeat && p.RequesterSeat == action.RequesterSeat && XieweiActionCosts(p, action) && ValidXieweiPayment(p)).ToArray();
        if (matches is not [var payment] || payment.Movements.SingleOrDefault(m => m.CardId == cardId) is not { } paid) return false;
        movement = paid; return true;
    }
}
