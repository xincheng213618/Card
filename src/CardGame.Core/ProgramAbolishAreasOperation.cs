namespace CardGame.Core;

public sealed record PlayerAreasAbolishedEvent(int Seat, bool Equipment, bool Judgment) : IGameEvent;

internal sealed class AbolishOwnerAreasProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishOwnerAreas;
    public override ISkillProgramEffectHandler Handler { get; } = new AbolishOwnerAreasSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.DiscardOwnedZoneCards,
        static (effect, context) => context.DiscardOwnedZoneCards(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "zones", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: only owner areas can be abolished.");
        var zones = reader.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Distinct().Count() != zones.Count ||
            zones.Any(zone => zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.zones: requires distinct equipment or judgment areas.");
        return new SkillProgramEffect(Op, target, 0, reader.Condition(), zones: zones);
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class AbolishOwnerAreasSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishOwnerAreas;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.AbolishOwnerAreas(frame, effect.Zones);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed partial class GameEngine
{
    private void AbolishProgramOwnerAreas(ProgramSkillFrame frame, IReadOnlyList<CardZoneKind> zones)
    {
        var owner = _players[frame.OwnerSeat];
        if (zones.Contains(CardZoneKind.Equipment)) owner.EquipmentAreaAbolished = true;
        if (zones.Contains(CardZoneKind.Judgment)) owner.JudgmentAreaAbolished = true;
        QueueGameEvent(new PlayerAreasAbolishedEvent(owner.Seat,
            owner.EquipmentAreaAbolished, owner.JudgmentAreaAbolished));
    }
}
