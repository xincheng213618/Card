using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouSimaShiRuntime(IMouSimaShiFacts facts, IMouSimaShiNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts) : IMouSimaShiRuntime
{
    internal const string Sanshi = "mou:sanshi", Zhenrao = "mou:zhenrao", Chenlve = "mou:chenlve", Action = "mou-sima-shi-anniversary";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    internal static bool Reachable(CardLocation p) => p.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile or CardZoneKind.DiscardPile;
    internal IEnumerable<int> Marked(int owner) => facts.History<MouSimaShiMarkedEvent>().Where(e => e.Source.OwnerSeat == owner).Select(e => e.CardId).Distinct();
    internal bool HasMarked(int owner, IEnumerable<int> ids) => ids.Intersect(Marked(owner)).Any();
    private bool Expired(long debt) => facts.History<MouSimaShiDeathSettledEvent>().Any(e => e.DebtId == debt);
    private IReadOnlyList<MouSimaShiCard> DiscardCards(int owner)
    {
        var materials = facts.History<MouSimaShiMaterialEvent>().Where(m => m.Actor == owner).ToArray();
        return facts.Entities(facts.History<MouSimaShiDiscardEvent>().Where(e => e.Turn == facts.Turn && Marked(owner).Contains(e.CardId) &&
            !materials.Any(m => m.CardId == e.CardId && m.Kind == e.Kind && (m.DirectDiscard == e.Sequence || e.NativeFinish && m.ParentId == e.ParentId && m.ProcessingEntry > 0 && m.ProcessingEntry == e.ProcessingEntry))).Select(e => e.CardId).Distinct()).Where(c => c.From == CardLocation.DiscardPile).ToArray();
    }
    private IReadOnlyList<int> Targets(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (facts.Use(c, w) is not { } use) return [];
        var targets = use.Actor == c.OwnerSeat ? use.Targets : use.Targets.Contains(c.OwnerSeat) ? new[] { use.Actor } : [];
        return targets.Where(t => t != c.OwnerSeat && facts.Alive(t) && facts.HandCount(t) > facts.HandCount(c.OwnerSeat) && !facts.History<MouSimaShiTargetEvent>().Any(e => e.Owner == c.OwnerSeat && e.Turn == facts.Turn && e.Target == t)).Distinct().ToArray();
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 36400, SkillProgramEffectOp.InitializeMouSimaShiSanshi, SkillProgramEffectOp.RecoverMouSimaShiSanshi, SkillProgramEffectOp.ResolveMouSimaShiZhenrao, SkillProgramEffectOp.RemoveMouSimaShiChenlve, SkillProgramEffectOp.ReturnMouSimaShiChenlve)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => t.Effects.Single().Op switch
    {
        SkillProgramEffectOp.InitializeMouSimaShiSanshi => !facts.History<MouSimaShiInitializedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat),
        SkillProgramEffectOp.RecoverMouSimaShiSanshi => DiscardCards(c.OwnerSeat).Count > 0,
        SkillProgramEffectOp.ResolveMouSimaShiZhenrao => Targets(c, w).Count > 0,
        _ => facts.Due(c, w) is not null
    };
    internal bool CanActivate(int owner) => facts.Alive(owner) && facts.Phase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && !facts.History<MouSimaShiClaimIssuedEvent>().Any(e => e.Source.OwnerSeat == owner);
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouSimaShi is not null || f.InstructionIndex != 1 || !facts.Parent(f)) throw new InvalidOperationException("Mou Sima Shi needs his actual owning native activation or window.");
        var candidate = f.WindowContext is { } w ? new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex) : null;
        var due = candidate is null ? null : facts.Due(candidate, f.WindowContext!);
        var cards = operation switch
        {
            SkillProgramEffectOp.RecoverMouSimaShiSanshi => DiscardCards(f.OwnerSeat),
            SkillProgramEffectOp.ResolveMouSimaShiChenlve => facts.Entities(Marked(f.OwnerSeat)).Where(c => Reachable(c.From) && c.From != CardLocation.Hand(f.OwnerSeat)).ToArray(),
            SkillProgramEffectOp.RemoveMouSimaShiChenlve when due is not null && !Expired(due.FrameId) => facts.Entities(facts.History<MouSimaShiMovedEvent>().Where(e => e.FrameId == due.FrameId && e.Operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve && e.To == CardLocation.Hand(f.OwnerSeat)).Select(e => e.CardId).Distinct()).Where(c => Reachable(c.From)).ToArray(),
            SkillProgramEffectOp.ReturnMouSimaShiChenlve when due is not null => facts.Entities(facts.History<MouSimaShiRemovedEvent>().Where(e => e.DebtId == due.FrameId && facts.Quarantined(e)).Select(e => e.CardId).Distinct()).Where(c => c.From == CardLocation.OutsideGame).ToArray(),
            _ => Array.Empty<MouSimaShiCard>()
        };
        if (operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve && !CanActivate(f.OwnerSeat) || operation is SkillProgramEffectOp.RemoveMouSimaShiChenlve or SkillProgramEffectOp.ReturnMouSimaShiChenlve && due is null) throw new InvalidOperationException("Chenlve lost its original actual-Play entitlement.");
        var phase = due?.Phase ?? facts.Phase();
        var r = new MouSimaShiReceipt(Source(f), f.GameplayHash, operation, f.WindowContext?.ParentFrameId ?? 0, facts.Turn) { Stage = operation == SkillProgramEffectOp.ResolveMouSimaShiZhenrao ? MouSimaShiStage.Choosing : MouSimaShiStage.Moving, Cards = cards, Phase = phase, DebtId = due?.FrameId ?? f.Id, Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouSimaShi = r }); events.AdvanceFact(new MouSimaShiStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.Turn));
        if (operation == SkillProgramEffectOp.InitializeMouSimaShiSanshi)
        {
            foreach (var group in facts.Deck().Where(c => c.Rank is >= 1 and <= 13).GroupBy(c => c.Rank).OrderBy(g => g.Key))
            { var random = native.Random(group.ToArray()); events.AdvanceFact(new MouSimaShiMarkedEvent(f.Id, r.Source, f.GameplayHash, random.Id, group.Key, random.Before, random.After)); }
            events.AdvanceFact(new MouSimaShiInitializedEvent(f.Id, r.Source, f.GameplayHash)); Complete(f); return SkillProgramStepOutcome.AwaitChild;
        }
        if (operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve) events.AdvanceFact(new MouSimaShiClaimIssuedEvent(f.Id, r.Source, r.GameplayHash, phase!));
        if (operation == SkillProgramEffectOp.RemoveMouSimaShiChenlve) events.AdvanceFact(new MouSimaShiPhaseSettledEvent(r.DebtId, f.Id));
        if (operation == SkillProgramEffectOp.ReturnMouSimaShiChenlve) events.AdvanceFact(new MouSimaShiDeathSettledEvent(r.DebtId, f.Id));
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouSimaShi is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && r.ParentId == (f.WindowContext?.ParentFrameId ?? 0) && facts.Parent(f) && f.InstructionIndex == 1 && Enum.IsDefined(r.Stage) && r.Index >= 0 && r.Index <= r.Cards.Count && r.Cards.Select(c => c.Id).Distinct().Count() == r.Cards.Count && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && facts.History<MouSimaShiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.ParentId == r.ParentId && e.Turn == r.Turn) == 1;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f) => Targets(new(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, f.WindowContext!.OccurrenceIndex), f.WindowContext).Select(t => new PromptChoice(new($"mou-sima-shi.{f.Id}.target.{t}"), "震扰：对" + facts.Name(t) + "造成1点伤害", [], [t], new Dictionary<string, string> { ["program-action"] = Action, ["target"] = t.ToString(CultureInfo.InvariantCulture) })).ToArray();
    private void Publish(ProgramSkillFrame f)
    {
        var options = Choices(f); if (options.Count == 0) { Complete(f); return; }
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new(DecisionKind.ProgramTrigger, f.OwnerSeat, "震扰：选择一名本回合尚未因此受伤且手牌数大于你的角色。", [], options.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), Choices = Array.AsReadOnly(options.ToArray()), IsPrivate = false }, facts.Human(f.OwnerSeat)); native.Publish();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { MouSimaShi: { Stage: MouSimaShiStage.Choosing } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat || Choices(f).SingleOrDefault(c => c.Id == supplied.Id) is not { } canonical || !canonical.Targets.SequenceEqual(supplied.Targets) || supplied.Cards.Count != 0 || supplied.Parameters.Count != canonical.Parameters.Count || canonical.Parameters.Any(p => supplied.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Zhenrao requires its current exact native target choice.");
        var use = facts.Use(new(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, f.WindowContext!.OccurrenceIndex), f.WindowContext) ?? throw new InvalidOperationException("Zhenrao lost the original use.");
        prompts.Clear(); var target = supplied.Targets.Single();
        frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MouSimaShi = r with { Stage = MouSimaShiStage.Damage, Target = target } });
        events.AdvanceFact(new MouSimaShiTargetEvent(f.Id, f.OwnerSeat, facts.Turn, target, use.ActionId)); native.Damage(f); Continue(f.Id);
    }
    internal void MovesPaid(long id, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = frames.ActiveProgram(id); var r = f.MouSimaShi!;
        if (!Valid(f) || r.Stage != MouSimaShiStage.MovementChildren || moves is not [var m] || r.Index <= 0 || r.Cards[r.Index - 1] is not { } c || c.Id != m.CardId || c.Kind != m.CardKind || c.From != m.From || m.Sequence <= r.Before) throw new InvalidOperationException("Mou Sima Shi requires his exact issued physical movement return.");
        var expected = Destination(f); if (m.To != expected && m.To != CardLocation.OutsideGame) throw new InvalidOperationException("Mou Sima Shi movement changed destination.");
        frames.Replace(id, f with { MouSimaShi = r with { After = m.Sequence } }); events.AdvanceFact(new MouSimaShiMovedEvent(f.Id, r.Operation, r.DebtId, m.CardId, m.CardKind, m.From, m.To, m.Sequence));
        if (r.Operation == SkillProgramEffectOp.RemoveMouSimaShiChenlve && m.To == CardLocation.OutsideGame) events.AdvanceFact(new MouSimaShiRemovedEvent(r.DebtId, m.CardId, m.Sequence));
    }
    private static CardLocation Destination(ProgramSkillFrame f) => f.MouSimaShi!.Operation switch { SkillProgramEffectOp.RemoveMouSimaShiChenlve => CardLocation.OutsideGame, SkillProgramEffectOp.ReturnMouSimaShiChenlve => CardLocation.DrawPile, _ => CardLocation.Hand(f.OwnerSeat) };
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouSimaShi: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Sima Shi lost the owning native receipt.");
            if (r.Stage == MouSimaShiStage.Choosing) { Publish(f); return; }
            if (r.Stage == MouSimaShiStage.Damage) { if (f.AttackAttempt is not null || native.Drain(f)) return; Complete(f); return; }
            if (r.Stage == MouSimaShiStage.MovementChildren) { if (native.Drain(f)) return; frames.ReplaceTop(f with { PendingMovementContinuation = null, MouSimaShi = r with { Stage = MouSimaShiStage.Moving } }); continue; }
            if (r.Stage == MouSimaShiStage.Shuffle) { native.Shuffle(f); Complete(frames.ActiveProgram(id)); return; }
            if (r.Stage != MouSimaShiStage.Moving) return;
            if (r.Index >= r.Cards.Count)
            {
                if (r.Operation == SkillProgramEffectOp.ReturnMouSimaShiChenlve && facts.History<MouSimaShiMovedEvent>().Any(e => e.FrameId == f.Id && e.To == CardLocation.DrawPile)) { frames.ReplaceTop(f with { MouSimaShi = r with { Stage = MouSimaShiStage.Shuffle } }); continue; }
                Complete(f); return;
            }
            var card = r.Cards[r.Index]; var now = facts.Entities([card.Id]).SingleOrDefault();
            var isReturn = r.Operation == SkillProgramEffectOp.ReturnMouSimaShiChenlve;
            if (now is null || r.Operation == SkillProgramEffectOp.RemoveMouSimaShiChenlve && Expired(r.DebtId) || (isReturn ? now.From != CardLocation.OutsideGame || !facts.History<MouSimaShiRemovedEvent>().Any(e => e.DebtId == r.DebtId && e.CardId == card.Id && facts.Quarantined(e)) : !Reachable(now.From)) || r.Operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve && now.From == CardLocation.Hand(f.OwnerSeat) || !isReturn && r.Operation != SkillProgramEffectOp.RemoveMouSimaShiChenlve && !facts.Alive(f.OwnerSeat)) { frames.ReplaceTop(f with { MouSimaShi = r with { Index = r.Index + 1 } }); continue; }
            var current = r.Cards.ToArray(); current[r.Index] = now;
            frames.ReplaceTop(f = f with { MouSimaShi = r with { Cards = current, Index = r.Index + 1, Stage = MouSimaShiStage.MovementChildren, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            native.Move(f, now, Destination(f));
        }
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouSimaShiCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouSimaShi = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 36400)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouSimaShi: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 36400)] private bool Movement(ProgramSkillFrame f) => f.MouSimaShi is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 36400)] private void Invariant(ProgramSkillFrame f) { if (f.MouSimaShi is not null && !Valid(f)) throw new InvalidOperationException("Invalid Mou Sima Shi owning receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 36400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 36400)] private bool Paid(ProgramSkillFrame f)
    {
        if (f.MouSimaShi is not null) return Valid(f);
        // The native window already owns the original accepted obligation, even
        // before its resolver has installed the moving receipt.
        return f.SkillId == Chenlve && f.TriggerId is "remove-issued-entities" or "death-return-shuffle" && f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && facts.Parent(f) && facts.Due(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex), w) is not null;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouSimaShiZhenrao)] private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderBy(c => facts.Friendly(f.OwnerSeat, c.Targets.Single()) ? 1 : 0).First();
}
