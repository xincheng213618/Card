namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<AnniversaryLiuYongPlan> AnniversaryLiuYongPlans(int owner)
    {
        var actor = _players[owner]; if (!actor.IsAlive || _winner != Winner.None) return [];
        var result = new List<AnniversaryLiuYongPlan>();
        foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash })
            foreach (var target in CurrentTableOrder().Select(seat => _players[seat]).Where(p => CanUseVirtualSlashTarget(actor, p, kind, physicalSuit: Suit.None, effectiveRank: 0, countsTowardLimit: false) &&
                !IsDirectedCardTargetProhibited(owner, p.Seat, kind) && !IsCardTargetProhibited(p, kind, Suit.None, null)))
                result.Add(new(kind, LegalActionKind.Slash) { Targets = [target.Seat] });
        var options = BuildProgramOrdinaryTrickUseOptions(actor, physicalSuit: Suit.None, enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []);
        result.AddRange(NextActualUseOrdinaryTrickOptions(actor, options, Suit.None, null, Suit.None, false).Where(o => IsOrdinaryTrick(o.EffectiveCardKind) && (IsDamageCardKind(o.EffectiveCardKind) || IsCompletedUndamagedDamageUse(o.EffectiveCardKind)))
            .Select(o => new AnniversaryLiuYongPlan(o.EffectiveCardKind, o.ActionKind, o.TargetCardId, o.RequiredCardKind, o.NextActualUseAdjusted) { Targets = GetImplicitSelfCardUseTargets(o.EffectiveCardKind, owner, o.TargetSeats) }));
        return Array.AsReadOnly(result.ToArray());
    }
    private void IssueAnniversaryLiuYongUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuYong!; var p = r.Plan!;
        if (r.Stage != AnniversaryLiuYongStage.IssuingUse || !r.GiftPaid || !AnniversaryLiuYongPlans(f.OwnerSeat).Any(candidate => candidate == p ||
            (candidate.Kind, candidate.ActionKind, candidate.TargetCardId, candidate.RequiredKind, candidate.Adjusted) == (p.Kind, p.ActionKind, p.TargetCardId, p.RequiredKind, p.Adjusted) && candidate.Targets.SequenceEqual(p.Targets))) throw new InvalidOperationException("Zhuning requires its current accepted native card plan after the real gift.");
        if (IsSlashCard(p.Kind)) { IssueAnniversaryLiuYongSlash(f); return; }
        if (p.Adjusted) _selectedNextCardTargetSeats = p.Targets;
        var origin = new ProgramVirtualOrdinaryTrickOrigin(f.Id, f.InstructionIndex, 0, f.OwnerSeat, f.OwnerSeat, f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, r.Turn, p.Kind, SkillProgramConditionKind.Always, null, null);
        var card = new Card(0, p.Kind, Suit.None, 0); var useId = BeginCardUse(card, f.OwnerSeat, p.Targets, p.Kind, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin);
        var use = LifecycleCardUse(useId)!; f = _resolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f = f with { AnniversaryLiuYong = f.AnniversaryLiuYong! with { Stage = AnniversaryLiuYongStage.NativeUse, UseId = useId, ActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongNativeIssuedEvent(f.Id, useId, use.Action!.ActionId, p.Kind));
        BeginJizhiOrNullificationWindow(useId, card, f.OwnerSeat, use.TargetSeats, p.ActionKind, p.TargetCardId, p.RequiredKind, p.Kind);
    }
    private void IssueAnniversaryLiuYongSlash(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuYong!; var p = r.Plan!; var actor = _players[f.OwnerSeat]; var target = _players[p.Targets.Single()]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, f.OwnerSeat, f.OwnerSeat, null, null, null, p.Kind, p.Targets, [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        var origin = new AnniversaryLiuYongSlashOrigin(f.Id, r.Source, r.Hash, id, action.ActionId, p.Kind, target.Seat);
        ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r with { Stage = AnniversaryLiuYongStage.NativeUse, UseId = id, ActionId = action.ActionId } });
        PushRuntimeFrame(new CardUseFrame(id, f.OwnerSeat, 0, p.Kind, p.Targets, PhysicalCardIds: []) { Action = action, UnlimitedUse = true, AnniversaryLiuYongSlash = origin });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongNativeIssuedEvent(f.Id, id, action.ActionId, p.Kind));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, p.Kind, f.OwnerSeat)); AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, p.Targets));
        RecordYingboCardUse(id, f.OwnerSeat, p.Kind); RecordProgramUsedBasicCard(f.OwnerSeat, p.Kind); RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, id, f.OwnerSeat, target.Seat, card: null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: p.Kind,
            ignoresArmor: HasCardArmorBypass(actor, target, p.Kind), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack; MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, target.Seat)); TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, p.Targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private bool IsAnniversaryLiuYongVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: AnniversaryLiuYongComposition.Zhuning } origin || _resolutionStack.SingleOrDefault(x => x.Id == origin.ParentProgramFrameId) is not ProgramSkillFrame { AnniversaryLiuYong: { GiftPaid: true, Plan: { } p } r } f ||
            r.Stage is not (AnniversaryLiuYongStage.IssuingUse or AnniversaryLiuYongStage.NativeUse) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.CardKind != p.Kind || !IsOrdinaryTrick(p.Kind) ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } a || a.ProviderSeat != f.OwnerSeat ||
            a.ActionId != origin.CardActionId || a.EffectiveKind != p.Kind || a.EffectiveSuit is not (null or Suit.None) || a.EffectiveRank is not (null or 0) || a.EffectiveIsRed == true || !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
            origin.OwnerSeat != f.OwnerSeat || origin.InitialActorSeat != f.OwnerSeat || origin.InitialTargetSeat != f.OwnerSeat || origin.BindingId != f.ActivationId || origin.SkillInstanceId != f.SkillInstanceId || origin.GameplayHash != f.GameplayHash || origin.InstructionIndex != 1 || origin.LifecycleFrameId != 0 || origin.TurnNumber != r.Turn ||
            r.UseId != 0 && (r.UseId != use.Id || r.ActionId != a.ActionId)) return false;
        RequireAnniversaryLiuYong(f);
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, f.OwnerSeat, f.OwnerSeat) : a.ActorSeat == f.OwnerSeat;
    }
    private bool IsAnniversaryLiuYongSlashUse(CardUseFrame use) => use.AnniversaryLiuYongSlash is { } o && use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && use.UnlimitedUse && use.CardKind == o.Kind && IsSlashCard(o.Kind) &&
        use.Action is { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a && a.ActionId == o.ActionId && a.EffectiveKind == o.Kind && a.ProviderSeat == o.Source.OwnerSeat &&
        _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is ProgramSkillFrame { AnniversaryLiuYong: { Stage: AnniversaryLiuYongStage.NativeUse, GiftPaid: true } r } f && r.Source == o.Source && r.Hash == o.Hash && r.UseId == use.Id && r.ActionId == a.ActionId &&
        r.Plan?.Kind == o.Kind && r.Plan.Targets.SequenceEqual([o.TargetSeat]) && ProgramEventHistory<AnniversaryLiuYongNativeIssuedEvent>().Count(e => e == new AnniversaryLiuYongNativeIssuedEvent(f.Id, use.Id, a.ActionId, o.Kind)) == 1 && ShownEntityUseActorMatches(use, f.OwnerSeat, f.OwnerSeat);
    private bool IsAnniversaryLiuYongSlashConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) => LifecycleCardUse(attack.ResolutionId) is { AnniversaryLiuYongSlash: { } origin } use && IsAnniversaryLiuYongSlashUse(use) &&
        processing.Count == 0 && attack.Card is null && attack.PhysicalCards.Count == 0 && attack.EffectiveCardKind == origin.Kind && attack.ProgramSkillCardUseFrameId == origin.FrameId && attack.ProgramSkillFrameId is null && !attack.IsSourceLess &&
        attack.CardUserSeat == use.SourceSeat && attack.SourceSeat == use.SourceSeat && (use.TargetSeats.Contains(attack.TargetSeat) || attack.DamageRedirected || attack.IsChainPropagation && ProgramEventHistory<ChainedDamagePropagatedEvent>().Any(e => e.ResolutionId == use.Id && e.TargetSeat == attack.TargetSeat));
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 43400)]
    private bool CompleteAnniversaryLiuYongSlash(CardAttackHandle attack, out bool paused)
    {
        paused = false; if (LifecycleCardUse(attack.ResolutionId) is not { AnniversaryLiuYongSlash: not null } use || !IsAnniversaryLiuYongSlashUse(use)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(43400)] private void ValidateAnniversaryLiuYongSlash(CardUseFrame use)
    { if (use.AnniversaryLiuYongSlash is not null && (!IsAnniversaryLiuYongSlashUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Zhuning must return its exact completed native Slash."); }
    [NativeUseReturn(43400)] private void ReturnAnniversaryLiuYongSlash(CardUseFrame use)
    { if (use.AnniversaryLiuYongSlash is { } origin) ReturnAnniversaryLiuYongNative(use, origin.FrameId); }
    private bool TryReturnAnniversaryLiuYongVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: AnniversaryLiuYongComposition.Zhuning } origin) return false;
        ReturnAnniversaryLiuYongNative(use, origin.ParentProgramFrameId); return true;
    }
    private void ReturnAnniversaryLiuYongNative(CardUseFrame use, long parentId)
    {
        var f = GetActiveProgramFrame(parentId); var r = f.AnniversaryLiuYong!;
        if (r.Stage != AnniversaryLiuYongStage.NativeUse || r.UseId != use.Id || r.ActionId != use.Action?.ActionId || use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardKind == r.Plan!.Kind) != 1 || ProgramEventHistory<AnniversaryLiuYongNativeReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Zhuning cannot return another whole use or return it twice.");
        var attack = use.CardAttack is { Active: true }; var caused = r.CausedDamage || use.CausedDamage;
        ReplaceRuntimeTop(f = f with { AnniversaryLiuYong = r with { Stage = AnniversaryLiuYongStage.NativeReturned, HadAttack = attack, CausedDamage = caused } });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYongNativeReturnedEvent(f.Id, use.Id, r.ActionId, attack, caused));
        if (!attack) CompleteAnniversaryLiuYongUse(f);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 43400)] private bool ReturnAnniversaryLiuYongSlashAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuYong: { Stage: AnniversaryLiuYongStage.NativeReturned } r } f || r.UseId != completion.ResolutionId || completion.ProgramFrameId != f.Id) return false;
        CompleteAnniversaryLiuYongUse(f); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 43400)] private bool ReturnAnniversaryLiuYongVirtualAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuYong: { Stage: AnniversaryLiuYongStage.NativeReturned, HadAttack: true } r } f) return false;
        if (LifecycleCardUse(r.UseId) is not null) throw new InvalidOperationException("Zhuning must wait for the true native attack cleanup.");
        CompleteAnniversaryLiuYongUse(f); return true;
    }
}
