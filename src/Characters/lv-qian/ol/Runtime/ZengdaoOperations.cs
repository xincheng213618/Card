using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZengdaoPublicGrant(CardConversionSource Source,string GameplayHash,long ProducerFrameId,int RecipientSeat,CardLocation Location,long Before,long After)
{
 private readonly IReadOnlyList<int> _entities=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
}
public sealed record ZengdaoEntityChoice(int CardId,ZengdaoPublicGrant Grant);
public sealed record ZengdaoDamagePaidEvent(long FrameId,long DamageAttemptResolutionId,CardConversionSource Source,int RecipientSeat,int TargetSeat,int CardId,long Before,long After):IGameEvent;
internal sealed class DepositZengdaoActualEquipmentDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DepositZengdaoActualEquipment;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new ConsumeSelectedCards(0)];}
internal sealed class ConsumeZengdaoForActualSourceDamageDescriptor:LvQianDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
