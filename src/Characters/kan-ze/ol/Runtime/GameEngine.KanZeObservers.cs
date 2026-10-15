namespace CardGame.Core;
public sealed record KuanshiProtectionGrant(long FrameId,CardConversionSource Source,string GameplayHash,int ArmedTurn,int TargetSeat);
public sealed record KuanshiDrawDebt(long GrantFrameId,int OwnerSeat,int ArmedTurn);
public sealed record KuanshiArmedEvent(KuanshiProtectionGrant Grant):IGameEvent;
public sealed record KuanshiExpiredEvent(long GrantFrameId,int TurnNumber,string Reason):IGameEvent;
public sealed record KuanshiDamagePreventedEvent(long GrantFrameId,long DamageAttemptResolutionId,int? SourceSeat,int TargetSeat,int Amount):IGameEvent;
public sealed record KuanshiDrawSkippedEvent(int OwnerSeat,int TurnNumber):IGameEvent;
public sealed partial class CharacterState
{
 private IReadOnlyList<KuanshiProtectionGrant> _kuanshiGrants=Array.AsReadOnly(Array.Empty<KuanshiProtectionGrant>());
 internal IReadOnlyList<KuanshiProtectionGrant> KuanshiGrants {get=>_kuanshiGrants;set=>_kuanshiGrants=Array.AsReadOnly(value.ToArray());}
 internal KuanshiDrawDebt? KuanshiDebt {get;set;}
}
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 350)]
    private bool AllowsGiveAllHandAndResolveXiashuTrigger(ProgramTriggerLegalityContext gate)
    {
        var features = gate.Features;
        var candidate = gate.Candidate;
        return !(features.HasOperation(SkillProgramEffectOp.GiveAllHandAndResolveXiashu) &&
            !CanOfferXiashu(candidate.OwnerSeat));
    }

 private bool CanOfferXiashu(int ownerSeat)=>_players[ownerSeat].IsAlive&&_cardZones.CardsAt(CardLocation.Hand(ownerSeat)).Count>0;
 private SkillProgramStepOutcome ArmKuanshi(ProgramSkillFrame supplied,int target)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.WindowContext is not {Window:SkillProgramTriggerWindow.TurnEnding,SourceSeat:{} actor}||actor!=f.OwnerSeat||f.SelectedTargetSeats is not [var selected]||selected!=target||!_players[target].IsAlive||!_players[f.OwnerSeat].IsAlive)throw new InvalidOperationException("Kuanshi needs one actual living target at its owner's end phase.");
  var grant=new KuanshiProtectionGrant(f.Id,KanZeSource(f),f.GameplayHash,_turnNumber,target);var owner=_players[f.OwnerSeat];
  owner.KuanshiGrants=owner.KuanshiGrants.Append(grant).ToArray();AdvanceEventRulesAndQueueFact(new KuanshiArmedEvent(grant));return SkillProgramStepOutcome.Continue;
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 440)]
 private void ObserveKuanshiExpiry(IGameEvent payload)
 {
  if(payload is not TurnStartedEvent && payload is not PlayerDiedEvent)return;
  foreach(var owner in _players)
  {
   if(owner.KuanshiGrants.Count==0&&owner.KuanshiDebt is null)continue;
   var started=payload as TurnStartedEvent;var died=payload as PlayerDiedEvent;
   var expired=owner.KuanshiGrants.Where(g=>started is not null&&started.ActorSeat==owner.Seat&&started.TurnNumber>g.ArmedTurn||died is not null&&(died.VictimSeat==owner.Seat||died.VictimSeat==g.TargetSeat)).ToArray();
   if(expired.Length>0){owner.KuanshiGrants=owner.KuanshiGrants.Except(expired).ToArray();foreach(var g in expired)AdvanceEventRulesAndQueueFact(new KuanshiExpiredEvent(g.FrameId,_turnNumber,died is null?"next-owner-turn":"participant-death"));}
   if(died?.VictimSeat==owner.Seat)owner.KuanshiDebt=null;
  }
 }
 private bool TryPreventKuanshiDamage(int? sourceSeat,int targetSeat,int actualAmount,long damageAttemptResolutionId)
 {
  if(CurrentDamageAttempt is not {} attack||attack.ResolutionId!=damageAttemptResolutionId||attack.TargetSeat!=targetSeat||(attack.IsSourceLess||attack.IsDelayedJudgmentDamage?null:(int?)attack.SourceSeat)!=sourceSeat||(!attack.DamageAmountFinalized||attack.DamageAmount!=actualAmount))throw new InvalidOperationException("Kuanshi requires the actual final native damage attempt.");
  if(actualAmount<=1||!IsValidPlayerSeat(targetSeat)||!_players[targetSeat].IsAlive)return false;
  var grants=_players.Where(p=>p.IsAlive&&p.KuanshiGrants.Count>0).OrderBy(p=>p.Seat).SelectMany(p=>p.KuanshiGrants).Where(g=>g.TargetSeat==targetSeat).ToArray();if(grants.Length==0)return false;
  foreach(var g in grants)
  {
   if(CompleteProgramEventHistory().OfType<KuanshiArmedEvent>().Count(e=>e.Grant==g)!=1)throw new InvalidOperationException("Kuanshi lost its actual permanent rule grant.");
   var owner=_players[g.Source.OwnerSeat];owner.KuanshiDebt??=new(g.FrameId,owner.Seat,g.ArmedTurn);
   AdvanceEventRulesAndQueueFact(new KuanshiDamagePreventedEvent(g.FrameId,damageAttemptResolutionId,sourceSeat,targetSeat,actualAmount));
  }
  return true;
 }
 private bool ConsumeKuanshiDrawSkip(int ownerSeat)
 {
  var owner=_players[ownerSeat];if(owner.KuanshiDebt is not {} debt||!owner.IsAlive||_turnNumber<=debt.ArmedTurn)return false;
  owner.KuanshiDebt=null;AdvanceEventRulesAndQueueFact(new KuanshiDrawSkippedEvent(ownerSeat,_turnNumber));return true;
 }
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 100)]
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 100)]
 private bool CanContinueIssuedKanZe(ProgramSkillFrame f)
 {if(f.XiashuPaid is null)return false;RequireIssuedKanZe(f);return true;}
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 160)]
 private void RequireIssuedKanZe(ProgramSkillFrame f)
 {
  if(f.XiashuPaid is not {} r)return;var i=r.Invoice;
  if(i.Source!=KanZeSource(f)||i.GameplayHash!=f.GameplayHash||i.WindowId!=f.WindowContext?.ParentFrameId||i.InstructionIndex!=f.InstructionIndex||i.TurnNumber!=_turnNumber||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.GiveAllHandAndResolveXiashu||i.GiftCount<=0||i.GiftAfter<=i.GiftBefore||r.Before>r.After||r.After>_movementSequence||!Enum.IsDefined(r.Stage)||f.SelectedTargetSeats is not [var target]||target!=i.RecipientSeat)throw new InvalidOperationException("Xiashu lost its frozen original invoice.");
  if(r.SelectedReveal.Count!=r.SelectedReveal.Distinct().Count())throw new InvalidOperationException("Xiashu reveal selection contains duplicate entities.");
  var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
  if(index<1||_resolutionStack[index-1] is not PlayPhaseStartingBoundaryFrame w||w.Id!=i.WindowId||w.OwnerSeat!=f.OwnerSeat||w.ItemIndex<0||w.ItemIndex>=w.Items.Count||w.Items[w.ItemIndex].Candidate is not {} candidate||candidate.OwnerSeat!=f.OwnerSeat||candidate.SkillId!=f.SkillId||candidate.SkillInstanceId!=f.SkillInstanceId||candidate.BindingId!=f.TriggerId||candidate.GameplayHash!=f.GameplayHash)throw new InvalidOperationException("Xiashu lost its native selected boundary candidate.");
  var gift=GetProgramCardSet(f,XiashuGiftBind).CardIds;
  if(gift.Count!=i.GiftCount||CompleteProgramEventHistory().OfType<XiashuGiftPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Invoice==i)!=1||gift.Any(id=>_cardMovements.Count(m=>m.Sequence>i.GiftBefore&&m.Sequence<=i.GiftAfter&&m.CardId==id&&m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.Hand(i.RecipientSeat)&&m.Reason.Value==XiashuGiftReason)!=1))throw new InvalidOperationException("Xiashu whole-hand gift was not paid exactly once.");
  if(r.Stage==XiashuStage.GiftChildren&&(r.Before!=i.GiftBefore||r.After!=i.GiftAfter||r.Count!=i.GiftCount))throw new InvalidOperationException("Xiashu gift stage differs from its original payment.");
  if(r.Stage is XiashuStage.RecipientReveal or XiashuStage.OwnerClaim)
  {
   if(f.PendingMovementContinuation is not null)throw new InvalidOperationException("Xiashu private choice cannot masquerade as movement.");
   if(_resolutionStack.LastOrDefault()?.Id==f.Id&&_pendingDecision is {} decision&&(decision.PlayerSeat!=(r.Stage==XiashuStage.OwnerClaim?f.OwnerSeat:i.RecipientSeat)||!decision.IsPrivate||!AssistedChoicesEqual(decision.Choices,XiashuChoices(f))))throw new InvalidOperationException("Xiashu lost its actual private chooser.");return;
  }
  if(f.PendingMovementContinuation is not {BeforeCount:0,CoverageResultBind:null,SubjectSeat:var actor}||actor!=f.OwnerSeat)throw new InvalidOperationException("Xiashu lost its native movement continuation.");
  if(r.Stage==XiashuStage.ClaimChildren&&(GetProgramCardSet(f,XiashuClaimBind).CardIds.Count!=r.Count||CompleteProgramEventHistory().OfType<XiashuClaimPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Before==r.Before&&e.After==r.After&&e.Count==r.Count)!=1||GetProgramCardSet(f,XiashuClaimBind).CardIds.Any(id=>_cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==id&&m.From==CardLocation.Hand(i.RecipientSeat)&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==XiashuClaimReason)!=1)))throw new InvalidOperationException("Xiashu claim invoice changed.");
 }
 private bool IsXiashuMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.XiashuPaid is {Stage:XiashuStage.GiftChildren or XiashuStage.ClaimChildren}&&effect?.Op==SkillProgramEffectOp.GiveAllHandAndResolveXiashu&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&CanContinueIssuedKanZe(f);
 private bool KanZeFirstChild(ProgramSkillFrame root,ResolutionFrame child)
 {
  if(!CanContinueIssuedKanZe(root)||root.XiashuPaid is not {} r||child is not CardsMovedTriggerWindowFrame moved)return false;
  return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==(r.Stage==XiashuStage.GiftChildren?XiashuGiftReason:XiashuClaimReason)&&m.From==CardLocation.Hand(r.Stage==XiashuStage.GiftChildren?root.OwnerSeat:r.Invoice.RecipientSeat)&&m.To==CardLocation.Hand(r.Stage==XiashuStage.GiftChildren?r.Invoice.RecipientSeat:root.OwnerSeat));
 }
 private ProgramSkillFrame? KanZeObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.KanZeFirstChild(r,c),static(e,i)=>e.HasMovementObserverSuffix(i));
 private bool HasKanZeBeforeDamageObserver(long id)=>KanZeObserverRoot() is {} root&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool HasKanZeDamageObserver(long id)=>KanZeObserverRoot() is {} root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 private bool IsKanZeDying()=>ActiveDying is {} d&&KanZeObserverRoot() is {} root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 160)]
 private bool TryAdvanceKanZeSubtree()=>_pendingDecision is null&&KanZeObserverRoot() is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
 private bool AllowsKanZeNestedDamage(ProgramSkillFrame o,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
 {if(sourceLess||o.AttackAttempt is not null||o.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=o.Id||KanZeObserverRoot() is not {} root||root.Id==o.Id)return false;var e=GetPausedPrivateOfferEffect(o);return e is {Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is {} p?ResolveProgramParticipant(o,p):ResolveProgramEffectTarget(o,e.Target));}
}
