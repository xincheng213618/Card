using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum HappyDiaoChanStage { Target,MeihunSuit,ViewHand,Cost,CostChildren,Pair,Pindian,PindianChildren,HuoxinSuit,Nonwinner,GiveChildren }
public sealed record HappyDiaoChanMove(int CardId,CardLocation From,CardLocation To,long Sequence);
public sealed record HappyDiaoChanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn,int ActualTurnOwner,HappyDiaoChanStage Stage)
{
    public int Target{get;init;}=-1;
    public Suit Suit{get;init;}
    public CardActionCost? Cost{get;init;}
    public long CostBefore{get;init;}
    public long CostAfter{get;init;}
    public long CostBatch{get;init;}
    public int PairSource{get;init;}=-1;
    public int PairOpponent{get;init;}=-1;
    public long PindianId{get;init;}
    public PindianResult? Result{get;init;}
    public int NonwinnerIndex{get;init;}
    public long Before{get;init;}
    public long After{get;init;}
    public int TransferOrdinal{get;init;}
    public bool IsViewedTake{get;init;}
    private readonly IReadOnlyList<int> _viewed=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Viewed {get=>_viewed;init=>_viewed=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<int> _nonwinners=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Nonwinners {get=>_nonwinners;init=>_nonwinners=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<HappyDiaoChanMove> _moves=Array.AsReadOnly(Array.Empty<HappyDiaoChanMove>());
    public IReadOnlyList<HappyDiaoChanMove> Moves {get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyDiaoChanReceipt? HappyDiaoChan{get;init;}}
public sealed record HappyDiaoChanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn,int ActualTurnOwner):IGameEvent;
public sealed record HappyDiaoChanDeclaredEvent(long FrameId,int TargetSeat,Suit Suit):IGameEvent;
public sealed record HappyDiaoChanCostPaidEvent(long FrameId,long BatchId,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record HappyDiaoChanPindianIssuedEvent(long FrameId,long PindianId,int SourceSeat,int OpponentSeat):IGameEvent;
public sealed record HappyDiaoChanPindianReturnedEvent(long FrameId,long PindianId,int SourceSeat,int OpponentSeat,int SourceRank,int OpponentRank):IGameEvent;
public sealed record HappyDiaoChanTransferIssuedEvent(long FrameId,int Ordinal,int GiverSeat,int RecipientSeat,int Count,bool ViewedTake,long Before,long After):IGameEvent;
public sealed record HappyDiaoChanNonwinnerChosenEvent(long FrameId,int Index,int Seat,Suit Suit,bool Gave):IGameEvent;
public sealed record HappyDiaoChanCharmIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat,Suit Suit,int IssuedTurn):IGameEvent;
public sealed record HappyDiaoChanCharmExpiredEvent(long FrameId,int TargetSeat,Suit Suit,int EndedTurn):IGameEvent;
internal interface IHappyDiaoChanHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp operation);}
internal abstract class HappyDiaoChanDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler{get;}=new HappyDiaoChanHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>op==SkillProgramEffectOp.ResolveHappyDiaoChanMeihun?
        [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterTurnEnded,SkillProgramTriggerWindow.CardUseTargetsFinalized])]:[new RequireActivationEntry()];
}
internal sealed class ResolveHappyDiaoChanMeihunDescriptor():HappyDiaoChanDescriptor(SkillProgramEffectOp.ResolveHappyDiaoChanMeihun);
internal sealed class ResolveHappyDiaoChanHuoxinDescriptor():HappyDiaoChanDescriptor(SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin);
internal sealed class HappyDiaoChanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHappyDiaoChanHost>().Resolve(f,op);
}
internal static class HappyDiaoChanComposition
{
    internal const string Meihun="happy:meihun",Huoxin="happy:huoxin";
    [ProgramCatalogValidation(42220)]internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Op is SkillProgramEffectOp.ResolveHappyDiaoChanMeihun or SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin))return;
        var valid=p.Id==Meihun?p.Activations.Count==0&&p.Triggers is[
            {Id:"owner-ended",Window:SkillProgramTriggerWindow.AfterTurnEnded,Subject:SkillProgramTriggerSubject.Owner,TurnOwnerScope:SkillProgramTurnOwnerScope.Own,Optional:true,Effects:[{Op:SkillProgramEffectOp.ResolveHappyDiaoChanMeihun}]},
            {Id:"first-slash-target",Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,OwnerRelation:SkillProgramCardActionOwnerRelation.Target,Optional:true,Effects:[{Op:SkillProgramEffectOp.ResolveHappyDiaoChanMeihun}]}]&&
                p.Triggers[1].CardKinds.Order().SequenceEqual(new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash}.Order()):
            p.Id==Huoxin&&p.Triggers.Count==0&&p.Activations is[{Id:"charm-contest",MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,TargetKind:SkillProgramTargetKind.AnyLiving,UsesPerTurn:null,UsesPerPhase:1,UsesPerGame:null,Effects:[{Op:SkillProgramEffectOp.ResolveHappyDiaoChanHuoxin}]}];
        if(!valid||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.BooleanStates.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||
            p.Triggers.Any(t=>t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)||
            p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path+": Happy Diao Chan requires her actual owner-ending/first Slash opportunity and one paid two-participant native contest.");
    }
}
