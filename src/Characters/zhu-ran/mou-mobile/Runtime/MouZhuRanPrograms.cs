using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouZhuRanStage { Commit, Pay, CostChildren, Effects, Execute, Damage, Drawing, DrawChildren, HeyuanTarget, HeyuanCost }
public enum MouZhuRanEffect { Damage, Draw }
public sealed record MouZhuRanCard(int CardId, CardLocation From, Suit Suit);
public sealed record MouZhuRanVote(int Seat)
{
    private readonly IReadOnlyList<MouZhuRanCard> _cards=[];
    public IReadOnlyList<MouZhuRanCard> Cards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
}
public sealed record MouZhuRanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,
    int Instruction,int ActualTurn,ActualDiscardRecoveryPhaseKey? Phase,long? WindowId,int Opponent)
{
    public MouZhuRanStage Stage {get;init;}
    public bool Accepted {get;init;}
    private readonly IReadOnlyList<int> _participants=[];
    private readonly IReadOnlyList<MouZhuRanVote> _votes=[];
    private readonly IReadOnlyList<MouZhuRanCard> _selected=[];
    private readonly IReadOnlyList<MouZhuRanCard> _paying=[];
    private readonly IReadOnlyList<MouZhuRanEffect> _effects=[];
    public IReadOnlyList<int> Participants {get=>_participants;init=>_participants=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<MouZhuRanVote> Votes {get=>_votes;init=>_votes=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<MouZhuRanCard> Selected {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<MouZhuRanCard> Paying {get=>_paying;init=>_paying=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<MouZhuRanEffect> Effects {get=>_effects;init=>_effects=Array.AsReadOnly(value.ToArray());}
    public int Cursor {get;init;}
    public int EffectIndex {get;init;}
    public int Payer {get;init;}=-1;
    public int Recipient {get;init;}=-1;
    public int X {get;init;}
    public int OwnerCount {get;init;}
    public int OwnerSuits {get;init;}
    public int OpponentCount {get;init;}
    public int OpponentSuits {get;init;}
    public long PreviousFrame {get;init;}
    public MouZhuRanEffect? LastEffect {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public NativeDrawOutcome? Draw {get;init;}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouZhuRanReceipt? MouZhuRan {get;init;}
}
public sealed record MouZhuRanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,
    SkillProgramEffectOp Operation,int Instruction,int ActualTurn,ActualDiscardRecoveryPhaseKey? Phase,long? WindowId,int Opponent):IGameEvent;
public sealed record MouZhuRanCommittedEvent(long FrameId,int Participants):IGameEvent;
public sealed record MouZhuRanCostPaidEvent(long FrameId,int Payer,int CardId,CardLocation From,Suit Suit,long Sequence):IGameEvent;
public sealed record MouZhuRanComparisonEvent(long FrameId,int OwnerCount,int OwnerSuits,int OpponentCount,int OpponentSuits,int X):IGameEvent;
public sealed record MouZhuRanEffectIssuedEvent(long FrameId,int Index,MouZhuRanEffect Effect,int SourceSeat,int RecipientSeat):IGameEvent;
public sealed record MouZhuRanDrawReturnedEvent(long FrameId,int RecipientSeat,int Requested,int Actual,int Replacement,long? AttemptId):IGameEvent;
public sealed record MouZhuRanResolvedEvent(long FrameId,int OwnerSeat,int DiscardCount,MouZhuRanEffect? LastEffect):IGameEvent;
public sealed record MouZhuRanHeyuanAcceptedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat,
    long PreviousFrame,int RequiredCards,MouZhuRanEffect Effect):IGameEvent;
public sealed record MouZhuRanCompletedEvent(long FrameId):IGameEvent;
internal interface IMouZhuRanHost {SkillProgramStepOutcome Start(ProgramSkillFrame frame,SkillProgramEffectOp operation);}
internal abstract class MouZhuRanDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)=>ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler=>new MouZhuRanHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase,
        Activated:ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>Op==SkillProgramEffectOp.ResolveMouZhuRanZhenwei?
        [new ReadSelectedTarget()]:[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouZhuRanZhenweiDescriptor:MouZhuRanDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouZhuRanZhenwei;}
internal sealed class ResolveMouZhuRanHeyuanDescriptor:MouZhuRanDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouZhuRanHeyuan;}
internal sealed class MouZhuRanHandler(SkillProgramEffectOp operation):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouZhuRanHost>().Start(frame,operation);
}
