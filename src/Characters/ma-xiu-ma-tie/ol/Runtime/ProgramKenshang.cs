using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record KenshangUseReceipt(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat,int MaterialCount,IReadOnlyList<int> OriginalTargets)
{
    private IReadOnlyList<int> _originalTargets=Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets{get=>_originalTargets;init=>_originalTargets=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int>? _targets;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<int>? RewrittenTargets{get=>_targets;init=>_targets=value is null?null:Array.AsReadOnly(value.ToArray());}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? RewriteFrameId{get;init;}
    public bool Offered{get;init;}
    public bool DrawIssued{get;init;}
}
public enum KenshangProgramStage{Targets,DrawChildren}
public sealed record KenshangProgramReceipt(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int InstructionIndex,KenshangProgramStage Stage)
{
    private IReadOnlyList<int> _selected=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public long Before{get;init;}public long After{get;init;}public int DrawCount{get;init;}
}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public KenshangUseReceipt? Kenshang{get;init;}[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public KenshangRequestReceipt? KenshangRequest{get;init;}}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public KenshangProgramReceipt? Kenshang{get;init;}[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public KenshangRequestReceipt? KenshangRequest{get;init;}}
public sealed record KenshangUseIssuedEvent(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat,int MaterialCount):IGameEvent;
public sealed record KenshangMaterialEvent(long UseId,int Index,CardActionCost Cost):IGameEvent;
public sealed record KenshangDamageEvent(long UseId,long ActionId,long DamageFrameId,int SourceSeat,int TargetSeat,int Amount,bool Chain):IGameEvent;
public sealed record KenshangTargetSelectedEvent(long ProgramFrameId,long UseId,int Index,int TargetSeat):IGameEvent;
public sealed record KenshangTargetsCommittedEvent(long ProgramFrameId,long UseId,long ActionId,int Count):IGameEvent;
public sealed record KenshangRedirectedEvent(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int InstructionIndex,int TargetIndex,int PreviousSeat,int TargetSeat):IGameEvent;
public sealed record KenshangDrawIssuedEvent(long ProgramFrameId,long UseId,long ActionId,int DamageTotal,int MaterialCount,long Before,long After,int ActualCount):IGameEvent;
internal interface IKenshangHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class KenshangDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new KenshangHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op==SkillProgramEffectOp.ResolveKenshangTargetRewrite?SkillProgramTriggerWindow.CardUseCommitted:SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ResolveKenshangTargetRewriteDescriptor:KenshangDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveKenshangTargetRewrite;}
internal sealed class DrawForKenshangShortDamageDescriptor:KenshangDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawForKenshangShortDamage;}
internal sealed class KenshangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IKenshangHost>().Resolve(f,op);}
internal static class KenshangConversionSchema
{
    internal static void Validate(string path,SkillProgramViewAs r)
    {
        if(r.KenshangUse is null)return;
        if(r.KenshangUse!=true||!r.VariableInputCount||r.InputCount!=1||!r.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment])||r.OutputKind!=CardKind.Slash||!r.ForPlay||r.ForResponse||!r.UseOnly||
            r.InputKinds.Count!=0||r.InputSuits.Count!=0||r.InputCategories.Count!=0||!r.AllowSameKind||r.AllowChainedInput||r.SameSuit||r.ExtendedUse||r.DistanceUnlimited||r.AllHandCardsOfSelectedSuit||r.SingleCardTrickUse||r.Condition.Kind!=SkillProgramConditionKind.Always||
            r.UsesPerPhase is not null||r.UsesPerActualTurn is not null||r.UsageGroup is not null||r.InheritPreviousPlaySuit||r.DamageBonus!=0||r.RecoveryBonus!=0||r.NonBasicUnrespondableSlash||r.NoDying||r.UnusedOutputNameThisGame||r.UnusedOutputThisTurn||r.ConversionStateId is not null||r.DeclarationValidation is not null||r.CostDestination is not null||r.ActivationUsageGroup is not null||r.RoundGainedRedHandOnly is not null||r.ChainedStateCost is not null||r.LastInSourceZone is not null||r.TieredRoundConversion is not null||r.RoundOnceCardlessBasic is not null||r.DrawFundedDistinctBasic is not null||r.RoundDistinctBasicUse is not null||r.DeclaredEntity||r.UseEffectiveInputSuit is not null||r.DisableSkillUntilTurnEndIfNoDamage is not null)
            throw new InvalidOperationException(path+": Kenshang requires only a variable, at-least-two HE ordinary Slash use, without pure responses or another payment/conversion policy.");
    }
}
internal static class KenshangComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveKenshangTargetRewrite or SkillProgramEffectOp.DrawForKenshangShortDamage;
    [ProgramCatalogValidation(360)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        var rules=p.ViewAs.Where(r=>r.KenshangUse==true).ToArray();
        if(rules.Length==0&&!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        if(rules is not[var rule]||p.Activations is not[var a]||a.MinCards!=2||a.MaxCards!=int.MaxValue||!a.SourceZones.SequenceEqual(rule.SourceZones)||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerTurn is not null||a.UsesPerPhase is not null||a.Effects is not[{Op:SkillProgramEffectOp.UseSelectedCardsAs,OutputKind:CardKind.Slash}e]||e.SourceBind!=rule.Id||p.Triggers.Count!=2)
            throw new InvalidOperationException(path+": Kenshang requires its complete >=2 HE native conversion and both issued-use lifecycle instructions.");
        KenshangConversionSchema.Validate(path,rule);
        foreach(var t in p.Triggers)
            if(t.Effects is not[var effect]||!Handles(effect.Op)||effect.Target!=SkillProgramEffectTarget.Owner||effect.Condition.Kind!=SkillProgramConditionKind.Always||t.Subject is not null||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.ConversionSource||t.SourceSkillId!=p.Id||t.SourceViewAsId!=rule.Id||t.SingleActionInstance!=(effect.Op==SkillProgramEffectOp.DrawForKenshangShortDamage)||t.IncludeResponseUses||t.Optional||t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Window!=(effect.Op==SkillProgramEffectOp.ResolveKenshangTargetRewrite?SkillProgramTriggerWindow.CardUseCommitted:SkillProgramTriggerWindow.CardUseCompleted))
                throw new InvalidOperationException(path+": Kenshang requires exact, mandatory conversion-source actual Use hooks, with the target change optional inside its owning receipt.");
    }
}
