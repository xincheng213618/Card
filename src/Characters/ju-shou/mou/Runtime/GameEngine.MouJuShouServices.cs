namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouJuShouRuntime MouJuShou=>GetRuntimeModule<MouJuShouRuntime>();
 [ProgramCapability]private sealed class MouJuShouFacts(GameEngine e):IMouJuShouFacts
 {
  public int Turn=>e._turnNumber;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;
  public bool Alive(int seat)=>e.IsValidPlayerSeat(seat)&&e._players[seat].IsAlive;public bool Human(int seat)=>e._players[seat].IsHuman;
  public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
  public bool Parent(ProgramSkillFrame f)
  {
   if(f.SkillId==MouJuShouRuntime.ZuoJun)return f.TriggerId is null&&f.ActivationId=="draw-and-choose"&&f.WindowContext is null&&f.ActivatingSeat is null&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==1&&History<ProgramSkillStartedEvent>().Count(x=>x.FrameId==f.Id&&x.OwnerSeat==f.OwnerSeat&&x.SkillId==f.SkillId&&x.ActivationId==f.ActivationId)==1;
   return e.MouJsMovementParent(f);
  }
  public bool InHand(int seat,int id)=>e._cardZones.GetLocation(id)==CardLocation.Hand(seat);
  public bool InDiscard(int id)=>e._cardZones.GetLocation(id)==CardLocation.DiscardPile;public bool Reclaimable(MouJsFirstLoss loss,CardMovementRecord discarded)=>e.MouJsReclaimable(loss,discarded);public bool ReadyToReclaim(MouJsFirstLoss loss)=>InDiscard(loss.CardId)&&e._cardMovements.LastOrDefault(m=>m.CardId==loss.CardId)is{}last&&e.MouJsReclaimable(loss,last);public MouJsMuwangQueuedEvent? MuwangOpportunity(ProgramSkillFrame f)=>e.MouJsMuwangOpportunity(f);
  public IReadOnlyList<int> Discardable(int seat)=>!Alive(seat)?[]:Array.AsReadOnly(new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.SelectMany(z=>e._cardZones.CardsAt(new(z,seat)).Where(c=>!e.IsForeignEquipmentDiscardPrevented(seat,c,new(z,seat),OwnedCardMoveIntent.Discard)&&!e.IsSelfHandCategoryDiscardForbidden(seat,c,new(z,seat),OwnedCardMoveIntent.Discard))).Select(c=>c.Id).ToArray());
  public string Label(int id)=>e.GetAdvancedCard(id).DisplayName;public IReadOnlyList<MouJsPlan> Plans(ProgramSkillFrame f)=>e.MouJsPlans(f);
  public IReadOnlyList<MouJsZuoJunGrant> Grants(int seat)=>e._players[seat].MouJsZuoJunGrants??[];public IReadOnlyList<MouJsFirstLoss> Losses(int seat)=>e._players[seat].MouJsFirstLosses??[];
  public IReadOnlyList<int> Seats=>Array.AsReadOnly(e._players.Select(p=>p.Seat).ToArray());
  public IReadOnlyList<(CardConversionSource Source,string Hash)> MuwangSources(int seat)=>!Alive(seat)?[]:Array.AsReadOnly(e.EnabledSkillPrograms(e._players[seat]).Where(p=>p.Id==MouJuShouRuntime.Muwang).Select(p=>(new CardConversionSource(p.Id,"first-loss",seat,e.GetRuntimeSkillInstanceId(e._players[seat],p.Id)),p.GameplayHash)).ToArray());
  public bool Eligible(int id){var kind=e.GetAdvancedCard(id).Kind;return GetProgramCardCategory(kind)==SkillProgramCardCategory.Basic||IsOrdinaryTrick(kind);}
 }
 [ProgramCapability]private sealed class MouJuShouNative(GameEngine e):IMouJuShouNative
 {
  public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);public void Publish()=>e.AdvanceRulesAndPublishState();
  public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,f.MouJuShou?.Stage==MouJuShouStage.HealthChildren?PostEventContinuation.Program:PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
  public SkillProgramStepOutcome LoseHp(ProgramSkillFrame f)=>e.ApplyProgramHpLoss(f.Id,f.SkillId,f.MouJuShou!.Actor,1);
  public void Use(ProgramSkillFrame f)=>e.IssueMouJsUse(f);
  public void SetGrants(int seat,IReadOnlyList<MouJsZuoJunGrant> grants)=>e._players[seat].MouJsZuoJunGrants=Array.AsReadOnly(grants.ToArray());
  public void SetLosses(int seat,IReadOnlyList<MouJsFirstLoss> losses)=>e._players[seat].MouJsFirstLosses=Array.AsReadOnly(losses.ToArray());
  public void Discard(ProgramSkillFrame f)
  {
   if(!e.MouJuShou.Valid(f)||f.MouJuShou is not{Stage:MouJuShouStage.DiscardChildren or MouJuShouStage.PenaltyChildren}r||r.Discard.Any(id=>!new MouJuShouFacts(e).Discardable(r.Actor).Contains(id)))throw new InvalidOperationException("Mou Ju Shou requires actual payable bound leftovers or a selected penalty entity.");
   e.MoveProgramLogicalDiscard(f,r.Actor,r.Discard,new("skill-program."+f.SkillId+".discard"),(_,moves)=>
   {
    var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouJuShou!;if(!e.MouJuShou.Valid(parent)||moves.Count!=r.Discard.Count||moves.Any(m=>!r.Discard.Contains(m.CardId)||m.From.OwnerSeat!=r.Actor||m.Sequence<=r.Before||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Mou Ju Shou lost his actual native logical-discard receipt.");
    e.ReplaceRuntimeFrame(f.Id,parent with{MouJuShou=paid with{After=moves.Count>0?moves.Max(m=>m.Sequence):r.Before}});foreach(var m in moves)e.AdvanceEventRulesAndQueueFact(new MouJsMovedEvent(f.Id,m.CardId,m.From,m.To,m.Sequence));
   });
  }
  public void Recover(ProgramSkillFrame f)
  {
   if(!e.MouJuShou.Valid(f)||f.MouJuShou is not{Stage:MouJuShouStage.RecoverChildren,Recover:true,CardId:>0}r||new MouJuShouFacts(e).Losses(r.Actor).SingleOrDefault(l=>l.Source==r.Source&&l.Turn==r.Turn&&l.CardId==r.CardId&&l.FirstSequence==r.LossSequence)is not{}loss||!new MouJuShouFacts(e).ReadyToReclaim(loss))throw new InvalidOperationException("Muwang requires the original tracked entity actually in Discard.");
   e.MoveProgramCardsFromMultipleSources([r.CardId],CardLocation.Hand(r.Actor),new("skill-program.mou:muwang.return"),(_,moves)=>{var parent=e.GetActiveProgramFrame(f.Id);if(moves is not[var m]||m.CardId!=r.CardId||m.From!=CardLocation.DiscardPile||m.Sequence<=r.Before||m.To!=CardLocation.Hand(r.Actor)&&m.To!=CardLocation.OutsideGame)throw new InvalidOperationException("Muwang cannot invent a returned entity.");e.ReplaceRuntimeFrame(f.Id,parent with{MouJuShou=parent.MouJuShou! with{After=m.Sequence}});if(m.To==CardLocation.Hand(r.Actor))e.MouJuShou.Recovered(f.Id,m);e.AdvanceEventRulesAndQueueFact(new MouJsMovedEvent(f.Id,m.CardId,m.From,m.To,m.Sequence));});
  }
 }
 private void ObserveMouJsMovement(CardMovementRecord movement)=>MouJuShou.ObserveMovement(movement);
 private bool IsMouJsHandLimitExempt(int actor,int card)=>MouJsHasGrant(actor,card);
 private bool HasMouJsEntityMark(int card)=>_players.Any(p=>MouJsHasGrant(p.Seat,card));
 private bool MouJsHasGrant(int actor,int card)=>IsValidPlayerSeat(actor)&&_players[actor].MouJsZuoJunGrants?.Any(g=>g.Recipient==actor&&g.CardId==card&&_cardZones.GetLocation(card)==CardLocation.Hand(actor))==true;
 private bool MouJsUsePurpose(int actor,CardKind kind,bool response,bool dying=false)
 {
  if(!response||dying||kind==CardKind.Nullification)return true;
  if(IsMouSmPlayedActor(_players[actor])||_resolutionStack.LastOrDefault()is ProgramSkillFrame{MouLuSuAnniversary:{Yin:true,Operation:SkillProgramEffectOp.ResolveMouLuSuMengmou}played}f&&played.Other==actor&&MouLuSuAnniversary.Valid(f))return false;
  return IsProgramResponseCardUse(_players[actor],kind)||IsSlashCard(kind)&&(ActiveBorrowedSword is{AwaitingSlashChoice:true}b&&b.WeaponOwnerSeat==actor||ActiveQinglongCrescentBlade is{}q&&q.Attack.SourceSeat==actor);
 }
 [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan,36900)]private bool MouJsMaterialBan(ProgramCardMaterialQuery q)=>MouJsUsePurpose(q.ActorSeat,q.Kind,q.IsResponse)&&q.CardIds.Any(id=>MouJsHasGrant(q.ActorSeat,id));
 [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan,36900)]private bool MouJsResponseBan(ProgramNativeResponseQuery q)=>MouJsUsePurpose(q.Provider.Seat,q.Kind,true,q.DyingUse)&&q.CardIds.Any(id=>MouJsHasGrant(q.Provider.Seat,id));
 [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan,36900)]private bool MouJsViewAsBan(ProgramViewAsLegalityQuery q)=>MouJsUsePurpose(q.Owner.Seat,q.Kind,q.ForResponse,q.DyingUse)&&q.CardIds.Any(id=>MouJsHasGrant(q.Owner.Seat,id));
 [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind,36900)]private bool MouJsActionBan(ProgramLegalActionQuery q)=>q.Action.Kind!=LegalActionKind.Recast&&q.Action.CardId is{}id&&MouJsHasGrant(q.Actor.Seat,id);
 [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation,36900)]private void MouJsSubmittedUse(ProgramSubmittedUseQuery q){if(q.CardIds.Any(id=>MouJsHasGrant(q.ActorSeat,id)))throw new InvalidOperationException("A currently held Zuo Jun entity cannot be used.");}
}
