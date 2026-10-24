namespace CardGame.Core;

internal interface IMoZhangFeiFacts : IProgramSelectionQuery
{
    bool Enabled { get; }
    bool SetupComplete { get; }
    Winner Winner { get; }
    int ActualTurn { get; }
    int TurnOwner { get; }
    int Round { get; }
    int PlayerCount { get; }
    bool Alive(int seat);
    int Hp(int seat);
    int MaximumHp(int seat);
    int HandCount(int seat);
    IReadOnlyList<string> ZhuohunGrantIds(int seat);
    bool AnyDying { get; }
    long MovementSequence { get; }
    IReadOnlyList<ProgramTriggerCandidate> Candidates(int seat, SkillProgramTriggerWindow window);
    SkillProgramTrigger Trigger(ProgramTriggerCandidate candidate);
    SkillProgram Program(string skillId);
    IEnumerable<T> History<T>() where T : IGameEvent;
    CardKind? DamageKind(long windowId);
    IReadOnlyList<Card> DrawPileSlashes(int maximum);
}
internal interface IMoZhangFeiOperations : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame frame);
    void Recover(ProgramSkillFrame frame, int amount);
    void LoseHp(ProgramSkillFrame frame, int seat, int amount);
    void Suppress(ProgramSkillFrame frame, int seat);
    void ObserveSkillRevisions();
    bool RemoveSkillGrant(int ownerSeat, string grantId);
    void GrantModifiedZhuohun(ProgramSkillFrame frame);
    void EnterRumo(ProgramSkillFrame frame);
    bool BeginRecovery(long frameId);
    bool BeginCharacterState(long frameId);
    bool BeginHpChanged(long frameId);
    bool BeginCardsMoved(long frameId);
    bool BeginSkillsChanged(long frameId);
    void GainSlash(ProgramSkillFrame frame, Card card, int recipientSeat);
    void CompleteDrawPileReplacement(ProgramSkillFrame frame, IReadOnlyList<int> ids, CardMoveReason reason, long before);
}

// Every mutable continuation belongs to ProgramSkillFrame. The three ports
// below carry scalar facts, native operations and the original frame store.
[ProgramRuntimeModule, ProgramCapability]
internal sealed partial class MoZhangFeiRuntime(IMoZhangFeiFacts facts, IMoZhangFeiOperations operations,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramOperationDraws draws,
    IProgramHealthChildQueries health, IProgramHealthChildOperations healthOperations) : IMoZhangFeiRuntime
{
    internal const string Zhuohun = "ol:mo-zhuohun";
    internal const string ModifiedZhuohun = "ol:mo-zhuohun-modified";
    internal const string Chenshi = "ol:mo-chenshi";
    internal const string ColorBinding = "first-color-arrival";
    internal const string GreenBinding = "issued-green-draw-replacement";
    internal const string GreenReason = "skill-program.ol:mo-zhuohun.green-slash-gain";
    internal static MoZhangFeiColor ColorOf(int hp) => hp >= 3 ? MoZhangFeiColor.Green : hp == 2 ? MoZhangFeiColor.Yellow : MoZhangFeiColor.Red;
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    internal bool IsModified(int owner) => facts.History<MoZhangFeiChenshiEnteredEvent>().Any(e => e.Source.OwnerSeat == owner);
    [ProgramTargetRule(ProgramTargetRulePoint.ExtraSlashTargets, 33700)]
    internal ProgramSlashTargetExpansion? ExtraSlashTargets(ProgramSlashTargetExpansionQuery q)
    {
        if (!facts.Enabled || q.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash) ||
            !(q.EffectiveIsRed == false || q.EffectiveIsRed is null && q.EffectiveSuit is Suit.Spade or Suit.Club) ||
            !facts.Alive(q.ActorSeat)) return null;
        var source = facts.Candidates(q.ActorSeat, SkillProgramTriggerWindow.AfterDamageApplied)
            .FirstOrDefault(c => c.SkillId == Chenshi && c.BindingId == "first-three-damage");
        if (source is null) return null;
        var color = ColorOf(facts.Hp(q.ActorSeat));
        var targets = q.LegalTargetSeats.Where(t => t != q.ActorSeat && facts.Alive(t) &&
            ColorOf(facts.Hp(t)) != color).Distinct().ToArray();
        return targets.Length == 0 ? null : new(new(source.SkillId, source.BindingId, source.OwnerSeat, source.SkillInstanceId),
            source.GameplayHash) { TargetSeats = targets };
    }
    // This recognizes a frozen accepted source during cold recovery. Current
    // liveness, skill possession and colors cannot revoke that original payment.
    [ProgramTargetRule(ProgramTargetRulePoint.ExtraSlashTargetSource, 33700)]
    internal bool RecognizesExtraSlashTargetSource(ProgramSlashTargetSourceQuery q) =>
        q.Source.SkillId == Chenshi && q.Source.BindingId == "first-three-damage" &&
        q.Source.OwnerSeat >= 0 && q.Source.OwnerSeat < facts.PlayerCount &&
        !string.IsNullOrWhiteSpace(q.Source.SkillInstanceId) && q.GameplayHash == facts.Program(Chenshi).GameplayHash;
    private static bool SameHealthChange(HpChangeContext a, HpChangeContext b) => a.Id == b.Id &&
        a.ParentFrameId == b.ParentFrameId && a.SourceSeat == b.SourceSeat && a.TargetSeat == b.TargetSeat &&
        a.Kind == b.Kind && a.Amount == b.Amount && a.HpBefore == b.HpBefore && a.HpAfter == b.HpAfter &&
        a.LossOccurrence == b.LossOccurrence && (a.FrozenLossCandidates is null ? b.FrozenLossCandidates is null :
            b.FrozenLossCandidates is not null && a.FrozenLossCandidates.SequenceEqual(b.FrozenLossCandidates));

    [ProgramPipelineHook(ProgramPipelinePhase.SlashTargetDuelSource, 33700)]
    internal CardUseEffectSource? SlashTargetDuelSource(CardUseFrame use, int targetSeat)
    {
        if (!facts.Enabled || use.Action is not { Type: CardActionType.Use } action ||
            use.CardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash) ||
            action.EffectiveKind != use.CardKind || action.ActorSeat != use.SourceSeat ||
            !(action.EffectiveSuit is Suit.Spade or Suit.Club && action.EffectiveIsRed != true ||
                action.EffectiveIsRed == false && action.PhysicalCards.Count > 0 && action.PhysicalCards.All(c => c.EffectiveIsRed == false)) ||
            targetSeat == use.SourceSeat || !facts.Alive(use.SourceSeat) || !facts.Alive(targetSeat) ||
            ColorOf(facts.Hp(use.SourceSeat)) != ColorOf(facts.Hp(targetSeat))) return null;
        var source = facts.Candidates(use.SourceSeat, SkillProgramTriggerWindow.AfterDamageApplied)
            .Concat(facts.Candidates(targetSeat, SkillProgramTriggerWindow.AfterDamageApplied))
            .FirstOrDefault(c => c.SkillId == Chenshi && c.BindingId == "first-three-damage");
        return source is null ? null : new(source.SkillId, source.BindingId, source.OwnerSeat, source.SkillInstanceId);
    }

    [ProgramPipelineHook(ProgramPipelinePhase.HealthChangeSubject, 33700)]
    internal bool PotentialHealthSubject(int targetSeat) => facts.Enabled && facts.SetupComplete &&
        facts.ActualTurn > 0 && facts.TurnOwner >= 0 && facts.TurnOwner < facts.PlayerCount && facts.Alive(facts.TurnOwner) &&
        facts.Candidates(facts.TurnOwner, SkillProgramTriggerWindow.AfterHealthChanged)
            .Any(c => c.SkillId is Zhuohun or ModifiedZhuohun && c.BindingId == ColorBinding);

    // Called at the native HP receipt boundary, before later children can
    // change HP again. Initial assignment and same-color changes do not enter.
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureHealthChange, 33700)]
    internal void CaptureHealth(HpChangeContext change)
    {
        if (!facts.Enabled || !facts.SetupComplete || facts.Winner != Winner.None || facts.ActualTurn < 1 ||
            facts.TurnOwner < 0 || facts.TurnOwner >= facts.PlayerCount || !facts.Alive(facts.TurnOwner) ||
            change.HpBefore == change.HpAfter || ColorOf(change.HpBefore) == ColorOf(change.HpAfter)) return;
        var color = ColorOf(change.HpAfter);
        foreach (var c in facts.Candidates(facts.TurnOwner, SkillProgramTriggerWindow.AfterHealthChanged)
                     .Where(c => c.SkillId is Zhuohun or ModifiedZhuohun && c.BindingId == ColorBinding))
        {
            if (facts.History<MoZhangFeiColorArrivedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat &&
                e.ActualTurn == facts.ActualTurn && e.Change.TargetSeat == change.TargetSeat && e.Color == color)) continue;
            events.AdvanceFact(new MoZhangFeiColorArrivedEvent(new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId),
                c.GameplayHash, facts.ActualTurn, facts.TurnOwner, change, color, c.SkillId == ModifiedZhuohun));
        }
    }
    internal IReadOnlyList<ProgramTriggerCandidate> HealthCandidates(HpChangeContext change) => Array.AsReadOnly(
        facts.History<MoZhangFeiColorArrivedEvent>().Where(e => SameHealthChange(e.Change, change))
            .Select(e => new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId,
                e.Source.SkillInstanceId, e.GameplayHash, 0)).ToArray());
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeHealthChangeCandidates, 33700)]
    internal IReadOnlyList<ProgramTriggerCandidate> IncludeHealthCandidates(HpChangeContext change,
        IReadOnlyList<ProgramTriggerCandidate> ordinary) => Array.AsReadOnly(ordinary
            .Where(c => !(c.SkillId is Zhuohun or ModifiedZhuohun && c.BindingId == ColorBinding))
            .Concat(HealthCandidates(change)).ToArray());
    internal ProgramSkillWindowContext HealthContext(HpChangeContext change, ProgramTriggerCandidate c,
        SkillProgramTriggerFacts triggerFacts) => new(SkillProgramTriggerWindow.AfterHealthChanged, change.Id,
            c.OwnerSeat, SourceSeat: change.SourceSeat, TargetSeat: change.TargetSeat, Amount: change.Amount,
            Facts: triggerFacts, HpChange: change);
    private MoZhangFeiColorArrivedEvent? ColorEntitlement(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        context.Window == SkillProgramTriggerWindow.AfterHealthChanged && context.HpChange is { } change
            ? facts.History<MoZhangFeiColorArrivedEvent>().SingleOrDefault(e => SameHealthChange(e.Change, change) &&
                e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && e.GameplayHash == c.GameplayHash)
            : null;
    private bool ChenshiEligible(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == Chenshi && c.BindingId == "first-three-damage" &&
        context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.Amount >= 3 &&
        (context.SourceSeat == c.OwnerSeat || context.TargetSeat == c.OwnerSeat) &&
        facts.DamageKind(context.ParentFrameId) is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash or CardKind.Duel &&
        !IsModified(c.OwnerSeat) && !facts.History<MoZhangFeiStartedEvent>().Any(e =>
            e.Receipt.Source.OwnerSeat == c.OwnerSeat && e.Receipt.Op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 33700,
        SkillProgramEffectOp.ResolveMoZhangFeiZhuohun, SkillProgramEffectOp.ResolveMoZhangFeiChenshi, SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw)]
    internal bool CanOffer(ProgramTriggerLegalityContext g) => g.Candidate.SkillId == Chenshi
        ? ChenshiEligible(g.Candidate, g.Context)
        : g.Candidate.BindingId == GreenBinding ? GreenEntitlement(g.Candidate, g.Context) is not null
        : ColorEntitlement(g.Candidate, g.Context) is not null;

    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MoZhangFei is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0);
        if (f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context)
            throw new InvalidOperationException("Mo Zhang Fei requires its exact native triggered operation.");
        var r = new MoZhangFeiReceipt(Source(f), f.GameplayHash, op, context.ParentFrameId,
            facts.ActualTurn, facts.Round, MoZhangFeiStage.Drawing);
        if (op == SkillProgramEffectOp.ResolveMoZhangFeiZhuohun)
        {
            var claim = ColorEntitlement(c, context) ?? throw new InvalidOperationException("Zhuohun lost its actual HP color arrival.");
            r = r with { ColorChangeId = claim.Change.Id, TargetSeat = claim.Change.TargetSeat, Color = claim.Color,
                Modified = claim.Modified, ActualTurn = claim.ActualTurn,
                Requested = claim.Modified ? Math.Max(0, facts.MaximumHp(f.OwnerSeat) - facts.HandCount(f.OwnerSeat)) : 1 };
        }
        else if (op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi)
        {
            if (!ChenshiEligible(c, context)) throw new InvalidOperationException("Chenshi requires the first one native Slash/Duel damage of at least three.");
            r = r with { Stage = MoZhangFeiStage.RecoveryChildren };
        }
        else
        {
            var grant = GreenEntitlement(c, context) ?? throw new InvalidOperationException("Zhuohun replacement lost its actual-turn green grant.");
            var draw = context.NativeDraw!;
            r = r with { Stage = MoZhangFeiStage.GainChildren, TargetSeat = draw.RecipientSeat,
                DrawAttemptId = draw.AttemptFrameId, Requested = draw.RequestedCount, GreenGrantFrameId = grant.FrameId,
                Before = facts.MovementSequence, After = facts.MovementSequence };
        }
        frames.ReplaceTop(f = f with { MoZhangFei = r });
        events.AdvanceFact(new MoZhangFeiStartedEvent(f.Id, r));
        if (op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi) operations.Recover(f, 1);
        else if (op == SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw) GainGreenSlashes(f);
        Continue(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool Drain(long id) => operations.BeginRecovery(id) || operations.BeginCharacterState(id) ||
        operations.BeginHpChanged(id) || operations.BeginCardsMoved(id) || operations.BeginSkillsChanged(id);
    private void Continue(long id)
    {
        if (frames.Top is not ProgramSkillFrame { MoZhangFei: { } r } f || f.Id != id) return;
        Assert(f);
        if (r.Stage == MoZhangFeiStage.Drawing)
        {
            frames.ReplaceTop(f = f with { MoZhangFei = r with { Stage = MoZhangFeiStage.DrawChildren } });
            draws.BeginOperationProgramDraw(f, f.OwnerSeat, r.Requested, new("skill-program.ol:mo-zhuohun.draw"));
            return;
        }
        if (Drain(id)) return;
        f = frames.ActiveProgram(id); r = f.MoZhangFei!;
        if (r.Stage == MoZhangFeiStage.DrawChildren)
        {
            if (r.DrawOutcome is null) throw new InvalidOperationException("Zhuohun resumed before its exact native draw returned.");
            frames.ReplaceTop(f = f with { MoZhangFei = r = r with { Stage = MoZhangFeiStage.ColorEffect } });
        }
        if (r.Stage == MoZhangFeiStage.ColorEffect)
        {
            frames.ReplaceTop(f = f with { MoZhangFei = r = r with { Stage = MoZhangFeiStage.EffectChildren } });
            if (r.Modified && facts.Alive(r.TargetSeat) && facts.Winner == Winner.None)
            {
                events.AdvanceFact(new MoZhangFeiColorEffectIssuedEvent(f.Id, r.Source, r.GameplayHash,
                    r.ActualTurn, r.TargetSeat, r.Color, r.ColorChangeId));
                if (r.Color == MoZhangFeiColor.Yellow) operations.Suppress(f, r.TargetSeat);
                else if (r.Color == MoZhangFeiColor.Red && !facts.AnyDying)
                {
                    var hp = facts.Hp(r.TargetSeat);
                    frames.ReplaceTop(f = f with { MoZhangFei = r = r with { RedRequestedHpLoss = Math.Max(0, hp), RedHpBefore = hp } });
                    events.AdvanceFact(new MoZhangFeiRedLossRequestedEvent(f.Id, r.TargetSeat, hp, Math.Max(0, hp)));
                    operations.LoseHp(f, r.TargetSeat, Math.Max(0, hp));
                }
            }
            if (frames.Top?.Id != id || Drain(id)) return;
        }
        else if (r.Stage == MoZhangFeiStage.RecoveryChildren)
        {
            frames.ReplaceTop(f = f with { MoZhangFei = r = r with { Stage = MoZhangFeiStage.Entering } });
            events.AdvanceFact(new MoZhangFeiChenshiEnteredEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.Round, r.WindowId));
            operations.ObserveSkillRevisions();
            foreach (var grant in facts.ZhuohunGrantIds(f.OwnerSeat))
                if (!operations.RemoveSkillGrant(f.OwnerSeat, grant))
                    throw new InvalidOperationException("Chenshi lost an original Zhuohun grant during its exact modification.");
            operations.GrantModifiedZhuohun(f);
            operations.EnterRumo(frames.ActiveProgram(id));
            if (frames.Top?.Id != id || Drain(id)) return;
        }
        Complete(frames.ActiveProgram(id));
    }
    private void Complete(ProgramSkillFrame f)
    {
        var r = f.MoZhangFei!;
        events.AdvanceFact(new MoZhangFeiCompletedEvent(f.Id, r.Op));
        frames.ReplaceTop(f = f with { MoZhangFei = null });
        operations.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 33700)]
    internal bool Resume(long id)
    {
        if (frames.Top is not ProgramSkillFrame { MoZhangFei: not null } f || f.Id != id) return false;
        Continue(id); return true;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMoZhangFeiZhuohun)]
    internal void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MoZhangFei is not { Stage: MoZhangFeiStage.DrawChildren, DrawOutcome: null } r ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Requested)
            throw new InvalidOperationException("Zhuohun draw return cannot change recipient or repay its request.");
        frames.ReplaceTop(f with { MoZhangFei = r with { DrawOutcome = outcome } });
        events.AdvanceFact(new MoZhangFeiDrawReturnedEvent(f.Id, outcome));
        Continue(f.Id);
    }

    internal MoZhangFeiColorEffectIssuedEvent? ActiveGreenGrant(int target) => facts.History<MoZhangFeiColorEffectIssuedEvent>()
        .LastOrDefault(e => e.Color == MoZhangFeiColor.Green && e.TargetSeat == target && e.ActualTurn == facts.ActualTurn);
    internal IReadOnlyList<ProgramTriggerCandidate> GreenCandidates(int recipientSeat)
    {
        if (ActiveGreenGrant(recipientSeat) is not { } grant) return [];
        return [new(recipientSeat, ModifiedZhuohun, GreenBinding, "issued:mo-zhuohun:" + grant.FrameId,
            facts.Program(ModifiedZhuohun).GameplayHash, 0)];
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IncludeNativeDrawCandidates, 33700)]
    internal IReadOnlyList<ProgramTriggerCandidate> IncludeGreenCandidates(int recipientSeat,
        IReadOnlyList<ProgramTriggerCandidate> ordinary) => Array.AsReadOnly(ordinary
            .Where(c => !(c.SkillId == ModifiedZhuohun && c.BindingId == GreenBinding))
            .Concat(GreenCandidates(recipientSeat)).ToArray());
    private MoZhangFeiColorEffectIssuedEvent? GreenEntitlement(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        context is { Window: SkillProgramTriggerWindow.NativeDrawStarting, NativeDraw: { } draw } &&
        ActiveGreenGrant(draw.RecipientSeat) is { } grant && c.SkillId == ModifiedZhuohun && c.BindingId == GreenBinding &&
        c.OwnerSeat == draw.RecipientSeat && c.SkillInstanceId == "issued:mo-zhuohun:" + grant.FrameId &&
        c.GameplayHash == facts.Program(ModifiedZhuohun).GameplayHash && draw.ActualTurnNumber == grant.ActualTurn ? grant : null;
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 33700)]
    internal bool IsIssuedCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.BindingId == GreenBinding && GreenEntitlement(c, context) is not null &&
            frames.Find(context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame window &&
            window.Window == SkillProgramTriggerWindow.NativeDrawStarting && window.CandidateIndex >= 0 &&
            window.CandidateIndex < window.Candidates.Count && window.Candidates[window.CandidateIndex] == c &&
            context.NativeDraw is { } native && frames.ParentOf(window.Id) is NativeDrawAttemptFrame attempt &&
            attempt.Id == native.AttemptFrameId && attempt.Context == native && attempt.ActiveWindowFrameId == window.Id)
            return true;
        return c.BindingId == ColorBinding && ColorEntitlement(c, context) is not null &&
            frames.Find(context.ParentFrameId) is HpChangedTriggerWindowFrame hp && hp.CandidateIndex >= 0 &&
            hp.CandidateIndex < hp.Candidates.Count && hp.Candidates[hp.CandidateIndex] == c &&
            context.HpChange is { } change && SameHealthChange(hp.Change, change) &&
            context.TargetSeat == change.TargetSeat && context.OwnerSeat == c.OwnerSeat && context.Amount == change.Amount;
    }
    internal bool IsIssuedGreenReplacement(ProgramSkillFrame f, NativeDrawAttemptContext native) =>
        f.MoZhangFei is { Op: SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw, Stage: MoZhangFeiStage.GainChildren } r &&
        Valid(f) && f.OwnerSeat == native.RecipientSeat && r.TargetSeat == native.RecipientSeat &&
        r.DrawAttemptId == native.AttemptFrameId && r.Requested == native.RequestedCount &&
        r.ActualTurn == native.ActualTurnNumber && f.WindowContext?.NativeDraw == native &&
        GreenEntitlement(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), f.WindowContext) is { } grant &&
        grant.FrameId == r.GreenGrantFrameId;
    internal NativeDrawPileSlashProof? GreenReplacementProof(ProgramSkillFrame f, NativeDrawAttemptContext context)
    {
        if (!IsIssuedGreenReplacement(f, context) || ActiveGreenGrant(context.RecipientSeat) is not { } grant)
            return null;
        return new(f.Id, grant.FrameId, grant.Source, grant.GameplayHash);
    }
    internal bool IsIssuedGreenReplacement(NativeDrawAttemptContext context, NativeDrawReplacementResult replacement)
    {
        if (replacement.DrawPileSlashProof is not { } proof || replacement.SourceKind != NativeDrawReplacementSourceKind.DrawPileSlash ||
            replacement.OwnerSeat != context.RecipientSeat || replacement.SkillId != ModifiedZhuohun ||
            replacement.BindingId != GreenBinding || replacement.SkillInstanceId != "issued:mo-zhuohun:" + proof.EntitlementFrameId ||
            replacement.Reason != new CardMoveReason(GreenReason) || replacement.GainedIds.Count > context.RequestedCount ||
            replacement.GainedIds.Distinct().Count() != replacement.GainedIds.Count ||
            !facts.History<NativeDrawAttemptStartedEvent>().Any(e => e.Context == context)) return false;
        var grants = facts.History<MoZhangFeiColorEffectIssuedEvent>().Where(e => e.FrameId == proof.EntitlementFrameId).ToArray();
        if (grants is not [var grant] || grant.Source != proof.EntitlementSource || grant.GameplayHash != proof.EntitlementGameplayHash ||
            grant.Source.SkillId != ModifiedZhuohun || grant.Color != MoZhangFeiColor.Green || grant.TargetSeat != context.RecipientSeat ||
            grant.ActualTurn != context.ActualTurnNumber || grant.Source.OwnerSeat != context.ActualTurnOwnerSeat ||
            facts.Program(ModifiedZhuohun).GameplayHash != grant.GameplayHash ||
            facts.History<MoZhangFeiStartedEvent>().Count(e => e.FrameId == grant.FrameId && e.Receipt.Source == grant.Source &&
                e.Receipt.GameplayHash == grant.GameplayHash && e.Receipt.Modified && e.Receipt.TargetSeat == grant.TargetSeat &&
                e.Receipt.Color == MoZhangFeiColor.Green && e.Receipt.ColorChangeId == grant.ChangeId) != 1) return false;
        var starts = facts.History<MoZhangFeiStartedEvent>().Where(e => e.FrameId == proof.ProgramFrameId).ToArray();
        if (starts is not [var start] || start.Receipt.Op != SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw ||
            start.Receipt.Source != new CardConversionSource(replacement.SkillId, replacement.BindingId, replacement.OwnerSeat, replacement.SkillInstanceId) ||
            start.Receipt.GameplayHash != facts.Program(ModifiedZhuohun).GameplayHash ||
            start.Receipt.DrawAttemptId != context.AttemptFrameId || start.Receipt.GreenGrantFrameId != grant.FrameId ||
            start.Receipt.TargetSeat != context.RecipientSeat || start.Receipt.ActualTurn != context.ActualTurnNumber ||
            start.Receipt.Requested != context.RequestedCount || start.Receipt.Before != replacement.SequenceBefore) return false;
        var materials = facts.History<MoZhangFeiGreenMaterialEvent>().Where(e => e.FrameId == proof.ProgramFrameId).ToArray();
        return materials.Select(e => e.CardId).SequenceEqual(replacement.GainedIds) && materials.All(e =>
            e.AttemptId == context.AttemptFrameId && e.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash &&
            e.From == CardLocation.DrawPile && e.To == CardLocation.Hand(context.RecipientSeat) && e.Reason == replacement.Reason &&
            e.Sequence > replacement.SequenceBefore && e.Sequence <= replacement.SequenceAfter);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.AdmitTurnSuppression, 33700)]
    internal bool IsIssuedYellow(ProgramSkillFrame f, int seat) => f.MoZhangFei is
        { Op: SkillProgramEffectOp.ResolveMoZhangFeiZhuohun, Stage: MoZhangFeiStage.EffectChildren,
            Modified: true, Color: MoZhangFeiColor.Yellow } r && r.TargetSeat == seat && Valid(f) &&
        facts.History<MoZhangFeiColorEffectIssuedEvent>().Count(e => e.FrameId == f.Id &&
            e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.TargetSeat == seat &&
            e.Color == r.Color && e.ChangeId == r.ColorChangeId && e.ActualTurn == r.ActualTurn) == 1;
    private void GainGreenSlashes(ProgramSkillFrame f)
    {
        var r = f.MoZhangFei!;
        var cards = facts.Alive(r.TargetSeat) ? facts.DrawPileSlashes(r.Requested) : [];
        foreach (var card in cards) operations.GainSlash(f, card, r.TargetSeat);
        f = frames.ActiveProgram(f.Id);
        frames.ReplaceTop(f = f with { MoZhangFei = r = r with { After = facts.MovementSequence, GainedIds = cards.Select(c => c.Id).ToArray() } });
        operations.CompleteDrawPileReplacement(f, r.GainedIds, new(GreenReason), r.Before);
        events.AdvanceFact(new MoZhangFeiGreenReplacementEvent(f.Id, r.GreenGrantFrameId!.Value, r.DrawAttemptId!.Value,
            r.TargetSeat, r.Requested, r.Before, r.After, r.GainedIds.Count));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33700)]
    internal bool RetainIssued(ProgramSkillFrame f) => f.MoZhangFei is not null ? Valid(f) :
        f.InstructionIndex is 0 or 1 && f.TriggerId is not null && f.TriggerId == f.ActivationId &&
        f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
        facts.Program(f.SkillId).GameplayHash == f.GameplayHash && f.WindowContext is { } context &&
        frames.ParentOf(f.Id)?.Id == context.ParentFrameId && IsIssuedCandidate(
            new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), context);
    private bool Valid(ProgramSkillFrame f)
    {
        if (f.MoZhangFei is not { } r || r.Source != Source(f) || r.GameplayHash != f.GameplayHash ||
            r.WindowId != f.WindowContext?.ParentFrameId || frames.ParentOf(f.Id)?.Id != r.WindowId ||
            f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) || r.Requested < 0 ||
            facts.Program(f.SkillId).GameplayHash != f.GameplayHash ||
            facts.History<MoZhangFeiStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            facts.History<MoZhangFeiCompletedEvent>().Any(e => e.FrameId == f.Id)) return false;
        var original = start.Receipt;
        if (original.Source != r.Source || original.GameplayHash != r.GameplayHash || original.Op != r.Op ||
            original.WindowId != r.WindowId || original.ActualTurn != r.ActualTurn || original.Round != r.Round ||
            original.ColorChangeId != r.ColorChangeId || original.TargetSeat != r.TargetSeat || original.Color != r.Color ||
            original.Modified != r.Modified || original.Requested != r.Requested || original.Before != r.Before ||
            original.DrawAttemptId != r.DrawAttemptId || original.GreenGrantFrameId != r.GreenGrantFrameId ||
            r.GainedIds is not System.Collections.IList { IsReadOnly: true } || r.GainedIds.Count > r.Requested ||
            r.GainedIds.Distinct().Count() != r.GainedIds.Count) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMoZhangFeiZhuohun)
        {
            var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0);
            var claim = ColorEntitlement(c, f.WindowContext!);
            if (r.Stage is MoZhangFeiStage.ColorEffect or MoZhangFeiStage.EffectChildren && r.DrawOutcome is null) return false;
            var redRequests = facts.History<MoZhangFeiRedLossRequestedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.RedRequestedHpLoss is { } loss ? r.Stage != MoZhangFeiStage.EffectChildren || !r.Modified ||
                    r.Color != MoZhangFeiColor.Red || loss < 0 || r.RedHpBefore != loss || redRequests is not [var red] ||
                    red != new MoZhangFeiRedLossRequestedEvent(f.Id, r.TargetSeat, loss, loss) :
                r.RedHpBefore is not null || redRequests.Length != 0) return false;
            return claim is not null && claim.Change.Id == r.ColorChangeId && claim.Color == r.Color &&
                claim.Change.TargetSeat == r.TargetSeat && claim.Modified == r.Modified && claim.ActualTurn == r.ActualTurn &&
                r.Stage is MoZhangFeiStage.Drawing or MoZhangFeiStage.DrawChildren or MoZhangFeiStage.ColorEffect or MoZhangFeiStage.EffectChildren &&
                (r.DrawOutcome is null ? !facts.History<MoZhangFeiDrawReturnedEvent>().Any(e => e.FrameId == f.Id) :
                    facts.History<MoZhangFeiDrawReturnedEvent>().Count(e => e.FrameId == f.Id &&
                        e.Outcome.RecipientSeat == f.OwnerSeat && e.Outcome.RequestedCount == r.Requested &&
                        e.Outcome.AttemptFrameId == r.DrawOutcome.AttemptFrameId &&
                        e.Outcome.SequenceBefore == r.DrawOutcome.SequenceBefore && e.Outcome.SequenceAfter == r.DrawOutcome.SequenceAfter &&
                        e.Outcome.ActualDrawIds.SequenceEqual(r.DrawOutcome.ActualDrawIds) &&
                        e.Outcome.ReplacementGainedIds.SequenceEqual(r.DrawOutcome.ReplacementGainedIds)) == 1);
        }
        if (r.Op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi)
            return (r.Stage == MoZhangFeiStage.Entering ?
                    facts.History<MoZhangFeiChenshiEnteredEvent>().Count(e => e.FrameId == f.Id &&
                        e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ActualTurn == r.ActualTurn &&
                        e.Round == r.Round && e.DamageWindowId == r.WindowId) == 1 :
                    !facts.History<MoZhangFeiChenshiEnteredEvent>().Any(e => e.FrameId == f.Id)) &&
                f.SkillId == Chenshi && f.TriggerId == "first-three-damage" &&
                r.Stage is MoZhangFeiStage.RecoveryChildren or MoZhangFeiStage.Entering &&
                f.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: >= 3 } damage &&
                (damage.SourceSeat == f.OwnerSeat || damage.TargetSeat == f.OwnerSeat) &&
                frames.Find(r.WindowId) is DamageTriggerWindowFrame { SourceCard: CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash or CardKind.Duel };
        return r.Op == SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw && r.Stage == MoZhangFeiStage.GainChildren &&
            f.WindowContext?.NativeDraw is { } draw && r.DrawAttemptId == draw.AttemptFrameId && r.TargetSeat == draw.RecipientSeat &&
            facts.History<MoZhangFeiColorEffectIssuedEvent>().Any(e => e.FrameId == r.GreenGrantFrameId &&
                e.Color == MoZhangFeiColor.Green && e.TargetSeat == r.TargetSeat && e.ActualTurn == r.ActualTurn);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33700)]
    internal void Assert(ProgramSkillFrame f)
    {
        if (f.MoZhangFei is not null && !Valid(f))
            throw new InvalidOperationException("Mo Zhang Fei lost its original frame, source, content or native window receipt.");
    }
    [ProgramPipelineHook(ProgramPipelinePhase.RumoOrigin, 33700)]
    internal bool RumoOrigin(long id, CardConversionSource source, string hash) => source.SkillId == Chenshi &&
        source.BindingId == "first-three-damage" && facts.Program(Chenshi).GameplayHash == hash &&
        facts.History<MoZhangFeiChenshiEnteredEvent>().Where(e => e.FrameId == id && e.Source == source && e.GameplayHash == hash)
            .ToArray() is [var entered] &&
        facts.History<MoZhangFeiStartedEvent>().Count(e => e.FrameId == id && e.Receipt.Source == source &&
            e.Receipt.GameplayHash == hash && e.Receipt.Op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi &&
            e.Receipt.WindowId == entered.DamageWindowId && e.Receipt.ActualTurn == entered.ActualTurn &&
            e.Receipt.Round == entered.Round) == 1;
    [ProgramPipelineHook(ProgramPipelinePhase.RumoEntry, 33700)]
    internal bool RumoEntry(ProgramSkillFrame f) => f.MoZhangFei is { Op: SkillProgramEffectOp.ResolveMoZhangFeiChenshi,
        Stage: MoZhangFeiStage.Entering } && Valid(f) && RumoOrigin(f.Id, Source(f), f.GameplayHash);
}
