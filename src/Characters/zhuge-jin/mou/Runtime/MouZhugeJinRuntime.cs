using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouZhugeJinRuntime(IMouZhugeJinFacts facts, IMouZhugeJinNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouZhugeJinRuntime
{
    internal const string Action = "mou-zhuge-jin", GiveReason = "skill-program.mou:taozhou.give", DrawReason = "skill-program.mou:taozhou.draw", DiscardReason = "skill-program.mou-zhuge-jin.discard";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 35600, SkillProgramEffectOp.ResolveMouZhugeJinHoude, SkillProgramEffectOp.ResolveMouZhugeJinZijin)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => facts.Use(c, w,
        c.SkillId == MouZhugeJinComposition.Houde ? SkillProgramEffectOp.ResolveMouZhugeJinHoude : SkillProgramEffectOp.ResolveMouZhugeJinZijin) is not null;
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouZhugeJin is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1 || facts.Program(f.SkillId)?.GameplayHash != f.GameplayHash || !facts.Parent(f, op)) throw new InvalidOperationException("Mou Zhuge Jin requires the original native activation or card-use boundary.");
        var use = op == SkillProgramEffectOp.ResolveMouZhugeJinTaozhou ? null : facts.Use(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), f.WindowContext!, op);
        if (op != SkillProgramEffectOp.ResolveMouZhugeJinTaozhou && use is null) throw new InvalidOperationException("Houde/Zijin lost its actual qualified first target or undamaging true Use.");
        var r = new MouZhugeJinReceipt(Source(f), f.GameplayHash, op, facts.Turn, facts.TurnOwner, facts.Round, use?.WindowId ?? 0, use?.UseId ?? 0, use?.Action.ActionId ?? 0)
        { Stage = use is null ? MouZhugeJinStage.Target : MouZhugeJinStage.Cost, Target = use?.Action.ActorSeat ?? -1, Red = use?.Red ?? false, Committed = use is not null, Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouZhugeJin = r }); events.AdvanceFact(new MouZhugeJinStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ActualTurn, r.TurnOwner, r.Round, r.WindowId, r.UseId, r.ActionId));
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouZhugeJin is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId)?.GameplayHash == f.GameplayHash &&
        f.InstructionIndex == 1 && facts.Parent(f, r.Operation) && Enum.IsDefined(r.Stage) && r.SecretNumber is >= 0 and <= 3 && r.Cards.Count <= 3 && r.Cards.Distinct().Count() == r.Cards.Count &&
        r.Given is >= 0 and <= 3 && r.DrawCursor is >= 0 and <= 2 && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence &&
        facts.History<MouZhugeJinStartedEvent>().Count(e => e == new MouZhugeJinStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ActualTurn, r.TurnOwner, r.Round, r.WindowId, r.UseId, r.ActionId)) == 1;
    private int Chooser(ProgramSkillFrame f) => f.MouZhugeJin is { Stage: MouZhugeJinStage.Give } r ? r.Target : f.OwnerSeat;
    private int Holder(ProgramSkillFrame f) => f.MouZhugeJin is { Operation: SkillProgramEffectOp.ResolveMouZhugeJinHoude, Red: false } r ? r.Target : f.OwnerSeat;
    private PromptChoice Option(ProgramSkillFrame f, string branch, string text, int? card = null, int? target = null) => new(new ChoiceId($"mou-zhuge-jin.{f.Id}.{f.MouZhugeJin!.Stage}.{branch}.{card}.{target}"), text,
        card is { } c ? [c] : [], target is { } s ? [s] : [], new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouZhugeJin!;
        if (r.Stage == MouZhugeJinStage.Target) return facts.Targets(f.OwnerSeat).Select(s => Option(f, "target", facts.Name(s), target: s)).ToArray();
        if (r.Stage == MouZhugeJinStage.SecretNumber) return Enumerable.Range(1, 3).Select(n => Option(f, "number-" + n, "秘密选择" + n)).ToArray();
        if (r.Stage == MouZhugeJinStage.Give)
        {
            var choices = new List<PromptChoice> { Option(f, "done", r.Cards.Count == 0 ? "不交牌，确认" : "确认交出所选的" + r.Cards.Count + "张手牌") };
            foreach (var c in facts.Hand(r.Target).Where(c => r.Cards.Contains(c.Id) || r.Cards.Count < 3)) choices.Add(Option(f, r.Cards.Contains(c.Id) ? "remove" : "add", (r.Cards.Contains(c.Id) ? "取消" : "选择") + c.Label, c.Id));
            return choices;
        }
        if (r.Stage == MouZhugeJinStage.Cost)
        {
            var holder = Holder(f); var cards = facts.Costs(f.OwnerSeat, holder); var choices = new List<PromptChoice>();
            if (holder != f.OwnerSeat && cards.Any(c => c.From.Zone == CardZoneKind.Hand)) choices.Add(Option(f, "hand", "随机弃置其一张手牌"));
            choices.AddRange(cards.Where(c => holder == f.OwnerSeat || c.From.Zone != CardZoneKind.Hand).Select(c => Option(f, "card", "弃置" + c.Label, c.Id)));
            choices.Add(Option(f, r.Operation == SkillProgramEffectOp.ResolveMouZhugeJinZijin ? "lose" : "decline", r.Operation == SkillProgramEffectOp.ResolveMouZhugeJinZijin ? "失去1点体力" : "不发动厚德")); return choices;
        }
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var r = f.MouZhugeJin!; var choices = Choices(f); var chooser = Chooser(f);
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        var text = r.Stage switch { MouZhugeJinStage.Target => "讨州：选择一名有手牌且没有自矜的其他角色。", MouZhugeJinStage.SecretNumber => "讨州：秘密选择1~3中的数字。",
            MouZhugeJinStage.Give => "讨州：可以交出至多三张手牌，选好后确认。", _ => r.Operation == SkillProgramEffectOp.ResolveMouZhugeJinZijin ? "自矜：弃置一张牌或失去1点体力。" : "厚德：可支付弃牌令此牌对你无效。" };
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(chooser)); native.Publish();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouZhugeJin: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) ||
            Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } canonical || !canonical.Cards.SequenceEqual(chosen.Cards) || !canonical.Targets.SequenceEqual(chosen.Targets) || canonical.Parameters.Count != chosen.Parameters.Count || canonical.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Mou Zhuge Jin requires the actual currently published private choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = chosen.Parameters["branch"];
        if (r.Stage == MouZhugeJinStage.Target) { frames.ReplaceTop(f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.SecretNumber, Target = chosen.Targets.Single() } }); Continue(f.Id); return; }
        if (r.Stage == MouZhugeJinStage.SecretNumber)
        { frames.ReplaceTop(f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.Give, SecretNumber = int.Parse(branch.AsSpan(7), CultureInfo.InvariantCulture), Committed = true } }); Continue(f.Id); return; }
        if (r.Stage == MouZhugeJinStage.Give)
        {
            if (branch != "done") { var id = chosen.Cards.Single(); frames.ReplaceTop(f with { MouZhugeJin = r with { Cards = branch == "add" ? r.Cards.Append(id).ToArray() : r.Cards.Where(c => c != id).ToArray() } }); Continue(f.Id); return; }
            // Publish cooldown only after the other player has committed the gift.
            events.AdvanceFact(new MouZhugeJinCooldownIssuedEvent(f.Id, r.Source, r.GameplayHash, r.Round, checked(r.Round + r.SecretNumber)));
            frames.ReplaceTop(f = f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.GiveChildren, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (r.Cards.Count > 0 && facts.Alive(f.OwnerSeat) && facts.Alive(r.Target)) native.Give(f); Continue(f.Id); return;
        }
        if (branch == "decline") { Complete(f); return; }
        if (branch == "lose")
        {
            frames.ReplaceTop(f = f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.LossChildren, HpBefore = facts.Hp(f.OwnerSeat) }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Lose(f); Continue(f.Id); return;
        }
        var cards = facts.Costs(f.OwnerSeat, Holder(f)); var paying = branch == "hand" ? native.RandomHand(f, cards.Where(c => c.From.Zone == CardZoneKind.Hand).ToArray()) : cards.Single(c => c.Id == chosen.Cards.Single());
        frames.ReplaceTop(f = f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.CostChildren, Paying = paying, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Discard(f); Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouZhugeJin: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Zhuge Jin lost his original native parent, private commitment or paid cursor.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage == MouZhugeJinStage.Drawing) return;
            if (r.Stage is MouZhugeJinStage.Target or MouZhugeJinStage.SecretNumber or MouZhugeJinStage.Give or MouZhugeJinStage.Cost)
            {
                if (!facts.Alive(Chooser(f)) || Choices(f).Count == 0) { Complete(f); return; }
                Publish(f); return;
            }
            if (r.Stage == MouZhugeJinStage.Draw)
            {
                if (r.DrawCursor >= 2) { Complete(f); return; }
                var seat = r.DrawCursor == 0 ? f.OwnerSeat : r.Target;
                if (!facts.Alive(seat)) { frames.ReplaceTop(f with { MouZhugeJin = r with { DrawCursor = r.DrawCursor + 1 } }); continue; }
                frames.ReplaceTop(f = f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.Drawing, Draw = null } }); draws.BeginOperationProgramDraw(f, seat, 1, new(DrawReason)); return;
            }
            if (native.Drain(f)) return;
            frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == MouZhugeJinStage.GiveChildren)
            {
                if (r.Given >= r.SecretNumber) { frames.ReplaceTop(f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.Draw } }); continue; }
                var x = r.SecretNumber - r.Given;
                events.AdvanceFact(new MouZhugeJinBudgetIssuedEvent(f.Id, r.Source, f.GameplayHash, r.Target, x));
                frames.ReplaceTop(f = f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.GrantChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                if (x >= 2 && facts.Alive(r.Target)) native.Grant(f); continue;
            }
            if (r.Stage == MouZhugeJinStage.DrawChildren) { frames.ReplaceTop(f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.Draw, DrawCursor = r.DrawCursor + 1 } }); continue; }
            if (r.Stage == MouZhugeJinStage.CostChildren && r.CostPaid && r.Operation == SkillProgramEffectOp.ResolveMouZhugeJinHoude && facts.Alive(f.OwnerSeat)) native.Nullify(f);
            Complete(frames.ActiveProgram(id)); return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhugeJinTaozhou)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouZhugeJin is not { Stage: MouZhugeJinStage.Drawing, Draw: null } r || outcome.RecipientSeat != (r.DrawCursor == 0 ? f.OwnerSeat : r.Target) || outcome.RequestedCount != 1)
            throw new InvalidOperationException("Taozhou cannot replay or substitute its actual ordered Draw1 receipt.");
        frames.ReplaceTop(f = f with { MouZhugeJin = r with { Stage = MouZhugeJinStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouZhugeJinDrawReturnedEvent(f.Id, outcome.RecipientSeat, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    { events.AdvanceFact(new MouZhugeJinCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouZhugeJin = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 35600)]
    private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouZhugeJin: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 35600)] private bool Movement(ProgramSkillFrame f) => f.MouZhugeJin is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 35600)]
    private void Invariant(ProgramSkillFrame f) { if (f.MouZhugeJin is not null && !Valid(f)) throw new InvalidOperationException("Invalid own-frame Mou Zhuge Jin receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 35600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 35600)]
    private bool Paid(ProgramSkillFrame f) => f.MouZhugeJin is { Committed: true } && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouZhugeJinTaozhou, SkillProgramEffectOp.ResolveMouZhugeJinHoude, SkillProgramEffectOp.ResolveMouZhugeJinZijin)]
    private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouZhugeJin!;
        if (r.Stage == MouZhugeJinStage.Give) return facts.Friendly(r.Target, f.OwnerSeat) && r.Cards.Count < 3 ? p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "add") ?? p.Choices.First() : p.Choices.First();
        if (r.Stage == MouZhugeJinStage.SecretNumber) return p.Choices[1]; return p.Choices.First();
    }
}
