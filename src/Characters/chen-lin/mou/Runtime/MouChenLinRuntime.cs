namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed partial class MouChenLinRuntime(IMouChenLinFacts facts, IMouChenLinNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts) : IMouChenLinRuntime
{
    internal const string Yaozuo = "mou:yaozuo", Zhuanwen = "mou:zhuanwen", RevealBind = "mou-chen-lin-written-cards", Action = "mou-chen-lin-anniversary";
    internal const string GiveReason = "skill-program.mou:zhuanwen.give", ReturnReason = "skill-program.mou:zhuanwen.return-top";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    internal static bool DamageCard(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault or CardKind.AgainstCurrent or CardKind.WumiaoFlood or CardKind.Lightning;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 37000, SkillProgramEffectOp.ResolveMouChenLinZhuanwen)] private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => facts.HandCount(c.OwnerSeat) > 0;
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouChenLin is not null || f.InstructionIndex != 1 || f.SkillId != Zhuanwen || !facts.Parent(f)) throw new InvalidOperationException("Zhuanwen requires its exact actual Ending owner window.");
        BeginWritten(f, -1); return SkillProgramStepOutcome.AwaitChild;
    }
    private void BeginWritten(ProgramSkillFrame f, int forcedTarget)
    {
        var r = new MouChenLinReceipt(Source(f), f.GameplayHash, facts.Turn, f.WindowContext?.ParentFrameId ?? 0, Math.Min(5, facts.HandCount(f.OwnerSeat)), forcedTarget) { Stage = MouChenLinStage.Revealing, Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouChenLin = r }); events.AdvanceFact(new MouChenLinStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.ParentId, r.Requested, r.ForcedTarget));
        native.Reveal(f);
        f = frames.ActiveProgram(f.Id); r = f.MouChenLin!; var cards = facts.Revealed(f);
        frames.ReplaceTop(f = f with { MouChenLin = r with { Stage = MouChenLinStage.RevealChildren, Cards = cards, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < cards.Count; i++) events.AdvanceFact(new MouChenLinRevealedEvent(f.Id, i, cards[i].Id, cards[i].Kind));
        Continue(f.Id);
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouChenLin is { } r && f.InstructionIndex == 1 && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && facts.Parent(f) && r.ParentId == (f.WindowContext?.ParentFrameId ?? 0) && r.Turn == facts.Turn && r.Requested is >= 0 and <= 5 && r.Cards.Count <= r.Requested && r.Cards.Select(c => c.Id).Distinct().Count() == r.Cards.Count && r.Used.All(id => r.Cards.Any(c => c.Id == id)) && r.Used.Distinct().Count() == r.Used.Count && Enum.IsDefined(r.Stage) && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && facts.History<MouChenLinStartedEvent>().Count(e => e == new MouChenLinStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.ParentId, r.Requested, r.ForcedTarget)) == 1;
    private IReadOnlyList<MouChenLinCard> Remaining(ProgramSkillFrame f) => facts.Current(f.MouChenLin!.Cards.Select(c => c.Id)).Where(c => c.From == CardLocation.Processing).ToArray();
    private IReadOnlyList<MouChenLinUsePlan> Plans(ProgramSkillFrame f, int target) => Remaining(f).Where(c => DamageCard(c.Kind) && !f.MouChenLin!.Used.Contains(c.Id)).Select(c => facts.Plan(f, c.Id, target)).OfType<MouChenLinUsePlan>().ToArray();
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int? target = null, int? card = null) => new(new($"mou-chen-lin.{f.Id}.{f.MouChenLin!.Stage}.{f.MouChenLin.Index}.{branch}.{target}.{card}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = Action, ["branch"] = branch });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouChenLin!;
        if (r.Stage == MouChenLinStage.BranchTarget)
        {
            var choices = new List<PromptChoice>(); var nonDamage = Remaining(f).Any(c => !DamageCard(c.Kind));
            foreach (var target in facts.Living().Where(t => r.ForcedTarget < 0 || t == r.ForcedTarget))
            {
                if (Plans(f, target).Count > 0) choices.Add(Choice(f, "use", "对" + facts.Name(target) + "使用伤害牌", target));
                if (nonDamage) choices.Add(Choice(f, "give", "令" + facts.Name(target) + "获得非伤害牌", target));
            }
            return choices;
        }
        if (r.Stage == MouChenLinStage.UseChoice) return Plans(f, r.Target).Select(p => Choice(f, "use-card", "对同一目标使用" + r.Cards.Single(c => c.Id == p.CardId).Label, r.Target, p.CardId)).ToArray();
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); if (choices.Count == 0) { ReturnRemaining(f); return; }
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat, "撰文：展示牌已冻结，选择一名角色与效果，伤害牌依次真实使用。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), IsPrivate = false, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(f.OwnerSeat)); native.Publish();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { MouChenLin: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat || Choices(f).SingleOrDefault(c => c.Id == supplied.Id) is not { } canonical || !canonical.Cards.SequenceEqual(supplied.Cards) || !canonical.Targets.SequenceEqual(supplied.Targets) || canonical.Parameters.Count != supplied.Parameters.Count || canonical.Parameters.Any(p => supplied.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Zhuanwen needs the current exact public entity, branch and fixed target choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MouChenLinStage.BranchTarget)
        {
            var target = supplied.Targets.Single(); var branch = supplied.Parameters["branch"];
            frames.ReplaceTop(f = f with { MouChenLin = r with { Target = target, Branch = branch, Stage = branch == "give" ? MouChenLinStage.GiveChildren : MouChenLinStage.UseChoice } }); events.AdvanceFact(new MouChenLinBranchEvent(f.Id, branch, target));
            if (branch == "give") StartMove(f, MouChenLinStage.GiveChildren); else Continue(f.Id); return;
        }
        var plan = Plans(f, r.Target).Single(p => p.CardId == supplied.Cards.Single());
        frames.ReplaceTop(f = f with { MouChenLin = r with { Stage = MouChenLinStage.IssuingUse, Plan = plan, Native = null, NativeReturned = false } }); native.Use(f);
    }
    private void StartMove(ProgramSkillFrame f, MouChenLinStage stage)
    {
        var r = f.MouChenLin!; frames.ReplaceTop(f = f with { MouChenLin = r with { Stage = stage, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Move(f); Continue(f.Id);
    }
    private void ReturnRemaining(ProgramSkillFrame f) => StartMove(f, MouChenLinStage.ReturningChildren);
    internal void MovesPaid(long id, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = frames.ActiveProgram(id); var r = f.MouChenLin!;
        if (!Valid(f) || moves.Any(m => !r.Cards.Any(c => c.Id == m.CardId && c.Kind == m.CardKind) || m.From != CardLocation.Processing || m.Sequence <= r.Before)) throw new InvalidOperationException("Zhuanwen lost its exact physical processing movement receipt.");
        frames.Replace(id, f with { MouChenLin = r with { After = moves.Count == 0 ? r.Before : moves.Max(m => m.Sequence) } });
        foreach (var m in moves) events.AdvanceFact(new MouChenLinMovedEvent(f.Id, r.Stage, m.CardId, m.From, m.To, m.Sequence));
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouChenLin: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Zhuanwen lost its original native parent, public entities or whole Use return.");
            if (r.Stage is MouChenLinStage.NativeUse or MouChenLinStage.IssuingUse or MouChenLinStage.Revealing) return;
            if (r.Stage is MouChenLinStage.BranchTarget or MouChenLinStage.UseChoice)
            {
                if (!facts.Alive(f.OwnerSeat) || facts.Finished || r.Target >= 0 && !facts.Alive(r.Target)) { ReturnRemaining(f); return; }
                Publish(f); return;
            }
            if (native.Drain(f)) return;
            frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == MouChenLinStage.ReturningChildren) { Complete(f); return; }
            if (r.Stage == MouChenLinStage.GiveChildren) { ReturnRemaining(f); return; }
            if (r.Stage == MouChenLinStage.RevealChildren) { frames.ReplaceTop(f with { MouChenLin = r with { Stage = MouChenLinStage.BranchTarget } }); continue; }
            if (r.Stage == MouChenLinStage.UseChildren) { frames.ReplaceTop(f with { MouChenLin = r with { Stage = MouChenLinStage.UseChoice, Native = null, Plan = null, NativeReturned = false } }); continue; }
            throw new InvalidOperationException("Zhuanwen has no legal native continuation at this cursor.");
        }
    }
    internal void UseReturned(MouChenLinNativeUse origin, bool activeAttack)
    {
        var f = frames.ActiveProgram(origin.FrameId); var r = f.MouChenLin!;
        if (!Valid(f) || r.Native != origin || r.NativeReturned) throw new InvalidOperationException("Zhuanwen needs its original once-completed native Use.");
        frames.ReplaceTop(f = f with { MouChenLin = r with { NativeReturned = true } }); events.AdvanceFact(new MouChenLinUseReturnedEvent(f.Id, origin.UseId, origin.ActionId)); if (!activeAttack) SettleUse(origin);
    }
    internal void SettleUse(MouChenLinNativeUse origin)
    {
        var f = frames.ActiveProgram(origin.FrameId); var r = f.MouChenLin!;
        if (!Valid(f) || r.Native != origin || !r.NativeReturned || r.Used.Contains(origin.CardId)) throw new InvalidOperationException("Zhuanwen cannot advance the queue before the native whole Use returns once.");
        frames.ReplaceTop(f with { MouChenLin = r with { Stage = MouChenLinStage.UseChildren, Used = r.Used.Append(origin.CardId).ToArray(), Index = r.Index + 1 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouChenLinCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouChenLin = null, MouChenLinInvitation = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 37000)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame f || f.Id != id) return false; if (f.MouChenLin is not null) { Continue(id); return true; } if (f.MouChenLinInvitation is not null) { ContinueInvitation(id); return true; } return false; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 37000)] private bool Movement(ProgramSkillFrame f) => (f.MouChenLin is not null || f.MouChenLinInvitation is not null) && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 37000)] private void Invariant(ProgramSkillFrame f) { if (f.MouChenLin is not null && !Valid(f)) throw new InvalidOperationException("Invalid Zhuanwen physical queue receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 37000)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 37000)] private bool Paid(ProgramSkillFrame f) => f.MouChenLin is not null && Valid(f) || f.MouChenLinInvitation is not null && ValidInvitation(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouChenLinZhuanwen, SkillProgramEffectOp.ResolveMouChenLinYaozuo)] private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderBy(c => c.Targets is [var t] && (c.Parameters["branch"] == "give" ? facts.Friendly(p.PlayerSeat, t) : !facts.Friendly(p.PlayerSeat, t)) ? 0 : 1).First();
}
