using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum RuanYuStage { Hand,Bottom,Order,FrontChildren,EndingTarget,EndingChildren,LoseHpIssuing,LoseHpChildren,RedChildren }
public sealed record RuanYuEntity(int CardId,CardKind Kind,CardLocation From);
public sealed record RuanYuMiaoxianPaymentSource(CardConversionSource Source,string GameplayHash,int HandRedCount,int? UniqueRedCardId,int HandBlackCount,int? UniqueBlackCardId)
{
    private IReadOnlyList<int> _redGrain=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> RedGrainIds {get=>_redGrain;init=>_redGrain=Array.AsReadOnly(value.ToArray());}
}
public sealed record RuanYuReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner,RuanYuStage Stage,int Recipient)
{
    public bool Paid {get;init;} public int Step {get;init;} public long Before {get;init;} public long After {get;init;} public int ActualCount {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? DebtId {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? ActionId {get;init;}
    private IReadOnlyList<int> _viewed=Array.AsReadOnly(Array.Empty<int>()),_hand=Array.AsReadOnly(Array.Empty<int>()),_selected=Array.AsReadOnly(Array.Empty<int>()),_order=Array.AsReadOnly(Array.Empty<int>()),_deck=Array.AsReadOnly(Array.Empty<int>()),_desired=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ViewedBottom {get=>_viewed;init=>_viewed=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> SelectedHand {get=>_hand;init=>_hand=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> SelectedBottom {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> OrderedBottom {get=>_order;init=>_order=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> DeckBefore {get=>_deck;init=>_deck=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> DesiredDeck {get=>_desired;init=>_desired=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<RuanYuEntity> _entities=Array.AsReadOnly(Array.Empty<RuanYuEntity>());
    public IReadOnlyList<RuanYuEntity> Entities {get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public RuanYuReceipt? RuanYu {get;init;}}
public sealed record RuanYuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner):IGameEvent;
public sealed record RuanYuMovementPaidEvent(long FrameId,int Step,RuanYuStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record RuanYuEntityMovedEvent(long FrameId,int Step,long BatchId,int CardId,CardKind Kind,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record XingzuoEndingIssuedEvent(long DebtId,CardConversionSource Source,string GameplayHash,int ActualTurn):IGameEvent;
public sealed record XingzuoEndingConsumedEvent(long DebtId,long FrameId,long ParentId):IGameEvent;
public sealed record XingzuoBottomOrderedEvent(long FrameId,int Index,int CardId,bool Ending):IGameEvent;
public sealed record MiaoxianPaymentQualifiedEvent(long BatchId,long? ParentFrameId,long ActionId,CardConversionSource Source,string GameplayHash,int CardId,CardKind Kind,CardLocation From,long Sequence,int ActualTurn,bool Red,bool Black):IGameEvent;
public sealed record MiaoxianNativeCounterspellFinishedEvent(long ActionId,long ParentFrameId,int CardId,long EntrySequence,long FinishSequence):IGameEvent;
public sealed record MiaoxianBlackConsumedEvent(long ActionId,CardConversionSource Source,int ActualTurn,int CardId):IGameEvent;
public sealed record MiaoxianRedDrawIssuedEvent(long FrameId,long ActionId,CardConversionSource Source,int ActualTurn):IGameEvent;
public sealed record RuanYuCompletedEvent(long FrameId):IGameEvent;
public sealed record XingzuoHpLossPaidEvent(long FrameId,int RemainingHp):IGameEvent;
internal interface IRuanYuHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class RuanYuDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op==SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange?SkillProgramTriggerWindow.PlayPhaseStarting:Op==SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange?SkillProgramTriggerWindow.TurnEnding:SkillProgramTriggerWindow.CardUseCommitted)];
    public override ProgramOperationInteraction Interaction=>Op==SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw?ProgramOperationInteraction.Automatic:ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler=>new RuanYuHandler(Op);
}
internal sealed class ResolveXingzuoPrivateBottomExchangeDescriptor:RuanYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange;}
internal sealed class ResolveXingzuoIssuedEndingExchangeDescriptor:RuanYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange;}
internal sealed class ResolveMiaoxianUniqueRedUseDrawDescriptor:RuanYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw;}
internal sealed class RuanYuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IRuanYuHost>().Resolve(f,op);}
internal sealed class RuanYuMiaoxianCommittedUseContract:IProgramCommittedUseContract
{public string SkillId=>"ol:miaoxian";public void ValidateTrigger(SkillProgramTrigger trigger)=>RuanYuComposition.ValidateRedTrigger("ol:miaoxian",trigger);}
internal static class RuanYuComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange or SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange or SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw;
    internal static void ValidateRedTrigger(string path,SkillProgramTrigger t)
    {
        if(t.Id!="unique-red-use"||t.Window!=SkillProgramTriggerWindow.CardUseCommitted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!t.IncludeResponseUses||t.Optional||t.Subject is not null||t.SingleActionInstance||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.Priority!=0||
            t.Effects is not[{Op:SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw,Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always}]||
            t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.ChoiceGroup is not null||t.OrderGroup is not null||t.MarkerCost is not null||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.Suits.Count!=0||t.DamageCardKinds.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.ExcludedReasons.Count!=0||t.JudgmentReasons.Count!=0||t.JudgmentSource is not null||t.DamageOccurrence is not null||t.MovementOccurrence is not null||t.RequireDamageSource is not null||t.RequireNoCardConversion is not null||t.GainPhaseQualification is not null||t.AllowNoEventTarget||t.NoDyingAtActivation||t.DeferredTurnEndOnly||t.OnlyDesignatedCardTargets||t.AllowOwnDiscardPhaseEnded||t.EvaluateConditionAtResolution||t.IgnoreOwnSkillMovements||t.MovementDiscardOnly)
            throw new InvalidOperationException(path+": Miaoxian red requires one exact mandatory actual-Use actor instruction, including genuine response Uses, with prepayment physical qualification.");
    }
    [ProgramCatalogValidation(600)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op)))&&!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Id is not("ol:xingzuo"or"ol:miaoxian")||p.Activations.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0||p.DamageModifiers.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations)throw new InvalidOperationException(path+": Ruan Yu requires independent exact bottom-exchange and true-use contracts.");
        var red=p.Id=="ol:miaoxian";
        if(p.Triggers.Count!=(red?1:2)||!red&&p.ViewAs.Count!=0)throw new InvalidOperationException(path+": Unexpected Ruan Yu bindings.");
        foreach(var t in p.Triggers)
        {
            if(red){ValidateRedTrigger(path,t);continue;}
            var ending=t.Id=="issued-ending";var op=red?SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw:ending?SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange:SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange;
            if(t.Id!=(red?"unique-red-use":ending?"issued-ending":"private-bottom")||t.Window!=(red?SkillProgramTriggerWindow.CardUseCommitted:ending?SkillProgramTriggerWindow.TurnEnding:SkillProgramTriggerWindow.PlayPhaseStarting)||t.Optional==red||t.OwnerRelation!=(red?SkillProgramCardActionOwnerRelation.Actor:null)||t.Subject!=SkillProgramTriggerSubject.Owner||t.IncludeResponseUses!=red||t.SingleActionInstance!=red||t.SourceSkillId is not null||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]||e.Op!=op)throw new InvalidOperationException(path+": Unexpected Ruan Yu timing, source or usage metadata.");
        }
        if(!red)return;
        // Optional activity cards have separate native-use contracts from this identity bundle.
        var expected=CardCatalog.ImplementedCards.Where(c=>c.CategoryName=="锦囊牌"&&c.Kind is not(CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning or CardKind.AllianceFeast or CardKind.SowBeans or CardKind.TransplantWood or CardKind.ReturnSoul or CardKind.StarShift or CardKind.BeamSwap or CardKind.HandRedistribution or CardKind.WenheChaos or CardKind.StoreGrain or CardKind.Reinforcements or CardKind.UnitedEffort or CardKind.SameBoat or CardKind.StriveUpstream or CardKind.AgainstCurrent or CardKind.WumiaoFlood)).Select(c=>c.Kind).ToHashSet();
        if(p.ViewAs.Count!=expected.Count||!p.ViewAs.Select(v=>v.OutputKind).ToHashSet().SetEquals(expected))throw new InvalidOperationException(path+": Miaoxian must include every native ordinary trick.");
        foreach(var v in p.ViewAs)if(v.Id!="black-"+v.OutputKind.ToString().ToLowerInvariant()||v.InputCount!=1||!v.SourceZones.SequenceEqual([CardZoneKind.Hand])||v.InputKinds.Count!=0||!v.InputSuits.SequenceEqual([Suit.Spade,Suit.Club])||!v.UseEffectiveInputSuit.GetValueOrDefault()||!v.AllowSameKind||v.ForPlay!=(v.OutputKind!=CardKind.Nullification)||v.ForResponse!=(v.OutputKind==CardKind.Nullification)||v.SingleCardTrickUse!=(v.OutputKind is not(CardKind.Nullification or CardKind.UnexpectedAssault))||v.Condition.Kind!=SkillProgramConditionKind.Always||v.UsesPerPhase is not null||v.UsesPerActualTurn is not null||v.CostDestination is not null||v.UseOnly||v.DamageBonus!=0||v.RecoveryBonus!=0||v.DistanceUnlimited||v.InputCategories.Count!=0||v.VariableInputCount||v.ExtendedUse||v.ExcludeOwnerEffects||v.UnusedOutputThisTurn)throw new InvalidOperationException(path+": Miaoxian conversion must retain exact one-Hand ordinary native use without extra privileges.");
    }
}
