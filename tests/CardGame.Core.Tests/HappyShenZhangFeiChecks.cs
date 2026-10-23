using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenZhangFeiChecks
{
    private const string Hero="happy:shen-zhang-fei",Shencai="happy:shencai",Watch="fixture:happy-zf-take-watch",Quiet="fixture:happy-zf-quiet",Peer="fixture:happy-zf-peer",Deck="fixture:happy-zf-deck";
    public static void RealTakeChildrenReturnBeforeDeathMarkCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(37,1,"happy-zhang-fei",Hero,4,0,0,0,0,[Watch],[],[],
            [new(Peer){StartingHand=[]},new(Peer){StartingHand=[]}],[new(Peer){StartingHand=[new(CardKind.Peach,Suit.Heart,3)]}],new string('c',64)){DefaultGodFactionId="shu"};
        var g=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,MaxTurns=4},registry);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        var targetCard=g.CreateSnapshot(1).Players[1].Hand.Single().Id;
        Accept(g,new UseProgramSkillCommand(0,Shencai,"sentence",[],[1],g.Revision,P(g)!.PromptId));
        Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="shen-zf-take")==true);
        var root=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Shencai&&f.ActivationId=="sentence");
        Need(root.ShenZhangFei is{Stage:ShenZhangFeiStage.TakeChoice,TakeBeforeDeath:true,ResultKind:CardKind.Slash}&&Deaths(g)==0&&!Facts<ShenZhangFeiSentenceEvent>(g).Any(e=>e.FrameId==root.Id),
            "The current Happy no-keyword judgment reaches its real obtain choice before adding this death mark.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand));
        Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);
        var movement=g.CardMovements.Single(m=>m.CardId==targetCard&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.anniversary:shencai.take");
        Need(g.CreateSnapshot(0).Players[0].Hand.Any(c=>c.Id==targetCard)&&Deaths(g)==0&&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==root.Id).ShenZhangFei is{Stage:ShenZhangFeiStage.TakeChildren,TakeBeforeDeath:true,Paid:true}&&
            !Facts<ShenZhangFeiSentenceEvent>(g).Any(e=>e.FrameId==root.Id),
            "The physically delivered region entity's genuine movement child observes no newly added death mark.");
        var before=State(g);g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(State(g)==before,"Cold keeps the original paid take, exact native movement child and not-yet-issued death mark.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        var facts=g.Events.Select(e=>e.Payload).ToArray();
        var returned=Array.FindIndex(facts,e=>e is ProgramBindingResolvedEvent{SkillId:Watch,BindingId:"take-observed",Completed:true});
        var marked=Array.FindIndex(facts,e=>e is ShenZhangFeiSentenceEvent sentence&&sentence.FrameId==root.Id);
        Need(returned>=0&&marked>returned&&Deaths(g)==1&&Facts<ShenZhangFeiSentenceEvent>(g).Count(e=>e.FrameId==root.Id&&e.Source.SkillId==Shencai&&e.Terms==PrintedCardTerms.None&&e.DeathCount==1)==1&&
            g.CardMovements.Count(m=>m.Sequence==movement.Sequence&&m.CardId==targetCard&&m.Reason.Value=="skill-program.anniversary:shencai.take")==1,
            "Only after the original native take observer completes does Happy Shencai add one death mark, without repeating the entity payment.");
        before=State(g);g=GameReplay.Replay(g.CreateCheckpoint().Options,g.AcceptedCommands,registry);
        Need(State(g)==before,"Replay preserves the deliberate take-before-death ordering and its typed original parent.");
    }
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(i=>g.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
    private static int Deaths(GameEngine g)=>g.CreateSnapshot(0).Players[1].Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.ShencaiSi)?.Count??0;
    private static T[] Facts<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Need(p.PlayerSeat==0,"Only the actual human answers this take/observer.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<bool> ready)
    {
        for(var n=0;n<160;n++)
        {if(ready())return;if(P(g) is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Happy Zhang Fei boundary "+JsonSerializer.Serialize(P(g)));Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Happy Zhang Fei missed its bounded real take boundary.");
    }
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Zhang Fei command.");}
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(g.CreateSnapshot(i))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-zhang-fei",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"anniversary:shen-zhang-fei");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"take-observed","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:shencai.take"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{{Quiet,new{name=Quiet,description="安静实际回合"}},{Watch,new{name=Watch,description="原生取牌后观察顺序",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}};
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in c.Programs)b.AddSkill(new(id,id,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Peer,"真实受判者","",Quiet,"wei",10));
            b.AddDeck(new(Deck,"无四类关键词的真实判定牌",0,0,[]){PhysicalCards=Enumerable.Range(0,40).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐神裁取牌后加死小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
