namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ForeignTurnContestClaimReason = "program.foreign-turn-contest.claim";
    private bool HasForeignTurnContests =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveForeignTurnPindian);
    private CardConversionSource ForeignContestSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    private bool TryBeginForeignActualTurnStart(CharacterState turnOwner)
    {
        if (!HasForeignTurnContests || _winner != Winner.None || !turnOwner.IsAlive) return false;
        if (_resolutionStack.Count != 0 || _pendingDecision is not null || turnOwner.Seat != _currentSeat)
            throw new InvalidOperationException("A foreign turn-start contest requires its clean actual turn boundary.");
        var entries = new List<(ProgramTriggerCandidate Candidate, SkillProgramTriggerFacts Facts)>();
        foreach (var owner in _players.Where(p => p.IsAlive && p.Seat != turnOwner.Seat))
        {
            var frozen = CaptureProgramTriggerFacts(owner);
            entries.AddRange(CollectEligibleProgramTriggerCandidates(owner,
                SkillProgramTriggerWindow.OtherActualTurnStarted, frozen).Select(c => (c, frozen)));
        }
        if (entries.Count == 0) return false;
        var sorted = entries.OrderByDescending(e => e.Candidate.Priority).ThenBy(e => e.Candidate.OwnerSeat)
            .ThenBy(e => e.Candidate.SkillId, StringComparer.Ordinal).ThenBy(e => e.Candidate.SkillInstanceId).ToArray();
        var id = ++_resolutionSequence;
        PushRuntimeFrame(new ForeignActualTurnStartWindowFrame(id, _turnNumber, turnOwner.Seat,
            sorted.Select(e => e.Candidate).ToArray(), sorted.Select(e => new ProgramSkillWindowContext(
                SkillProgramTriggerWindow.OtherActualTurnStarted, id, e.Candidate.OwnerSeat,
                SourceSeat: turnOwner.Seat, TargetSeat: turnOwner.Seat,
                OccurrenceIndex: e.Candidate.OccurrenceIndex, Facts: e.Facts)).ToArray()));
        AdvanceRuntimeTop<ForeignActualTurnStartWindowFrame>(); return true;
    }
    private void ContinueOwnActualTurnStart(ForeignActualTurnStartWindowFrame parent)
    {
        if (parent.OwnerSeat != _currentSeat || parent.ActualTurnNumber != _turnNumber)
            throw new InvalidOperationException("A foreign contest lost its exact own-turn continuation.");
        if (_winner != Winner.None) { if (_resolutionStack.Count == 0 && _status != EngineStatus.Completed) CompleteGame(); return; }
        if (!_players[parent.OwnerSeat].IsAlive) { EndTurn(); return; }
        ContinueTurnAfterForeignActualContests(_players[parent.OwnerSeat]);
    }
    private void ContinueForeignActualTurnStartCore()
    {
        while (_resolutionStack.LastOrDefault() is ForeignActualTurnStartWindowFrame parent)
        {
            if (parent.CandidateIndex >= parent.Candidates.Count || _winner != Winner.None)
            {
                PopResolutionFrame(parent.Id, ResolutionFrameKind.ForeignActualTurnStartWindow);
                ContinueOwnActualTurnStart(parent); return;
            }
            var candidate = parent.Candidates[parent.CandidateIndex]; var context = parent.Contexts[parent.CandidateIndex];
            if (!CanRunProgramTrigger(candidate, context)) { AdvanceForeignActualTurnStartCandidate(parent, false, false); continue; }
            ReplaceRuntimeTop(parent with { Step = ResolutionFrameStep.AwaitingResponse });
            ExposeProgramTriggerDecision(candidate, context); return;
        }
    }
    private void AdvanceForeignActualTurnStartCandidate(ForeignActualTurnStartWindowFrame parent, bool activated, bool completed)
    {
        var c = parent.Candidates[parent.CandidateIndex];
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(parent.Id, c.SkillId, c.BindingId, c.SkillInstanceId,
            c.OwnerSeat, SkillProgramTriggerWindow.OtherActualTurnStarted, activated, completed));
        ReplaceRuntimeTop(parent with { CandidateIndex = parent.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
    }
    private void CompleteForeignActualTurnStartBinding(ProgramSkillFrame child)
    {
        if (_resolutionStack.LastOrDefault() is not ForeignActualTurnStartWindowFrame parent ||
            parent.Id != child.WindowContext?.ParentFrameId || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(child, parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("A foreign turn-start binding lost its exact typed parent.");
        ReplaceRuntimeTop(parent with { CandidateIndex = parent.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceRuntimeTop<ForeignActualTurnStartWindowFrame>();
    }
    private bool HasForeignActualTurnStartBoundary()
    {
        if (_resolutionStack.FirstOrDefault() is not ForeignActualTurnStartWindowFrame parent) return false;
        if (parent.ActualTurnNumber != _turnNumber || parent.OwnerSeat != _currentSeat ||
            parent.Candidates.Count != parent.Contexts.Count || parent.CandidateIndex < 0 || parent.CandidateIndex > parent.Candidates.Count ||
            parent.Contexts.Where((context, index) => context.Window != SkillProgramTriggerWindow.OtherActualTurnStarted ||
                context.ParentFrameId != parent.Id || context.SourceSeat != parent.OwnerSeat || context.TargetSeat != parent.OwnerSeat ||
                context.OwnerSeat != parent.Candidates[index].OwnerSeat || context.OccurrenceIndex != parent.Candidates[index].OccurrenceIndex).Any())
            throw new InvalidOperationException("A foreign actual-turn cursor lost its frozen participants and contexts.");
        return true;
    }
    private bool CanRunForeignActualTurnContest(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        var owner = _players[candidate.OwnerSeat];
        return context.Window == SkillProgramTriggerWindow.OtherActualTurnStarted &&
            owner.IsAlive && owner.Hp > 0 && owner.Hp < owner.MaxHp &&
            context.SourceSeat == _currentSeat && context.TargetSeat == _currentSeat && owner.Seat != _currentSeat &&
            _players[_currentSeat].IsAlive && GetHand(owner).Count > 0 && GetHand(_players[_currentSeat]).Count > 0 &&
            CanBePindianTarget(owner.Seat, _currentSeat) &&
            _resolutionStack.OfType<ForeignActualTurnStartWindowFrame>().LastOrDefault() is { } parent &&
            parent.Id == context.ParentFrameId && parent.OwnerSeat == _currentSeat && parent.ActualTurnNumber == _turnNumber &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            parent.Candidates[parent.CandidateIndex] == candidate &&
            CompleteProgramEventHistory().OfType<TurnStartedEvent>().LastOrDefault() is { } started &&
            started.TurnNumber == _turnNumber && started.ActorSeat == _currentSeat;
    }
    private bool IsForeignActualTurnPindian(ProgramSkillFrame f) =>
        f.WindowContext is { Window: SkillProgramTriggerWindow.OtherActualTurnStarted } context &&
        context.SourceSeat == _currentSeat && context.TargetSeat == _currentSeat &&
        context.OwnerSeat == f.OwnerSeat && f.OwnerSeat != _currentSeat &&
        _resolutionStack.OfType<ForeignActualTurnStartWindowFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is { } parent &&
        parent.OwnerSeat == _currentSeat && parent.ActualTurnNumber == _turnNumber &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]);

    // Called only while the real revealed Pindian frame is still the direct child.
    // Old bindings retain a null property and their exact historical JSON shape.
    private ForeignTurnPindianOrigin? FreezeForeignTurnPindianOrigin(ProgramSkillFrame program, PindianFrame pindian)
    {
        if (!IsForeignActualTurnPindian(program)) return null;
        var plan = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!);
        if (plan.Instructions.Count != 3 || program.InstructionIndex != 2 ||
            plan.Instructions[1].Op != SkillProgramEffectOp.StartPindian ||
            plan.Instructions[1].ResultBind != pindian.ProgramResultBind ||
            plan.Instructions[2].Op != SkillProgramEffectOp.ResolveForeignTurnPindian ||
            plan.Instructions[2].SourceBind != pindian.ProgramResultBind ||
            pindian.ParentFrameId != program.Id || pindian.Result is not { } result ||
            result.SourceSeat != program.OwnerSeat || result.OpponentSeat != _currentSeat ||
            program.SelectedTargetSeats is not [var selected] || selected != result.OpponentSeat ||
            !CompleteProgramEventHistory().OfType<PindianResultDeterminedEvent>().Any(e =>
                e.FrameId == pindian.Id && e.SkillId == program.SkillId && e.Result == result))
            throw new InvalidOperationException("A foreign turn contest lost its original revealed result producer.");
        var origin = new ForeignTurnPindianOrigin(pindian.Id, program.Id, _turnNumber,
            _currentSeat, program.GameplayHash, result);
        AdvanceEventRulesAndQueueFact(new ForeignTurnContestOriginFrozenEvent(origin,
            ForeignContestSource(program), pindian.ProgramResultBind!));
        return origin;
    }
    private bool MatchesForeignTurnContestOrigin(ProgramSkillFrame f, string bind, ForeignTurnPindianOrigin origin)
    {
        var result = f.PindianResultBindings.SingleOrDefault(b => b.Name == bind);
        return IsForeignActualTurnPindian(f) && origin.ProgramFrameId == f.Id && origin.PindianFrameId > f.Id &&
            origin.ActualTurnNumber == _turnNumber && origin.ActualTurnOwnerSeat == _currentSeat &&
            origin.GameplayHash == f.GameplayHash && result?.ForeignTurnOrigin == origin &&
            result.SourceSeat == origin.Result.SourceSeat && result.OpponentSeat == origin.Result.OpponentSeat &&
            result.SourceRank == origin.Result.SourceRank && result.OpponentRank == origin.Result.OpponentRank &&
            result.SourceWon == origin.Result.SourceWon && origin.Result.SourceSeat == f.OwnerSeat &&
            origin.Result.OpponentSeat == _currentSeat && origin.Result.SourceCardId > 0 &&
            origin.Result.OpponentCardId > 0 && origin.Result.SourceCardId != origin.Result.OpponentCardId &&
            CompleteProgramEventHistory().OfType<ForeignTurnContestOriginFrozenEvent>().Count(e =>
                e.Origin == origin && e.Source == ForeignContestSource(f) && e.ResultBind == bind) == 1 &&
            CompleteProgramEventHistory().OfType<PindianResultDeterminedEvent>().Count(e =>
                e.FrameId == origin.PindianFrameId && e.SkillId == f.SkillId && e.Result == origin.Result) == 1;
    }
    private SkillProgramStepOutcome ResolveForeignTurnPindian(ProgramSkillFrame frame, string bind)
    {
        var f = GetActiveProgramFrame(frame.Id); var binding = f.PindianResultBindings.SingleOrDefault(b => b.Name == bind);
        if (f.ForeignTurnContest is not null || binding?.ForeignTurnOrigin is not { } origin ||
            !MatchesForeignTurnContestOrigin(f, bind, origin))
            throw new InvalidOperationException("A foreign turn contest cannot replay or replace its original result.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[_currentSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (origin.Result.SourceWon)
        {
            ReplaceRuntimeTop(f = f with { ForeignTurnContest = new(f.InstructionIndex, bind, origin,
                ForeignTurnContestStage.Complete, RestrictionIssued: true) });
            GrantProgramTurnCardTargetRestriction(f, SkillProgramCardTargetRestriction.SelfOnly, origin.Result.OpponentSeat);
            var grant = _turnCardUseEffects.TargetRestrictions.Single(r => r.ParentFrameId == f.Id && r.EffectIndex == f.InstructionIndex - 1);
            AdvanceEventRulesAndQueueFact(new ForeignTurnContestRestrictedEvent(f.Id, ForeignContestSource(f), origin, grant.GrantSequence));
            return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(f = f with { ForeignTurnContest = new(f.InstructionIndex, bind, origin, ForeignTurnContestStage.ClaimChildren),
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var id = origin.Result.OpponentCardId;
        if (_cardZones.GetLocation(id) == CardLocation.DiscardPile)
        {
            var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(c => c.Id == id);
            var before = _cardMovements.Count;
            MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new(ForeignTurnContestClaimReason));
            f = GetActiveProgramFrame(f.Id);
            var movement = _cardMovements.Skip(before).Single(m => m.CardId == id &&
                m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ForeignTurnContestClaimReason);
            ReplaceRuntimeTop(f = f with { ForeignTurnContest = f.ForeignTurnContest! with { ClaimedCardId = id, MovementSequence = movement.Sequence } });
            AdvanceEventRulesAndQueueFact(new ForeignTurnContestCardClaimedEvent(f.Id, ForeignContestSource(f), origin, id, movement.Sequence));
        }
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeForeignTurnContest(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.ForeignTurnContest is not { } r) return false;
        AssertForeignTurnContest(f);
        if (r.Stage == ForeignTurnContestStage.SlashIssued)
            throw new InvalidOperationException("A foreign virtual Slash returned without its exact typed receipt.");
        if (r.Stage == ForeignTurnContestStage.Complete) { FinishProgramSkill(f, true); return true; }
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id, subjectSeat: f.OwnerSeat)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.Origin.Result.OpponentSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !CanUseForeignTurnContestSlash(r.Origin.Result.OpponentSeat, f.OwnerSeat))
        { ReplaceRuntimeTop(f = f with { ForeignTurnContest = r with { Stage = ForeignTurnContestStage.Complete } }); FinishProgramSkill(f, false); return true; }
        BeginForeignTurnContestSlash(f); return true;
    }
    private bool CanUseForeignTurnContestSlash(int actor, int target) =>
        _players[actor].IsAlive && _players[target].IsAlive && actor != target &&
        !HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsCardUseForbidden(actor, CardKind.Slash, CardActionType.Use) &&
        !IsDirectedCardTargetProhibited(actor, target, CardKind.Slash) &&
        !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null) &&
        !HasBeneficiarySuitShield(actor, target, Suit.None) && !IsSlashProhibited(_players[target]) &&
        CanSpendSlashUse(_players[actor], _players[target], ignoresCount: true);
    private void BeginForeignTurnContestSlash(ProgramSkillFrame f)
    {
        var r = f.ForeignTurnContest!; var actor = _players[r.Origin.Result.OpponentSeat]; var target = _players[f.OwnerSeat];
        if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("A foreign Slash cannot overwrite a child attack.");
        var id = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId, CardActionType.Use,
            actor.Seat, actor.Seat, null, null, null, CardKind.Slash, [target.Seat], [], [],
            effectiveSuit: Suit.None, effectiveRank: 0));
        var returned = new ForeignTurnContestSlashReturn(f.Id, f.InstructionIndex, ForeignContestSource(f), r.Origin, id, action.ActionId);
        ReplaceRuntimeTop(f with { ForeignTurnContest = r with { Stage = ForeignTurnContestStage.SlashIssued, SlashReturn = returned } });
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, CardKind.Slash, [target.Seat], PhysicalCardIds: [])
        { Action = action, ForeignTurnContestSlashReturn = returned });
        AdvanceEventRulesAndQueueFact(new ForeignTurnContestSlashIssuedEvent(returned));
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.Slash, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [target.Seat]));
        RecordYingboCardUse(id, actor.Seat, CardKind.Slash); RecordProgramUsedBasicCard(actor.Seat, CardKind.Slash);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash); RecordActualPlayPhaseUse(action);
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card: null, damageAmount: actor.HasAlcoholEffect ? 2 : 1,
            playedCardKind: CardKind.Slash, ignoresArmor: HasCardArmorBypass(actor, target, CardKind.Slash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false; ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor.Seat, target.Seat));
        TryMarkProgramUseCommitted(id);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
    }
    private void CompleteForeignTurnContestSlash(AttackCompletionReceipt completion)
    {
        var returned = completion.ForeignTurnContestSlashReturn ?? throw new InvalidOperationException("Missing foreign Slash return.");
        var f = GetActiveProgramFrame(returned.ProgramFrameId); var r = f.ForeignTurnContest;
        if (completion.ProgramFrameId != f.Id || completion.ResolutionId != returned.CardUseFrameId ||
            r is not { Stage: ForeignTurnContestStage.SlashIssued } || r.SlashReturn != returned ||
            returned.Source != ForeignContestSource(f) || returned.Origin != r.Origin || returned.InstructionIndex != f.InstructionIndex ||
            _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == returned.CardUseFrameId) ||
            !CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e => e.ResolutionId == returned.CardUseFrameId))
            throw new InvalidOperationException("The foreign Slash completed outside its original program and paid result.");
        ReplaceRuntimeTop(f = f with { ForeignTurnContest = r with { Stage = ForeignTurnContestStage.Complete } });
        AdvanceEventRulesAndQueueFact(new ForeignTurnContestSlashReturnedEvent(returned));
        FinishProgramSkill(f, true);
    }
    private void AssertForeignTurnContest(ProgramSkillFrame f)
    {
        if (f.ForeignTurnContest is not { } r) return;
        if (r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 3 ||
            !MatchesForeignTurnContestOrigin(f, r.SourceBind, r.Origin) || !Enum.IsDefined(r.Stage) ||
            r.RestrictionIssued != r.Origin.Result.SourceWon ||
            r.RestrictionIssued && r.Stage != ForeignTurnContestStage.Complete ||
            r.ClaimedCardId is { } id && (id != r.Origin.Result.OpponentCardId || r.MovementSequence is not { } sequence ||
                !_cardMovements.Any(m => m.Sequence == sequence && m.CardId == id && m.From == CardLocation.DiscardPile &&
                    m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ForeignTurnContestClaimReason) ||
                CompleteProgramEventHistory().OfType<ForeignTurnContestCardClaimedEvent>().Count(e =>
                    e.ProgramFrameId == f.Id && e.Source == ForeignContestSource(f) && e.Origin == r.Origin && e.CardId == id && e.MovementSequence == sequence) != 1) ||
            r.ClaimedCardId is null && r.MovementSequence is not null)
            throw new InvalidOperationException("A foreign contest changed its exact original result, turn or paid claim.");
        if (r.RestrictionIssued && !CompleteProgramEventHistory().OfType<ForeignTurnContestRestrictedEvent>().Any(e =>
            e.ProgramFrameId == f.Id && e.Source == ForeignContestSource(f) && e.Origin == r.Origin &&
            _turnCardUseEffects.TargetRestrictions.Any(g => g.GrantSequence == e.GrantSequence && g.ParentFrameId == f.Id &&
                g.EffectIndex == f.InstructionIndex - 1 && g.SubjectSeat == r.Origin.Result.OpponentSeat &&
                g.Restriction == SkillProgramCardTargetRestriction.SelfOnly && g.TurnNumber == _turnNumber && g.TurnSeat == _currentSeat)))
            throw new InvalidOperationException("The foreign contest lost its actual-turn self-only issuance.");
        if (r.SlashReturn is { } returned && (returned.ProgramFrameId != f.Id || returned.InstructionIndex != f.InstructionIndex ||
            returned.Source != ForeignContestSource(f) || returned.Origin != r.Origin ||
            !CompleteProgramEventHistory().OfType<ForeignTurnContestSlashIssuedEvent>().Any(e => e.Return == returned)))
            throw new InvalidOperationException("A foreign contest lost its exact issued Slash child.");
    }
    private sealed partial class ProgramSkillHost : IForeignTurnContestAidProgramHost
    {
        public SkillProgramStepOutcome ResolveForeignTurnPindian(ProgramSkillFrame frame, string resultBind) =>
            engine.ResolveForeignTurnPindian(frame, resultBind);
        public SkillProgramStepOutcome OfferSameTypeDifferentNameOrExtraTarget(ProgramSkillFrame frame) =>
            engine.BeginSameTypeAid(frame);
    }
}
