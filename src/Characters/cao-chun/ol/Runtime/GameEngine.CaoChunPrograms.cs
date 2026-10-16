namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ShanjiaDrawReason="skill-program.shanjia.draw",ShanjiaDiscardReason="skill-program.shanjia.discard";
 private static CardConversionSource CaoChunSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool CaoChunSourceCurrent(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==f.GameplayHash;
 private SkillProgramStepOutcome ExecuteCaoChun(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=GetActiveProgramFrame(supplied.Id);var owner=_players[f.OwnerSeat];if(f.CaoChun is not null||f.InstructionIndex!=1)throw new InvalidOperationException("Shanjia cannot reopen its owned instruction.");
  if(op==SkillProgramEffectOp.InitializeShanjiaLossMarkers)
  {
   if(f.WindowContext?.Window!=SkillProgramTriggerWindow.GameStarting||!CaoChunSourceCurrent(f)||owner.Shanjia is not null)throw new InvalidOperationException("Shanjia marks require one actual native game start.");owner.Shanjia=new(CaoChunSource(f),f.GameplayHash,3);AdvanceEventRulesAndQueueFact(new ShanjiaMarksChangedEvent(CaoChunSource(f),f.GameplayHash,0,3,0));return SkillProgramStepOutcome.Continue;
  }
  if(_phase!=TurnPhase.Play||f.OwnerSeat!=_currentSeat||_turnNumber<1)throw new InvalidOperationException("Shanjia requires its actual own Play.");
  if(op==SkillProgramEffectOp.DrawAndRequestShanjiaSlash)
  {
   if(f.TriggerId is not null||!CaoChunSourceCurrent(f))throw new InvalidOperationException("Shanjia requires its unpaid current activation source.");var r=new CaoChunReceipt(CaoChunSource(f),f.GameplayHash,1,_turnNumber,_cardUseDebitPhaseInstanceId,CaoChunStage.DrawChildren,_movementSequence,_movementSequence);
   ReplaceRuntimeTop(f=f with{CaoChun=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});var cards=DrawCards(owner,3,true,new(ShanjiaDrawReason));r=r with{After=_movementSequence,ActualCount=cards.Count};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{CaoChun=r});IssueCaoChunMovement(f.Id,r);ResumeCaoChun(f.Id);return SkillProgramStepOutcome.AwaitChild;
  }
  if(op==SkillProgramEffectOp.PayShanjiaHandUseDebt)
  {
   if(f.WindowContext?.CaoChunPayment is not{}payment||!ValidCaoChunPayment(payment)||owner.Shanjia?.Debt!=payment.Debt||!owner.IsAlive)throw new InvalidOperationException("Shanjia debt lost its accepted actual hand payment.");var d=payment.Debt;var next=d with{Remaining=d.Remaining-1};owner.Shanjia=owner.Shanjia with{Debt=next};
   var r=new CaoChunReceipt(d.Source,d.GameplayHash,1,_turnNumber,_cardUseDebitPhaseInstanceId,CaoChunStage.DebtChoice,Payment:payment);ReplaceRuntimeTop(f=f with{CaoChun=r});AdvanceEventRulesAndQueueFact(new ShanjiaHandUseDebitedEvent(f.Id,d.Source,d.GameplayHash,_turnNumber,r.PhaseInstance,payment.Action.ActionId,d.Remaining,next.Remaining));PublishCaoChun(f);return SkillProgramStepOutcome.AwaitChoice;
  }
  if(op!=SkillProgramEffectOp.RequestShanjiaEndingSlash||f.WindowContext?.Window!=SkillProgramTriggerWindow.PlayEnding||!CaoChunSourceCurrent(f)||owner.Shanjia?.Debt is not{}ending||ending.ActualTurn!=_turnNumber||ending.PhaseInstance!=_cardUseDebitPhaseInstanceId||ending.EndingConsumed||ending.DiscardedAny&&!ending.OnlyEquipment)throw new InvalidOperationException("Shanjia ending Slash lost its exact settled actual Play debt.");owner.Shanjia=owner.Shanjia with{Debt=ending with{EndingConsumed=true}};ReplaceRuntimeTop(f=f with{CaoChun=new(CaoChunSource(f),f.GameplayHash,1,_turnNumber,_cardUseDebitPhaseInstanceId,CaoChunStage.EndingSlash)});PublishCaoChun(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 private Dictionary<string,string>CaoChunArgs(string branch)=>new(){{"program-action","shanjia"},{"branch",branch}};
 private IReadOnlyList<PromptChoice>CaoChunChoices(ProgramSkillFrame f)
 {
  var r=f.CaoChun!;var owner=_players[f.OwnerSeat];var result=new List<PromptChoice>();
  if(r.Stage==CaoChunStage.DebtChoice)
  {
   if(owner.IsAlive)result.AddRange(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard,canSelect:(_,c)=>!c.IsGeneralWeapon).Select(c=>new PromptChoice(new($"shanjia.{f.Id}.discard.{c.Cards.Single()}"),c.Label,c.Cards,[],CaoChunArgs("discard"))));
   if(result.Count==0)result.Add(new(new($"shanjia.{f.Id}.empty"),"无合法牌可弃",[],[],CaoChunArgs("empty")));return Array.AsReadOnly(result.ToArray());
  }
  if(!CaoChunSourceCurrent(f))return [];
  foreach(var target in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))
  {
   if(r.Stage==CaoChunStage.EndingSlash)
   {
    if(CanUseSlashTarget(owner,target,new Card(0,CardKind.Slash,Suit.None,0),null,CardKind.Slash,ignoreDistance:true,noEffectiveRank:true,physicalCardIds:[]))result.Add(new(new($"shanjia.{f.Id}.virtual.{target.Seat}"),$"视为对{target.Name}使用无距离限制的杀",[],[target.Seat],CaoChunArgs("virtual")));continue;
   }
   if(_contentRegistry.ProgramDependencies.UsesRoundOnceCardlessBasics)
   foreach(var(source,_)in RoundOnceCardlessRules(owner,CardKind.Slash))if(RoundOnceCardlessProgramTargetLegal(owner,target.Seat,source))
   {var args=CaoChunArgs("slash");args["effective-kind"]=CardKind.Slash.ToString();args["round-once-cardless"]="true";AddConversionParameters(args,source);result.Add(new(new($"shanjia.{f.Id}.cardless.{source.SkillInstanceId}.{source.BindingId}.{target.Seat}"),"使用独立卫境额度视为使用杀",[],[target.Seat],args));}
   bool Legal(Card c,CardKind k,CardConversionSource?source=null,IReadOnlyList<int>?ids=null)=>CanUseSlashTarget(owner,target,c,source,k,physicalCardIds:ids)&&AssistedSlashPaymentHasRange(owner,target.Seat,ids?.Select(GetAttackCard).ToArray()??[c]);
   foreach(var card in GetSlashUseCards(owner))foreach(var kind in GetSlashUseKinds(owner,IsSlashCard(card.Kind)?card.Kind:CardKind.Slash))
   {
    if(!Legal(card,kind)||IsTurnPhysicalUseForbidden(owner.Seat,[card.Id]))continue;var args=CaoChunArgs("slash");args["effective-kind"]=kind.ToString();result.AddRange(CreateConversionChoiceVariants(owner,card,kind,false,$"shanjia.{f.Id}.{card.Id}.{kind}.{target.Seat}",$"对{target.Name}使用杀",[card.Id],[target.Seat],args));
   }
   foreach(var card in GetPlayableCards(owner).Concat(GetEquipment(owner)).DistinctBy(c=>c.Id))foreach(var kind in SlashKinds.Where(k=>k!=CardKind.Slash))foreach(var conversion in GetProgramViewAsConversions(owner,card,kind,false))
   {
    if(IsTurnHandCardRestricted(owner,card)||IsTurnPhysicalUseForbidden(owner.Seat,[card.Id])||!Legal(card,kind,conversion))continue;var args=CaoChunArgs("slash");args["effective-kind"]=kind.ToString();AddConversionParameters(args,conversion);result.Add(new(new($"shanjia.{f.Id}.typed.{kind}.{conversion.SkillInstanceId}.{conversion.BindingId}.{card.Id}.{target.Seat}"),$"将真实材料当{CardCatalog.Get(kind).DisplayName}使用",[card.Id],[target.Seat],args));
   }
   if(GetFactionResponsePolicy(owner,CardKind.Slash)is{}policy&&CanRequestAssistedProgramFactionSlash(owner.Seat,target.Seat)){var args=CaoChunArgs("faction");args["skill"]=policy.SkillId;result.Add(new(new($"shanjia.{f.Id}.faction.{target.Seat}"),"请求势力角色提供杀",[],[target.Seat],args));}
   foreach(var pair in GetZhangbaSlashPairs(owner))
   {
    var ids=pair.Select(c=>c.Id).ToArray();if(!Legal(pair[0],CardKind.Slash,ids:ids)||IsTurnPhysicalUseForbidden(owner.Seat,ids))continue;var args=CaoChunArgs("slash");args["effective-kind"]=CardKind.Slash.ToString();args["equipment"]=CardKind.ZhangbaSerpentSpear.ToString();result.Add(new(new($"shanjia.{f.Id}.pair.{string.Join('-',ids)}.{target.Seat}"),"丈八蛇矛使用杀",ids,[target.Seat],args));
   }
   foreach(var kind in SlashKinds)foreach(var selection in GetProgramMultiCardViewAsSelections(owner,kind,false))
   {
    var ids=selection.Cards.Select(c=>c.Id).ToArray();if(!Legal(selection.Cards[0],kind,selection.Source,ids)||IsTurnPhysicalUseForbidden(owner.Seat,ids))continue;var args=CaoChunArgs("slash");args["effective-kind"]=kind.ToString();AddConversionParameters(args,selection.Source);result.Add(new(new($"shanjia.{f.Id}.multi.{selection.Source.SkillInstanceId}.{selection.Source.BindingId}.{string.Join('-',ids)}.{target.Seat}"),"将真实材料当杀使用",ids,[target.Seat],args));
   }
  }
  result.Add(new(new($"shanjia.{f.Id}.skip"),"不使用杀",[],[],CaoChunArgs("skip")));return Array.AsReadOnly(result.ToArray());
 }
 private void PublishCaoChun(ProgramSkillFrame f)
 {
  var choices=CaoChunChoices(f);if(choices.Count==0){FinishCaoChun(f);return;}ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"缮甲：请选择实际结算",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),f.CaoChun!.Stage==CaoChunStage.InitialSlash?_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat).Select(p=>p.Seat).ToArray():choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,"缮甲","缮甲",_contentRegistry.GetSkill(f.SkillId).Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.DrawAndRequestShanjiaSlash,SkillProgramEffectOp.PayShanjiaHandUseDebt,SkillProgramEffectOp.RequestShanjiaEndingSlash)]private PromptChoice CaoChunAiChoice(ProgramSkillFrame f)=>CaoChunChoices(f).First();
 [ProgramAnswer("shanjia")]private void ResolveCaoChunChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireCaoChun(f);var r=f.CaoChun!;if(_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],CaoChunChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Shanjia choice changed actual actor, owned payment or target.");ClearPendingDecision();var branch=choice.Parameters["branch"];
  if(r.Stage==CaoChunStage.DebtChoice)
  {
   if(branch=="empty"){FinishCaoChun(f);return;}var id=choice.Cards.Single();var from=_cardZones.GetLocation(id);var kind=GetAdvancedCard(id).Kind;r=r with{Stage=CaoChunStage.DiscardChildren,Before=_movementSequence,After=_movementSequence,ActualCount=1,DiscardId=id,DiscardFrom=from,DiscardKind=kind};ReplaceRuntimeTop(f with{CaoChun=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
   MoveProgramLogicalDiscard(f,f.OwnerSeat,[id],new(ShanjiaDiscardReason),(batch,moves)=>{var paid=r with{After=moves.Single().Sequence,PaidBatchId=batch};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{CaoChun=paid});if(_players[f.OwnerSeat].Shanjia is{}s&&s.Debt is{}d)_players[f.OwnerSeat].Shanjia=s with{Debt=d with{DiscardedAny=true,OnlyEquipment=d.OnlyEquipment&&CardCatalog.Get(kind).CategoryName=="装备牌"}};IssueCaoChunMovement(f.Id,paid);});AdvanceRuntimeProgram(f.Id);return;
  }
  if(!CaoChunSourceCurrent(f)){FinishCaoChun(f);return;}if(branch=="skip"){if(r.Stage==CaoChunStage.InitialSlash)ArmCaoChunDebt(f);FinishCaoChun(GetActiveProgramFrame(f.Id));return;}
  if(branch=="faction"){BeginCaoChunFactionSlash(f,choice.Targets.Single());return;}
  var ending=r.Stage==CaoChunStage.EndingSlash;IReadOnlyList<Card>cards=[];CardKind slashKind=CardKind.Slash;CardConversionSource?conversion=null;RoundOnceCardlessBasicPaymentReceipt? independent=null;
  if(!ending){if(choice.Cards.Count==0){if(choice.Parameters.GetValueOrDefault("round-once-cardless")!="true"||!TryReadConversionSource(choice.Parameters,out conversion)||conversion is null)throw new InvalidOperationException("Shanjia cardless request lacks its exact independent source.");independent=ConsumeRoundOnceCardlessBasic(conversion,false);}else{var selected=ReadAssistedSlashPayment(_players[f.OwnerSeat],choice);cards=selected.Cards;slashKind=selected.Kind;conversion=selected.Conversion;}}
  var native=new CaoChunNativeSlash(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,ending,choice.Targets.Single(),slashKind){Materials=cards.Select(c=>new CardActionCost(c.Id,c.Kind,_cardZones.GetLocation(c.Id))).ToArray(),PaymentBefore=_movementSequence,RoundOncePayment=independent};ReplaceRuntimeTop(f=f with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=native},Step=ResolutionFrameStep.ResolvingEffect});
  if(!ending&&cards.Count>0){ResolveSlashCore(_players[f.OwnerSeat],_players[native.TargetSeat],cards[0],slashKind,f.OwnerSeat,physicalCards:cards,countsTowardSlashLimit:false,usesZhuqueFan:slashKind==CardKind.FireSlash&&cards[0].Kind==CardKind.Slash&&HasZhuqueFan(_players[f.OwnerSeat]),conversionSource:conversion,programSkillCardUseFrameId:f.Id);return;}
  var actor=_players[f.OwnerSeat];var logical=new Card(0,CardKind.Slash,Suit.None,0);var target=_players[native.TargetSeat];var useId=BeginCardUse(logical,actor.Seat,[target.Seat],CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,target,CardKind.Slash),physicalCardIds:[],conversionSource:conversion);if(independent is not null)IssueRoundOnceCardlessNativeUse(useId,conversion!);BindCaoChunNativeUse(LifecycleCardUse(useId)!);
  var attack=new CardAttackHandle(this,useId,actor.Seat,target.Seat,card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,target,CardKind.Slash),programSkillCardUseFrameId:f.Id);ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);CaptureProgramAlcoholConsumption(useId,actor);actor.HasAlcoholEffect=false;MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,CardKind.Slash);AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,CardKind.Slash,actor.Seat,target.Seat,IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,target);if(!TryMarkProgramUseCommitted(useId)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(useId)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,LifecycleCardUse(useId)!.TargetSeats,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
 }
 private void IssueCaoChunMovement(long id,CaoChunReceipt r)=>AdvanceEventRulesAndQueueFact(new ShanjiaMovementPaidEvent(id,r.Source,r.GameplayHash,r.Stage,r.Before,r.After,r.ActualCount,r.PaidBatchId));
 private void ArmCaoChunDebt(ProgramSkillFrame f)
 {
  var r=f.CaoChun!;var owner=_players[f.OwnerSeat];if(!owner.IsAlive)return;var marks=owner.Shanjia?.LossMarks??0;if(owner.Shanjia is not{}state)throw new InvalidOperationException("Shanjia activation has no real starting marker state.");var debt=new ShanjiaPhaseDebt(r.Source,r.GameplayHash,f.Id,r.ActualTurn,r.PhaseInstance,marks,marks);owner.Shanjia=state with{Debt=debt};AdvanceEventRulesAndQueueFact(new ShanjiaDebtArmedEvent(r.Source,r.GameplayHash,f.Id,r.ActualTurn,r.PhaseInstance,marks));
 }
 private bool DrainCaoChun(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,24500)]private bool ResumeCaoChun(long id)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoChun:{}r}f||f.Id!=id)return false;RequireCaoChun(f);
  if(r.Stage is CaoChunStage.DrawChildren or CaoChunStage.DiscardChildren){if(DrainCaoChun(f))return true;if(r.Stage==CaoChunStage.DrawChildren&&CaoChunSourceCurrent(f)){ReplaceRuntimeTop(f=f with{CaoChun=r with{Stage=CaoChunStage.InitialSlash},PendingMovementContinuation=null});PublishCaoChun(f);return true;}if(r.Stage==CaoChunStage.DrawChildren&&_players[f.OwnerSeat].IsAlive)ArmCaoChunDebt(f);FinishCaoChun(f);return true;}
  if(r.Stage==CaoChunStage.NativeSlash&&r.Slash is{Returned:true}n){if(!n.Ending)ArmCaoChunDebt(f);FinishCaoChun(f);return true;}return false;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,24500)]private bool ReturnCaoChunMovement(ProgramSkillFrame f)=>f.CaoChun is not null&&ResumeCaoChun(f.Id);
 private bool IsCaoChunMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>e?.Op is (SkillProgramEffectOp.DrawAndRequestShanjiaSlash or SkillProgramEffectOp.PayShanjiaHandUseDebt)&&f.CaoChun?.Stage is (CaoChunStage.DrawChildren or CaoChunStage.DiscardChildren)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&CanContinueCaoChun(f);
 private void FinishCaoChun(ProgramSkillFrame f){if(f.CaoChun?.Stage==CaoChunStage.InitialSlash&&_players[f.OwnerSeat].IsAlive&&!CompleteProgramEventHistory().OfType<ShanjiaDebtArmedEvent>().Any(e=>e.ActivationFrameId==f.Id))ArmCaoChunDebt(f);ReplaceRuntimeTop(f=f with{CaoChun=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 private void RequireCaoChun(ProgramSkillFrame f)
 {
  if(f.CaoChun is not{}r||r.Source.SkillId!=f.SkillId||r.Source.SkillInstanceId!=f.SkillInstanceId||r.Source.OwnerSeat!=f.OwnerSeat||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.InstructionIndex!=1||r.ActualTurn!=_turnNumber||r.PhaseInstance!=_cardUseDebitPhaseInstanceId||!Enum.IsDefined(r.Stage)||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("Shanjia lost its exact owning source, instruction or actual phase.");
  if(r.Payment is{}p&&(!ValidCaoChunPayment(p)||CompleteProgramEventHistory().OfType<ShanjiaHandUseDebitedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.ActionId==p.Action.ActionId&&e.ActualTurn==r.ActualTurn&&e.PhaseInstance==r.PhaseInstance&&e.RemainingBefore==p.Debt.Remaining&&e.RemainingAfter==p.Debt.Remaining-1)!=1))throw new InvalidOperationException("Shanjia cannot duplicate or borrow a hand-use debit.");
  if(r.Stage is CaoChunStage.DrawChildren or CaoChunStage.DiscardChildren)
  {
   if(f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null,SubjectSeat:var seat}||seat!=f.OwnerSeat||CompleteProgramEventHistory().OfType<ShanjiaMovementPaidEvent>().Count(e=>e==new ShanjiaMovementPaidEvent(f.Id,r.Source,r.GameplayHash,r.Stage,r.Before,r.After,r.ActualCount,r.PaidBatchId))!=1||_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&(r.Stage==CaoChunStage.DrawChildren?m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ShanjiaDrawReason:m.CardId==r.DiscardId&&m.From==r.DiscardFrom&&m.To==CardLocation.DiscardPile&&m.Reason.Value==ShanjiaDiscardReason))!=r.ActualCount)throw new InvalidOperationException("Shanjia lacks its once-paid real movement invoice.");
   if(r.Stage==CaoChunStage.DrawChildren&&(r.ActualCount is <0 or >3||r.PaidBatchId is not null)||r.Stage==CaoChunStage.DiscardChildren&&(r.ActualCount!=1||r.PaidBatchId is not >0||r.DiscardFrom?.OwnerSeat!=f.OwnerSeat||r.DiscardFrom?.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)))throw new InvalidOperationException("Shanjia payment changed its native owned zone or actual count.");
  }
  if(r.Stage==CaoChunStage.NativeSlash&&r.Slash is not{})throw new InvalidOperationException("Shanjia native Slash lost its issued receipt.");
 }

 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 310)]
 private void AssertRegisteredCaoChun(ProgramSkillFrame frame)
 {
     if (frame.CaoChun is not null) RequireCaoChun(frame);
 }
 [ProgramCapability]
 private sealed class CaoChunProgramCapability(GameEngine engine) : ICaoChunProgramHost
 {public SkillProgramStepOutcome ExecuteCaoChun(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.ExecuteCaoChun(f,op);}
}
