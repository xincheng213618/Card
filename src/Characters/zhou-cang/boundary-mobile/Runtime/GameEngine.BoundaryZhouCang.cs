namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundaryZhongyongReason = "skill-program.boundary-zhou-cang.zhongyong";
    private bool HasBoundaryZhouCang => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong);
    private static CardConversionSource BoundaryZhongyongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class BoundaryZhouCangHost(GameEngine engine) : IBoundaryZhouCangHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginBoundaryZhongyong(f); }

    // Capture the actual declaration phase and paid entity lineage, before a later
    // observer can change the phase or claim and return an entity to the discard pile.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 53300)]
    private void ObserveBoundaryZhongyongOrigin(IGameEvent payload)
    {
        if (!HasBoundaryZhouCang) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } a } use &&
            IsSlashCard(a.EffectiveKind) && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == a.ActorSeat)
        {
            AdvanceEventRulesAndQueueFact(new BoundaryZhongyongUseOriginEvent(use.Id, a.ActionId, a.ActorSeat, phase));
            RecordBoundaryZhongyongEntities(a);
        }
        else if (payload is CardActionAcceptedEvent { Action: { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge } response })
            RecordBoundaryZhongyongEntities(response);
    }
    private void RecordBoundaryZhongyongEntities(CardActionContext a)
    {
        foreach (var c in a.PhysicalCards)
        {
            var location = _cardZones.GetLocation(c.CardId);
            var paid = location.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile
                ? _cardMovements.LastOrDefault(m => m.CardId == c.CardId && m.From == c.From && m.To.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile)?.Sequence
                : null;
            AdvanceEventRulesAndQueueFact(new BoundaryZhongyongEntityOriginEvent(a.ActionId, c.CardId, c.From, _movementSequence, paid));
        }
    }
    private bool BoundaryZhongyongEntityAvailable(long action, int id)
    {
        if (ProgramEventHistory<BoundaryZhongyongEntityOriginEvent>().SingleOrDefault(e => e.ActionId == action && e.CardId == id) is not { } origin ||
            _cardZones.GetLocation(id).Zone is not (CardZoneKind.Processing or CardZoneKind.DiscardPile)) return false;
        // Slash declaration precedes its native hand->Processing payment. A
        // response is recorded after payment. Both retain the same original
        // source and first actual payment, so claims before Accepted are visible.
        var payment = origin.PaidSequence is { } paid
            ? _cardMovements.SingleOrDefault(m => m.Sequence == paid && m.CardId == id && m.From == origin.From && m.To.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile)
            : _cardMovements.FirstOrDefault(m => m.Sequence > origin.Sequence && m.CardId == id && m.From == origin.From && m.To.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile);
        return payment is not null && !_cardMovements.Any(m => m.CardId == id && m.Sequence > payment.Sequence &&
            m.From.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile && m.To.Zone is not (CardZoneKind.Processing or CardZoneKind.DiscardPile));
    }

    private CardUseFrame? BoundaryZhongyongUse(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.SkillId != BoundaryZhouCangComposition.Skill || c.BindingId != BoundaryZhouCangComposition.Binding ||
            context.Window != SkillProgramTriggerWindow.CardUseCompleted || context.CardUse is not { } identity ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Step: ResolutionFrameStep.Completed, Action: { Type: CardActionType.Use } a } use ||
            a.ActionId != identity.CardActionId || a.ActorSeat != c.OwnerSeat || !IsSlashCard(a.EffectiveKind) ||
            ProgramEventHistory<BoundaryZhongyongUseOriginEvent>().SingleOrDefault(e => e.UseId == use.Id && e.ActionId == a.ActionId && e.Actor == c.OwnerSeat) is null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1 ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not
                { Continuation: ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard } window ||
            window.ParentFrameId != use.Id || window.Action.ActionId != a.ActionId || !DesignatedExtraTargetCandidateMatches(c, context, window)) return null;
        return use;
    }
    private CardActionContext[] BoundaryZhongyongResponses(CardUseFrame use)
    {
        var accepted = ProgramEventHistory<CardActionAcceptedEvent>().Select(e => e.Action).ToArray();
        return ProgramEventHistory<CardResponseCompletedEvent>().Where(e => e.AttackOwnerFrameId == use.Id && e.ParentFrameId == use.Id &&
                e.EffectiveKind == CardKind.Dodge && e.OriginalContinuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge &&
                use.TargetSeats.Contains(e.NativeActorSeat))
            .Select(e => accepted.SingleOrDefault(a => a.ActionId == e.ActionId && a.Type == CardActionType.Response &&
                a.EffectiveKind == CardKind.Dodge && a.ParentActionId == use.Action!.ActionId && a.ActorSeat == e.NativeActorSeat && a.ProviderSeat == e.ProviderSeat))
            .Where(a => a is not null).Cast<CardActionContext>().DistinctBy(a => a.ActionId).ToArray();
    }
    private bool HasBoundaryZhouCangResponseObserver(CardActionContext a, ProgramCardContinuation continuation) =>
        HasBoundaryZhouCang && a.Type == CardActionType.Response && a.EffectiveKind == CardKind.Dodge &&
        continuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge && a.ParentActionId is { } parent &&
        _resolutionStack.OfType<CardUseFrame>().Any(u => u.Action?.ActionId == parent &&
            ProgramEventHistory<BoundaryZhongyongUseOriginEvent>().Any(e => e.UseId == u.Id && e.ActionId == parent));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 53300, SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong)]
    private bool CanOfferBoundaryZhongyong(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!_players[c.OwnerSeat].IsAlive || BoundaryZhongyongUse(c, context) is not { } use) return false;
        var responses = BoundaryZhongyongResponses(use);
        return responses.Length > 0 ? responses.Any(a => a.PhysicalCards.Any(p => BoundaryZhongyongEntityAvailable(a.ActionId, p.CardId))) ||
                use.Action!.PhysicalCards.Any(p => BoundaryZhongyongEntityAvailable(use.Action.ActionId, p.CardId)) :
            use.Action!.PhysicalCards.Any(p => BoundaryZhongyongEntityAvailable(use.Action.ActionId, p.CardId));
    }
    private CardUseFrame RequireBoundaryZhongyongUse(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } c || BoundaryZhongyongUse(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), c) is not { } use ||
            _resolutionStack.FindIndex(x => x.Id == f.Id) is var i && (i < 2 || _resolutionStack[i - 1].Id != c.ParentFrameId || _resolutionStack[i - 2].Id != use.Id))
            throw new InvalidOperationException("Zhongyong requires the original completed native Slash and its immediate trigger mother.");
        return use;
    }
    private SkillProgramStepOutcome BeginBoundaryZhongyong(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = RequireBoundaryZhongyongUse(f);
        if (f.BoundaryZhongyong is not null) throw new InvalidOperationException("Zhongyong cannot reopen its original completed use.");
        var responses = BoundaryZhongyongResponses(use);
        var phase = ProgramEventHistory<BoundaryZhongyongUseOriginEvent>().Single(e => e.UseId == use.Id).Phase;
        var r = new BoundaryZhongyongReceipt(BoundaryZhongyongSource(f), f.GameplayHash, use.Id, use.Action!.ActionId, f.WindowContext!.ParentFrameId,
            phase, responses.Length > 0, BoundaryZhongyongStage.Choose)
        {
            Slash = use.Action.PhysicalCards.Where(p => BoundaryZhongyongEntityAvailable(use.Action.ActionId, p.CardId)).Select(p => p.CardId).ToArray(),
            Dodge = responses.SelectMany(a => a.PhysicalCards.Where(p => BoundaryZhongyongEntityAvailable(a.ActionId, p.CardId)).Select(p => p.CardId)).Distinct().ToArray(),
            Excluded = use.TargetSeats
        };
        ReplaceRuntimeTop(f = f with { BoundaryZhongyong = r });
        AdvanceEventRulesAndQueueFact(new BoundaryZhongyongStartedEvent(f.Id, r.Source, r.Hash, r.UseId, r.ActionId, r.WindowId, r.Responded, r.Slash.Count, r.Dodge.Count, r.Phase));
        if (!r.Responded)
        {
            if (r.Slash.Count == 0) { FinishBoundaryZhongyong(f); return SkillProgramStepOutcome.AwaitChild; }
            MoveBoundaryZhongyong(f, BoundaryZhongyongStage.AcquireSlashChildren, f.OwnerSeat, r.Slash);
        }
        else PublishBoundaryZhongyong(f);
        // The operation owns its original instruction until all movement observers return.
        return _pendingDecision is not null ? SkillProgramStepOutcome.AwaitChoice : SkillProgramStepOutcome.AwaitChild;
    }
    private bool BoundaryZhongyongOriginalTurn(BoundaryZhongyongReceipt r) => r.Phase.TurnNumber == _turnNumber && r.Phase.ActualTurnOwnerSeat == _currentSeat;
    private int[] BoundaryZhongyongRecipients(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat &&
        !f.BoundaryZhongyong!.Excluded.Contains(p.Seat)).Select(p => p.Seat).ToArray();
    private int[] AvailableBoundaryZhongyong(ProgramSkillFrame f, bool slash)
    {
        var r = f.BoundaryZhongyong!; var use = RequireBoundaryZhongyongUse(f);
        return (slash ? r.Slash : r.Dodge).Where(id => slash ? BoundaryZhongyongEntityAvailable(r.ActionId, id) :
            BoundaryZhongyongResponses(use).Any(a => a.PhysicalCards.Any(p => p.CardId == id) && BoundaryZhongyongEntityAvailable(a.ActionId, id))).ToArray();
    }
    private IReadOnlyList<PromptChoice> BoundaryZhongyongChoices(ProgramSkillFrame f)
    {
        var r = f.BoundaryZhongyong!; var result = new List<PromptChoice>();
        PromptChoice Choice(string branch, string label, IReadOnlyList<int> ids, int? target = null) => new(new($"boundary-zhongyong.{f.Id}.{branch}.{target}"), label, ids, target is { } t ? [t] : [],
            new Dictionary<string, string> { ["program-action"] = "boundary-zhongyong", ["branch"] = branch, ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        if (_players[f.OwnerSeat].IsAlive && (r.Paid || HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)))
        {
            if (r.Stage == BoundaryZhongyongStage.Choose)
            {
                result.Add(Choice("acquire-dodge", "获得响应的闪，然后可以将杀交给另一名其他角色", AvailableBoundaryZhongyong(f, false)));
                var dodge = AvailableBoundaryZhongyong(f, false);
                if (dodge.Length > 0) foreach (var target in BoundaryZhongyongRecipients(f))
                    result.Add(Choice("give-dodge", $"将响应的闪交给{_players[target].Name}，杀次数+1且下一杀伤害+1", dodge, target));
            }
            else if (r.Stage == BoundaryZhongyongStage.GiveSlash && AvailableBoundaryZhongyong(f, true) is { Length: > 0 } slash)
                foreach (var target in BoundaryZhongyongRecipients(f)) result.Add(Choice("give-slash", $"将此杀交给{_players[target].Name}", slash, target));
        }
        result.Add(Choice("decline", r.Stage == BoundaryZhongyongStage.GiveSlash ? "不交出杀" : "不发动", []));
        return result;
    }
    private void PublishBoundaryZhongyong(ProgramSkillFrame f)
    {
        var choices = BoundaryZhongyongChoices(f); var s = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "忠勇：选择获得或交出本次杀与响应的闪。", choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, s.Name, s.Name, s.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("boundary-zhongyong")]
    private void ResolveBoundaryZhongyongChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Zhongyong lost its original frame.");
        AssertBoundaryZhongyong(f);
        if (!BoundaryZhongyongChoices(f).Any(c => AssistedChoicesEqual([c], [selected])) || _pendingDecision?.PlayerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Zhongyong choice lost its current entities or exact recipient.");
        ClearPendingDecision();
        switch (selected.Parameters["branch"])
        {
            case "decline": FinishBoundaryZhongyong(f); break;
            case "acquire-dodge":
                if (selected.Cards.Count == 0)
                { ReplaceRuntimeTop(f = f with { BoundaryZhongyong = f.BoundaryZhongyong! with { Stage = BoundaryZhongyongStage.GiveSlash, Paid = true } }); PublishBoundaryZhongyong(f); }
                else MoveBoundaryZhongyong(f, BoundaryZhongyongStage.AcquireDodgeChildren, f.OwnerSeat, selected.Cards);
                break;
            case "give-dodge": MoveBoundaryZhongyong(f, BoundaryZhongyongStage.GiveDodgeChildren, selected.Targets.Single(), selected.Cards); break;
            case "give-slash": MoveBoundaryZhongyong(f, BoundaryZhongyongStage.GiveSlashChildren, selected.Targets.Single(), selected.Cards); break;
            default: throw new InvalidOperationException("Unknown Zhongyong branch.");
        }
    }
    private void MoveBoundaryZhongyong(ProgramSkillFrame f, BoundaryZhongyongStage stage, int recipient, IReadOnlyList<int> ids)
    {
        foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
        var r = f.BoundaryZhongyong! with { Stage = stage, Paid = true, Recipient = recipient, Index = f.BoundaryZhongyong!.Index + 1,
            Requested = ids, Before = _movementSequence, After = _movementSequence, Settlement = null };
        ReplaceRuntimeTop(f = f with { BoundaryZhongyong = r, PendingMovementContinuation = new(recipient, 0, null) });
        void Settled(HappyWeiYanGainRequestSettlement settlement)
        {
            ValidateHappyWeiYanGainRequestSettlement(settlement, f.Id, ids, CardLocation.Hand(recipient), r.Before);
            var now = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(now with { BoundaryZhongyong = now.BoundaryZhongyong! with { Settlement = settlement, After = _movementSequence } });
            foreach (var m in settlement.Movements)
            {
                AdvanceEventRulesAndQueueFact(new BoundaryZhongyongMovementEvent(f.Id, r.Index, stage, recipient, m.CardId, m.From, m.To, m.Sequence));
                if (recipient == f.OwnerSeat && m.To == CardLocation.Hand(recipient) && BoundaryZhongyongOriginalTurn(r))
                    AdvanceEventRulesAndQueueFact(new BoundaryZhongyongUseBannedEvent(f.Id, recipient, m.CardId, r.Phase.TurnNumber, r.Phase.ActualTurnOwnerSeat, m.Sequence));
            }
        }
        MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(recipient), new(BoundaryZhongyongReason),
            afterRequestSettled: recipient == f.OwnerSeat ? Settled : NativeGiveRequestCompletion(f.Id, f.OwnerSeat, recipient, ids, r.Before, Settled), producerFrameId: f.Id);
        var current = GetActiveProgramFrame(f.Id); var paid = current.BoundaryZhongyong!;
        ReplaceRuntimeTop(current with { BoundaryZhongyong = paid with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new BoundaryZhongyongMovementSettledEvent(f.Id, r.Index, stage, recipient, ids.Count, r.Before, _movementSequence));
        ContinueBoundaryZhongyong(f.Id);
    }
    private bool DrainBoundaryZhongyong(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueBoundaryZhongyong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryZhongyong: { } r } f || f.Id != id) return;
        AssertBoundaryZhongyong(f);
        if (f.PendingMovementContinuation is not null)
        {
            if (DrainBoundaryZhongyong(f)) return;
            f = GetActiveProgramFrame(id); r = f.BoundaryZhongyong!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        }
        if (r.Stage is BoundaryZhongyongStage.Choose or BoundaryZhongyongStage.GiveSlash)
        { if (_pendingDecision is null) PublishBoundaryZhongyong(f); return; }
        if (r.Stage == BoundaryZhongyongStage.AcquireDodgeChildren && _players[f.OwnerSeat].IsAlive)
        { ReplaceRuntimeTop(f = f with { BoundaryZhongyong = r with { Stage = BoundaryZhongyongStage.GiveSlash, Settlement = null, Requested = [] } }); PublishBoundaryZhongyong(f); return; }
        if (r.Stage == BoundaryZhongyongStage.GiveDodgeChildren && BoundaryZhongyongOriginalTurn(r) && _players[f.OwnerSeat].IsAlive &&
            r.Settlement!.Movements.Any(m => m.To == CardLocation.Hand(r.Recipient)))
        {
            if (ProgramEventHistory<BoundaryZhongyongBonusEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Zhongyong cannot grant the same paid bonus twice.");
            GrantProgramTurnRuleModifier(f, SkillRuleQuery.SlashLimit, SkillRuleOperation.Add, 1, []);
            var index = _namedNextSlashReserves.FindIndex(n => n.OwnerSeat == f.OwnerSeat && n.SkillId == f.SkillId && n.TurnNumber == _turnNumber && n.TurnSeat == _currentSeat);
            if (index < 0) _namedNextSlashReserves.Add(new(f.OwnerSeat, f.SkillId, _turnNumber, _currentSeat, 1));
            else _namedNextSlashReserves[index] = _namedNextSlashReserves[index] with { Amount = checked(_namedNextSlashReserves[index].Amount + 1) };
            AdvanceEventRulesAndQueueFact(new NextSlashDamageReservedEvent(f.OwnerSeat, f.SkillId, _turnNumber, _currentSeat, r.UseId, r.Excluded.FirstOrDefault(), 1));
            AdvanceEventRulesAndQueueFact(new BoundaryZhongyongBonusEvent(f.Id, r.UseId, f.OwnerSeat, r.Recipient, _turnNumber, _currentSeat));
        }
        FinishBoundaryZhongyong(f);
    }
    private void FinishBoundaryZhongyong(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { BoundaryZhongyong = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 53300)]
    private bool ResumeBoundaryZhongyong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundaryZhongyong: not null } f || f.Id != id) return false;
        ContinueBoundaryZhongyong(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 53300)]
    private bool ReturnBoundaryZhongyongMovement(ProgramSkillFrame f)
    { if (f.BoundaryZhongyong is null) return false; ContinueBoundaryZhongyong(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 53300)]
    private bool AdvanceBoundaryZhongyong()
    {
        if (_pendingDecision is not null) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { BoundaryZhongyong: not null, PendingMovementContinuation: not null } f)
        { ContinueBoundaryZhongyong(f.Id); return true; }
        return BoundaryZhongyongObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 53300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 53300)]
    private bool KeepBoundaryZhongyongPaidFrame(ProgramSkillFrame f) => f.BoundaryZhongyong is { Paid: true };
}
