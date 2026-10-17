namespace CardGame.Core;
public sealed partial class GameEngine
{
 private CardActionContext PrepareZonghuAction(CardActionContext a,ZonghuPaidOrigin? p)=>p is null?a:CaptureFactionAction(new(a.ActionId,a.ParentActionId,a.Type,p.Need.ActorSeat,p.Need.ProviderSeat,p.Need.RequesterSeat,null,a.OpponentSeat,a.EffectiveKind,a.TargetSeats,[],[p.Source],a.DesignatedTargetSeats,Suit.None,0,null));
 private void IssueZonghuUse(CardUseFrame use,ZonghuPaidOrigin? p)
 {
  if(p is null)return;
  if(!ValidZonghuPayment(p)||ProgramEventHistory<ZonghuAttemptResolvedEvent>().Count(e=>e.FrameId==p.AttemptId&&e.Matched&&e.NativeLegal)!=1||use.CardId!=0||use.PhysicalCardIds is not{Count:0}||use.CardKind!=p.OutputKind||use.Action is not{Type:CardActionType.Use,PhysicalCards.Count:0}a||a.ActorSeat!=p.Need.ActorSeat||a.ProviderSeat!=p.Need.ProviderSeat||a.RequesterSeat!=p.Need.RequesterSeat||a.ConversionChain is not[var source]||source!=p.Source||!use.TargetSeats.SequenceEqual([p.TargetSeat])&&!(p.OutputKind==CardKind.Alcohol&&use.TargetSeats.Count==0)||ProgramEventHistory<ZonghuUseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId)||ProgramEventHistory<ZonghuResponseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId))throw new InvalidOperationException("Zonghu cannot issue a second zero-entity basic or change its real payer, actor or original native target.");
  UpdateLifecycleCardUse(use.Id,u=>u with{ZonghuOrigin=p});AdvanceEventRulesAndQueueFact(new ZonghuUseIssuedEvent(p.AttemptId,use.Id,a.ActionId,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,p.OutputKind));
  var i=_resolutionStack.FindIndex(f=>f.Id==use.Id);if(i>0&&_resolutionStack[i-1]is ProgramSkillFrame{ZonghuReturn:{}r}parent&&r.Origin==p&&r.UseId==0)ReplaceRuntimeFrame(parent.Id,parent with{ZonghuReturn=r with{UseId=use.Id,ActionId=a.ActionId}});
 }
 private bool IsZonghuUse(long id)=>LifecycleCardUse(id)is{}u&&IsIssuedZonghuUse(u);
 private bool IsIssuedZonghuUse(CardUseFrame use)
 {
  if(use.ZonghuOrigin is not{}p||!ValidZonghuPayment(p)||use.CardId!=0||use.PhysicalCardIds is not{Count:0}||use.Action is not{Type:CardActionType.Use,PhysicalCards.Count:0}current)return false;
  var a=current;if(use.CurrentSlashFirePolicy is{}fire){if(!IsSlashCard(p.OutputKind))return false;AssertCurrentSlashFirePolicy(use);a=fire.OriginalAction;}
  return (use.CardKind==p.OutputKind||use.CurrentSlashFirePolicy is not null&&IsSlashCard(p.OutputKind))&&a.EffectiveKind==p.OutputKind&&a.ConversionChain is[var source]&&source==p.Source&&a.ProviderSeat==p.Need.ProviderSeat&&a.RequesterSeat==p.Need.RequesterSeat&&a.ResponderSeat is null&&a.EffectiveSuit==Suit.None&&a.EffectiveRank==0&&a.EffectiveIsRed is null&&ZonghuIssuedActorMatches(use,p)&&ProgramEventHistory<ZonghuAttemptResolvedEvent>().Count(e=>e.FrameId==p.AttemptId&&e.Matched&&e.NativeLegal)==1&&ProgramEventHistory<ZonghuUseIssuedEvent>().Where(e=>e.AttemptId==p.AttemptId).ToArray()is[var issued]&&issued.UseId==use.Id&&issued.ActionId==a.ActionId&&issued.ActorSeat==p.Need.ActorSeat&&issued.ProviderSeat==p.Need.ProviderSeat&&issued.RequesterSeat==p.Need.RequesterSeat&&issued.OutputKind==p.OutputKind;
 }
 private bool ZonghuIssuedActorMatches(CardUseFrame use,ZonghuPaidOrigin p)=>ProgramEventHistory<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==use.Id)?ShownEntityUseActorMatches(use,p.Need.ActorSeat,p.Need.ProviderSeat):_resolutionStack.LastOrDefault()?.Id==use.Id&&use.SourceSeat==p.Need.ActorSeat&&use.Action?.ActorSeat==p.Need.ActorSeat&&!ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Any(e=>e.CardUseFrameId==use.Id);
 private Card ZonghuRepresentation(long id)=>LifecycleCardUse(id)is{}u&&IsIssuedZonghuUse(u)?new(0,u.CardKind,Suit.None,0):throw new InvalidOperationException("Zonghu representation requires its exact once-paid native zero use.");
 private void DispatchZonghu(ZonghuPaidOrigin p)
 {
  if(!ValidZonghuPayment(p)||!_players[p.Need.ActorSeat].IsAlive||!_players[p.Need.ProviderSeat].IsAlive)throw new InvalidOperationException("Zonghu cannot issue for a dead actor/provider.");
  ClearPendingDecision();var n=p.Need;
  if(n.Intent is ZonghuIntent.Dodge or ZonghuIntent.Duel or ZonghuIntent.Group or ZonghuIntent.FactionDodge or ZonghuIntent.FactionSlashPlayed){DispatchZonghuResponse(p);return;}
  if(n.Intent==ZonghuIntent.Dying){DispatchZonghuRecovery(p,true);return;}
  if(n.Intent==ZonghuIntent.Qinglong){var old=ActiveQinglongCrescentBlade!.Attack;ActiveQinglongCrescentBlade=null;SetCardUseStep(old.ResolutionId,ResolutionFrameStep.ResolvingEffect);AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId,n.ActorSeat,p.TargetSeat,true,[],p.OutputKind));SuspendContinuationForQinglongFollowup(old.ResolutionId);BeginZonghuSlash(p,null,false,null);return;}
  if(n.Intent==ZonghuIntent.BorrowedSword){var b=ActiveBorrowedSword!;PopResponseWindow(b.ResolutionId);SetCardUseStep(b.ResolutionId,ResolutionFrameStep.ResolvingEffect);b.AwaitingSlashChoice=false;b.SlashCardId=0;b.EffectiveSlashKind=p.OutputKind;BeginZonghuSlash(p,b,true,null);return;}
  if(n.Intent==ZonghuIntent.Play){BeginZonghuSlash(p,null,true,null);return;}
  var parent=GetActiveProgramFrame(n.OwnerFrameId);if(parent.InstructionIndex!=n.InstructionIndex||GetPausedPrivateOfferEffect(parent)?.Op!=n.RequestOp)throw new InvalidOperationException("Zonghu requested use moved its original program cursor.");
  ReplaceRuntimeTop(parent=parent with{ZonghuReturn=new(p,0,0),Step=ResolutionFrameStep.ResolvingEffect});
  if(n.Intent==ZonghuIntent.GenericBasic)
  {var k=parent.KeBiNengPingduan!;var action=new LegalAction(IsSlashCard(p.OutputKind)?LegalActionKind.Slash:p.OutputKind==CardKind.Peach?LegalActionKind.Peach:LegalActionKind.Alcohol,0,p.OutputKind==CardKind.Alcohol?null:p.TargetSeat,"宗护：真实零实体基本牌",p.OutputKind,TargetSeats:p.OutputKind==CardKind.Alcohol?[]:[p.TargetSeat]){ConversionSource=p.Source};AdvanceEventRulesAndQueueFact(new ZonghuKeActionAcceptedEvent(parent.Id,parent.InstructionIndex,k.Source,parent.GameplayHash,p.AttemptId,p.OutputKind,p.TargetSeat));ReplaceRuntimeTop(parent=parent with{ZonghuKeAcceptedOrigin=p,KeBiNengPingduan=k with{BasicActions=k.BasicActions.Append(action).ToArray(),SelectedBasicAction=action,Stage=KeBiNengPingduanStage.BasicIssuing}});if(IsSlashCard(p.OutputKind))BeginZonghuSlash(p,null,true,parent.Id);else if(p.OutputKind==CardKind.Peach)DispatchZonghuRecovery(p,false);else{var id=BeginCardUse(new(0,p.OutputKind,Suit.None,0),n.ActorSeat,[],p.OutputKind,physicalCardIds:[],conversionSource:p.Source,zonghuOrigin:p);BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Alcohol));}return;}
  PrepareZonghuProgram(parent,p);BeginZonghuSlash(p,null,true,parent.Id);
 }
 private void PrepareZonghuProgram(ProgramSkillFrame parent,ZonghuPaidOrigin p)
 {
  var e=GetPausedPrivateOfferEffect(parent)!;var n=p.Need;
  if(IsSayingSlashRequest(parent)){PrepareSayingSuppliedSlash(parent,n.ActorSeat,n.ProviderSeat,p.TargetSeat,p.OutputKind,[],p.Source);return;}
  if(IsChanshuangSlashRequest(parent)){PrepareChanshuangSuppliedSlash(parent,n.ActorSeat,n.ProviderSeat,p.TargetSeat,p.OutputKind,[],p.Source);return;}
  if(IsQigongRequest(parent)){ReplaceRuntimeTop(parent with{LvKuangLvXiang=parent.LvKuangLvXiang! with{Stage="issuing",IssuingIds=[],IssuingKind=p.OutputKind,IssuingProvider=p.Need.ProviderSeat}});return;}
  if(IsWuJingHejiRequest(parent)){ReplaceRuntimeTop(parent with{WuJing=parent.WuJing! with{Stage=WuJingStage.IssuingUse,Selection=new(p.OutputKind,false,false){Conversions=[p.Source]},Before=_movementSequence,After=_movementSequence}});return;}
  if(IsCaoChunInitialRequest(parent)){var r=parent.CaoChun!;ReplaceRuntimeTop(parent with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=new(parent.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,false,p.TargetSeat,p.OutputKind){ProviderSeat=n.ProviderSeat,Materials=[],PaymentBefore=_movementSequence}}});return;}
  if(n.Intent==ZonghuIntent.ProgramSlash)CommitProgramChoiceResult(parent.Id,e.ResultBind!,RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption,n.ActorSeat,"宗护：视为使用杀。");
  else if(n.Intent==ZonghuIntent.AssistedSlash){ReplaceRuntimeTop(parent with{AssistedSlashRequest=null});CommitProgramChoiceResult(parent.Id,e.ResultBind!,"used-slash",n.ActorSeat,"宗护：视为使用杀。");}
  else if(n.Intent==ZonghuIntent.FixedSlash)ReplaceRuntimeTop(parent with{FixedTargetSlash=parent.FixedTargetSlash! with{Issued=true}});
  else if(n.Intent==ZonghuIntent.NearestLegalSlash)ReplaceRuntimeTop(parent with{NearestLegalSlashRequest=null});
 }
 private void BeginZonghuSlash(ZonghuPaidOrigin p,BorrowedSwordHandle? borrowed,bool counts,long? parent,FactionCardRequestHandle? faction=null)
 {
  var actor=_players[p.Need.ActorSeat];var target=_players[p.TargetSeat];var kind=p.OutputKind;var id=BeginCardUse(new(0,kind,Suit.None,0),actor.Seat,[target.Seat],kind,ignoresArmor:HasCardArmorBypass(actor,target,kind),physicalCardIds:[],conversionSource:p.Source,zonghuOrigin:p);
  var actual=LifecycleCardUse(id)!;var attack=new CardAttackHandle(this,id,actor.Seat,actual.TargetSeats[0],card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:kind,ignoresArmor:HasCardArmorBypass(actor,_players[actual.TargetSeats[0]],kind),programSkillCardUseFrameId:parent);
  if(borrowed is not null)borrowed.ActiveAttack=attack;if(faction is not null)faction.ActiveAttack=attack;ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;
  if(counts&&_phase==TurnPhase.Play&&actor.Seat==_currentSeat&&actual.UnlimitedUse!=true&&!IgnoresProgramSlashLimit(actor,p.Source))RecordSlashUseDebit(id,actor.Seat);
  MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,kind);CompleteQigongNativePayment(id);CompleteCaoChunNativePayment(id);AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,kind,actor.Seat,actual.TargetSeats[0],IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,_players[actual.TargetSeats[0]]);
  if(TryBeginProvidedCardSupplyCompletion(attack,faction))return;
  if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,LifecycleCardUse(id)!.TargetSeats,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
 }
 private void DispatchZonghuRecovery(ZonghuPaidOrigin p,bool dying)
 {
  var n=p.Need;var source=_players[n.ActorSeat];var target=_players[p.TargetSeat];var card=new Card(0,p.OutputKind,Suit.None,0);var id=BeginCardUse(card,source.Seat,[target.Seat],p.OutputKind,physicalCardIds:[],conversionSource:p.Source,zonghuOrigin:p);
  if(dying)UpdateLifecycleCardUse(id,u=>u with{DyingResponse=new(n.OwnerFrameId,source.Seat,p.OutputKind==CardKind.Peach,p.OutputKind==CardKind.Peach?0:null,p.OutputKind==CardKind.Alcohol,p.OutputKind==CardKind.Alcohol?0:null)});
  BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Recovery));
 }
 private bool ContinueZonghuSimpleUse(long id,ProgramSimpleCardContinuation c)
 {if(c.CardId!=0||!IsZonghuUse(id))return false;var u=LifecycleCardUse(id)!;var actor=_players[u.SourceSeat];var card=ZonghuRepresentation(id);SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);if(!actor.IsAlive||_winner!=Winner.None){FinishCardUse(id,card,u.CardKind);return true;}if(c.Effect==SimpleCardUseEffect.Recovery&&u.CardKind is CardKind.Peach or CardKind.Alcohol)CompleteRecoveryCardUse(actor,_players[u.TargetSeats[u.TargetIndex]],card,id,u.CardKind,c.RecoveryAmount,c.RecoveryPolicySources??[]);else if(c.Effect==SimpleCardUseEffect.Alcohol&&u.CardKind==CardKind.Alcohol)CompleteAlcoholUse(actor,card,id);else throw new InvalidOperationException("Zonghu changed its native simple-card continuation.");return true;}
 private bool FinishZonghuAttack(CardAttackHandle attack,out bool paused)
 {paused=false;if(attack.Card is not null||!IsZonghuUse(attack.ResolutionId)||!IsSlashCard(attack.EffectiveCardKind??CardKind.Dodge))return false;var u=LifecycleCardUse(attack.ResolutionId)!;SetCardUseStep(u.Id,ResolutionFrameStep.Completed);AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(u.Id,0,u.CardKind));if(_winner==Winner.None&&TryBeginProgramCardWindow(attack,u.Action!,SkillProgramTriggerWindow.CardUseCompleted,u.TargetSeats,ProgramCardContinuation.CompletedSlash,cardUseCausedDamage:attack.CardUseCausedDamage))paused=true;else{PopFinishedCardUse(u.Id);CompleteFinishedAttackCardUse(attack);}return true;}
 [NativeUseValidation(27512)]private void ValidateZonghuNativeReturn(CardUseFrame use){if(use.ZonghuOrigin is not null&&(!IsIssuedZonghuUse(use)||use.Step!=ResolutionFrameStep.Completed))throw new InvalidOperationException("Zonghu cannot retire before its real native use and children finish.");}
 [NativeUseReturn(18)]private void ReturnZonghuNativeUse(CardUseFrame use)
 {
  if(use.ZonghuOrigin is not{}p)return;if(!IsIssuedZonghuUse(use)||_resolutionStack.Any(f=>f.Id==use.Id)||ProgramEventHistory<ZonghuUseReturnedEvent>().Any(e=>e.AttemptId==p.AttemptId))throw new InvalidOperationException("Zonghu native return is not its exact once completed use.");
  if(_resolutionStack.SingleOrDefault(f=>f.Id==p.Need.OwnerFrameId)is ProgramSkillFrame{ZonghuReturn:{}r}parent&&r.Origin==p&&r.UseId==use.Id)ReplaceRuntimeFrame(parent.Id,parent with{ZonghuReturn=r with{Returned=true}});AdvanceEventRulesAndQueueFact(new ZonghuUseReturnedEvent(p.AttemptId,use.Id,use.Action!.ActionId));
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,27513)]private bool ResumeZonghuRequestedUse(long id)
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZonghuReturn:{Returned:true}r}parent||parent.Id!=id)return false;if(ProgramEventHistory<ZonghuUseReturnedEvent>().Count(e=>e.AttemptId==r.Origin.AttemptId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)!=1)throw new InvalidOperationException("Zonghu caller lost its once-completed typed return.");if(parent.SunYuan is not null||parent.Chanshuang is not null||parent.WuJing is not null||parent.LvKuangLvXiang is not null||parent.CaoChun is not null||parent.KeBiNengPingduan is not null)return false;ReplaceRuntimeTop(parent=parent with{ZonghuReturn=null});if(parent.FixedTargetSlash is{Issued:true})TryResumeFixedSlashAndDeclaredDeck(id);else AdvanceRuntimeProgram(id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27513)][ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27513)]private bool ContinueZonghuOriginalProgram(ProgramSkillFrame f)=>f.ZonghuReturn is{}r&&ValidZonghuPayment(r.Origin)&&r.Origin.Need.OwnerFrameId==f.Id&&r.Origin.Need.InstructionIndex==f.InstructionIndex;
}
public sealed record ZonghuKeActionAcceptedEvent(long ProgramId,int InstructionIndex,CardConversionSource RequestSource,string RequestHash,long AttemptId,CardKind OutputKind,int TargetSeat):IGameEvent;
