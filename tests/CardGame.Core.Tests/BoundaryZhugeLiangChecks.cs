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
    public static void ParserLegacyAndNativeAi()
    {
        var baseEffect="""{"op":"reorderTopCards","target":"owner","amount":5,"populationThreshold":4,"belowPopulationAmount":3}""";
        foreach(var bad in new[]{baseEffect.Replace("\"populationThreshold\":4","\"populationThreshold\":0"),baseEffect.Replace("\"belowPopulationAmount\":3","\"belowPopulationAmount\":6"),baseEffect.Replace("\"populationThreshold\":4","\"populationThreshold\":null"),baseEffect.Replace("\"belowPopulationAmount\":3","\"belowPopulationAmount\":1.5"),baseEffect.Replace(",\"belowPopulationAmount\":3",""),baseEffect.Insert(baseEffect.Length-1,",\"exactTopCount\":2"),baseEffect.Insert(baseEffect.Length-1,",\"numberExpression\":\"livingPlayerCount\""),baseEffect.Insert(baseEffect.Length-1,",\"allBottomStateId\":\"missing\""),baseEffect.Replace("\"populationThreshold\":4,\"belowPopulationAmount\":3","\"allBottomStateId\":\"missing\"" )})
        {var rejected=false;try{Load(bad);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Invalid/mixed population or undeclared state rejects catalog load: "+bad);}
        var completion=baseEffect.Insert(baseEffect.Length-1,",\"allBottomStateId\":\"done\"");
        var state="\"states\":[{\"id\":\"done\",\"initialValue\":false,\"visibility\":\"private\",\"resetScope\":\"turn\",\"reacquirePolicy\":\"preserveUntilGameEnd\"}],";
        foreach(var changed in new[]{state.Replace("\"private\"","\"public\""),state.Replace("\"turn\"","\"game\""),state.Replace("false","true")})
        {var rejected=false;try{Load(completion,changed);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Completion state requires initially-false private actual-turn scope.");}
        foreach(var window in new[]{"turnEnding","drawPhaseStarting"}){var rejected=false;try{Load(completion,state,window);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Completion cannot arm from the later or unrelated window.");}
        Require(Load(completion,state).Triggers.Single().Effects.Single().AllBottomStateId=="done","Valid declared completion metadata compiles.");
        var old=Load("""{"op":"reorderTopCards","target":"owner","amount":5}""");Require(old.Triggers.Single().Effects.Single().PopulationThresholdCount==null&&!JsonSerializer.Serialize(old.Triggers.Single().Effects.Single()).Contains("PopulationThresholdCount")&&!JsonSerializer.Serialize(new ProgramTopReorder([],[],[],false)).Contains("Population"),"Missing new fields preserve old null serialized shape.");
        var(g,r)=Start(mode:"ai");Skip(g);Play(g);End(g);
        AdvanceUntil(g,x=>Pending(x)?.Kind==DecisionKind.ProgramTopReorder&&Pending(x)?.PlayerSeat==1&&Frame(x).TriggerId=="prepare-order");Witness(g,"formal-ai-prepare-private");Replay(g,r);
        AdvanceUntil(g,x=>Pending(x)?.Kind==DecisionKind.ProgramTopReorder&&Pending(x)?.PlayerSeat==1&&Frame(x).TriggerId=="ending-order");Witness(g,"formal-ai-ending-private");Replay(g,r);
        AdvanceUntil(g,x=>x.CreateSnapshot(0).CurrentSeat==2);Witness(g,"formal-ai-native-completed");Replay(g,r);
        var started=g.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Where(e=>e.OwnerSeat==1&&e.SkillId==Skill).ToArray();Require(started.Count(e=>e.BindingId=="prepare-order")==1&&started.Count(e=>e.BindingId=="ending-order")==1,"Formal optional AI actually activates preparation and real ending through new public prior, then native free ordering completes.");
        Console.WriteLine($"AI native actual owner seat1: HP={g.CreateSnapshot(1).Players[1].Hp}, Hand={g.CreateSnapshot(1).Players[1].HandCount}, hand departures={g.CardMovements.Count(m=>m.From==CardLocation.Hand(1))}.");
        Require(g.CardMovements.Any(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile),"Native AI still performs its real normal discard after zero-draw all-bottom viewing.");
        Console.WriteLine("Formal optional native AI prepare/end witness: alive=4, draw=0; no mandatory replacement, no private-deck estimate/RNG.");
    }
    private static SkillProgram Load(string effect,string state="",string window="turnStartBeforeNormalFlow")=>SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:population-parser","revision":1,{{state}}"triggers":[{"id":"order","window":"{{window}}","subject":"owner","optional":true,"effects":[{{effect}}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:population-parser":{"name":"fixture","description":"fixture"}}}""").Programs["fixture:population-parser"];
}
