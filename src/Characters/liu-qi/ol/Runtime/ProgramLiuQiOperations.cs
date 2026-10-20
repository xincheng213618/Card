namespace CardGame.Core;
public enum LiuQiStage { ChooseRecipient, ChooseGift, GiftChildren, DrawChildren }
public sealed record LiuQiPlayEntry(int ActualTurn,int OwnerSeat,int PhaseInstance,bool Skipped);
public sealed record LiuQiForeignTargetUse(int ActualTurn,int OwnerSeat,int PhaseInstance,long CardUseFrameId,long? ActionId,int TargetSeat);
public sealed record LiuQiReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,long WindowId,LiuQiStage Stage,int? RecipientSeat=null,int? CardId=null,CardLocation? From=null,CardKind? Name=null,long Before=0,long After=0,int RequestedDraw=0,int ActualDraw=0);
public sealed record LiuQiPlayEnteredEvent(LiuQiPlayEntry Entry):IGameEvent;
public sealed record LiuQiForeignUseRecordedEvent(LiuQiForeignTargetUse Use):IGameEvent;
internal interface ILiuQiProgramHost {SkillProgramStepOutcome BeginLiuQi(ProgramSkillFrame frame,SkillProgramEffect effect);}
internal abstract class LiuQiDescriptor:ProgramOperationDescriptorBase
{
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class DrawForUnskippedUntargetedPlayDescriptor:LiuQiDescriptor
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawForUnskippedUntargetedPlay;
 public override ISkillProgramEffectHandler Handler{get;}=new LiuQiHandler(SkillProgramEffectOp.DrawForUnskippedUntargetedPlay);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class LiuQiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int s,ISkillProgramEffectHost h)=>h.GetCapability<ILiuQiProgramHost>().BeginLiuQi(f,e);
}
internal static class LiuQiComposition
{
 internal static bool IsOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName or SkillProgramEffectOp.DrawForUnskippedUntargetedPlay;
 internal static void Activation(string path,SkillProgramActivation a){if(a.Effects.Any(e=>IsOp(e.Op)))throw new InvalidOperationException(path+": LiuQi requires actual own native phase triggers.");}
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>IsOp(e.Op)))return;
  if(t.Effects is not[var e]||e.Condition.Kind!=SkillProgramConditionKind.Always||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||!t.Optional||t.Window!=(e.Op==SkillProgramEffectOp.ReceiveWenjiGiftAndArmTurnName?SkillProgramTriggerWindow.PlayPhaseStarting:SkillProgramTriggerWindow.TurnEnding))throw new InvalidOperationException(path+": LiuQi requires one unconditional optional own PlayStart/Ending effect.");
 }
}

public sealed partial record ProgramSkillFrame
{
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public LiuQiReceipt? LiuQi {get;init;}
}
public sealed partial class CharacterState
{
 private IReadOnlyList<WenjiTurnNameGrant>? _wenjiTurnNameGrants;
 internal IReadOnlyList<WenjiTurnNameGrant>? WenjiTurnNameGrants {get=>_wenjiTurnNameGrants;set=>_wenjiTurnNameGrants=value is null?null:Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<LiuQiPlayEntry>? _liuQiPlayEntries;
 internal IReadOnlyList<LiuQiPlayEntry>? LiuQiPlayEntries {get=>_liuQiPlayEntries;set=>_liuQiPlayEntries=value is null?null:Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<LiuQiForeignTargetUse>? _liuQiForeignTargetUses;
 internal IReadOnlyList<LiuQiForeignTargetUse>? LiuQiForeignTargetUses {get=>_liuQiForeignTargetUses;set=>_liuQiForeignTargetUses=value is null?null:Array.AsReadOnly(value.ToArray());}
}
public sealed partial record NullificationWindowFrame
{
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public WenjiCounterspellQualification? WenjiCounterspell {get;init;}
}
