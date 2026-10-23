namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<AnniversaryYangBiaoPlan> YangBiaoPlans(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!; var actor = _players[f.OwnerSeat];
        if (!actor.IsAlive || !IsValidPlayerSeat(r.Target) || !_players[r.Target].IsAlive || _winner != Winner.None) return [];
        var target = _players[r.Target]; var plans = new List<AnniversaryYangBiaoPlan>();
        if (CanUseVirtualSlashTarget(actor, target, CardKind.Slash, physicalSuit: Suit.None, effectiveRank: 0, ignoreDistance: true, countsTowardLimit: false) &&
            !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, CardKind.Slash) && !IsCardTargetProhibited(target, CardKind.Slash, Suit.None, null))
            plans.Add(new(CardKind.Slash, LegalActionKind.Slash) { Targets = [target.Seat] });
        plans.AddRange(BuildProgramOrdinaryTrickUseOptions(actor, CardKind.Dismantlement, Suit.None, enforceUsePermission: true, beneficiaryShieldSuit: Suit.None,
                actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []).Where(o => o.TargetSeats.SequenceEqual([target.Seat]))
            .Select(o => new AnniversaryYangBiaoPlan(o.EffectiveCardKind, o.ActionKind, o.TargetCardId, o.RequiredCardKind) { Targets = o.TargetSeats }));
        // Juee expressly names one recipient, rather than all Five Grains recipients.
        if (_cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile) > 0 &&
            BuildProgramOrdinaryTrickUseOptions(actor, CardKind.FiveGrains, Suit.None, enforceUsePermission: true, beneficiaryShieldSuit: Suit.None,
                actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []).Any() &&
            !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, CardKind.FiveGrains) && !IsCardTargetProhibited(target, CardKind.FiveGrains, Suit.None, null))
            plans.Add(new(CardKind.FiveGrains, LegalActionKind.FiveGrains) { Targets = [target.Seat] });
        return Array.AsReadOnly(plans.ToArray());
    }
    private static bool YangBiaoPlansEqual(AnniversaryYangBiaoPlan a, AnniversaryYangBiaoPlan b) =>
        (a.Kind, a.ActionKind, a.TargetCardId, a.RequiredKind) == (b.Kind, b.ActionKind, b.TargetCardId, b.RequiredKind) && a.Targets.SequenceEqual(b.Targets);
    private void NextYangBiaoUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!;
        if (r.Cursor >= r.Budget || !_players[f.OwnerSeat].IsAlive || !_players[r.Target].IsAlive || _winner != Winner.None) { CompleteYangBiao(f); return; }
        var plans = YangBiaoPlans(f); var kinds = plans.Select(p => p.Kind).Distinct().ToArray();
        if (kinds.Length == 0) { CompleteYangBiao(f); return; }
        var before = _random.State; var kind = kinds[_random.Next(kinds.Length)]; var after = _random.State;
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.UsePlan, RandomKinds = kinds, RandomBefore = before, RandomAfter = after, RandomKind = kind } });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoRandomCardEvent(f.Id, r.Cursor, kind));
        var selected = plans.Where(p => p.Kind == kind).ToArray();
        if (selected is [var only]) { ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = f.AnniversaryYangBiao! with { Plan = only, Stage = AnniversaryYangBiaoStage.IssuingUse } }); IssueYangBiaoUse(f); }
        else PublishYangBiao(f);
    }
    private void IssueYangBiaoUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!; var p = r.Plan!;
        if (!ValidYangBiao(f) || r.Stage != AnniversaryYangBiaoStage.IssuingUse || !YangBiaoPlans(f).Any(plan => YangBiaoPlansEqual(plan, p))) throw new InvalidOperationException("Juee lost its frozen currently legal fixed-recipient native plan.");
        if (p.Kind == CardKind.Slash) { IssueYangBiaoSlash(f); return; }
        var origin = new ProgramVirtualOrdinaryTrickOrigin(f.Id, f.InstructionIndex, 0, f.OwnerSeat, f.OwnerSeat, f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, r.Turn, p.Kind, SkillProgramConditionKind.Always, null, null);
        var card = new Card(0, p.Kind, Suit.None, 0); var id = BeginCardUse(card, f.OwnerSeat, p.Targets, p.Kind, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin);
        var use = LifecycleCardUse(id)!; f = _resolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f = f with { AnniversaryYangBiao = f.AnniversaryYangBiao! with { Stage = AnniversaryYangBiaoStage.NativeUse, UseId = id, ActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoNativeIssuedEvent(f.Id, r.Cursor, id, use.Action!.ActionId, p.Kind, r.Target));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, use.TargetSeats, p.ActionKind, p.TargetCardId, p.RequiredKind, p.Kind);
    }
    private void IssueYangBiaoSlash(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!; var p = r.Plan!; var actor = _players[f.OwnerSeat]; var target = _players[r.Target]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, null, CardActionType.Use, actor.Seat, actor.Seat, null, null, null, p.Kind, p.Targets, [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.NativeUse, UseId = id, ActionId = action.ActionId }, VirtualBasicDraft = new(f.InstructionIndex, id) });
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, p.Kind, p.Targets, PhysicalCardIds: []) { Action = action, VirtualBasicReturn = new(f.Id, f.InstructionIndex), UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoNativeIssuedEvent(f.Id, r.Cursor, id, action.ActionId, p.Kind, r.Target));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, p.Kind, actor.Seat)); AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, p.Targets));
        RecordYingboCardUse(id, actor.Seat, p.Kind); RecordProgramUsedBasicCard(actor.Seat, p.Kind); RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: p.Kind, ignoresArmor: HasCardArmorBypass(actor, target, p.Kind));
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack; MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, target.Seat)); TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, p.Targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private bool IsExactAnniversaryYangBiaoBasicDraft(ProgramSkillFrame f, ProgramVirtualBasicDraft draft) =>
        f.AnniversaryYangBiao is { Stage: AnniversaryYangBiaoStage.NativeUse, Plan.Kind: CardKind.Slash } r && ValidYangBiao(f) &&
        draft.InstructionIndex == f.InstructionIndex && draft.ChildFrameId == r.UseId &&
        ProgramEventHistory<AnniversaryYangBiaoNativeIssuedEvent>().Count(e => e == new AnniversaryYangBiaoNativeIssuedEvent(f.Id, r.Cursor, r.UseId, r.ActionId, CardKind.Slash, r.Target)) == 1;
    private bool IsAnniversaryYangBiaoVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: AnniversaryYangBiaoComposition.Juee } o ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ParentProgramFrameId) is not ProgramSkillFrame { AnniversaryYangBiao: { Plan: { } p } r } f ||
            !ValidYangBiao(f) || r.Stage is not (AnniversaryYangBiaoStage.IssuingUse or AnniversaryYangBiaoStage.NativeUse) || p.Kind is not (CardKind.Dismantlement or CardKind.FiveGrains) || use.CardKind != p.Kind || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } a || a.ActionId != o.CardActionId || a.ProviderSeat != f.OwnerSeat || a.EffectiveKind != p.Kind || a.EffectiveSuit is not (null or Suit.None) || a.EffectiveRank is not (null or 0) || a.EffectiveIsRed == true || !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
            o.OwnerSeat != f.OwnerSeat || o.InitialActorSeat != f.OwnerSeat || o.InitialTargetSeat != f.OwnerSeat || o.BindingId != f.ActivationId || o.SkillInstanceId != f.SkillInstanceId || o.GameplayHash != f.GameplayHash || o.InstructionIndex != f.InstructionIndex || o.LifecycleFrameId != 0 || o.TurnNumber != r.Turn ||
            r.UseId != 0 && (r.UseId != use.Id || r.ActionId != a.ActionId)) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, f.OwnerSeat, f.OwnerSeat) : a.ActorSeat == f.OwnerSeat;
    }
    private bool IsAnniversaryYangBiaoFiveGrainsUse(long id) => LifecycleCardUse(id) is { CardKind: CardKind.FiveGrains } use && IsAnniversaryYangBiaoVirtualUse(use);
    private bool TryReturnAnniversaryYangBiaoVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: AnniversaryYangBiaoComposition.Juee } o) return false;
        var f = GetActiveProgramFrame(o.ParentProgramFrameId); var r = f.AnniversaryYangBiao!;
        if (use.Step != ResolutionFrameStep.Completed || r.UseId != use.Id || r.ActionId != use.Action?.ActionId) throw new InvalidOperationException("Juee cannot return an unfinished or different native ordinary trick.");
        CompleteYangBiaoNative(f, use.CardAttack is { Active: true }); return true;
    }
    private void CompleteYangBiaoNative(ProgramSkillFrame f, bool? hadAttack = null)
    {
        var r = f.AnniversaryYangBiao!;
        if (!ValidYangBiao(f) || r.Stage != AnniversaryYangBiaoStage.NativeUse || LifecycleCardUse(r.UseId) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardId == 0 && e.CardKind == r.Plan!.Kind) != 1) throw new InvalidOperationException("Juee must return the same whole native Use once before advancing its ordinal.");
        // A basic Slash returns after its CardUse frame has been popped, so
        // ActiveCardAttack can no longer project it. Its native attack cleanup
        // still follows; issuing the next Use here would lose its new prompt
        // to that old cleanup. The original exact Slash plan proves this return.
        var attack = hadAttack ?? r.Plan!.Kind == CardKind.Slash;
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.ReturnedUse, HadAttack = attack } });
        AdvanceEventRulesAndQueueFact(new AnniversaryYangBiaoNativeReturnedEvent(f.Id, r.Cursor, r.UseId, attack));
        if (!attack) FinishYangBiaoUse(f);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 43700)] private bool ReturnYangBiaoAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryYangBiao: { Stage: AnniversaryYangBiaoStage.ReturnedUse, HadAttack: true, Plan.Kind: CardKind.Slash } r } f ||
            completion.ResolutionId != r.UseId) return false;
        FinishYangBiaoUse(f); return true;
    }
    private void FinishYangBiaoUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!;
        if (!ValidYangBiao(f) || r.Stage != AnniversaryYangBiaoStage.ReturnedUse) throw new InvalidOperationException("Juee lost the returned ordinal's native completion.");
        ReplaceRuntimeTop(f = f with { AnniversaryYangBiao = r with { Stage = AnniversaryYangBiaoStage.NextUse, Cursor = r.Cursor + 1, Plan = null, UseId = 0, ActionId = 0, RandomKind = null, RandomKinds = [], HadAttack = false } });
        ContinueYangBiao(f.Id);
    }
}
