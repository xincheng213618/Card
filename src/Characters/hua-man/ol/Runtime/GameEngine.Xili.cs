namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void PayXiliIncrease(ProgramSkillFrame f)
 {
  var r=f.HuaMan!;if(_resolutionStack[^2]is not BeforeDamageProgramWindowFrame w||w.Id!=r.WindowId||w.Prevented||CurrentDamageAttempt is not{}attack||attack.ResolutionId!=r.DamageAttemptResolutionId||attack.SourceSeat!=r.OtherSeat||attack.TargetSeat!=r.DamageTargetSeat)throw new InvalidOperationException("Paid Xili cannot borrow a later damage attempt.");attack.IncreaseFinalizedDamageAmount(1);ReplaceRuntimeFrame(w.Id,w with{Amount=checked(w.Amount+1)});AdvanceEventRulesAndQueueFact(new XiliDamageIncreasedEvent(f.Id,attack.ResolutionId,w.Id,f.OwnerSeat,w.SourceSeat,w.TargetSeat,w.Amount,w.Amount+1));PayHuaManDraw(f,f.OwnerSeat,2,HuaManStage.XiliPayerDraw);
 }
}
