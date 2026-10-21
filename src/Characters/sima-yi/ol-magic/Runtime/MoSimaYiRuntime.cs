using System.Globalization;

namespace CardGame.Core;

[ProgramRuntimeModule]
[ProgramCapability]
internal sealed partial class MoSimaYiRuntime(IProgramRuntimeFrames frames, IProgramRuntimeEvents events,
    IProgramRuntimePrompts prompts, IMoSimaYiFacts facts, IMoSimaYiNative native,
    IProgramHealthChildQueries health, IProgramHealthChildOperations healthOperations) : IMoSimaYiHost
{
    internal IReadOnlyList<MoSimaYiRecorded> Records(int owner) => Array.AsReadOnly(facts.History<MoSimaYiRecordAddedEvent>()
        .Where(e => e.OwnerSeat == owner).Select(e => new MoSimaYiRecorded(e.SkillId, e.CardKind)).Distinct().ToArray());

    internal bool IsMarked(int id) => id > 0 && facts.History<MoSimaYiDodgeMarkedEvent>().Any(e => e.CardId == id);
    internal bool IsOwnMarked(int owner, int id) => id > 0 && facts.History<MoSimaYiDodgeMarkedEvent>().Any(e =>
        e.OwnerSeat == owner && e.CardId == id && facts.History<MoSimaYiStartedEvent>().Any(s =>
            s.FrameId == e.FrameId && s.Source.OwnerSeat == owner && s.Source.SkillId == MoSimaYiComposition.Guifu &&
            s.Operation == SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge));
    internal bool NameAvailable(int owner, CardKind kind) => Records(owner).Any(e => e.CardKind == kind) &&
        !facts.History<MoSimaYiNameUsedEvent>().Any(e => e.ActorSeat == owner && e.Turn == facts.Turn && e.Kind == kind);

    internal void SlashReturned(long frameId)
    {
        var f = frames.ActiveProgram(frameId);
        var r = f.MoSimaYi ?? throw new InvalidOperationException("The native retaliation lost its owner.");
        if (!Valid(f) || r.Stage != MoSimaYiStage.NativeSlash || !r.NativeReturned ||
            facts.History<MoSimaYiSlashReturnedEvent>().Any(e => e.FrameId == frameId && e.Index == r.Index))
            throw new InvalidOperationException("Each frozen retaliation can return only once.");
        events.AdvanceFact(new MoSimaYiSlashReturnedEvent(f.Id, r.Index, r.NativeUseId, r.NativeActionId));
        frames.ReplaceTop(f with { MoSimaYi = r with { Stage = MoSimaYiStage.Children, Index = r.Index + 1,
            NativeUseId = 0, NativeActionId = 0, NativeReturned = false }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        Continue(frameId);
    }

    internal void RecordDamage(MoSimaYiCause cause)
    {
        if (facts.History<MoSimaYiDamageCauseEvent>().Any(e => e.Cause.DamageId == cause.DamageId)) return;
        events.AdvanceFact(new MoSimaYiDamageCauseEvent(cause));
        if (cause.CardKind is null && cause.SkillId is null) return;
        foreach (var source in facts.GuiFuOwners())
        {
            var entry = new MoSimaYiRecorded(cause.SkillId, cause.CardKind);
            if (!Records(source.OwnerSeat).Contains(entry))
                events.AdvanceFact(new MoSimaYiRecordAddedEvent(source.OwnerSeat, cause, cause.SkillId, cause.CardKind));
        }
    }

    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MoSimaYi is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1 || !MoSimaYiComposition.Handles(operation))
            throw new InvalidOperationException("Magic Sima Yi needs its original instruction.");
        var source = new CardConversionSource(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
        var r = new MoSimaYiReceipt(source, f.GameplayHash, operation, MoSimaYiStage.Children,
            f.WindowContext?.ParentFrameId ?? f.Id, facts.Round, facts.Turn);
        f = f with { MoSimaYi = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) };
        frames.ReplaceTop(f);
        events.AdvanceFact(new MoSimaYiStartedEvent(f.Id, source, f.GameplayHash, operation, r.WindowId, r.Round, r.Turn));
        if (operation == SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge)
        {
            if (facts.FindDodge() is { } entity) native.ObtainDodge(f, entity);
        }
        else if (operation == SkillProgramEffectOp.ResolveMoSimaYiMoubian)
        {
            var records = Records(f.OwnerSeat);
            if (records.Count < 3) throw new InvalidOperationException("Moubian requires three actually recorded entries.");
            var skills = records.Where(e => e.SkillId is not null).Select(e => e.SkillId!)
                .Append(MoSimaYiComposition.Zhouxi).Distinct(StringComparer.Ordinal).ToArray();
            frames.ReplaceTop(f = f with { MoSimaYi = r with { Stage = MoSimaYiStage.Granting, Skills = skills } });
            for (var i = 0; i < skills.Length; i++) events.AdvanceFact(new MoSimaYiSkillFrozenEvent(f.Id, i, skills[i]));
            native.EnterRumo(f);
        }
        else if (operation == SkillProgramEffectOp.ResolveMoSimaYiZhouxi)
        {
            var active = facts.EffectiveSkills(f.OwnerSeat);
            var pool = MoSimaYiComposition.Pool.Where(id => !active.Contains(id, StringComparer.Ordinal)).ToArray();
            var sample = native.Sample(pool, Math.Min(3, pool.Length), f.Id);
            frames.ReplaceTop(f = f with { MoSimaYi = r with { Stage = MoSimaYiStage.ChoosingSkill, Skills = sample } });
            for (var i = 0; i < sample.Count; i++) events.AdvanceFact(new MoSimaYiCandidateEvent(f.Id, i, sample[i]));
        }
        else if (operation == SkillProgramEffectOp.ResolveMoSimaYiRetaliation)
        {
            var targets = facts.RetaliationActors(f.OwnerSeat, r.Round);
            frames.ReplaceTop(f = f with { MoSimaYi = r with { Stage = MoSimaYiStage.IssuingSlash, Targets = targets } });
            for (var i = 0; i < targets.Count; i++) events.AdvanceFact(new MoSimaYiSlashTargetEvent(f.Id, i, targets[i]));
        }
        else
        {
            if (f.SelectedTargetSeats is not [var target]) throw new InvalidOperationException("Lieshi needs one real selected character.");
            frames.ReplaceTop(f with { MoSimaYi = r with { Stage = MoSimaYiStage.ChoosingLieshi, TargetSeat = target } });
        }
        Continue(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    internal bool Valid(ProgramSkillFrame f) => f.MoSimaYi is { } r && f.InstructionIndex == 1 &&
        r.Source == new CardConversionSource(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId) &&
        r.GameplayHash == f.GameplayHash && MoSimaYiComposition.Handles(r.Operation) &&
        (f.WindowContext is null ? r.WindowId == f.Id : frames.ParentOf(f.Id)?.Id == r.WindowId && r.WindowId == f.WindowContext.ParentFrameId) &&
        facts.History<MoSimaYiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source &&
            e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.WindowId == r.WindowId) == 1;

    internal void Continue(long frameId)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MoSimaYi: { } r } f && f.Id == frameId)
        {
            if (!Valid(f)) throw new InvalidOperationException("Magic Sima Yi lost its original issued receipt.");
            if (r.Stage == MoSimaYiStage.Damage)
            {
                if (native.FinishDamage(f)) return;
                frames.ReplaceTop(f with { MoSimaYi = r with { Stage = MoSimaYiStage.Children } });
                continue;
            }
            if (r.Stage == MoSimaYiStage.NativeSlash) return;
            if (r.Stage == MoSimaYiStage.Children)
            {
                if (native.DrainChildren(f.Id)) return;
                f = frames.ActiveProgram(f.Id); r = f.MoSimaYi!;
                f = f with { PendingMovementContinuation = null };
                frames.ReplaceTop(f);
                if (r.Operation == SkillProgramEffectOp.ResolveMoSimaYiMoubian)
                    frames.ReplaceTop(f with { MoSimaYi = r with { Stage = MoSimaYiStage.Granting } });
                else if (r.Operation == SkillProgramEffectOp.ResolveMoSimaYiRetaliation)
                    frames.ReplaceTop(f with { MoSimaYi = r with { Stage = MoSimaYiStage.IssuingSlash } });
                else if (r.Operation == SkillProgramEffectOp.ResolveMoSimaYiLieshi && r.Index < 2)
                    frames.ReplaceTop(f with { MoSimaYi = r with { Stage = MoSimaYiStage.ChoosingLieshi } });
                else { Complete(f); return; }
                continue;
            }
            if (facts.IsGameOver || !facts.IsAlive(f.OwnerSeat)) { Complete(f); return; }
            if (r.Stage == MoSimaYiStage.Granting)
            {
                if (r.Index == r.Skills.Count) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MoSimaYi = r with { Stage = MoSimaYiStage.Children, Index = r.Index + 1 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                native.GrantSkill(f, r.Skills[r.Index], temporary: false);
                continue;
            }
            if (r.Stage == MoSimaYiStage.IssuingSlash)
            {
                if (r.Index == r.Targets.Count) { Complete(f); return; }
                var actor = r.Targets[r.Index];
                if (facts.IsAlive(actor) && native.BeginSlash(f, actor, f.OwnerSeat)) return;
                frames.ReplaceTop(f with { MoSimaYi = r with { Index = r.Index + 1 } });
                continue;
            }
            var choices = Choices(f);
            if (choices.Count == 0) { Complete(f); return; }
            var chooser = r.Stage == MoSimaYiStage.ChoosingLieshi && r.Index == 1 ? r.TargetSeat!.Value : f.OwnerSeat;
            frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
            prompts.Set(new(DecisionKind.ProgramTrigger, chooser,
                r.Stage == MoSimaYiStage.ChoosingSkill ? "骤袭：获得一项技能直到你的下个实际回合开始。" : "烈誓：选择一项尚未被本次发动选择的效果。",
                [], [], f.OwnerSeat)
            {
                PromptId = prompts.NextId(), IsPrivate = true, Choices = choices,
                SkillPrompt = new(f.SkillId, facts.SkillName(f.SkillId), facts.SkillName(f.SkillId), "真实已发行技能选择")
            }, facts.IsHuman(chooser));
            native.PublishState();
            return;
        }
    }

    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MoSimaYi!;
        if (r.Stage == MoSimaYiStage.ChoosingSkill)
            return Array.AsReadOnly(r.Skills.Select((skill, i) => Choice(f, i, facts.SkillName(skill))).ToArray());
        if (r.Stage != MoSimaYiStage.ChoosingLieshi) return [];
        var actor = r.Index == 0 ? f.OwnerSeat : r.TargetSeat!.Value;
        if (!facts.IsAlive(actor)) return [];
        var options = new[] { "废除判定区并受到技能拥有者的1点火焰伤害", "弃置所有闪", "弃置所有杀" };
        return Array.AsReadOnly(Enumerable.Range(0, 3).Where(i => i != r.FirstOption && (i != 0 || facts.HasJudgmentArea(actor)))
            .Select(i => Choice(f, i, options[i])).ToArray());
    }
    private static PromptChoice Choice(ProgramSkillFrame f, int index, string text) => new(
        new($"mo-sima-yi.{f.Id}.{f.MoSimaYi!.Stage}.{f.MoSimaYi.Index}.{index}"), text, [], [],
        new Dictionary<string, string> { ["program-action"] = "mo-sima-yi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = index.ToString(CultureInfo.InvariantCulture) });

    [ProgramAnswer("mo-sima-yi")]
    private void Answer(PromptChoice choice)
    {
        if (frames.Top is not ProgramSkillFrame { MoSimaYi: { } r } f || !Valid(f) ||
            !Choices(f).Any(c => c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) &&
                c.Parameters.OrderBy(p => p.Key).SequenceEqual(choice.Parameters.OrderBy(p => p.Key))))
            throw new InvalidOperationException("Magic Sima Yi accepts only its current private native choice.");
        var actor = r.Stage == MoSimaYiStage.ChoosingLieshi && r.Index == 1 ? r.TargetSeat!.Value : f.OwnerSeat;
        if (prompts.Current?.PlayerSeat != actor) throw new InvalidOperationException("Another character cannot answer this choice.");
        var index = int.Parse(choice.Parameters["option"], CultureInfo.InvariantCulture);
        prompts.Clear();
        if (r.Stage == MoSimaYiStage.ChoosingSkill)
        {
            frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MoSimaYi = r with { Stage = MoSimaYiStage.Children, SelectedOption = index }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            native.GrantSkill(f, r.Skills[index], temporary: true);
        }
        else
        {
            frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MoSimaYi = r with
                { Stage = index == 0 ? MoSimaYiStage.Damage : MoSimaYiStage.Children, Index = r.Index + 1,
                    FirstOption = r.Index == 0 ? index : r.FirstOption, SelectedOption = index }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            events.AdvanceFact(new MoSimaYiLieshiChosenEvent(f.Id, actor, index));
            native.ApplyLieshi(f, actor, index);
        }
        Continue(f.Id);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveMoSimaYiZhouxi, SkillProgramEffectOp.ResolveMoSimaYiLieshi)]
    private PromptChoice? AiChoice(ProgramSkillFrame f) => Choices(f).FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "1") ?? Choices(f).FirstOrDefault();

    [ProgramContinuation(ProgramContinuationKind.Resume, 33900)]
    private bool Resume(long id)
    {
        if (frames.Top is not ProgramSkillFrame { MoSimaYi: not null } f || f.Id != id) return false;
        Continue(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33900)]
    private bool Movement(ProgramSkillFrame f) => f.MoSimaYi is not null && Resume(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33900)]
    private bool PaidContinuation(ProgramSkillFrame f) => Valid(f);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33900)]
    private bool MovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        Valid(f) && effect?.Op == f.MoSimaYi!.Operation && f.PendingMovementContinuation == pending &&
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33900)]
    private void Invariant(ProgramSkillFrame f)
    {
        if (f.MoSimaYi is not null && !Valid(f)) throw new InvalidOperationException("Invalid original Magic Sima Yi receipt.");
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MoSimaYiCompletedEvent(f.Id, f.MoSimaYi!.Operation));
        native.Complete(f with { MoSimaYi = null, PendingMovementContinuation = null });
    }
}
