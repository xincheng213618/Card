namespace CardGame.Core;
internal interface IXuanGongZhuProgramHost
{
    SkillProgramStepOutcome ArmQimeiPair(ProgramSkillFrame frame);
    SkillProgramStepOutcome SettleQimeiPairEquality(ProgramSkillFrame frame);
    SkillProgramStepOutcome OfferZhuijiActualPlayChoice(ProgramSkillFrame frame);
    SkillProgramStepOutcome SettleZhuijiActualPlayDebt(ProgramSkillFrame frame);
    SkillProgramStepOutcome AfterActualIdentityAppearanceRecover(ProgramSkillFrame frame);
}
internal abstract class XuanGongZhuDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new XuanGongZhuHandler(Op);
}
internal sealed class ArmQimeiPairDescriptor : XuanGongZhuDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ArmQimeiPair;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),new ReadSelectedTarget()];
}
internal sealed class SettleQimeiPairEqualityDescriptor : XuanGongZhuDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleQimeiPairEquality;
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Automatic;
    // The producer supplies either an actual movement or HP-change receipt.
    // Its exact native window is enforced by the operation's trigger contract.
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class OfferZhuijiActualPlayChoiceDescriptor : XuanGongZhuDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferZhuijiActualPlayChoice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class SettleZhuijiActualPlayDebtDescriptor : XuanGongZhuDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleZhuijiActualPlayDebt;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class AfterActualIdentityAppearanceRecoverDescriptor:XuanGongZhuDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AfterActualIdentityAppearanceRecover;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.IdentityGeneralAppeared),new ReadSelectedTarget()];
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Automatic;
}
internal sealed class XuanGongZhuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>op switch
    {
        SkillProgramEffectOp.ArmQimeiPair=>h.GetCapability<IXuanGongZhuProgramHost>().ArmQimeiPair(f),
        SkillProgramEffectOp.SettleQimeiPairEquality=>h.GetCapability<IXuanGongZhuProgramHost>().SettleQimeiPairEquality(f),
        SkillProgramEffectOp.OfferZhuijiActualPlayChoice=>h.GetCapability<IXuanGongZhuProgramHost>().OfferZhuijiActualPlayChoice(f),
        SkillProgramEffectOp.SettleZhuijiActualPlayDebt=>h.GetCapability<IXuanGongZhuProgramHost>().SettleZhuijiActualPlayDebt(f),
        SkillProgramEffectOp.AfterActualIdentityAppearanceRecover=>h.GetCapability<IXuanGongZhuProgramHost>().AfterActualIdentityAppearanceRecover(f),
        _=>throw new InvalidOperationException("Unknown Xuan Gong Zhu operation.")
    };
}
