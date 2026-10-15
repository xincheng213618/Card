namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void PaySouyingMutation(ProgramSkillFrame f)
 {
  var r=f.HuaMan!;if(r.Stage!=HuaManStage.DiscardChildren||!r.Paid||r.Souying is not{}q||r.DiscardedId is not{}discarded||LifecycleCardUse(q.NativeUseId)is not{Action:{}action}use||use.Action.ActionId!=q.ActionId||use.HuaManSouyingQualification!=q||use.HuaManSouyingMutation is not null)throw new InvalidOperationException("Paid Souying lost its native use, unique direction or actual discarded entity.");
  var cancel=f.OwnerSeat==q.TargetSeat;var ids=!cancel&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None?action.PhysicalCards.Select(c=>c.CardId).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing&&!IsClaimedUseCardEntity(use.Id,id)).ToArray():[];
  var before=_movementSequence;var mutation=new SouyingNativeMutation(f.Id,r.Source,r.GameplayHash,q,r.Before,r.After,discarded,before,before,cancel){ClaimedEntities=ids,BeforeTargets=use.TargetSeats};
  if(cancel)
  {
   var targets=Array.AsReadOnly(Array.Empty<int>());var updated=CloneDesignatedExtraTargetAction(action,targets);
   UpdateProgramRoleCardUse(use with{HuaManSouyingMutation=mutation,TargetSeats=targets,TargetIndex=0,Action=updated,TargetsAdjusted=true,SlashTargetsCancelled=IsSlashCard(use.CardKind),SequentialTrick=null,PreparedTargetAttacks=use.PreparedTargetAttacks is null?null:Array.AsReadOnly(Array.Empty<CardAttackState>()),Continuations=use.Continuations with{FangtianHalberd=use.Continuations.FangtianHalberd is{Active:true}multi?multi with{TargetSeats=targets}:use.Continuations.FangtianHalberd}},updated);
  }
  else ReplaceRuntimeFrame(use.Id,use with{HuaManSouyingMutation=mutation});
  ReplaceRuntimeTop(f=f with{HuaMan=r with{Stage=HuaManStage.ClaimChildren,Before=before,After=before,ActualCount=0},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(ids.Length>0)MoveCards(ids.Select(GetAdvancedCard).ToArray(),CardLocation.Processing,CardLocation.Hand(f.OwnerSeat),new(HuaManClaimReason));
  use=LifecycleCardUse(q.NativeUseId)!;mutation=mutation with{ClaimAfter=_movementSequence};ReplaceRuntimeFrame(use.Id,use with{HuaManSouyingMutation=mutation});f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{HuaMan=f.HuaMan!with{After=_movementSequence,ActualCount=ids.Length}});AdvanceEventRulesAndQueueFact(new SouyingNativeMutationEvent(f.Id,use.Id,q.ActionId,r.Source,cancel,ids.Length,r.Before,r.After,before,_movementSequence));RecordHuaManMovement(f,f.OwnerSeat);AdvanceRuntimeProgram(f.Id);
 }
 private bool TryGetSouyingCancelledVirtualPrimaryReturn(CardUseFrame use,ProgramSkillFrame parent,out IReadOnlyList<int> primaryTarget)
 {
  primaryTarget=[];
  if(!HasHuaManSouyingCancellation(use)||use.HuaManSouyingMutation is not{BeforeTargets:[var original],Qualification:{}q}r||original!=q.TargetSeat||use is not{CardId:0,PhysicalCardIds.Count:0,ProgramUseCommitted:true,ProgramUseAccepted:true,TargetSeats.Count:0}||!IsSlashCard(use.CardKind)||use.CardAttack is not{CardId:null,PhysicalCardIds.Count:0}attack||attack.ProgramSkillCardUseFrameId!=parent.Id||attack.SourceSeat!=use.SourceSeat||use.Action is not{Type:CardActionType.Use,PhysicalCards.Count:0}action||!IsSlashCard(action.EffectiveKind)||action.ActionId!=q.ActionId||action.ActorSeat!=q.ActorSeat||action.ProviderSeat!=q.ProviderSeat||action.ActorSeat!=use.SourceSeat||parent.OwnerSeat!=action.ActorSeat||parent.SelectedTargetSeats is not[var selected]||selected!=original||parent.InstructionIndex<1)return false;
  var index=_resolutionStack.FindIndex(f=>f.Id==use.Id);if(index<1||_resolutionStack[index-1]is not ProgramSkillFrame actualParent||actualParent.Id!=parent.Id||actualParent!=parent||_contentRegistry.GetSkill(parent.SkillId).Program is not{}program||program.GameplayHash!=parent.GameplayHash)return false;
  var plan=ProgramInstructionResolver.Default.Resolve(parent,program);if(parent.InstructionIndex>plan.Instructions.Count)return false;var paused=plan.GetPausedInstruction(parent.InstructionIndex).Effect;var source=new CardConversionSource(parent.SkillId,GetProgramBindingId(parent),parent.OwnerSeat,parent.SkillInstanceId);
  if(paused.Op==SkillProgramEffectOp.UseVirtualCard){if(paused.OutputKind!=CardKind.Slash||!paused.UseCardActionWindows||paused.TargetRestriction is not(SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget or SkillProgramCardTargetRestriction.NormalSlashTarget)||!action.ConversionChain.SequenceEqual([source]))return false;}
  else if(paused.Op==SkillProgramEffectOp.UseVirtualSlash){if(!action.ConversionChain.SequenceEqual([source]))return false;}
  else return false;
  var history=CompleteProgramEventHistory().ToArray();var declared=history.OfType<CardUseDeclaredEvent>().Where(e=>e.ResolutionId==use.Id).ToArray();var targets=history.OfType<TargetsConfirmedEvent>().Where(e=>e.ResolutionId==use.Id).ToArray();var accepted=history.OfType<CardActionAcceptedEvent>().Where(e=>e.Action.ActionId==action.ActionId).ToArray();
  if(declared is not[{CardId:0}d]||d.SourceSeat!=action.ActorSeat||d.CardKind!=use.CardKind||targets is not[{TargetSeats:[var confirmed]}]||confirmed!=original||accepted is not[var a]||a.Action.Type!=CardActionType.Use||a.Action.ActorSeat!=action.ActorSeat||a.Action.ProviderSeat!=action.ProviderSeat||a.Action.EffectiveKind!=action.EffectiveKind||a.Action.PhysicalCards.Count!=0||!a.Action.TargetSeats.SequenceEqual([original])||!a.Action.ConversionChain.SequenceEqual(action.ConversionChain)||a.Action.ParentActionId!=action.ParentActionId)return false;
  var mutations=history.OfType<SouyingNativeMutationEvent>().Where(e=>e.NativeUseId==use.Id).ToArray();if(mutations is not[var mutation]||!IsHuaManSouyingMutationFact(use,mutation,r.BeforeTargets)||Array.IndexOf(history,d)>=Array.IndexOf(history,a)||Array.IndexOf(history,a)>=Array.IndexOf(history,mutation))return false;
  primaryTarget=Array.AsReadOnly(new[]{original});return true;
 }

}
