namespace CardGame.Core;
internal sealed partial class MouJiangJiRuntime
{
 internal MouJiangJiEffectRounds IssueEffect(ProgramSkillFrame f,CardUseFrame use,ProgramCardTriggerWindowFrame window)
 {
  if(!Valid(f)||f.MouJiangJi is not{Stage:MouJiangJiStage.YingshiChoice,Target:>=0}r||r.UseId!=use.Id||r.WindowId!=window.Id||window.ParentFrameId!=use.Id||window.Continuation!=ProgramCardContinuation.FinalizedTrick||window.TrickContinuation is null||use.Action is not{Type:CardActionType.Use}a||a.ActionId!=r.ActionId||!r.Targets.Contains(r.Target)||use.MouJiangJiRounds is{}old&&(old.ActionId!=r.ActionId||old.Started!=0||old.Grants.Any(g=>g.Source.OwnerSeat==f.OwnerSeat)))throw new InvalidOperationException("Yingshi requires its exact accepted ordinary-Trick target and a once-issued native extra effect.");
  var grant=new MouJiangJiEffectGrant(f.Id,r.Source,r.GameplayHash,window.Id,use.Id,a.ActionId,r.Target);var previous=use.MouJiangJiRounds;
  var nativeState=previous?.Native??new ExtraCardEffectState{Source=r.Source,ProgramFrameId=f.Id,ActionId=a.ActionId,PaidCardId=0,Trick=window.TrickContinuation};
  var result=new MouJiangJiEffectRounds(a.ActionId,nativeState){Grants=(previous?.Grants??[]).Append(grant).ToArray()};events.AdvanceFact(new MouJiangJiEffectIssuedEvent(grant));return result;
 }
 internal bool ValidEffects(CardUseFrame use)
 {
  if(use.MouJiangJiRounds is not{}r)return true;
  if(use.Action is not{Type:CardActionType.Use}a||a.ActionId!=r.ActionId||r.Native.ActionId!=a.ActionId||r.Native.Trick is null||r.Native.PaidCardId!=0||r.Started<0||r.Started>r.Grants.Count||r.Grants.Count==0||r.Grants.Select(g=>g.FrameId).Distinct().Count()!=r.Grants.Count||r.Grants.Select(g=>g.Source.OwnerSeat).Distinct().Count()!=r.Grants.Count||r.Native.WholeGroupRepeated!=(r.Started>0)||r.Grants.Any(g=>g.Source.SkillId!=Yingshi||g.Source.BindingId!="ordinary-trick-target"||g.Source.OwnerSeat!=a.ActorSeat||g.UseId!=use.Id||g.ActionId!=a.ActionId||g.GameplayHash!=facts.Program(Yingshi)?.GameplayHash||!facts.History<MouJiangJiStartedEvent>().Any(e=>e.FrameId==g.FrameId&&e.Source==g.Source&&e.GameplayHash==g.GameplayHash&&e.Operation==SkillProgramEffectOp.ResolveMouJiangJiYingshi&&e.UseId==g.UseId&&e.ActionId==g.ActionId&&e.WindowId==g.WindowId)||facts.History<MouJiangJiEffectIssuedEvent>().Count(e=>e.Grant==g)!=1))return false;
  var starts=facts.History<MouJiangJiEffectStartedEvent>().Where(e=>e.UseId==use.Id&&e.ActionId==a.ActionId).ToArray();var done=facts.History<MouJiangJiEffectCompletedEvent>().Where(e=>e.UseId==use.Id&&e.ActionId==a.ActionId).ToArray();
  return Enumerable.Range(1,r.Started).SequenceEqual(starts.Select(e=>e.Round))&&starts.All(e=>e.FrameId==r.Grants[e.Round-1].FrameId&&e.Target==r.Grants[e.Round-1].Target)&&Enumerable.Range(1,r.Started-(r.Active?1:0)).SequenceEqual(done.Select(e=>e.Round))&&done.All(e=>starts.Any(s=>(s.UseId,s.ActionId,s.FrameId,s.Round,s.Target)==(e.UseId,e.ActionId,e.FrameId,e.Round,e.Target)))&&(r.Started==0?!r.Active&&r.ActiveIndex==-1&&r.ActiveTarget==-1:r.ActiveTarget==starts[^1].Target&&r.ActiveIndex==Array.IndexOf(use.TargetSeats.ToArray(),r.ActiveTarget))&&(!r.Active||r.ActiveIndex==-1||r.ActiveIndex>=0&&r.ActiveIndex<use.TargetSeats.Count&&use.TargetSeats[r.ActiveIndex]==r.ActiveTarget);
 }
 internal MouJiangJiEffectRounds BeginEffect(CardUseFrame use,ExtraCardEffectState? previous)
 {
  if(!ValidEffects(use)||use.MouJiangJiRounds is not{}r||r.Started>=r.Grants.Count||r.Active)throw new InvalidOperationException("Yingshi cannot begin an unrelated or already active extra effect.");
  var grant=r.Grants[r.Started];var index=Array.IndexOf(use.TargetSeats.ToArray(),grant.Target);if(use.CardKind==CardKind.BorrowedSword&&index%2!=0)index=-1;
  var nativeState=r.Native with{WholeGroupRepeated=true,MaterialClaims=previous?.MaterialClaims??r.Native.MaterialClaims,FrozenGroupDamageSourceSeat=previous?.FrozenGroupDamageSourceSeat??r.Native.FrozenGroupDamageSourceSeat};
  var result=r with{Started=r.Started+1,Active=true,ActiveIndex=index,ActiveTarget=grant.Target,Native=nativeState};events.AdvanceFact(new MouJiangJiEffectStartedEvent(use.Id,r.ActionId,grant.FrameId,result.Started,grant.Target));return result;
 }
 internal MouJiangJiEffectRounds CompleteEffect(CardUseFrame use)
 {
  if(!ValidEffects(use)||use.MouJiangJiRounds is not{Active:true,Started:>0}r)throw new InvalidOperationException("Yingshi lost its ordered active native extra effect.");
  events.AdvanceFact(new MouJiangJiEffectCompletedEvent(use.Id,r.ActionId,r.Grants[r.Started-1].FrameId,r.Started,r.ActiveTarget));return r with{Active=false};
 }
}
