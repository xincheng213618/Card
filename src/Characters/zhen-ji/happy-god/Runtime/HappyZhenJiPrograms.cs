using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IHappyZhenJiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal interface IHappyZhenJiQuery:IProgramSelectionQuery { bool Allows(int owner,int target); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget,41240,SkillProgramEffectOp.ResolveHappyZhenJiShenfu)]
internal sealed class HappyShenfuTargetFilter:IProgramSelectionTargetFilter
{public bool Allows(ProgramSelectionContext c,int target,IProgramSelectionQueries q)=>q.Get<IHappyZhenJiQuery>().Allows(c.OwnerSeat,target);}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits,41240,SkillProgramEffectOp.ResolveHappyZhenJiShenfu)]
internal sealed class HappyShenfuTargetValidator:IProgramSelectionValidator
{public CommandError? Validate(ProgramSelectionValidationRequest r,IProgramSelectionQueries q)=>r.Targets is[var target]&&q.Get<IHappyZhenJiQuery>().Allows(r.Context.OwnerSeat,target)?null:new(CommandErrorCode.InvalidTarget,"神赋不能再次指定本实际回合已指定的角色。");}
internal abstract class HappyZhenJiDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new HappyZhenJiHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance|ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase,Activated:ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>op==SkillProgramEffectOp.ResolveHappyZhenJiShenfu
        ?[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.AnyLiving)]
        :[new RequireTriggerWindow(op==SkillProgramEffectOp.StoreHappyZhenJiQixian?SkillProgramTriggerWindow.PlayEnding:op==SkillProgramEffectOp.CollectHappyZhenJiFeifu?SkillProgramTriggerWindow.CardResponseCompleted:SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class HappyShenfuDescriptor():HappyZhenJiDescriptor(SkillProgramEffectOp.ResolveHappyZhenJiShenfu);
internal sealed class HappyQixianStoreDescriptor():HappyZhenJiDescriptor(SkillProgramEffectOp.StoreHappyZhenJiQixian);
internal sealed class HappyFeifuDescriptor():HappyZhenJiDescriptor(SkillProgramEffectOp.CollectHappyZhenJiFeifu);
internal sealed class HappyQixianReturnDescriptor():HappyZhenJiDescriptor(SkillProgramEffectOp.ReturnHappyZhenJiQixian);
internal sealed class HappyZhenJiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IHappyZhenJiHost>().Begin(f,op);}
internal static class HappyZhenJiComposition
{
    internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveHappyZhenJiShenfu or SkillProgramEffectOp.StoreHappyZhenJiQixian or SkillProgramEffectOp.CollectHappyZhenJiFeifu or SkillProgramEffectOp.ReturnHappyZhenJiQixian;
    [ProgramCatalogValidation(41240)] internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(e=>Own(e.Op)))return;
        static bool E(IReadOnlyList<SkillProgramEffect> es,SkillProgramEffectOp op)=>es is[{Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always}e]&&e.Op==op;
        var valid=p.Id switch
        {
            "happy:shenfu"=>p.Triggers.Count==0&&p.Activations is[{Id:"three-options",TargetKind:SkillProgramTargetKind.AnyLiving,MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,UsesPerTurn:null,UsesPerPhase:1}a]&&E(a.Effects,SkillProgramEffectOp.ResolveHappyZhenJiShenfu),
            "happy:qixian"=>p.Activations.Count==0&&p.Modifiers is[{Query:SkillRuleQuery.HandLimit,Operation:SkillRuleOperation.Set,Value:7,Priority:405}]&&p.Triggers.Count==2&&p.Triggers.All(t=>t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.UsageScope is null&&t.MarkerCost is null&&t.Subject==SkillProgramTriggerSubject.Owner&&(t.Id=="play-ending-store"&&t.Window==SkillProgramTriggerWindow.PlayEnding&&t.Optional&&E(t.Effects,SkillProgramEffectOp.StoreHappyZhenJiQixian)||t.Id=="seven-at-turn-end"&&t.Window==SkillProgramTriggerWindow.AfterTurnEnded&&!t.Optional&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.AnyLiving&&E(t.Effects,SkillProgramEffectOp.ReturnHappyZhenJiQixian))),
            "happy:feifu"=>p.Activations.Count==0&&p.ViewAs is[{Id:"black-as-dodge",OutputKind:CardKind.Dodge,ForPlay:false,ForResponse:true}v]&&v.InputSuits.SequenceEqual([Suit.Spade,Suit.Club])&&v.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment])&&p.Triggers is[{Id:"finished-feifu",Window:SkillProgramTriggerWindow.CardResponseCompleted,OwnerRelation:SkillProgramCardActionOwnerRelation.ConversionSource,SourceSkillId:"happy:feifu",SourceViewAsId:"black-as-dodge",Optional:false,IsSkillInvocation:false,SingleActionInstance:false}t]&&E(t.Effects,SkillProgramEffectOp.CollectHappyZhenJiFeifu),_=>false
        };
        if(!valid||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.Id!="happy:qixian"&&p.Modifiers.Count!=0||p.Id!="happy:feifu"&&p.ViewAs.Count!=0)throw new InvalidOperationException(path+": Happy Zhen Ji requires her formal actual-phase Shenfu debit, fixed seven and source-specific completed Feifu response.");
    }
}
public enum HappyZjStage{Branch,Discard,Damaging,TargetDraw,EffectChildren,RewardDraw,RewardChildren,SelectStore,StoreChildren,FeifuChildren,ReturnChildren,Finished}
public sealed record HappyZjEntity(int Id,CardKind Kind,CardLocation From);
public sealed record HappyZjPayment(int Ordinal,long Before,long After)
{
    private readonly IReadOnlyList<CardMovementRecord> _moves=[];
    public IReadOnlyList<CardMovementRecord> Moves{get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
}
public sealed record HappyZjReceipt(CardConversionSource Source,string Hash,SkillProgramEffectOp Op,int Turn,int TurnOwner,int Phase,long Parent)
{
    public HappyZjStage Stage{get;init;}
    public int Target{get;init;}
    public int Ordinal{get;init;}
    public int Reward{get;init;}
    public int Limit{get;init;}
    public string? Branch{get;init;}
    public bool Refunded{get;init;}
    public NativeDrawOutcome? Draw{get;init;}
    public long? Action{get;init;}
    public PublicPersistentPileSource? Pile{get;init;}
    private readonly IReadOnlyList<int> _selected=[];
    public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<HappyZjEntity> _entities=[];
    public IReadOnlyList<HappyZjEntity> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<HappyZjPayment> _payments=[];
    public IReadOnlyList<HappyZjPayment> Payments{get=>_payments;init=>_payments=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyZjReceipt? HappyZhenJi{get;init;}}
public sealed record HappyZjStartedEvent(long FrameId,CardConversionSource Source,string Hash,SkillProgramEffectOp Op,int Turn,int TurnOwner,int Phase,long Parent,int Target,int Ordinal):IGameEvent;
public sealed record HappyZjBranchEvent(long FrameId,string Branch):IGameEvent;
public sealed record HappyZjRefundedEvent(long FrameId,int Turn,int Phase,int Target,int Ordinal,int Reward,int Before,int After):IGameEvent;
public sealed record HappyZjPaidEvent(long FrameId,int Ordinal,long Before,long After):IGameEvent;
public sealed record HappyZjFeifuClaimedEvent(long FrameId,long ActionId,CardConversionSource Source,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record HappyZjCompletedEvent(long FrameId):IGameEvent;
