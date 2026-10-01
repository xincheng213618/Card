namespace CardGame.Core;
internal interface IHandComparisonProgramHost
{
 SkillProgramStepOutcome CompareSelectedHandWithHpHand(ProgramSkillFrame frame, SkillProgramEffect effect);
 void AdjustPersistentHandLimit(ProgramSkillFrame frame,int amount,string stateId);
 void ProhibitSelfCardTargetsForTurn(ProgramSkillFrame frame);
}
public sealed record ProgramHandComparisonDraft(int TargetSeat,int SourceCardId,IReadOnlyList<int> CandidateIds,int Required,IReadOnlyList<int> RevealedIds,string Stage);
public sealed record ProgramHandComparisonResolvedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,string StateId,int TurnNumber,int SourceCardId,int TargetSeat,IReadOnlyList<int> RevealedIds,bool ColorMatched,bool RankMatched) : IGameEvent;
public sealed record ProgramPersistentHandLimitChangedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,string StateId,int Amount) : IGameEvent;
public sealed record ProgramSelfCardTargetsProhibitedEvent(int OwnerSeat,string SkillId,string SkillInstanceId,int TurnNumber,int TurnSeat) : IGameEvent;
internal sealed record RequireActivationHandComparison : ProgramResourceOperation;
internal sealed class CompareSelectedHandWithHpHandDescriptor : ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.CompareSelectedHandWithHpHand;
 public override ISkillProgramEffectHandler Handler {get;}=new CompareSelectedHandWithHpHandHandler();
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.Reveal,static(e,c)=>c.Draw(e));
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
 {
  r.AllowOnly("op","target","stateId","condition");
  var e=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),1,r.Condition(),stateId:r.RequiredIdentifier("stateId"));RequireAlways(e,r.Path);return e;
 }
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireActivationHandComparison(),new ReadSelectedTarget()];
}
internal sealed class AdjustPersistentHandLimitDescriptor : ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AdjustPersistentHandLimit;
 public override ISkillProgramEffectHandler Handler {get;}=new AdjustPersistentHandLimitHandler();
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GrantTurnRuleModifier,static(e,c)=>c.GrantTurnRuleModifier(e));
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
 {
  r.AllowOnly("op","target","amount","stateId","condition");var amount=r.RequiredInt("amount");if(amount is not (-1 or 1))throw new InvalidOperationException("Persistent hand-limit adjustment must be -1 or +1.");
  return new(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),amount,r.Condition(),stateId:r.RequiredIdentifier("stateId"));
 }
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class ProhibitSelfCardTargetsForTurnDescriptor : ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ProhibitSelfCardTargetsForTurn;
 public override ISkillProgramEffectHandler Handler {get;}=new ProhibitSelfCardTargetsForTurnHandler();
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,static(e,c)=>c.GrantTurnCardTargetRestriction(e));
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r){r.AllowOnly("op","target","condition");return new(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition());}
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
public sealed class CompareSelectedHandWithHpHandHandler : ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.CompareSelectedHandWithHpHand;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>((IHandComparisonProgramHost)host).CompareSelectedHandWithHpHand(f,e);}
public sealed class AdjustPersistentHandLimitHandler : ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.AdjustPersistentHandLimit;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host){((IHandComparisonProgramHost)host).AdjustPersistentHandLimit(f,e.Amount,e.StateId!);return SkillProgramStepOutcome.Continue;}}
public sealed class ProhibitSelfCardTargetsForTurnHandler : ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>SkillProgramEffectOp.ProhibitSelfCardTargetsForTurn;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host){((IHandComparisonProgramHost)host).ProhibitSelfCardTargetsForTurn(f);return SkillProgramStepOutcome.Continue;}}
