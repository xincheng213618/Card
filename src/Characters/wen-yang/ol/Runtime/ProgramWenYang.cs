using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WenYangStage { Choice, DrawChildren, RecoveryChildren, MaximumChildren, GrantChildren, HandDiscardChildren, GainChildren, EndingChildren }
public sealed record WenYangEntity(int CardId, CardKind Kind, CardLocation From);
public sealed record WenYangReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner,WenYangStage Stage,
    long? DamageId=null,long? DebtId=null,int Awakening=0,string? Branch=null,int Requested=0,int ExpectedRecovery=0,int HpBefore=0,int HpAfter=0,int MaximumBefore=0,int MaximumAfter=0,long Before=0,long After=0,int MovementStep=0,bool Paid=false)
{
    private readonly IReadOnlyList<WenYangEntity> _entities=Array.AsReadOnly(Array.Empty<WenYangEntity>());
    private readonly IReadOnlyList<int> _grain=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<WenYangEntity> Entities { get=>_entities; init=>_entities=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> WoodenOxGrain { get=>_grain; init=>_grain=Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WenYangReceipt? WenYang { get; init; } }
public sealed record WenYangStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner,long? DamageId,long? DebtId,int Awakening):IGameEvent;
public sealed record WenYangAwakenedEvent(long FrameId,int OwnerSeat,int Level,int MaximumBefore,int MaximumAfter,int HpBefore,int HpAfter):IGameEvent;
public sealed record WenYangLeafGrantedEvent(long FrameId,int OwnerSeat,string SkillId):IGameEvent;
public sealed record WenYangMovementIssuedEvent(long FrameId,int Step,WenYangStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record WenYangEntityMovedEvent(long FrameId,int Step,int CardId,CardKind Kind,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record WenYangDrawIssuedEvent(long FrameId,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record WenYangCompletedEvent(long FrameId,SkillProgramEffectOp Op):IGameEvent;

internal interface IWenYangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op); }
internal abstract class WenYangDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; }=new WenYangHandler(op);
    public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.ResolveLvliActualDamageChoice?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; }=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
}
internal sealed class ResolveWenYangAwakeningDescriptor():WenYangDescriptor(SkillProgramEffectOp.ResolveWenYangAwakening)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[]; }
internal sealed class WenYangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IWenYangHost>().Resolve(f,op);
}
internal static class WenYangComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveLvliActualDamageChoice or SkillProgramEffectOp.ResolveWenYangAwakening or SkillProgramEffectOp.ResolveQingjiaoWholeHandExchange or SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard;
    [ProgramCatalogValidation(540)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op)))&&!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Activations.Count!=0||p.Modifiers.Count!=0||p.ViewAs.Count!=0||p.Contributions.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.CardPolicies.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations||
            (p.Id=="ol:lvli"?p.Triggers.Count!=2:p.Id=="ol:choujue"?p.Triggers.Count!=2:p.Id=="ol:beishui"?p.Triggers.Count!=1:p.Id!="ol:qingjiao"||p.Triggers.Count!=2))
            throw new InvalidOperationException(path+": Wen Yang requires his exact native damage, two awakening and paid whole-hand/Ending entries.");
        foreach(var t in p.Triggers)
        {
            if(t.Effects is not[var e]||!Handles(e.Op)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.OwnerRelation is not null||t.SourceSkillId is not null||t.SourceViewAsId is not null||
                t.UsageLimit is not null||t.UsageScope is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.ChoiceGroup is not null||t.MarkerCost is not null||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.Suits.Count!=0||t.DamageCardKinds.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.ExcludedReasons.Count!=0||t.JudgmentReasons.Count!=0||t.JudgmentSource is not null||t.MovementOccurrence is not null||t.RequireDamageSource is not null||t.RequireNoCardConversion is not null||t.IncludeResponseUses||t.SingleActionInstance||t.OnlyDesignatedCardTargets||t.AllowNoEventTarget||t.DeferredTurnEndOnly||t.NoDyingAtActivation||t.AllowOwnDiscardPhaseEnded||t.EvaluateConditionAtResolution||t.IgnoreOwnSkillMovements||t.MovementDiscardOnly||t.GainPhaseQualification is not null||t.Priority!=0)
                throw new InvalidOperationException(path+": Wen Yang does not permit arbitrary filters, usage aliases or foreign receipts.");
            var valid=p.Id switch
            {
                "ol:lvli"=>e.Op==SkillProgramEffectOp.ResolveLvliActualDamageChoice&&t.Window==SkillProgramTriggerWindow.AfterDamageApplied&&t.Optional&&t.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&
                    (t.Id=="after-causing-damage"&&t.Subject==SkillProgramTriggerSubject.DamageSource||t.Id=="after-receiving-damage"&&t.Subject==SkillProgramTriggerSubject.Owner),
                "ol:choujue"=>e.Op==SkillProgramEffectOp.ResolveWenYangAwakening&&t.Window==SkillProgramTriggerWindow.AfterTurnEnded&&!t.Optional&&t.DamageOccurrence is null&&t.Subject==SkillProgramTriggerSubject.Owner&&
                    (t.Id=="own-actual-turn-ended"&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own||t.Id=="foreign-actual-turn-ended"&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving),
                "ol:beishui"=>e.Op==SkillProgramEffectOp.ResolveWenYangAwakening&&t.Id=="actual-preparation"&&t.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&!t.Optional&&t.DamageOccurrence is null&&t.Subject==SkillProgramTriggerSubject.Owner&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own,
                _=>t.Subject==SkillProgramTriggerSubject.Owner&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&t.DamageOccurrence is null&&
                    (t.Id=="whole-hand-at-play-start"&&e.Op==SkillProgramEffectOp.ResolveQingjiaoWholeHandExchange&&t.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&t.Optional||t.Id=="issued-actual-ending"&&e.Op==SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard&&t.Window==SkillProgramTriggerWindow.TurnEnding&&!t.Optional)
            };
            if(!valid)throw new InvalidOperationException(path+": Wen Yang's instruction must keep its exact skill, native window, subject and optionality.");
        }
        if(p.Triggers.Select(t=>t.Id).Distinct().Count()!=p.Triggers.Count)throw new InvalidOperationException(path+": Duplicate Wen Yang entries.");
    }
}
