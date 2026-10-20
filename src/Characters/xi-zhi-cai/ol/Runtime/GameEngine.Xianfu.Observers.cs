namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool ValidXianfuEcho(ProgramSkillFrame f)
 {
  if(f.XianfuPending is not {} p)return false;var r=p.Receipt;var g=r.Grant;
  return g.Source.OwnerSeat==f.OwnerSeat&&g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId&&g.GameplayHash==f.GameplayHash&&r==f.WindowContext?.XianfuEcho&&p.InstructionIndex==f.InstructionIndex&&p.NativeIssued&&r.ActualAmount>0&&r.OriginalTargetSeat==g.TargetSeat&&
   CompleteProgramEventHistory().OfType<XianfuRelationArmedEvent>().Count(e=>e.Grant==g)==1&&CompleteProgramEventHistory().OfType<XianfuEchoQualifiedEvent>().Count(e=>e.Receipt==r)==1&&CompleteProgramEventHistory().OfType<XianfuEchoIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Receipt==r&&e.InstructionIndex==p.InstructionIndex&&e.NativeAmount==p.NativeAmount)==1&&
   GetPausedPrivateOfferEffect(f)?.Op==SkillProgramEffectOp.SettleXianfuNativeEcho;
 }
}
