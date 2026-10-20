using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoYuMinimumTransitionEvent(long OpportunityId,int Index,CardConversionSource Source,string GameplayHash,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]long? BatchId,int ActualTurn,int TurnOwner,int HandBefore,int HandAfter,int MinimumBefore,int MinimumAfter,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]long? DeathFrameId=null):IGameEvent;
public sealed record CaoYuMinimumSeatEvent(long OpportunityId,int Seat,int Before,int After,bool Alive,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]bool? AliveBefore=null):IGameEvent;
public sealed record CaoYuMinimumDeathRecordedEvent(long OpportunityId,long DeathFrameId,long ParentFrameId,int VictimSeat,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? KillerSeat,DeathReturnKind ReturnKind):IGameEvent;
public sealed record CaoYuMinimumArmedEvent(long FrameId,long OpportunityId,CardConversionSource Source,string GameplayHash):IGameEvent;
public sealed record CaoYuOpportunityConsumedEvent(long FrameId,long OpportunityId,CardConversionSource ResolverSource,bool Adjust):IGameEvent;
public sealed record CaoYuXiangxuComparisonEvent(long FrameId,long OpportunityId,int ReferenceSeat,int OwnHand,int ReferenceHand,string Branch,int Requested):IGameEvent;
internal sealed class ResolveXiangxuMinimumTransitionDescriptor:CaoYuDescriptor {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXiangxuMinimumTransition;}
internal sealed class ResolveXiangxuIssuedTurnEndAdjustmentDescriptor:CaoYuDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment;}
