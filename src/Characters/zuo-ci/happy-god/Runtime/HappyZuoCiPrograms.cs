using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum HappyZuoCiStage { InitialChildren, PhantomChoice, TemplateChoice, GainChildren, Drawing, DrawChildren, RefreshChildren }
public sealed record HappyZuoCiFace(CardKind Kind,Suit Suit,int Rank,string? PrintedName);
public sealed record HappyZuoCiEntity(int CardId,int PrototypeId,long CreatedFrameId)
{
    private readonly IReadOnlyList<HappyZuoCiFace> _faces=Array.AsReadOnly(Array.Empty<HappyZuoCiFace>());
    public IReadOnlyList<HappyZuoCiFace> Faces {get=>_faces;init=>_faces=Array.AsReadOnly(value.ToArray());}
    public bool ManuallyTransformed {get;init;}
}
public sealed record HappyZuoCiPrototype(int PhysicalId,HappyZuoCiFace Face);
public sealed record HappyZuoCiState(long InitialFrameId,CardConversionSource Source,string GameplayHash,CardLocation Deck)
{
    private readonly IReadOnlyList<HappyZuoCiEntity> _entities=Array.AsReadOnly(Array.Empty<HappyZuoCiEntity>());
    private readonly IReadOnlyList<int> _templates=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<HappyZuoCiEntity> Entities {get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> UsedTemplates {get=>_templates;init=>_templates=Array.AsReadOnly(value.ToArray());}
    public int BonusTurn {get;init;}=-1;
    public int BonusUses {get;init;}
}
public sealed partial class CharacterState {internal HappyZuoCiState? HappyZuoCi {get;set;}}
public sealed record HappyZuoCiReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn,HappyZuoCiStage Stage)
{
    public int PhantomId {get;init;}
    public int TemplateId {get;init;}
    public int Requested {get;init;}
    public int Overflow {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public NativeDrawOutcome? DrawOutcome {get;init;}
    private readonly IReadOnlyList<int> _cards=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> GainedIds {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyZuoCiReceipt? HappyZuoCi {get;init;}}
// The destination invoice belongs to trusted movement history, never a player view.
public sealed record HappyZuoCiPhantomDestinationPlan(int CardId,int Issuer,long InitialFrameId,CardConversionSource Source,string GameplayHash,
    int FaceVersion,HappyZuoCiFace Face,CardLocation From,CardLocation RequestedTo,CardLocation ActualTo,CardMoveReason Reason,long SequenceBefore,int EventCountBefore,
    int? OriginalForeignGainSequence=null,int? OriginalRecipient=null);
public sealed partial record CardMovementRecord
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyZuoCiPhantomDestinationPlan? HappyZuoCiPhantomDestination {get;init;}}
public sealed record HappyZuoCiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,long ParentId,int ActualTurn):IGameEvent;
public sealed record HappyZuoCiPoolInitializedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Count):IGameEvent;
public sealed record HappyZuoCiGainIssuedEvent(long FrameId,int Requested,int ActualPhantoms,int Overflow,long Before,long After):IGameEvent;
public sealed record HappyZuoCiTransformedEvent(long FrameId,int OwnerSeat,bool SameSuit):IGameEvent;
public sealed record HappyZuoCiRefreshedEvent(long FrameId,int OwnerSeat,int Count):IGameEvent;
public sealed record HappyZuoCiHuanjingIssuedEvent(long FrameId,int OwnerSeat,int ActualTurn,int Amount):IGameEvent;
public sealed record HappyZuoCiDrawReturnedEvent(long FrameId,int Requested,int Actual,long Before,long After):IGameEvent;
internal interface IHappyZuoCiHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp operation);}
internal abstract class HappyZuoCiDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler{get;}=new HappyZuoCiHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.TransformHappyZuoCiPhantom?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>op switch
    {
        SkillProgramEffectOp.InitializeHappyZuoCiPhantoms=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.GainHappyZuoCiPhantoms=>[new RequireTriggerWindows([SkillProgramTriggerWindow.RoundStarting,SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramTriggerWindow.CardsMoved])],
        SkillProgramEffectOp.RefreshHappyZuoCiPhantoms=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        _=>[new RequireActivationEntry()]
    };
}
internal sealed class InitializeHappyZuoCiPhantomsDescriptor():HappyZuoCiDescriptor(SkillProgramEffectOp.InitializeHappyZuoCiPhantoms);
internal sealed class GainHappyZuoCiPhantomsDescriptor():HappyZuoCiDescriptor(SkillProgramEffectOp.GainHappyZuoCiPhantoms);
internal sealed class RefreshHappyZuoCiPhantomsDescriptor():HappyZuoCiDescriptor(SkillProgramEffectOp.RefreshHappyZuoCiPhantoms);
internal sealed class TransformHappyZuoCiPhantomDescriptor():HappyZuoCiDescriptor(SkillProgramEffectOp.TransformHappyZuoCiPhantom);
internal sealed class ResolveHappyZuoCiHuanjingDescriptor():HappyZuoCiDescriptor(SkillProgramEffectOp.ResolveHappyZuoCiHuanjing);
internal sealed class HappyZuoCiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHappyZuoCiHost>().Resolve(f,op);
}
internal static class HappyZuoCiComposition
{
    internal const string Huanshu="happy:huanshu",Huanhua="happy:huanhua",Huanjing="happy:huanjing";
    internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.InitializeHappyZuoCiPhantoms or SkillProgramEffectOp.GainHappyZuoCiPhantoms or SkillProgramEffectOp.RefreshHappyZuoCiPhantoms or SkillProgramEffectOp.TransformHappyZuoCiPhantom or SkillProgramEffectOp.ResolveHappyZuoCiHuanjing;
    [ProgramCatalogValidation(42600)]internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Own(e.Op)))return;
        var valid=p.Id==Huanshu?p.Activations.Count==0&&p.Triggers is[
            {Id:"initial-phantoms",Window:SkillProgramTriggerWindow.GameStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:false,Effects:[{Op:SkillProgramEffectOp.InitializeHappyZuoCiPhantoms}]},
            {Id:"round-phantoms",Window:SkillProgramTriggerWindow.RoundStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:false,Effects:[{Op:SkillProgramEffectOp.GainHappyZuoCiPhantoms}]},
            {Id:"damage-phantoms",Window:SkillProgramTriggerWindow.AfterDamageApplied,Subject:SkillProgramTriggerSubject.Owner,DamageOccurrence:SkillProgramDamageOccurrence.PerDamagePoint,Optional:false,Effects:[{Op:SkillProgramEffectOp.GainHappyZuoCiPhantoms}]},
            {Id:"refresh-phantoms",Window:SkillProgramTriggerWindow.PlayPhaseStarting,Subject:SkillProgramTriggerSubject.Owner,Optional:false,Effects:[{Op:SkillProgramEffectOp.RefreshHappyZuoCiPhantoms}]},
            {Id:"foreign-phantom",Window:SkillProgramTriggerWindow.CardsMoved,Subject:SkillProgramTriggerSubject.Owner,MovementOccurrence:SkillProgramMovementOccurrence.PerCard,Optional:false,Effects:[{Op:SkillProgramEffectOp.GainHappyZuoCiPhantoms}]}]&&p.Triggers[4].SourceZones.SequenceEqual(new[]{CardZoneKind.Hand}):
            p.Triggers.Count==0&&p.Activations is[{MinCards:0,MaxCards:0,MinTargets:0,MaxTargets:0,UsesPerTurn:null,UsesPerPhase:null}a]&&
            (p.Id==Huanhua&&a.Id=="transform-phantom"&&a.UsesPerGame is null&&a.Effects is[{Op:SkillProgramEffectOp.TransformHappyZuoCiPhantom}]||
             p.Id==Huanjing&&a.Id=="limited-phantoms"&&a.UsesPerGame==1&&a.Effects is[{Op:SkillProgramEffectOp.ResolveHappyZuoCiHuanjing}]);
        if(!valid||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.BooleanStates.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||
            p.Triggers.Any(t=>t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.MarkerCost is not null)||
            p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path+": Happy Zuo Ci requires his exact real phantom pool, per-point/round gains, persistent face transform and limited turn bonus.");
    }
}
