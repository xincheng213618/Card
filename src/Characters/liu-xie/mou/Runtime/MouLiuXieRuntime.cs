using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouLiuXieRuntime(IMouLiuXieFacts facts, IMouLiuXieNative native, IProgramRuntimeFrames frames,
    IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouLiuXieRuntime
{
    internal const string Action = "mou-liu-xie", DrawReason = "skill-program.mou:zhanban.draw", DiscardReason = "skill-program.mou:zhanban.discard";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private bool ChenshengQualified(int owner)
    {
        if (owner == facts.CurrentSeat || !facts.Alive(owner)) return false;
        var living = Enumerable.Range(0, facts.PlayerCount).Where(facts.Alive).ToArray();
        var maximum = living.Max(facts.HandCount);
        var maximumSeats = living.Where(s => facts.HandCount(s) == maximum).ToArray();
        return maximumSeats.Length != 1 || maximumSeats[0] != owner && maximumSeats[0] != facts.CurrentSeat;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 34800, SkillProgramEffectOp.PrepareMouLiuXieChensheng)]
    private bool CanTrigger(ProgramTriggerLegalityContext c) => ChenshengQualified(c.OwnerSeat);
    public SkillProgramStepOutcome PrepareChensheng(ProgramSkillFrame f)
    {
        if (!facts.ChenshengParent(f) || !ChenshengQualified(f.OwnerSeat))
            throw new InvalidOperationException("Chensheng requires the other actual turn's native ending and neither participant uniquely leading Hand count.");
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 34800, SkillProgramEffectOp.ResolveMouLiuXieZhanban)]
    private bool CanActivate(ProgramActivationLegalityContext c) => facts.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat &&
        !facts.History<MouLiuXieStartedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Phase == phase);
    [ProgramPhaseUsedProjection(34800)]
    private int UsedPhase(int owner, string skill, int fallback) => skill == MouLiuXieComposition.Zhanban && facts.Phase is { } phase
        ? Math.Max(fallback, facts.History<MouLiuXieStartedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Phase == phase)) : fallback;
    public SkillProgramStepOutcome Start(ProgramSkillFrame supplied)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouLiuXie is not null || !facts.ExactStart(f) || facts.Phase is not { } phase)
            throw new InvalidOperationException("Zhanban requires its original actual Play activation.");
        var participants = Enumerable.Range(1, facts.PlayerCount - 1).Select(n => (f.OwnerSeat + n) % facts.PlayerCount).Where(facts.Alive).ToArray();
        var lordCandidates = facts.LordQualified(f.OwnerSeat) ? participants.Where(facts.Qun).ToArray() : [];
        var receipt = new MouLiuXieReceipt(Source(f), f.GameplayHash, f.InstructionIndex, facts.Turn, phase)
        {
            Stage = lordCandidates.Length > 0 ? MouLiuXieStage.LordExclusions : MouLiuXieStage.OwnerCount,
            Participants = participants, LordCandidates = lordCandidates, Before = facts.MovementSequence, After = facts.MovementSequence
        };
        frames.ReplaceTop(f = f with { MouLiuXie = receipt });
        events.AdvanceFact(new MouLiuXieStartedEvent(f.Id, receipt.Source, receipt.GameplayHash, receipt.Instruction, receipt.ActualTurn, phase));
        Continue(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if (f.MouLiuXie is not { } r || f.SkillId != MouLiuXieComposition.Zhanban || f.ActivationId != "adjust-all-hands" || f.TriggerId is not null || f.WindowContext is not null ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Instruction != f.InstructionIndex || f.InstructionIndex != 1 || r.Source != Source(f) ||
            r.GameplayHash != f.GameplayHash || facts.Program(f.SkillId)?.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.NextStage) ||
            r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || r.Phase.ActorSeat != f.OwnerSeat || r.Cursor < 0 || r.Cursor > r.Participants.Count ||
            r.RewardCursor < 0 || r.RewardCursor > r.Adjustments.Count || r.TargetCount < -1 || r.Requested < 0 || r.Before < 0 || r.After < r.Before || r.After > facts.MovementSequence ||
            r.Participants.Distinct().Count() != r.Participants.Count || r.Participants.Contains(f.OwnerSeat) ||
            r.Excluded.Distinct().Count() != r.Excluded.Count || r.Excluded.Any(s => !r.LordCandidates.Contains(s)) ||
            r.LordCandidates.Any(s => !r.Participants.Contains(s)) || r.Selected.Distinct().Count() != r.Selected.Count || r.Paying.Distinct().Count() != r.Paying.Count ||
            r.Adjustments.Select(a => a.Seat).Distinct().Count() != r.Adjustments.Count || r.Adjustments.Any(a => !r.Participants.Contains(a.Seat) || !Enum.IsDefined(a.Kind) || a.ActualCount < 0)) return false;
        return facts.History<MouLiuXieStartedEvent>().Count(e => e == new MouLiuXieStartedEvent(f.Id, r.Source, r.GameplayHash, r.Instruction, r.ActualTurn, r.Phase)) == 1;
    }
    private int Chooser(ProgramSkillFrame f) => f.MouLiuXie is { Stage: MouLiuXieStage.SelectingDiscard } r ? r.Payer : f.OwnerSeat;
    private PromptChoice Option(ProgramSkillFrame f, string branch, string label, int value = -1, int? card = null, int? target = null) => new(
        new ChoiceId($"mou-liu-xie.{f.Id}.{f.MouLiuXie!.Stage}.{f.MouLiuXie.Cursor}.{f.MouLiuXie.RewardCursor}.{branch}.{value}.{card}.{target}"), label,
        card is { } id ? [id] : [], target is { } seat ? [seat] : [],
        new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["value"] = value.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouLiuXie!;
        if (r.Stage == MouLiuXieStage.LordExclusions)
            return r.LordCandidates.Select(s => Option(f, "exclude", (r.Excluded.Contains(s) ? "恢复" : "排除") + facts.Name(s), target: s))
                .Append(Option(f, "confirm", "确认天承排除名单")).ToArray();
        if (r.Stage == MouLiuXieStage.OwnerCount)
            return Enumerable.Range(0, 4).Select(n => Option(f, "draw", n == 0 ? "不摸不弃" : $"摸{n}张牌", n))
                .Concat(Enumerable.Range(1, Math.Min(3, facts.Discardable(f.OwnerSeat).Count)).Select(n => Option(f, "discard", $"弃置{n}张手牌", n))).ToArray();
        if (r.Stage == MouLiuXieStage.SelectingDiscard)
        {
            var choices = facts.Discardable(r.Payer).Where(id => r.Selected.Contains(id) || r.Selected.Count < r.Requested)
                .Select(id => Option(f, "card", (r.Selected.Contains(id) ? "取消" : "选择") + "弃置【" + facts.CardLabel(id) + "】", card: id)).ToList();
            if (r.Selected.Count == r.Requested) choices.Add(Option(f, "confirm", $"确认弃置{r.Selected.Count}张手牌"));
            return choices;
        }
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); var chooser = Chooser(f);
        if (choices.Count == 0) throw new InvalidOperationException("Zhanban lost its current native choice.");
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooser, f.MouLiuXie!.Stage switch
        {
            MouLiuXieStage.LordExclusions => "天承：选择本次不成为斩绊目标的群雄角色。",
            MouLiuXieStage.OwnerCount => "斩绊：先摸或弃至多三张手牌。",
            _ => $"斩绊：选择{f.MouLiuXie.Requested}张实际可弃置的手牌。"
        }, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(chooser));
        native.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouLiuXie: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) ||
            Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } canonical || !canonical.Cards.SequenceEqual(chosen.Cards) || !canonical.Targets.SequenceEqual(chosen.Targets) ||
            canonical.Parameters.Count != chosen.Parameters.Count || canonical.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Zhanban requires the original current chooser's published choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = chosen.Parameters["branch"];
        if (r.Stage == MouLiuXieStage.LordExclusions)
        {
            if (branch == "exclude")
            {
                var seat = chosen.Targets.Single();
                frames.ReplaceTop(f with { MouLiuXie = r with { Excluded = r.Excluded.Contains(seat) ? r.Excluded.Where(s => s != seat).ToArray() : r.Excluded.Append(seat).ToArray() } });
            }
            else
            {
                frames.ReplaceTop(f with { MouLiuXie = r with { Stage = MouLiuXieStage.OwnerCount } });
                foreach (var seat in r.Excluded) events.AdvanceFact(new MouLiuXieExcludedEvent(f.Id, seat));
            }
        }
        else if (r.Stage == MouLiuXieStage.OwnerCount)
        {
            var count = int.Parse(chosen.Parameters["value"], CultureInfo.InvariantCulture);
            if (branch == "draw")
            {
                if (count > 0) { BeginDraw(f, f.OwnerSeat, count, false, MouLiuXieStage.Adjust); return; }
                frames.ReplaceTop(f with { MouLiuXie = r with { Stage = MouLiuXieStage.Adjust } });
            }
            else { BeginDiscard(f, f.OwnerSeat, count, false, MouLiuXieStage.Adjust); return; }
        }
        else if (r.Stage == MouLiuXieStage.SelectingDiscard)
        {
            if (branch == "card")
            {
                var card = chosen.Cards.Single();
                frames.ReplaceTop(f with { MouLiuXie = r with { Selected = r.Selected.Contains(card) ? r.Selected.Where(id => id != card).ToArray() : r.Selected.Append(card).ToArray() } });
            }
            else
            {
                if (r.Selected.Any(id => !facts.Discardable(r.Payer).Contains(id))) throw new InvalidOperationException("Zhanban's actual Hand payment changed before acceptance.");
                var before = facts.MovementSequence;
                frames.ReplaceTop(f = f with { MouLiuXie = r with { Stage = MouLiuXieStage.DiscardChildren, Paying = r.Selected, Selected = [], Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                native.Discard(f);
                if (frames.Top?.Id != f.Id) return;
            }
        }
        Continue(f.Id);
    }
    private void BeginDiscard(ProgramSkillFrame f, int payer, int count, bool adjustment, MouLiuXieStage next)
    {
        var r = f.MouLiuXie!;
        var requested = Math.Min(Math.Max(0, count), facts.Discardable(payer).Count);
        if (requested == 0)
        {
            if (adjustment) RecordAdjustment(f, payer, MouLiuXieAdjustmentKind.None, 0);
            f = frames.ActiveProgram(f.Id);
            frames.ReplaceTop(f with { MouLiuXie = f.MouLiuXie! with { Stage = next } });
            Continue(f.Id); return;
        }
        frames.ReplaceTop(f = f with { MouLiuXie = r with { Stage = MouLiuXieStage.SelectingDiscard, Payer = payer, Requested = requested, IsAdjustment = adjustment, NextStage = next, Selected = [], Paying = [] } });
        Publish(f);
    }
    private void BeginDraw(ProgramSkillFrame f, int recipient, int count, bool adjustment, MouLiuXieStage next)
    {
        var r = f.MouLiuXie!;
        frames.ReplaceTop(f = f with { MouLiuXie = r with { Stage = MouLiuXieStage.Drawing, Payer = recipient, Requested = count, IsAdjustment = adjustment, NextStage = next, Draw = null }, PendingMovementContinuation = null });
        draws.BeginOperationProgramDraw(f, recipient, count, new(DrawReason));
    }
    private void RecordAdjustment(ProgramSkillFrame f, int seat, MouLiuXieAdjustmentKind kind, int count)
    {
        var r = f.MouLiuXie!;
        if (r.Adjustments.Any(a => a.Seat == seat)) throw new InvalidOperationException("Zhanban cannot classify the same adjustment twice.");
        frames.ReplaceTop(f with { MouLiuXie = r with { Adjustments = r.Adjustments.Append(new(seat, kind, count)).ToArray() } });
        events.AdvanceFact(new MouLiuXieAdjustedEvent(f.Id, seat, kind, count));
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouLiuXie: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Zhanban lost its original accepted phase, source or payment cursor.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage is MouLiuXieStage.LordExclusions or MouLiuXieStage.OwnerCount or MouLiuXieStage.SelectingDiscard)
            { Publish(f); return; }
            if (r.Stage == MouLiuXieStage.Drawing) return;
            if (r.Stage is MouLiuXieStage.DiscardChildren or MouLiuXieStage.DrawChildren)
            {
                if (native.Drain(f)) return;
                if (r.IsAdjustment)
                {
                    var count = r.Stage == MouLiuXieStage.DrawChildren ? r.Draw!.ActualDrawIds.Count :
                        facts.History<MouLiuXieDiscardPaidEvent>().Count(e => e.FrameId == f.Id && e.Payer == r.Payer && e.Adjustment && e.Sequence > r.Before && e.Sequence <= r.After);
                    RecordAdjustment(f, r.Payer, count == 0 ? MouLiuXieAdjustmentKind.None : r.Stage == MouLiuXieStage.DrawChildren ? MouLiuXieAdjustmentKind.Draw : MouLiuXieAdjustmentKind.Discard, count);
                    f = frames.ActiveProgram(id); r = f.MouLiuXie!;
                }
                frames.ReplaceTop(f with { MouLiuXie = r with { Stage = r.NextStage, Paying = [], Draw = null, IsAdjustment = false }, PendingMovementContinuation = null });
                continue;
            }
            if (r.Stage == MouLiuXieStage.Damage)
            {
                if (f.AttackAttempt is not null || native.Drain(f)) return;
                frames.ReplaceTop(f with { MouLiuXie = r with { Stage = MouLiuXieStage.Rewards }, PendingMovementContinuation = null }); continue;
            }
            if (r.Stage == MouLiuXieStage.Adjust)
            {
                if (r.TargetCount < 0)
                {
                    var targetCount = facts.HandCount(f.OwnerSeat);
                    frames.ReplaceTop(f with { MouLiuXie = r with { TargetCount = targetCount } });
                    events.AdvanceFact(new MouLiuXieTargetCountEvent(f.Id, targetCount)); continue;
                }
                if (r.Cursor >= r.Participants.Count)
                { frames.ReplaceTop(f with { MouLiuXie = r with { Stage = MouLiuXieStage.Rewards } }); continue; }
                var seat = r.Participants[r.Cursor];
                frames.ReplaceTop(f = f with { MouLiuXie = r with { Cursor = r.Cursor + 1 } });
                if (!facts.Alive(seat) || r.Excluded.Contains(seat)) continue;
                var difference = facts.HandCount(seat) - r.TargetCount;
                if (difference == 0) { RecordAdjustment(f, seat, MouLiuXieAdjustmentKind.Same, 0); continue; }
                if (difference > 0) BeginDiscard(f, seat, difference, true, MouLiuXieStage.Adjust);
                else BeginDraw(f, seat, -difference, true, MouLiuXieStage.Adjust);
                return;
            }
            if (r.Stage == MouLiuXieStage.Rewards)
            {
                if (r.RewardCursor >= r.Adjustments.Count) { Complete(f); return; }
                var adjustment = r.Adjustments[r.RewardCursor];
                frames.ReplaceTop(f = f with { MouLiuXie = r with { RewardCursor = r.RewardCursor + 1 } });
                if (!facts.Alive(adjustment.Seat) || adjustment.Kind == MouLiuXieAdjustmentKind.None) continue;
                if (adjustment.Kind == MouLiuXieAdjustmentKind.Discard) BeginDraw(f, adjustment.Seat, 3, false, MouLiuXieStage.Rewards);
                else if (adjustment.Kind == MouLiuXieAdjustmentKind.Draw) BeginDiscard(f, adjustment.Seat, 3, false, MouLiuXieStage.Rewards);
                else
                {
                    frames.ReplaceTop(f = f with { MouLiuXie = f.MouLiuXie! with { Stage = MouLiuXieStage.Damage, Payer = adjustment.Seat } });
                    events.AdvanceFact(new MouLiuXieDamageIssuedEvent(f.Id, f.OwnerSeat, adjustment.Seat));
                    native.Damage(f, adjustment.Seat);
                    if (frames.Top?.Id == f.Id) continue;
                }
                return;
            }
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouLiuXieZhanban)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouLiuXie is not { Stage: MouLiuXieStage.Drawing, Draw: null } r || outcome.RecipientSeat != r.Payer || outcome.RequestedCount != r.Requested)
            throw new InvalidOperationException("Zhanban must return the original exact recipient's native draw.");
        frames.ReplaceTop(f = f with { MouLiuXie = r with { Stage = MouLiuXieStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouLiuXieDrawReturnedEvent(f.Id, outcome.RecipientSeat, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId, r.IsAdjustment));
        Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MouLiuXieCompletedEvent(f.Id));
        frames.ReplaceTop(f = f with { MouLiuXie = null, PendingMovementContinuation = null }); native.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 34800)]
    private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouLiuXie: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 34800)]
    private bool Movement(ProgramSkillFrame f) => f.MouLiuXie is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 34800)]
    private void Invariant(ProgramSkillFrame f) { if (f.MouLiuXie is not null && !Valid(f)) throw new InvalidOperationException("Invalid Liu Xie owning adjustment receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 34800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 34800)]
    private bool Paid(ProgramSkillFrame f) => f.MouLiuXie is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouLiuXieZhanban)]
    private PromptChoice Ai(PendingDecision prompt, ProgramSkillFrame f)
    {
        var r = f.MouLiuXie!;
        if (r.Stage == MouLiuXieStage.LordExclusions)
            return prompt.Choices.FirstOrDefault(c => c.Targets is [var seat] && facts.Friendly(f.OwnerSeat, seat) && !r.Excluded.Contains(seat)) ?? prompt.Choices.Single(c => c.Parameters["branch"] == "confirm");
        if (r.Stage == MouLiuXieStage.OwnerCount)
            return prompt.Choices.Single(c => c.Parameters["branch"] == "draw" && c.Parameters["value"] == "3");
        if (r.Selected.Count == r.Requested) return prompt.Choices.Single(c => c.Parameters["branch"] == "confirm");
        return prompt.Choices.Where(c => c.Cards is [var id] && !r.Selected.Contains(id)).OrderBy(c => facts.KeepValue(r.Payer, c.Cards.Single())).First();
    }
}
