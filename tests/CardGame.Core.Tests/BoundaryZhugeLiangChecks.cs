using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static PopulationTopReorderScenario;

internal static class BoundaryZhugeLiangChecks
{
    public static void PopulationPartitionAndShortPile()
    {
        foreach(var players in new[]{2,3,4,5})
        {
            var(g,r)=Start(players:players<4?4:players,draw:2,mode:players<4?"population-"+players:"",pausePrepare:players>=4);
            if(players<4)
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:population-reduction");Activate(g);
                for(var killed=1;killed<=4-players;killed++)
                {
                    if(killed>1)Activate(g);
                    Reach(g,p=>p.Choices.Any(c=>c.Targets.Count==1));Answer(g,c=>c.Targets.SequenceEqual(new[]{killed}));
                    AdvanceUntil(g,x=>Pending(x)?.SkillPrompt?.SkillId==Skill||Pending(x)?.SkillPrompt?.SkillId=="fixture:population-reduction"&&Pending(x)?.Choices.Any(c=>Action(c)=="activate")==true);
                }
                Reach(g,p=>p.SkillPrompt?.SkillId==Skill);Require(g.CreateSnapshot(0).Players.Count(p=>p.IsAlive)==players,"Real loss/death commands establish the lower live population.");
            }
            Replay(g,r);Activate(g);var d=Frame(g).TopReorder!;
            Witness(g,"population-"+players+"-view");Require(d.ViewedCardIds.Count==(players<4?3:5),"Each real population uses 5/3 rather than min(alive,5).");
            Require(d.ViewedCardIds is IList<int>{IsReadOnly:true},"New owning viewed entity collection is frozen.");
            var ids=d.ViewedCardIds.ToArray();var before=Pile(g);var moves=g.CardMovements.Count;
            Answer(g,c=>c.Cards.SequenceEqual(new[]{ids[1]}));Replay(g,r);Require(Pile(g).SequenceEqual(before)&&g.CardMovements.Count==moves&&!Eligible(g),"Partial choice freezes only draft, never pile or full-bottom state.");
            Answer(g,c=>c.Cards.SequenceEqual(new[]{ids[0]}));Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="finish-top");
            foreach(var id in ids.Skip(2).Reverse()){Replay(g,r);Answer(g,c=>c.Cards.SequenceEqual(new[]{id}));}
            Play(g);Replay(g,r);Require(!Eligible(g),"One or more physical top cards deny the later opportunity.");
            Require(g.CardMovements.Skip(moves).Where(m=>m.To==CardLocation.Hand(0)).Select(m=>m.CardId).SequenceEqual(new[]{ids[1],ids[0]}),"Actual normal draw consumes the chosen top-first entity order once.");
            Require(Pile(g).SequenceEqual(before.Skip(ids.Length).Concat(ids.Skip(2))),"First chosen bottom entity is closest to bottom, preserving actual mature order.");
        }
        foreach(var remaining in new[]{0,1,2,3})
        {
            var(g,r)=Start(remaining:remaining);Activate(g);
            if(remaining>0){Require(Frame(g).TopReorder!.ViewedCardIds.Count==remaining,"Short nonempty pile uses only actual available entities.");FinishBottom(g,r);}Play(g);Replay(g,r);
            Require(Eligible(g)==(remaining>0),"Only nonempty actual completed all-bottom grants eligibility.");
            Require(!g.CardMovements.Any(m=>m.Reason==CardMoveReasons.Reshuffle),"Nonempty short pile is not silently replenished.");
        }
    }
}
