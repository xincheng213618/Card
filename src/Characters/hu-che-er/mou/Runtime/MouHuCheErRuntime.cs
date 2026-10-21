using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouHuCheErRuntime(IMouHuCheErFacts facts, IMouHuCheErNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouHuCheErHost
{
    internal const string MouHuCheErAction = "mou-hu-che-er-kongwu", MouHuCheErDiscardReason = "skill-program.mou:kongwu.discard", MouHuCheErDrawReason = "skill-program.mou:kongwu.draw";
    private static CardConversionSource MouHuCheErSource(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    internal bool CanStart(int owner) => facts.Phase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && facts.Recorded(p) && facts.Targets(owner).Count > 0;
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied) => BeginMouHuCheEr(supplied);
    private SkillProgramStepOutcome BeginMouHuCheEr(ProgramSkillFrame supplied)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouHuCheEr is not null || f.SkillId != MouHuCheErComposition.Kongwu || f.ActivationId != "show-hand" || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !CanStart(f.OwnerSeat) || facts.Phase() is not { } phase || !facts.Owns(f)) throw new InvalidOperationException("Kongwu requires its actual owner Play and an actual other Hand to reveal.");
        var r = new MouHuCheErReceipt(MouHuCheErSource(f), f.GameplayHash, phase) { Stage = MouHuCheErStage.Target, Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouHuCheEr = r }); events.AdvanceFact(new MouHuCheErStartedEvent(f.Id, r.Source, r.GameplayHash, phase)); PublishMouHuCheEr(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouHuCheEr is { } r && f.SkillId == MouHuCheErComposition.Kongwu && f.ActivationId == "show-hand" && f.TriggerId is null && f.WindowContext is null && f.InstructionIndex == 1 &&
        f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && r.Source == MouHuCheErSource(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == r.GameplayHash &&
        r.Phase.ActorSeat == f.OwnerSeat && r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && facts.Recorded(r.Phase) && r.Phase.TurnNumber == facts.Turn && Enum.IsDefined(r.Stage) && r.Count >= 0 && r.Index >= 0 && r.Index <= r.Count &&
        r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && facts.History<MouHuCheErStartedEvent>().Count(e => e == new MouHuCheErStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase)) == 1;
    private PromptChoice MouHuCheErChoice(ProgramSkillFrame f, string branch, string label, int? target = null, int slot = -1) => new(new($"mou-hu-che-er.{f.Id}.{f.MouHuCheEr!.Stage}.{target}.{slot}"), label, [], target is { } t ? [t] : [],
        new Dictionary<string,string> { ["program-action"] = MouHuCheErAction, ["branch"] = branch, ["slot-index"] = slot.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> MouHuCheErChoices(ProgramSkillFrame f)
    {
        var r = f.MouHuCheEr!;
        if (r.Stage == MouHuCheErStage.Target) return Array.AsReadOnly(facts.Targets(f.OwnerSeat).Select(t => MouHuCheErChoice(f, "target", "展示其一张手牌：" + facts.Name(t), t)).ToArray());
        if (r.Stage == MouHuCheErStage.HandSlot) return Array.AsReadOnly(r.Hand.Where(h => facts.InHand(r.Target, h.Id)).Select(h => MouHuCheErChoice(f, "hand", $"展示第{h.Slot + 1}张手牌", slot: h.Slot)).ToArray());
        return [];
    }
    private void PublishMouHuCheEr(ProgramSkillFrame f)
    {
        if (!Valid(f)) throw new InvalidOperationException("Kongwu lost its original actual phase or private Hand slots.");
        var choices = MouHuCheErChoices(f); if (choices.Count == 0) { FinishMouHuCheEr(f); return; }
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        var prompt = new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat, f.MouHuCheEr!.Stage == MouHuCheErStage.Target ? "孔武：选择一名有手牌的其他角色。" : "孔武：选择一个匿名手牌位置，公开展示该实体。", [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, TargetSeat = f.MouHuCheEr.Target >= 0 ? f.MouHuCheEr.Target : null, Choices = choices, SkillPrompt = new(f.SkillId, facts.SkillName(f.SkillId), facts.SkillName(f.SkillId), facts.SkillDescription(f.SkillId)) };
        prompts.Set(prompt, facts.Human(f.OwnerSeat)); native.PublishState();
    }
    private static bool SameChoice(PromptChoice a, PromptChoice b) => a.Id == b.Id && a.Cards.SequenceEqual(b.Cards) && a.Targets.SequenceEqual(b.Targets) && a.Parameters.Count == b.Parameters.Count && a.Parameters.All(p => b.Parameters.GetValueOrDefault(p.Key) == p.Value);
    [ProgramAnswer(MouHuCheErAction)] private void AnswerMouHuCheEr(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouHuCheEr: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat || !MouHuCheErChoices(f).Any(c => SameChoice(c, chosen))) throw new InvalidOperationException("Kongwu requires the current actual target or anonymous Hand position.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MouHuCheErStage.Target)
        {
            var target = chosen.Targets.Single(); var hand = facts.Hand(target);
            frames.ReplaceTop(f = f with { MouHuCheEr = r with { Target = target, Hand = hand, Stage = MouHuCheErStage.HandSlot } }); events.AdvanceFact(new MouHuCheErTargetEvent(f.Id, target)); PublishMouHuCheEr(f); return;
        }
        var card = r.Hand.Single(h => h.Slot == int.Parse(chosen.Parameters["slot-index"], CultureInfo.InvariantCulture));
        var category = facts.Category(card.Kind); var ids = facts.SameSuitDiscards(r.Target, card.Suit);
        frames.ReplaceTop(f = f with { MouHuCheEr = r with { Stage = MouHuCheErStage.DiscardChildren, Shown = card.Id, Suit = card.Suit, Category = category, Discards = ids, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        native.Reveal(f); if (ids.Count > 0) native.Discard(f);
        Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouHuCheEr: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Kongwu changed its original source, counted chain or native return.");
            if (facts.Finished) { FinishMouHuCheEr(f); return; }
            if (r.Stage is MouHuCheErStage.Target or MouHuCheErStage.HandSlot) { PublishMouHuCheEr(f); return; }
            if (r.Stage == MouHuCheErStage.DiscardChildren)
            {
                if (native.Drain(f)) return;
                var count = facts.Alive(r.Target) ? facts.CategoryCount(r.Target, r.Category) : 0;
                frames.ReplaceTop(f = f with { MouHuCheEr = r with { Count = count, Stage = MouHuCheErStage.SlashChildren }, PendingMovementContinuation = null });
                events.AdvanceFact(new MouHuCheErCountedEvent(f.Id, r.Target, count, r.Category)); continue;
            }
            if (r.Stage == MouHuCheErStage.NativeSlash) return;
            if (r.Stage == MouHuCheErStage.SlashChildren)
            {
                if (native.Drain(f)) return; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Index < r.Count && facts.Alive(f.OwnerSeat) && facts.Alive(r.Target))
                {
                    frames.ReplaceTop(f = f with { MouHuCheEr = r with { Stage = MouHuCheErStage.SlashIssuing, UseId = 0, ActionId = 0, Returned = false } });
                    if (native.CanSlash(f)) { native.Slash(f); return; }
                    frames.ReplaceTop(f with { MouHuCheEr = r with { Index = r.Count, Stage = MouHuCheErStage.SlashChildren } }); continue;
                }
                var draw = !r.Hurt && facts.Alive(f.OwnerSeat) ? Math.Max(0, facts.MaxHp(f.OwnerSeat) - facts.HandCount(f.OwnerSeat)) : 0;
                if (draw == 0) { FinishMouHuCheEr(f); return; }
                frames.ReplaceTop(f = f with { MouHuCheEr = r with { Stage = MouHuCheErStage.Drawing, DrawCount = draw } });
                draws.BeginOperationProgramDraw(f, f.OwnerSeat, draw, new(MouHuCheErDrawReason)); return;
            }
            if (r.Stage == MouHuCheErStage.Drawing) return;
            if (r.Stage == MouHuCheErStage.DrawChildren) { if (native.Drain(f)) return; FinishMouHuCheEr(frames.ActiveProgram(id)); return; }
            throw new InvalidOperationException("Kongwu has no legal continuation for this native cursor.");
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouHuCheErKongwu)] private void ReturnMouHuCheErDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouHuCheEr is not { Stage: MouHuCheErStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.DrawCount) throw new InvalidOperationException("Kongwu refill requires its original native draw return.");
        frames.ReplaceTop(f = f with { MouHuCheEr = r with { Stage = MouHuCheErStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouHuCheErDrawReturnedEvent(f.Id, r.DrawCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void FinishMouHuCheEr(ProgramSkillFrame f)
    {
        var r = f.MouHuCheEr!; events.AdvanceFact(new MouHuCheErCompletedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.Target, r.Hurt));
        frames.ReplaceTop(f = f with { MouHuCheEr = null, PendingMovementContinuation = null }); native.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 35800)] private bool ResumeMouHuCheEr(long id) { if (frames.Top is not ProgramSkillFrame { MouHuCheEr: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 35800)] private bool ReturnMouHuCheErMovement(ProgramSkillFrame f) => f.MouHuCheEr is not null && ResumeMouHuCheEr(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 35800)] private void AssertMouHuCheEr(ProgramSkillFrame f) { if (f.MouHuCheEr is not null && !Valid(f)) throw new InvalidOperationException("Invalid Kongwu actual phase receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 35800)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 35800)] private bool PaidMouHuCheEr(ProgramSkillFrame f) => f.MouHuCheEr is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuCheErKongwu)] private PromptChoice AiMouHuCheEr(PendingDecision p, ProgramSkillFrame f) => f.MouHuCheEr!.Stage == MouHuCheErStage.Target ? p.Choices.OrderBy(c => facts.Friendly(f.OwnerSeat, c.Targets.Single()) ? 1 : 0).First() : p.Choices.First();
    private IEnumerable<MouHuCheErDebtEvent> ActiveDebts(int target) => facts.History<MouHuCheErDebtEvent>().Where(d => d.Target == target && !facts.History<MouHuCheErDebtExpiredEvent>().Any(e => e.FrameId == d.FrameId));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 35800)] private void ObserveDamage(IGameEvent payload)
    {
        if (facts.Damage(payload) is not { } damage || frames.Find(damage.FrameId) is not ProgramSkillFrame { MouHuCheEr: { } r } f) return;
        frames.Replace(f.Id, f with { MouHuCheEr = r with { Hurt = true } }); events.AdvanceFact(damage);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 35801)] private void ObserveBoundaries(IGameEvent payload)
    {
        if (payload is ActualPhaseEndedEvent { Phase.Kind: ActualDiscardRecoveryPhaseKind.Play } ended)
        {
            foreach (var paid in facts.History<MouHuCheErCompletedEvent>().Where(e => e.Phase == ended.Phase && e.Target >= 0 && !facts.History<MouHuCheErPhaseSettledEvent>().Any(s => s.FrameId == e.FrameId)).ToArray())
            {
                var owner = paid.Source.OwnerSeat; var target = paid.Target;
                var applies = facts.Alive(owner) && facts.Alive(target) && facts.HasHp(owner) && facts.HasHp(target) && facts.Hp(target) <= facts.Hp(owner) && facts.HandCount(target) <= facts.HandCount(owner);
                events.AdvanceFact(new MouHuCheErPhaseSettledEvent(paid.FrameId, paid.Phase, paid.Target, applies));
                if (applies) events.AdvanceFact(new MouHuCheErDebtEvent(paid.FrameId, paid.Target, facts.Turn, paid.Phase.Token));
            }
        }
        if (payload is TurnStartedEvent start)
            foreach (var debt in ActiveDebts(start.ActorSeat).Where(d => !facts.History<MouHuCheErDebtTurnEvent>().Any(t => t.FrameId == d.FrameId)).ToArray())
                events.AdvanceFact(new MouHuCheErDebtTurnEvent(debt.FrameId, debt.Target, start.TurnNumber));
        if (payload is TurnEndedEvent end)
            foreach (var debt in ActiveDebts(end.ActorSeat).Where(d => facts.History<MouHuCheErDebtTurnEvent>().Any(t => t.FrameId == d.FrameId && t.Turn == end.TurnNumber && t.Target == end.ActorSeat)).ToArray())
                events.AdvanceFact(new MouHuCheErDebtExpiredEvent(debt.FrameId, debt.Target, end.TurnNumber));
    }
}
