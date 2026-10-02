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
    public static void ActualEndingExtraAndScheduled()
    {
        foreach(var bottom in new[]{false,true})
        {
            var(g,r)=Start();Activate(g);if(bottom)FinishBottom(g,r);else{Answer(g,c=>c.Cards.Count==1);FinishBottom(g,r);}Play(g);End(g);
            if(bottom){Reach(g,p=>p.SkillPrompt?.SkillId==Skill);Witness(g,"normal-ending-offer");Require(g.ResolutionStack.First() is TurnEndingBoundaryFrame,"Real ending offer owns the actual end-turn boundary.");Replay(g,r);Activate(g);Require(Frame(g).TopReorder!.Population!.AllBottomStateId==null,"End reorder cannot re-arm itself.");FinishBottom(g,r);}
            AdvanceUntil(g,x=>x.CreateSnapshot(0).CurrentSeat==1);Replay(g,r);
            Require(g.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Skill&&e.BindingId=="ending-order")== (bottom?1:0),"Only one actual all-bottom completion enables one finite true Ending.");
        }
        var(skip,sr)=Start();Activate(skip);FinishBottom(skip);Play(skip);End(skip);Reach(skip,p=>p.SkillPrompt?.SkillId==Skill);Skip(skip);AdvanceUntil(skip,x=>x.CreateSnapshot(0).CurrentSeat==1);Replay(skip,sr);
        Require(!skip.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Any(e=>e.SkillId==Skill&&e.BindingId=="ending-order"),"Declined ending does not restart its own cursor.");
        var(extra,er)=Start(mode:"extra");Activate(extra);FinishBottom(extra);Play(extra);var firstTurn=extra.CreateSnapshot(0).TurnNumber;End(extra);Reach(extra,p=>p.SkillPrompt?.SkillId==Skill);Skip(extra);
        Reach(extra,p=>p.SkillPrompt?.SkillId=="fixture:population-ending");Activate(extra);Reach(extra,p=>p.SkillPrompt?.SkillId==Skill);Witness(extra,"extra-prepare-offer");Require(extra.CreateSnapshot(0).TurnNumber==firstTurn+1&&!Eligible(extra),"Real extra actual turn resets the private eligibility before its new preparation.");Replay(extra,er);
        Activate(extra);FinishBottom(extra);Play(extra);End(extra);Reach(extra,p=>p.SkillPrompt?.SkillId==Skill);Replay(extra,er);Activate(extra);FinishBottom(extra);AdvanceUntil(extra,x=>x.CreateSnapshot(0).CurrentSeat==1);Replay(extra,er);
        Require(extra.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Skill&&e.BindingId=="prepare-order")==2&&extra.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Skill&&e.BindingId=="ending-order")==1,"Distinct Normal/Extra turns each pay preparation quota and their own actual ending opportunity.");
        foreach(var mode in new[]{"scheduled-play","scheduled-draw"})
        {
            var(g,r)=Start(mode:mode,pausePrepare:false);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:population-schedule");Replay(g,r);Activate(g);
            if(mode=="scheduled-play"){Play(g);End(g);}
            Reach(g,p=>p.SkillPrompt?.SkillId==Skill);Witness(g,mode+"-resumed-prepare");Require(!g.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Any(e=>e.SkillId==Skill),"Real scheduled child returns before the next preparation binding starts.");Replay(g,r);
            Activate(g);FinishBottom(g);Play(g);End(g);Reach(g,p=>p.SkillPrompt?.SkillId==Skill);Skip(g);AdvanceUntil(g,x=>x.CreateSnapshot(0).CurrentSeat==1);Replay(g,r);
            var events=g.Events.Select(e=>e.Payload).OfType<ProgramPhaseScheduledEvent>().ToArray();Require(events.Length==2&&events[0].Started&&!events[1].Started&&events[0].Phase==(mode=="scheduled-play"?TurnPhase.Play:TurnPhase.Draw),"Real Draw/Play scheduled start/end and exact parent continuation retain one actual turn.");
        }
        Console.WriteLine("Actual command witnesses: Normal/Extra Ending and preparation before-parent Scheduled Draw/Play cold prefixes, not host phase mutation.");
    }
    public static void PrivateFacesColdAndInputs()
    {
        var(g,r)=Start();Activate(g);var d=Frame(g).TopReorder!;var ids=d.ViewedCardIds.ToArray();var revision=g.Revision;Witness(g,"private-complete-faces");
        foreach(var viewer in Enumerable.Range(0,4).Append(-1))
        {
            var v=g.CreateSnapshot(viewer);Require(viewer==0?v.PrivateRevealedCards!.Select(c=>c.Id).SequenceEqual(ids)&&v.PrivateRevealedCards!.All(c=>c.Suit!=Suit.None&&c.Rank>0):v.PrivateRevealedCards==null&&v.PendingDecision==null,"Only owner sees frozen complete entity, suit, rank and order; other viewers and spectator see none.");
        }
        Require(g.Revision==revision&&g.CreateSnapshot(0).PrivateRevealedCards is IList<CardSnapshot>{IsReadOnly:true},"Private face projection is immutable and pure.");Replay(g,r);
        var p=Pending(g)!;var checkpoint=GameCheckpointJson.Serialize(g.CreateCheckpoint());
        foreach(var command in new GameCommand[]{new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision),new AnswerPromptCommand(0,p.PromptId,new ChoiceId("foreign"),g.Revision)})
        {Require(!g.Submit(command).Accepted&&g.Revision==revision&&checkpoint==GameCheckpointJson.Serialize(g.CreateCheckpoint()),"Foreign actor/choice rejects atomically without RNG or draft changes.");}
        Answer(g,c=>c.Cards.SequenceEqual(new[]{ids[1]}));Require(!g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted,"Consumed prompt cannot choose a second time.");
        Witness(g,"private-partial-top");Require(g.CreateSnapshot(0).PrivateRevealedCards!.Select(c=>c.Id).SequenceEqual(ids),"Partial selection keeps full single viewed set, not a new draw.");Replay(g,r);FinishBottom(g,r);Play(g);Require(g.CreateSnapshot(0).PrivateRevealedCards==null,"Actual committed order clears private faces.");Replay(g,r);
        var(all,ar)=Start();Activate(all);var original=Pile(all);Answer(all,c=>c.Parameters.GetValueOrDefault("action")=="finish-top");Witness(all,"all-bottom-uncommitted");Require(!Eligible(all)&&Pile(all).SequenceEqual(original),"Uncommitted finish-top is not a true full-bottom receipt.");Replay(all,ar);
        while(Pending(all)?.Kind==DecisionKind.ProgramTopReorder){Replay(all,ar);Answer(all,c=>c.Cards.Count==1);}Play(all);Witness(all,"all-bottom-committed");Require(Eligible(all),"Only completed exact physical partition grants state.");Replay(all,ar);
    }
    public static void SourceLifetimeAndPaidDeath()
    {
        // Explicit host audits: source mutation cannot normally interleave a private prompt command.
        var(before,_)=Start();Activate(before);Answer(before,c=>c.Cards.Count==1);var pile=Pile(before);var grant=Players(before)[0].SkillGrants.Grants.Single(x=>x.SkillId==Skill);Players(before)[0].SkillGrants.RemoveGrant(grant.GrantId);
        Answer(before,c=>c.Parameters.GetValueOrDefault("action")=="finish-top");Require(!Eligible(before,grant.SkillInstanceId)&&Pile(before).SequenceEqual(pile)&&!before.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.TopReorder!=null),"HOST AUDIT: lost source before physical commit cancels with no state and no pile movement.");
        foreach(var replacement in new[]{false,true})
        {
            var(g,_r)=Start();Activate(g);FinishBottom(g);Play(g);var owner=Players(g)[0];var current=owner.SkillGrants.Grants.Single(x=>x.SkillId==Skill);owner.SkillGrants.RemoveGrant(current.GrantId);
            owner.SkillGrants.Grant(replacement?current with{GrantId="host:new",SkillInstanceId="host:new",SourceId="host:new"}:current);
            Require(Eligible(g,current.SkillInstanceId)&&Eligible(g)==!replacement,"HOST AUDIT: paid scalar survives old source loss but replacement instance does not inherit it.");End(g);
            if(!replacement){Reach(g,p=>p.SkillPrompt?.SkillId==Skill);Skip(g);}AdvanceUntil(g,x=>x.CreateSnapshot(0).CurrentSeat==1);
        }
        var(dead,dr)=Start(mode:"death");Activate(dead);FinishBottom(dead);Play(dead);End(dead);Reach(dead,p=>p.SkillPrompt?.SkillId=="fixture:population-ending");Replay(dead,dr);Witness(dead,"paid-ending-before-death-child");Activate(dead);
        for(var i=0;i<32&&dead.CreateSnapshot(0).Winner==Winner.None;i++){var p=Pending(dead);if(p!=null&&p.Choices.Count>0)Answer(dead,c=>c.Cards.Count==0);else Accept(dead,new AdvanceOneStepCommand(dead.Revision));}
        Require(!dead.CreateSnapshot(0).Players[0].IsAlive&&!dead.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Any(e=>e.SkillId==Skill&&e.BindingId=="ending-order"),"REAL COMMAND: owner death in earlier actual Ending child prevents later paid offer finitely.");Replay(dead,dr);Witness(dead,"paid-ending-real-owner-death");
        Console.WriteLine("Source loss/regrant above are host lifecycle audits; actual paid owner death is accepted commands with all-view cold replay.");
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
