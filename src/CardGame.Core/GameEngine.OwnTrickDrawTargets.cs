namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OwnTrickDrawReason = "program.own-multi-target-trick.draw";
    private static int ActualTrickTargetCount(CardUseFrame use) => (use.CardKind == CardKind.BorrowedSword
        ? use.TargetSeats.Where((_, i) => i % 2 == 0) : use.TargetSeats).Distinct().Count();
    private bool HasOwnMultiTargetTrickDraw => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick);
    private bool OwnMultiTargetTrickCandidate(ProgramTriggerCandidate c) => GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick);
    private bool HasOwnMultiTargetTrickSelfTarget(CardUseFrame use, int seat) => HasOwnMultiTargetTrickDraw &&
        use.SourceSeat == seat && ActualTrickTargetCount(use) > 1 && IsOrdinaryTrick(use.CardKind) &&
        EnabledSkillPrograms(_players[seat]).Any(p => p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick)));
    private bool CanOfferOwnTrickOrHandCategory(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick))
            return context.ActualUseTarget is { } identity && identity.TargetSeat == c.OwnerSeat &&
                LifecycleCardUse(identity.CardUseFrameId) is { Action.Type: CardActionType.Use } use &&
                IsOrdinaryTrick(use.CardKind) && ActualTrickTargetCount(use) > 1 && use.TargetSeats.Contains(c.OwnerSeat);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RestrictDamageSourceHandCategory))
            return context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat == c.OwnerSeat &&
                context.SourceSeat is { } source && IsValidPlayerSeat(source) && _players[source].IsAlive &&
                ActiveDamageTrigger is { } damage && damage.Id == context.ParentFrameId && damage.ParentFrameId == context.DamageFrameId &&
                GetDamageTriggerAttack(damage) is { IsSourceLess: false };
        return true;
    }
    private bool OwnTrickDrawMatches(ProgramSkillFrame root, ActualUseTargetWindowFrame parent)
    {
        if (root.OwnTrickDraw is not { } r || root.InstructionIndex != 1 || r.InstructionIndex != 0 ||
            root.WindowContext is not { Window: SkillProgramTriggerWindow.OtherActualUseTargeted, ActualUseTarget: { } identity } c ||
            c.ParentFrameId != parent.Id || parent.ParentFrameId != r.Use.CardUseFrameId || identity != r.Use ||
            identity.TargetSeat != root.OwnerSeat || !MatchesActualUseTarget(identity) ||
            r.Source != new CardConversionSource(root.SkillId, GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId) ||
            r.GameplayHash != root.GameplayHash || r.TargetCount < 2 || r.ActualDrawCount is < 0 or > 1 ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(root, parent.Candidates[parent.CandidateIndex]) || !OwnMultiTargetTrickCandidate(parent.Candidates[parent.CandidateIndex]) ||
            !IsOrdinaryTrick(identity.EffectiveKind)) return false;
        var records = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        bool Draw(CardMovementRecord m) => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == OwnTrickDrawReason;
        return records.Count(Draw) == r.ActualDrawCount && records.All(m => Draw(m) || m.From == CardLocation.DiscardPile &&
            m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle) &&
            CompleteProgramEventHistory().OfType<OwnTrickDrawIssuedEvent>().Count(e => e.ProgramFrameId == root.Id && e.Receipt == (r with { Applied = false })) == 1 &&
            (!r.Applied || CompleteProgramEventHistory().OfType<OwnTrickTargetNullifiedEvent>().Count(e => e.ProgramFrameId == root.Id &&
                e.CardUseFrameId == r.Use.CardUseFrameId && e.TargetSeat == root.OwnerSeat && e.Source == r.Source) == 1);
    }
    private SkillProgramStepOutcome BeginOwnTrickDraw(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.OwnTrickDraw is not null || f.InstructionIndex != 1 || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ActualUseTargetWindowFrame parent || f.WindowContext?.ActualUseTarget is not { } identity ||
            !CanRunActualUseTarget(parent.Candidates[parent.CandidateIndex], f.WindowContext) ||
            !CanOfferOwnTrickOrHandCategory(parent.Candidates[parent.CandidateIndex], GetProgramTrigger(parent.Candidates[parent.CandidateIndex]), f.WindowContext))
            throw new InvalidOperationException("An own trick draw requires its exact live multi-target use/candidate.");
        var before = PaidTargetMovementSequence;
        DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new(OwnTrickDrawReason));
        var after = PaidTargetMovementSequence;
        var actual = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == OwnTrickDrawReason);
        f = GetActiveProgramFrame(f.Id);
        var r = new ProgramOwnTrickDrawReceipt(0, identity, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, ActualTrickTargetCount(LifecycleCardUse(identity.CardUseFrameId)!), before, after, actual);
        ReplaceRuntimeTop(f with { OwnTrickDraw = r });
        AdvanceEventRulesAndQueueFact(new OwnTrickDrawIssuedEvent(f.Id, r));
        if (AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat) == SkillProgramStepOutcome.Continue) ResumeOwnTrickDraw(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeOwnTrickDraw(long id)
    {
        var f = GetActiveProgramFrame(id); if (f.OwnTrickDraw is not { } r) return false;
        if (_resolutionStack.Count < 2 || _resolutionStack[^2] is not ActualUseTargetWindowFrame parent || !OwnTrickDrawMatches(f, parent))
            throw new InvalidOperationException("Own-trick draw lost its exact original use and committed reward ledger.");
        if (f.PendingMovementContinuation is not null && AwaitProgramBoundCardMovements(id, f.OwnerSeat) != SkillProgramStepOutcome.Continue) return true;
        f = GetActiveProgramFrame(id);
        if (!r.Applied)
        {
            MarkCardEffectIneffective(r.Use.CardUseFrameId, r.Use.TargetSeat);
            ReplaceRuntimeTop(f with { OwnTrickDraw = r with { Applied = true } });
            AdvanceEventRulesAndQueueFact(new OwnTrickTargetNullifiedEvent(id, r.Use.CardUseFrameId, r.Use.TargetSeat, r.Source));
            AdvanceEventRulesAndQueueFact(new ProgramCardEffectNullifiedEvent(id, f.SkillId, GetProgramBindingId(f), f.OwnerSeat,
                r.Use.ActorSeat, r.Use.CardUseFrameId, r.Use.EffectiveKind));
        }
        FinishProgramSkill(GetActiveProgramFrame(id), completed: true); return true;
    }
    private bool IsOwnTrickDrawMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) =>
        e?.Op == SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick && f.OwnTrickDraw is not null &&
        m.SubjectSeat == f.OwnerSeat && m.BeforeCount == 0 && m.CoverageResultBind is null &&
        _resolutionStack.OfType<ActualUseTargetWindowFrame>().Any(p => OwnTrickDrawMatches(f, p));
    private bool OwnTrickDrawFirstChild(ProgramSkillFrame f, ResolutionFrame child) => f.OwnTrickDraw is { } r &&
        f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } m && m.SubjectSeat == f.OwnerSeat &&
        child is CardsMovedTriggerWindowFrame first && first.Batch.ParentFrameId == f.Id &&
        (first.Batch.AwaitingProgramFrameId is null || first.Batch.AwaitingProgramFrameId == f.Id) &&
        first.Batch.OriginSkillId == f.SkillId && first.Batch.OriginSkillInstanceId == f.SkillInstanceId && first.Batch.OriginOwnerSeat == f.OwnerSeat &&
        first.Batch.Movements.Count > 0 && first.Batch.Movements.All(x => _cardMovements.Contains(x) &&
            x.Sequence > r.SequenceBefore && x.Sequence <= r.SequenceAfter && x.From == CardLocation.DrawPile && x.To == CardLocation.Hand(f.OwnerSeat) && x.Reason.Value == OwnTrickDrawReason);
    private bool IsOwnTrickDrawProgramDying() => ActiveDying is { } dying && _resolutionStack.OfType<ActualUseTargetWindowFrame>()
        .Any(w => ActualUseTargetObserverRoot(w.ParentFrameId) is { OwnTrickDraw: not null } root &&
            _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    private sealed partial class ProgramSkillHost : IOwnTrickAndHandCategoryProgramHost
    {
        public SkillProgramStepOutcome DrawThenNullifyOwnMultiTargetTrick(ProgramSkillFrame f) => engine.BeginOwnTrickDraw(f);
        public void RestrictDamageSourceHandCategory(ProgramSkillFrame f, SkillProgramCardCategory category) => engine.GrantDamageSourceHandCategory(f, category);
    }
}
