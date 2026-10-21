using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouGuanPingRuntime(IMouGuanPingFacts facts, IMouGuanPingNative native, IProgramRuntimeFrames frames,
    IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouGuanPingRuntime
{
    internal const string Action = "mou-guan-ping", DrawReason = "skill-program.mou:wuwei.draw";
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 35000, SkillProgramEffectOp.ResolveMouGuanPingWuwei)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => facts.Use(c, w)?.MouGuanPing is { TypeCount: > 0 };
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouGuanPing is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1 || f.TriggerId != MouGuanPingComposition.Trigger || f.ActivationId != f.TriggerId || f.WindowContext is not { } w ||
            facts.Use(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w) is not { MouGuanPing: { } use } actual)
            throw new InvalidOperationException("Wuwei requires its original paid native Slash occurrence.");
        frames.ReplaceTop(f = f with { MouGuanPing = new(use, w.ParentFrameId, MouGuanPingStage.Choosing) { Targets = actual.TargetSeats } });
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if (f.MouGuanPing is not { } r || f.SkillId != MouGuanPingComposition.Skill || f.TriggerId != MouGuanPingComposition.Trigger || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.GameplayHash != facts.Program().GameplayHash || r.Use.GameplayHash != f.GameplayHash || r.Use.Source.SkillInstanceId != f.SkillInstanceId || r.Use.Source.OwnerSeat != f.OwnerSeat ||
            r.Use.TypeCount is < 1 or > 3 || r.Cursor < 0 || r.Cursor > r.Use.TypeCount || r.Mask < 0 || r.Mask > 7 || !Enum.IsDefined(r.Stage) ||
            f.WindowContext is not { } w || w.ParentFrameId != r.WindowId || frames.ParentOf(f.Id)?.Id != r.WindowId ||
            facts.Use(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w) is not { MouGuanPing: { } live } ||
            live != r.Use with { OptionMask = r.Mask }) return false;
        var selected = facts.History<MouGuanPingOptionEvent>().Where(e => e.FrameId == f.Id).ToArray();
        return selected.Length == r.Cursor && selected.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, r.Cursor)) &&
            selected.All(e => e.UseId == r.Use.UseId && e.ActionId == r.Use.ActionId && e.OwnerSeat == f.OwnerSeat && e.ActualTurn == r.Use.ActualTurn && e.TurnOwner == r.Use.TurnOwner && e.Option is >= 1 and <= 3) &&
            selected.Aggregate(0, (mask, e) => mask | 1 << (e.Option - 1)) == r.Mask;
    }
    private PromptChoice Option(ProgramSkillFrame f, int option, string label) => new(new ChoiceId($"mou-guan-ping.{f.Id}.{f.MouGuanPing!.Cursor}.{option}"), label, [], [],
        new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f) => [Option(f, 1, "摸一张牌"), Option(f, 2, "此杀目标本回合非锁定技能失效"), Option(f, 3, "本回合武威可发动次数+1"), Option(f, 0, "结束本次选择")];
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat, $"武威：还可选择 {f.MouGuanPing!.Use.TypeCount - f.MouGuanPing.Cursor} 次，可重复选择。", [], [], f.OwnerSeat)
        { PromptId = prompts.NextId(), Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(f.OwnerSeat)); native.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouGuanPing: { Stage: MouGuanPingStage.Choosing } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat ||
            Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } canonical || !canonical.Cards.SequenceEqual(chosen.Cards) || !canonical.Targets.SequenceEqual(chosen.Targets) ||
            canonical.Parameters.Count != chosen.Parameters.Count || canonical.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Wuwei requires its actual current option, original paid materials and selection cursor.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var option = int.Parse(chosen.Parameters["option"], CultureInfo.InvariantCulture);
        if (option == 0) { Complete(f); return; }
        var mask = r.Mask | 1 << (option - 1);
        events.AdvanceFact(new MouGuanPingOptionEvent(f.Id, r.Use.UseId, r.Use.ActionId, f.OwnerSeat, r.Use.ActualTurn, r.Use.TurnOwner, r.Cursor, option));
        frames.ReplaceTop(f = f with { MouGuanPing = r with { Cursor = r.Cursor + 1, Mask = mask, Stage = option == 1 ? MouGuanPingStage.Drawing : MouGuanPingStage.Children, Draw = null } });
        native.SetMask(f, mask);
        if (option == 1) { draws.BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(DrawReason)); return; }
        if (option == 2) foreach (var target in r.Targets.Where(facts.Alive)) native.Suppress(f, target);
        Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouGuanPing: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Wuwei lost its original paid Slash, native window or option cursor.");
            if (facts.Finished || !facts.Alive(f.OwnerSeat)) { Complete(f); return; }
            if (r.Stage == MouGuanPingStage.Drawing) return;
            if (r.Stage == MouGuanPingStage.Children)
            {
                if (native.Drain(f)) return;
                frames.ReplaceTop(f with { MouGuanPing = r with { Stage = MouGuanPingStage.Choosing }, PendingMovementContinuation = null }); continue;
            }
            if (r.Cursor >= r.Use.TypeCount) { Complete(f); return; }
            Publish(f); return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouGuanPingWuwei)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouGuanPing is not { Stage: MouGuanPingStage.Drawing, Draw: null } r || outcome.RequestedCount != 1 || outcome.RecipientSeat != f.OwnerSeat)
            throw new InvalidOperationException("Wuwei draw requires the exact once-issued native option draw.");
        frames.ReplaceTop(f = f with { MouGuanPing = r with { Stage = MouGuanPingStage.Children, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouGuanPingDrawReturnedEvent(f.Id, r.Cursor - 1, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    {
        var r = f.MouGuanPing!; events.AdvanceFact(new MouGuanPingCompletedEvent(f.Id, r.Use.UseId, r.Cursor, r.Mask));
        frames.ReplaceTop(f = f with { MouGuanPing = null, PendingMovementContinuation = null }); native.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 35000)]
    private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouGuanPing: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 35000)]
    private bool Movement(ProgramSkillFrame f) => f.MouGuanPing is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 35000)]
    private void Invariant(ProgramSkillFrame f) { if (f.MouGuanPing is not null && !Valid(f)) throw new InvalidOperationException("Invalid Wuwei original-use receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 35000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 35000)]
    private bool Paid(ProgramSkillFrame f) => f.MouGuanPing is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouGuanPingWuwei)]
    private PromptChoice Ai(PendingDecision prompt, ProgramSkillFrame f) => prompt.Choices.First(c => c.Parameters["option"] ==
        (f.MouGuanPing!.Use.TypeCount == 3 ? (f.MouGuanPing.Cursor + 1).ToString(CultureInfo.InvariantCulture) : "1"));
}
