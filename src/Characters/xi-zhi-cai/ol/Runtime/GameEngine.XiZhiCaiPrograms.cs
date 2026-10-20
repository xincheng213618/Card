using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum XianfuEchoKind { Damage, Recovery }
public sealed record XianfuRelationGrant(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat,long GameStartingWindowId);
public sealed record XianfuEchoQualification(XianfuRelationGrant Grant,XianfuEchoKind Kind,long NativeChangeId,long NativeParentFrameId,int OriginalTargetSeat,int ActualAmount,int? OriginalSourceSeat,int? HpBefore,int HpAfter);
public sealed record XianfuRelationArmedEvent(XianfuRelationGrant Grant):IGameEvent;
public sealed record XianfuEchoQualifiedEvent(XianfuEchoQualification Receipt):IGameEvent;
public sealed record XianfuEchoIssuedEvent(long FrameId,XianfuEchoQualification Receipt,int InstructionIndex,int NativeAmount):IGameEvent;
public sealed record XianfuEchoCompletedEvent(long FrameId,long NativeChangeId,XianfuEchoKind Kind):IGameEvent;
public sealed record XianfuEchoPending(XianfuEchoQualification Receipt,int InstructionIndex,int HpBefore,bool NativeIssued,int NativeAmount);
public sealed record ChoucePointReceipt(CardConversionSource Source,string GameplayHash,long DamageWindowId,long DamageFrameId,long OriginalAttackId,int PointIndex,int OriginalAmount,int? DamageSourceSeat,int DamageTargetSeat,int StartInstructionIndex,long JudgmentFrameId,string Reason,string ResultBind);
public enum ChouceStage { Judgment, ChooseTarget, ChooseCard, NativeChildren }
public sealed record ChoucePending(ChoucePointReceipt Receipt,ChouceStage Stage,Suit? FrozenSuit=null,int? TargetSeat=null,int? CardId=null,CardLocation? CardLocation=null,long Before=0,long After=0,int Count=0);
public sealed record ChouceJudgmentIssuedEvent(long FrameId,ChoucePointReceipt Receipt):IGameEvent;
public sealed record ChouceJudgmentFrozenEvent(long FrameId,long JudgmentFrameId,Suit EffectiveSuit,int? CardId):IGameEvent;
public sealed record ChouceOutcomeIssuedEvent(long FrameId,long JudgmentFrameId,int TargetSeat,bool Red,long Before,long After,int Count,int? CardId):IGameEvent;
public sealed record ChouceCompletedEvent(long FrameId,long JudgmentFrameId,int PointIndex):IGameEvent;
public sealed partial class CharacterState
{
 private IReadOnlyList<XianfuRelationGrant> _xianfuRelations=Array.AsReadOnly(Array.Empty<XianfuRelationGrant>());
 internal IReadOnlyList<XianfuRelationGrant> XianfuRelations{get=>_xianfuRelations;set=>_xianfuRelations=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillWindowContext
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XianfuEchoQualification? XianfuEcho{get;init;}}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XianfuEchoPending? XianfuPending{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ChoucePending? Chouce{get;init;}
}
public sealed partial class GameEngine
{

 private CardConversionSource XiZhiCaiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 [ProgramCapability]
 private sealed class XiZhiCaiProgramCapability(GameEngine engine) : IXiZhiCaiProgramHost
 {
  public SkillProgramStepOutcome ArmXianfuRelation(ProgramSkillFrame f,int t)=>engine.ArmXianfuRelation(f,t);
  public SkillProgramStepOutcome SettleXianfuNativeEcho(ProgramSkillFrame f)=>engine.SettleXianfuNativeEcho(f);
  public SkillProgramStepOutcome StartChouceDamagePointJudgment(ProgramSkillFrame f,SkillProgramEffect e)=>engine.StartChouceDamagePointJudgment(f,e);
  public SkillProgramStepOutcome ResolveChouceFrozenJudgment(ProgramSkillFrame f,SkillProgramEffect e)=>engine.ResolveChouceFrozenJudgment(f,e);
 }
}
