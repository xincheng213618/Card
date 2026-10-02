namespace CardGame.Core;

internal sealed record CreateHpPairSnapshot : ProgramResourceOperation;
internal sealed record ReadHpPairSnapshot : ProgramResourceOperation;
internal sealed record ReadCapturedPlacementCard(string Name) : ProgramResourceOperation;

internal sealed class PlaceSelectedEquipmentOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceSelectedEquipment;
    public override ISkillProgramEffectHandler Handler { get; } = new PlaceSelectedEquipmentOperationHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.PlaceSelectedEquipment());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("Equipment placement requires one selected recipient.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 1,
            reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new ReadCapturedPlacementCard(effect.SourceBind!), new ReadSingleCardSet(effect.SourceBind!),
            new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.SelectedTargetEquipment)];
}

internal sealed class FreezeSelectedHpPairOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.FreezeSelectedHpPair;
    public override ISkillProgramEffectHandler Handler { get; } = new FreezeSelectedHpPairOperationHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Filter,
        static (_, context) => context.FrozenHpPairBenefits());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("HP pair freezing belongs to the owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new CreateHpPairSnapshot()];
}

public sealed class PlaceSelectedEquipmentOperationHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceSelectedEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.PlaceSelectedEquipment(frame, targetSeat, effect.SourceBind!);
}

public sealed class FreezeSelectedHpPairOperationHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.FreezeSelectedHpPair;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.FreezeSelectedHpPair(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed record ProgramHpPairSnapshot(int InstructionIndex, int OwnerSeat, int SelectedSeat,
    int OwnerHp, int SelectedHp, bool ParticipantsAlive, int? HigherSeat, int? LowerSeat);
