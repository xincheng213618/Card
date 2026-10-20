using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum FeiYiStage { DiscardChoice, DrawChildren, DiscardChildren, DirectionChoice, HezhongDrawChildren }
public sealed partial record CardMovementBatchContext
{
    private IReadOnlyList<FeiYiSingletonTransition>? _feiYiSingletons;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FeiYiSingletonTransition>? FeiYiSingletons { get=>_feiYiSingletons; init=>_feiYiSingletons=value is null?null:Array.AsReadOnly(value.ToArray()); }
}
public sealed record FeiYiReceipt(CardConversionSource Source,string GameplayHash,int Instruction,int Turn,int TurnOwner,
    SkillProgramEffectOp Operation,FeiYiStage Stage,long PhaseInstance=0,bool Odd=false,long? WindowId=null,long? BatchId=null,
    int? RevealedCard=null,int? RevealedRank=null,bool? Greater=null,int Minimum=0,int HandCount=0,bool Paid=false,long Before=0,long After=0)
{
    private IReadOnlyList<int> _selected=[];
    private IReadOnlyList<int> _candidates=[];
    public IReadOnlyList<int> Selected { get=>_selected; init=>_selected=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Candidates { get=>_candidates; init=>_candidates=Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public FeiYiReceipt? FeiYi { get;init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public FeiYiEffectRounds? FeiYiRounds { get;init; } }
public sealed record FeiYiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,
    int Turn,int TurnOwner,long PhaseInstance,bool Odd,long? WindowId,long? BatchId) : IGameEvent;
public sealed record FeiYiMovementIssuedEvent(long FrameId,FeiYiStage Stage,long Before,long After,int Count,int Minimum) : IGameEvent;
public sealed record FeiYiCompletedEvent(long FrameId,bool Applied) : IGameEvent;
internal interface IFeiYiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame,SkillProgramEffectOp op); }
internal abstract class FeiYiDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler {get;}=new FeiYiHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(5));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class FeiYiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IFeiYiHost>().Execute(f,op);
}
internal static class FeiYiComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.YanruParityDrawDiscard or SkillProgramEffectOp.RevealSingletonAndArmRankEffects;
    [ProgramCatalogValidation(190)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        foreach(var a in p.Activations.Where(a=>a.Effects.Any(e=>IsOperation(e.Op))))
            if(a.Effects is not [{Op:SkillProgramEffectOp.YanruParityDrawDiscard,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||
                a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=0||a.MaxTargets!=0||a.UsesPerTurn is not null||a.UsesPerPhase is not null||a.UsesPerGame is not null||
                a.Condition.Kind!=SkillProgramConditionKind.Always||a.MarkerCost is not null||a.ContinueAfterOwnerDeath)
                throw new InvalidOperationException(path+": Yanru requires one empty owner actual-Play invocation with its independent parity ledger.");
        foreach(var t in p.Triggers.Where(t=>t.Effects.Any(e=>IsOperation(e.Op))))
            if(t.Effects is not [{Op:SkillProgramEffectOp.RevealSingletonAndArmRankEffects,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||
                t.Window!=SkillProgramTriggerWindow.CardsMoved||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||
                !t.SourceZones.SequenceEqual([CardZoneKind.Hand])||t.DestinationZones.Count!=0||t.MovementOccurrence!=SkillProgramMovementOccurrence.PerBatch||
                t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.UsageScope is not null||t.UsageLimit is not null||t.NamedUsageGroup is not null||t.ChoiceGroup is not null||
                t.MarkerCost is not null||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||
                t.MovementDiscardOnly||t.IgnoreOwnSkillMovements||t.GainPhaseQualification is not null||t.OwnerRelation is not null||t.IncludeResponseUses||t.EvaluateConditionAtResolution)
                throw new InvalidOperationException(path+": Hezhong requires one exact optional native singleton transition and its direction ledger.");
    }
}
