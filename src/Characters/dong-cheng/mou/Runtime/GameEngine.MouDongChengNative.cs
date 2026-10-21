namespace CardGame.Core;
public sealed partial class GameEngine
{
 private CardActionContext PrepareMouDcAction(CardActionContext action)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouDongCheng:{Stage:MouDongChengStage.IssuingUse}r}f)return action;
  if(!MouDongCheng.Valid(f)||r.Actual!=1||action.Type!=CardActionType.Use||action.ActorSeat!=f.OwnerSeat||action.ProviderSeat!=f.OwnerSeat||action.RequesterSeat is not null||action.ResponderSeat is not null||action.EffectiveKind!=r.Kind||action.PhysicalCards.Count!=0||!action.ConversionChain.SequenceEqual([r.Source])||!action.TargetSeats.SequenceEqual(r.Targets))throw new InvalidOperationException("Douwei's declaration lost its paid zero-material source, actor or targets.");
  return CaptureFactionAction(new(action.ActionId,action.ParentActionId,action.Type,action.ActorSeat,action.ProviderSeat,action.RequesterSeat,action.ResponderSeat,action.OpponentSeat,action.EffectiveKind,action.TargetSeats,action.PhysicalCards,action.ConversionChain,action.DesignatedTargetSeats,effectiveSuit:Suit.None,effectiveRank:0,effectiveIsRed:null));
 }
 private IReadOnlyList<int> MouDcTargets(ProgramSkillFrame f,CardKind kind)
 {
  if(f.MouDongCheng is not{}r||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None||!MouDcDamageKind(kind)||IsCardUseForbidden(f.OwnerSeat,kind,CardActionType.Use)||!TargetRules.AllowsNativeUseMaterial(new(f.OwnerSeat,kind,[],r.Source))||IsCuiYanNextUseForbidden(f.OwnerSeat,[],null))return[];
  var actor=_players[f.OwnerSeat];var represented=new Card(0,kind,Suit.None,0);var candidates=_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat&&IsWithinAttackRange(p.Seat,f.OwnerSeat)&&!HasTurnCardTargetRestriction(f.OwnerSeat,SkillProgramCardTargetRestriction.SelfOnly)&&!IsDirectedCardTargetProhibited(actor.Seat,p.Seat,kind)&&!IsCardTargetProhibited(p,kind,Suit.None,null)&&!IsShenwuRobeTargetProhibited(actor.Seat,p.Seat,kind)&&!HasBeneficiarySuitShield(actor.Seat,p.Seat,Suit.None)).ToArray();
  if(IsSlashCard(kind))return Array.AsReadOnly(candidates.Where(t=>CanUseSlashTarget(actor,t,represented,r.Source,kind,ignoreDistance:true,noEffectiveRank:true,physicalCardIds:[])&&CanSpendSlashUse(actor,t,false,kind,physicalCardIds:[])).Select(p=>p.Seat).ToArray());
  if(kind==CardKind.Lightning)return Array.AsReadOnly(candidates.Where(t=>!t.JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(t,kind)&&!HasCardPolicy(t,SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget)).Select(t=>t.Seat).ToArray());
  var options=BuildProgramOrdinaryTrickUseOptions(actor,kind,Suit.None,enforceUsePermission:true,beneficiaryShieldSuit:Suit.None,actualEffectiveColor:null,hasActualColor:true,physicalCardIds:[],conversionSource:r.Source);
  return Array.AsReadOnly(candidates.Where(t=>options.Any(o=>o.TargetSeats.Contains(t.Seat))).Select(t=>t.Seat).ToArray());
 }
 private void IssueMouDcUse(ProgramSkillFrame f)
 {
  if(!MouDongCheng.Valid(f)||f.MouDongCheng is not{Stage:MouDongChengStage.IssuingUse,Native:null,Actual:1}r||r.Targets.Any(t=>!MouDcTargets(f,r.Kind).Contains(t))||ProgramEventHistory<MouDcMovementPaidEvent>().Count(e=>e.FrameId==f.Id&&e.CardId==r.CardId&&e.Actual==1&&e.Before==r.Before&&e.After==r.After)!=1)throw new InvalidOperationException("Douwei's zero-material Use requires the actual discarded cost and still-legal reverse-range target set.");
  var card=new Card(0,r.Kind,Suit.None,0);var id=BeginCardUse(card,f.OwnerSeat,r.Targets,r.Kind,physicalCardIds:[],conversionSource:r.Source);var use=LifecycleCardUse(id)!;
  if(use.MouDongChengDouwei is null)throw new InvalidOperationException("Douwei must bind its original zero-material use before native declaration.");
  if(r.Targets.Count==0){FinishCardUse(id,card,r.Kind);return;}
  if(r.Kind==CardKind.Lightning){BeginOriginalUseLightning(id,r.Source,f.GameplayHash);return;}
  if(IsSlashCard(r.Kind))
  {
   var actor=_players[f.OwnerSeat];var attack=new CardAttackHandle(this,id,f.OwnerSeat,use.TargetSeats[0],card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:r.Kind,ignoresArmor:HasCardArmorBypass(actor,_players[use.TargetSeats[0]],r.Kind),programSkillCardUseFrameId:f.Id);
   ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);if(_phase==TurnPhase.Play&&_currentSeat==actor.Seat&&use.UnlimitedUse!=true)RecordSlashUseDebit(id,actor.Seat);MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,r.Kind);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,r.Kind,actor.Seat,use.TargetSeats[0],IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,_players[use.TargetSeats[0]]);
   if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,use.TargetSeats,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);return;
  }
  var action=r.Kind switch{CardKind.Duel=>LegalActionKind.Duel,CardKind.FireAttack=>LegalActionKind.FireAttack,CardKind.BarbarianAssault=>LegalActionKind.BarbarianAssault,CardKind.ArrowBarrage=>LegalActionKind.ArrowBarrage,CardKind.Lightning=>LegalActionKind.Lightning,CardKind.UnexpectedAssault=>LegalActionKind.UnexpectedAssault,CardKind.WumiaoFlood=>LegalActionKind.WumiaoFlood,_=>BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat],r.Kind,Suit.None,enforceUsePermission:true,physicalCardIds:[],conversionSource:r.Source).First(o=>o.TargetSeats.Contains(r.Targets[0])).ActionKind};
  BeginJizhiOrNullificationWindow(id,card,f.OwnerSeat,use.TargetSeats,action,requiredCardKind:r.Kind==CardKind.BarbarianAssault?CardKind.Slash:r.Kind==CardKind.ArrowBarrage?CardKind.Dodge:null,playedCardKind:r.Kind);
 }
 private void BindMouDcUse(CardUseFrame use)
 {
  var at=_resolutionStack.FindIndex(x=>x.Id==use.Id);if(at<1||_resolutionStack[at-1]is not ProgramSkillFrame{MouDongCheng:{Stage:MouDongChengStage.IssuingUse,Native:null}r}f)return;
  if(!MouDongCheng.Valid(f)||use.CardId!=0||use.CardKind!=r.Kind||use.PhysicalCardIds is not{Count:0}||use.SourceSeat!=f.OwnerSeat||use.Action is not{Type:CardActionType.Use,PhysicalCards.Count:0}a||a.ActorSeat!=f.OwnerSeat||a.ProviderSeat!=f.OwnerSeat||a.RequesterSeat is not null||!a.ConversionChain.SequenceEqual([r.Source])||!use.TargetSeats.SequenceEqual(r.Targets))throw new InvalidOperationException("Douwei cannot change its real actor/provider, original target set, neutral zero-material Use or paid source.");
  var n=new MouDcNativeUse(f.Id,r.Source,r.GameplayHash,r.Turn,r.Kind,use.Id,a.ActionId){Targets=use.TargetSeats};ReplaceRuntimeFrame(f.Id,f with{MouDongCheng=r with{Stage=MouDongChengStage.NativeUse,Native=n}});UpdateLifecycleCardUse(use.Id,u=>u with{MouDongChengDouwei=n});AdvanceEventRulesAndQueueFact(new MouDcUseIssuedEvent(f.Id,use.Id,a.ActionId,f.OwnerSeat,r.Kind));
 }
 private bool MouDcUseRidesOn(ProgramSkillFrame f,CardUseFrame u)=>MouDongCheng.Valid(f)&&f.MouDongCheng is{Native:{}n,Stage:MouDongChengStage.NativeUse or MouDongChengStage.ReturnedUse}r&&u.MouDongChengDouwei is{}own&&MouDongChengRuntime.SameUse(n,own)&&n.FrameId==f.Id&&u.Id==n.UseId&&u.CardId==0&&u.CardKind==n.Kind&&u.PhysicalCardIds is{Count:0}&&u.TargetSeats.SequenceEqual(n.Targets)&&u.Action is{Type:CardActionType.Use,PhysicalCards.Count:0}a&&a.ActionId==n.ActionId&&a.ProviderSeat==f.OwnerSeat&&a.RequesterSeat is null&&ShownEntityUseActorMatches(u,f.OwnerSeat,f.OwnerSeat)&&a.ConversionChain.SequenceEqual([r.Source])&&a.EffectiveSuit==Suit.None&&a.EffectiveRank==0&&a.EffectiveIsRed is null&&ProgramEventHistory<MouDcUseIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.UseId==u.Id&&e.ActionId==a.ActionId&&e.Owner==f.OwnerSeat&&e.Kind==n.Kind)==1;
 private bool IsMouDcZeroUse(long id)=>LifecycleCardUse(id)is{MouDongChengDouwei:{}n}u&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is{}parent&&MouDcUseRidesOn(parent,u);
 private bool MatchesMouDcNativeAction(long id,CardActionContext a,int card)=>card==0&&IsMouDcZeroUse(id)&&LifecycleCardUse(id)?.Action is{}original&&a.ActionId==original.ActionId&&a.Type==CardActionType.Use&&a.ActorSeat==original.ActorSeat&&a.ProviderSeat==original.ProviderSeat&&a.RequesterSeat==original.RequesterSeat&&a.EffectiveKind==original.EffectiveKind&&a.PhysicalCards.Count==0&&a.ConversionChain.SequenceEqual(original.ConversionChain);
 private Card ReadMouDcUseAppearance(long id,CardAppearanceReference appearance)
 {if(!IsMouDcZeroUse(id))return ReadMouWenChouUseAppearance(id,appearance);var kind=LifecycleCardUse(id)!.CardKind;if(appearance.Id!=0||appearance.Kind!=kind||appearance.Suit!=Suit.None||appearance.Rank!=0)throw new InvalidOperationException("Douwei's zero-material native appearance changed.");return new(0,kind,Suit.None,0);}
 private bool IsMouDcZeroAttackConsistent(CardAttackHandle attack,IReadOnlyList<Card> processing)
 {
  if(!IsMouDcZeroUse(attack.ResolutionId)||attack.Card is not null||attack.PhysicalCards.Count!=0||processing.Count!=0||LifecycleCardUse(attack.ResolutionId)is not{}use||attack.EffectiveCardKind!=use.CardKind)return false;
  if(ActiveGroupCard is{}g&&g.ResolutionId==use.Id)return g.DamageSourceSeat==attack.SourceSeat&&g.CurrentAttack?.ResolutionId==use.Id;
  return attack.SourceSeat==use.SourceSeat&&(IsSlashCard(use.CardKind)||use.CardKind is CardKind.Duel or CardKind.FireAttack or CardKind.UnexpectedAssault);
 }
 private bool ContinueMouDcSlash(CardAttackHandle attack)
 {
  if(!IsMouDcZeroUse(attack.ResolutionId)||!IsSlashCard(attack.EffectiveCardKind??CardKind.Dodge)||LifecycleCardUse(attack.ResolutionId)is not{}u)return false;
  var next=u.TargetIndex+1;while(next<u.TargetSeats.Count&&!_players[u.TargetSeats[next]].IsAlive)next++;if(_winner!=Winner.None||next>=u.TargetSeats.Count)return false;
  SetCardUseTargetIndex(u.Id,next);SetCardUseStep(u.Id,ResolutionFrameStep.ResolvingEffect);
  var amount=u.ProgramAdjustedSlashBaseDamage??1;var continued=new CardAttackHandle(this,u.Id,u.SourceSeat,u.TargetSeats[next],card:null,damageAmount:amount,playedCardKind:u.CardKind,ignoresArmor:HasCardArmorBypass(_players[u.SourceSeat],_players[u.TargetSeats[next]],u.CardKind),programSkillCardUseFrameId:u.MouDongChengDouwei!.FrameId);continued.SetCardUseCausedDamage(attack.CardUseCausedDamage);ActiveCardAttack=continued;ActiveDuel=null;ClearPendingDecision();BeginSlashTargetResolution(continued);return true;
 }
 [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion,37400)]private bool FinishMouDcZeroAttack(CardAttackHandle attack,out bool paused)
 {
  paused=false;if(!IsMouDcZeroUse(attack.ResolutionId)||attack.Card is not null||LifecycleCardUse(attack.ResolutionId)is not{}u||ActiveGroupCard?.ResolutionId==u.Id)return false;
  SetCardUseStep(u.Id,ResolutionFrameStep.Completed);AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(u.Id,0,u.CardKind));if(_winner==Winner.None&&TryBeginProgramCardWindow(attack,u.Action!,SkillProgramTriggerWindow.CardUseCompleted,u.TargetSeats,ProgramCardContinuation.CompletedSlash,cardUseCausedDamage:attack.CardUseCausedDamage))paused=true;else{PopFinishedCardUse(u.Id);CompleteFinishedAttackCardUse(attack);}return true;
 }
 [NativeUseValidation(37400)]private void ValidateMouDcUse(CardUseFrame u)
 {if(u.MouDongChengDouwei is{}n&&(u.Step!=ResolutionFrameStep.Completed||_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is not{}parent||!MouDcUseRidesOn(parent,u)))throw new InvalidOperationException("Douwei cannot retire an incomplete or different original zero-material Use.");}
 [NativeUseReturn(37400)]private bool ReturnMouDcUse(CardUseFrame u)
 {if(u.MouDongChengDouwei is not{}n)return false;var parent=GetActiveProgramFrame(n.FrameId);if(!MouDcUseRidesOn(parent,u)||LifecycleCardUse(u.Id)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==u.Id)!=1)throw new InvalidOperationException("Douwei must receive the unique finished original native Use.");MouDongCheng.UseReturned(n,u.CardAttack is{Active:true});return true;}
 [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup,37400)]private bool ResumeMouDcAttack()
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouDongCheng:{Stage:MouDongChengStage.ReturnedUse}}f)return false;AdvanceRuntimeProgram(f.Id);return true;}
}
