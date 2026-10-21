using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouJiangWeiAnniversaryRuntime(IMouJiangWeiAnniversaryFacts facts, IMouJiangWeiAnniversaryNative native, IProgramRuntimeFrames frames,
    IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouJiangWeiAnniversaryRuntime
{
    internal const string Juemou = "mou:juemou", Fuzhan = "mou:fuzhan", Action = "mou-jiang-wei-anniversary", CostReason = "skill-program.mou:juemou.discard", DrawReason = "skill-program.mou:juemou.draw";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 35200, SkillProgramEffectOp.InitializeMouJiangWeiJuemou, SkillProgramEffectOp.ResolveMouJiangWeiJuemou, SkillProgramEffectOp.RestoreMouJiangWeiJuemou, SkillProgramEffectOp.ResolveMouJiangWeiFuzhan)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => facts.CanTrigger(c, w, t.Effects.Single().Op);
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouJiangWeiAnniversary is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { } w || !facts.Parent(f) ||
            !facts.CanTrigger(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w, operation))
            throw new InvalidOperationException("Mou Jiang Wei requires the exact native Use, turn or real survived/entered Dying occurrence.");
        var instance = facts.Instance(f.OwnerSeat); var yin = facts.State(f.OwnerSeat, instance, "yin"); var modified = facts.State(f.OwnerSeat, instance, "modified");
        var cause = operation == SkillProgramEffectOp.RestoreMouJiangWeiJuemou ? facts.CausalDying(f.OwnerSeat, w) : null;
        var stage = operation == SkillProgramEffectOp.InitializeMouJiangWeiJuemou ? MouJiangWeiAnniversaryStage.Initial : operation == SkillProgramEffectOp.ResolveMouJiangWeiJuemou
            ? yin ? MouJiangWeiAnniversaryStage.Victim : MouJiangWeiAnniversaryStage.Damage : MouJiangWeiAnniversaryStage.Recovery;
        var r = new MouJiangWeiAnniversaryReceipt(Source(f), f.GameplayHash, operation, instance, w.ParentFrameId, facts.Turn, yin, modified,
            facts.Action(w), cause?.Cause ?? 0, cause?.Dying ?? 0) { Stage = stage, Paid = stage != MouJiangWeiAnniversaryStage.Initial, Victim = !yin ? f.OwnerSeat : -1, DamageSource = !yin ? f.OwnerSeat : -1, Before = facts.MovementSequence, After = facts.MovementSequence };
        frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = r });
        events.AdvanceFact(new MouJiangWeiAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.Turn, r.Yin, r.Modified, r.ActionId, r.CauseFrameId, r.DyingId));
        if (operation == SkillProgramEffectOp.ResolveMouJiangWeiJuemou)
        {
            native.State(f, "yin", !yin);
            if (!yin) { BeginDamage(f); return SkillProgramStepOutcome.AwaitChild; }
        }
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if (f.MouJiangWeiAnniversary is not { } r || f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || r.Source != Source(f) ||
            r.GameplayHash != f.GameplayHash || facts.Program(f.SkillId).GameplayHash != f.GameplayHash || r.ParentId != f.WindowContext?.ParentFrameId || frames.ParentOf(f.Id)?.Id != r.ParentId ||
            r.Turn != facts.Turn || r.Before < 0 || r.After < r.Before || r.After > facts.MovementSequence || !Enum.IsDefined(r.Stage) || !facts.Parent(f)) return false;
        return facts.History<MouJiangWeiAnniversaryStartedEvent>().Count(e => e == new MouJiangWeiAnniversaryStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.Turn, r.Yin, r.Modified, r.ActionId, r.CauseFrameId, r.DyingId)) == 1;
    }
    private int Chooser(ProgramSkillFrame f) => f.MouJiangWeiAnniversary is { Stage: MouJiangWeiAnniversaryStage.Discard } r ? r.Victim : f.OwnerSeat;
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int? target = null, int? card = null) => new(new ChoiceId($"mou-jiang-wei-anniversary.{f.Id}.{f.MouJiangWeiAnniversary!.Stage}.{branch}.{target}.{card}"), label,
        card is { } c ? [c] : [], target is { } t ? [t] : [], new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouJiangWeiAnniversary!;
        if (r.Stage == MouJiangWeiAnniversaryStage.Initial) return [Choice(f, "yang", "阳：自己受到伤害后摸已损失体力值张牌"), Choice(f, "yin", "阴：令一名角色弃另一角色牌并受到其伤害")];
        if (r.Stage == MouJiangWeiAnniversaryStage.Victim) return facts.Living().Select(t => Choice(f, "victim", "选择弃牌者及伤害目标：" + facts.Name(t), t)).ToArray();
        if (r.Stage == MouJiangWeiAnniversaryStage.Source) return facts.Living().Where(t => t != r.Victim).Select(t => Choice(f, "source", "选择牌的持有者及伤害来源：" + facts.Name(t), t)).ToArray();
        if (r.Stage != MouJiangWeiAnniversaryStage.Discard) return [];
        var cards = facts.Discardable(r.Victim, r.DamageSource); var options = cards.Where(c => c.From.Zone == CardZoneKind.Equipment).Select(c => Choice(f, "equipment", "弃置装备【" + c.Label + "】", card: c.Id)).ToList();
        if (cards.Any(c => c.From.Zone == CardZoneKind.Hand)) options.Add(Choice(f, "hand", "弃置其一张随机手牌")); return options;
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); var chooser = Chooser(f); frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooser, f.MouJiangWeiAnniversary!.Stage == MouJiangWeiAnniversaryStage.Initial ? "绝谋：选择游戏开始时的阴阳状态。" : "绝谋：结算本次已发动的转换效果。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(chooser)); native.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouJiangWeiAnniversary: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) ||
            Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } canonical || !canonical.Cards.SequenceEqual(chosen.Cards) || !canonical.Targets.SequenceEqual(chosen.Targets) || canonical.Parameters.Count != chosen.Parameters.Count || canonical.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Juemou requires the actual current chooser and published branch.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = chosen.Parameters["branch"];
        if (r.Stage == MouJiangWeiAnniversaryStage.Initial) { native.State(f, "yin", branch == "yin"); Complete(f); return; }
        if (r.Stage == MouJiangWeiAnniversaryStage.Victim) { frames.ReplaceTop(f with { MouJiangWeiAnniversary = r with { Victim = chosen.Targets.Single(), Stage = MouJiangWeiAnniversaryStage.Source } }); Continue(f.Id); return; }
        if (r.Stage == MouJiangWeiAnniversaryStage.Source)
        {
            var source = chosen.Targets.Single(); frames.ReplaceTop(f with { MouJiangWeiAnniversary = r with { DamageSource = source, Stage = MouJiangWeiAnniversaryStage.Discard } });
            events.AdvanceFact(new MouJiangWeiAnniversaryPairEvent(f.Id, r.Victim, source)); Continue(f.Id); return;
        }
        var cards = facts.Discardable(r.Victim, r.DamageSource);
        var card = branch == "hand" ? native.RandomHand(f, cards.Where(c => c.From.Zone == CardZoneKind.Hand).ToArray()) : cards.Single(c => c.Id == chosen.Cards.Single());
        frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = r with { Stage = MouJiangWeiAnniversaryStage.DiscardChildren, Card = card, Before = facts.MovementSequence, After = facts.MovementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        native.Discard(f); Continue(f.Id);
    }
    private void BeginDamage(ProgramSkillFrame f)
    {
        var r = f.MouJiangWeiAnniversary!;
        if (!facts.Alive(r.Victim)) { Complete(f); return; }
        frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = r with { Stage = MouJiangWeiAnniversaryStage.Damage } });
        events.AdvanceFact(new MouJiangWeiAnniversaryDamageIssuedEvent(f.Id, r.Victim, r.DamageSource)); native.Damage(f); Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouJiangWeiAnniversary: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Juemou lost its owning native parent, accepted conversion branch or paid child cursor.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage is MouJiangWeiAnniversaryStage.Initial or MouJiangWeiAnniversaryStage.Victim or MouJiangWeiAnniversaryStage.Source) { Publish(f); return; }
            if (r.Stage == MouJiangWeiAnniversaryStage.Discard)
            {
                if (!facts.Alive(r.Victim)) { Complete(f); return; }
                // The user explicitly permits an empty HE holder. Discard is an
                // effect, never a cost or a condition for the subsequent damage.
                if (Choices(f).Count == 0) { BeginDamage(f); return; }
                Publish(f); return;
            }
            if (r.Stage is MouJiangWeiAnniversaryStage.DiscardChildren or MouJiangWeiAnniversaryStage.DrawChildren or MouJiangWeiAnniversaryStage.RecoveryChildren)
            {
                if (native.Drain(f)) return;
                frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage == MouJiangWeiAnniversaryStage.DiscardChildren) { BeginDamage(f); return; }
                if (r.Stage == MouJiangWeiAnniversaryStage.RecoveryChildren && r.Operation == SkillProgramEffectOp.ResolveMouJiangWeiFuzhan) native.State(f, "modified", true);
                Complete(frames.ActiveProgram(id)); return;
            }
            if (r.Stage == MouJiangWeiAnniversaryStage.Damage)
            {
                if (f.AttackAttempt is not null || native.Drain(f)) return;
                if (r.Yin || !facts.Alive(f.OwnerSeat)) { Complete(f); return; }
                var count = Math.Max(0, facts.MaxHp(f.OwnerSeat) - facts.Hp(f.OwnerSeat));
                if (count == 0) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = r with { Stage = MouJiangWeiAnniversaryStage.Drawing, Requested = count } }); draws.BeginOperationProgramDraw(f, f.OwnerSeat, count, new(DrawReason)); return;
            }
            if (r.Stage == MouJiangWeiAnniversaryStage.Drawing) return;
            if (r.Stage == MouJiangWeiAnniversaryStage.Recovery)
            {
                var targetHp = r.Operation == SkillProgramEffectOp.RestoreMouJiangWeiJuemou ? 1 : facts.MaxHp(f.OwnerSeat);
                var amount = Math.Max(0, targetHp - facts.Hp(f.OwnerSeat));
                frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = r with { Stage = MouJiangWeiAnniversaryStage.RecoveryChildren, Requested = amount } });
                events.AdvanceFact(new MouJiangWeiAnniversaryRecoveryIssuedEvent(f.Id, amount, targetHp, r.DyingId));
                if (amount > 0 && facts.Alive(f.OwnerSeat)) native.Recover(f, amount); continue;
            }
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouJiangWeiJuemou)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouJiangWeiAnniversary is not { Stage: MouJiangWeiAnniversaryStage.Drawing, Draw: null } r || outcome.RequestedCount != r.Requested || outcome.RecipientSeat != f.OwnerSeat)
            throw new InvalidOperationException("Juemou draw must return from the original real Yang damage exactly once.");
        frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = r with { Stage = MouJiangWeiAnniversaryStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouJiangWeiAnniversaryDrawReturnedEvent(f.Id, r.Requested, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouJiangWeiAnniversaryCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouJiangWeiAnniversary = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 35200)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouJiangWeiAnniversary: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 35200)] private bool Movement(ProgramSkillFrame f) => f.MouJiangWeiAnniversary is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 35200)] private void Invariant(ProgramSkillFrame f) { if (f.MouJiangWeiAnniversary is not null && !Valid(f)) throw new InvalidOperationException("Invalid Mou Jiang Wei owning receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 35200)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 35200)] private bool Paid(ProgramSkillFrame f) => f.MouJiangWeiAnniversary is { Paid: true } && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.InitializeMouJiangWeiJuemou, SkillProgramEffectOp.ResolveMouJiangWeiJuemou)]
    private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f) => f.MouJiangWeiAnniversary!.Stage switch {
        MouJiangWeiAnniversaryStage.Initial => p.Choices.First(),
        MouJiangWeiAnniversaryStage.Victim => p.Choices.OrderBy(c => facts.Friendly(f.OwnerSeat, c.Targets.Single()) ? 1 : 0).First(),
        MouJiangWeiAnniversaryStage.Source => p.Choices.OrderByDescending(c => facts.Friendly(f.OwnerSeat, c.Targets.Single()) ? 1 : 0).First(), _ => p.Choices.First() };
}
