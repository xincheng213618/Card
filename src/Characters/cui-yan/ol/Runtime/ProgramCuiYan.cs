using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum CuiYanStage { DrawChildren, DamageChoice, DamageChildren }
public sealed record CuiYanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int ActualTurn,int TurnOwner,CuiYanStage Stage)
{
    public long UseId {get;init;} public long ActionId {get;init;} public Suit Suit {get;init;}
    public bool DrawQualified {get;init;} public long Before {get;init;} public long After {get;init;} public int ActualDraw {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public int? DamageTarget {get;init;}
    private IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CuiYanReceipt? CuiYan {get;init;}}
public sealed record CuiYanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long WindowId,int ActualTurn,int TurnOwner,long UseId,long ActionId,Suit Suit,bool DrawQualified):IGameEvent;
public sealed record CuiYanFrozenTargetEvent(long FrameId,int Index,int TargetSeat):IGameEvent;
public sealed record CuiYanDrawIssuedEvent(long FrameId,long Before,long After,int ActualCount):IGameEvent;
public sealed record CuiYanDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat):IGameEvent;
public sealed record CuiYanCompletedEvent(long FrameId):IGameEvent;
// Historical facts are collected whenever this capability exists in the catalogue,
// including turns during which the actual character has not yet acquired a skill.
public sealed record CuiYanActualUseActorEvent(long UseId,long ActionId,int ActualTurn,int TurnOwner,int ActorSeat,CardKind Kind):IGameEvent;
public sealed record CuiYanEverTargetedEvent(long UseId,int ActualTurn,int TurnOwner,int TargetSeat):IGameEvent;
public sealed record CuiYanRespondedEvent(int ActualTurn,int TurnOwner,int ResponderSeat):IGameEvent;
public sealed record CuiYanEndQualifiedEvent(int ActualTurn,int TurnOwner,int OwnerSeat,bool UsedOrPlayed,bool WasTargeted):IGameEvent;
internal interface ICuiYanHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class CuiYanDescriptor(SkillProgramEffectOp op,SkillProgramTriggerWindow window):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(window)];
    public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.ResolveZhijieActualTurnEnd?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler {get;}=new CuiYanHandler(op);
}
internal sealed class CuiYanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ICuiYanHost>().Resolve(f,op);}
internal static class CuiYanComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveShutongFinalizedUse or SkillProgramEffectOp.ResolveZhijieActualTurnEnd;
    [ProgramCatalogValidation(650)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op)))&&!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0||p.DamageModifiers.Count!=0||p.LordSkillProjection)
            throw new InvalidOperationException(path+": Cui Yan requires independent mandatory finalized-use and actual-turn-end triggers.");
        var shutong=p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveShutongFinalizedUse));
        if(p.Triggers.Count!=(shutong?1:2))throw new InvalidOperationException(path+": Unexpected Cui Yan trigger collection.");
        foreach(var t in p.Triggers)
        {
            if(t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always} e]||!Handles(e.Op)||t.Optional||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.MarkerCost is not null||t.ChoiceGroup is not null||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.Suits.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.DamageCardKinds.Count!=0||t.IncludeResponseUses||t.SingleActionInstance||t.OnlyDesignatedCardTargets||t.NoDyingAtActivation||t.DeferredTurnEndOnly||t.RequireNoCardConversion is not null||t.RequireDamageSource is not null||t.Priority!=0||
               (shutong?e.Op!=SkillProgramEffectOp.ResolveShutongFinalizedUse||t.Id!="designated-use"||t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Subject is not null:
                e.Op!=SkillProgramEffectOp.ResolveZhijieActualTurnEnd||t.Window!=SkillProgramTriggerWindow.AfterTurnEnded||t.OwnerRelation is not null||t.Subject!=SkillProgramTriggerSubject.Owner||t.Id!=(t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own?"end-own":"end-foreign")||t.TurnOwnerScope is not(SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving)))
                throw new InvalidOperationException(path+": Cui Yan needs the exact complete native timing, participants and unfiltered mandatory instruction.");
        }
        if(!shutong&&(p.Triggers.Count(t=>t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own)!=1||p.Triggers.Count(t=>t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving)!=1))throw new InvalidOperationException(path+": Zhijie must cover both own and other actual turn ends.");
    }
}
