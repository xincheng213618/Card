using System.Globalization;

namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed class MouLidanRuntime(IProgramRuntimeFrames frames, IProgramRuntimeEvents events,
    IProgramRuntimePrompts prompts, IMouChengYuFacts facts, IMouChengYuNative native)
{
    internal static bool Slash(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    internal static bool Trick(CardKind kind) => CardUseCategoryCatalog.Get(kind) is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick;

    internal void AcceptUse(CardUseFrame use)
    {
        if (use.MouLidan is not null || use.Action is not { Type: CardActionType.Use } action || !Slash(action.EffectiveKind) && !Trick(action.EffectiveKind)) return;
        var consumed = facts.History<MouLidanConsumedEvent>();
        var permits = facts.History<MouLidanPermitEvent>().Where(p => p.Source.OwnerSeat == action.ActorSeat &&
            p.ForSlash == Slash(action.EffectiveKind) && !consumed.Any(c => c.PermitFrameId == p.FrameId && c.PermitActionId == p.ActionId)).ToArray();
        if (permits.Length == 0) return;
        var first = permits[0];
        var source = first.Source with { BindingId = "next-matching-targets" };
        var receipt = new MouLidanUseReceipt(source, first.GameplayHash, use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, permits.Length);
        native.FreezeLidan(use, receipt);
        foreach (var permit in permits)
            events.AdvanceFact(new MouLidanConsumedEvent(permit.FrameId, permit.ActionId, permit.Source, use.Id, action.ActionId, action.ActorSeat));
        events.AdvanceFact(new MouLidanUseFrozenEvent(use.Id, action.ActionId, source, first.GameplayHash, action.ActorSeat, action.ProviderSeat, permits.Length));
    }

    internal SkillProgramStepOutcome Resolve(ProgramSkillFrame f)
    {
        var action = facts.Action(f);
        if (f.TriggerId == "alternating-use-permit")
        {
            if (Slash(action.EffectiveKind) || Trick(action.EffectiveKind))
            {
                if (facts.History<MouLidanPermitEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.ActionId == action.ActionId))
                    throw new InvalidOperationException("Lidan cannot issue the same completed Use permission twice.");
                events.AdvanceFact(new MouLidanPermitEvent(f.Id, Source(f), f.GameplayHash, action.ActionId, action.EffectiveKind, !Slash(action.EffectiveKind)));
            }
            return SkillProgramStepOutcome.Continue;
        }
        var use = facts.TargetUse(f);
        if (f.TriggerId != "next-matching-targets" || use?.MouLidan is not { Offered: false } r ||
            r.Source != Source(f) || r.GameplayHash != f.GameplayHash || action.ActionId != r.ActionId || f.InstructionIndex != 1)
            throw new InvalidOperationException("Lidan requires the original matching native Use and its frozen permission stack.");
        frames.ReplaceTop(f = f with { MouLidanSelection = new(use.Id, r.ActionId, r.Maximum) });
        if (facts.TargetOptions(use.Id, []).Count == 0) Complete(f);
        else Publish(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouLidanSelection!;
        var result = new List<PromptChoice>();
        var use = facts.Use(r.UseId)!;
        var width = use.CardKind == CardKind.BorrowedSword ? 2 : 1;
        if (r.Selected.Count / width < r.Maximum)
            foreach (var option in facts.TargetOptions(r.UseId, r.Selected))
                result.Add(Choice(f, "add", option.Label, option.Seats));
        result.Add(Choice(f, "finish", "完成额外目标选择", []));
        return Array.AsReadOnly(result.ToArray());
    }
    private static PromptChoice Choice(ProgramSkillFrame f, string verb, string text, IReadOnlyList<int> targets) => new(
        new($"mou-lidan.{f.Id}.{verb}.{string.Join('.', targets)}"), text, [], targets,
        new Dictionary<string, string> { ["program-action"] = "mou-lidan", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = verb });
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f);
        prompts.Set(new(DecisionKind.ProgramTrigger, f.OwnerSeat, "戾断：选择本次用牌的合法额外目标。", [],
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), Choices = choices, SkillPrompt = new(f.SkillId, "戾断", "戾断", "每份已签发许可可增加一个合法目标；借刀须选择完整目标对。") }, facts.Human(f.OwnerSeat));
        native.PublishState();
    }
    [ProgramAnswer("mou-lidan")]
    private void Answer(PromptChoice selected)
    {
        if (frames.Top is not ProgramSkillFrame { MouLidanSelection: { } r } f || !Valid(f) ||
            prompts.Current?.PlayerSeat != f.OwnerSeat || !Choices(f).Any(c => Same(c, selected)))
            throw new InvalidOperationException("Lidan accepts only its current canonical whole-target choice.");
        prompts.Clear();
        if (selected.Parameters["verb"] == "finish") Complete(f);
        else
        {
            frames.ReplaceTop(f = f with { MouLidanSelection = r with { Selected = r.Selected.Concat(selected.Targets).ToArray() } });
            var width = facts.Use(r.UseId)!.CardKind == CardKind.BorrowedSword ? 2 : 1;
            if (f.MouLidanSelection!.Selected.Count / width == r.Maximum || facts.TargetOptions(r.UseId, f.MouLidanSelection.Selected).Count == 0) Complete(f);
            else Publish(f);
        }
    }
    private void Complete(ProgramSkillFrame f)
    {
        native.ApplyTargets(f, f.MouLidanSelection!);
        native.Finish(f with { MouLidanSelection = null });
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouLidanSelection is { } r && f.InstructionIndex == 1 &&
        facts.NativeParentMatches(f) && facts.Use(r.UseId) is { MouLidan: { Offered: false } receipt, Action: { } action } &&
        action.ActionId == r.ActionId && receipt.Maximum == r.Maximum && receipt.Source == Source(f) && receipt.GameplayHash == f.GameplayHash;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouChengYuLidan)]
    private PromptChoice Ai(PendingDecision decision, ProgramSkillFrame f) => decision.Choices.Single(c => c.Parameters["verb"] == "finish");
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 34100)]
    private bool PaidSource(ProgramSkillFrame f) => f.MouLidanSelection is not null && Valid(f) || facts.TargetUse(f)?.MouLidan is { Offered: false } r && r.Source == Source(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 34100)]
    private void Invariant(ProgramSkillFrame f)
    {
        if (f.MouLidanSelection is not null && !Valid(f)) throw new InvalidOperationException("Lidan lost its owning designation window or paid permissions.");
    }
    internal static bool Same(PromptChoice a, PromptChoice b) => a.Id == b.Id && a.Cards.SequenceEqual(b.Cards) &&
        a.Targets.SequenceEqual(b.Targets) && a.Parameters.OrderBy(p => p.Key).SequenceEqual(b.Parameters.OrderBy(p => p.Key));
}
