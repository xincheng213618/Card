using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouDianWeiRuntime(IMouDianWeiFacts facts, IMouDianWeiNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouDianWeiRuntime
{
    internal const string Action = "mou-dian-wei", DrawReason = "skill-program.mou:kuangzhan.draw";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    internal static string Bind(MouDianWeiReceipt r) => "kuangzhan-" + r.Index;
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouDianWei is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1 || facts.Program(f.SkillId)?.GameplayHash != f.GameplayHash || !facts.Parent(f, op))
            throw new InvalidOperationException("Mou Dian Wei requires his original actual activation or own full-turn boundary.");
        var start = op == SkillProgramEffectOp.ResolveMouDianWeiKangyongStart;
        var end = op == SkillProgramEffectOp.ResolveMouDianWeiKangyongEnd;
        var amount = end ? facts.History<MouDianWeiDebtIssuedEvent>().Where(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId && e.GameplayHash == f.GameplayHash && e.ActualTurn == facts.Turn && e.TurnOwner == facts.TurnOwner).Sum(e => e.ActualRecovered)
            : start ? Math.Max(0, facts.MaxHp(f.OwnerSeat) - facts.Hp(f.OwnerSeat)) : Math.Max(0, facts.MaxHp(f.OwnerSeat) - facts.Hand(f.OwnerSeat));
        if (end && facts.History<MouDianWeiDebtPaidEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId && e.ActualTurn == facts.Turn && e.TurnOwner == facts.TurnOwner))
            throw new InvalidOperationException("Kangyong cannot debit the same actual-turn recovery twice.");
        var r = new MouDianWeiReceipt(Source(f), f.GameplayHash, op, facts.Turn, facts.TurnOwner)
        { Stage = start ? MouDianWeiStage.RecoveryChildren : end ? MouDianWeiStage.LossChildren : MouDianWeiStage.Drawing, Requested = amount, HpBefore = facts.Hp(f.OwnerSeat), Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouDianWei = r });
        events.AdvanceFact(new MouDianWeiStartedEvent(f.Id, r.Source, f.GameplayHash, op, r.ActualTurn, r.TurnOwner, amount));
        if (start)
        {
            frames.ReplaceTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Recover(f); Continue(f.Id);
        }
        else if (end)
        {
            var lost = Math.Min(amount, Math.Max(0, facts.Hp(f.OwnerSeat) - 1));
            frames.ReplaceTop(f = f with { MouDianWei = r with { ActualLoss = lost }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            native.Lose(f, lost); events.AdvanceFact(new MouDianWeiDebtPaidEvent(f.Id, r.Source, r.ActualTurn, r.TurnOwner, amount, lost)); Continue(f.Id);
        }
        else draws.BeginOperationProgramDraw(f, f.OwnerSeat, amount, new(DrawReason));
        return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouDianWei is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 &&
        facts.Program(f.SkillId)?.GameplayHash == f.GameplayHash && facts.Parent(f, r.Operation) && Enum.IsDefined(r.Stage) && r.Requested >= 0 && r.X >= 0 && r.Index >= 0 && r.Index <= r.X &&
        r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence &&
        facts.History<MouDianWeiStartedEvent>().Count(e => e == new MouDianWeiStartedEvent(f.Id, r.Source, f.GameplayHash, r.Operation, r.ActualTurn, r.TurnOwner, r.Requested)) == 1;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f) => facts.Opponents(f.OwnerSeat).Select(seat => new PromptChoice(new ChoiceId($"mou-dian-wei.{f.Id}.{f.MouDianWei!.Index}.{seat}"),
        "与" + facts.Name(seat) + "拼点", [], [seat], new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray();
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f);
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat, $"狂战：选择第{f.MouDianWei!.Index + 1}/{f.MouDianWei.X}次拼点的角色。", [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(f.OwnerSeat)); native.Publish();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouDianWei: { Stage: MouDianWeiStage.Opponent } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat ||
            Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } canonical || !canonical.Cards.SequenceEqual(chosen.Cards) || !canonical.Targets.SequenceEqual(chosen.Targets) || canonical.Parameters.Count != chosen.Parameters.Count || canonical.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Kuangzhan requires its exact currently published native opponent choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MouDianWei = r with { Stage = MouDianWeiStage.Pindian, Opponent = chosen.Targets.Single() } }); native.Pindian(f);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouDianWei: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Dian Wei lost his actual parent, source or already paid cursor.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage == MouDianWeiStage.Drawing || r.Stage == MouDianWeiStage.Slash) return;
            if (r.Stage is MouDianWeiStage.DrawChildren or MouDianWeiStage.PindianChildren or MouDianWeiStage.ResultChildren or MouDianWeiStage.RecoveryChildren or MouDianWeiStage.LossChildren)
            {
                if (native.Drain(f)) return;
                frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage == MouDianWeiStage.RecoveryChildren)
                {
                    var recovered = f.RecoveryReceipt ?? throw new InvalidOperationException("Kangyong requires its actual native maximum-recovery receipt.");
                    if (recovered.TargetSeat != f.OwnerSeat || recovered.InstructionIndex != 1) throw new InvalidOperationException("Kangyong cannot borrow another recovery receipt.");
                    events.AdvanceFact(new MouDianWeiDebtIssuedEvent(f.Id, r.Source, f.GameplayHash, r.ActualTurn, r.TurnOwner, recovered.RecoveryFrameId, recovered.Recovered)); Complete(f); return;
                }
                if (r.Stage == MouDianWeiStage.LossChildren) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MouDianWei = r with { Stage = r.Stage == MouDianWeiStage.PindianChildren ? MouDianWeiStage.Result : MouDianWeiStage.Opponent } }); continue;
            }
            if (r.Stage == MouDianWeiStage.Opponent)
            {
                if (!facts.Alive(f.OwnerSeat) || r.Index >= r.X || Choices(f).Count == 0) { Complete(f); return; }
                Publish(f); return;
            }
            if (r.Stage == MouDianWeiStage.Pindian)
            {
                var result = facts.Result(f);
                events.AdvanceFact(new MouDianWeiContestReturnedEvent(f.Id, r.Index, r.ContestId, result.SourceWon));
                frames.ReplaceTop(f with { MouDianWei = r with { Stage = MouDianWeiStage.PindianChildren, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); continue;
            }
            if (r.Stage == MouDianWeiStage.Result)
            {
                var result = facts.Result(f);
                var actor = result.SourceWon ? f.OwnerSeat : result.OpponentSeat;
                var targets = (result.SourceWon ? facts.NotWon(f.OwnerSeat) : new[] { f.OwnerSeat }).Where(target => facts.CanSlash(actor, target)).ToArray();
                if (targets.Length > 0)
                {
                    frames.ReplaceTop(f = f with { MouDianWei = r with { Stage = MouDianWeiStage.Slash } }); native.Slash(f, actor, targets); return;
                }
                frames.ReplaceTop(f with { MouDianWei = r with { Stage = MouDianWeiStage.ResultChildren, Index = r.Index + 1, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); continue;
            }
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouDianWeiKuangzhan)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouDianWei is not { Stage: MouDianWeiStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.Requested)
            throw new InvalidOperationException("Kuangzhan cannot replay or substitute its actual draw-to-maximum receipt.");
        var x = outcome.ActualDrawIds.Concat(outcome.ReplacementGainedIds).Distinct().Count();
        frames.ReplaceTop(f = f with { MouDianWei = r with { Stage = MouDianWeiStage.DrawChildren, X = x, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouDianWeiDrawReturnedEvent(f.Id, r.Requested, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, x, outcome.AttemptFrameId)); Continue(f.Id);
    }
    internal void SlashReturned(ProgramSkillFrame f, MouDianWeiSlash returned)
    {
        if (!Valid(f) || f.MouDianWei is not { Stage: MouDianWeiStage.Slash } r || r.Slash != returned || returned.ProgramId != f.Id || returned.Source != r.Source || returned.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Kuangzhan Slash returned outside its exact actual comparison and immutable target set.");
        events.AdvanceFact(new MouDianWeiSlashReturnedEvent(f.Id, returned.UseId, returned.ActionId));
        frames.ReplaceTop(f with { MouDianWei = r with { Stage = MouDianWeiStage.ResultChildren, Index = r.Index + 1, Slash = null, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MouDianWeiCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouDianWei = null, PendingMovementContinuation = null }); native.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 35300)]
    private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouDianWei: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 35300)]
    private bool Movement(ProgramSkillFrame f) => f.MouDianWei is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 35300)]
    private void Invariant(ProgramSkillFrame f) { if (f.MouDianWei is not null && !Valid(f)) throw new InvalidOperationException("Invalid frame-owned Mou Dian Wei receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 35300)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 35300)]
    private bool Paid(ProgramSkillFrame f) => f.MouDianWei is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouDianWeiKuangzhan)]
    private PromptChoice Ai(PendingDecision prompt, ProgramSkillFrame f) => prompt.Choices.First();
}
