namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueShiZyVirtualDuel(ProgramSkillFrame f)
    {
        var r = f.ShiZhangYan!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.Target].IsAlive || !CanIssueSelectedActorDuel(r.Target, f.OwnerSeat)) { FinishShiZy(f); return; }
        var original = FrozenShiZyCompletedSlash(f) ?? throw new InvalidOperationException("Xiaoge lost its exact completed Slash before suspending its native attack.");
        if (original.CardAttack!.Active != r.OriginalAttackActive)
            throw new InvalidOperationException("Xiaoge cannot suspend a modified original native Slash cursor.");
        UpdateLifecycleCardUse(original.Id, use => use with { CardAttack = use.CardAttack! with { Active = false } });
        var origin = new ProgramVirtualOrdinaryTrickOrigin(f.Id, f.InstructionIndex, r.WindowId, f.OwnerSeat, f.OwnerSeat, r.Target,
            f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, _turnNumber, CardKind.Duel, SkillProgramConditionKind.Always, null, null);
        var id = _resolutionSequence + 1;
        ReplaceRuntimeTop(f = f with { ShiZhangYan = r with { NativeUseId = id, Duel = origin, Stage = ShiZyStage.DuelIssued } });
        var card = new Card(0, CardKind.Duel, Suit.None, 0);
        if (BeginCardUse(card, f.OwnerSeat, [r.Target], CardKind.Duel, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin) != id)
            throw new InvalidOperationException("Xiaoge lost the reserved exact zero-entity native Duel.");
        var native = LifecycleCardUse(id)!; origin = native.VirtualOrdinaryTrickOrigin!;
        if (_resolutionStack.SingleOrDefault(frame => frame.Id == f.Id) is not ProgramSkillFrame parent || parent != f ||
            _resolutionStack.FindIndex(frame => frame.Id == id) != _resolutionStack.FindIndex(frame => frame.Id == f.Id) + 1)
            throw new InvalidOperationException("Xiaoge's newly issued native Duel lost its frozen original parent.");
        ReplaceRuntimeFrame(f.Id, f with { ShiZhangYan = f.ShiZhangYan! with { Duel = origin } });
        AdvanceEventRulesAndQueueFact(new ShiZyDuelIssuedEvent(id, origin));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, native.TargetSeats, LegalActionKind.Duel, playedCardKind: CardKind.Duel);
    }
    // This rule contributes qualification only; appearance, counterspells,
    // native Duel responses, damage and return payment use the shared origin.
    private bool ValidShiZyVirtualDuelUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: ShiZyXiaoge, OutputKind: CardKind.Duel } o || use.CardId != 0 || use.CardKind != CardKind.Duel ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a ||
            a.ActionId != o.CardActionId || a.ProviderSeat != o.InitialActorSeat || !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
            o.ConditionKind != SkillProgramConditionKind.Always || o.PindianBind is not null || o.FrozenSourceWon is not null || o.TurnNumber != _turnNumber ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ParentProgramFrameId) is not ProgramSkillFrame { ShiZhangYan: { Stage: ShiZyStage.DuelIssued } r } f ||
            r.Op != SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel || f.SkillId != ShiZyXiaoge || f.TriggerId != "sole-slash-duel" ||
            f.InstructionIndex != o.InstructionIndex || f.OwnerSeat != o.OwnerSeat || f.SkillInstanceId != o.SkillInstanceId || f.GameplayHash != o.GameplayHash ||
            r.Source != ShiZySource(f) || r.Hash != o.GameplayHash || r.WindowId != o.LifecycleFrameId || f.WindowContext?.ParentFrameId != r.WindowId ||
            r.NativeUseId != use.Id || r.Duel is not { } issued || (issued with { CardActionId = 0 }) != (o with { CardActionId = 0 }) ||
            o.InitialActorSeat != f.OwnerSeat || o.InitialTargetSeat != r.Target || !ValidShiZy(f) ||
            LifecycleCardUse(r.UseId) is not { Step: ResolutionFrameStep.Completed, Action: { } slash } original ||
            !IsSlashCard(original.CardKind) || slash.ActionId != r.ActionId || slash.ActorSeat != f.OwnerSeat ||
            original.SourceSeat != f.OwnerSeat || original.TargetIndex != r.OriginalTargetIndex || original.CardAttack?.Active != false ||
            r.OriginalAttackActive is null || slash.EffectiveDesignatedTargetSeats is not [var target] || target != r.Target)
            return false;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        return index > 0 && _resolutionStack[index - 1].Id == f.Id && MatchesDeclaredActualDamageUse(use, a.ActorSeat, o.InitialActorSeat);
    }
    private bool TryReturnShiZyVirtualDuel(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: ShiZyXiaoge } origin) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhangYan: { Stage: ShiZyStage.DuelIssued } r } f ||
            f.Id != origin.ParentProgramFrameId || f.InstructionIndex != origin.InstructionIndex || r.NativeUseId != use.Id || r.Duel != origin ||
            use.Step != ResolutionFrameStep.Completed || !ValidShiZy(f)) throw new InvalidOperationException("Xiaoge's native Duel lost its exact original completed-Slash program return.");
        if (use.CardAttack is { Active: true })
        {
            if (ProgramEventHistory<ProgramVirtualOrdinaryAttackReturnedEvent>().Any(e => e.UseId == use.Id)) throw new InvalidOperationException("Xiaoge Duel cannot return the same native attack twice.");
            AdvanceEventRulesAndQueueFact(new ProgramVirtualOrdinaryAttackReturnedEvent(use.Id, origin)); return true;
        }
        CompleteShiZyDuelReturn(f, use.Id, origin); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 50500)]
    private bool ResumeShiZyNativeDuel()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiZhangYan: { Stage: ShiZyStage.DuelIssued, Duel: { } origin } r } f ||
            r.Op != SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel || _resolutionStack.Any(x => x.Id == r.NativeUseId) ||
            !ProgramEventHistory<ProgramVirtualOrdinaryAttackReturnedEvent>().Any(e => e.UseId == r.NativeUseId && e.Origin == origin)) return false;
        CompleteShiZyDuelReturn(f, r.NativeUseId, origin); return true;
    }
    private void CompleteShiZyDuelReturn(ProgramSkillFrame f, long useId, ProgramVirtualOrdinaryTrickOrigin origin)
    {
        if (ProgramEventHistory<ShiZyDuelReturnedEvent>().Any(e => e.UseId == useId)) throw new InvalidOperationException("Xiaoge's issued Duel already returned.");
        ReplaceRuntimeTop(f = f with { ShiZhangYan = f.ShiZhangYan! with { Stage = ShiZyStage.DuelChildren } });
        AdvanceEventRulesAndQueueFact(new ShiZyDuelReturnedEvent(useId, origin)); ContinueShiZy(f.Id);
    }

    private CardUseFrame? FrozenShiZyCompletedSlash(ProgramSkillFrame f) => f.ShiZhangYan is
        { Op: SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel, OriginalAttackActive: not null, OriginalTargetIndex: { } cursor } r &&
        ValidShiZy(f) && LifecycleCardUse(r.UseId) is { Step: ResolutionFrameStep.Completed, Action: { Type: CardActionType.Use } action, CardAttack: not null } use &&
        use.SourceSeat == f.OwnerSeat && action.ActorSeat == f.OwnerSeat && use.TargetIndex == cursor && action.ActionId == r.ActionId &&
        IsSlashCard(use.CardKind) && action.EffectiveDesignatedTargetSeats is [var target] && target == r.Target &&
        _resolutionStack.SingleOrDefault(frame => frame.Id == r.WindowId) is ProgramCardTriggerWindowFrame window &&
        window.ParentFrameId == use.Id && window.AttackOwnerFrameId == use.Id && window.Continuation == ProgramCardContinuation.CompletedSlash &&
        window.CompletedResponseReturn is null && SameOverflowAction(window.Action, action) &&
        ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardKind == use.CardKind) == 1 ? use : null;

    private void RestoreShiZyCompletedSlash(ProgramSkillFrame f)
    {
        var r = f.ShiZhangYan!;
        if (r.Stage != ShiZyStage.DuelChildren || r.Duel is not { } origin || r.OriginalAttackActive is not { } active ||
            FrozenShiZyCompletedSlash(f) is not { CardAttack.Active: false } original ||
            _resolutionStack.Any(frame => frame.Id == r.NativeUseId) ||
            ProgramEventHistory<ShiZyDuelReturnedEvent>().Count(e => e.UseId == r.NativeUseId && e.Origin == origin) != 1 ||
            ProgramEventHistory<ShiZyAttackRestoredEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Xiaoge cannot restore a different, unfinished or already restored original Slash attack.");
        if (active) UpdateLifecycleCardUse(original.Id, use => use with { CardAttack = use.CardAttack! with { Active = true } });
        AdvanceEventRulesAndQueueFact(new ShiZyAttackRestoredEvent(f.Id, original.Id, r.ActionId, original.TargetIndex, active));
    }

    private bool HasShiZySuspendedCompletedParent(ProgramCardTriggerWindowFrame window)
    {
        var at = _resolutionStack.FindIndex(frame => frame.Id == window.Id);
        if (window.Continuation != ProgramCardContinuation.CompletedSlash || window.CompletedResponseReturn is not null ||
            window.Action.Type != CardActionType.Use || window.AttackOwnerFrameId != window.ParentFrameId || at < 1 ||
            _resolutionStack[at - 1] is not CardUseFrame { Step: ResolutionFrameStep.Completed, Action: { } action, CardAttack: not null } original ||
            original.Id != window.ParentFrameId || !IsSlashCard(original.CardKind) || !SameOverflowAction(action, window.Action) ||
            at + 1 >= _resolutionStack.Count || _resolutionStack[at + 1] is not ProgramSkillFrame
                { ShiZhangYan: { Stage: ShiZyStage.DuelIssued or ShiZyStage.DuelChildren, Duel: { } origin } r } child ||
            child.SkillId != ShiZyXiaoge || child.TriggerId != "sole-slash-duel" || child.OwnerSeat != action.ActorSeat ||
            child.WindowContext?.ParentFrameId != window.Id || r.WindowId != window.Id || r.UseId != original.Id || r.ActionId != action.ActionId ||
            r.Op != SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel || r.NativeUseId <= 0 || !ValidShiZy(child) ||
            r.OriginalAttackActive is null || original.TargetIndex != r.OriginalTargetIndex || original.SourceSeat != child.OwnerSeat || original.CardAttack.Active ||
            origin.ParentProgramFrameId != child.Id || origin.InstructionIndex != child.InstructionIndex || origin.LifecycleFrameId != window.Id ||
            origin.OwnerSeat != child.OwnerSeat || origin.InitialActorSeat != child.OwnerSeat || origin.InitialTargetSeat != r.Target ||
            origin.SkillId != child.SkillId || origin.BindingId != child.TriggerId || origin.SkillInstanceId != child.SkillInstanceId ||
            origin.GameplayHash != child.GameplayHash || origin.TurnNumber != r.Turn || origin.OutputKind != CardKind.Duel || origin.CardActionId <= 0 ||
            action.EffectiveDesignatedTargetSeats is not [var target] || target != r.Target ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.Id && e.CardKind == original.CardKind) != 1 ||
            ProgramEventHistory<ShiZyDuelIssuedEvent>().Count(e => e.UseId == r.NativeUseId && e.Origin == origin) != 1 ||
            action.PhysicalCards.Any(cost => _cardZones.GetLocation(cost.CardId).Zone is not
                (CardZoneKind.DiscardPile or CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.Equipment or CardZoneKind.Judgment)) ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var candidate = window.Candidates[window.CandidateIndex];
        if (candidate.OwnerSeat != child.OwnerSeat || candidate.SkillId != child.SkillId || candidate.SkillInstanceId != child.SkillInstanceId ||
            candidate.TriggerId != child.TriggerId || candidate.GameplayHash != child.GameplayHash ||
            CreateCardActionProgramContext(window, candidate) != child.WindowContext) return false;
        if (r.Stage == ShiZyStage.DuelIssued)
            return at + 2 < _resolutionStack.Count && _resolutionStack[at + 2] is CardUseFrame native &&
                native.Id == r.NativeUseId && native.VirtualOrdinaryTrickOrigin == origin && ValidShiZyVirtualDuelUse(native);
        return !_resolutionStack.Any(frame => frame.Id == r.NativeUseId) &&
            ProgramEventHistory<ShiZyDuelReturnedEvent>().Count(e => e.UseId == r.NativeUseId && e.Origin == origin) == 1 &&
            (at + 2 == _resolutionStack.Count || ShiZyFirstChild(child, _resolutionStack[at + 2]) && ShiZySuffix(at + 1));
    }
}
