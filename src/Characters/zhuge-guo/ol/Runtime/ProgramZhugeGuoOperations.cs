namespace CardGame.Core;
public enum ZhugeGuoStage { GainChildren,ExtraTargetChoice,YuhuaInspect }
public sealed record ZhugeGuoReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,long WindowId,ZhugeGuoStage Stage,
    long? UseId=null,long? ActionId=null,CardActionContext? OriginalAction=null,int? GainedCardId=null,CardKind? GainedKind=null,long Before=0,long After=0,
    QirangUseQualification? Qualification=null,int RequestedPeek=0,IReadOnlyList<int>? Viewed=null,bool PlacementIssued=false,IReadOnlyList<int>? PlacedTop=null,IReadOnlyList<int>? PlacedBottom=null)
{
    private readonly IReadOnlyList<int> _viewed=Array.AsReadOnly((Viewed??[]).ToArray());
    public IReadOnlyList<int> Viewed {get=>_viewed;init=>_viewed=Array.AsReadOnly((value??[]).ToArray());}
    private readonly IReadOnlyList<int> _placedTop=Array.AsReadOnly((PlacedTop??[]).ToArray());
    public IReadOnlyList<int> PlacedTop {get=>_placedTop;init=>_placedTop=Array.AsReadOnly((value??[]).ToArray());}
    private readonly IReadOnlyList<int> _placedBottom=Array.AsReadOnly((PlacedBottom??[]).ToArray());
    public IReadOnlyList<int> PlacedBottom {get=>_placedBottom;init=>_placedBottom=Array.AsReadOnly((value??[]).ToArray());}
}
internal interface IZhugeGuoProgramHost {SkillProgramStepOutcome BeginZhugeGuo(ProgramSkillFrame f,SkillProgramEffect e);}
internal abstract class ZhugeGuoDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class ZhugeGuoHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IZhugeGuoProgramHost>().BeginZhugeGuo(f,e);
}
internal static class ZhugeGuoComposition
{
    internal static void Activation(string path,SkillProgramActivation a)
    {
        if(a.Effects.Any(e=>e.Op is SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse or SkillProgramEffectOp.AddTargetForQirangPhysicalTrick or SkillProgramEffectOp.InspectYuhuaTop))throw new InvalidOperationException(path+": ZhugeGuo operations require native trigger windows, not activations.");
    }
    internal static void Trigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(e=>e.Op is SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse or SkillProgramEffectOp.AddTargetForQirangPhysicalTrick or SkillProgramEffectOp.InspectYuhuaTop))return;
        if(t.Effects is not[var e] || e.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": ZhugeGuo requires one exact unconditional native effect.");
        if(e.Op==SkillProgramEffectOp.InspectYuhuaTop && (t.Window is not(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding)||t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own))throw new InvalidOperationException(path+": Yuhua requires actual own prepare/ending mandatory entry.");
        if(e.Op is SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse or SkillProgramEffectOp.AddTargetForQirangPhysicalTrick && (t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor))throw new InvalidOperationException(path+": Qirang requires actual actor's finalized native equipment/trick Use.");
    }
}

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ZhugeGuoReceipt? ZhugeGuo {get;init;}
}
public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public QirangUseQualification? QirangQualification {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public QirangEquipmentOrigin? QirangEquipmentOrigin {get;init;}
}
public sealed partial class CharacterState
{
    private IReadOnlyList<QirangEntityLease>? _qirangEntityLeases;
    internal IReadOnlyList<QirangEntityLease>? QirangEntityLeases {get=>_qirangEntityLeases;set=>_qirangEntityLeases=value is null?null:Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<YuhuaDelayDebt>? _yuhuaDelayDebts;
    internal IReadOnlyList<YuhuaDelayDebt>? YuhuaDelayDebts {get=>_yuhuaDelayDebts;set=>_yuhuaDelayDebts=value is null?null:Array.AsReadOnly(value.ToArray());}
    internal YuhuaActiveQuota? YuhuaActiveQuota {get;set;}
}
