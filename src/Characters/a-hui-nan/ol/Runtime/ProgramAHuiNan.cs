using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record AHuiNanBasicUseRecordedEvent(int ActualTurn,int TurnOwner,long OriginFrameId,long? ActionId,int ActorSeat,CardKind Kind,int Index):IGameEvent;
public sealed record AHuiNanBasicUseSnapshot(AHuiNanBasicUseRecordedEvent Declaration,CardKind Kind);
public enum AHuiNanJuemanStage { NoEffect,DrawChildren,UseChoice,NativeUse,NativeChildren }
public sealed record AHuiNanJuemanUseOrigin(long ProgramFrameId,long CardUseFrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActorSeat,CardKind CardKind,string TargetSignature);
public sealed record AHuiNanJuemanReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,int TurnOwner,AHuiNanJuemanStage Stage)
{
    public long Before {get;init;}
    public long After {get;init;}
    public int DrawCount {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AHuiNanJuemanUseOrigin? Use {get;init;}
    private IReadOnlyList<AHuiNanBasicUseSnapshot> _history=Array.AsReadOnly(Array.Empty<AHuiNanBasicUseSnapshot>());
    public IReadOnlyList<AHuiNanBasicUseSnapshot> History {get=>_history;init=>_history=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AHuiNanJuemanReceipt? AHuiNanJueman {get;init;}}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AHuiNanJuemanUseOrigin? AHuiNanJueman {get;init;}}
public sealed record AHuiNanJuemanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int BasicCount,int FirstTwoOwnerUses,CardKind? ThirdKind):IGameEvent;
public sealed record AHuiNanJuemanDrawnEvent(long FrameId,int OwnerSeat,int Count,long Before,long After):IGameEvent;
public sealed record AHuiNanJuemanUseIssuedEvent(AHuiNanJuemanUseOrigin Origin):IGameEvent;
public sealed record AHuiNanJuemanUseReturnedEvent(AHuiNanJuemanUseOrigin Origin):IGameEvent;
public sealed record AHuiNanJuemanCompletedEvent(long FrameId,int DrawCount,bool Used,string Outcome):IGameEvent;
internal interface IAHuiNanHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame);}
internal sealed class ResolveJuemanFirstBasicTurnEndDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJuemanFirstBasicTurnEnd;
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
    public override ISkillProgramEffectHandler Handler=>new AHuiNanHandler();
}
internal sealed class AHuiNanHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJuemanFirstBasicTurnEnd;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IAHuiNanHost>().Resolve(f);
}
internal static class AHuiNanComposition
{
    [ProgramCatalogValidation(420)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        static bool Has(SkillProgramEffect e)=>e.Op==SkillProgramEffectOp.ResolveJuemanFirstBasicTurnEnd;
        if(!p.Activations.Any(a=>a.Effects.Any(Has))&&!p.Triggers.Any(t=>t.Effects.Any(Has)))return;
        if(p.Id!="ol:jueman"||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.Contributions.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations||
            p.Triggers is not[var own,var foreign]||own.Id!="actual-end-own"||own.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||foreign.Id!="actual-end-foreign"||foreign.TurnOwnerScope!=SkillProgramTurnOwnerScope.OtherLiving||
            p.Triggers.Any(t=>t.Window!=SkillProgramTriggerWindow.AfterTurnEnded||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional||t.OwnerRelation is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.MarkerCost is not null||t.UsageScope is not null||t.UsageLimit is not null||
                t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.IncludeResponseUses||t.SingleActionInstance||t.IgnoreOwnSkillMovements||
                t.Effects is not[{Op:SkillProgramEffectOp.ResolveJuemanFirstBasicTurnEnd,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]))
            throw new InvalidOperationException(path+": Jueman requires its exact mandatory own/foreign actual-turn-end single-operation bindings.");
    }
}
