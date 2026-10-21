using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouSsxStage { InitialHolder, HolderChoice, GiftCards, GiftChildren, TransferHolder, DepositChoice, DepositChildren, BenefitChoice, RecoverChildren, Drawing, DrawChildren, FieldChoice, DiscardChildren, FailureRecoverChildren, FailureObtainChildren, FailureFaction, FailureMaximumHpChildren }
public sealed record MouSsxEntity(int CardId, CardKind Kind, CardLocation From);
public sealed record MouSsxStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentFrameId, int HolderSeat) : IGameEvent;
public sealed record MouSsxZhuChangedEvent(long FrameId, int OwnerSeat, int PreviousSeat, int CurrentSeat, bool Initial) : IGameEvent;
public sealed record MouSsxMissionFailedEvent(long FrameId, int OwnerSeat, int LastHolderSeat) : IGameEvent;
public sealed record MouSsxGiftCommittedEvent(long FrameId, int HolderSeat, int Count) : IGameEvent;
public sealed record MouSsxMovedEvent(long FrameId, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouSsxBenefitIssuedEvent(long FrameId, int RecipientSeat, bool Recover) : IGameEvent;
public sealed record MouSsxDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual) : IGameEvent;
public sealed record MouSsxFailureStepEvent(long FrameId, int Step) : IGameEvent;
public sealed record MouSsxCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record MouSsxReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentFrameId)
{
    public MouSsxStage Stage { get; init; }
    public int HolderSeat { get; init; }=-1;
    public int Required { get; init; }
    public int RecipientSeat { get; init; }=-1;
    public bool Paid { get; init; }
    public bool DrawReturned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private IReadOnlyList<int> _selected=[];
    private IReadOnlyList<MouSsxEntity> _entities=[];
    public IReadOnlyList<int> Selected { get=>_selected; init=>_selected=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouSsxEntity> Entities { get=>_entities; init=>_entities=Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouSsxReceipt? MouSsx { get; init; } }
internal interface IMouSunShangxiangFacts:IProgramSelectionQuery
{
    bool GameOver{get;}
    long MovementSequence{get;}
    bool Alive(int seat);
    bool Human(int seat);
    string Name(int seat);
    string? Faction(int seat);
    SkillProgram Program(string id);
    IReadOnlyList<int> Living{get;}
    IReadOnlyList<Card> Hand(int seat);
    CardLocation Location(int id);
    IReadOnlyList<MouSsxEntity> Field(int owner,bool equipmentOnly);
    IReadOnlyList<MouSsxEntity> Zhuang(int owner);
    IEnumerable<T> History<T>()where T:IGameEvent;
    bool ParentMatches(ProgramSkillFrame f);
    bool MovementRecoveryChild(ProgramSkillFrame f,ResolutionFrame child);
    double HelpValue(int actor,int target);
    double KeepValue(int actor,int id);
}
internal interface IMouSunShangxiangNative:IProgramRuntimeOperations
{
    void Mark(ProgramSkillFrame f,int previous,int current,bool initial);
    void Give(ProgramSkillFrame f,int payer,IReadOnlyList<int> ids);
    void Deposit(ProgramSkillFrame f,MouSsxEntity entity);
    void ObtainZhuang(ProgramSkillFrame f,IReadOnlyList<MouSsxEntity> entities);
    void Discard(ProgramSkillFrame f,MouSsxEntity entity);
    void Armor(ProgramSkillFrame f,int seat);
    void Recover(ProgramSkillFrame f,int seat);
    void Faction(ProgramSkillFrame f);
    void MaximumHp(ProgramSkillFrame f);
    bool Drain(ProgramSkillFrame f);
    void Finish(ProgramSkillFrame f);
    void Publish();
}
internal interface IMouSunShangxiangHost
{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal sealed class MouSunShangxiangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouSunShangxiangHost>().Resolve(f,op);
}
internal abstract class MouSunShangxiangDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MouSunShangxiangHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(4));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c)=>c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouSunShangxiangJieyinDescriptor:MouSunShangxiangDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouSunShangxiangJieyin;}
internal sealed class ResolveMouSunShangxiangLiangzhuDescriptor:MouSunShangxiangDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu;}
internal sealed class ResolveMouSunShangxiangXiaojiDescriptor:MouSunShangxiangDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji;}
