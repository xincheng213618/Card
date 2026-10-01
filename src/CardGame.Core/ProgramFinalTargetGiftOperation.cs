namespace CardGame.Core;

internal interface IFinalTargetGiftHost
{
    SkillProgramStepOutcome GiveOwnedCardToOtherFinalTargetAndDraw(ProgramSkillFrame frame);
}
// One real HE gift to a different finalized Trick target; physical Equipment rewards two, otherwise one.
internal sealed class GiveOwnedCardToOtherFinalTargetAndDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveOwnedCardToOtherFinalTargetAndDrawHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,static(e,c)=>c.FinalTargetGift());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition());RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
public sealed class GiveOwnedCardToOtherFinalTargetAndDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.GiveOwnedCardToOtherFinalTargetAndDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IFinalTargetGiftHost)h).GiveOwnedCardToOtherFinalTargetAndDraw(f);
}
public sealed record ProgramFinalTargetGiftDraft(long ActionId,string Stage,int? RecipientSeat=null,int? CardId=null,int Reward=0,int? ReceiptOrdinal=null);
public sealed record ProgramFinalTargetGiftCommittedEvent(long FrameId,long ActionId,int OwnerSeat,string SkillId,string SkillInstanceId,int RecipientSeat,int CardId,int Reward,int? ReceiptOrdinal) : IGameEvent;
