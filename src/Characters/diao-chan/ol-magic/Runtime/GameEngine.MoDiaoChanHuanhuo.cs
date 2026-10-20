namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMoDiaoChan => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep);
    private static CardConversionSource MoDcSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int? MoDcDueTurn(MoDiaoChanPromiseEvent p) => CompleteProgramEventHistory().OfType<TurnStartedEvent>()
        .Skip(p.AfterTurnStartOrdinal).FirstOrDefault(t => t.ActorSeat == p.TargetSeat)?.TurnNumber;
    private MoDiaoChanPromiseEvent[] ActiveMoDcPromises(int actor) => !HasMoDiaoChan || !HasOngoingWholeActualUseTurn || actor != _turnProgression.OwnerSeat ||
        CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != actor ? [] :
        ProgramEventHistory<MoDiaoChanPromiseEvent>().Where(p => p.TargetSeat == actor && MoDcDueTurn(p) == _turnNumber &&
            ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Count(e => e.Claim.PromiseId == p.PromiseId) < 2).ToArray();
    private int MoDcRemaining(int actor) => ActiveMoDcPromises(actor).Select(p => 2 - ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Count(e => e.Claim.PromiseId == p.PromiseId)).DefaultIfEmpty(0).Max();

    // Only command progression calls this; view/query code merely reads the signed lock.
    private void PrepareMoDiaoChanHandSelection(CharacterState actor)
    {
        if (!HasMoDiaoChan || ActiveMoDcPromises(actor.Seat).Length == 0 || CurrentActualDiscardRecoveryPhase() is not { } phase) return;
        var hand = GetHand(actor).Select(c => c.Id).ToHashSet();
        var actions = BuildLegalActionsCore(actor, includeProgramActions: true, selectedPhysicalCardId: null, nativeOriginalCard: null, ignoreMoDiaoChanSelection: true);
        var pool = actions.Where(a => a.Kind is not (LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.Recast))
            .SelectMany(a => a.CardId is { } id ? new[] { id } : a.SelectableCardIds ?? []).Where(hand.Contains).Distinct().Order().ToArray();
        var old = ProgramEventHistory<MoDiaoChanLockEvent>().LastOrDefault(e => e.ActorSeat == actor.Seat && e.ActualTurn == _turnNumber);
        if (old is not null && old.PhaseToken == phase.Token && (old.CardId == 0 && pool.Length == 0 || pool.Contains(old.CardId))) return;
        var before = _random.State; var index = pool.Length == 0 ? -1 : _random.Next(pool.Length);
        var lockId = ProgramEventHistory<MoDiaoChanLockEvent>().Select(e => e.LockId).DefaultIfEmpty().Max() + 1;
        AdvanceEventRulesAndQueueFact(new MoDiaoChanLockEvent(lockId, actor.Seat, _turnNumber, phase.Token, index < 0 ? 0 : pool[index], pool.Length, index, before, _random.State));
    }
    private ForcedHandSelectionSnapshot? MoDiaoChanHandSelection(int actor)
    {
        if (ActiveMoDcPromises(actor).Length == 0 || CurrentActualDiscardRecoveryPhase() is not { } phase ||
            ProgramEventHistory<MoDiaoChanLockEvent>().LastOrDefault(e => e.ActorSeat == actor && e.ActualTurn == _turnNumber && e.PhaseToken == phase.Token) is not { CardId: > 0 } pick ||
            _cardZones.GetLocation(pick.CardId) != CardLocation.Hand(actor)) return null;
        return new(MoDiaoChanComposition.Huanhuo, pick.LockId, phase.Token, pick.CardId, MoDcRemaining(actor));
    }
    private IReadOnlyList<LegalAction> FilterMoDiaoChanHandSelection(CharacterState actor, IEnumerable<LegalAction> actions)
    {
        if (MoDiaoChanHandSelection(actor.Seat) is not { } hint) return actions.ToArray();
        return actions.Where(a => a.Kind is LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.Recast ||
            (a.CardId is > 0 and var id ? _cardZones.GetLocation(id) != CardLocation.Hand(actor.Seat) || id == hint.CardId :
            a.SelectableCardIds is not { Count: > 0 } ids || ids.Contains(hint.CardId))).ToArray();
    }
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 10)]
    private void ValidateMoDiaoChanSubmittedUse(ProgramSubmittedUseQuery query)
    {
        if (!MoDcHuanhuoPaymentAllowed(query.ActorSeat, query.CardIds))
            throw new InvalidOperationException("Huanhuo requires its current selected Hand entity in this real Play use.");
    }

    private bool MoDcHuanhuoPaymentAllowed(int actor, IReadOnlyList<int> ids, long? existingUseId = null)
    {
        // A compulsory Play selection is not a response or a skill-payment ban.
        if (ActiveDying is not null || ActiveNullificationWindow is not null || ActiveCardAttack is not null || ActiveBorrowedSword is not null ||
            _resolutionStack.LastOrDefault() is ProgramSkillFrame) return true;
        if (existingUseId is { } use && LifecycleCardUse(use)?.MoDiaoChanClaims?.Any(q => q.ActorSeat == actor) == true) return true;
        var hint = MoDiaoChanHandSelection(actor);
        return hint is null || ids.All(id => _cardZones.GetLocation(id) != CardLocation.Hand(actor)) || ids.Contains(hint.CardId);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 31400)]
    private void CaptureMoDiaoChanUses(IGameEvent fact)
    {
        if (!HasMoDiaoChan) return;
        CardActionContext? action = null; ResolutionFrame? parent = null;
        if (fact is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } a } use)
        { action = a; parent = use; }
        else if (fact is CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted)
        {
            action = accepted.Action;
            if (action.EffectiveKind == CardKind.Nullification && IsCompletedResponseUse(action, ProgramCardContinuation.NullificationResponse)) parent = ActiveNullificationWindow;
            else if (action.EffectiveKind == CardKind.Dodge && IsCompletedResponseUse(action, ProgramCardContinuation.Dodge) && ActiveCardAttack is { } attack)
                parent = _resolutionStack.SingleOrDefault(f => f.Id == attack.ResolutionId);
        }
        if (action is null || parent is null || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase) return;
        foreach (var p in ActiveMoDcPromises(action.ActorSeat))
        {
            if (ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Any(e => e.Claim.PromiseId == p.PromiseId && e.Claim.ActionId == action.ActionId)) continue;
            var ordinal = ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Count(e => e.Claim.PromiseId == p.PromiseId) + 1;
            var source = p.Source with { BindingId = "affected-use-completed" };
            var q = new MoDiaoChanUseClaim(p.PromiseId, parent.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind, phase.Token, _turnNumber, ordinal, source, p.GameplayHash);
            parent = _resolutionStack.Single(f => f.Id == parent.Id);
            ReplaceRuntimeFrame(parent.Id, parent with { MoDiaoChanClaims = (parent.MoDiaoChanClaims ?? []).Append(q).ToArray() });
            AdvanceEventRulesAndQueueFact(new MoDiaoChanUseClaimedEvent(q));
        }
    }
    private MoDiaoChanUseClaim? MoDcCompletedClaim(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (MoDcDirectClaim(candidate, context) is { } direct) return direct;
        if (candidate.SkillId != MoDiaoChanComposition.Huanhuo || candidate.BindingId != "affected-use-completed" || context.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            context.CardUse is not { } identity || _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            w.Action.ActionId != identity.CardActionId || _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId)?.MoDiaoChanClaims is not { } claims) return null;
        return claims.SingleOrDefault(q => q.Source.OwnerSeat == candidate.OwnerSeat && q.Source.SkillInstanceId == candidate.SkillInstanceId && q.GameplayHash == candidate.GameplayHash &&
            q.ActionId == w.Action.ActionId && q.ProviderSeat == w.Action.ProviderSeat && MouGuquActorLineageMatches(q.ParentFrameId, q.ActionId, q.ActorSeat, w.Action.ActorSeat, q.ProviderSeat, w.Action.Type) &&
            ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Count(e => e.Claim == q) == 1);
    }
    private bool IsIssuedMoDiaoChanCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => MoDcCompletedClaim(c, context) is { } q &&
        !ProgramEventHistory<MoDiaoChanStartedEvent>().Any(e => e.Source == q.Source && _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == e.FrameId && f.MoDiaoChan?.ActionId == q.ActionId)) &&
        !ProgramEventHistory<MoDiaoChanUseSettledEvent>().Any(e => e.PromiseId == q.PromiseId && e.ActionId == q.ActionId);
    private void CollectIssuedMoDiaoChanCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasMoDiaoChan || window != SkillProgramTriggerWindow.CardUseCompleted || _resolutionStack.LastOrDefault()?.MoDiaoChanClaims is not { } claims) return;
        foreach (var q in claims.Where(q => q.ActionId == action.ActionId && q.ProviderSeat == action.ProviderSeat && q.ActorSeat == action.ActorSeat))
        {
            result.RemoveAll(c => c.OwnerSeat == q.Source.OwnerSeat && c.SkillId == q.Source.SkillId && c.TriggerId == q.Source.BindingId && c.SkillInstanceId == q.Source.SkillInstanceId);
            result.Add(new(q.Source.OwnerSeat, -1, q.Source.SkillId, q.Source.BindingId, q.GameplayHash, q.Source.SkillInstanceId, 0,
                CreateCardActionProgramContext(action, window, q.Ordinal, q.Source.OwnerSeat, -1, CaptureProgramTriggerFacts(_players[q.Source.OwnerSeat], action))));
        }
    }
    private bool HasIssuedMoDiaoChanResponse(CardActionContext action) => HasMoDiaoChan && _resolutionStack.Any(f => f.MoDiaoChanClaims?.Any(q => q.ActionId == action.ActionId && q.ParentFrameId == f.Id &&
        ProgramEventHistory<MoDiaoChanUseClaimedEvent>().Count(e => e.Claim == q) == 1) == true);

    private Card[] MoDcDiscardable(int owner) => GetHand(_players[owner]).Concat(GetEquipment(_players[owner])).Where(c =>
        !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private void PayMoDcRandomDiscard(ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!; var pool = _players[r.ActorSeat].IsAlive ? MoDcDiscardable(r.ActorSeat).OrderBy(c => c.Id).ToArray() : [];
        var before = _random.State; var index = pool.Length == 0 ? -1 : _random.Next(pool.Length); var seq = _movementSequence;
        ReplaceRuntimeTop(f = f with { MoDiaoChan = r = r with { Paid = true, Stage = MoDiaoChanStage.CostChildren, Before = seq, After = seq }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MoDiaoChanRandomDiscardEvent(f.Id, r.ActorSeat, pool.Length, index, before, _random.State, index < 0 ? null : pool[index].Id));
        if (index >= 0) MoDcDiscardOne(f, r.ActorSeat, pool[index]);
        SealMoDcMovement(f.Id, seq);
    }
    private void MoDcDiscardOne(ProgramSkillFrame f, int owner, Card card)
    {
        var from = _cardZones.GetLocation(card.Id);
        MoveLogicalDiscard(f.OwnerSeat, owner, card, from, new(f.SkillId == MoDiaoChanComposition.Huanhuo ? "skill-program.ol:mo-huanhuo.discard" : "skill-program.ol:mo-qingshi.cost"), parentFrameId: f.Id);
        var move = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.From == from && m.Sequence > f.MoDiaoChan!.Before);
        if (move is not null) AdvanceEventRulesAndQueueFact(new MoDiaoChanCostEvent(f.Id, card.Id, card.Kind, from, move.To, move.Sequence));
    }
}
