using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum XiashuStage { GiftChildren,RecipientReveal,OwnerClaim,ClaimChildren }
public sealed record XiashuInvoice(CardConversionSource Source,string GameplayHash,long WindowId,int InstructionIndex,int TurnNumber,int RecipientSeat,long GiftBefore,long GiftAfter,int GiftCount);
public sealed record XiashuPaidPending(XiashuInvoice Invoice,XiashuStage Stage,long Before,long After,int Count,IReadOnlyList<int>? SelectedReveal=null)
{
 private readonly IReadOnlyList<int> _selectedReveal=Array.AsReadOnly((SelectedReveal??[]).ToArray());
 public IReadOnlyList<int> SelectedReveal {get=>_selectedReveal;init=>_selectedReveal=Array.AsReadOnly((value??[]).ToArray());}
}
public sealed record XiashuGiftPaidEvent(long FrameId,XiashuInvoice Invoice):IGameEvent;
public sealed record XiashuClaimPaidEvent(long FrameId,long Before,long After,int Count,bool Shown):IGameEvent;
public sealed record XiashuCompletedEvent(long FrameId,int GiftCount,int ClaimCount):IGameEvent;
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public XiashuPaidPending? XiashuPaid{get;init;}}
public sealed partial class GameEngine
{
 private const string XiashuGiftBind="xiashu.gift",XiashuShownBind="xiashu.shown",XiashuClaimBind="xiashu.claim",XiashuGiftReason="program.xiashu.gift",XiashuClaimReason="program.xiashu.claim";
 private CardConversionSource KanZeSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private SkillProgramStepOutcome BeginXiashu(ProgramSkillFrame supplied,int recipient)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(f.XiashuPaid is not null||f.InstructionIndex!=2||f.SelectedTargetSeats is not [var selected]||selected!=recipient||recipient==f.OwnerSeat||f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting,SourceSeat:{} actor} w||actor!=f.OwnerSeat||actor!=_currentSeat)throw new InvalidOperationException("Xiashu requires its unpaid native own Play-start.");
  var ids=_cardZones.CardsAt(CardLocation.Hand(f.OwnerSeat)).Select(c=>c.Id).ToArray();
  if(ids.Length==0||!_players[f.OwnerSeat].IsAlive||!_players[recipient].IsAlive){FinishProgramSkill(f,true);return SkillProgramStepOutcome.AwaitChild;}
  SetProgramCardSet(f.Id,XiashuGiftBind,ids,SkillProgramCardSetVisibility.Private);
  var before=_movementSequence;var invoice=new XiashuInvoice(KanZeSource(f),f.GameplayHash,w.ParentFrameId,f.InstructionIndex,_turnNumber,recipient,before,before,ids.Length);
  ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{XiashuPaid=new(invoice,XiashuStage.GiftChildren,before,before,ids.Length),PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(recipient), new(XiashuGiftReason), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,recipient,ids,_movementSequence,(_,records)=>{var active=GetActiveProgramFrame(f.Id);var paid=invoice with{GiftAfter=records.Max(m=>m.Sequence)};ReplaceRuntimeTop(active with{XiashuPaid=active.XiashuPaid!with{Invoice=paid,After=paid.GiftAfter}});AdvanceEventRulesAndQueueFact(new XiashuGiftPaidEvent(f.Id,paid));}));
  AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private PromptChoice XiashuChoice(ProgramSkillFrame f,string branch,string label,IReadOnlyList<int>? cards=null)=>new(new($"xiashu.{f.Id}.{branch}"),label,cards??[],[],new Dictionary<string,string>{{"program-action","xiashu"},{"branch",branch}});
 private IReadOnlyList<PromptChoice> XiashuChoices(ProgramSkillFrame f)
 {
  if(f.XiashuPaid!.Stage==XiashuStage.OwnerClaim)return Array.AsReadOnly(new[]{XiashuChoice(f,"shown","获得展示的手牌"),XiashuChoice(f,"unshown","获得未展示的手牌")});
  var selected=f.XiashuPaid!.SelectedReveal;
  return Array.AsReadOnly(_cardZones.CardsAt(CardLocation.Hand(f.XiashuPaid.Invoice.RecipientSeat)).Where(c=>!selected.Contains(c.Id)).Select(c=>XiashuChoice(f,"card-"+c.Id,c.Kind.ToString(),[c.Id])).Append(XiashuChoice(f,"finish","完成展示选择")).ToArray());
 }
 private void PublishXiashuDecision(ProgramSkillFrame f)
 {
  var seat=f.XiashuPaid!.Stage==XiashuStage.OwnerClaim?f.OwnerSeat:f.XiashuPaid.Invoice.RecipientSeat;var choices=XiashuChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);
  ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse,PendingMovementContinuation=null});
  _pendingDecision=new(DecisionKind.ProgramTrigger,seat,"请选择下书结算。",choices.SelectMany(c=>c.Cards).ToArray(),[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=seat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
  _status=_players[seat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.GiveAllHandAndResolveXiashu)]
 private PromptChoice XiashuAiChoice(ProgramSkillFrame f)=>f.XiashuPaid!.Stage==XiashuStage.RecipientReveal?XiashuChoices(f).Last():XiashuChoices(f).First();
 [ProgramAnswer("xiashu")]
 private void ResolveXiashuChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireIssuedKanZe(f);var r=f.XiashuPaid!;var seat=r.Stage==XiashuStage.OwnerClaim?f.OwnerSeat:r.Invoice.RecipientSeat;
  if(_pendingDecision?.PlayerSeat!=seat||!AssistedChoicesEqual([choice],XiashuChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Xiashu decision lost its actual chooser.");ClearPendingDecision();
  if(r.Stage==XiashuStage.RecipientReveal)
  {
   if(choice.Parameters["branch"]!="finish"){var ids=r.SelectedReveal.Append(choice.Cards.Single()).ToArray();ReplaceRuntimeTop(f=f with{XiashuPaid=r with{SelectedReveal=ids}});PublishXiashuDecision(f);return;}
   var shown=r.SelectedReveal.Where(id=>_cardZones.GetLocation(id)==CardLocation.Hand(r.Invoice.RecipientSeat)).ToArray();SetProgramCardSet(f.Id,XiashuShownBind,shown,SkillProgramCardSetVisibility.Public);
   AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,Array.AsReadOnly(shown.Select(id=>ToSnapshot(GetAdvancedCard(id))).ToArray())));
   ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id)with{XiashuPaid=r with{Stage=XiashuStage.OwnerClaim}});if(TryBeginPublicHandRevealWindow(f.Id,"xiashu-selected-hand"))return;if(!_players[f.OwnerSeat].IsAlive||!_players[r.Invoice.RecipientSeat].IsAlive){FinishXiashu(f);return;}PublishXiashuDecision(f);return;
  }
  var isShown=choice.Parameters["branch"]=="shown";var visible=GetProgramCardSet(f,XiashuShownBind).CardIds;
  var take=_players[f.OwnerSeat].IsAlive&&_players[r.Invoice.RecipientSeat].IsAlive?_cardZones.CardsAt(CardLocation.Hand(r.Invoice.RecipientSeat)).Where(c=>visible.Contains(c.Id)==isShown).Select(c=>c.Id).ToArray():[];
  SetProgramCardSet(f.Id,XiashuClaimBind,take,SkillProgramCardSetVisibility.Private);var before=_movementSequence;
  ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id)with{XiashuPaid=r with{Stage=XiashuStage.ClaimChildren,Before=before,After=before,Count=take.Length},PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
  if(take.Length>0)MoveProgramCardsFromMultipleSources(take,CardLocation.Hand(f.OwnerSeat),new(XiashuClaimReason));
  ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id)with{XiashuPaid=f.XiashuPaid!with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new XiashuClaimPaidEvent(f.Id,before,_movementSequence,take.Length,isShown));AdvanceRuntimeProgram(f.Id);
 }
 private bool DrainXiashu(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
 [ProgramContinuation(ProgramContinuationKind.Resume,22800)]private bool ResumeXiashu(long id)
 {
  if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{XiashuPaid:{} r} f||f.Id!=id||r.Stage is XiashuStage.RecipientReveal or XiashuStage.OwnerClaim)return false;
  RequireIssuedKanZe(f);if(DrainXiashu(f))return true;
  if(r.Stage==XiashuStage.GiftChildren&&_players[r.Invoice.RecipientSeat].IsAlive){ReplaceRuntimeTop(f=f with{XiashuPaid=r with{Stage=XiashuStage.RecipientReveal},PendingMovementContinuation=null});PublishXiashuDecision(f);}else FinishXiashu(f);return true;
 }
 [ProgramContinuation(ProgramContinuationKind.Movement,22800)]private bool ReturnXiashuMovement(ProgramSkillFrame f)=>f.XiashuPaid is not null&&ResumeXiashu(f.Id);
 private void FinishXiashu(ProgramSkillFrame f){var r=f.XiashuPaid!;AdvanceEventRulesAndQueueFact(new XiashuCompletedEvent(f.Id,r.Invoice.GiftCount,r.Stage==XiashuStage.ClaimChildren?r.Count:0));ReplaceRuntimeTop(f=f with{XiashuPaid=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
 [ProgramCapability]
 private sealed class KanZeProgramCapability(GameEngine engine) : IKanZeProgramHost
 {public SkillProgramStepOutcome BeginXiashu(ProgramSkillFrame f,int target)=>engine.BeginXiashu(f,target);public SkillProgramStepOutcome ArmKuanshi(ProgramSkillFrame f,int target)=>engine.ArmKuanshi(f,target);}
}
