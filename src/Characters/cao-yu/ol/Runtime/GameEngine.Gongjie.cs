using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void AdvanceGongjieDonor(ProgramSkillFrame f)
 {var r=f.CaoYu!;ReplaceRuntimeTop(f=f with{CaoYu=r with{Index=r.Index+1,Stage=CaoYuStage.GongjieTake,Materials=[],GrainIds=[],Before=0,After=0,Count=0},PendingMovementContinuation=null});if(f.CaoYu!.Index>=r.Targets.Count)AfterGongjieGifts(f);else PublishCaoYu(f);}
 private void AfterGongjieGifts(ProgramSkillFrame f)
 {var count=ProgramEventHistory<CaoYuCardPaidEvent>().Where(e=>e.FrameId==f.Id&&e.Stage==CaoYuStage.GongjieGiftChildren&&e.Suit!=Suit.None).Select(e=>e.Suit).Distinct().Count();if(!_players[f.OwnerSeat].IsAlive){FinishCaoYu(f);return;}DrawCaoYu(f,count,CaoYuStage.GongjieDrawChildren,GongjieDrawReason);}
}
