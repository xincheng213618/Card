using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void NextQingyaHand(ProgramSkillFrame f)
    {
        while(f.YaDan!.Cursor<f.YaDan.Targets.Count&&_players[f.OwnerSeat].IsAlive)
        {
            var r=f.YaDan;var seat=r.Targets[r.Cursor];var materials=QingyaHandMaterials(f.OwnerSeat,seat);
            if(materials.Count==0){AdvanceEventRulesAndQueueFact(new QingyaParticipantSkippedEvent(f.Id,r.Cursor,seat));ReplaceRuntimeTop(f=f with{YaDan=r with{Cursor=r.Cursor+1,Materials=[]}});continue;}
            ReplaceRuntimeTop(f=f with{YaDan=r with{Stage=QingyaStage.HandChoice,Materials=materials}});
            for(var i=0;i<materials.Count;i++)AdvanceEventRulesAndQueueFact(new QingyaHandCandidateEvent(f.Id,r.Cursor,i,materials[i]));
            PublishYaDan(f);return;
        }
        FinishYaDan(f);
    }
}

