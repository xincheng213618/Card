namespace CardGame.Core;

internal interface IActionCategoryGiftProgramHost
{
    SkillProgramStepOutcome ChooseDifferentActionCategoryGift(ProgramSkillFrame frame, int targetSeat,
        string resultBind, IReadOnlyList<CardZoneKind> zones);
}

internal sealed class ChooseDifferentActionCategoryGiftProgramDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseDifferentActionCategoryGift;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseDifferentActionCategoryGiftProgramHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "resultBind", "condition");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget ||
            zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: a category gift requires a selected other participant's hand or equipment.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition(),
            zones: zones, resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted), new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),
            new CreateChoiceResult(effect.ResultBind!, ["gifted", "declined"])];
}

public sealed class ChooseDifferentActionCategoryGiftProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseDifferentActionCategoryGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IActionCategoryGiftProgramHost)host).ChooseDifferentActionCategoryGift(frame, targetSeat,
            effect.ResultBind!, effect.Zones);
}
