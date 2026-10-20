using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ChanshuangOption { Recast=1,Slash=2,Discard=3 }
public enum ChanshuangStage { Votes,Ready,RecastCard,DiscardCards,CostChildren,DrawChildren,SlashTarget,SlashChoice,SlashIssuing,NativeUse,NativeChildren }
public sealed record ChanshuangVote(int Seat,ChanshuangOption Option);
public sealed record ChanshuangMaterial(int CardId,CardKind Kind,CardLocation From,bool GeneralWeapon);
public sealed record ChanshuangSelection(int ActorSeat,int ProviderSeat,int TargetSeat,CardKind Kind)
{
 private IReadOnlyList<ChanshuangMaterial> _materials=[];private IReadOnlyList<CardConversionSource> _sources=[];
 public IReadOnlyList<ChanshuangMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<CardConversionSource> Sources{get=>_sources;init=>_sources=Array.AsReadOnly(value.ToArray());}
 public bool Equals(ChanshuangSelection? other)=>other is not null&&ActorSeat==other.ActorSeat&&ProviderSeat==other.ProviderSeat&&TargetSeat==other.TargetSeat&&Kind==other.Kind&&Materials.SequenceEqual(other.Materials)&&Sources.SequenceEqual(other.Sources);
 public override int GetHashCode()=>HashCode.Combine(ActorSeat,ProviderSeat,TargetSeat,Kind,Materials.Count,Sources.Count);
}
public sealed record ChanshuangNativeOrigin(long ProgramFrameId,int Cursor,CardConversionSource Source,string GameplayHash,int ActualTurn,long UseId,long ActionId,ChanshuangSelection Selection,CardActionContext OriginalAction,long BeforePayment=0)
{
 private CardActionContext _action=OriginalAction;
 public CardActionContext OriginalAction{get=>_action;init=>_action=value;}
 public bool Equals(ChanshuangNativeOrigin? other)=>other is not null&&ProgramFrameId==other.ProgramFrameId&&Cursor==other.Cursor&&Source==other.Source&&GameplayHash==other.GameplayHash&&ActualTurn==other.ActualTurn&&UseId==other.UseId&&ActionId==other.ActionId&&Selection==other.Selection&&BeforePayment==other.BeforePayment&&System.Text.Json.JsonSerializer.Serialize(OriginalAction)==System.Text.Json.JsonSerializer.Serialize(other.OriginalAction);
 public override int GetHashCode()=>HashCode.Combine(ProgramFrameId,Cursor,Source,UseId,ActionId,Selection);
}
public sealed record ChanshuangReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int ActualTurn,int ActualTurnOwner,int PeerSeat,int FrozenMask,ChanshuangStage Stage,int Cursor=0,int? SlashTarget=null,int Drawn=0,long Before=0,long After=0,NativeRecastInvocationDraft? Recast=null,NativeLogicalDiscardInvocationDraft? Discard=null,ChanshuangSelection? Selection=null,ChanshuangNativeOrigin? Native=null,bool NativeReturned=false)
{
 private IReadOnlyList<ChanshuangVote> _votes=[];private IReadOnlyList<int> _selected=[],_grain=[];private IReadOnlyList<ChanshuangMaterial> _materials=[];
 public IReadOnlyList<ChanshuangVote> Votes{get=>_votes;init=>_votes=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<ChanshuangMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Grain{get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ChanshuangReceipt? Chanshuang{get;init;}}
public sealed partial record CardUseFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ChanshuangNativeOrigin? ChanshuangSlash{get;init;}}
public sealed record ChanshuangStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Operation,int ActualTurn,int ActualTurnOwner,int PeerSeat,int FrozenMask):IGameEvent;
public sealed record ChanshuangVoteRevealedEvent(long FrameId,int Index,int Seat,ChanshuangOption Option):IGameEvent;
public sealed record ChanshuangMaterialSelectedEvent(long FrameId,int Cursor,int Index,int ActorSeat,int CardId):IGameEvent;
public sealed record ChanshuangGrainFrozenEvent(long FrameId,int Cursor,int Index,int CardId):IGameEvent;
public sealed record ChanshuangMaterialPaidEvent(long FrameId,int Cursor,int Index,int ActorSeat,int CardId,CardKind Kind,CardLocation From,CardLocation To,int Sequence,long BatchId):IGameEvent;
public sealed record ChanshuangCostIssuedEvent(long FrameId,int Cursor,int ActorSeat,ChanshuangOption Option,int Count,int Before,int After):IGameEvent;
public sealed record ChanshuangDrawIssuedEvent(long FrameId,int Cursor,int ActorSeat,int Count,int Before,int After):IGameEvent;
public sealed record ChanshuangSlashSelectedEvent(long FrameId,int Cursor,int ActorSeat,int TargetSeat):IGameEvent;
public sealed record ChanshuangFactionRequestedEvent(long FrameId,int Cursor,int ActorSeat,int TargetSeat,FactionResponsePolicySource Source):IGameEvent;
public sealed record ChanshuangSlashPreparedEvent(long FrameId,int Cursor,int ActorSeat,int ProviderSeat,int TargetSeat,CardKind Kind,string SelectionHash):IGameEvent;
public sealed record ChanshuangSlashIssuedEvent(long FrameId,int Cursor,long UseId,long ActionId,string OriginalActionHash):IGameEvent;
public sealed record ChanshuangSlashMaterialPaidEvent(long UseId,long ActionId,int CardId,CardLocation From,CardLocation To,int Sequence):IGameEvent;
public sealed record ChanshuangSlashReturnedEvent(long FrameId,int Cursor,long UseId,long ActionId):IGameEvent;
public sealed record ChanshuangItemCompletedEvent(long FrameId,int Cursor,int ActorSeat,ChanshuangOption Option,bool Executed,long? UseId):IGameEvent;
public sealed record ChanshuangCompletedEvent(long FrameId,int Count):IGameEvent;
internal interface ILiYiHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class ChanshuangDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>op;public override ISkillProgramEffectHandler Handler{get;}=new ChanshuangHandler(op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class ResolveChanshuangSimultaneousChoicesDescriptor():ChanshuangDescriptor(SkillProgramEffectOp.ResolveChanshuangSimultaneousChoices)
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];}
internal sealed class ResolveChanshuangEndingDescriptor():ChanshuangDescriptor(SkillProgramEffectOp.ResolveChanshuangEnding)
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];}
internal sealed class ChanshuangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ILiYiHost>().Resolve(f,op);}
internal static class LiYiComposition
{
 [ProgramCatalogValidation(660)]
 internal static void ValidateProgram(string path,SkillProgram p)
 {
  bool Own(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveChanshuangSimultaneousChoices or SkillProgramEffectOp.ResolveChanshuangEnding;
  if(p.Activations.Any(a=>a.Effects.Any(Own))||p.Triggers.Any(t=>t.Effects.Any(Own)))
  {
   if(p.Id!="ol:chanshuang"||p.Activations is not[var a]||a.Id!="simultaneous-options"||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerTurn is not null||a.UsesPerPhase!=1||a.Effects is not[{Op:SkillProgramEffectOp.ResolveChanshuangSimultaneousChoices,Target:SkillProgramEffectTarget.Owner}]||p.Triggers is not[var t]||t.Id!="ending-first-items"||t.Window!=SkillProgramTriggerWindow.TurnEnding||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional||t.Priority!=0||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageScope is not null||t.UsageLimit is not null||t.Effects is not[{Op:SkillProgramEffectOp.ResolveChanshuangEnding,Target:SkillProgramEffectTarget.Owner}]||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0)throw new InvalidOperationException(path+": Chanshuang needs its complete actual-Play simultaneous activation and native Ending first-X resolver.");
  }
  foreach(var rule in p.CardPolicies.Where(c=>c.Kind==SkillProgramCardPolicyKind.EmptyWeaponStoneAxe))
   if(p.Id!="ol:zhanjin"||rule.Value!=0||rule.CardKinds.Count!=0||rule.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Zhanjin is pure live empty-weapon metadata; it never supplies a physical card.");
 }
}
