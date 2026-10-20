using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum WuTuGuRanshangStage{Issuing,HpChildren}
public sealed record WuTuGuRanshangReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,int Amount,WuTuGuRanshangStage Stage);
public sealed record WuTuGuRanshangIssuedEvent(long ProgramFrameId,CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,int Amount):IGameEvent;
public sealed record WuTuGuRanshangPaidEvent(long ProgramFrameId,int Amount,int RemainingHp):IGameEvent;
public sealed record WuTuGuRanshangCompletedEvent(long ProgramFrameId):IGameEvent;
internal sealed class AccumulateRanshangFirePointDescriptor():WuTuGuDescriptor(SkillProgramEffectOp.AccumulateRanshangFirePoint){}
internal sealed class ResolveRanshangEndingHpLossDescriptor():WuTuGuDescriptor(SkillProgramEffectOp.ResolveRanshangEndingHpLoss){}
