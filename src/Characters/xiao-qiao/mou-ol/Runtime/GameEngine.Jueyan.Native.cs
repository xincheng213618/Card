namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<MouXiaoQiaoOlUsePlan> MouXqNativePlans(CharacterState actor, CardKind kind, CardConversionSource source)
    {
        if (!actor.IsAlive || _winner != Winner.None || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) ||
            !TargetRules.AllowsNativeUseMaterial(new(actor.Seat, kind, [], source)) || IsCuiYanNextUseForbidden(actor.Seat, [], null)) return [];
        var actions = new List<LegalAction>();
        var required = new Dictionary<(LegalActionKind, int?), CardKind?>();
        if (IsOrdinaryTrick(kind))
        {
            foreach (var p in BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None, enforceUsePermission: true,
                beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: [],
                includeNextActualUseAdjustment: false, conversionSource: source).Where(p => p.ActionKind != LegalActionKind.Recast))
            {
                actions.Add(new(p.ActionKind, 0, p.TargetSeats.Count == 1 ? p.TargetSeats[0] : null, p.Description, kind, p.TargetCardId, p.TargetSeats) { ConversionSource = source });
                required[(p.ActionKind, p.TargetCardId)] = p.RequiredCardKind;
            }
        }
        else if (IsSlashCard(kind))
        {
            var card = new Card(0, kind, Suit.None, 0);
            var targets = _players.Where(t => CanUseVirtualSlashTarget(actor, t, kind, physicalSuit: Suit.None, effectiveColor: null,
                effectiveRank: 0, physicalCardIds: [], conversionSource: source)).ToArray();
            actions.AddRange(targets.Select(t => new LegalAction(LegalActionKind.Slash, 0, t.Seat, $"绝颜：对{t.Name}使用【{card.DisplayName}】", kind) { ConversionSource = source }));
            AddProgramTargetCountSlashActions(actions, actor, card, targets, card.DisplayName, kind, source, null);
        }
        else if (kind is CardKind.Peach or CardKind.Alcohol)
        {
            var legal = !HasSelfCardTargetProhibition(actor.Seat) && !IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, kind) &&
                !IsCardTargetProhibited(actor, kind, Suit.None) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None);
            if (legal && kind == CardKind.Peach && IsRuleWounded(actor)) actions.Add(new(LegalActionKind.Peach, 0, actor.Seat, "绝颜：视为对自己使用【桃】", kind) { ConversionSource = source });
            if (legal && kind == CardKind.Alcohol && !actor.HasAlcoholEffect && (CanUseJianjiOrdinaryAlcohol(actor, null) ||
                HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) || HasNextUnlimitedCard(actor) || HasMouWujingAllowance(actor) ||
                HasFriendQihuiAllowance(actor) || HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, kind)))
                actions.Add(new(LegalActionKind.Alcohol, 0, actor.Seat, "绝颜：视为对自己使用【酒】", kind) { ConversionSource = source });
        }
        else if (kind is CardKind.Zong or CardKind.RealgarWine)
            AddDragonBoatPhysicalOrConvertedActions(actions, actor, new(0, kind, Suit.None, 0), kind, source);
        else if (kind == CardKind.Intoxication)
            AddGuanduPhysicalOrConvertedActions(actions, actor, new(0, kind, Suit.None, 0), kind, source);
        AddNextCardTargetAdjustmentActions(actions, actor); AddNextActualUseAdjustmentActions(actions, actor);
        return Array.AsReadOnly(FilterTurnCardUseRestrictions(actor, actions).Select(a => new MouXiaoQiaoOlUsePlan(kind, a.Kind, a.TargetCardId,
            required.GetValueOrDefault((a.Kind, a.TargetCardId)), a.Description, IsTargetAdjustmentAction(actor, a)) { Targets = a.TargetSeats }).ToArray());
    }
    private CardActionContext PrepareMouXqJueyanAction(CardActionContext action)
    { var prepared = MouXiaoQiaoOl.PrepareNativeAction(action); return ReferenceEquals(prepared, action) ? action : CaptureFactionAction(prepared); }
    private bool IsMouXqJueyanUse(long id) => LifecycleCardUse(id) is { } use && MouXiaoQiaoOl.IsIssuedUse(use);
    private Card MouXqJueyanRepresentation(long id) => IsMouXqJueyanUse(id) ? new(0, LifecycleCardUse(id)!.CardKind, Suit.None, 0) :
        throw new InvalidOperationException("Jueyan's zero-material native use lost its actual origin.");
    private bool SkipMouXqJueyanFinishedMovement(long id, Card c) => c.Id == 0 && IsMouXqJueyanUse(id) && c.Kind == LifecycleCardUse(id)!.CardKind;
    private Card ReadMouXqJueyanAppearance(long id, CardAppearanceReference a)
    {
        if (!IsMouXqJueyanUse(id)) return ReadDiouUseAppearance(id, a);
        var c = MouXqJueyanRepresentation(id);
        if (a.Id != 0 || a.Kind != c.Kind || a.Suit != Suit.None || a.Rank != 0) throw new InvalidOperationException("Jueyan cannot inherit appearance from its retained original entity.");
        return c;
    }
    // The module freezes admission and ordered targets. This bridge executes the
    // existing native card primitives; it creates no synthetic accepted facts.
    private void IssueMouXqNativeUse(ProgramSkillFrame f, MouXiaoQiaoOlUsePlan p)
    {
        var actor = _players[f.OwnerSeat]; var source = f.MouXiaoQiaoOl!.Source; var card = new Card(0, p.Kind, Suit.None, 0);
        if (p.Adjusted) _selectedNextCardTargetSeats = p.Targets;
        if (p.Kind is CardKind.Zong or CardKind.RealgarWine)
        { ResolveDragonBoatCard(actor, card, new(p.ActionKind, 0, p.Targets.FirstOrDefault(), p.Description, p.Kind, TargetSeats: p.Targets) { ConversionSource = source }); return; }
        if (p.Kind == CardKind.Intoxication)
        { ResolveGuanduCard(actor, card, new(p.ActionKind, 0, p.Targets.FirstOrDefault(), p.Description, p.Kind, TargetSeats: p.Targets) { ConversionSource = source }); return; }
        if (p.Kind == CardKind.Peach)
        { ResolveRecoveryCard(actor, actor, card, "桃", p.Kind, conversionSource: source, physicalCards: []); return; }
        var id = BeginCardUse(card, actor.Seat, p.Targets, p.Kind, physicalCardIds: [], conversionSource: source);
        if (LifecycleCardUse(id)?.MouXiaoQiaoOlOrigin is null) throw new InvalidOperationException("Jueyan must bind its own exact material-free issuance.");
        if (p.Kind == CardKind.Alcohol)
        { RecordMouZhaJiangAwareAlcoholDebit(id, actor); BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return; }
        if (IsSlashCard(p.Kind))
        {
            var targets = LifecycleCardUse(id)!.TargetSeats;
            var attack = new CardAttackHandle(this, id, actor.Seat, targets[0], card: null, damageAmount: 1 + actor.AlcoholDamageBonus,
                playedCardKind: p.Kind, ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], p.Kind), programSkillCardUseFrameId: f.Id);
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
            if (_phase == TurnPhase.Play && actor.Seat == _currentSeat && LifecycleCardUse(id)?.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, source)) RecordSlashUseDebit(id, actor.Seat);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind); CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, targets[0], IgnoresArmor: attack.IgnoresArmor)); TryMarkProgramUseCommitted(id);
            if (!TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted, targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
            return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, LifecycleCardUse(id)!.TargetSeats, p.ActionKind, p.TargetCardId, p.RequiredKind, p.Kind);
    }
    private bool ContinueMouXqJueyanSimpleUse(long id, ProgramSimpleCardContinuation c)
    {
        if (c.CardId != 0 || !IsMouXqJueyanUse(id)) return false;
        var use = LifecycleCardUse(id)!; var actor = _players[use.SourceSeat]; var card = MouXqJueyanRepresentation(id);
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (!actor.IsAlive || _winner != Winner.None) { FinishCardUse(id, card, use.CardKind); return true; }
        if (c.Effect == SimpleCardUseEffect.Recovery && use.CardKind is CardKind.Peach or CardKind.Zong)
            CompleteRecoveryCardUse(actor, _players[use.TargetSeats[use.TargetIndex]], card, id, use.CardKind, c.RecoveryAmount, c.RecoveryPolicySources ?? []);
        else if (c.Effect == SimpleCardUseEffect.Alcohol && use.CardKind is CardKind.Alcohol or CardKind.RealgarWine) CompleteAlcoholUse(actor, card, id);
        else throw new InvalidOperationException("Jueyan changed its exact accepted native simple effect.");
        return true;
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 34000)]
    private bool FinishMouXqJueyanAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsMouXqJueyanUse(attack.ResolutionId) || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) && kind is not (CardKind.Duel or CardKind.FireAttack)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!; SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats,
            ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    private bool IsMouXqJueyanAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (!IsMouXqJueyanUse(attack.ResolutionId) || attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            LifecycleCardUse(attack.ResolutionId) is not { } use || attack.SourceSeat != use.SourceSeat ||
            attack.EffectiveCardKind != use.CardKind || attack.ProgramSkillCardUseFrameId is { } parent && parent != (use.MouXiaoQiaoOlOrigin?.ProgramFrameId ?? use.MouXiaoQiaoJueyanRescueOrigin?.Request.ParentFrameId)) return false;
        var ancestors=MouXqNativeAncestorProcessing(use.Id);
        return ancestors is not null&&processing.Select(c=>c.Id).Order().SequenceEqual(ancestors.Order());
    }
    private IReadOnlyList<int>? MouXqNativeAncestorProcessing(long id)
    {
        if(!IsMouXqJueyanUse(id))return null;
        var index=_resolutionStack.FindIndex(f=>f.Id==id);if(index<0)return null;
        return Array.AsReadOnly(_resolutionStack.Take(index).OfType<CardUseFrame>().SelectMany(use=>use.Action?.PhysicalCards.Select(m=>m.CardId)??use.PhysicalCardIds??[])
            .Where(card=>card>0&&_cardZones.GetLocation(card)==CardLocation.Processing).Distinct().ToArray());
    }
    private void SignMouXqNativeBatches(ProgramSkillFrame f,CardUseFrame use)
    {
        foreach(var b in _pendingCardsMovedBatches.Where(b=>b.ParentFrameId==use.Id&&b.AwaitingProgramFrameId is null&&
            b.OriginSkillId==f.SkillId&&b.OriginSkillInstanceId==f.SkillInstanceId&&b.OriginOwnerSeat==f.OwnerSeat))
        {
            if(b.Movements.Count==0||b.Movements.Any(m=>!_cardMovements.Contains(m)||m.Sequence<=f.MouXiaoQiaoOl!.Before||m.Sequence>_movementSequence)||
                b.NativeDrawInvocation is{}draw&&!ValidNativeDrawInvocation(draw))throw new InvalidOperationException("Jueyan's native Use lost its actual movement and draw invoices.");
            AdvanceEventRulesAndQueueFact(new MouXiaoQiaoJueyanBatchSignedEvent(f.Id,use.Id,b.Id,b.ParentBatchId,b.Movements.Count));
            foreach(var m in b.Movements)AdvanceEventRulesAndQueueFact(new MouXiaoQiaoJueyanMovementSignedEvent(f.Id,use.Id,b.Id,m.CardId,m.CardKind,m.From,m.To,m.Reason,m.Sequence));
        }
    }
    private bool IsMouXqNativeSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext b)=>
        f.MouXiaoQiaoOl is{Stage:MouXiaoQiaoOlStage.NativeChildren,NativeReturned:true}r&&r.Source.SkillId==MouXiaoQiaoOlRuntime.Jueyan&&
        b.ParentFrameId==r.UseId&&b.AwaitingProgramFrameId is null&&b.Movements.Count>0&&b.OriginSkillId==f.SkillId&&
        b.OriginSkillInstanceId==f.SkillInstanceId&&b.OriginOwnerSeat==f.OwnerSeat&&
        ProgramEventHistory<MouXiaoQiaoJueyanBatchSignedEvent>().Count(e=>e==new MouXiaoQiaoJueyanBatchSignedEvent(f.Id,r.UseId,b.Id,b.ParentBatchId,b.Movements.Count))==1&&
        b.Movements.All(m=>_cardMovements.Contains(m)&&ProgramEventHistory<MouXiaoQiaoJueyanMovementSignedEvent>().Count(e=>
            e==new MouXiaoQiaoJueyanMovementSignedEvent(f.Id,r.UseId,b.Id,m.CardId,m.CardKind,m.From,m.To,m.Reason,m.Sequence))==1);

}
