using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SouyingQualification(int ActualTurn,long NativeUseId,long ActionId,int ActorSeat,int ProviderSeat,int TargetSeat,int DirectionOrdinal);
public sealed record SouyingNativeMutation(long ProgramFrameId,CardConversionSource Source,string GameplayHash,SouyingQualification Qualification,long PaymentBefore,long PaymentAfter,int DiscardedId,long ClaimBefore=0,long ClaimAfter=0,bool Cancelled=false)
{
 private readonly IReadOnlyList<int> _cards=Array.AsReadOnly(Array.Empty<int>());
 private readonly IReadOnlyList<int> _beforeTargets=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> BeforeTargets{get=>_beforeTargets;init=>_beforeTargets=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> ClaimedEntities{get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
}
public sealed record SouyingNativeMutationEvent(long FrameId,long NativeUseId,long ActionId,CardConversionSource Source,bool Cancelled,int ClaimedCount,long PaymentBefore,long PaymentAfter,long ClaimBefore,long ClaimAfter):IGameEvent;
internal sealed class PaySouyingDirectionalSoleTargetDescriptor:HuaManDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PaySouyingDirectionalSoleTarget;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
