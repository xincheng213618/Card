using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum SunYuanStage { Target, ObtainCard, ObtainChildren, Request, SlashIssuing, NativeUse, NativeChildren, DiscardCards, DiscardChildren, DamageChildren, ReclaimChildren }
public sealed record SunYuanEntity(int CardId,CardKind Kind,CardKind EffectiveKind,CardLocation From,bool GeneralWeapon=false);
public sealed record SunYuanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int ActualTurn,int TurnOwner,SunYuanStage Stage)
{
    public bool Paid{get;init;}public int Holder{get;init;}public int Required{get;init;}public int FrozenLostHp{get;init;}public long DamageFrameId{get;init;}public int DamageAmount{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? DamageSource{get;init;}
    public int Step{get;init;}public long Before{get;init;}public long After{get;init;}public int ActualCount{get;init;}public bool NativeReturned{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SayingSelection? Selection{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SayingNativeOrigin? Native{get;init;}
    private IReadOnlyList<int> _selected=[],_grain=[];private IReadOnlyList<SunYuanEntity> _materials=[];
    public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Grain{get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<SunYuanEntity> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SunYuanReceipt? SunYuan{get;init;}}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SayingNativeOrigin? SayingSlash{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public DongxinUseReceipt? DongxinUse{get;init;}
}
public sealed record SunYuanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int ActualTurn,int TurnOwner,int Holder,long DamageFrameId,int? DamageSource,int DamageAmount,int FrozenLostHp,int Required):IGameEvent;
public sealed record SunYuanTargetEvent(long FrameId,int Holder):IGameEvent;
public sealed record SunYuanSelectedEvent(long FrameId,int Index,int CardId):IGameEvent;
public sealed record SunYuanMaterialEvent(long FrameId,int Index,int CardId,CardKind Kind,CardKind EffectiveKind,CardLocation From,bool GeneralWeapon):IGameEvent;
public sealed record SunYuanGrainEvent(long FrameId,int Index,int CardId):IGameEvent;
public sealed record SunYuanPaidEvent(long FrameId,int ActualTurn,int TurnOwner,int OwnerSeat,string Direction,int Count):IGameEvent;
public sealed record SunYuanEntityMovedEvent(long FrameId,int Step,SunYuanStage Stage,long BatchId,int CardId,CardKind Kind,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record SunYuanMovementIssuedEvent(long FrameId,int Step,SunYuanStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record SunYuanCompletedEvent(long FrameId,bool Applied):IGameEvent;
internal interface ISunYuanHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal abstract class SunYuanDescriptor(SkillProgramEffectOp op,SkillProgramTriggerWindow window):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(window)];
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler{get;}=new SunYuanHandler(op);
}
internal sealed class SunYuanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host)=>host.GetCapability<ISunYuanHost>().Resolve(frame,op);}
internal static class SunYuanComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply or SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim;
    [ProgramCatalogValidation(670)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op)))&&!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Id is not("ol:saying"or"ol:dongxin")||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0||p.DamageModifiers.Count!=0||p.CardPolicies.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations||p.Triggers.Count!=(p.Id=="ol:saying"?1:2))throw new InvalidOperationException(path+": Sun Yuan requires her two complete independent native contracts.");
        foreach(var t in p.Triggers)
        {
            var saying=p.Id=="ol:saying";
            if(!t.Optional||t.Priority!=0||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.OwnerRelation is not null||t.UsageLimit is not null||t.UsageScope is not null||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Suits.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.MovementReasons.Count!=0||t.DamageCardKinds.Count!=0||t.IncludeResponseUses||t.SingleActionInstance||t.MarkerCost is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.ChoiceGroup is not null||t.OrderGroup is not null||t.RequireDamageSource is not null||t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]||e.Op!=(saying?SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply:SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim)||
                (saying?t.Id!="play-start-obtain"||t.Window!=SkillProgramTriggerWindow.PlayPhaseStarting||t.Subject!=SkillProgramTriggerSubject.Owner||t.DamageOccurrence is not null:t.Id is not("dealt"or"received")||t.Window!=SkillProgramTriggerWindow.AfterDamageApplied||t.Subject!=SkillProgramTriggerSubject.Any||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage))throw new InvalidOperationException(path+": Sun Yuan requires exact optional timings and per-damage independent directions.");
        }
        if(p.Id=="ol:dongxin"&&p.Triggers.Select(t=>t.Id).Distinct().Count()!=2)throw new InvalidOperationException(path+": Dongxin must retain both once-per-actual-turn directions.");
    }
}
