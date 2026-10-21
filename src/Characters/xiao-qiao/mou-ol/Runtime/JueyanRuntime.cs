namespace CardGame.Core;

internal sealed partial class MouXiaoQiaoOlRuntime
{
    internal bool JueyanAvailable(int owner, int cardId)
    {
        if (!facts.Alive(owner) || facts.Location(cardId) != CardLocation.Hand(owner)) return false;
        var kind = facts.EffectiveKind(owner, cardId);
        var ordinary = IsOrdinary(kind); var redBasic = CardCatalog.Get(kind).CategoryName == "基本牌" && facts.EffectiveRed(owner, cardId) == true;
        return (ordinary || redBasic) && !facts.History<MouXiaoQiaoJueyanUsedEvent>().Any(e =>
            e.Source.OwnerSeat == owner && e.Round == facts.Round && e.RedBasic == redBasic);
    }
    internal static bool IsOrdinary(CardKind kind) => CardCatalog.Get(kind).CategoryName == "锦囊牌" &&
        kind is not (CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning);
    [ProgramSelectionModule(ProgramSelectionStage.CardAfterDeclaration, 34000, SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan)]
    internal sealed class JueyanCards : IProgramSelectionCardFilter
    {
        public bool Allows(ProgramSelectionContext c, Card card, IProgramSelectionQueries queries)
        {
            var runtime = queries.Get<IMouXiaoQiaoOlFacts>();
            var kind = runtime.EffectiveKind(c.OwnerSeat, card.Id);
            var redBasic = CardCatalog.Get(kind).CategoryName == "基本牌" && runtime.EffectiveRed(c.OwnerSeat, card.Id) == true;
            return runtime.Location(card.Id) == CardLocation.Hand(c.OwnerSeat) && (IsOrdinary(kind) || redBasic) &&
                !runtime.History<MouXiaoQiaoJueyanUsedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Round == runtime.Round && e.RedBasic == redBasic) &&
                runtime.Sources(c.OwnerSeat, c.SkillId).Any(source => runtime.NativePlans(c.OwnerSeat, kind, new(c.SkillId, c.Activation.Id, c.OwnerSeat, source.SkillInstanceId)).Count > 0);
        }
    }
    private void BeginJueyan(ProgramSkillFrame f)
    {
        if (f.SkillId != Jueyan || f.ActivationId != "retained-hand-use" || f.SelectedCardIds is not [var id] ||
            !JueyanAvailable(f.OwnerSeat, id)) throw new InvalidOperationException("Jueyan requires one currently legal retained Hand entity and an unspent Round branch.");
        var kind = facts.EffectiveKind(f.OwnerSeat, id); var redBasic = !IsOrdinary(kind);
        if (f.MouXiaoQiaoJueyanResponse is not null ? !facts.IsResponseRequest(f) : facts.NativePlans(f.OwnerSeat, kind, Source(f)).Count == 0)
            throw new InvalidOperationException("Jueyan cannot retain an otherwise unusable card.");
        var r = f.MouXiaoQiaoOl! with { Paid = true, Stage = MouXiaoQiaoOlStage.RevealChildren, SelectedCard = id,
            OutputKind = kind, ShownSuit = facts.EffectiveSuit(f.OwnerSeat, id), RedBasic = redBasic };
        frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r });
        events.AdvanceFact(new MouXiaoQiaoJueyanUsedEvent(f.Id, r.Source, f.GameplayHash, r.Round, id, kind, r.ShownSuit, redBasic));
        operations.Reveal(f, [id]); Continue(f.Id);
    }
    private void AfterJueyanReveal(ProgramSkillFrame f)
    {
        if (f.MouXiaoQiaoOl is not { Operation: SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan, Paid: true } r)
            throw new InvalidOperationException("Jueyan cannot issue a native Use without its exact retained-Hand public reveal.");
        if (f.MouXiaoQiaoJueyanResponse is not null)
        { events.AdvanceFact(new MouXiaoQiaoOlCompletedEvent(f.Id, r.Operation)); operations.ReturnResponse(f); return; }
        frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { Stage = MouXiaoQiaoOlStage.UseChoice } }); Publish(f);
    }
    [ProgramAnswerBefore(34000)]
    internal bool AnswerResponse(PromptChoice choice) => choice.Parameters.GetValueOrDefault("response") == "mou-xiao-qiao-jueyan" && operations.BeginResponse(choice);
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 34000)]
    internal void CapturePublicHandReveal(IGameEvent payload)
    {
        if (!facts.Enabled || !facts.SetupComplete || facts.Turn <= 0 || facts.Round <= 0) return;
        var (parentId, ids) = facts.PublicReveal(payload);
        if (ids.Count == 0 || frames.Find(parentId) is not { } parent) return;
        foreach (var owner in Enumerable.Range(0, facts.PlayerCount).Where(facts.Alive))
        {
            var source = facts.Sources(owner, Jueyan).FirstOrDefault(); if (source is null) continue;
            foreach (var card in facts.Hand(owner).Where(c => ids.Contains(c.Id)))
            {
                var suit = facts.EffectiveSuit(owner, card.Id);
                if (suit == Suit.None || facts.History<MouXiaoQiaoJueyanSuitClaimedEvent>().Any(e => e.Source.OwnerSeat == owner && e.Round == facts.Round && e.Suit == suit)) continue;
                var actualSource = new CardConversionSource(source.SkillId, source.BindingId, owner, source.SkillInstanceId);
                var claim = new PublicHandRevealClaim(parentId, actualSource, source.GameplayHash, facts.Round, suit, card.Id);
                parent = frames.Find(parentId)!;
                frames.Replace(parentId, parent with { PublicHandRevealClaims = (parent.PublicHandRevealClaims ?? []).Append(claim).ToArray() });
                events.AdvanceFact(new MouXiaoQiaoJueyanSuitClaimedEvent(parentId, actualSource, source.GameplayHash, facts.Round, suit, card.Id));
            }
        }
    }
    internal bool BeginPublicReveal(long parentId, PublicHandRevealReturnKind kind, string? returnKey = null)
    {
        if (frames.Top is not { PublicHandRevealClaims: { Count: > 0 } all } parent || parent.Id != parentId) return false;
        var claims = all.Where(q => !facts.History<PublicHandRevealClaimReturnedEvent>().Any(e => e.Claim == q)).ToArray();
        if (claims.Length == 0) return false;
        var window = new PublicHandRevealWindowFrame(frames.NextId(), parentId, kind) { Claims = claims, ReturnKey = returnKey };
        frames.Push(window); events.AdvanceFact(new PublicHandRevealWindowStartedEvent(window.Id, parentId, kind, claims.Length, returnKey));
        frames.Advance(window.Id); return true;
    }
    internal bool PublicRevealSuspends(long id)
    {
        foreach(var w in frames.OfType<PublicHandRevealWindowFrame>())
        {
            if(w.Claims.Count==0||w.ClaimIndex<0||w.ClaimIndex>w.Claims.Count||
                facts.History<PublicHandRevealWindowStartedEvent>().Count(e=>e==new PublicHandRevealWindowStartedEvent(w.Id,w.ParentFrameId,w.ReturnKind,w.Claims.Count,w.ReturnKey))!=1||
                w.Claims.Any(q=>q.RevealFrameId!=w.ParentFrameId||!ClaimWasIssued(q)))continue;
            for(var parent=frames.ParentOf(w.Id);parent is not null;parent=frames.ParentOf(parent.Id))if(parent.Id==id)return true;
        }
        return false;
    }
    internal bool RevealProducerSuspended(long id,string key)=>frames.ChildOf(id)is PublicHandRevealWindowFrame w&&w.ParentFrameId==id&&w.ReturnKind==PublicHandRevealReturnKind.Registered&&w.ReturnKey==key&&PublicRevealSuspends(id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,5)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,5)]
    internal bool PreservePublicRevealProducer(ProgramSkillFrame f)=>frames.ChildOf(f.Id)is PublicHandRevealWindowFrame w&&
        w.ParentFrameId==f.Id&&PublicRevealSuspends(f.Id)&&facts.Program(f.SkillId).GameplayHash==f.GameplayHash;
    [NativeFrameRoute(typeof(PublicHandRevealWindowFrame), 34000)]
    internal void AdvancePublicReveal(PublicHandRevealWindowFrame window)
    {
        if (frames.Top?.Id != window.Id || frames.ParentOf(window.Id)?.Id != window.ParentFrameId ||
            window.ClaimIndex < 0 || window.ClaimIndex > window.Claims.Count ||
            facts.History<PublicHandRevealWindowStartedEvent>().Count(e => e == new PublicHandRevealWindowStartedEvent(
                window.Id, window.ParentFrameId, window.ReturnKind, window.Claims.Count, window.ReturnKey)) != 1 ||
            window.ReturnKind == PublicHandRevealReturnKind.Registered && string.IsNullOrWhiteSpace(window.ReturnKey) ||
            window.Claims.Any(q => !(frames.Find(window.ParentFrameId)?.PublicHandRevealClaims ?? []).Contains(q)))
            throw new InvalidOperationException("A public Hand reveal lost its actual producer and exact reward cursor.");
        while (window.ClaimIndex < window.Claims.Count)
        {
            var q = window.Claims[window.ClaimIndex];
            if (q.RevealFrameId != window.ParentFrameId || !ClaimWasIssued(q)) throw new InvalidOperationException("A public reveal reward lacks its original Hand/suit receipt.");
            if (facts.Alive(q.Source.OwnerSeat) && facts.Winner == Winner.None)
            {
                var candidate = new ProgramTriggerCandidate(q.Source.OwnerSeat, q.Source.SkillId, q.Source.BindingId, q.Source.SkillInstanceId!, q.GameplayHash, 0);
                operations.BeginRevealTrigger(candidate, new(SkillProgramTriggerWindow.PublicHandRevealed, window.Id, candidate.OwnerSeat,
                    SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat) { PublicHandReveal = q }); return;
            }
            events.AdvanceFact(new PublicHandRevealClaimReturnedEvent(window.Id, window.ClaimIndex, q));
            frames.ReplaceTop(window = window with { ClaimIndex = window.ClaimIndex + 1 });
        }
        frames.Pop(window.Id, ResolutionFrameKind.PublicHandRevealWindow);
        events.AdvanceFact(new PublicHandRevealWindowReturnedEvent(window.Id, window.ParentFrameId));
        operations.ReturnRevealWindow(window);
    }
    private bool ClaimWasIssued(PublicHandRevealClaim q) => q.Source.SkillId == Jueyan && q.Source.BindingId == "public-hand-first-suit" &&
        facts.Program(Jueyan).GameplayHash == q.GameplayHash && facts.History<MouXiaoQiaoJueyanSuitClaimedEvent>().Count(e =>
            e == new MouXiaoQiaoJueyanSuitClaimedEvent(q.RevealFrameId, q.Source, q.GameplayHash, q.Round, q.Suit, q.CardId)) == 1;
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 34000)]
    internal bool IssuedRevealCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        context is { Window: SkillProgramTriggerWindow.PublicHandRevealed, PublicHandReveal: { } q } && ClaimWasIssued(q) &&
        c.OwnerSeat == q.Source.OwnerSeat && c.SkillId == q.Source.SkillId && c.BindingId == q.Source.BindingId && c.SkillInstanceId == q.Source.SkillInstanceId &&
        c.GameplayHash == q.GameplayHash && frames.Find(context.ParentFrameId) is PublicHandRevealWindowFrame w &&
        frames.ParentOf(w.Id)?.Id == q.RevealFrameId && w.ClaimIndex >= 0 && w.ClaimIndex < w.Claims.Count && w.Claims[w.ClaimIndex] == q;
    private SkillProgramStepOutcome BeginSuitReward(ProgramSkillFrame f)
    {
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0);
        if (f.WindowContext is not { } context || !IssuedRevealCandidate(c, context) || context.PublicHandReveal is not { } q)
            throw new InvalidOperationException("Jueyan's Draw1 requires its actual first public Hand-suit reveal.");
        var r = new MouXiaoQiaoOlReceipt(Source(f), f.GameplayHash, SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal, q.Round, context.ParentFrameId)
        { Stage = MouXiaoQiaoOlStage.Drawing, Paid = true, SelectedCard = q.CardId, ShownSuit = q.Suit };
        frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r });
        events.AdvanceFact(new MouXiaoQiaoOlStartedEvent(f.Id, r.Source, f.GameplayHash, r.Operation, r.Round, r.ParentFrameId));
        draws.BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(DrawReason)); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal)]
    internal void ReturnSuitDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MouXiaoQiaoOl is not { Operation: SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal, Stage: MouXiaoQiaoOlStage.Drawing, Draw: null } r ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1) throw new InvalidOperationException("Jueyan cannot repeat or alter a first-suit Draw1.");
        frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { Stage = MouXiaoQiaoOlStage.DrawChildren, Draw = outcome,
            Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouXiaoQiaoOlDrawReturnedEvent(f.Id, 1, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId));
        if (!Drain(f.Id)) Complete(frames.ActiveProgram(f.Id));
    }
    [ProgramWindowReturn(SkillProgramTriggerWindow.PublicHandRevealed)]
    internal void ReturnSuitReward(ProgramSkillFrame f, bool completed)
    {
        if (f.WindowContext?.PublicHandReveal is not { } q || frames.Top is not PublicHandRevealWindowFrame w ||
            w.Id != f.WindowContext.ParentFrameId || w.ClaimIndex >= w.Claims.Count || w.Claims[w.ClaimIndex] != q)
            throw new InvalidOperationException("Jueyan must return once to the original public reveal cursor.");
        events.AdvanceFact(new PublicHandRevealClaimReturnedEvent(w.Id, w.ClaimIndex, q));
        frames.ReplaceTop(w = w with { ClaimIndex = w.ClaimIndex + 1 }); frames.Advance(w.Id);
    }
}
