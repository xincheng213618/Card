using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouCaoShuangAnniversaryRuntime(IMouCaoShuangAnniversaryFacts facts, IMouCaoShuangAnniversaryNative native,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws, IProgramAwakeningOverrideQuery awakeningConditions) : IMouCaoShuangAnniversaryRuntime
{
    internal const string Jianzhuan = "mou:jianzhuan", Fanshi = "mou:fanshi", Fudou = "mou:fudou", Action = "mou-cao-shuang-anniversary", DiscardReason = "skill-program.mou:jianzhuan.discard", DrawReason = "skill-program.mou:jianzhuan.draw";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    internal static int[] Items(int mask) => Enumerable.Range(1, 4).Where(i => (mask & 1 << (i - 1)) != 0).ToArray();
    internal int Remaining(int owner) => facts.History<MouCaoShuangAnniversaryDeletedEvent>().Where(e => e.Owner == owner).Aggregate(15, (mask, e) => mask & ~(1 << (e.Option - 1)));
    private MouCaoShuangAnniversaryOptionEvent[] PhaseUses(int owner) => facts.History<MouCaoShuangAnniversaryOptionEvent>().Where(e => e.Owner == owner && e.Turn == facts.Turn && e.PhaseId == facts.PhaseId).ToArray();
    private int[] Available(int owner) => Items(Remaining(owner)).Except(PhaseUses(owner).Select(e => e.Option)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 35500, SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan, SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption, SkillProgramEffectOp.ResolveMouCaoShuangFanshi, SkillProgramEffectOp.ResolveMouCaoShuangFudou)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        var op = t.Effects.Single().Op;
        if (op == SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan) return facts.ActualPlay(c.OwnerSeat) && Available(c.OwnerSeat).Length > 0 && facts.Use(c, w) is not null;
        if (op == SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption) return c.OwnerSeat == facts.CurrentSeat && Items(Remaining(c.OwnerSeat)).Length > 0 && Items(Remaining(c.OwnerSeat)).All(i => PhaseUses(c.OwnerSeat).Any(e => e.Option == i)) &&
            !facts.History<MouCaoShuangAnniversaryDeletedEvent>().Any(e => e.Owner == c.OwnerSeat && e.Turn == facts.Turn && e.PhaseId == facts.PhaseId);
        if (op == SkillProgramEffectOp.ResolveMouCaoShuangFanshi) return (Items(Remaining(c.OwnerSeat)).Length < 2 || awakeningConditions.Granted(c.OwnerSeat,c.SkillId,c.BindingId)) && !facts.History<MouCaoShuangAnniversaryAwakenedEvent>().Any(e => e.Owner == c.OwnerSeat);
        var use = facts.Use(c, w); return use is { Other: >= 0, Red: { } red } && (red ? !facts.Hurt(use.Other, c.OwnerSeat) : facts.Hurt(use.Other, c.OwnerSeat));
    }
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouCaoShuangAnniversary is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.TriggerId is null || f.TriggerId != f.ActivationId || f.InstructionIndex != 1 || f.WindowContext is not { } w || !facts.Parent(f) || !Qualifies(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), facts.Program(f.SkillId).Triggers.Single(t => t.Id == f.TriggerId), w))
            throw new InvalidOperationException("Mou Cao Shuang requires his exact current native Use, actual phase or Ending occurrence.");
        var use = operation is SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan or SkillProgramEffectOp.ResolveMouCaoShuangFudou ? facts.Use(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w) : null;
        var x = operation == SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan ? PhaseUses(f.OwnerSeat).Length + 1 : 1;
        var stage = operation == SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan ? MouCaoShuangAnniversaryStage.Option : operation == SkillProgramEffectOp.ResolveMouCaoShuangFanshi ? MouCaoShuangAnniversaryStage.NextRepeat : MouCaoShuangAnniversaryStage.Fudou;
        var r = new MouCaoShuangAnniversaryReceipt(Source(f), f.GameplayHash, operation, w.ParentFrameId, facts.Turn, facts.PhaseId, Remaining(f.OwnerSeat), x, use?.Action.ActionId ?? 0, use?.UseId ?? 0, use?.Other ?? -1, use?.Red == true)
        { Stage = stage, Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r });
        events.AdvanceFact(new MouCaoShuangAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.Turn, r.PhaseId, r.Remaining, r.X, r.ActionId, r.UseId, r.Other, r.Red));
        if (operation == SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption) { native.Delete(f); Complete(f); return SkillProgramStepOutcome.AwaitChild; }
        if (operation == SkillProgramEffectOp.ResolveMouCaoShuangFanshi) events.AdvanceFact(new MouCaoShuangAnniversaryAwakenedEvent(f.Id, f.OwnerSeat, r.Remaining));
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouCaoShuangAnniversary is { } r && f.InstructionIndex == 1 && f.TriggerId == f.ActivationId && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash &&
        r.ParentId == f.WindowContext?.ParentFrameId && frames.ParentOf(f.Id)?.Id == r.ParentId && r.Turn == facts.Turn && r.PhaseId == facts.PhaseId && Enum.IsDefined(r.Stage) && r.Repeat >= 0 && r.Repeat <= 3 * Math.Max(1,Items(r.Remaining).Length) && r.Cursor is >= 0 and <= 2 &&
        r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && facts.Parent(f) &&
        (r.Operation != SkillProgramEffectOp.ResolveMouCaoShuangFanshi || Items(r.Remaining).Length < 2 || awakeningConditions.Issued(f)) &&
        facts.History<MouCaoShuangAnniversaryStartedEvent>().Count(e => e == new MouCaoShuangAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.Turn, r.PhaseId, r.Remaining, r.X, r.ActionId, r.UseId, r.Other, r.Red)) == 1;
    private int Chooser(ProgramSkillFrame f) => f.MouCaoShuangAnniversary is { Stage: MouCaoShuangAnniversaryStage.Cards } r ? r.Payer : f.OwnerSeat;
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int? card = null, int? target = null, int item = 0) => new(new ChoiceId($"mou-cao-shuang-anniversary.{f.Id}.{f.MouCaoShuangAnniversary!.Stage}.{f.MouCaoShuangAnniversary.Repeat}.{f.MouCaoShuangAnniversary.Cards.Count}.{branch}.{card}.{target}.{item}"), label,
        card is { } c ? [c] : [], target is { } t ? [t] : [], new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["item"] = item.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouCaoShuangAnniversary!;
        if (r.Stage == MouCaoShuangAnniversaryStage.Option) return Available(f.OwnerSeat).Select(i => Choice(f, "option", i switch { 1 => $"令一名角色弃{r.X}张牌", 2 => $"摸{r.X}张牌", 3 => $"重铸{r.X}张手牌", _ => $"弃{r.X}张牌" }, item: i)).ToArray();
        if (r.Stage == MouCaoShuangAnniversaryStage.Target) return facts.Living().Select(t => Choice(f, "target", "令其弃牌：" + facts.Name(t), target: t)).ToArray();
        if (r.Stage == MouCaoShuangAnniversaryStage.Cards) return facts.Payable(r.Payer, r.Option == 3).Where(c => !r.Cards.Any(s => s.Id == c.Id)).Select(c => Choice(f, "card", (r.Option == 3 ? "重铸【" : "弃置【") + c.Label + "】", c.Id)).ToArray();
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); var chooser = Chooser(f); frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooser, "渐专：按本次已冻结的X执行选项，不足数量时尽量执行。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(chooser)); native.PublishState();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouCaoShuangAnniversary: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) || Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } c ||
            !c.Cards.SequenceEqual(chosen.Cards) || !c.Targets.SequenceEqual(chosen.Targets) || c.Parameters.Count != chosen.Parameters.Count || c.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Jianzhuan requires the current actual chooser and exact published item or entity.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MouCaoShuangAnniversaryStage.Option)
        {
            var item = int.Parse(chosen.Parameters["item"], CultureInfo.InvariantCulture);
            events.AdvanceFact(new MouCaoShuangAnniversaryOptionEvent(f.Id, f.OwnerSeat, r.Turn, r.PhaseId, item, r.X)); BeginItem(f, item); return;
        }
        if (r.Stage == MouCaoShuangAnniversaryStage.Target) { PrepareCards(f, chosen.Targets.Single()); return; }
        var card = facts.Payable(r.Payer, r.Option == 3).Single(x => x.Id == chosen.Cards.Single());
        frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Cards = r.Cards.Append(card).ToArray() } }); Continue(f.Id);
    }
    private void BeginItem(ProgramSkillFrame f, int item)
    {
        var r = f.MouCaoShuangAnniversary!;
        frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Option = item, Payer = f.OwnerSeat, Cards = [], Draw = null, Required = 0, Before = facts.Sequence, After = facts.Sequence } });
        if (item == 1) { frames.ReplaceTop(f with { MouCaoShuangAnniversary = f.MouCaoShuangAnniversary! with { Stage = MouCaoShuangAnniversaryStage.Target } }); Continue(f.Id); return; }
        if (item == 2) { BeginDraw(f, f.OwnerSeat, r.X, false); return; }
        PrepareCards(f, f.OwnerSeat);
    }
    private void PrepareCards(ProgramSkillFrame f, int payer)
    {
        var r = f.MouCaoShuangAnniversary!; var count = Math.Min(r.X, facts.Payable(payer, r.Option == 3).Count);
        frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Payer = payer, Required = count, Stage = MouCaoShuangAnniversaryStage.Cards, Cards = [] } }); Continue(f.Id);
    }
    private void BeginDraw(ProgramSkillFrame f, int target, int count, bool recast)
    {
        if (count <= 0 || !facts.Alive(target)) { ItemDone(f, 0); return; }
        var r = f.MouCaoShuangAnniversary!;
        frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.Drawing, Payer = target, Required = count, Draw = null } });
        draws.BeginOperationProgramDraw(f, target, count, recast ? CardMoveReasons.RecastDraw : new(DrawReason));
    }
    private void ItemDone(ProgramSkillFrame f, int actual)
    {
        var r = f.MouCaoShuangAnniversary!;
        events.AdvanceFact(new MouCaoShuangAnniversaryItemCompletedEvent(f.Id, r.Repeat, r.Option, r.X, r.Payer, actual));
        if (r.Operation != SkillProgramEffectOp.ResolveMouCaoShuangFanshi) { Complete(f); return; }
        frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.NextRepeat, Repeat = r.Repeat + 1, Cards = [], Draw = null }, PendingMovementContinuation = null }); Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouCaoShuangAnniversary: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Cao Shuang lost his real use/phase, accepted item or child cursor.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage is MouCaoShuangAnniversaryStage.Option or MouCaoShuangAnniversaryStage.Target) { Publish(f); return; }
            if (r.Stage == MouCaoShuangAnniversaryStage.Cards)
            {
                if (r.Cards.Count < r.Required && Choices(f).Count > 0) { Publish(f); return; }
                if (r.Cards.Count == 0) { ItemDone(f, 0); return; }
                frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.MoveChildren, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Move(f); continue;
            }
            if (r.Stage == MouCaoShuangAnniversaryStage.MoveChildren)
            {
                if (native.Drain(f)) return; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Option == 3) { BeginDraw(f, r.Payer, r.Cards.Count, true); return; }
                ItemDone(f, r.Cards.Count); return;
            }
            if (r.Stage == MouCaoShuangAnniversaryStage.Drawing) return;
            if (r.Stage == MouCaoShuangAnniversaryStage.DrawChildren)
            {
                if (native.Drain(f)) return; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Operation == SkillProgramEffectOp.ResolveMouCaoShuangFudou) { frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.Fudou, Draw = null } }); continue; }
                if (r.Option == 3) native.CompleteRecast(f, r.Draw!.ActualDrawIds.Count);
                ItemDone(frames.ActiveProgram(id), r.Option == 3 ? r.Cards.Count : r.Draw!.ActualDrawIds.Count + r.Draw.ReplacementGainedIds.Count); return;
            }
            if (r.Stage == MouCaoShuangAnniversaryStage.NextRepeat)
            {
                var remaining = Items(r.Remaining);
                if (remaining.Length > 0 && r.Repeat < 3 * remaining.Length) { BeginItem(f, remaining[r.Repeat % remaining.Length]); return; }
                frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.Maximum } }); continue;
            }
            if (r.Stage == MouCaoShuangAnniversaryStage.Maximum)
            { frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.Recover } }); native.Maximum(f); continue; }
            if (r.Stage == MouCaoShuangAnniversaryStage.Recover)
            { frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.RecoveryChildren } }); if (facts.Alive(f.OwnerSeat)) native.Recover(f); continue; }
            if (r.Stage == MouCaoShuangAnniversaryStage.RecoveryChildren)
            { if (native.Drain(f)) return; frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.Grant } }); continue; }
            if (r.Stage == MouCaoShuangAnniversaryStage.Grant)
            { native.Grant(f); frames.ReplaceTop(frames.ActiveProgram(id) with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.GrantChildren } }); continue; }
            if (r.Stage == MouCaoShuangAnniversaryStage.GrantChildren)
            { if (native.Drain(f)) return; Complete(frames.ActiveProgram(id)); return; }
            if (r.Stage == MouCaoShuangAnniversaryStage.HpChildren)
            { if (native.Drain(f)) return; frames.ReplaceTop(f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.Fudou } }); continue; }
            if (r.Stage == MouCaoShuangAnniversaryStage.Fudou)
            {
                if (r.Cursor >= 2) { Complete(f); return; }
                var target = r.Cursor == 0 ? f.OwnerSeat : r.Other;
                frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Cursor = r.Cursor + 1 } });
                if (!facts.Alive(target)) continue;
                if (r.Red) { BeginDraw(f, target, 1, false); return; }
                frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = f.MouCaoShuangAnniversary! with { Stage = MouCaoShuangAnniversaryStage.HpChildren, Payer = target } });
                events.AdvanceFact(new MouCaoShuangAnniversaryHpIssuedEvent(f.Id, r.Cursor, target)); native.LoseHp(f, target); continue;
            }
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan, SkillProgramEffectOp.ResolveMouCaoShuangFanshi, SkillProgramEffectOp.ResolveMouCaoShuangFudou)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouCaoShuangAnniversary is not { Stage: MouCaoShuangAnniversaryStage.Drawing, Draw: null } r || outcome.RequestedCount != r.Required || outcome.RecipientSeat != r.Payer) throw new InvalidOperationException("Mou Cao Shuang requires the exact native draw return for the current effect.");
        frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = r with { Stage = MouCaoShuangAnniversaryStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouCaoShuangAnniversaryDrawReturnedEvent(f.Id, r.Repeat, r.Payer, r.Required, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouCaoShuangAnniversaryCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouCaoShuangAnniversary = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 35500)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouCaoShuangAnniversary: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 35500)] private bool Movement(ProgramSkillFrame f) => f.MouCaoShuangAnniversary is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 35500)] private void Invariant(ProgramSkillFrame f) { if (f.MouCaoShuangAnniversary is not null && !Valid(f)) throw new InvalidOperationException("Invalid Cao Shuang native item receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 35500)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 35500)] private bool Paid(ProgramSkillFrame f) => f.MouCaoShuangAnniversary is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan, SkillProgramEffectOp.ResolveMouCaoShuangFanshi)]
    private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f) => f.MouCaoShuangAnniversary!.Stage switch {
        MouCaoShuangAnniversaryStage.Option => p.Choices.FirstOrDefault(c => c.Parameters["item"] == "2") ?? p.Choices.First(),
        MouCaoShuangAnniversaryStage.Target => p.Choices.OrderBy(c => facts.Friendly(f.OwnerSeat, c.Targets.Single()) ? 1 : 0).First(),
        _ => p.Choices.OrderBy(c => facts.Keep(p.PlayerSeat, c.Cards.Single())).First() };
}
