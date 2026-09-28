namespace CardGame.Core;

public sealed record CharacterSkillsLostEvent(int Seat, int SourceSeat, IReadOnlyList<string> SkillIds) : IGameEvent;

internal sealed class LoseDeathSourceSkillsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseDeathSourceSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new LoseDeathSourceSkillsSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Death;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.ClaimDeathCleanupCards, static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: death source skill loss requires owner.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class LoseDeathSourceSkillsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LoseDeathSourceSkills;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.LoseDeathSourceSkills(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed partial class GameEngine
{
    private void LoseProgramDeathSourceSkills(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext;
        if (context?.Window != SkillProgramTriggerWindow.OwnerDied)
            throw new InvalidOperationException("Death source skill loss requires an owner-death window.");
        if (context.SourceSeat is not { } sourceSeat || !IsValidPlayerSeat(sourceSeat)) return;

        var source = _players[sourceSeat];
        var lost = source.SkillGrants.Grants.Where(grant =>
                grant.IsEnabled && !grant.SourceId.StartsWith("equipment:", StringComparison.Ordinal))
            .ToArray();
        foreach (var grant in lost) source.SkillGrants.SetEnabled(grant.GrantId, false);
        QueueGameEvent(new CharacterSkillsLostEvent(sourceSeat, frame.OwnerSeat,
            Array.AsReadOnly(lost.Select(grant => grant.SkillId).Distinct(StringComparer.Ordinal).ToArray())));
        AddLog("SkillTriggered", $"{source.Name} 受到【断肠】影响，失去所有武将技能。",
            frame.OwnerSeat, sourceSeat);
    }
}
