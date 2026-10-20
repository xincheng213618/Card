using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record XionghuoGrant(CardConversionSource Source,string GameplayHash,long GrantFrameId,int TargetSeat,int Count);
public sealed record XuRongTurnRestriction(CardConversionSource Source,long PaidFrameId,int SubjectSeat,int ActualTurnNumber,int ActualTurnOwner,int PhaseInstanceId,int Branch);
public sealed record XuRongCausalEntity(int CardId,CardLocation OriginalLocation);
public sealed record XuRongDyingProof(long DyingFrameId,long ParentFrameId,int VictimSeat,int HpAtEntry,int RescueCardsRequired,long? DamageAttemptResolutionId,long? NativeActionId,int? CauseSourceSeat,bool SourceLess,IReadOnlyList<XuRongCausalEntity> Entities);
public sealed record XuRongPending(CardConversionSource Source,string GameplayHash,string Stage,int InstructionIndex,long ParentFrameId,int SubjectSeat,int ActualTurnNumber,int PhaseInstanceId,IReadOnlyList<XionghuoGrant> Grants,uint RandomBefore,uint RandomAfter,int Branch,long MovementBefore,long MovementAfter,IReadOnlyList<int> PaidCardIds,long? DyingFrameId=null,long? NativeActionId=null);
public sealed record XionghuoPoolChangedEvent(long FrameId,CardConversionSource Source,int Before,int After,string Kind):IGameEvent;
public sealed record XionghuoMarkTransferredEvent(long FrameId,XionghuoGrant Grant,int PoolBefore,int PoolAfter,int TargetBefore,int TargetAfter):IGameEvent;
public sealed record XionghuoPlayDebtPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long PlayWindowId,int SubjectSeat,int ActualTurnNumber,int PhaseInstanceId,int RemovedCount,uint RandomBefore,uint RandomAfter,int Branch):IGameEvent;
public sealed record XuRongCardsPaidEvent(long FrameId,CardConversionSource Source,string Kind,long Before,long After,IReadOnlyList<int> CardIds):IGameEvent;
public sealed record XuRongCompletedEvent(long FrameId,CardConversionSource Source,string Stage,int SubjectSeat,long? DyingFrameId):IGameEvent;
public sealed record XuRongCausalClaimReceipt(long ProducerFrameId,CardConversionSource Source,string GameplayHash,long DyingFrameId,long NativeOwnerId,long? NativeActionId,int HpAtEntry,int RescueCardsRequired,IReadOnlyList<int> CardIds);
public sealed record XuRongCausalClaimIssuedEvent(XuRongCausalClaimReceipt Receipt):IGameEvent;
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<XuRongCausalClaimReceipt>? XuRongCausalClaims{get;init;}
}
public sealed partial record JudgmentFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<XuRongCausalClaimReceipt>? XuRongCausalClaims{get;init;}
}
public sealed partial class CharacterState
{
 private readonly List<XionghuoGrant> _xionghuoGrants=[];
 private readonly List<XuRongTurnRestriction> _xuRongRestrictions=[];
 internal IReadOnlyList<XionghuoGrant> XionghuoGrants=>_xionghuoGrants.AsReadOnly();
 internal IReadOnlyList<XuRongTurnRestriction> XuRongRestrictions=>_xuRongRestrictions.AsReadOnly();
 internal void AddXionghuoGrant(XionghuoGrant grant)=>_xionghuoGrants.Add(grant);
 internal bool RemoveXionghuoGrant(XionghuoGrant grant)=>_xionghuoGrants.Remove(grant);
 internal void AddXuRongRestriction(XuRongTurnRestriction r)=>_xuRongRestrictions.Add(r);
 internal void ExpireXuRongRestrictions(int turn)=>_xuRongRestrictions.RemoveAll(r=>r.ActualTurnNumber<=turn);
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XuRongPending? XuRong{get;init;}
}
public sealed partial record DyingFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XuRongDyingProof? XuRongProof{get;init;}
}
public sealed partial record ProgramSkillWindowContext
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XuRongDyingProof? XuRongDying{get;init;}
}
public sealed partial class GameEngine
{
 private const string XuRongTakeReason="program.xionghuo.random-take",XuRongClaimReason="program.shajue2023.claim";
 private CardConversionSource XuRongSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private int XuRongPool(int owner)=>_players[owner].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Baoli,owner));
 private IEnumerable<XionghuoGrant> XuRongSubjectGrants(int owner,int subject)=>_players[subject].XionghuoGrants.Where(g=>g.TargetSeat==subject&&g.Source.OwnerSeat==owner&&LiveXionghuoSource(g));
 private int XuRongHandLimitPenalty(CharacterState owner)=>owner.XuRongRestrictions.Count(r=>r.Branch==1&&r.SubjectSeat==owner.Seat&&r.ActualTurnNumber==_turnNumber&&r.ActualTurnOwner==_currentSeat);
 private bool IsXuRongSlashTargetForbidden(int actor,int target,CardKind kind)=>IsSlashCard(kind)&&_players[actor].XuRongRestrictions.Any(r=>r.Branch==0&&r.SubjectSeat==actor&&r.Source.OwnerSeat==target&&r.ActualTurnNumber==_turnNumber&&r.ActualTurnOwner==_currentSeat);
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 470)]
 private void ObserveXuRongTurnExpiry(IGameEvent payload)
 {if(payload is TurnEndedEvent t)foreach(var p in _players)p.ExpireXuRongRestrictions(t.TurnNumber);}
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 430)]
 private bool CanOfferXuRongLifecycle(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
 {
  var t=GetProgramTrigger(candidate);if(t.Effects.Count!=1)return true;
  return t.Effects[0].Op switch
  {
   SkillProgramEffectOp.InitializeXionghuoPool=>context.Window==SkillProgramTriggerWindow.GameStarting&&!CompleteProgramEventHistory().OfType<XionghuoPoolChangedEvent>().Any(e=>e.Source.OwnerSeat==candidate.OwnerSeat&&e.Kind=="initialize"),
   SkillProgramEffectOp.SettleXionghuoMarkedPlay=>context.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&context.SourceSeat is {} subject&&subject!=candidate.OwnerSeat&&XuRongSubjectGrants(candidate.OwnerSeat,subject).Any(),
   SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause=>context.Window==SkillProgramTriggerWindow.DyingEntering&&context.TargetSeat is {} victim&&victim!=candidate.OwnerSeat&&context.XuRongDying is {} proof&&proof.VictimSeat==victim,
   _=>true
  };
 }
 private bool CanTargetXuRong(int owner,int target,SkillProgramActivation activation)=>!activation.Effects.Any(e=>e.Op==SkillProgramEffectOp.TransferXionghuoMark)||target!=owner&&_players[target].IsAlive&&_players[target].Markers.GetValueOrDefault(PlayerMarkerKind.Baoli)<3;
 [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 60)]
 private bool CanActivateXuRong(int owner,SkillProgramActivation activation)=>!activation.Effects.Any(e=>e.Op==SkillProgramEffectOp.TransferXionghuoMark)||XuRongPool(owner)>0&&_players.Any(p=>p.Seat!=owner&&p.IsAlive&&p.Markers.GetValueOrDefault(PlayerMarkerKind.Baoli)<3);
 private SkillProgramStepOutcome ExecuteXuRong(SkillProgramEffectOp op,ProgramSkillFrame f)=>op switch
 {
  SkillProgramEffectOp.InitializeXionghuoPool=>InitializeXuRong(f),
  SkillProgramEffectOp.TransferXionghuoMark=>TransferXuRong(f),
  SkillProgramEffectOp.SettleXionghuoMarkedPlay=>SettleXuRong(f),
  SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause=>ClaimXuRong(f),
  _=>throw new InvalidOperationException("Unknown Xu Rong operation.")
 };
 private SkillProgramStepOutcome InitializeXuRong(ProgramSkillFrame f)
 {
  var before=XuRongPool(f.OwnerSeat);if(f.WindowContext?.Window!=SkillProgramTriggerWindow.GameStarting||before!=0)throw new InvalidOperationException("Xu Rong initial pool requires one actual unpaid GameStart.");
  MutateParticipantMarker(f,f.OwnerSeat,PlayerMarkerKind.Baoli,3);AdvanceEventRulesAndQueueFact(new XionghuoPoolChangedEvent(f.Id,XuRongSource(f),before,3,"initialize"));return SkillProgramStepOutcome.Continue;
 }
 private SkillProgramStepOutcome TransferXuRong(ProgramSkillFrame f)
 {
  if(f.TriggerId is not null||f.SelectedTargetSeats is not [var target]||target==f.OwnerSeat||!_players[target].IsAlive||XuRongPool(f.OwnerSeat)<1||_players[target].Markers.GetValueOrDefault(PlayerMarkerKind.Baoli)>=3||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Xionghuo needs one actual owned pool marker and a real other target.");
  var before=XuRongPool(f.OwnerSeat);var targetBefore=_players[target].Markers.GetValueOrDefault(PlayerMarkerKind.Baoli);RemoveXuRongTargetMarkers(f,f.OwnerSeat,f.OwnerSeat,1);MutateParticipantMarker(f,target,PlayerMarkerKind.Baoli,1);
  var grant=new XionghuoGrant(XuRongSource(f),f.GameplayHash,f.Id,target,1);_players[target].AddXionghuoGrant(grant);AdvanceEventRulesAndQueueFact(new XionghuoMarkTransferredEvent(f.Id,grant,before,before-1,targetBefore,targetBefore+1));return SkillProgramStepOutcome.Continue;
 }
 private SkillProgramStepOutcome SettleXuRong(ProgramSkillFrame input)
 {
  var f=GetActiveProgramFrame(input.Id);if(f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting,SourceSeat:{}subject} w||subject==f.OwnerSeat||subject!=_currentSeat||f.InstructionIndex!=1)throw new InvalidOperationException("Xionghuo requires the actual marked foreign Play boundary.");
  var grants=XuRongSubjectGrants(f.OwnerSeat,subject).ToArray();if(grants.Length==0||f.XuRong is not null)throw new InvalidOperationException("Xionghuo phase debt has no unpaid source-attributed mark.");
  var before=_random.State;var branch=_random.Next(3);var after=_random.State;
  foreach(var g in grants){_players[subject].RemoveXionghuoGrant(g);RemoveXuRongTargetMarkers(f,subject,g.Source.OwnerSeat,g.Count);}
  var source=XuRongSource(f);var r=new XuRongPending(source,f.GameplayHash,"phase-paid",f.InstructionIndex,w.ParentFrameId,subject,_turnNumber,_cardUseDebitPhaseInstanceId,Array.AsReadOnly(grants),before,after,branch,_movementSequence,_movementSequence,Array.AsReadOnly(Array.Empty<int>()));ReplaceRuntimeTop(f=f with{XuRong=r});
  AdvanceEventRulesAndQueueFact(new XionghuoPlayDebtPaidEvent(f.Id,source,f.GameplayHash,w.ParentFrameId,subject,r.ActualTurnNumber,r.PhaseInstanceId,grants.Sum(g=>g.Count),before,after,branch));
  if(branch is 0 or 1)_players[subject].AddXuRongRestriction(new(source,f.Id,subject,r.ActualTurnNumber,_currentSeat,r.PhaseInstanceId,branch));
  ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id)with{XuRong=r with{Stage=branch==0?"fire-native":branch==1?"hp-native":"take-native"}});
  if(branch==0){BeginProgramSkillDamage(f,subject,1,nature:DamageNature.Fire);return SkillProgramStepOutcome.AwaitChild;}
  if(branch==1){new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,subject,1);AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  TakeXuRongRandomCards(f);return SkillProgramStepOutcome.AwaitChild;
 }
 private void RemoveXuRongTargetMarkers(ProgramSkillFrame f,int subject,int source,int count)
 {
  var target=_players[subject];var key=(PlayerMarkerKind.Baoli,source);var actual=target.MarkerSourceCounts.GetValueOrDefault(key);if(count<1||actual<count)throw new InvalidOperationException("Xionghuo removed a marker not owned by this actual source.");
  if(actual==count)target.MarkerSourceCounts.Remove(key);else target.MarkerSourceCounts[key]=actual-count;var total=target.Markers[PlayerMarkerKind.Baoli]-count;if(total==0)target.Markers.Remove(PlayerMarkerKind.Baoli);else target.Markers[PlayerMarkerKind.Baoli]=total;AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,subject,PlayerMarkerKind.Baoli,-count,total,source,"program.xionghuo.consume"));
 }
 private void TakeXuRongRandomCards(ProgramSkillFrame f)
 {
  var r=f.XuRong!;var subject=_players[r.SubjectSeat];var hand=GetHand(subject).OrderBy(c=>c.Id).ToArray();var equip=GetEquipment(subject).OrderBy(c=>c.Id).ToArray();var ids=new List<int>();if(hand.Length>0)ids.Add(hand[_random.Next(hand.Length)].Id);if(equip.Length>0)ids.Add(equip[_random.Next(equip.Length)].Id);IssueXuRongMove(f,ids,XuRongTakeReason);
 }
 private SkillProgramStepOutcome ClaimXuRong(ProgramSkillFrame f)
 {
  if(f.XuRong is not null||f.WindowContext is not {Window:SkillProgramTriggerWindow.DyingEntering,XuRongDying:{}proof}w||ActiveDying is not {} dying||dying.Id!=proof.DyingFrameId||dying.XuRongProof!=proof||proof.VictimSeat==f.OwnerSeat||f.InstructionIndex!=1)throw new InvalidOperationException("ShaJue2023 requires the actual other native dying entry and its original frozen rescue requirement.");
  var before=XuRongPool(f.OwnerSeat);var gained=Math.Max(0,Math.Min(1,3-_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Baoli)));if(gained>0)MutateParticipantMarker(f,f.OwnerSeat,PlayerMarkerKind.Baoli,gained);var source=XuRongSource(f);AdvanceEventRulesAndQueueFact(new XionghuoPoolChangedEvent(f.Id,source,before,before+gained,"dying"));
  var r=new XuRongPending(source,f.GameplayHash,"claim-native",f.InstructionIndex,w.ParentFrameId,proof.VictimSeat,_turnNumber,_cardUseDebitPhaseInstanceId,Array.AsReadOnly(Array.Empty<XionghuoGrant>()),0,0,-1,_movementSequence,_movementSequence,Array.AsReadOnly(Array.Empty<int>()),proof.DyingFrameId,proof.NativeActionId);ReplaceRuntimeTop(f=f with{XuRong=r});
  var ids=proof.RescueCardsRequired>1?proof.Entities.Where(e=>_cardZones.GetLocation(e.CardId)==e.OriginalLocation).Select(e=>e.CardId).ToArray():[];if(ids.Length>0)ArmXuRongCausalClaim(f,proof,ids);IssueXuRongMove(f,ids,XuRongClaimReason);return SkillProgramStepOutcome.AwaitChild;
 }
 private void ArmXuRongCausalClaim(ProgramSkillFrame f,XuRongDyingProof proof,IReadOnlyList<int> ids)
 {
  if(proof.RescueCardsRequired<=1||proof.DamageAttemptResolutionId is not {} nativeId||ids.Any(id=>!proof.Entities.Any(e=>e.CardId==id&&_cardZones.GetLocation(id)==e.OriginalLocation)))throw new InvalidOperationException("ShaJue cannot claim unrelated entities or a later dying requirement.");
  var receipt=new XuRongCausalClaimReceipt(f.Id,XuRongSource(f),f.GameplayHash,proof.DyingFrameId,nativeId,proof.NativeActionId,proof.HpAtEntry,proof.RescueCardsRequired,Array.AsReadOnly(ids.ToArray()));
  var native=_resolutionStack.SingleOrDefault(x=>x.Id==nativeId)??throw new InvalidOperationException("ShaJue causal native owner disappeared before payment.");
  if(native is CardUseFrame{Action:{}action}use&&action.ActionId==proof.NativeActionId&&ids.All(id=>action.PhysicalCards.Any(c=>c.CardId==id)))UpdateLifecycleCardUse(use.Id,current=>current with{XuRongCausalClaims=Array.AsReadOnly((current.XuRongCausalClaims??[]).Append(receipt).ToArray())});
  else if(native is JudgmentFrame{Continuation:JudgmentContinuationKind.Lightning,DelayedCardId:{}held}judgment&&ids.SequenceEqual(new[]{held}))ReplaceRuntimeFrame(judgment.Id,judgment with{XuRongCausalClaims=Array.AsReadOnly((judgment.XuRongCausalClaims??[]).Append(receipt).ToArray())});
  else throw new InvalidOperationException("ShaJue causal entities do not belong to the exact paid native card or held Lightning.");
  AdvanceEventRulesAndQueueFact(new XuRongCausalClaimIssuedEvent(receipt));
 }
 private bool IsXuRongCausalEntityClaim(long nativeOwnerId,int cardId)
 {
  var native=_resolutionStack.SingleOrDefault(f=>f.Id==nativeOwnerId);var claims=native switch{CardUseFrame c=>c.XuRongCausalClaims,JudgmentFrame j=>j.XuRongCausalClaims,_=>null};if(claims is null)return false;
  return claims.Any(r=>r.NativeOwnerId==nativeOwnerId&&r.HpAtEntry<0&&r.RescueCardsRequired==1-r.HpAtEntry&&r.RescueCardsRequired>1&&_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash==r.GameplayHash&&r.CardIds.Contains(cardId)&&CompleteProgramEventHistory().OfType<XuRongCausalClaimIssuedEvent>().Count(e=>(e.Receipt with{CardIds=r.CardIds})==r&&e.Receipt.CardIds.SequenceEqual(r.CardIds))==1&&CompleteProgramEventHistory().OfType<XuRongCardsPaidEvent>().Count(e=>e.FrameId==r.ProducerFrameId&&e.Source==r.Source&&e.Kind==XuRongClaimReason&&e.CardIds.SequenceEqual(r.CardIds))==1&&CompleteProgramEventHistory().OfType<XuRongCardsPaidEvent>().Single(e=>e.FrameId==r.ProducerFrameId&&e.Source==r.Source&&e.Kind==XuRongClaimReason&&e.CardIds.SequenceEqual(r.CardIds)) is {} paid&&r.CardIds.All(id=>_cardMovements.Count(m=>m.Sequence>paid.Before&&m.Sequence<=paid.After&&m.CardId==id&&m.Reason.Value==XuRongClaimReason&&m.To==CardLocation.Hand(r.Source.OwnerSeat))==1));
 }
 private void IssueXuRongMove(ProgramSkillFrame f,IReadOnlyList<int> ids,string reason)
 {
  var r=f.XuRong!;var paid=_players[f.OwnerSeat].IsAlive?ids.Where(id=>_cardZones.GetLocation(id).Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Processing or CardZoneKind.Judgment or CardZoneKind.DiscardPile).ToArray():[];var before=_movementSequence;ReplaceRuntimeTop(f=f with{XuRong=r with{MovementBefore=before,MovementAfter=before,PaidCardIds=Array.AsReadOnly(paid)},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(paid.Length>0)MoveProgramCardsFromMultipleSources(paid,CardLocation.Hand(f.OwnerSeat),new(reason));f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{XuRong=f.XuRong!with{MovementAfter=_movementSequence}});AdvanceEventRulesAndQueueFact(new XuRongCardsPaidEvent(f.Id,r.Source,reason,before,_movementSequence,Array.AsReadOnly(paid)));AdvanceRuntimeProgram(f.Id);
 }
 [ProgramCapability]
 private sealed class XuRongProgramCapability(GameEngine engine) : IXuRongProgramHost
 {public SkillProgramStepOutcome ExecuteXuRong(SkillProgramEffectOp op,ProgramSkillFrame frame)=>engine.ExecuteXuRong(op,frame);}
}
