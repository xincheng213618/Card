using System.Text.Json;
namespace CardGame.Core;
public sealed record EquipmentSuitSkillGrantRule(string Id,string SkillId,int MinimumDistinctSuits);
public sealed record EquipmentSuitSkillGrantSource(string CapabilityGrantId,string CapabilitySkillId,string CapabilitySkillInstanceId,string RuleId);
public sealed record EquipmentSuitSkillGrantChangedEvent(int OwnerSeat,string GrantId,string SkillId,string SkillInstanceId,EquipmentSuitSkillGrantSource Source,bool Added,int DistinctSuits):IGameEvent;
public sealed record EquipmentSuitProgramSourceReceipt(long FrameId,int OwnerSeat,string SkillId,string SkillInstanceId,string GameplayHash,string GrantId,EquipmentSuitSkillGrantSource Source);
public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentSuitProgramSourceReceipt? EquipmentSuitSourceReceipt { get; init; }
}
internal static class EquipmentSuitSkillGrantParser
{
    internal static IReadOnlyList<EquipmentSuitSkillGrantRule> Parse(JsonElement skill,string path)
    {
        if(!skill.TryGetProperty("equipmentSuitSkillGrants",out var array))return [];
        if(array.ValueKind!=JsonValueKind.Array || array.GetArrayLength() is <1 or >16)throw new InvalidOperationException(path+": equipment suit grants require 1..16 rules.");
        var rules=new List<EquipmentSuitSkillGrantRule>();
        foreach(var node in array.EnumerateArray())
        {
            if(node.ValueKind!=JsonValueKind.Object || node.EnumerateObject().Any(p=>p.Name is not("id" or "skillId" or "minimumDistinctSuits")))throw new InvalidOperationException(path+": invalid equipment suit grant fields.");
            string ReadId(string name)
            {
                if(!node.TryGetProperty(name,out var value)||value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString())||value.GetString()!.Length>128)throw new InvalidOperationException(path+": equipment suit grant requires valid "+name+".");
                return value.GetString()!;
            }
            if(node.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=node.EnumerateObject().Count())throw new InvalidOperationException(path+": duplicate equipment suit grant property.");
            var id=ReadId("id");var skillId=ReadId("skillId");
            if(!node.TryGetProperty("minimumDistinctSuits",out var bound)||bound.ValueKind!=JsonValueKind.Number||!bound.TryGetInt32(out var minimum))throw new InvalidOperationException(path+": equipment suit grant requires integer minimumDistinctSuits.");
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(skillId)||minimum is <1 or >4 || rules.Any(r=>r.Id==id||r.SkillId==skillId))throw new InvalidOperationException(path+": invalid or repeated equipment suit grant.");
            try{_=new ContentId(skillId);}catch(ArgumentException e){throw new InvalidOperationException(path+": invalid equipment suit grant skillId.",e);} rules.Add(new(id,skillId,minimum));
        }
        return Array.AsReadOnly(rules.ToArray());
    }
}
public enum HeQiStage { SelectingReveal,DiscardChildren,SelectingObtain,ObtainChildren }
public sealed record HeQiMaterial(int CardId,CardKind PrintedKind,CardLocation From,int OriginalSlot);
public sealed record HeQiReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int TurnNumber,int TurnOwnerSeat,int TargetSeat,int Maximum,
    HeQiStage Stage,IReadOnlyList<HeQiMaterial> Candidates,IReadOnlyList<int>? Revealed=null,IReadOnlyList<HeQiMaterial>? Paid=null,long Before=0,long After=0,int? ObtainedCardId=null)
{
    private readonly IReadOnlyList<HeQiMaterial> _candidates=Array.AsReadOnly(Candidates.ToArray());
    public IReadOnlyList<HeQiMaterial> Candidates {get=>_candidates;init=>_candidates=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<int> _revealed=Array.AsReadOnly((Revealed??[]).ToArray());
    public IReadOnlyList<int> Revealed {get=>_revealed;init=>_revealed=Array.AsReadOnly((value??[]).ToArray());}
    private readonly IReadOnlyList<HeQiMaterial> _paid=Array.AsReadOnly((Paid??[]).ToArray());
    public IReadOnlyList<HeQiMaterial> Paid {get=>_paid;init=>_paid=Array.AsReadOnly((value??[]).ToArray());}
}
public sealed record HeQiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int InstructionIndex,int TurnNumber,int TargetSeat,int Maximum):IGameEvent;
public sealed record HeQiSegmentPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,HeQiStage Stage,long Before,long After,int Count):IGameEvent;
internal interface IHeQiProgramHost {SkillProgramStepOutcome BeginHeQi(ProgramSkillFrame frame,int target);}
internal sealed class RevealJointHandsAndDiscardDodgesDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RevealJointHandsAndDiscardDodges;
    public override ISkillProgramEffectHandler Handler {get;}=new RevealJointHandsAndDiscardDodgesHandler();
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.DiscardSelected,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r){r.AllowOnly("op","target","condition");if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.SelectedTarget)throw new InvalidOperationException("Joint reveal requires selected foreign participant.");var e=new SkillProgramEffect(Op,SkillProgramEffectTarget.SelectedTarget,0,r.Condition());RequireAlways(e,r.Path);return e;}
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class RevealJointHandsAndDiscardDodgesHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.RevealJointHandsAndDiscardDodges;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IHeQiProgramHost>().BeginHeQi(f,seat);
}
internal static class HeQiComposition
{
    internal static void Activation(string path,SkillProgramActivation a)
    {
        if(!a.Effects.Any(e=>e.Op==SkillProgramEffectOp.RevealJointHandsAndDiscardDodges))return;
        if(a.Effects is not[{Op:SkillProgramEffectOp.RevealJointHandsAndDiscardDodges}] || a.MinCards!=0 || a.MaxCards!=0 || a.MinTargets!=1 || a.MaxTargets!=1 || a.TargetKind!=SkillProgramTargetKind.OtherLiving || a.UsesPerPhase!=1 || a.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": joint reveal requires one actual Play activation and no initial card payment.");
    }
}
