namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void EnsureLiuLiDamagePool(int owner)
    {
        if (ProgramEventHistory<AnniversaryLiuLiPoolNameEvent>().Any(e => e.OwnerSeat == owner)) return;
        var pool = new[] { CardKind.Slash, CardKind.Duel, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FireAttack }
            .Concat(_cardZones.CreateDiagnostics().Select(c => c.CardKind).Where(k => k is CardKind.WumiaoFlood or CardKind.QizhengXiangsheng)).Distinct().ToArray();
        foreach (var name in pool) AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiPoolNameEvent(owner, name));
    }
    private bool LiuLiNameChosen(int owner, CardKind kind) => ProgramEventHistory<AnniversaryLiuLiNameChosenEvent>().Any(e => e.Source.OwnerSeat == owner && e.Name == ProgramBasicCardName(kind) && LiuLiChosenProof(e));
    private IReadOnlyList<AnniversaryLiuLiPlan> LiuLiPlans(int owner)
    {
        var actor = _players[owner]; if (!actor.IsAlive || _winner != Winner.None) return [];
        var pool = ProgramEventHistory<AnniversaryLiuLiPoolNameEvent>().Where(e => e.OwnerSeat == owner).Select(e => e.Name).ToHashSet();
        var result = new List<AnniversaryLiuLiPlan>();
        if (pool.Contains(CardKind.Slash) && !LiuLiNameChosen(owner, CardKind.Slash))
            foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash })
                foreach (var target in CurrentTableOrder().Select(seat => _players[seat]).Where(p => CanUseVirtualSlashTarget(actor, p, kind, physicalSuit: Suit.None, effectiveRank: 0, ignoreDistance: true, countsTowardLimit: false) &&
                    !IsDirectedCardTargetProhibited(owner, p.Seat, kind) && !IsCardTargetProhibited(p, kind, Suit.None, null))) result.Add(new(kind, LegalActionKind.Slash) { Targets = [target.Seat] });
        var ordinary = BuildProgramOrdinaryTrickUseOptions(actor, physicalSuit: Suit.None, enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []);
        result.AddRange(NextActualUseOrdinaryTrickOptions(actor, ordinary, Suit.None, null, Suit.None, false).Where(o => pool.Contains(o.EffectiveCardKind) && !LiuLiNameChosen(owner, o.EffectiveCardKind))
            .Select(o => new AnniversaryLiuLiPlan(o.EffectiveCardKind, o.ActionKind, o.TargetCardId, o.RequiredCardKind, o.NextActualUseAdjusted) { Targets = GetImplicitSelfCardUseTargets(o.EffectiveCardKind, owner, o.TargetSeats) }));
        return Array.AsReadOnly(result.ToArray());
    }
    private void IssueLiuLiUse(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuLi!; var p = r.Plan!;
        if (!ValidLiuLi(f) || r.Stage != AnniversaryLiuLiStage.IssuingUse || !LiuLiPlans(f.OwnerSeat).Any(plan => LiuLiPlansEqual(plan, p))) throw new InvalidOperationException("Dehua lost its currently legal unused damage-name native plan.");
        if (IsSlashCard(p.Kind)) { IssueLiuLiSlash(f); return; }
        if (p.Adjusted) _selectedNextCardTargetSeats = p.Targets;
        var origin = new ProgramVirtualOrdinaryTrickOrigin(f.Id, f.InstructionIndex, 0, f.OwnerSeat, f.OwnerSeat, f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, r.Turn, p.Kind, SkillProgramConditionKind.Always, null, null);
        var card = new Card(0, p.Kind, Suit.None, 0); var id = BeginCardUse(card, f.OwnerSeat, p.Targets, p.Kind, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin);
        var use = LifecycleCardUse(id)!; f = _resolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f = f with { AnniversaryLiuLi = f.AnniversaryLiuLi! with { Stage = AnniversaryLiuLiStage.NativeUse, UseId = id, ActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiNativeIssuedEvent(f.Id, id, use.Action!.ActionId, p.Kind));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, use.TargetSeats, p.ActionKind, p.TargetCardId, p.RequiredKind, p.Kind);
    }
    private static bool LiuLiPlansEqual(AnniversaryLiuLiPlan a, AnniversaryLiuLiPlan b) => (a.Kind, a.ActionKind, a.TargetCardId, a.RequiredKind, a.Adjusted) == (b.Kind, b.ActionKind, b.TargetCardId, b.RequiredKind, b.Adjusted) && a.Targets.SequenceEqual(b.Targets);
    private void IssueLiuLiSlash(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuLi!; var p = r.Plan!; var actor = _players[f.OwnerSeat]; var target = _players[p.Targets.Single()]; var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, null, CardActionType.Use, actor.Seat, actor.Seat, null, null, null, p.Kind, p.Targets, [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.NativeUse, UseId = id, ActionId = action.ActionId }, VirtualBasicDraft = new(f.InstructionIndex, id) });
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, p.Kind, p.Targets, PhysicalCardIds: []) { Action = action, VirtualBasicReturn = new(f.Id, f.InstructionIndex), UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiNativeIssuedEvent(f.Id, id, action.ActionId, p.Kind));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, p.Kind, actor.Seat)); AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, p.Targets));
        RecordYingboCardUse(id, actor.Seat, p.Kind); RecordProgramUsedBasicCard(actor.Seat, p.Kind); RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, null, damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: p.Kind, ignoresArmor: HasCardArmorBypass(actor, target, p.Kind));
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack; MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, target.Seat)); TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, p.Targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }
    private bool IsExactAnniversaryLiuLiBasicDraft(ProgramSkillFrame f, ProgramVirtualBasicDraft draft) => f.AnniversaryLiuLi is { Stage: AnniversaryLiuLiStage.NativeUse, Plan: { } p } r &&
        f.SkillId == AnniversaryLiuLiComposition.Dehua && IsSlashCard(p.Kind) && draft.InstructionIndex == f.InstructionIndex && draft.ChildFrameId == r.UseId && ValidLiuLi(f) &&
        ProgramEventHistory<AnniversaryLiuLiNativeIssuedEvent>().Count(e => e == new AnniversaryLiuLiNativeIssuedEvent(f.Id, r.UseId, r.ActionId, p.Kind)) == 1;
    private bool IsAnniversaryLiuLiVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: AnniversaryLiuLiComposition.Dehua } o || _resolutionStack.SingleOrDefault(x => x.Id == o.ParentProgramFrameId) is not ProgramSkillFrame { AnniversaryLiuLi: { Plan: { } p } r } f ||
            !ValidLiuLi(f) || r.Stage is not (AnniversaryLiuLiStage.IssuingUse or AnniversaryLiuLiStage.NativeUse) || !IsOrdinaryTrick(p.Kind) || use.CardKind != p.Kind || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } a || a.ActionId != o.CardActionId || a.ProviderSeat != f.OwnerSeat || a.EffectiveKind != p.Kind || a.EffectiveSuit is not (null or Suit.None) || a.EffectiveRank is not (null or 0) || a.EffectiveIsRed == true || !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
            o.OwnerSeat != f.OwnerSeat || o.InitialActorSeat != f.OwnerSeat || o.InitialTargetSeat != f.OwnerSeat || o.BindingId != f.ActivationId || o.SkillInstanceId != f.SkillInstanceId || o.GameplayHash != f.GameplayHash || o.InstructionIndex != f.InstructionIndex || o.LifecycleFrameId != 0 || o.TurnNumber != r.Turn ||
            r.UseId != 0 && (r.UseId != use.Id || r.ActionId != a.ActionId)) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, f.OwnerSeat, f.OwnerSeat) : a.ActorSeat == f.OwnerSeat;
    }
    private bool TryReturnAnniversaryLiuLiVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: AnniversaryLiuLiComposition.Dehua } o) return false;
        var f = GetActiveProgramFrame(o.ParentProgramFrameId); var r = f.AnniversaryLiuLi!;
        if (use.Step != ResolutionFrameStep.Completed || r.UseId != use.Id || r.ActionId != use.Action?.ActionId) throw new InvalidOperationException("Dehua cannot return another unfinished native ordinary trick.");
        CompleteLiuLiNative(f, use.CardAttack is { Active: true }); return true;
    }
    private void CompleteLiuLiNative(ProgramSkillFrame f, bool? hadAttack = null)
    {
        var r = f.AnniversaryLiuLi!;
        if (!ValidLiuLi(f) || r.Stage != AnniversaryLiuLiStage.NativeUse || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardId == 0 && e.CardKind == r.Plan!.Kind) != 1)
            throw new InvalidOperationException("Dehua must finish its same whole zero-entity native Use exactly once.");
        var attack = hadAttack ?? ActiveCardAttack is { } a && a.ResolutionId == r.UseId;
        ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.ReturnedUse, HadAttack = attack } });
        if (!attack) SettleLiuLiName(f);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 43300)] private bool ReturnLiuLiAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuLi: { Stage: AnniversaryLiuLiStage.ReturnedUse, HadAttack: true } } f) return false;
        SettleLiuLiName(f); return true;
    }
    private void SettleLiuLiName(ProgramSkillFrame f)
    {
        var r = f.AnniversaryLiuLi!; var name = ProgramBasicCardName(r.Plan!.Kind);
        if (!ValidLiuLi(f) || r.Stage != AnniversaryLiuLiStage.ReturnedUse || ProgramEventHistory<AnniversaryLiuLiNameChosenEvent>().Any(e => e.FrameId == f.Id) || LiuLiNameChosen(f.OwnerSeat, name)) throw new InvalidOperationException("Dehua cannot settle a name twice.");
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiNameChosenEvent(f.Id, r.Source, r.Hash, name, r.UseId, r.ActionId));
        if (ProgramEventHistory<AnniversaryLiuLiPoolNameEvent>().Where(e => e.OwnerSeat == f.OwnerSeat).All(e => LiuLiNameChosen(f.OwnerSeat, e.Name)))
        {
            ReplaceRuntimeTop(f = f with { AnniversaryLiuLi = r with { Stage = AnniversaryLiuLiStage.SkillChildren } });
            AdvanceEventRulesAndQueueFact(new AnniversaryLiuLiDehuaCompletedEvent(f.Id, f.OwnerSeat, ProgramEventHistory<AnniversaryLiuLiPoolNameEvent>().Count(e => e.OwnerSeat == f.OwnerSeat)));
            foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
            foreach (var grant in _players[f.OwnerSeat].SkillGrants.Grants.Where(g => g.SkillId == AnniversaryLiuLiComposition.Dehua).ToArray()) _players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
            AdvanceEventRulesAndQueueFact(new CharacterSkillsLostEvent(f.OwnerSeat, f.OwnerSeat, Array.AsReadOnly(new[] { AnniversaryLiuLiComposition.Dehua })));
            ContinueLiuLi(f.Id); return;
        }
        FinishLiuLi(f);
    }
}
