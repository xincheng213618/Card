namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasOtherActualBasicDiscardCapability =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawFromOtherActualBasicDiscard);
    private bool HasJudgedRankSlashCapability =>
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy);
    private static bool IsOtherActualBasicDiscardTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.DrawFromOtherActualBasicDiscard }];
    private bool OtherActualBasicDiscardAlreadyIssued(int owner, string skill) =>
        ProgramEventHistory<OtherActualBasicDiscardDrawIssuedEvent>().Any(e =>
            e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.ActualTurnNumber == _turnNumber &&
            e.ActualTurnOwnerSeat == _turnProgression.OwnerSeat);
    private int[] MatchingOtherActualBasicDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (batch.Id <= 0 || batch.TurnNumber != _turnNumber || batch.MovementTiming is not { } timing || !IsValidPlayerSeat(timing.ActualTurnOwnerSeat) ||
            timing.ActualTurnOwnerSeat != _turnProgression.OwnerSeat || timing.ActualTurnOwnerSeat == candidate.OwnerSeat ||
            OtherActualBasicDiscardAlreadyIssued(candidate.OwnerSeat, candidate.SkillId)) return [];
        return batch.Movements.Select((m, index) => (m, index)).Where(p =>
            p.m.TurnNumber == _turnNumber && _cardMovements.Contains(p.m) &&
            GetProgramDiscardSource(p.m) is { OwnerSeat: var other } && other is { } seat && seat != candidate.OwnerSeat &&
            MatchesSkillProgramCardCategory(p.m.CardKind, SkillProgramCardCategory.Basic)).Select(p => p.index).ToArray();
    }
    private bool CanRunOtherActualBasicDiscard(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        context.Window == SkillProgramTriggerWindow.DiscardPileReceived && context.MovementBatch is { } batch &&
        context.MovementIndex is { } index && MatchingOtherActualBasicDiscardIndexes(batch, candidate).Contains(index) &&
        context.SourceSeat == GetProgramDiscardSource(batch.Movements[index])?.OwnerSeat;
    private void DrawFromOriginalOtherBasicDiscard(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, 0);
        if (frame.InstructionIndex != 1 || frame.TriggerId is null || frame.WindowContext is not { } context ||
            !IsOtherActualBasicDiscardTrigger(GetProgramTrigger(frame)) || !CanRunOtherActualBasicDiscard(candidate, context))
            throw new InvalidOperationException("A basic discard benefit lost its original accepted actual-turn movement.");
        var batch = context.MovementBatch!; var movement = batch.Movements[context.MovementIndex!.Value];
        AdvanceEventRulesAndQueueFact(new OtherActualBasicDiscardDrawIssuedEvent(frame.Id, CreateProgramTurnEffectSource(frame),
            frame.GameplayHash, _turnNumber, batch.MovementTiming!.ActualTurnOwnerSeat, batch.Id, movement.Sequence,
            GetProgramDiscardSource(movement)!.Value.OwnerSeat!.Value, movement.CardId));
        // Issuance consumes the quota even if the deck is empty. Generic Draw owns all gain observers and returns.
        DrawProgramCards(frame.Id, frame.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private,
            new($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.DrawFromOtherActualBasicDiscard}"));
    }
    private void GrantOriginalJudgedRankSlashPolicy(ProgramSkillFrame supplied, string bind)
    {
        var frame = GetActiveProgramFrame(supplied.Id); var set = GetProgramCardSet(frame, bind);
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex != 2 || frame.WindowContext is not null || _phase != TurnPhase.Play || frame.OwnerSeat != _currentSeat ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            plan.Instructions is not [{ Op: SkillProgramEffectOp.StartJudgment, Target: SkillProgramEffectTarget.Owner, ResultBind: var result,
                JudgmentReason: var reason }, { Op: SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy, SourceBind: var input }] ||
            result != bind || input != bind || set.Visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException("A rank-split Slash policy requires its own completed owner judgment.");
        var fact = CompleteProgramEventHistory().OfType<JudgmentResolvedEvent>().Single(e => e.ParentResolutionId == frame.Id &&
            e.TargetSeat == frame.OwnerSeat && e.Reason == reason);
        if (fact.CardId is null && fact.Rank is null && set.CardIds.Count == 0) return;
        if (fact.CardId is not { } card || fact.Rank is not (>= 1 and <= 13) || fact.CardKind is null ||
            set.CardIds.Count > 1 || set.CardIds.Count == 1 && set.CardIds[0] != card ||
            CompleteProgramEventHistory().OfType<JudgmentRequestedEvent>().Count(e => e.ResolutionId == fact.ResolutionId &&
                e.ParentResolutionId == frame.Id && e.TargetSeat == frame.OwnerSeat && e.Reason == reason) != 1)
            throw new InvalidOperationException("A rank-split Slash policy replaced its finalized physical result.");
        var grant = _turnCardUseEffects.GrantJudgedRankSlashPolicy(_turnNumber, _turnProgression.OwnerSeat, frame.Id, 1,
            CreateProgramTurnEffectSource(frame), frame.GameplayHash, fact.ResolutionId, card, fact.Rank.Value);
        if (CompleteProgramEventHistory().OfType<TurnJudgedRankSlashPolicyGrantedEvent>().Any(e => e.Policy.ParentFrameId == frame.Id))
            throw new InvalidOperationException("One completed judgment cannot issue its rank policy twice.");
        AdvanceEventRulesAndQueueFact(new TurnJudgedRankSlashPolicyGrantedEvent(grant));
        // This operation consumes the temporary judgment binding. The mature cleanup moves only
        // its exact cards still in Processing; an earlier legitimate judgment claim keeps its card.
        // Generic post-instruction movement draining owns every resulting observer before completion.
        CleanupProgramBoundCards(frame, completed: true);
    }
    private TurnJudgedRankSlashPolicy? CurrentJudgedRankSlashPolicy(int actor) =>
        !HasJudgedRankSlashCapability || _winner != Winner.None || !_players[actor].IsAlive || _phase is TurnPhase.NotStarted or TurnPhase.Finished ? null :
        _turnCardUseEffects.JudgedRankSlashPolicies.Where(g => g.TurnNumber == _turnNumber && g.TurnSeat == _turnProgression.OwnerSeat &&
            g.Source.OwnerSeat == actor).OrderByDescending(g => g.GrantSequence).FirstOrDefault();
    private bool HasJudgedRankSlashDistance(int actor, CardKind kind, int? rank, long? existingUseFrameId = null) =>
        IsSlashCard(kind) && (existingUseFrameId is { } id
            ? HasIssuedJudgedRankSlashDistance(id, actor)
            : rank is > 0 && CurrentJudgedRankSlashPolicy(actor) is { } grant && rank < grant.Rank);
    private bool HasJudgedRankSlashQuota(int actor, CardKind kind, int? rank) =>
        IsSlashCard(kind) && rank is > 0 && CurrentJudgedRankSlashPolicy(actor) is { } grant && rank > grant.Rank;
    // Opening a public provider request does not inspect another player's Hand. Each disclosed payment
    // is checked again by IsSpecificRankProviderPaymentLegal with its actual frozen effective rank.
    private bool HasPotentialJudgedRankSlashQuota(int actor, CardKind kind) =>
        IsSlashCard(kind) && CurrentJudgedRankSlashPolicy(actor) is { Rank: < 13 };
    private JudgedRankSlashThresholdSnapshot? JudgedRankSlashSnapshot(int actor) => CurrentJudgedRankSlashPolicy(actor) is { } grant
        ? new(grant.TurnNumber, grant.Rank) : null;
    private void ObserveJudgedRankSlashUse(IGameEvent payload)
    {
        if (!HasJudgedRankSlashCapability || payload is not CardUseDeclaredEvent declared || !IsSlashCard(declared.CardKind)) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == declared.ResolutionId);
        if (use?.Action is not { } action || CurrentJudgedRankSlashPolicy(action.ActorSeat) is not { } policy) return;
        if (action.Type != CardActionType.Use || action.EffectiveKind != declared.CardKind || use.SourceSeat != declared.SourceSeat ||
            action.ActorSeat != use.SourceSeat || use.JudgedRankSlashUse is not null)
            throw new InvalidOperationException("A judged-rank Slash declaration lost its actual owning action.");
        var rank = action.EffectiveRank;
        var receipt = new JudgedRankSlashUseReceipt(use.Id, action.ActionId, action.ActorSeat, rank, policy,
            rank is > 0 && rank < policy.Rank, rank is > 0 && rank > policy.Rank);
        ReplaceRuntimeFrame(use.Id, use with { JudgedRankSlashUse = receipt });
        AdvanceEventRulesAndQueueFact(new JudgedRankSlashUsePolicyAppliedEvent(receipt));
    }
    private bool HasIssuedJudgedRankSlashDistance(long? useId, int actor) => useId is { } id &&
        LifecycleCardUse(id)?.JudgedRankSlashUse is { ActorSeat: var owner, IgnoresDistance: true } && owner == actor;
    private bool HasIssuedJudgedRankSlashQuota(long useId, int actor) =>
        LifecycleCardUse(useId)?.JudgedRankSlashUse is { ActorSeat: var owner, IgnoresQuota: true } && owner == actor;
    private void AssertJudgedRankSlashPolicies()
    {
        if (!HasJudgedRankSlashCapability && !HasOtherActualBasicDiscardCapability) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.JudgedRankSlashUse is not null))
        {
            var r = use.JudgedRankSlashUse!;
            if (use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
                r.CardUseFrameId != use.Id || r.CardActionId != action.ActionId ||
                !ShownEntityUseActorMatches(use, r.ActorSeat, action.ProviderSeat) ||
                r.EffectiveRank != action.EffectiveRank || r.ActorSeat != r.Policy.Source.OwnerSeat ||
                r.IgnoresDistance != (r.EffectiveRank is > 0 && r.EffectiveRank < r.Policy.Rank) ||
                r.IgnoresQuota != (r.EffectiveRank is > 0 && r.EffectiveRank > r.Policy.Rank) ||
                ProgramEventHistory<TurnJudgedRankSlashPolicyGrantedEvent>().Count(e => e.Policy == r.Policy) != 1 ||
                ProgramEventHistory<JudgedRankSlashUsePolicyAppliedEvent>().Count(e => e.Receipt == r) != 1)
                throw new InvalidOperationException("An issued rank-split Slash lost its frozen owner, rank or quota decision.");
        }
        foreach (var grant in _turnCardUseEffects.JudgedRankSlashPolicies)
        {
            var program = _contentRegistry.GetSkill(grant.Source.SkillId).Program;
            var activation = program?.Activations.SingleOrDefault(a => a.Id == grant.Source.BindingId);
            if (program?.GameplayHash != grant.GameplayHash || activation?.Effects is not
                    [{ Op: SkillProgramEffectOp.StartJudgment }, { Op: SkillProgramEffectOp.GrantJudgedRankSplitSlashTurnPolicy }] ||
                grant.EffectIndex != 1 || !IsValidPlayerSeat(grant.Source.OwnerSeat) || !IsValidPlayerSeat(grant.TurnSeat) ||
                ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == grant.ParentFrameId &&
                    e.OwnerSeat == grant.Source.OwnerSeat && e.SkillId == grant.Source.SkillId && e.ActivationId == grant.Source.BindingId) != 1 ||
                ProgramEventHistory<TurnJudgedRankSlashPolicyGrantedEvent>().Count(e => e.Policy == grant) != 1 ||
                ProgramEventHistory<JudgmentResolvedEvent>().Count(e => e.ResolutionId == grant.JudgmentFrameId &&
                    e.ParentResolutionId == grant.ParentFrameId && e.TargetSeat == grant.Source.OwnerSeat && e.CardId == grant.JudgmentCardId && e.Rank == grant.Rank) != 1)
                throw new InvalidOperationException("A judged-rank turn policy lost its exact issued public result.");
        }
        var issuedDraws = ProgramEventHistory<OtherActualBasicDiscardDrawIssuedEvent>();
        if (issuedDraws.Count == 0) return;
        if (issuedDraws
            .GroupBy(e => (e.Source.OwnerSeat, e.Source.SkillId, e.ActualTurnNumber, e.ActualTurnOwnerSeat)).Any(g => g.Count() != 1))
            throw new InvalidOperationException("A basic discard benefit issued more than once in its actual turn.");
        foreach (var issued in issuedDraws)
        {
            var program = _contentRegistry.GetSkill(issued.Source.SkillId).Program;
            var trigger = program?.Triggers.SingleOrDefault(t => t.Id == issued.Source.BindingId);
            var movement = _cardMovements.SingleOrDefault(m => m.Sequence == issued.MovementSequence);
            if (program?.GameplayHash != issued.GameplayHash || trigger is null || !IsOtherActualBasicDiscardTrigger(trigger) ||
                !IsValidPlayerSeat(issued.Source.OwnerSeat) || !IsValidPlayerSeat(issued.ActualTurnOwnerSeat) ||
                issued.ActualTurnOwnerSeat == issued.Source.OwnerSeat || issued.DiscardOwnerSeat == issued.Source.OwnerSeat ||
                movement is null || movement.CardId != issued.CardId || movement.TurnNumber != issued.ActualTurnNumber ||
                GetProgramDiscardSource(movement)?.OwnerSeat != issued.DiscardOwnerSeat ||
                !MatchesSkillProgramCardCategory(movement.CardKind, SkillProgramCardCategory.Basic) ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == issued.FrameId &&
                    e.OwnerSeat == issued.Source.OwnerSeat && e.SkillId == issued.Source.SkillId && e.BindingId == issued.Source.BindingId &&
                    e.SkillInstanceId == issued.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.DiscardPileReceived) != 1)
                throw new InvalidOperationException("An issued basic discard draw lost its exact physical origin and accepted binding.");
        }
    }
    private sealed partial class ProgramSkillHost : IJudgedRankSlashProgramHost
    {
        public void GrantJudgedRankSplitSlashTurnPolicy(ProgramSkillFrame frame, string bind) => engine.GrantOriginalJudgedRankSlashPolicy(frame, bind);
        public void DrawFromOtherActualBasicDiscard(ProgramSkillFrame frame) => engine.DrawFromOriginalOtherBasicDiscard(frame);
    }
}
