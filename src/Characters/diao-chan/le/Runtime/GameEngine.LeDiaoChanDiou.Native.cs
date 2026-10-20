namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<DiouUsePlan> DiouNativePlans(CharacterState actor, CardKind kind, CardConversionSource source)
    {
        if (!actor.IsAlive || _winner != Winner.None || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) ||
            !TargetRules.AllowsNativeUseMaterial(new(actor.Seat, kind, [], source)) || IsCuiYanNextUseForbidden(actor.Seat, [], null)) return [];
        var result = new List<DiouUsePlan>();
        void Add(LegalActionKind action, IReadOnlyList<int> targets, string text, int? card = null,
            CardKind? required = null, bool adjusted = false) =>
            result.Add(new(kind, action, card, required, text, adjusted) { Targets = targets });
        if (IsOrdinaryTrick(kind))
        {
            var options = BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None, enforceUsePermission: true,
                beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true,
                physicalCardIds: [], includeNextActualUseAdjustment: false, conversionSource: source)
                .Where(p => p.ActionKind != LegalActionKind.Recast).ToArray();
            var ordinary = options.Select(p => new LegalAction(p.ActionKind, 0,
                p.TargetSeats.Count == 1 ? p.TargetSeats[0] : null, p.Description, kind, p.TargetCardId, p.TargetSeats)
                { ConversionSource = source }).ToList();
            // Use the same genuine zero-material adjustment producers as
            // native actions. Both existing grant types must publish their
            // legal ordered targets before the choice is accepted and paid.
            AddNextCardTargetAdjustmentActions(ordinary, actor);
            AddNextActualUseAdjustmentActions(ordinary, actor);
            foreach (var a in ordinary)
                Add(a.Kind, a.TargetSeats, a.Description, a.TargetCardId, options.FirstOrDefault()?.RequiredCardKind,
                    IsTargetAdjustmentAction(actor, a));
            return Array.AsReadOnly(result.ToArray());
        }
        var neutral = new Card(0, kind, Suit.None, 0);
        if (IsSlashCard(kind))
        {
            var actions = new List<LegalAction>();
            var targets = _players.Where(t => CanUseVirtualSlashTarget(actor, t, kind, physicalCardIds: [],
                conversionSource: source)).ToArray();
            actions.AddRange(targets.Select(t => new LegalAction(LegalActionKind.Slash, 0, t.Seat,
                $"视为对{t.Name}使用【{neutral.DisplayName}】", kind) { ConversionSource = source }));
            AddProgramTargetCountSlashActions(actions, actor, neutral, targets, neutral.DisplayName, kind, source, null);
            AddNextCardTargetAdjustmentActions(actions, actor); AddNextActualUseAdjustmentActions(actions, actor);
            foreach (var a in actions) Add(a.Kind, a.TargetSeats, a.Description, adjusted: a.ProgramActivationId is not null);
        }
        else if (kind is CardKind.Peach or CardKind.Alcohol)
        {
            var selfLegal = !HasSelfCardTargetProhibition(actor.Seat) &&
                !IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, kind) && !IsCardTargetProhibited(actor, kind, Suit.None) &&
                !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None);
            if (selfLegal && kind == CardKind.Peach && IsRuleWounded(actor)) Add(LegalActionKind.Peach, [actor.Seat], "视为对自己使用【桃】");
            if (selfLegal && kind == CardKind.Alcohol && !actor.HasAlcoholEffect &&
                (CanUseJianjiOrdinaryAlcohol(actor, null) || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) ||
                 HasNextUnlimitedCard(actor) || HasMouWujingAllowance(actor) || HasFriendQihuiAllowance(actor) ||
                 HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, kind)))
                Add(LegalActionKind.Alcohol, [actor.Seat], "视为对自己使用【酒】");
        }
        else if (kind is CardKind.Zong or CardKind.RealgarWine)
        {
            var actions = new List<LegalAction>(); AddDragonBoatPhysicalOrConvertedActions(actions, actor, neutral, kind, source);
            foreach (var a in actions) Add(a.Kind, a.TargetSeats.Count > 0 ? a.TargetSeats : [actor.Seat], a.Description);
        }
        else if (kind == CardKind.Intoxication)
        {
            var actions = new List<LegalAction>(); AddGuanduPhysicalOrConvertedActions(actions, actor, neutral, kind, source);
            foreach (var a in actions) Add(a.Kind, a.TargetSeats, a.Description);
        }
        else if (EquipmentCatalog.IsEquipment(kind) && kind != CardKind.GeneralWeapon)
        {
            if (actor.EquipmentSlotCapacity(EquipmentCatalog.Get(kind).Slot) > 0 && !HasSelfCardTargetProhibition(actor.Seat) &&
                !IsCardTargetProhibited(actor, kind, Suit.None) && !IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, kind) &&
                !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None))
                Add(LegalActionKind.Equip, [actor.Seat], $"视为使用并装备【{neutral.DisplayName}】");
        }
        else if (IsDelayedCard(kind))
        {
            foreach (var t in _players.Where(t => t.IsAlive && !t.JudgmentAreaAbolished &&
                !HasJudgmentEffectiveCard(t, kind) && !HasCardPolicy(t, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) &&
                (kind == CardKind.Lightning ? t.Seat == actor.Seat : t.Seat != actor.Seat) &&
                (kind != CardKind.SupplyShortage || HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, kind) ||
                    HasCardDistanceExemption(actor, t, kind) || GetCombatDistance(actor.Seat, t.Seat) <= GetCardUseDistanceLimit(actor, kind)) &&
                !IsDirectedCardTargetProhibited(actor.Seat, t.Seat, kind) && !IsCardTargetProhibited(t, kind, Suit.None) &&
                !IsShenwuRobeTargetProhibited(actor.Seat, t.Seat, kind) && !HasBeneficiarySuitShield(actor.Seat, t.Seat, Suit.None)))
                if (ApplyTurnCardGroupTargetRestrictions(actor, [t.Seat]).Contains(t.Seat))
                    Add(DelayedPlacementActionKind(kind), [t.Seat], $"视为对{t.Name}使用【{neutral.DisplayName}】");
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void DispatchDiou(ProgramSkillFrame f, DiouUsePlan p)
    {
        var r = f.Diou!; var actor = _players[f.OwnerSeat];
        if (!r.Paid || r.Stage != DiouStage.UseChoice || p.Kind != r.OutputKind ||
            !DiouNativePlans(actor, r.OutputKind, DiouSource(f)).Any(o => SameDiouPlan(o, p)))
            throw new InvalidOperationException("Diou cannot issue another card name or bypass current native legality.");
        ReplaceRuntimeTop(f = f with { Diou = r with { Stage = DiouStage.NativeIssuing, Plan = p } });
        r = f.Diou!;
        AdvanceEventRulesAndQueueFact(new DiouPlanAcceptedEvent(f.Id, p.Kind, p.ActionKind,
            p.TargetCardId, p.RequiredKind, p.Adjusted, p.Targets.Count));
        for (var i = 0; i < p.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new DiouTargetAcceptedEvent(f.Id, i, p.Targets[i]));
        Card card;
        if (EquipmentCatalog.IsEquipment(p.Kind) || IsDelayedCard(p.Kind))
        {
            var id = checked(CreateCardZoneDiagnostics().Select(c => c.CardId).DefaultIfEmpty(0).Max() + 1);
            card = new(id, p.Kind, Suit.None, 0);
            _cardZones.AddGeneratedCard(card); _generatedPhysicalCardIds.Add(id);
            ReplaceRuntimeTop(f = f with { Diou = r with { CarrierId = id } });
            AdvanceEventRulesAndQueueFact(new DiouCarrierCreatedEvent(f.Id, DiouSource(f), f.GameplayHash, id, p.Kind));
        }
        else card = new(0, p.Kind, Suit.None, 0);
        if (p.Adjusted) _selectedNextCardTargetSeats = p.Targets;
        var source = DiouSource(f);
        if (p.Kind is CardKind.Zong or CardKind.RealgarWine)
        {
            ResolveDragonBoatCard(actor, card, new(p.ActionKind, 0, p.Targets.FirstOrDefault(), p.Description,
                p.Kind, TargetSeats: p.Targets) { ConversionSource = source }); return;
        }
        if (p.Kind == CardKind.Intoxication)
        {
            ResolveGuanduCard(actor, card, new(p.ActionKind, 0, p.Targets.FirstOrDefault(), p.Description,
                p.Kind, TargetSeats: p.Targets) { ConversionSource = source }); return;
        }
        if (p.Kind == CardKind.Peach)
        { ResolveRecoveryCard(actor, actor, card, "桃", p.Kind, conversionSource: source, physicalCards: []); return; }
        var useId = BeginCardUse(card, actor.Seat, p.Targets, p.Kind, physicalCardIds: [], conversionSource: source);
        if (LifecycleCardUse(useId)?.DiouOrigin is null)
            throw new InvalidOperationException("Diou must bind its distinct zero-material issuance before native effects.");
        if (card.Id > 0)
            MoveCard(card, CardLocation.OutsideGame, CardLocation.Processing, new("skill-program.le-diao-chan.diou.carrier"));
        if (EquipmentCatalog.IsEquipment(p.Kind))
        { if (!TryBeginEquipmentTargetPrograms(actor, card, useId)) CompleteEquipmentUse(actor, card, useId); return; }
        if (p.Kind == CardKind.Alcohol)
        { RecordMouZhaJiangAwareAlcoholDebit(useId, actor); BeginSimpleCardUse(useId, new(0, SimpleCardUseEffect.Alcohol)); return; }
        if (IsSlashCard(p.Kind))
        {
            var targets = LifecycleCardUse(useId)!.TargetSeats;
            var attack = new CardAttackHandle(this, useId, actor.Seat, targets[0], card: null,
                damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: p.Kind,
                ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], p.Kind), programSkillCardUseFrameId: f.Id);
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
            if (_phase == TurnPhase.Play && actor.Seat == _currentSeat && LifecycleCardUse(useId)?.UnlimitedUse != true &&
                !IgnoresProgramSlashLimit(actor, source)) RecordSlashUseDebit(useId, actor.Seat);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, p.Kind); CaptureProgramAlcoholConsumption(useId, actor); actor.HasAlcoholEffect = false;
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, p.Kind, actor.Seat, targets[0], IgnoresArmor: attack.IgnoresArmor));
            TryMarkProgramUseCommitted(useId);
            if (!TryBeginProgramCardWindow(attack, LifecycleCardUse(useId)!.Action!, SkillProgramTriggerWindow.CardUseCommitted,
                targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
            return;
        }
        BeginJizhiOrNullificationWindow(useId, card, actor.Seat, LifecycleCardUse(useId)!.TargetSeats,
            p.ActionKind, p.TargetCardId, p.RequiredKind, p.Kind);
    }
    private static bool SameDiouPlan(DiouUsePlan a, DiouUsePlan b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_diao_chan_le_LeDiaoChanDiou_Native.SameDiouPlan(a,b);
    private CardActionContext PrepareDiouAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Diou: { Stage: DiouStage.NativeIssuing } r } f)
            return action;
        if (!r.Paid || r.Plan is not { } plan || !DiouRootMatches(f) || action.Type != CardActionType.Use ||
            action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.RequesterSeat is not null ||
            action.PhysicalCards.Count != 0 || action.EffectiveKind != plan.Kind ||
            !action.ConversionChain.SequenceEqual([DiouSource(f)]))
            throw new InvalidOperationException("Diou cannot normalize another producer's action or replace any genuine material appearance.");
        // Generic CaptureCardUseAction has no rank witness for an empty
        // material set and may leave suit/rank unspecified. Like the other
        // actual zero-material producers, Diou explicitly signs its neutral
        // appearance before policies, accepted facts and native issuance.
        return CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type,
            action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
            action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain,
            action.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null));
    }
    private void BindDiouNativeUse(CardUseFrame use)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { Diou: { Stage: DiouStage.NativeIssuing } r } f) return;
        if (!DiouRootMatches(f) || !r.Paid || r.Plan is not { } plan || use.DiouOrigin is not null ||
            use.Action is not { Type: CardActionType.Use } a || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat ||
            a.RequesterSeat is not null || a.PhysicalCards.Count != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            a.EffectiveKind != r.OutputKind || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null ||
            !a.ConversionChain.SequenceEqual([DiouSource(f)]) || use.CardId != r.CarrierId ||
            !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
            !a.TargetSeats.SequenceEqual(GetImplicitSelfCardUseTargets(plan.Kind, f.OwnerSeat, plan.Targets)))
            throw new InvalidOperationException("Diou lost its exact separate zero-material native Use and retained-Hand source.");
        var origin = new DiouNativeOrigin(f.Id, DiouSource(f), f.GameplayHash, use.Id, a.ActionId, f.OwnerSeat, r.OutputKind, r.CarrierId);
        UpdateLifecycleCardUse(use.Id, u => u with { DiouOrigin = origin });
        ReplaceRuntimeFrame(f.Id, f with { Diou = r with { Stage = DiouStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        AdvanceEventRulesAndQueueFact(new DiouNativeIssuedEvent(f.Id, use.Id, a.ActionId, f.OwnerSeat, r.OutputKind, r.CarrierId));
    }
    private bool IsIssuedDiouUse(CardUseFrame use)
    {
        if (use.DiouOrigin is not { } o || use.Action is not { Type: CardActionType.Use } a ||
            o.UseId != use.Id || o.ActionId != a.ActionId || o.Kind != use.CardKind || a.EffectiveKind != use.CardKind ||
            use.SourceSeat != a.ActorSeat || use.CardId != o.CarrierId ||
            a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null || a.PhysicalCards.Count != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null ||
            !a.ConversionChain.SequenceEqual([o.Source]) ||
            !MouGuquActorLineageMatches(use.Id, a.ActionId, o.ActorSeat, a.ActorSeat, a.ProviderSeat, a.Type) ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame { Diou: { } r } f ||
            !DiouRootMatches(f) || r.UseId != use.Id || r.ActionId != a.ActionId || r.CarrierId != o.CarrierId ||
            o.Source != DiouSource(f) || o.GameplayHash != f.GameplayHash) return false;
        return ProgramEventHistory<DiouNativeIssuedEvent>().Count(e => e == new DiouNativeIssuedEvent(f.Id, use.Id,
            a.ActionId, o.ActorSeat, o.Kind, o.CarrierId)) == 1 && (o.CarrierId == 0 || IsDiouCarrier(o.CarrierId));
    }
    private bool IsDiouUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedDiouUse(use);
    private bool IsDiouZeroUse(long id) => LifecycleCardUse(id) is { CardId: 0 } use && IsIssuedDiouUse(use);
    private Card DiouRepresentation(long id) => IsDiouZeroUse(id) ? new(0, LifecycleCardUse(id)!.CardKind, Suit.None, 0) :
        throw new InvalidOperationException("Diou lost its zero-material native card representation.");
    private Card ReadDiouUseAppearance(long id, CardAppearanceReference appearance)
    {
        if (!IsDiouZeroUse(id)) return ReadDrawFundedDistinctBasicUseAppearance(id, appearance);
        if (appearance.Id != 0 || appearance.Kind != LifecycleCardUse(id)!.CardKind ||
            appearance.Suit != Suit.None || appearance.Rank != 0)
            throw new InvalidOperationException("Diou's native continuation changed its exact neutral zero-material appearance.");
        return DiouRepresentation(id);
    }
    private bool SkipDiouFinishedMovement(long id, Card c) => c.Id == 0 && IsDiouZeroUse(id) && c.Kind == LifecycleCardUse(id)!.CardKind;
    private bool ContinueDiouSimpleUse(long id, ProgramSimpleCardContinuation continuation)
    {
        if (continuation.CardId != 0 || !IsDiouUse(id)) return false;
        var use = LifecycleCardUse(id)!; var actor = _players[use.SourceSeat]; var card = DiouRepresentation(id);
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (!actor.IsAlive || _winner != Winner.None) { FinishCardUse(id, card, use.CardKind); return true; }
        if (continuation.Effect == SimpleCardUseEffect.Recovery && use.CardKind is CardKind.Peach or CardKind.Zong)
            CompleteRecoveryCardUse(actor, _players[use.TargetSeats[use.TargetIndex]], card, id, use.CardKind,
                continuation.RecoveryAmount, continuation.RecoveryPolicySources ?? []);
        else if (continuation.Effect == SimpleCardUseEffect.Alcohol && use.CardKind is CardKind.Alcohol or CardKind.RealgarWine)
            CompleteAlcoholUse(actor, card, id);
        else throw new InvalidOperationException("Diou's zero-material simple effect changed its accepted native card name.");
        return true;
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 30)]
    private bool FinishDiouAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsDiouUse(attack.ResolutionId) ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) && kind is not (CardKind.Duel or CardKind.FireAttack)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    private bool IsDiouAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { DiouOrigin: { } o } use || !IsIssuedDiouUse(use) ||
            attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            !IsSlashCard(use.CardKind) && use.CardKind is not (CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage) ||
            attack.SourceSeat != use.SourceSeat || attack.EffectiveCardKind != use.CardKind ||
            IsSlashCard(use.CardKind) && attack.ProgramSkillCardUseFrameId != o.ProgramFrameId ||
            attack.ProgramSkillCardUseFrameId is { } issuingProgram && issuingProgram != o.ProgramFrameId) return false;
        var ancestors = DiouNativeAncestorProcessing(use.Id);
        return ancestors is not null && processing.Select(c => c.Id).Distinct().Order().SequenceEqual(ancestors.Order());
    }
    [NativeUseValidation(29042)]
    private void ValidateDiouNativeUse(CardUseFrame use)
    {
        if (use.DiouOrigin is not null && (!IsIssuedDiouUse(use) || use.Step != ResolutionFrameStep.Completed ||
            use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null ||
            use.DiouOrigin.CarrierId > 0 && _cardZones.GetLocation(use.DiouOrigin.CarrierId) == CardLocation.Processing))
            throw new InvalidOperationException("Diou cannot return before its genuine whole native Use and carrier placement or retirement.");
    }
    [NativeUseReturn(29042)]
    private void ReturnDiouNativeUse(CardUseFrame use)
    {
        if (use.DiouOrigin is not { } o) return;
        if (LifecycleCardUse(use.Id) is not null || use.Step != ResolutionFrameStep.Completed ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { Diou: { } r } f || f.Id != o.ProgramFrameId ||
            r.UseId != use.Id || r.ActionId != o.ActionId || r.Returned ||
            ProgramEventHistory<DiouNativeReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Diou can return only once to its original paid native parent.");
        var batches = SignDiouNativeBatches(f, use);
        ReplaceRuntimeTop(f = f with { Diou = r with { Stage = DiouStage.NativeChildren, Returned = true, NativeBatchIds = batches },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new DiouNativeReturnedEvent(f.Id, use.Id, o.ActionId));
    }
    private IReadOnlyList<long> SignDiouNativeBatches(ProgramSkillFrame f, CardUseFrame use)
    {
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null &&
            b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat).ToArray();
        foreach (var b in batches)
        {
            if (b.Movements.Count == 0 || b.Movements.Any(m => !_cardMovements.Contains(m) ||
                m.Sequence <= f.Diou!.ShowSequence || m.Sequence > _movementSequence) ||
                b.NativeDrawInvocation is { } draw && !ValidNativeDrawInvocation(draw))
                throw new InvalidOperationException("Diou's retired native Use lost its exact original movement and draw invoices.");
            AdvanceEventRulesAndQueueFact(new DiouNativeBatchSignedEvent(f.Id, use.Id, b.Id, b.ParentBatchId, b.Movements.Count));
            foreach (var m in b.Movements)
                AdvanceEventRulesAndQueueFact(new DiouNativeMovementSignedEvent(f.Id, use.Id, b.Id, m.CardId,
                    m.CardKind, m.From, m.To, m.Reason, m.Sequence));
        }
        return Array.AsReadOnly(batches.Select(b => b.Id).ToArray());
    }
    private bool IsDiouNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) =>
        f.Diou is { Stage: DiouStage.NativeChildren, Returned: true } r && DiouRootMatches(f) &&
        b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.Movements.Count > 0 &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat &&
        r.NativeBatchIds.Contains(b.Id) && ProgramEventHistory<DiouNativeBatchSignedEvent>().Count(e =>
            e == new DiouNativeBatchSignedEvent(f.Id, r.UseId, b.Id, b.ParentBatchId, b.Movements.Count)) == 1 &&
        b.Movements.All(m => _cardMovements.Contains(m) && ProgramEventHistory<DiouNativeMovementSignedEvent>().Count(e =>
            e == new DiouNativeMovementSignedEvent(f.Id, r.UseId, b.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)) == 1);
}
