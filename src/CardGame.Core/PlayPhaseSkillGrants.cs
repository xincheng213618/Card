namespace CardGame.Core;

public sealed record ProgramPlayPhaseSkillsGrantedEvent(
    long FrameId, CardConversionSource Source, string GameplayHash,
    int RecipientSeat, SkillGrantPhaseExpiry Expiry, IReadOnlyList<SkillGrant> Grants) : IGameEvent
{
    private readonly IReadOnlyList<SkillGrant> _grants = Array.AsReadOnly(Grants.ToArray());
    public IReadOnlyList<SkillGrant> Grants
    {
        get => _grants;
        init => _grants = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record ProgramPlayPhaseSkillGrantExpiredEvent(
    int RecipientSeat, SkillGrant Grant, string Reason) : IGameEvent;

public sealed record ProgramPlayPhaseSkillBoundaryClosedEvent(
    SkillGrantPhaseExpiry Expiry, string Reason) : IGameEvent;

internal static class PlayPhaseSkillGrantContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantPlayPhaseSkills)) return;
        if (trigger.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
            trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own)
            throw new InvalidOperationException($"Invalid skill program at {path}: play-phase skill grants require an own owner PlayPhaseStarting trigger or an activation.");
    }
}

internal interface IPlayPhaseSkillGrantHost
{
    void GrantPlayPhaseSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds);
}

public sealed class GrantPlayPhaseSkillsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPlayPhaseSkills;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        if (targetSeat != frame.OwnerSeat || host is not IPlayPhaseSkillGrantHost phaseHost)
            throw new InvalidOperationException("A play-phase skill grant requires its exact owner host.");
        phaseHost.GrantPlayPhaseSkills(frame, effect.SkillIds);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class GrantPlayPhaseSkillsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantPlayPhaseSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantPlayPhaseSkillsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GrantTurnSkills, static (effect, context) => context.GrantTurnSkills(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "skillIds", "condition");
        var ids = reader.RequiredIdentifierArray("skillIds");
        if (ids.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.skillIds: play-phase grants require at least one distinct skill.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader),
            0, reader.Condition(), skillIds: ids);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    // The trigger contract supplies the narrow lifecycle restriction. An
    // activation has no trigger window and uses the same exact runtime guard.
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
