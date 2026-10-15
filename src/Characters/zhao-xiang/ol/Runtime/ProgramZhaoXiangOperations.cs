using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhaoXiangConversionPayment(long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualActorSeat,int ProviderSeat,int? RequesterSeat,int PhysicalCardId,CardKind InputKind,CardKind OutputKind,int Removed,int Remaining):IGameEvent;
public sealed record ZhaoXiangMeiYingGainedEvent(long ProgramFrameId,long DamageWindowId,long DamageParentId,CardConversionSource Source,int Amount,int Total):IGameEvent;
public sealed record ZhaoXiangExchangePaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int Removed,int CumulativeRemoved,string PoolFingerprint):IGameEvent;
public sealed record ZhaoXiangExchangeSelectedEvent(long FrameId,int OwnerSeat,string OriginalGeneralId,string GeneralId,int MaximumBefore,int MaximumAfter):IGameEvent;
public sealed record ZhaoXiangConversionDrawPaidEvent(long FrameId,long ActionId,int OwnerSeat,long Before,long After,int Actual):IGameEvent;
public sealed record ZhaoXiangExchangeRecoveryIssuedEvent(long FrameId,int OwnerSeat,int Requested):IGameEvent;
public sealed record ZhaoXiangReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public long ParentFrameId {get;init;}
    public int Turn {get;init;}
    public int InstructionIndex {get;init;}
    public string Stage {get;init;}="";
    public long? ActionId {get;init;}
    public int Removed {get;init;}
    public int CumulativeRemoved {get;init;}
    private IReadOnlyList<string> _generals=Array.Empty<string>();
    public IReadOnlyList<string> Generals {get=>_generals;init=>_generals=Array.AsReadOnly(value.ToArray());}
    public string OriginalGeneralId {get;init;}="";
    public string? SelectedGeneralId {get;init;}
    public int MaximumBefore {get;init;}
    public int MaximumAfter {get;init;}
    public int RecoveryRequested {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public int ActualDraw {get;init;}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ZhaoXiangReceipt? ZhaoXiang {get;init;}
}
internal interface IZhaoXiangProgramHost
{
    void GainMeiYingAfterActualSlashDamage(ProgramSkillFrame f);
    SkillProgramStepOutcome ExchangeFiveShuBySpentMeiYing(ProgramSkillFrame f);
    SkillProgramStepOutcome DrawAfterPaidMeiYingConversion(ProgramSkillFrame f);
}
internal sealed class GainMeiYingAfterActualSlashDamageDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage;
    public override ISkillProgramEffectHandler Handler{get;}=new ZhaoXiangHandler(0);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ExchangeFiveShuBySpentMeiYingDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing;
    public override ISkillProgramEffectHandler Handler{get;}=new ZhaoXiangHandler(1);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Recover,static(e,c)=>c.Recover(new(SkillProgramEffectOp.Recover,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class DrawAfterPaidMeiYingConversionDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAfterPaidMeiYingConversion;
    public override ISkillProgramEffectHandler Handler{get;}=new ZhaoXiangHandler(2);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class ZhaoXiangHandler(int kind):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>kind switch{0=>SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage,1=>SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing,_=>SkillProgramEffectOp.DrawAfterPaidMeiYingConversion};
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)
    {
        var host=h.GetCapability<IZhaoXiangProgramHost>();if(kind==0){host.GainMeiYingAfterActualSlashDamage(f);return SkillProgramStepOutcome.Continue;}
        return kind==1?host.ExchangeFiveShuBySpentMeiYing(f):host.DrawAfterPaidMeiYingConversion(f);
    }
}
internal static class ZhaoXiangComposition
{
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage) &&
            (t.Effects is not [{Op:SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage}] || t.Window!=SkillProgramTriggerWindow.AfterDamageApplied ||
             t.Subject!=SkillProgramTriggerSubject.Any || t.Optional || t.UsageScope is not null || t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage ||
             !t.DamageCardKinds.Order().SequenceEqual(new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash}.Order()) ||
             t.MarkerCost is not null || t.NamedUsageGroup is not null || t.Condition.Kind!=SkillProgramTriggerConditionKind.Always))
            throw new InvalidOperationException(path+": proportional MeiYing requires one real Slash damage occurrence and one OR-qualified owner.");
        if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing) &&
            (t.Effects is not [{Op:SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing}] || t.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
             t.Subject!=SkillProgramTriggerSubject.Owner || t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own || !t.Optional || t.UsageScope!=SkillUsageScope.Game || t.UsageLimit!=1 ||
             t.MarkerCost is not null || t.NamedUsageGroup is not null || t.Condition.Kind!=SkillProgramTriggerConditionKind.Always))
            throw new InvalidOperationException(path+": five-Shu replacement requires an optional own preparation, game-once, source-owned all-marker payment.");
        if(t.Effects.Any(e=>e.Op==SkillProgramEffectOp.DrawAfterPaidMeiYingConversion) &&
            (t.Effects is not [{Op:SkillProgramEffectOp.DrawAfterPaidMeiYingConversion}] || t.Window is not(SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted) ||
             t.Subject is not null || t.OwnerRelation!=SkillProgramCardActionOwnerRelation.ConversionSource || t.SourceSkillId is null || !t.AllowNoEventTarget || !t.SingleActionInstance || t.Condition.Kind!=SkillProgramTriggerConditionKind.Always ||
             t.Optional || t.UsageScope is not null || t.UsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null ||
             t.CardKinds.Count!=0))
            throw new InvalidOperationException(path+": conversion reward requires its real conversion-source provider and exact already-paid material, without a second usage or marker cost.");
    }
    internal static void ValidateActivation(string path,SkillProgramActivation a)
    {
        if(a.Effects.Any(e=>e.Op is SkillProgramEffectOp.GainMeiYingAfterActualSlashDamage or SkillProgramEffectOp.ExchangeFiveShuBySpentMeiYing or SkillProgramEffectOp.DrawAfterPaidMeiYingConversion))
            throw new InvalidOperationException(path+": Zhao Xiang operations require their exact native damage, preparation or accepted-conversion window; activation borrowing is forbidden.");
    }
}
