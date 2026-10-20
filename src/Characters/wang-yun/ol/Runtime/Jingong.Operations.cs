using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WangYunTrickReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public int Turn {get;init;}
    public int PhaseInstance {get;init;}
    public int InstructionIndex {get;init;}
    public int CardId {get;init;}
    public CardKind PrintedKind {get;init;}
    public CardLocation From {get;init;}
    public string Stage {get;init;}="";
    private IReadOnlyList<CardKind> _samples=Array.Empty<CardKind>();
    public IReadOnlyList<CardKind> Samples {get=>_samples;init=>_samples=Array.AsReadOnly(value.ToArray());}
    public CardKind? SelectedKind {get;init;}
    public WangYunNativeUseOrigin? NativeUse {get;init;}
    public bool NativeReturned {get;init;}
}
public sealed record WangYunRandomTricksStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseInstance,int CardId,CardKind PrintedKind,CardLocation From,string SamplesFingerprint):IGameEvent;
internal sealed class UseThreeRandomTricksFromSelectedMaterialDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial;
    public override ISkillProgramEffectHandler Handler{get;}=new WangYunHandler(2);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ConsumeSelectedCards(1)];
}
