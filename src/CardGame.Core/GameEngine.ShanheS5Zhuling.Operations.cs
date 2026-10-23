namespace CardGame.Core;

internal interface IShanheS5JixianProgramHost
{
    SkillProgramStepOutcome ResolveShanheS5Jixian(ProgramSkillFrame frame, string stage);
}

internal sealed class ResolveShanheS5JixianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Jixian;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheS5JixianHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) =>
        {
            // Every legal target guarantees at least one draw; public target selection
            // and native Slash AI remain shared. This is an opportunity estimate.
            if (effect.StateId == "draw") context.Draw(new(SkillProgramEffectOp.Draw,
                SkillProgramEffectTarget.Owner, 1, effect.Condition));
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stage", "condition");
        var stage = r.RequiredIdentifier("stage");
        if (stage is not ("prepare" or "draw" or "penalty"))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.stage: requires prepare, draw or penalty.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId: stage);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        e.StateId == "prepare"
            ? [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded), new ReadSelectedTarget()]
            : [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded)];
}

public sealed class ResolveShanheS5JixianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Jixian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheS5JixianProgramHost>().ResolveShanheS5Jixian(frame, effect.StateId!);
}

internal static class ShanheS5JixianComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Jixian)) return;
        if (window != SkillProgramTriggerWindow.DrawPhaseEnded || effects is not [
                { Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLivingShanheS5Jixian },
                { Op: SkillProgramEffectOp.ResolveShanheS5Jixian, StateId: "prepare" },
                { Op: SkillProgramEffectOp.UseVirtualSlash, Target: SkillProgramEffectTarget.SelectedTarget, TargetReference: null },
                { Op: SkillProgramEffectOp.ResolveShanheS5Jixian, StateId: "draw" },
                { Op: SkillProgramEffectOp.ResolveShanheS5Jixian, StateId: "penalty" }] ||
            effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException($"Invalid S5 Jixian composition at {path}: requires actual Draw end, one qualified target, frozen conditions, native Slash, draw and original-target penalty.");
    }
}

// Scalar-only public identities. The normal native use and draw facts own all card collections.
public sealed record ShanheS5JixianFrozenEvent(long FrameId, string SkillId, string SkillInstanceId, string GameplayHash,
    long DrawWindowId, int OwnerSeat, int TargetSeat, int ActualTurnNumber, int ConditionMask, int X,
    int OwnerSkillCount, int TargetSkillCount) : IGameEvent;
public sealed record ShanheS5JixianSlashIssuedEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, int OriginalTargetSeat) : IGameEvent;
public sealed record ShanheS5JixianDrawPaidEvent(long FrameId, long UseId, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record ShanheS5JixianResolvedEvent(long FrameId, long UseId, int OriginalTargetSeat, bool DamagedOriginalTarget, bool HpPenalty) : IGameEvent;
