using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MiuyanUseInvoice(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,
    int ActualTurn,int TurnOwner,int RoundNumber,ActualDiscardRecoveryPhaseKey Phase,SkillPolarity PreviousState,SkillPolarity CurrentState,
    int OriginalActor,int ProviderSeat,CardActionCost Material,long BeforePayment);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MiuyanUseInvoice? MiuyanUse{get;init;} }
public enum WangGuanStage { MiuyanGainChildren, ShiluDrawChildren, ShiluTarget, ShiluCard }
public sealed record WangGuanHandEntity(int CardId,CardKind PrintedKind,int HolderSeat,int Slot,long Entry);
public sealed record WangGuanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,
    long ParentId,long NativeId,int FrozenHp,WangGuanStage Stage,long Before,long After,int ActualCount)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MiuyanUseInvoice? Use{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public int? HolderSeat{get;init;}
    private IReadOnlyList<WangGuanHandEntity> _entities=Array.AsReadOnly(Array.Empty<WangGuanHandEntity>());
    public IReadOnlyList<WangGuanHandEntity> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangGuanReceipt? WangGuan{get;init;} }
public sealed record MiuyanUseIssuedEvent(MiuyanUseInvoice Invoice):IGameEvent;
public sealed record MiuyanPhaseHandRevealedEvent(ActualDiscardRecoveryPhaseKey Phase,long RevealFrameId,int CardId,int HolderSeat,long Entry,string RevealKind):IGameEvent;
public sealed record MiuyanRoundDisabledEvent(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int RoundNumber):IGameEvent;
public sealed record WangGuanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,long ParentId,long NativeId,int FrozenHp):IGameEvent;
public sealed record MiuyanClaimEntityFrozenEvent(long FrameId,int Index,WangGuanHandEntity Entity):IGameEvent;
public sealed record MiuyanEntityObtainedEvent(long FrameId,int Index,WangGuanHandEntity Entity,long MovementSequence):IGameEvent;
public sealed record WangGuanMovementIssuedEvent(long FrameId,WangGuanStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record ShiluHolderChosenEvent(long FrameId,int HolderSeat,long MovementCutoff):IGameEvent;
public sealed record ShiluCandidateFrozenEvent(long FrameId,int Index,WangGuanHandEntity Entity):IGameEvent;
public sealed record ShiluHandIdentityGrantedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long DamageFrameId,int HolderSeat,int CardId,long HandEntry,long MovementCutoff):IGameEvent;
public sealed record WangGuanCompletedEvent(long FrameId):IGameEvent;

internal interface IWangGuanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal abstract class WangGuanDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new WangGuanHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op==SkillProgramEffectOp.ResolveMiuyanCompletedUse?SkillProgramTriggerWindow.CardUseCompleted:SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ResolveMiuyanCompletedUseDescriptor:WangGuanDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMiuyanCompletedUse; public override bool UsesConversionPolarity=>true;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class ResolveShiluDrawRevealIdentityDescriptor:WangGuanDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveShiluDrawRevealIdentity; }
internal sealed class WangGuanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IWangGuanHost>().Resolve(f,op); }
internal static class WangGuanComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveMiuyanCompletedUse or SkillProgramEffectOp.ResolveShiluDrawRevealIdentity;
    [ProgramCatalogValidation(440)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Activations.Count!=0||p.Triggers.Count!=1||p.Id is not("ol:miuyan"or"ol:shilu"))throw new InvalidOperationException(path+": Wang Guan requires his complete exact current ordinary-OL program.");
        var t=p.Triggers[0];var miuyan=p.Id=="ol:miuyan";
        if(t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]||
            e.Op!=(miuyan?SkillProgramEffectOp.ResolveMiuyanCompletedUse:SkillProgramEffectOp.ResolveShiluDrawRevealIdentity)||t.Optional||t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)
            throw new InvalidOperationException(path+": Wang Guan requires one mandatory exact owner instruction.");
        if(miuyan)
        {
            if(t.Window!=SkillProgramTriggerWindow.CardUseCompleted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Subject is not null||t.IncludeResponseUses||!t.SingleActionInstance||
                p.ViewAs is not[{Id:"black-fire-attack",OutputKind:CardKind.FireAttack,ForPlay:true,ForResponse:false,SingleCardTrickUse:true}r]||
                r.InputCount!=1||r.Condition.Kind!=SkillProgramConditionKind.Always||r.InputKinds.Count!=0||!r.InputSuits.Order().SequenceEqual(new[]{Suit.Spade,Suit.Club}.Order())||
                !r.SourceZones.Order().SequenceEqual(new[]{CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.WoodenOxGrain}.Order())||!r.AllowSameKind)
                throw new InvalidOperationException(path+": Miuyan requires a true completed native Use and one black HE/Ox-grain Fire Attack conversion.");
        }
        else if(p.ViewAs.Count!=0||t.Window!=SkillProgramTriggerWindow.AfterDamageApplied||t.Subject!=SkillProgramTriggerSubject.Owner||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage)
            throw new InvalidOperationException(path+": Shilu requires one mandatory positive whole-damage observer.");
    }
}
