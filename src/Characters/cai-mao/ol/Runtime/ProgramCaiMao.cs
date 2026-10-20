using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum CaiMaoStage { Targets, Exchange, ExchangeChildren, ToggleChildren }
public sealed record CaiMaoReveal(int HolderSeat,int CardId,CardKind PrintedKind,long HandEntry,string Bind);
public sealed record CaiMaoReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,int FrozenHp,long ParentId,CaiMaoStage Stage)
{
    public long Before {get;init;} public long After {get;init;} public int Cursor {get;init;} public bool Committed {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public bool? ExchangeAccepted {get;init;}
    private IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<CaiMaoReveal> _reveals=Array.AsReadOnly(Array.Empty<CaiMaoReveal>());
    public IReadOnlyList<CaiMaoReveal> Reveals {get=>_reveals;init=>_reveals=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<CaiMaoExchange> _exchanges=Array.AsReadOnly(Array.Empty<CaiMaoExchange>());
    public IReadOnlyList<CaiMaoExchange> Exchanges {get=>_exchanges;init=>_exchanges=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CaiMaoReceipt? CaiMao {get;init;}}
public sealed record CaiMaoStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,int FrozenHp,long ParentId):IGameEvent;
public sealed record CaiMaoTargetChosenEvent(long FrameId,int Index,int TargetSeat):IGameEvent;
public sealed record CaiMaoTargetsCommittedEvent(long FrameId,int Count):IGameEvent;
public sealed record CaiMaoHandRevealedEvent(long FrameId,int Index,CaiMaoReveal Entity):IGameEvent;
public sealed record CaiMaoCompletedEvent(long FrameId,SkillProgramEffectOp Op,int Exchanges,int Toggles):IGameEvent;
internal interface ICaiMaoHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class CaiMaoDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override ISkillProgramEffectHandler Handler=>new CaiMaoHandler(Op);
}
internal sealed class CaiMaoHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ICaiMaoHost>().Resolve(f,op);}
internal static class CaiMaoComposition
{
    [ProgramCatalogValidation(460)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        bool Handles(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveZuolianWholeRevealExchange or SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles;
        if(!p.Activations.Any(a=>a.Effects.Any(Handles))&&!p.Triggers.Any(t=>t.Effects.Any(Handles)))return;
        if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.Contributions.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path+": Cai Mao operations require their exact independent bindings.");
        if(p.Id=="ol:zuolian"&&p.Triggers.Count==0&&p.Activations is[var a]&&a.Id=="whole-exchange"&&a.MinCards==0&&a.MaxCards==0&&a.MinTargets==0&&a.MaxTargets==0&&a.UsesPerTurn is null&&a.UsesPerPhase==1&&a.TargetKind==SkillProgramTargetKind.AnyLiving&&
            a.Effects is[{Op:SkillProgramEffectOp.ResolveZuolianWholeRevealExchange,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])return;
        if(p.Id=="ol:jingzhou"&&p.Activations.Count==0&&p.Triggers is[var t]&&t.Id=="before-own-damage"&&t.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&t.Subject==SkillProgramTriggerSubject.DamageTarget&&t.Optional&&t.DamageOccurrence is null&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&
            t.Effects is[{Op:SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])return;
        throw new InvalidOperationException(path+": Cai Mao requires the original phase-limited whole exchange or optional actual before-damage toggles.");
    }
}
