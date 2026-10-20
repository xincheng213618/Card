using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum LiuBaStage { GiftTarget, GiftCard, GiftChildren, ReturnChildren, RecastTarget, RecastCard, RecastChildren, DrawChildren, Coins, Bottom, ExchangeChildren }
public sealed record LiuBaEntity(int CardId,CardKind Kind,CardLocation From);
public sealed record LiuBaReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner,int PhaseInstance,LiuBaStage Stage)
{
    public int Recipient {get;init;} public bool Paid {get;init;} public long Before {get;init;} public long After {get;init;} public int ActualCount {get;init;} public int Step {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? DebtId {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public long? BatchId {get;init;}
    private IReadOnlyList<LiuBaEntity> _entities=Array.AsReadOnly(Array.Empty<LiuBaEntity>());
    public IReadOnlyList<LiuBaEntity> Entities {get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _bottom=Array.AsReadOnly(Array.Empty<int>()),_coins=Array.AsReadOnly(Array.Empty<int>()),_selected=Array.AsReadOnly(Array.Empty<int>()),_grain=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ViewedBottom {get=>_bottom;init=>_bottom=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Coins {get=>_coins;init=>_coins=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> SelectedBottom {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Grain {get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiuBaReceipt? LiuBa {get;init;}}
public sealed record LiuBaStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner,int PhaseInstance):IGameEvent;
public sealed record LiuBaEntityMovedEvent(long FrameId,int Step,long BatchId,int CardId,CardKind Kind,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record LiuBaMovementIssuedEvent(long FrameId,int Step,LiuBaStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record LiuBaCoinMintedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int RecipientSeat,int CardId,long GainSequence):IGameEvent;
public sealed record LiuBaEndingViewedEvent(long FrameId,long ParentId,int HolderSeat,int ActualTurn,int Count):IGameEvent;
public sealed record LiuBaCompletedEvent(long FrameId,SkillProgramEffectOp Op,int Steps):IGameEvent;
internal interface ILiuBaHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal interface ILiuBaSelectionQuery:IProgramSelectionQuery {int MaximumHp(int ownerSeat);}
[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit,27390,SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)]
internal sealed class LiuBaRecastPhaseLimit:IProgramSelectionPhaseLimitProvider
{public int? GetLimit(ProgramSelectionContext context,IProgramSelectionQueries queries)=>queries.Get<ILiuBaSelectionQuery>().MaximumHp(context.OwnerSeat);}
internal abstract class LiuBaDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler=>new LiuBaHandler(Op);
}
internal sealed class LiuBaHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ILiuBaHost>().Resolve(f,op);}
internal static class LiuBaComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.IssueTongduHandGift or SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd or SkillProgramEffectOp.RecastZhubiAndMarkActualDraw or SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding;
    [ProgramCatalogValidation(570)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op)))&&!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        bool Exact(IReadOnlyList<SkillProgramEffect> e,SkillProgramEffectOp op)=>e is[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}only]&&only.Op==op;
        if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.Contributions.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations)throw new InvalidOperationException(path+": Liu Ba requires exact independent physical gift, recast and exchange protocols.");
        if(p.Id=="ol:tongdu"&&p.Activations.Count==0&&p.Triggers.Count==2&&p.Triggers.Any(t=>t.Id=="preparation-gift"&&t.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Optional&&Exact(t.Effects,SkillProgramEffectOp.IssueTongduHandGift))&&p.Triggers.Any(t=>t.Id=="issued-play-end"&&t.Window==SkillProgramTriggerWindow.PlayEnding&&t.Subject==SkillProgramTriggerSubject.Owner&&!t.Optional&&Exact(t.Effects,SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd))&&p.Triggers.All(t=>t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.UsageLimit is null&&t.OwnerRelation is null&&t.SourceSkillId is null&&t.SourceZones.Count==0&&t.DestinationZones.Count==0))return;
        if(p.Id=="ol:zhubi"&&p.Activations is[var a]&&a.Id=="recast-coin"&&a.MinCards==0&&a.MaxCards==0&&a.MinTargets==0&&a.MaxTargets==0&&a.TargetKind==SkillProgramTargetKind.AnyLiving&&a.UsesPerTurn is null&&a.UsesPerPhase is null&&Exact(a.Effects,SkillProgramEffectOp.RecastZhubiAndMarkActualDraw)&&p.Triggers is[var end]&&end.Id=="issued-holder-ending"&&end.Window==SkillProgramTriggerWindow.TurnEnding&&end.Subject==SkillProgramTriggerSubject.Owner&&!end.Optional&&end.UsageLimit is null&&end.OwnerRelation is null&&end.SourceSkillId is null&&end.Condition.Kind==SkillProgramTriggerConditionKind.Always&&Exact(end.Effects,SkillProgramEffectOp.ExchangeZhubiCoinsAtEnding))return;
        throw new InvalidOperationException(path+": Liu Ba operations require complete exact original bindings.");
    }
}
