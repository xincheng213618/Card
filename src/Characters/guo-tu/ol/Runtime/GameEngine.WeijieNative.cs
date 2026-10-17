namespace CardGame.Core;
public sealed partial class GameEngine
{
 private CardActionContext PrepareWeijieAction(CardActionContext a,WeijiePaidOrigin? p)=>p is null?a:CaptureFactionAction(new(a.ActionId,a.ParentActionId,a.Type,p.Need.ActorSeat,p.Need.ProviderSeat,p.Need.RequesterSeat,null,a.OpponentSeat,a.EffectiveKind,a.TargetSeats,[],[p.Source],a.DesignatedTargetSeats,Suit.None,0,null));
 private void IssueWeijieUse(CardUseFrame use,WeijiePaidOrigin? p)
 {
  if(p is null)return;
  if(!ValidWeijiePayment(p)||ProgramEventHistory<WeijieAttemptResolvedEvent>().Count(e=>e.FrameId==p.AttemptId&&e.Matched&&e.NativeLegal)!=1||use.CardId!=0||use.PhysicalCardIds is not{Count:0}||use.CardKind!=p.OutputKind||use.Action is not{Type:CardActionType.Use,PhysicalCards.Count:0}a||a.ActorSeat!=p.Need.ActorSeat||a.ProviderSeat!=p.Need.ProviderSeat||a.RequesterSeat!=p.Need.RequesterSeat||a.ConversionChain is not[var source]||source!=p.Source||!use.TargetSeats.SequenceEqual([p.TargetSeat])&&!(p.OutputKind==CardKind.Alcohol&&use.TargetSeats.Count==0)||ProgramEventHistory<WeijieUseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId)||ProgramEventHistory<WeijieResponseIssuedEvent>().Any(e=>e.AttemptId==p.AttemptId))throw new InvalidOperationException("Weijie cannot issue a second zero-entity basic or change its real payer, actor or original native target.");
  UpdateLifecycleCardUse(use.Id,u=>u with{WeijieOrigin=p});AdvanceEventRulesAndQueueFact(new WeijieUseIssuedEvent(p.AttemptId,use.Id,a.ActionId,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,p.OutputKind));
  var i=_resolutionStack.FindIndex(f=>f.Id==use.Id);if(i>0&&_resolutionStack[i-1]is ProgramSkillFrame{WeijieReturn:{}r}parent&&r.Origin==p&&r.UseId==0)ReplaceRuntimeFrame(parent.Id,parent with{WeijieReturn=r with{UseId=use.Id,ActionId=a.ActionId}});
 }
 private bool IsWeijieUse(long id)=>LifecycleCardUse(id)is{}u&&IsIssuedWeijieUse(u);
 private bool IsIssuedWeijieUse(CardUseFrame use)
 {
  if(use.WeijieOrigin is not{}p||!ValidWeijiePayment(p)||use.CardId!=0||use.PhysicalCardIds is not{Count:0}||use.Action is not{Type:CardActionType.Use,PhysicalCards.Count:0}current)return false;
  var a=current;if(use.CurrentSlashFirePolicy is{}fire){if(!IsSlashCard(p.OutputKind))return false;AssertCurrentSlashFirePolicy(use);a=fire.OriginalAction;}
  return (use.CardKind==p.OutputKind||use.CurrentSlashFirePolicy is not null&&IsSlashCard(p.OutputKind))&&a.EffectiveKind==p.OutputKind&&a.ConversionChain is[var source]&&source==p.Source&&a.ProviderSeat==p.Need.ProviderSeat&&a.RequesterSeat==p.Need.RequesterSeat&&a.ResponderSeat is null&&a.EffectiveSuit==Suit.None&&a.EffectiveRank==0&&a.EffectiveIsRed is null&&WeijieIssuedActorMatches(use,p)&&ProgramEventHistory<WeijieAttemptResolvedEvent>().Count(e=>e.FrameId==p.AttemptId&&e.Matched&&e.NativeLegal)==1&&ProgramEventHistory<WeijieUseIssuedEvent>().Where(e=>e.AttemptId==p.AttemptId).ToArray()is[var issued]&&issued.UseId==use.Id&&issued.ActionId==a.ActionId&&issued.ActorSeat==p.Need.ActorSeat&&issued.ProviderSeat==p.Need.ProviderSeat&&issued.RequesterSeat==p.Need.RequesterSeat&&issued.OutputKind==p.OutputKind;
 }
 private bool WeijieIssuedActorMatches(CardUseFrame use,WeijiePaidOrigin p)=>ProgramEventHistory<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==use.Id)?ShownEntityUseActorMatches(use,p.Need.ActorSeat,p.Need.ProviderSeat):_resolutionStack.LastOrDefault()?.Id==use.Id&&use.SourceSeat==p.Need.ActorSeat&&use.Action?.ActorSeat==p.Need.ActorSeat&&!ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Any(e=>e.CardUseFrameId==use.Id);
 private Card WeijieRepresentation(long id)=>LifecycleCardUse(id)is{}u&&IsIssuedWeijieUse(u)?new(0,u.CardKind,Suit.None,0):throw new InvalidOperationException("Weijie representation requires its exact once-paid native zero use.");
 private void DispatchWeijie(WeijiePaidOrigin p)
 {
  if(!ValidWeijiePayment(p)||!_players[p.Need.ActorSeat].IsAlive||!_players[p.Need.ProviderSeat].IsAlive)throw new InvalidOperationException("Weijie cannot issue for a dead actor/provider.");
  ClearPendingDecision();var n=p.Need;
  if(n.Intent is WeijieIntent.Dodge or WeijieIntent.Duel or WeijieIntent.Group or WeijieIntent.FactionDodge or WeijieIntent.FactionSlashPlayed){DispatchWeijieResponse(p);return;}
  if(n.Intent==WeijieIntent.Dying){DispatchWeijieRecovery(p,true);return;}
  if(n.Intent==WeijieIntent.Qinglong){var old=ActiveQinglongCrescentBlade!.Attack;ActiveQinglongCrescentBlade=null;SetCardUseStep(old.ResolutionId,ResolutionFrameStep.ResolvingEffect);AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId,n.ActorSeat,p.TargetSeat,true,[],p.OutputKind));SuspendContinuationForQinglongFollowup(old.ResolutionId);BeginWeijieSlash(p,null,false,null);return;}
  if(n.Intent==WeijieIntent.BorrowedSword){var b=ActiveBorrowedSword!;PopResponseWindow(b.ResolutionId);SetCardUseStep(b.ResolutionId,ResolutionFrameStep.ResolvingEffect);b.AwaitingSlashChoice=false;b.SlashCardId=0;b.EffectiveSlashKind=p.OutputKind;BeginWeijieSlash(p,b,true,null);return;}
  if(n.Intent==WeijieIntent.FactionSlashUse){DispatchWeijieFactionUse(p);return;}
  var parent=GetActiveProgramFrame(n.OwnerFrameId);if(parent.InstructionIndex!=n.InstructionIndex||GetPausedPrivateOfferEffect(parent)?.Op!=n.RequestOp)throw new InvalidOperationException("Weijie requested use moved its original program cursor.");
  ReplaceRuntimeTop(parent=parent with{WeijieReturn=new(p,0,0),Step=ResolutionFrameStep.ResolvingEffect});
  if(n.Intent==WeijieIntent.GenericBasic)
  {var k=parent.KeBiNengPingduan!;var action=new LegalAction(IsSlashCard(p.OutputKind)?LegalActionKind.Slash:p.OutputKind==CardKind.Peach?LegalActionKind.Peach:LegalActionKind.Alcohol,0,p.OutputKind==CardKind.Alcohol?null:p.TargetSeat,"诿解：真实零实体基本牌",p.OutputKind,TargetSeats:p.OutputKind==CardKind.Alcohol?[]:[p.TargetSeat]){ConversionSource=p.Source};AdvanceEventRulesAndQueueFact(new WeijieKeActionAcceptedEvent(parent.Id,parent.InstructionIndex,k.Source,parent.GameplayHash,p.AttemptId,p.OutputKind,p.TargetSeat));ReplaceRuntimeTop(parent=parent with{WeijieKeAcceptedOrigin=p,KeBiNengPingduan=k with{BasicActions=k.BasicActions.Append(action).ToArray(),SelectedBasicAction=action,Stage=KeBiNengPingduanStage.BasicIssuing}});if(IsSlashCard(p.OutputKind))BeginWeijieSlash(p,null,false,parent.Id);else if(p.OutputKind==CardKind.Peach)DispatchWeijieRecovery(p,false);else{var id=BeginCardUse(new(0,p.OutputKind,Suit.None,0),n.ActorSeat,[],p.OutputKind,physicalCardIds:[],conversionSource:p.Source,weijieOrigin:p);BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Alcohol));}return;}
  PrepareWeijieProgram(parent,p);BeginWeijieSlash(p,null,false,parent.Id);
 }
 private void PrepareWeijieProgram(ProgramSkillFrame parent,WeijiePaidOrigin p)
 {
  var e=GetPausedPrivateOfferEffect(parent)!;var n=p.Need;
  if(IsSayingSlashRequest(parent)){PrepareSayingSuppliedSlash(parent,n.ActorSeat,n.ProviderSeat,p.TargetSeat,p.OutputKind,[],p.Source);return;}
  if(IsChanshuangSlashRequest(parent)){PrepareChanshuangSuppliedSlash(parent,n.ActorSeat,n.ProviderSeat,p.TargetSeat,p.OutputKind,[],p.Source);return;}
  if(IsQigongRequest(parent)){ReplaceRuntimeTop(parent with{LvKuangLvXiang=parent.LvKuangLvXiang! with{Stage="issuing",IssuingIds=[],IssuingKind=p.OutputKind,IssuingProvider=p.Need.ProviderSeat}});return;}
  if(IsWuJingHejiRequest(parent)){ReplaceRuntimeTop(parent with{WuJing=parent.WuJing! with{Stage=WuJingStage.IssuingUse,Selection=new(p.OutputKind,false,false){Conversions=[p.Source]},Before=_movementSequence,After=_movementSequence}});return;}
  if(IsCaoChunInitialRequest(parent)){var r=parent.CaoChun!;ReplaceRuntimeTop(parent with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=new(parent.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,false,p.TargetSeat,p.OutputKind){ProviderSeat=n.ProviderSeat,Materials=[],PaymentBefore=_movementSequence}}});return;}
  if(n.Intent==WeijieIntent.ProgramSlash)CommitProgramChoiceResult(parent.Id,e.ResultBind!,RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption,n.ActorSeat,"诿解：视为使用杀。");
  else if(n.Intent==WeijieIntent.AssistedSlash){ReplaceRuntimeTop(parent with{AssistedSlashRequest=null});CommitProgramChoiceResult(parent.Id,e.ResultBind!,"used-slash",n.ActorSeat,"诿解：视为使用杀。");}
  else if(n.Intent==WeijieIntent.FixedSlash)ReplaceRuntimeTop(parent with{FixedTargetSlash=parent.FixedTargetSlash! with{Issued=true}});
  else if(n.Intent==WeijieIntent.NearestLegalSlash)ReplaceRuntimeTop(parent with{NearestLegalSlashRequest=null});
 }
 private void BeginWeijieSlash(WeijiePaidOrigin p,BorrowedSwordHandle? borrowed,bool counts,long? parent,FactionCardRequestHandle? faction=null)
 {
  var actor=_players[p.Need.ActorSeat];var target=_players[p.TargetSeat];var kind=p.OutputKind;var id=BeginCardUse(new(0,kind,Suit.None,0),actor.Seat,[target.Seat],kind,ignoresArmor:HasCardArmorBypass(actor,target,kind),physicalCardIds:[],conversionSource:p.Source,weijieOrigin:p);
  var actual=LifecycleCardUse(id)!;var attack=new CardAttackHandle(this,id,actor.Seat,actual.TargetSeats[0],card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:kind,ignoresArmor:HasCardArmorBypass(actor,_players[actual.TargetSeats[0]],kind),programSkillCardUseFrameId:parent);
  if(borrowed is not null)borrowed.ActiveAttack=attack;if(faction is not null)faction.ActiveAttack=attack;ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;
  if(counts&&_phase==TurnPhase.Play&&actor.Seat==_currentSeat&&actual.UnlimitedUse!=true&&!IgnoresProgramSlashLimit(actor,p.Source))RecordSlashUseDebit(id,actor.Seat);
  MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,kind);CompleteQigongNativePayment(id);CompleteCaoChunNativePayment(id);AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,kind,actor.Seat,actual.TargetSeats[0],IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,_players[actual.TargetSeats[0]]);
  if(TryBeginProvidedCardSupplyCompletion(attack,faction))return;
  if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,LifecycleCardUse(id)!.TargetSeats,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
 }
 private void DispatchWeijieRecovery(WeijiePaidOrigin p,bool dying)
 {
  var n=p.Need;var source=_players[n.ActorSeat];var target=_players[p.TargetSeat];var card=new Card(0,p.OutputKind,Suit.None,0);var id=BeginCardUse(card,source.Seat,[target.Seat],p.OutputKind,physicalCardIds:[],conversionSource:p.Source,weijieOrigin:p);
  if(dying)UpdateLifecycleCardUse(id,u=>u with{DyingResponse=new(n.OwnerFrameId,source.Seat,p.OutputKind==CardKind.Peach,p.OutputKind==CardKind.Peach?0:null,p.OutputKind==CardKind.Alcohol,p.OutputKind==CardKind.Alcohol?0:null)});
  BeginSimpleCardUse(id,new(0,SimpleCardUseEffect.Recovery));
 }
 private bool ContinueWeijieSimpleUse(long id,ProgramSimpleCardContinuation c)
 {if(c.CardId!=0||!IsWeijieUse(id))return false;var u=LifecycleCardUse(id)!;var actor=_players[u.SourceSeat];var card=WeijieRepresentation(id);SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);if(!actor.IsAlive||_winner!=Winner.None){FinishCardUse(id,card,u.CardKind);return true;}if(c.Effect==SimpleCardUseEffect.Recovery&&u.CardKind is CardKind.Peach or CardKind.Alcohol)CompleteRecoveryCardUse(actor,_players[u.TargetSeats[u.TargetIndex]],card,id,u.CardKind,c.RecoveryAmount,c.RecoveryPolicySources??[]);else if(c.Effect==SimpleCardUseEffect.Alcohol&&u.CardKind==CardKind.Alcohol)CompleteAlcoholUse(actor,card,id);else throw new InvalidOperationException("Weijie changed its native simple-card continuation.");return true;}
 private bool FinishWeijieAttack(CardAttackHandle attack,out bool paused)
 {paused=false;if(attack.Card is not null||!IsWeijieUse(attack.ResolutionId)||!IsSlashCard(attack.EffectiveCardKind??CardKind.Dodge))return false;var u=LifecycleCardUse(attack.ResolutionId)!;SetCardUseStep(u.Id,ResolutionFrameStep.Completed);AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(u.Id,0,u.CardKind));if(_winner==Winner.None&&TryBeginProgramCardWindow(attack,u.Action!,SkillProgramTriggerWindow.CardUseCompleted,u.TargetSeats,ProgramCardContinuation.CompletedSlash,cardUseCausedDamage:attack.CardUseCausedDamage))paused=true;else{PopFinishedCardUse(u.Id);CompleteFinishedAttackCardUse(attack);}return true;}
 [NativeUseValidation(27462)]private void ValidateWeijieNativeReturn(CardUseFrame use){if(use.WeijieOrigin is not null&&(!IsIssuedWeijieUse(use)||use.Step!=ResolutionFrameStep.Completed))throw new InvalidOperationException("Weijie cannot retire before its real native use and children finish.");}
 [NativeUseReturn(19)]private void ReturnWeijieNativeUse(CardUseFrame use)
 {
  if(use.WeijieOrigin is not{}p)return;if(!IsIssuedWeijieUse(use)||_resolutionStack.Any(f=>f.Id==use.Id)||ProgramEventHistory<WeijieUseReturnedEvent>().Any(e=>e.AttemptId==p.AttemptId))throw new InvalidOperationException("Weijie native return is not its exact once completed use.");
  if(_resolutionStack.SingleOrDefault(f=>f.Id==p.Need.OwnerFrameId)is ProgramSkillFrame{WeijieReturn:{}r}parent&&r.Origin==p&&r.UseId==use.Id)ReplaceRuntimeFrame(parent.Id,parent with{WeijieReturn=r with{Returned=true}});AdvanceEventRulesAndQueueFact(new WeijieUseReturnedEvent(p.AttemptId,use.Id,use.Action!.ActionId));
 }
 [ProgramContinuation(ProgramContinuationKind.Resume,27463)]private bool ResumeWeijieRequestedUse(long id)
 {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WeijieReturn:{Returned:true}r}parent||parent.Id!=id)return false;if(ProgramEventHistory<WeijieUseReturnedEvent>().Count(e=>e.AttemptId==r.Origin.AttemptId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)!=1)throw new InvalidOperationException("Weijie caller lost its once-completed typed return.");if(TryGetSayingSlashRequest(parent,out _,out _)||parent.Chanshuang is not null||parent.WuJing is not null||parent.LvKuangLvXiang is not null||parent.CaoChun is not null||parent.KeBiNengPingduan is not null)return false;ReplaceRuntimeTop(parent=parent with{WeijieReturn=null});if(parent.FixedTargetSlash is{Issued:true})TryResumeFixedSlashAndDeclaredDeck(id);else AdvanceRuntimeProgram(id);return true;}
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27463)][ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27463)]private bool ContinueWeijieOriginalProgram(ProgramSkillFrame f)=>f.WeijieReturn is{}r&&ValidWeijiePayment(r.Origin)&&r.Origin.Need.OwnerFrameId==f.Id&&r.Origin.Need.InstructionIndex==f.InstructionIndex;
}
public sealed record WeijieKeActionAcceptedEvent(long ProgramId,int InstructionIndex,CardConversionSource RequestSource,string RequestHash,long AttemptId,CardKind OutputKind,int TargetSeat):IGameEvent;
