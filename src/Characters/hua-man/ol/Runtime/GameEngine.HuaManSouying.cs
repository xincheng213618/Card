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
}
