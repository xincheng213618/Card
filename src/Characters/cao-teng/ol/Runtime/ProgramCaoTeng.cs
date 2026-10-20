using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum CaoTengStage { FactionChoice, FactionApplying, YongzuTarget, YongzuChoice, YongzuChildren, JianxiongDrawChildren, JianxiongClaimChildren }
public sealed record CaoTengReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,long ParentId,CaoTengStage Stage)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? TargetSeat{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? Faction{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? FirstOption{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? Option{get;init;}
    public bool SameFaction{get;init;}public int Cursor{get;init;}public int RecoveryRequested{get;init;}public long Before{get;init;}public long After{get;init;}
    private IReadOnlyList<CaoTengDamageMaterial> _materials=Array.AsReadOnly(Array.Empty<CaoTengDamageMaterial>());
    public IReadOnlyList<CaoTengDamageMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CaoTengReceipt? CaoTeng{get;init;}}
public sealed record CaoTengStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,long ParentId):IGameEvent;
public sealed record CaoTengRecipientChosenEvent(long FrameId,int TargetSeat,bool SameFaction):IGameEvent;
public sealed record CaoTengBenefitIssuedEvent(long FrameId,int Index,int RecipientSeat,string Option,int RecoveryRequested,long Before):IGameEvent;
public sealed record CaoTengBenefitAppliedEvent(long FrameId,int Index,long After):IGameEvent;
public sealed record CaoTengCompletedEvent(long FrameId):IGameEvent;
internal interface ICaoTengHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op);}
internal abstract class CaoTengDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new CaoTengHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class CaoTengHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ICaoTengHost>().Resolve(f,op);}
internal static class CaoTengComposition
{
    [ProgramCatalogValidation(480)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        bool Handles(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveQingliuFactionChoice or SkillProgramEffectOp.ResolveYongzuDistinctBenefits or SkillProgramEffectOp.ResolveCaoTengJianxiongDrawAndClaim;
        if(!p.Triggers.Any(t=>t.Effects.Any(Handles))&&!p.Activations.Any(a=>a.Effects.Any(Handles)))return;
        if(p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.Contributions.Count!=0||p.DamageModifiers.Count!=0||p.LordSkillProjection)
            throw new InvalidOperationException(path+": Cao Teng operations require their exact independent native bindings.");
        var q=p.Id=="ol:qingliu";var y=p.Id=="ol:yongzu";var j=p.Id=="ol:caoteng-jianxiong";
        if(!q&&!y&&!j||p.Triggers.Count!=(q?2:1))throw new InvalidOperationException(path+": Unexpected Cao Teng skill or binding count.");
        foreach(var t in p.Triggers)
        {
            if(t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.MarkerCost is not null||t.UsageLimit is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.CardKinds.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||
                t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]||
                q&&(e.Op!=SkillProgramEffectOp.ResolveQingliuFactionChoice||t.Optional||t.DamageOccurrence is not null||!(t.Id=="choose-opening-faction"&&t.Window==SkillProgramTriggerWindow.GameStarting||t.Id=="first-rescue-faction"&&t.Window==SkillProgramTriggerWindow.DyingExited))||
                y&&(e.Op!=SkillProgramEffectOp.ResolveYongzuDistinctBenefits||!t.Optional||t.Id!="distinct-benefits"||t.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow||t.DamageOccurrence is not null)||
                j&&(e.Op!=SkillProgramEffectOp.ResolveCaoTengJianxiongDrawAndClaim||!t.Optional||t.Id!="draw-and-claim"||t.Window!=SkillProgramTriggerWindow.AfterDamageApplied||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage))
                throw new InvalidOperationException(path+": Cao Teng changed the exact mandatory setup/rescue, optional preparation or AND damage benefit.");
        }
    }
}
