using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record RuanYuMiaoxianPaymentSource(CardConversionSource Source,string GameplayHash,int HandRedCount,int? UniqueRedCardId,int HandBlackCount,int? UniqueBlackCardId)
{
    private IReadOnlyList<int> _redGrain=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> RedGrainIds {get=>_redGrain;init=>_redGrain=Array.AsReadOnly(value.ToArray());}
}
public sealed record MiaoxianPaymentQualifiedEvent(long BatchId,long? ParentFrameId,long ActionId,CardConversionSource Source,string GameplayHash,int CardId,CardKind Kind,CardLocation From,long Sequence,int ActualTurn,bool Red,bool Black):IGameEvent;
public sealed record MiaoxianNativeCounterspellFinishedEvent(long ActionId,long ParentFrameId,int CardId,long EntrySequence,long FinishSequence):IGameEvent;
public sealed record MiaoxianBlackConsumedEvent(long ActionId,CardConversionSource Source,int ActualTurn,int CardId):IGameEvent;
public sealed record MiaoxianRedDrawIssuedEvent(long FrameId,long ActionId,CardConversionSource Source,int ActualTurn):IGameEvent;
internal sealed class ResolveMiaoxianUniqueRedUseDrawDescriptor:RuanYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw;}
internal sealed class RuanYuMiaoxianCommittedUseContract:IProgramCommittedUseContract
{public string SkillId=>"ol:miaoxian";public void ValidateTrigger(SkillProgramTrigger trigger)=>RuanYuComposition.ValidateRedTrigger("ol:miaoxian",trigger);}
