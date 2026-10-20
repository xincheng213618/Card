namespace CardGame.Core;
public sealed partial class GameEngine
{
 private CardUseFrame? ChijieDamageUse(int owner,ProgramSkillWindowContext w,out CompletedUndamagedUseDamageRecordedEvent damage)
 {
  damage=null!;if(w.Window!=SkillProgramTriggerWindow.AfterDamageApplied||_resolutionStack.SingleOrDefault(f=>f.Id==w.ParentFrameId)is not DamageTriggerWindowFrame window||window.TargetSeat!=owner||window.SourceSeat==owner||w.TargetSeat!=owner||w.SourceSeat!=window.SourceSeat||window.TriggerWindow!=w.Window)return null;
  var facts=CompleteProgramEventHistory().OfType<CompletedUndamagedUseDamageRecordedEvent>().Where(e=>e.DamageFrameId==window.ParentFrameId&&e.TargetSeat==owner&&e.SourceSeat==window.SourceSeat&&e.Amount>0&&!e.SourceLess).ToArray();if(facts is not[var fact]||LifecycleCardUse(fact.CardUseFrameId)is not{Action:{Type:CardActionType.Use}a}use||fact.CardActionId!=a.ActionId||fact.EffectiveKind!=use.CardKind||!HasExactAcceptedActualHandGainUse(use,a))return null;damage=fact;return use;
 }
 private IReadOnlyList<int> ChijieClaimable(CardUseFrame use)
 {
  if(use.XinPiOrigin is not{}o||use.Action is not{}a||o.UseId!=use.Id||o.Action.ActionId!=a.ActionId||!o.Action.PhysicalCards.SequenceEqual(a.PhysicalCards)||!HasExactAcceptedCompletedUndamagedUse(use,a))return [];
  return Array.AsReadOnly(o.Materials.Where(m=>!IsClaimedUseCardEntity(use.Id,m.CardId)&&XinPiMaterialStillOriginal(use,m)).Select(m=>m.CardId).Distinct().ToArray());
 }
 private bool ChijieUndamagedIncoming(int owner,ProgramSkillWindowContext w,out CardUseFrame use)
 {
  var actual=ExactXinPiNativeWindow(w,true);use=actual!;return actual is{Action:{}a}&&a.ActorSeat!=owner&&XinPiMainTargets(actual).Contains(owner)&&!actual.CausedDamage&&!CompleteProgramEventHistory().OfType<CompletedUndamagedUseDamageRecordedEvent>().Any(e=>e.CardUseFrameId==actual.Id&&e.Amount>0)&&ChijieClaimable(actual).Count>0;
 }
}
