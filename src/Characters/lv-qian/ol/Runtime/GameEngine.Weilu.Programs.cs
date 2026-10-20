namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void PayNextWeiluTarget(ProgramSkillFrame f)
 {
  var r=f.LvQian!;if(r.Cursor>=r.Targets.Count){FinishLvQian(f);return;}var target=_players[r.Targets[r.Cursor]];
  if(r.Stage==LvQianStage.LoseHpChildren)
  {
   var before=target.Hp;var requested=target.IsAlive&&target.HasHpValue?Math.Max(0,before-1):0;
   ReplaceRuntimeTop(f=f with{LvQian=r with{HpProof=new(target.Seat,requested,before,HpChangeKind.Loss)}});
   if(requested>0)new ProgramSkillHost(this).LoseHp(f.Id,f.SkillId,target.Seat,requested);
   f=GetActiveProgramFrame(f.Id);var lost=Math.Max(0,before-target.Hp);var debt=new WeiluPaidDebt(r.Source,r.GameplayHash,f.Id,r.Phase!,target.Seat,lost);_players[f.OwnerSeat].WeiluDebts=[..(_players[f.OwnerSeat].WeiluDebts??[]),debt];AdvanceEventRulesAndQueueFact(new WeiluHpPaidEvent(f.Id,r.Source,r.GameplayHash,r.Phase!,target.Seat,lost));
  }
  else
  {
   var debt=(_players[f.OwnerSeat].WeiluDebts??[]).SingleOrDefault(g=>g.Phase==r.Phase&&g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId&&g.TargetSeat==target.Seat);if(debt is null){ReplaceRuntimeTop(f with{LvQian=r with{Cursor=r.Cursor+1}});return;}_players[f.OwnerSeat].WeiluDebts=(_players[f.OwnerSeat].WeiluDebts??[]).Where(g=>g!=debt).ToArray();AdvanceEventRulesAndQueueFact(new WeiluDebtConsumedEvent(f.Id,debt.ProducerFrameId,debt.Phase,target.Seat,debt.ActualHpLost,target.IsAlive));ReplaceRuntimeTop(f=f with{LvQian=r with{HpProof=new(target.Seat,target.IsAlive?Math.Min(debt.ActualHpLost,target.MaxHp-target.Hp):0,target.Hp,HpChangeKind.Recovery)}});if(target.IsAlive&&debt.ActualHpLost>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,target.Seat,debt.ActualHpLost,null,null);
  }
  f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{LvQian=f.LvQian!with{Cursor=r.Cursor+1}});
 }
}
