using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MaYuanYiStage { StoreChildren, OpponentDrawChildren, MaximumChildren, GrantChildren, TargetChoice, Judgment, JudgmentChildren, DamageChildren }
public sealed record MaYuanYiStoredEvent(long FrameId, CardConversionSource Source, int Index, int CardId, CardLocation Pile, long Sequence) : IGameEvent;
public sealed record MaYuanYiBingActionEvent(long ActionId, long? ParentActionId, int ActualTurn, int TurnOwner, CardActionType Type, int ActorSeat, int ProviderSeat, int? RequesterSeat, int? OpponentSeat, CardKind Kind, int CardId, CardLocation From, CardConversionSource Source, long StoredFrameId, long StoredSequence, long PaymentSequence) : IGameEvent;
public sealed record MaYuanYiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long ParentId, int ActualTurn, int TurnOwner, int FactionCount, int BingBefore, int MaximumBefore, int HpBefore, long? ActionId, int? OpponentSeat) : IGameEvent;
public sealed record MaYuanYiMovementEvent(long FrameId, MaYuanYiStage Stage, long Before, long After, int Count) : IGameEvent;
public sealed record MaYuanYiCompletedEvent(long FrameId) : IGameEvent;
public sealed record MaYuanYiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long ParentId, int ActualTurn, int TurnOwner, int FactionCount, int BingBefore, int MaximumBefore, int HpBefore, MaYuanYiStage Stage,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? ActionId=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? OpponentSeat=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] CardLocation? Pile=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? TargetSeat=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? JudgmentId=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? JudgmentCardId=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] Suit? JudgmentSuit=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? JudgmentHandoffSequence=null,
    long Before=0, long After=0, int ActualCount=0);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MaYuanYiReceipt? MaYuanYi { get; init; } }
internal interface IMaYuanYiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class MaYuanYiDescriptor(SkillProgramEffectOp op, SkillProgramTriggerWindow window) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler {get;}=new MaYuanYiHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(window)];
    public override ProgramOperationInteraction Interaction=>op==SkillProgramEffectOp.ResolveBinghuoEndingJudgment?ProgramOperationInteraction.Choice:ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
}
internal sealed class MaYuanYiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op=>op; public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IMaYuanYiHost>().Resolve(f,op); }
internal static class MaYuanYiComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing or SkillProgramEffectOp.ResolveWangjingBingOpponentDraw or SkillProgramEffectOp.ResolveMoucuanAwakening or SkillProgramEffectOp.ResolveBinghuoEndingJudgment;
    [ProgramCatalogValidation(550)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Activations.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0)
            throw new InvalidOperationException(path+": Ma Yuan Yi requires its exact native pile, opponent, awakening and judgment protocols.");
        var expected=p.Id switch {"ol:jibing"=>SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing,"ol:wangjing"=>SkillProgramEffectOp.ResolveWangjingBingOpponentDraw,"ol:moucuan"=>SkillProgramEffectOp.ResolveMoucuanAwakening,"ol:binghuo"=>SkillProgramEffectOp.ResolveBinghuoEndingJudgment,_=>(SkillProgramEffectOp)(-1)};
        if(p.Triggers.Count!=(p.Id is"ol:wangjing"or"ol:binghuo"?2:1)||p.Triggers.Any(t=>t.Effects is not [{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]||e.Op!=expected||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.MarkerCost is not null||t.Priority!=0))
            throw new InvalidOperationException(path+": Ma Yuan Yi requires complete, unfiltered single native instructions.");
        foreach(var t in p.Triggers)
        {
            var ok=expected switch {
                SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing=>t.Id=="replace-normal-draw"&&t.Window==SkillProgramTriggerWindow.DrawPhaseStarting&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Optional&&t.DrawPhaseMode==SkillProgramDrawPhaseMode.Replacement&&t.UsageLimit is null,
                SkillProgramEffectOp.ResolveMoucuanAwakening=>t.Id=="all-factions-awakening"&&t.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&t.Subject==SkillProgramTriggerSubject.Owner&&!t.Optional&&t.UsageScope==SkillUsageScope.Game&&t.UsageLimit==1,
                SkillProgramEffectOp.ResolveBinghuoEndingJudgment=>(t.Id=="own-actual-turn-bing-ending"&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own||t.Id=="foreign-actual-turn-bing-ending"&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving)&&t.Window==SkillProgramTriggerWindow.TurnEnding&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Optional&&t.UsageLimit is null,
                _=>!t.Optional&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.ConversionSource&&t.SourceSkillId=="ol:jibing"&&t.UsageLimit is null&&
                    (t.Id=="bing-use-opponent"&&t.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&t.SourceViewAsId=="bing-slash"&&t.CardKinds.Count==0||
                     t.Id=="bing-response-opponent"&&t.Window==SkillProgramTriggerWindow.CardResponseAccepted&&t.SourceViewAsId is null&&t.CardKinds.Count==0)};
            if(!ok)throw new InvalidOperationException(path+": Ma Yuan Yi lost its exact lifecycle, conversion-source or mandatory opponent trigger.");
        }
        if(expected!=SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing){if(p.ViewAs.Count!=0)throw new InvalidOperationException(path+": Only Jibing owns pile conversions.");return;}
        if(p.ViewAs.Count!=2||!p.ViewAs.Any(v=>v.Id=="bing-slash"&&v.OutputKind==CardKind.Slash&&v.ForPlay)||!p.ViewAs.Any(v=>v.Id=="bing-dodge"&&v.OutputKind==CardKind.Dodge&&!v.ForPlay)||
            p.ViewAs.Any(v=>v.SourcePileSkillId!="ol:jibing"||v.InputCount!=1||v.VariableInputCount||!v.ForResponse||!v.AllowSameKind||!v.SourceZones.SequenceEqual([CardZoneKind.PublicPersistentPile])||v.InputKinds.Count!=0||v.InputSuits.Count!=0||v.UsesPerPhase is not null||v.UsesPerActualTurn is not null||v.SingleCardTrickUse||v.ExtendedUse))
            throw new InvalidOperationException(path+": Jibing converts exactly one entity of its own original live pile, including same-name entities, for native Use or played responses.");
    }
}
