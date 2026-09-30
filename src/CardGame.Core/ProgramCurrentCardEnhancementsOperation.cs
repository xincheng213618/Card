namespace CardGame.Core;

[Flags]
public enum CurrentCardEnhancement { None = 0, ExtraTarget = 1, IgnoreArmor = 2, Uncancelable = 4, DrawAfterDamage = 8 }
public sealed record ProgramCardEnhancementDraft(long CardUseFrameId, IReadOnlyList<CurrentCardEnhancement> Selected, bool ChoosingExtraTarget = false);
public sealed record CurrentCardEnhancedEvent(long CardUseFrameId, long CardActionId, int OwnerSeat, CurrentCardEnhancement Enhancement, int? ExtraTargetSeat = null) : IGameEvent;
internal interface ICurrentCardEnhancementProgramHost
{
    SkillProgramStepOutcome ApplyCurrentCardEnhancements(ProgramSkillFrame frame, int maximum);
}
internal sealed class ApplyCurrentCardEnhancementsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyCurrentCardEnhancements;
    public override ISkillProgramEffectHandler Handler { get; } = new ApplyCurrentCardEnhancementsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || r.RequiredInt("amount") is < 1 or > 4)
            throw new InvalidOperationException("Current-card enhancements require an owner and maximum 1..4.");
        return new(Op, SkillProgramEffectTarget.Owner, r.RequiredInt("amount"), r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
public sealed class ApplyCurrentCardEnhancementsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyCurrentCardEnhancements;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ICurrentCardEnhancementProgramHost)host).ApplyCurrentCardEnhancements(frame, effect.Amount);
}
