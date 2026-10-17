namespace CardGame.Core;

internal interface IDragonBoatGeneralHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation, int targetSeat);
}

internal abstract class DragonBoatGeneralDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new DragonBoatGeneralHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
}
internal sealed class DrawDragonBoatConsecutiveTargetDescriptor : DragonBoatGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawDragonBoatConsecutiveTarget;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class ArmDragonBoatDianhuDescriptor : DragonBoatGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmDragonBoatDianhu;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(reader.Path + ": activity Dianhu requires its actual selected target.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting), new ReadSelectedTarget(),
            new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class DrawDragonBoatDianhuDescriptor : DragonBoatGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawDragonBoatDianhu;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DrawDragonBoatTunjiangDescriptor : DragonBoatGeneralDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawDragonBoatTunjiang;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class DragonBoatGeneralHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<IDragonBoatGeneralHost>().Execute(frame, operation, targetSeat);
}

internal interface IDragonBoatAllyQuery : IProgramSelectionQuery { bool IsTeammate(int ownerSeat, int targetSeat); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 42000, SkillProgramEffectOp.DrawAndOfferJianjiExactCard)]
internal sealed class DragonBoatJianjiTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) =>
        context.SkillId != "dragonboat:jianji" || queries.Get<IDragonBoatAllyQuery>().IsTeammate(context.OwnerSeat, targetSeat);
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 42000, SkillProgramEffectOp.DrawAndOfferJianjiExactCard)]
internal sealed class DragonBoatJianjiTargetValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Context.SkillId != "dragonboat:jianji" || request.Targets is [var target] &&
            queries.Get<IDragonBoatAllyQuery>().IsTeammate(request.Context.OwnerSeat, target)
            ? null : new(CommandErrorCode.InvalidTarget, "活动谏计只能令当前队友摸牌并使用该牌。");
}
