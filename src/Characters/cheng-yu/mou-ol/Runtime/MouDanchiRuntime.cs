using System.Globalization;

namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed partial class MouDanchiRuntime(IProgramRuntimeFrames frames, IProgramRuntimeEvents events,
    IProgramRuntimePrompts prompts, IMouChengYuFacts facts, IMouChengYuNative native,
    IProgramHealthChildQueries health, IProgramHealthChildOperations healthOperations)
{
    internal void AcceptUse(long useId, CardActionContext action)
    {
        var next = facts.History<MouDanchiNextUseEvent>();
        foreach (var promise in facts.History<MouDanchiPromiseEvent>().Where(p => p.Damage.Turn == facts.Turn &&
            p.Damage.SourceSeat == action.ActorSeat && p.Damage.OriginalActionId != action.ActionId &&
            (action.Type != CardActionType.Use || p.Damage.OriginalUseId != useId) && !next.Any(n => n.PromiseId == p.FrameId)))
            events.AdvanceFact(new MouDanchiNextUseEvent(promise.FrameId, useId, action.ActionId, action.ActorSeat,
                action.EffectiveKind, Category(action.EffectiveKind), facts.Turn));
    }
    internal static SkillProgramCardCategory Category(CardKind kind) => CardUseCategoryCatalog.Get(kind) switch
    {
        CardUseCategories.Basic => SkillProgramCardCategory.Basic,
        CardUseCategories.Equipment => SkillProgramCardCategory.Equipment,
        _ => SkillProgramCardCategory.Trick
    };
    internal MouDanchiPromiseEvent? RewardPromise(int owner, long actionId) => facts.History<MouDanchiPromiseEvent>().FirstOrDefault(p =>
        p.Source.OwnerSeat == owner && p.Damage.Turn == facts.Turn &&
        facts.History<MouDanchiNextUseEvent>().Any(n => n.PromiseId == p.FrameId && n.ActionId == actionId) &&
        !facts.History<MouDanchiCompletedEvent>().Any(c => c.PromiseId == p.FrameId && c.Reward));
    internal bool DamageAvailable(int owner) => !facts.History<MouDanchiAcceptedEvent>().Any(e => e.Source.OwnerSeat == owner && e.Damage.Turn == facts.Turn);

    internal SkillProgramStepOutcome Resolve(ProgramSkillFrame f)
    {
        var source = new CardConversionSource(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
        if (f.MouDanchi is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.TriggerId == "nearby-damage-type")
        {
            if (facts.Damage(f) is not { } damage || !DamageAvailable(f.OwnerSeat))
                throw new InvalidOperationException("Danchi needs its unused nearby native damage occurrence.");
            frames.ReplaceTop(f = f with { MouDanchi = new(source, f.GameplayHash, damage.WindowId, f.Id, damage.Turn, damage.VictimSeat)
                { Stage = MouDanchiStage.TypeChoice } });
            events.AdvanceFact(new MouDanchiAcceptedEvent(f.Id, source, f.GameplayHash, damage));
            Publish(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        var action = facts.Action(f);
        if (f.TriggerId != "source-next-use" || RewardPromise(f.OwnerSeat, action.ActionId) is not { } promise ||
            promise.Source.SkillInstanceId != f.SkillInstanceId || promise.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Danchi requires the frozen source's first later true Use to finish.");
        var next = facts.History<MouDanchiNextUseEvent>().Single(n => n.PromiseId == promise.FrameId);
        frames.ReplaceTop(f = f with { MouDanchi = new(source, f.GameplayHash, f.WindowContext!.ParentFrameId, promise.FrameId, facts.Turn, promise.Damage.VictimSeat)
            { Stage = MouDanchiStage.DrawIssuing, Chosen = promise.Category, Different = promise.Category != next.Category, NextActionId = action.ActionId } });
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    internal bool Valid(ProgramSkillFrame f) => f.MouDanchi is { } r && f.InstructionIndex == 1 && r.Source ==
        new CardConversionSource(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId) && r.GameplayHash == f.GameplayHash &&
        r.WindowId == f.WindowContext?.ParentFrameId && facts.NativeParentMatches(f) &&
        (f.TriggerId == "nearby-damage-type"
            ? facts.History<MouDanchiAcceptedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash) == 1
            : facts.History<MouDanchiPromiseEvent>().Any(p => p.FrameId == r.PromiseId && p.Source.OwnerSeat == f.OwnerSeat &&
                p.Source.SkillInstanceId == f.SkillInstanceId && p.GameplayHash == r.GameplayHash && p.Damage.Turn == r.Turn) &&
              facts.History<MouDanchiNextUseEvent>().Count(n => n.PromiseId == r.PromiseId && n.ActionId == r.NextActionId) == 1);

    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouDanchi: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Danchi lost its issued source, content or actual native parent.");
            if (r.Stage == MouDanchiStage.NativeUse) return;
            if (r.Stage == MouDanchiStage.Children)
            {
                if (native.DrainChildren(f)) return;
                frames.ReplaceTop(f = f with { PendingMovementContinuation = null, MouDanchi = r with
                    { Stage = r.Kind == CardKind.DrawTwo && r.Different ? MouDanchiStage.SlashChoice : MouDanchiStage.Complete } });
                continue;
            }
            if (facts.GameOver || !facts.Alive(f.OwnerSeat) || r.Stage == MouDanchiStage.Complete) { Complete(f); return; }
            if (r.Stage == MouDanchiStage.DrawIssuing)
            {
                if (native.IssueReward(f, CardKind.DrawTwo, f.OwnerSeat)) return;
                frames.ReplaceTop(f with { MouDanchi = r with { Stage = r.Different ? MouDanchiStage.SlashChoice : MouDanchiStage.Complete } });
                continue;
            }
            if (r.Stage == MouDanchiStage.SlashChoice && facts.SlashTargets(f.OwnerSeat).Count == 0) { Complete(f); return; }
            Publish(f); return;
        }
    }
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        if (f.MouDanchi is not { } r) return [];
        if (r.Stage == MouDanchiStage.TypeChoice)
            return Array.AsReadOnly(new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
                .Select(c => Choice(f, c.ToString(), c switch { SkillProgramCardCategory.Basic => "基本牌", SkillProgramCardCategory.Trick => "锦囊牌", _ => "装备牌" }, [])).ToArray());
        if (r.Stage != MouDanchiStage.SlashChoice) return [];
        return Array.AsReadOnly(facts.SlashTargets(f.OwnerSeat).Select(s => Choice(f, "slash", "视为对该角色使用杀", [s]))
            .Append(Choice(f, "decline", "不额外使用杀", [])).ToArray());
    }
    private static PromptChoice Choice(ProgramSkillFrame f, string verb, string text, IReadOnlyList<int> targets) => new(
        new($"mou-danchi.{f.Id}.{f.MouDanchi!.Stage}.{verb}.{string.Join('.', targets)}"), text, [], targets,
        new Dictionary<string, string> { ["program-action"] = "mou-danchi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = verb });
    private void Publish(ProgramSkillFrame f)
    {
        var chooser = f.MouDanchi!.Stage == MouDanchiStage.TypeChoice ? f.MouDanchi.VictimSeat : f.OwnerSeat;
        var choices = Choices(f);
        prompts.Set(new(DecisionKind.ProgramTrigger, chooser, "胆持：选择本次效果。", [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, "胆持", "胆持", "受伤角色选择类型；原伤害来源下一张真实用牌完成后结算奖励。") }, facts.Human(chooser));
        native.PublishState();
    }
    [ProgramAnswer("mou-danchi")]
    private void Answer(PromptChoice selected)
    {
        if (frames.Top is not ProgramSkillFrame { MouDanchi: { } r } f || !Valid(f) || !Choices(f).Any(c => MouLidanRuntime.Same(c, selected)) ||
            prompts.Current?.PlayerSeat != (r.Stage == MouDanchiStage.TypeChoice ? r.VictimSeat : f.OwnerSeat))
            throw new InvalidOperationException("Only the actual Danchi chooser may accept its current canonical option.");
        prompts.Clear();
        if (r.Stage == MouDanchiStage.TypeChoice)
        {
            var accepted = facts.History<MouDanchiAcceptedEvent>().Single(e => e.FrameId == f.Id);
            var category = Enum.Parse<SkillProgramCardCategory>(selected.Parameters["verb"]);
            events.AdvanceFact(new MouDanchiPromiseEvent(f.Id, r.Source, r.GameplayHash, accepted.Damage, category));
            Complete(f); return;
        }
        if (selected.Targets is [var target] && native.IssueReward(f, CardKind.Slash, target)) return;
        Complete(f);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouChengYuDanchi)]
    private PromptChoice Ai(PendingDecision decision, ProgramSkillFrame f) => f.MouDanchi!.Stage == MouDanchiStage.TypeChoice
        ? decision.Choices.First(c => c.Parameters["verb"] == "Basic") : decision.Choices.First(c => c.Parameters["verb"] == "decline");
    [ProgramContinuation(ProgramContinuationKind.Resume, 34101)]
    private bool Resume(long id)
    {
        if (frames.Top is not ProgramSkillFrame { MouDanchi: not null } f || f.Id != id) return false;
        Continue(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 34101)]
    private bool Movement(ProgramSkillFrame f) => f.MouDanchi is not null && Resume(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 34101)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 34101)]
    private bool Paid(ProgramSkillFrame f) => f.MouDanchi is not null && Valid(f) ||
        f.SkillId == MouChengYuComposition.Danchi && f.TriggerId == "source-next-use" && facts.NativeParentMatches(f) &&
        RewardPromise(f.OwnerSeat, facts.Action(f).ActionId) is { } p && p.GameplayHash == f.GameplayHash && p.Source.SkillInstanceId == f.SkillInstanceId;
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 34101)]
    private bool MovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.MouDanchi is { Stage: MouDanchiStage.Children } && Valid(f) && effect?.Op == SkillProgramEffectOp.ResolveMouChengYuDanchi &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 34101)]
    private void Invariant(ProgramSkillFrame f)
    {
        if (f.MouDanchi is not null && !Valid(f)) throw new InvalidOperationException("Invalid Danchi original damage and next-use receipt.");
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MouDanchiCompletedEvent(f.Id, f.MouDanchi!.PromiseId, f.TriggerId == "source-next-use"));
        native.Finish(f with { MouDanchi = null, PendingMovementContinuation = null });
    }
}
