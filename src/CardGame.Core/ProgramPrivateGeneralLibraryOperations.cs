using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record PrivateGeneralLibrarySource(int OwnerSeat,string CapabilitySkillId,string CapabilityGrantId,string CapabilitySkillInstanceId);
public sealed record GeneralLibraryProjectionSource(PrivateGeneralLibrarySource LibrarySource,string GeneralId,string SkillId);
public sealed record PrivateGeneralLibrary(PrivateGeneralLibrarySource Source,bool Initialized,IReadOnlyList<string> GeneralIds,
    string? RevealedGeneralId=null,string? DeclaredSkillId=null,string? SelectedFactionId=null,long Revision=0);
public enum GeneralLibraryStep { SelectGeneral, SelectSkill, SelectFaction }
public sealed record ProgramGeneralLibraryDraft(GeneralLibraryStep Step,PrivateGeneralLibrarySource Source,bool InitialDrawCommitted,
    string? SelectedGeneralId=null,string? SelectedSkillId=null,string? SelectedFactionId=null);
public sealed record PrivateGeneralLibraryPolicy(IReadOnlyList<SkillTag> ExcludedSkillTags);
public sealed record PrivateGeneralLibrarySnapshot(PrivateGeneralLibrarySource Source,int Count,string? RevealedGeneralId,
    string? DeclaredSkillId,GeneralGender? Gender,string? FactionId,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? GeneralIds=null);
public sealed record PrivateGeneralLibraryCountChangedEvent(PrivateGeneralLibrarySource Source,int Count):IGameEvent;
public sealed record PrivateGeneralLibraryDeclaredEvent(PrivateGeneralLibrarySource Source,string GeneralId,string SkillId,GeneralGender Gender,string? FactionId):IGameEvent;
public sealed record PrivateGeneralLibraryRemovedEvent(PrivateGeneralLibrarySource Source):IGameEvent;
internal interface IPrivateGeneralLibraryProgramHost
{SkillProgramStepOutcome ExecutePrivateGeneralLibrary(SkillProgramEffect effect,ProgramSkillFrame frame);}
internal abstract class PrivateGeneralLibraryDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","amount","skillIds","excludedSkillTags","condition");
        var amount=Op==SkillProgramEffectOp.ChoosePrivateGeneralAvatar?0:r.RequiredInt("amount");
        if(Op==SkillProgramEffectOp.ChoosePrivateGeneralAvatar&&r.Has("amount")||amount<0||amount>32||Op!=SkillProgramEffectOp.ChoosePrivateGeneralAvatar&&amount<1)throw new InvalidOperationException("Invalid private general library amount.");
        var refs=r.Has("skillIds")?r.RequiredIdentifierArray("skillIds"):[];
        if(refs.Count!=(Op==SkillProgramEffectOp.AcquirePrivateGeneralAvatar?1:0))throw new InvalidOperationException("A private avatar acquisition requires one exact source skill reference.");
        var tags=r.RequiredEnumArray<SkillTag>("excludedSkillTags");
        if(tags.Distinct().Count()!=tags.Count||tags.Any(t=>t is not(SkillTag.Limited or SkillTag.Awakening or SkillTag.Lord)))throw new InvalidOperationException("Invalid general library exclusion tags.");
        var effect=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),amount,r.Condition(),skillIds:refs){GeneralLibraryPolicy=new(Array.AsReadOnly(tags.ToArray()))};RequireAlways(effect,r.Path);return effect;
    }
}
internal sealed class InitializePrivateGeneralLibraryDescriptor:PrivateGeneralLibraryDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializePrivateGeneralLibrary;
    public override ISkillProgramEffectHandler Handler{get;}=new InitializePrivateGeneralLibraryHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class AcquirePrivateGeneralAvatarDescriptor:PrivateGeneralLibraryDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AcquirePrivateGeneralAvatar;
    public override ISkillProgramEffectHandler Handler{get;}=new AcquirePrivateGeneralAvatarHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ChoosePrivateGeneralAvatarDescriptor:PrivateGeneralLibraryDescriptor
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChoosePrivateGeneralAvatar;
    public override ISkillProgramEffectHandler Handler{get;}=new ChoosePrivateGeneralAvatarHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindows(Array.AsReadOnly(new[]{SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.AfterTurnEnded}))];
}
public abstract class PrivateGeneralLibraryHandler:ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op{get;}
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>((IPrivateGeneralLibraryProgramHost)host).ExecutePrivateGeneralLibrary(e,f);
}
public sealed class InitializePrivateGeneralLibraryHandler:PrivateGeneralLibraryHandler{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.InitializePrivateGeneralLibrary;}
public sealed class AcquirePrivateGeneralAvatarHandler:PrivateGeneralLibraryHandler{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AcquirePrivateGeneralAvatar;}
public sealed class ChoosePrivateGeneralAvatarHandler:PrivateGeneralLibraryHandler{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ChoosePrivateGeneralAvatar;}
