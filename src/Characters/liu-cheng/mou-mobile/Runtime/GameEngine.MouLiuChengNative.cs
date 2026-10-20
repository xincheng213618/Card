namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<MouLiuChengUsePlan> MouLiuChengNativePlans(CharacterState actor, CardKind kind, CardConversionSource source)
    {
        if (kind is not (CardKind.Slash or CardKind.Dismantlement) || !actor.IsAlive || _winner != Winner.None || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) ||
            !TargetRules.AllowsNativeUseMaterial(new(actor.Seat, kind, [], source)) || IsCuiYanNextUseForbidden(actor.Seat, [], null) ||
            HasTurnCardTargetRestriction(actor.Seat, SkillProgramCardTargetRestriction.SelfOnly)) return [];
        var result = new List<MouLiuChengUsePlan>();
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
                physicalSuit: Suit.None, effectiveColor: null, effectiveRank: 0, conversionSource: source, countsTowardLimit: false)).ToArray();
            actions.AddRange(targets.Select(t => new LegalAction(LegalActionKind.Slash, 0, t.Seat,
                $"视为对{t.Name}使用【{neutral.DisplayName}】", kind) { ConversionSource = source }));
            AddProgramTargetCountSlashActions(actions, actor, neutral, targets, neutral.DisplayName, kind, source, null);
            AddNextCardTargetAdjustmentActions(actions, actor); AddNextActualUseAdjustmentActions(actions, actor);
            foreach (var a in actions) Add(a.Kind, a.TargetSeats, a.Description, adjusted: a.ProgramActivationId is not null);
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void DispatchMouLiuCheng(ProgramSkillFrame f, MouLiuChengUsePlan p)
    {
        var r = f.MouLiuChengCompletion!; var actor = _players[f.OwnerSeat];
        if (!r.Paid || r.Stage != MouLiuChengStage.UseChoice || p.Kind != r.OutputKind ||
            !MouLiuChengNativePlans(actor, r.OutputKind, MouLiuChengSource(f)).Any(o => SameMouLiuChengPlan(o, p)))
            throw new InvalidOperationException("MouLiuCheng cannot issue another card name or bypass current native legality.");
        ReplaceRuntimeTop(f = f with { MouLiuChengCompletion = r with { Stage = MouLiuChengStage.NativeIssuing, Plan = p } });
        r = f.MouLiuChengCompletion!;
        AdvanceEventRulesAndQueueFact(new MouLiuChengPlanAcceptedEvent(f.Id, p.Kind, p.ActionKind,
            p.TargetCardId, p.RequiredKind, p.Adjusted, p.Targets.Count));
        for (var i = 0; i < p.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new MouLiuChengTargetAcceptedEvent(f.Id, i, p.Targets[i]));
        var card = new Card(0, p.Kind, Suit.None, 0);
        if (p.Adjusted) _selectedNextCardTargetSeats = p.Targets;
        var source = MouLiuChengSource(f);
        var useId = BeginCardUse(card, actor.Seat, p.Targets, p.Kind, physicalCardIds: [], conversionSource: source);
        if (LifecycleCardUse(useId)?.MouLiuChengOrigin is null)
            throw new InvalidOperationException("MouLiuCheng must bind its distinct zero-material issuance before native effects.");
        if (IsSlashCard(p.Kind))
        {
            var targets = LifecycleCardUse(useId)!.TargetSeats;
            var attack = new CardAttackHandle(this, useId, actor.Seat, targets[0], card: null,
                damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: p.Kind,
                ignoresArmor: HasCardArmorBypass(actor, _players[targets[0]], p.Kind), programSkillCardUseFrameId: f.Id);
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
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
    private static bool SameMouLiuChengPlan(MouLiuChengUsePlan a, MouLiuChengUsePlan b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_cheng_mou_mobile_MouLiuChengNative.SameMouLiuChengPlan(a,b);
    private CardActionContext PrepareMouLiuChengAction(CardActionContext action)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLiuChengCompletion: { Stage: MouLiuChengStage.NativeIssuing } r } f)
            return action;
        if (!r.Paid || r.Plan is not { } plan || !MouLiuChengRootMatches(f) || action.Type != CardActionType.Use ||
            action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.RequesterSeat is not null ||
            action.PhysicalCards.Count != 0 || action.EffectiveKind != plan.Kind ||
            !action.ConversionChain.SequenceEqual([MouLiuChengSource(f)]))
            throw new InvalidOperationException("MouLiuCheng cannot normalize another producer's action or replace any genuine material appearance.");
        // Generic CaptureCardUseAction has no rank witness for an empty
        // material set and may leave suit/rank unspecified. Like the other
        // actual zero-material producers, MouLiuCheng explicitly signs its neutral
        // appearance before policies, accepted facts and native issuance.
        return CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type,
            action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
            action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain,
            action.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null));
    }
    private void BindMouLiuChengNativeUse(CardUseFrame use)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { MouLiuChengCompletion: { Stage: MouLiuChengStage.NativeIssuing } r } f) return;
        if (!MouLiuChengRootMatches(f) || !r.Paid || r.Plan is not { } plan || use.MouLiuChengOrigin is not null ||
            use.Action is not { Type: CardActionType.Use } a || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat ||
            a.RequesterSeat is not null || a.PhysicalCards.Count != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            a.EffectiveKind != r.OutputKind || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null ||
            !a.ConversionChain.SequenceEqual([MouLiuChengSource(f)]) || use.CardId != 0 ||
            !a.TargetSeats.SequenceEqual(use.TargetSeats) ||
            !a.TargetSeats.SequenceEqual(GetImplicitSelfCardUseTargets(plan.Kind, f.OwnerSeat, plan.Targets)))
            throw new InvalidOperationException("MouLiuCheng lost its exact separate zero-material native Use and source-bound completion.");
        var origin = new MouLiuChengNativeOrigin(f.Id, MouLiuChengSource(f), f.GameplayHash, use.Id, a.ActionId, f.OwnerSeat, r.OutputKind);
        UpdateLifecycleCardUse(use.Id, u => u with { MouLiuChengOrigin = origin });
        ReplaceRuntimeFrame(f.Id, f with { MouLiuChengCompletion = r with { Stage = MouLiuChengStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        AdvanceEventRulesAndQueueFact(new MouLiuChengNativeIssuedEvent(f.Id, use.Id, a.ActionId, f.OwnerSeat, r.OutputKind));
    }
    private bool IsIssuedMouLiuChengUse(CardUseFrame use)
    {
        if (use.MouLiuChengOrigin is not { } o || use.Action is not { Type: CardActionType.Use } a ||
            o.UseId != use.Id || o.ActionId != a.ActionId || o.Kind != use.CardKind || a.EffectiveKind != use.CardKind ||
            use.SourceSeat != a.ActorSeat || use.CardId != 0 ||
            a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null || a.PhysicalCards.Count != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null ||
            !a.ConversionChain.SequenceEqual([o.Source]) ||
            !MouGuquActorLineageMatches(use.Id, a.ActionId, o.ActorSeat, a.ActorSeat, a.ProviderSeat, a.Type) ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame { MouLiuChengCompletion: { } r } f ||
            !MouLiuChengRootMatches(f) || r.UseId != use.Id || r.ActionId != a.ActionId ||
            o.Source != MouLiuChengSource(f) || o.GameplayHash != f.GameplayHash) return false;
        return ProgramEventHistory<MouLiuChengNativeIssuedEvent>().Count(e => e == new MouLiuChengNativeIssuedEvent(f.Id, use.Id,
            a.ActionId, o.ActorSeat, o.Kind)) == 1 && use.CardId == 0;
    }
    private bool MatchesMouLiuChengNativeAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && LifecycleCardUse(id) is { Action: { } current } use && IsIssuedMouLiuChengUse(use) &&
        action.ActionId == current.ActionId && action.Type == current.Type && action.ActorSeat == current.ActorSeat &&
        action.ProviderSeat == current.ProviderSeat && action.EffectiveKind == current.EffectiveKind && action.PhysicalCards.Count == 0 &&
        action.ConversionChain.SequenceEqual(current.ConversionChain);
    private bool IsMouLiuChengUse(long id) => LifecycleCardUse(id) is { } use && IsIssuedMouLiuChengUse(use);
    private bool IsMouLiuChengZeroUse(long id) => LifecycleCardUse(id) is { CardId: 0 } use && IsIssuedMouLiuChengUse(use);
    private Card MouLiuChengRepresentation(long id) => IsMouLiuChengZeroUse(id) ? new(0, LifecycleCardUse(id)!.CardKind, Suit.None, 0) :
        throw new InvalidOperationException("MouLiuCheng lost its zero-material native card representation.");
    private Card ReadMouLiuChengUseAppearance(long id, CardAppearanceReference appearance)
    {
        if (!IsMouLiuChengZeroUse(id)) return ReadDiouUseAppearance(id, appearance);
        if (appearance.Id != 0 || appearance.Kind != LifecycleCardUse(id)!.CardKind ||
            appearance.Suit != Suit.None || appearance.Rank != 0)
            throw new InvalidOperationException("MouLiuCheng's native continuation changed its exact neutral zero-material appearance.");
        return MouLiuChengRepresentation(id);
    }
    private bool SkipMouLiuChengFinishedMovement(long id, Card c) => c.Id == 0 && IsMouLiuChengZeroUse(id) && c.Kind == LifecycleCardUse(id)!.CardKind;
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 20)]
    private bool FinishMouLiuChengAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || !IsMouLiuChengUse(attack.ResolutionId) ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    private bool IsMouLiuChengAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { MouLiuChengOrigin: { } o } use || !IsIssuedMouLiuChengUse(use) ||
            attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            !IsSlashCard(use.CardKind) ||
            attack.SourceSeat != use.SourceSeat || attack.EffectiveCardKind != use.CardKind ||
            IsSlashCard(use.CardKind) && attack.ProgramSkillCardUseFrameId != o.ProgramFrameId ||
            attack.ProgramSkillCardUseFrameId is { } issuingProgram && issuingProgram != o.ProgramFrameId) return false;
        var ancestors = MouLiuChengNativeAncestorProcessing(use.Id);
        return ancestors is not null && processing.Select(c => c.Id).Distinct().Order().SequenceEqual(ancestors.Order());
    }
    [NativeUseValidation(29401)]
    private void ValidateMouLiuChengNativeUse(CardUseFrame use)
    {
        if (use.MouLiuChengOrigin is not null && (!IsIssuedMouLiuChengUse(use) || use.Step != ResolutionFrameStep.Completed ||
            use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null))
            throw new InvalidOperationException("MouLiuCheng cannot return before its genuine whole native Use and native effects.");
    }
    [NativeUseReturn(29401)]
    private void ReturnMouLiuChengNativeUse(CardUseFrame use)
    {
        if (use.MouLiuChengOrigin is not { } o) return;
        if (LifecycleCardUse(use.Id) is not null || use.Step != ResolutionFrameStep.Completed ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLiuChengCompletion: { } r } f || f.Id != o.ProgramFrameId ||
            r.UseId != use.Id || r.ActionId != o.ActionId || r.Returned ||
            ProgramEventHistory<MouLiuChengNativeReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("MouLiuCheng can return only once to its original paid native parent.");
        var batches = SignMouLiuChengNativeBatches(f, use);
        ReplaceRuntimeTop(f = f with { MouLiuChengCompletion = r with { Stage = MouLiuChengStage.NativeChildren, Returned = true, NativeBatchIds = batches },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouLiuChengNativeReturnedEvent(f.Id, use.Id, o.ActionId));
    }
    private IReadOnlyList<long> SignMouLiuChengNativeBatches(ProgramSkillFrame f, CardUseFrame use)
    {
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null &&
            b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat).ToArray();
        foreach (var b in batches)
        {
            if (b.Movements.Count == 0 || b.Movements.Any(m => !_cardMovements.Contains(m) ||
                m.Sequence <= f.MouLiuChengCompletion!.PaidSequence || m.Sequence > _movementSequence) ||
                b.NativeDrawInvocation is { } draw && !ValidNativeDrawInvocation(draw))
                throw new InvalidOperationException("MouLiuCheng's retired native Use lost its exact original movement and draw invoices.");
            AdvanceEventRulesAndQueueFact(new MouLiuChengNativeBatchSignedEvent(f.Id, use.Id, b.Id, b.ParentBatchId, b.Movements.Count));
            foreach (var m in b.Movements)
                AdvanceEventRulesAndQueueFact(new MouLiuChengNativeMovementSignedEvent(f.Id, use.Id, b.Id, m.CardId,
                    m.CardKind, m.From, m.To, m.Reason, m.Sequence));
        }
        return Array.AsReadOnly(batches.Select(b => b.Id).ToArray());
    }
    private bool IsMouLiuChengNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) =>
        f.MouLiuChengCompletion is { Stage: MouLiuChengStage.NativeChildren, Returned: true } r && MouLiuChengRootMatches(f) &&
        b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null && b.Movements.Count > 0 &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.OriginOwnerSeat == f.OwnerSeat &&
        r.NativeBatchIds.Contains(b.Id) && ProgramEventHistory<MouLiuChengNativeBatchSignedEvent>().Count(e =>
            e == new MouLiuChengNativeBatchSignedEvent(f.Id, r.UseId, b.Id, b.ParentBatchId, b.Movements.Count)) == 1 &&
        b.Movements.All(m => _cardMovements.Contains(m) && ProgramEventHistory<MouLiuChengNativeMovementSignedEvent>().Count(e =>
            e == new MouLiuChengNativeMovementSignedEvent(f.Id, r.UseId, b.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)) == 1);
}
