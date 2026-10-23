namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasAnniversaryCuiLieCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi);
 [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory,46900)]private bool CuiLieFinalizedWindow(ProgramTargetCardWindowQuery q)=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi);
 private bool CuiLieAdditionalEligible(CardUseFrame u,int seat)
 {
  var a=u.Action!;var target=_players[seat];if(!target.IsAlive||u.TargetSeats.Contains(seat)||_winner!=Winner.None||HasTurnCardTargetRestriction(a.ActorSeat,SkillProgramCardTargetRestriction.SelfOnly)||IsDirectedCardTargetProhibited(a.ActorSeat,seat,u.CardKind,u.Id)||IsCardTargetProhibited(target,u.CardKind,a.EffectiveSuit??Suit.None,ActualTargetPolicyColor(a))||HasBeneficiarySuitShield(a.ActorSeat,seat,a.EffectiveSuit))return false;
  return u.CardKind switch
  {
   CardKind.StarShift or CardKind.HandRedistribution=>CanBeWenheAdditionalTarget(u,target),
   CardKind.Intoxication or CardKind.StoreGrain or CardKind.Reinforcements=>CanBeGuanduAdditionalTarget(u,target),
   CardKind.SameBoat or CardKind.AgainstCurrent or CardKind.Zong=>CanBeDragonBoatAdditionalTarget(u,target),
   CardKind.FireAttack=>GetHand(target).Count>0,
   CardKind.QizhengXiangsheng or CardKind.WumiaoFlood=>true,
   CardKind.UnexpectedAssault=>GetHand(target).Count>0,
   CardKind.Snatch or CardKind.Dismantlement=>HasTargetCard(target),
   CardKind.BorrowedSword=>GetWeapon(target) is not null&&_players.Any(p=>IsLegalBorrowedSwordSlashTarget(target,p)),
   CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning=>!target.JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(target,u.CardKind)&&!HasCardPolicy(target,SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget),
   CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.PeachGarden or CardKind.FiveGrains=>!HasCardPolicy(target,SkillProgramCardPolicyKind.ExcludeGlobalTarget,u.CardKind),
   _=>IsSlashCard(u.CardKind)||u.CardKind is CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo or CardKind.Duel or CardKind.IronChain
  };
 }
 private void SettleCuiLieContest(ProgramSkillFrame f)
 {
  var r=f.AnniversaryCuiLie!;if(r.Result is not{}result||CuiLieUse(f.WindowContext) is not{}use)throw new InvalidOperationException("Chibi lost its whole original native Use after Pindian.");
  if(result.SourceWon){ApplyCuiLieMutation(f,[]);return;}
  AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieDisabledEvent(r.Source,r.Turn,f.Id));
  if(!CuiLieAdditionalEligible(use,f.OwnerSeat)){FinishCuiLie(f);return;}
  if(use.CardKind==CardKind.BorrowedSword){ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=r with{Stage=AnniversaryCuiLieStage.BorrowedVictim}});PublishCuiLie(f);return;}
  ApplyCuiLieMutation(f,[f.OwnerSeat]);
 }
 private void ApplyCuiLieMutation(ProgramSkillFrame f,IReadOnlyList<int> added)
 {
  var r=f.AnniversaryCuiLie!;if(!ValidCuiLie(f)||r.Result is not{}result||CuiLieUse(f.WindowContext) is not{Action:{}prior}use||use.Id!=r.UseId||prior.ActionId!=r.ActionId||ProgramEventHistory<AnniversaryCuiLieUseChangedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Chibi cannot replace another Use or apply its paid result twice.");
  var original=use.TargetSeats;IReadOnlyList<int> targets;int[] materials=[];
  if(result.SourceWon)
  {
   if(added.Count!=0)throw new InvalidOperationException("A won Chibi cancels its entire native Use.");
   targets=[];materials=_players[f.OwnerSeat].IsAlive?prior.PhysicalCards.Select(c=>c.CardId).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing&&!IsClaimedUseCardEntity(use.Id,id)).ToArray():[];
  }
  else
  {
   if(!CuiLieAdditionalEligible(use,f.OwnerSeat)||added.Count!=(use.CardKind==CardKind.BorrowedSword?2:1)||added[0]!=f.OwnerSeat||use.CardKind==CardKind.BorrowedSword&&!IsLegalBorrowedSwordSlashTarget(_players[f.OwnerSeat],_players[added[1]]))throw new InvalidOperationException("Chibi requires a current legal extra recipient and complete BorrowedSword pair.");
   targets=Array.AsReadOnly(original.Concat(added).ToArray());
  }
  var action=CloneDesignatedExtraTargetAction(prior,targets);var simple=use.AdjustedSimpleContinuation;
  if(!result.SourceWon&&_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==r.ParentId) is{Continuation:ProgramCardContinuation.FinalizedSimpleCard,SimpleContinuation:{}current})simple??=current with{RecoveryPolicySources=current.RecoveryPolicySources is{}policies?Array.AsReadOnly(policies.ToArray()):null};
  var changed=use with{Action=action,TargetSeats=targets,TargetsAdjusted=true,AdjustedSimpleContinuation=simple,Enhancements=use.Enhancements|(!result.SourceWon?CurrentCardEnhancement.ExtraTarget:CurrentCardEnhancement.None)};
  if(result.SourceWon)changed=changed with{TargetIndex=0,SlashTargetsCancelled=IsSlashCard(use.CardKind),SequentialTrick=null,PreparedTargetAttacks=use.PreparedTargetAttacks is not null?Array.AsReadOnly(Array.Empty<CardAttackState>()):null,Continuations=use.Continuations with{FangtianHalberd=use.Continuations.FangtianHalberd is{Active:true}multi?multi with{TargetSeats=[]}:use.Continuations.FangtianHalberd}};
  UpdateProgramRoleCardUse(changed,action);
  AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieUseChangedEvent(f.Id,r.Source,r.Hash,use.Id,prior.ActionId,prior.ActorSeat,result.SourceWon){BeforeTargets=original,AfterTargets=targets,Materials=materials,MaterialSequenceBefore=_movementSequence});
  if(!result.SourceWon){RefreshCurrentUseAddedTargetCandidates(changed,[f.OwnerSeat]);FinishCuiLie(f);return;}
  f=CuiLieChildren(f,AnniversaryCuiLieStage.MutationChildren);ReplaceRuntimeTop(f=f with{AnniversaryCuiLie=f.AnniversaryCuiLie! with{Materials=materials,Moves=[]}});r=f.AnniversaryCuiLie!;
  if(materials.Length>0)MoveProgramCardsFromMultipleSources(materials,CardLocation.Hand(f.OwnerSeat),new(CuiLieClaim),(_,moves)=>{var active=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(active with{AnniversaryCuiLie=active.AnniversaryCuiLie! with{Moves=moves,After=_movementSequence}});AdvanceEventRulesAndQueueFact(new AnniversaryCuiLieMaterialClaimedEvent(f.Id,use.Id,prior.ActionId,f.OwnerSeat,moves.Count,r.Before,_movementSequence));});
  ContinueCuiLie(f.Id);
 }
 private bool IsAnniversaryCuiLieTargetMutationFact(CardUseFrame use,AnniversaryCuiLieUseChangedEvent e,IReadOnlyList<int> original,int? currentProofActor=null)
 {
  if(use.Action is not{}a||e.UseId!=use.Id||e.ActionId!=a.ActionId||e.Actor!=(currentProofActor??a.ActorSeat)||e.Source.OwnerSeat==e.Actor||e.Source.SkillId!=AnniversaryCuiLieComposition.Chibi||e.Source.BindingId!="far-target-contest"||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.Hash||!e.BeforeTargets.SequenceEqual(original)||e.MaterialSequenceBefore<0||e.MaterialSequenceBefore>_movementSequence||e.Materials.Distinct().Count()!=e.Materials.Count||e.Materials.Any(id=>!a.PhysicalCards.Any(c=>c.CardId==id)))return false;
  var start=ProgramEventHistory<AnniversaryCuiLieStartedEvent>().Where(s=>s.FrameId==e.FrameId).ToArray();var result=ProgramEventHistory<AnniversaryCuiLiePindianReturnedEvent>().Where(s=>s.FrameId==e.FrameId).ToArray();
  if(start is not[var s]||s.Source!=e.Source||s.Hash!=e.Hash||s.Operation!=SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi||s.Actor!=e.Actor||result is not[var p]||p.Won!=e.Cancelled||ProgramEventHistory<AnniversaryCuiLieUseChangedEvent>().Count(x=>x.FrameId==e.FrameId)!=1||ProgramEventHistory<ProgramBindingStartedEvent>().Count(b=>b.FrameId==e.FrameId&&b.OwnerSeat==e.Source.OwnerSeat&&b.SkillId==e.Source.SkillId&&b.BindingId==e.Source.BindingId&&b.SkillInstanceId==e.Source.SkillInstanceId&&b.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized)!=1||ProgramEventHistory<PindianResultDeterminedEvent>().Count(x=>x.FrameId==p.ContestId&&x.SkillId==e.Source.SkillId&&x.Result.SourceSeat==e.Source.OwnerSeat&&x.Result.OpponentSeat==e.Actor&&x.Result.SourceRank==p.SourceRank&&x.Result.OpponentRank==p.OpponentRank&&x.Result.SourceWon==p.Won)!=1)return false;
  if(e.Cancelled)return e.AfterTargets.Count==0;
  var count=use.CardKind==CardKind.BorrowedSword?2:1;return e.Materials.Count==0&&!original.Contains(e.Source.OwnerSeat)&&e.AfterTargets.Count==original.Count+count&&e.AfterTargets.Take(original.Count).SequenceEqual(original)&&e.AfterTargets[original.Count]==e.Source.OwnerSeat&&e.AfterTargets.All(IsValidPlayerSeat)&&ProgramEventHistory<AnniversaryCuiLieDisabledEvent>().Count(d=>d.FrameId==e.FrameId&&d.Source==e.Source&&d.Turn==s.Turn)==1;
 }
 private bool HasAnniversaryCuiLieCancellation(CardUseFrame use)=>ProgramEventHistory<AnniversaryCuiLieUseChangedEvent>().Any(e=>e.UseId==use.Id&&e.Cancelled&&IsAnniversaryCuiLieTargetMutationFact(use,e,e.BeforeTargets));
 private bool IsAnniversaryCuiLieClaimedUseMaterial(long useId,int cardId)=>LifecycleCardUse(useId) is{}use&&ProgramEventHistory<AnniversaryCuiLieUseChangedEvent>().Any(e=>e.UseId==useId&&e.Cancelled&&e.Materials.Contains(cardId)&&IsAnniversaryCuiLieTargetMutationFact(use,e,e.BeforeTargets)&&ProgramEventHistory<AnniversaryCuiLieMaterialClaimedEvent>().Any(m=>m.FrameId==e.FrameId&&m.UseId==useId&&m.ActionId==e.ActionId&&m.Recipient==e.Source.OwnerSeat&&_cardMovements.Count(move=>move.CardId==cardId&&move.Sequence>m.Before&&move.Sequence<=m.After&&move.From==CardLocation.Processing&&(move.To==CardLocation.Hand(m.Recipient)||IsHappyWeiYanRedirectedGain(move,CardLocation.Hand(m.Recipient)))&&move.Reason.Value==CuiLieClaim)==1));
}
