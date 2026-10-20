namespace CardGame.Core;
public sealed partial class GameEngine
{
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

    [ProgramDamageHook(ProgramDamagePhase.AfterEquipmentChoices, 10)]
    private ProgramDamageInterception InterceptKuanshiDamage(IDamageAttempt attack, int amount, DamageNature nature) =>
        TryPreventKuanshiDamage(attack.IsSourceLess || attack.IsDelayedJudgmentDamage ? null : attack.SourceSeat,
            attack.TargetSeat, amount, attack.ResolutionId) ? ProgramDamageInterception.Prevented : ProgramDamageInterception.Continue;
}
