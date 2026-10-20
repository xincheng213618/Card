using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record DaQiaoXiaoQiaoLuoyanSource(string CapabilityGrantId,string CapabilitySkillInstanceId,string LeafSkillId);
public sealed record DaQiaoXiaoQiaoLuoyanChangedEvent(int OwnerSeat,string GrantId,string SkillId,string InstanceId,DaQiaoXiaoQiaoLuoyanSource Source,bool Added):IGameEvent;
internal sealed class MaintainLuoyanPileSkillsDescriptor():DaQiaoXiaoQiaoDescriptor(SkillProgramEffectOp.MaintainLuoyanPileSkills){}
