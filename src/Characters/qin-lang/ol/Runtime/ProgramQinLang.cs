using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum QinLangXianyingStage { DrawChildren, DiscardChoice, DiscardChildren, EndChoice, NativeUse, NativeChildren }
public sealed record QinLangXianyingCard(int CardId,CardKind Kind,CardLocation From,CardKind? EffectiveName=null);
public sealed record QinLangXianyingUseOrigin(long ProgramFrameId,long CardUseFrameId,long GrantFrameId,
    CardConversionSource Source,string GameplayHash,int ActualTurn,CardKind CardKind,int ActorSeat,
    LegalActionKind ActionKind,int? TargetCardId,CardKind? RequiredCardKind,string TargetSignature);
public sealed record QinLangXianyingReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,
    int ActualTurn,int TurnOwner,int RoundNumber,QinLangXianyingStage Stage)
{
    public long Before { get; init; }
    public long After { get; init; }
    public int ActualDrawCount { get; init; }
    public long PaidBatchId { get; init; }
    public bool Paid { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QinLangXianyingUseOrigin? Use { get; init; }
    private IReadOnlyList<QinLangXianyingCard> _cards=Array.AsReadOnly(Array.Empty<QinLangXianyingCard>()),_selected=Array.AsReadOnly(Array.Empty<QinLangXianyingCard>()),_aux=Array.AsReadOnly(Array.Empty<QinLangXianyingCard>());
    public IReadOnlyList<QinLangXianyingCard> Cards { get=>_cards; init=>_cards=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<QinLangXianyingCard> Selected { get=>_selected; init=>_selected=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<QinLangXianyingCard> Auxiliaries { get=>_aux; init=>_aux=Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<QinLangXianyingGrantedEvent> _grants=Array.AsReadOnly(Array.Empty<QinLangXianyingGrantedEvent>());
    public IReadOnlyList<QinLangXianyingGrantedEvent> Grants { get=>_grants; init=>_grants=Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QinLangXianyingReceipt? QinLangXianying { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QinLangXianyingUseOrigin? QinLangXianying { get; init; } }
public sealed record QinLangXianyingStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int RoundNumber,bool Ending) : IGameEvent;
public sealed record QinLangXianyingDrawnEvent(long FrameId,int OwnerSeat,int Count,long Before,long After) : IGameEvent;
public sealed record QinLangXianyingDiscardPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int RoundNumber,int Count,long Before,long After) : IGameEvent;
public sealed record QinLangXianyingEntityDiscardedEvent(long FrameId,int CardId,CardKind Kind,CardLocation From,long Sequence,CardKind Name) : IGameEvent;
public sealed record QinLangXianyingGrantedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int Count,CardKind Name) : IGameEvent;
public sealed record QinLangXianyingGrantConsumedEvent(long GrantFrameId,long ProgramFrameId,int OwnerSeat,int ActualTurn,bool UseIssued) : IGameEvent;
public sealed record QinLangXianyingUseIssuedEvent(QinLangXianyingUseOrigin Origin) : IGameEvent;
public sealed record QinLangXianyingUseReturnedEvent(QinLangXianyingUseOrigin Origin) : IGameEvent;
internal interface IQinLangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffect effect); }
internal abstract class QinLangDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler=>new QinLangHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; }=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class ResolveXianyingDrawDiscardDescriptor:QinLangDescriptor { public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXianyingDrawDiscard; }
internal sealed class ResolveXianyingTurnEndUsesDescriptor:QinLangDescriptor
{ public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXianyingTurnEndUses; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)]; }
internal sealed class QinLangHandler(SkillProgramEffectOp operation):ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op=>operation; public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IQinLangHost>().Resolve(f,e); }
internal static class QinLangComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveXianyingDrawDiscard or SkillProgramEffectOp.ResolveXianyingTurnEndUses;
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op)))&&!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        bool One(SkillProgramTrigger t,SkillProgramEffectOp op)=>t.Effects is[var e]&&e.Op==op&&e.Target==SkillProgramEffectTarget.Owner&&e.Condition.Kind==SkillProgramConditionKind.Always;
        if(p.Id!="ol:xianying"||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.Contributions.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations||p.Triggers is not[var prep,var damage,var own,var foreign]||
           prep.Id!="preparation"||prep.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow||!prep.Optional||!One(prep,SkillProgramEffectOp.ResolveXianyingDrawDiscard)||
           damage.Id!="after-damage"||damage.Window!=SkillProgramTriggerWindow.AfterDamageApplied||damage.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage||!damage.Optional||!One(damage,SkillProgramEffectOp.ResolveXianyingDrawDiscard)||
           own.Id!="turn-end-own"||own.Window!=SkillProgramTriggerWindow.AfterTurnEnded||own.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||own.Optional||!One(own,SkillProgramEffectOp.ResolveXianyingTurnEndUses)||
           foreign.Id!="turn-end-foreign"||foreign.Window!=SkillProgramTriggerWindow.AfterTurnEnded||foreign.TurnOwnerScope!=SkillProgramTurnOwnerScope.OtherLiving||foreign.Optional||!One(foreign,SkillProgramEffectOp.ResolveXianyingTurnEndUses)||
           p.Triggers.Any(t=>t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.MarkerCost is not null||t.UsageLimit is not null||t.UsageScope is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.IncludeResponseUses||t.SingleActionInstance||t.IgnoreOwnSkillMovements))
            throw new InvalidOperationException(path+": Xianying requires its exact preparation/damage draw-discard and own/foreign actual-turn-end paid-grant entries.");
    }
}
