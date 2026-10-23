using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyHuaTuoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp operation); }
internal abstract class HappyHuaTuoDescriptor(SkillProgramEffectOp operation):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>operation;
    public override ISkillProgramEffectHandler Handler{get;}=new HappyHuaTuoHandler(operation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction=>operation==SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Recover,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>operation switch
    {
        SkillProgramEffectOp.ResolveHappyHuaTuoMedicine=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering),new SelectSingleTarget()],
        SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),new SelectTargetSet(0,3),new CreateChoiceResult("needle-mode",["recover","lose","zero"])],
        SkillProgramEffectOp.InitializeHappyHuaTuoMedicine=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        _=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)]
    };
}
internal sealed class InitializeHappyHuaTuoMedicineDescriptor():HappyHuaTuoDescriptor(SkillProgramEffectOp.InitializeHappyHuaTuoMedicine);
internal sealed class GainHappyHuaTuoMedicineDescriptor():HappyHuaTuoDescriptor(SkillProgramEffectOp.GainHappyHuaTuoMedicine);
internal sealed class ResolveHappyHuaTuoMedicineDescriptor():HappyHuaTuoDescriptor(SkillProgramEffectOp.ResolveHappyHuaTuoMedicine);
internal sealed class ResolveHappyHuaTuoShenzhenDescriptor():HappyHuaTuoDescriptor(SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen);
internal sealed class HappyHuaTuoHandler(SkillProgramEffectOp operation):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHappyHuaTuoHost>().Resolve(f,operation);
}
public enum HappyHuaNeedleStage { AmountChoice,TargetChoice,Committed }
public sealed record HappyHuaNeedleReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,HappyHuaNeedleStage Stage,int Amount,int Before,int After);
public sealed record HappyHuaRescueReceipt(CardConversionSource Source,string GameplayHash,long EntryId,long DyingId,int VictimSeat,int HpBefore,int MedicineBefore,int MedicineAfter);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyHuaNeedleReceipt? HappyHuaNeedle{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyHuaRescueReceipt? HappyHuaRescue{get;init;}
}
public sealed record HappyHuaMedicinePaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Before,int After,string Purpose):IGameEvent;
public sealed record HappyHuaRescueIssuedEvent(long FrameId,long EntryId,long DyingId,int VictimSeat,int HpBefore):IGameEvent;
public sealed record HappyHuaNeedleTargetEvent(long FrameId,int Ordinal,int Seat,string Mode):IGameEvent;
internal sealed class HappyTaoxianCommittedContract:IProgramCommittedUseContract
{
    public string SkillId=>"happy:taoxian";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if(t is not{Id:"other-peach-use",Window:SkillProgramTriggerWindow.CardUseCommitted,OwnerRelation:SkillProgramCardActionOwnerRelation.Observer,Subject:null,IncludeResponseUses:true,Optional:false,SingleActionInstance:false,SourceSkillId:null,SourceViewAsId:null,UsageScope:null,UsageLimit:null,MarkerCost:null,Condition.Kind:SkillProgramTriggerConditionKind.Always,
            Effects:[{Op:SkillProgramEffectOp.Draw,Target:SkillProgramEffectTarget.Owner,Amount:1,NumberExpression:null,SourceBind:null,ResultBind:null,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always}]}||!t.CardKinds.SequenceEqual([CardKind.Peach]))
            throw new InvalidOperationException("Happy Taoxian requires one mandatory native owner draw for an actual other Peach use, including rescue uses.");
    }
}
internal static class HappyHuaTuoComposition
{
    [ProgramCatalogValidation(41780)]internal static void Validate(string path,SkillProgram p)
    {
        static bool IsOwnOp(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.InitializeHappyHuaTuoMedicine or SkillProgramEffectOp.GainHappyHuaTuoMedicine or SkillProgramEffectOp.ResolveHappyHuaTuoMedicine or SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen;
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(IsOwnOp))return;
        if(p.Id is not("happy:jishi" or "happy:shenzhen"))throw new InvalidOperationException(path+": Happy Hua Tuo operations belong only to their exact original skill definitions.");
        var valid=p.Id=="happy:jishi"?
            p.Triggers is[{Id:"initial-medicine",Window:SkillProgramTriggerWindow.GameStarting,Optional:false,Effects:[{Op:SkillProgramEffectOp.InitializeHappyHuaTuoMedicine}]},
                {Id:"red-hand-loss",Window:SkillProgramTriggerWindow.CardsMoved,Optional:false,Effects:[{Op:SkillProgramEffectOp.GainHappyHuaTuoMedicine}]} gain,
                {Id:"medicine-rescue",Window:SkillProgramTriggerWindow.DyingEntering,Subject:SkillProgramTriggerSubject.Any,Optional:true,Effects:[{Op:SkillProgramEffectOp.ResolveHappyHuaTuoMedicine},{Op:SkillProgramEffectOp.RecoverTo,Target:SkillProgramEffectTarget.SelectedTarget,NumberExpression:SkillProgramNumberExpression.IntegerConstant,MinimumValue:1,ClampToMaxHp:true}]}]&&
            gain.SourceZones.SequenceEqual([CardZoneKind.Hand])&&gain.Suits.Count==0&&gain.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&
            p.Modifiers is[{Query:SkillRuleQuery.HandLimit,Operation:SkillRuleOperation.Add,Value:3}]:
            p.Triggers is[{Id:"medicine-needles",Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,Optional:true,Effects:[{Op:SkillProgramEffectOp.ResolveHappyHuaTuoShenzhen},
                {Op:SkillProgramEffectOp.Recover,Target:SkillProgramEffectTarget.SelectedTargets,Amount:1,Condition:{Kind:SkillProgramConditionKind.ChoiceIs,SourceBind:"needle-mode",OptionId:"recover"}},
                {Op:SkillProgramEffectOp.LoseHpParticipants,Target:SkillProgramEffectTarget.Owner,Amount:1,Condition:{Kind:SkillProgramConditionKind.ChoiceIs,SourceBind:"needle-mode",OptionId:"lose"}}]}]&&p.Modifiers.Count==0;
        if(!valid||p.Activations.Count!=0||p.CardPolicies.Count!=0||p.ViewAs.Count!=0||p.BooleanStates.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||
            p.Triggers.Any(t=>t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.MarkerCost is not null||
                t.Effects.Where(IsOwnOp).Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path+": Happy Hua Tuo requires exact original medicine payment, dying target recovery and equally sized native needle outcomes.");
    }
}
