using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LuYuShengChecks
{
    private const string Cang="ol:cangxin",Run="ol:runwei",Owner="fixture:lys-owner",Driver="fixture:lys-driver",Quiet="fixture:lys-quiet",Watch="fixture:lys-watch",Conditional="fixture:lys-conditional",Health="fixture:lys-health",Mode="identity:classic-lu-yu-sheng-fixture",Legacy="identity:lu-yu-sheng-legacy-fixture";
    public static void BottomRevealNativeDrawAndZeroHeartsCold()
    {
        var(g,r)=Start("draw-watch",true);var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.LuYuSheng is not null);var bottom=f.LuYuSheng!.BottomCards.ToArray();
        Need(bottom.Length==3&&f.LuYuSheng is{Stage:LuYuShengStage.DrawChildren,Count:3,ActualCount:3}&&bottom.All(id=>g.CreateCardZoneDiagnostics().Any(z=>z.CardId==id&&z.Location==CardLocation.DrawPile)),"Cangxin publicly shows the three real bottom entities and draws three different cards from the native top without repositioning the bottom.");
        Need(Enumerable.Range(0,4).All(s=>bottom.All(id=>g.CreateSnapshot(s).PublicRevealedCards.Any(c=>c.Id==id)))&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"Original bottom cards are public to every viewer while the real native child choice stays private.");
        g=Cold(g,r);Continue(g);Play(g);var draw=E<LuYuShengMovementIssuedEvent>(g).Single(e=>e.Stage==LuYuShengStage.DrawChildren);
        Need(V(g).HandCount==9&&draw.Count==3&&g.CardMovements.Where(m=>m.Sequence>draw.Before&&m.Sequence<=draw.After).All(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0)&&!bottom.Contains(m.CardId)),"The additive skill draw is followed by the independent normal Draw2 exactly once.");_=Cold(g,r);
        (g,r)=Start("spades");Need(V(g).HandCount==6&&E<LuYuShengBottomRevealedEvent>(g).Count()==3&&E<LuYuShengMovementIssuedEvent>(g).Single().Count==0,"Zero actual shown Hearts is a real reveal with zero skill draws, followed by normal Draw2.");_=Cold(g,r);
    }
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(c);Need(x.Accepted,x.Error?.Message??"The real command was rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(p.PlayerSeat==0,"Only the actual human can Answer; AI advances natively.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<700;i++)
        {
            if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Lu Yu Sheng ended before expected boundary: "+Diagnostic(g));var p=P(g);
            if(p is{PlayerSeat:0}&&Optional(p,Run))Skip(g);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{PlayerSeat:0}&&p.SkillPrompt?.SkillId==Health)Continue(g);
            else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Lu Yu Sheng prompt: "+Diagnostic(g));else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Lu Yu Sheng did not reach its true native boundary: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Status,Prompt=P(g),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive}).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves public bottom entities, private payer choices, exact native tails, actual-phase grants and damage-instance reductions.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal",bool early=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=scenario.EndsWith("legacy",StringComparison.Ordinal)?Legacy:Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(early)Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Watch);else Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-lu-yu-sheng",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:lu-yu-sheng");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"hurt-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
              {"id":"draw-seven","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":7}]},
              {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
              {"id":"fire-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3,"nature":"fire"}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"native-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
             {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{Run}}","minimumHp":8}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-native-child","window":"{{(scenario is"draw-watch" or"draw-loss"?"cardsGained":"cardsMoved")}}","subject":"owner",{{(scenario is"draw-watch" or"draw-loss"?"\"destinationZones\":[\"hand\"]":"\"sourceZones\":[\"hand\"]")}},"movementOccurrence":"perBatch","movementReasons":["{{(scenario is"draw-watch" or"draw-loss"?"skill-program.lu-yu-sheng.draw":"skill-program.lu-yu-sheng.runwei.discard")}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}{{(scenario=="draw-watch"?"":",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":"+(scenario=="paid-death"?"20":"1")+"}")}}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Health,Conditional,Watch};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="陆郁生原生命令边界夹具",optionLabels=id==Health||id==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:lys-pick-owner","本人公开评分","固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            var skills=new List<string>{Driver};if(scenario!="draw-loss")skills.Add(Cang);skills.Add(scenario is"paid-loss" or"draw-loss"?Conditional:Run);if(scenario is"draw-watch" or"paid-loss" or"paid-death" or"draw-loss")skills.Add(Watch);if(scenario=="lion")skills.Add(Health);
            b.AddGeneral(new(Owner,"陆郁生真实本人","ol-lu-yu-sheng","fixture:lys-pick-owner","wu",8,skills,GeneralGender.Female));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:lys-peer-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {var pick="fixture:lys-pick-"+index;b.AddSkill(new(pick,"其他公开评分","固定顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100003d-index)});b.AddGeneral(new(id,"其他真实角色","supporter",pick,"wu",8,[Quiet]));}
            b.AddCard(new("fixture:lys-lion","白银狮子","装备牌","真实原生移除回复",CardKind.SilverLion));b.AddCard(new("fixture:lys-ox","木牛流马","装备牌","真实原生存粮与移除",CardKind.WoodenOx));
            b.AddDeck(new("fixture:lys-deck","固定小实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scenario is"reshuffle" or"zero-draw"?28:96).Select(i=>new ContentDeckPhysicalCard((scenario is"lion" or"ox")&&i%5==0?scenario=="ox"?"fixture:lys-ox":"fixture:lys-lion":"standard:slash",scenario=="spades"?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new(scenario.EndsWith("legacy",StringComparison.Ordinal)?Legacy:Mode,"陆郁生原生规则",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:lys-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
