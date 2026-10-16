using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DingShangWanChecks
{
    private const string Fudao="ol:fudao",Fengyan="ol:fengyan",Owner="fixture:dsw-owner",Driver="fixture:dsw-driver",Quiet="fixture:dsw-quiet",Late="fixture:dsw-late",Peer="fixture:dsw-peer",Watch="fixture:dsw-watch",Conditional="fixture:dsw-conditional",Health="fixture:dsw-health",Mode="identity:classic-ding-shang-wan-fixture";
    public static void OpeningZeroGiftArbitraryDiscardFixedRecordActualEndsAndOverlapCold()
    {
        var(g,r)=Start("ends");Need(V(g).HandCount==7&&Current(g)is{Stage:DingShangWanStage.OpeningGiftChoice,Maximum:3},"The real GameStarting draws exactly three after initial hands and before turn one.");
        Need(Enumerable.Range(1,3).All(seat=>g.CreateSnapshot(seat).PendingDecision is null),"Opening hand drafts remain private to their actual owner.");
        g=Cold(g,r);Finish(g);Reach(g,()=>Current(g)is{Stage:DingShangWanStage.OpeningDiscardChoice});
        Need(!E<FudaoHandRecordedEvent>(g).Any(),"Zero real gift proceeds to optional discard rather than inventing a movement or prematurely writing the record.");
        g=Cold(g,r);Finish(g);Play(g);Need(E<FudaoHandRecordedEvent>(g).Single().HandCount==7&&V(g).HandCount==9,"Zero gift and zero discard legitimately record seven before the normal two-card Draw.");
        Use(g,"give",V(g).Hand.Take(2).Select(c=>c.Id).ToArray(),[2]);Play(g);End(g);Reach(g,()=>Optional(P(g),Fudao));
        Need(g.State.CurrentSeat==0&&V(g).HandCount==7&&g.ResolutionStack.OfType<DeferredTurnEndFrame>().Any(),"The own trigger follows a real TurnEnded boundary with seven actual cards.");
        g=Cold(g,r);Activate(g);Reach(g,()=>E<DingShangWanCompletedEvent>(g).Count()>1&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DingShangWan is not null));
        Need(V(g).HandCount==9&&E<DingShangWanMovementIssuedEvent>(g).Last().Count==2,"Self overlap gives the single participant a genuine native Draw2, with no extra one-card singleton.");
        Reach(g,()=>Optional(P(g),Fudao)&&g.State.CurrentSeat!=0);var target=g.State.CurrentSeat;var ownBefore=V(g).HandCount;var targetBefore=V(g,target).HandCount;
        Need(targetBefore==7&&E<TurnEndedEvent>(g).Last().ActorSeat==target,"Foreign actual end observes the completed native Ending reset and freezes its true hand count.");
        g=Cold(g,r);Activate(g);Reach(g,()=>E<DingShangWanMovementIssuedEvent>(g).Last().Stage==DingShangWanStage.PairTargetDraw&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DingShangWan is not null));
        Need(V(g).HandCount==ownBefore+1&&V(g,target).HandCount==targetBefore+1&&E<FudaoHandRecordedEvent>(g).Single().HandCount==7,"Different participants each draw once while the original opening record stays fixed.");_=Cold(g,r);

        (g,r)=Start();Recipient(g,1);Pick(g,3);g=Cold(g,r);Finish(g);Reach(g,()=>Current(g)is{Stage:DingShangWanStage.OpeningDiscardChoice});
        Need(V(g).HandCount==4&&V(g,1).HandCount==7,"Three exact original hand entities go to the one selected other participant.");Pick(g,4);g=Cold(g,r);Finish(g);Play(g);
        Need(E<FudaoHandRecordedEvent>(g).Single().HandCount==0&&V(g).HandCount==2&&g.CardMovements.Count(m=>m.Reason.Value=="skill-program.ding-shang-wan.fudao.discard")==4,"Any discard includes the complete remaining hand; zero is a valid fixed record.");_=Cold(g,r);

        (g,r)=Start("multiple");Finish(g);Reach(g,()=>Current(g)is{Stage:DingShangWanStage.OpeningDiscardChoice});Finish(g);Play(g);
        Need(E<FudaoHandRecordedEvent>(g).Count()==4&&E<FudaoHandRecordedEvent>(g).All(e=>e.HandCount==7),"All four real GameStarting participants independently choose zero gift/discard and record seven.");
        Use(g,"give",V(g).Hand.Take(2).Select(c=>c.Id).ToArray(),[2]);Play(g);End(g);Reach(g,()=>Optional(P(g),Fudao));g=Cold(g,r);Activate(g);
        Reach(g,()=>E<DingShangWanStartedEvent>(g).Count(e=>e.Operation==SkillProgramEffectOp.DrawFudaoRecordedPair&&e.Turn==1)==4&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.DingShangWan is not null));
        Need(E<DingShangWanStartedEvent>(g).Where(e=>e.Operation==SkillProgramEffectOp.DrawFudaoRecordedPair&&e.Turn==1).Select(e=>e.Source.OwnerSeat).Distinct().Count()==4&&V(g).HandCount==12,
            "Every later matching owner retains the actual end's frozen seven-card qualification after earlier pair draws change the real turn owner's hand.");_=Cold(g,r);
    }

    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static DingShangWanReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.DingShangWan is not null)?.DingShangWan;
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand command){var x=g.Submit(command);Need(x.Accepted,x.Error?.Message??"The real command was rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> f){var p=P(g)!;Need(p.PlayerSeat==0,"Tests may answer only the real human; AI decisions must Advance.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(f).Id,g.Revision));}
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Branch(GameEngine g,string b)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==b);
    private static void Recipient(GameEngine g,int seat)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="recipient"&&c.Targets.SequenceEqual([seat]));
    private static void Pick(GameEngine g,int count){for(var i=0;i<count;i++)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pick");}
    private static void Finish(GameEngine g)=>Branch(g,"finish");
    private static void Use(GameEngine g,string id,int[]?cards=null,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<500;i++)
        {
            if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Ding Shang Wan completed before expected boundary: "+State(g));var p=P(g);
            if(p is{PlayerSeat:0}&&Optional(p,Fudao))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.Nullification or DecisionKind.RespondSlash or DecisionKind.RespondDodge})Answer(g,c=>c.Cards.Count==0);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Ding Shang Wan prompt: "+JsonSerializer.Serialize(new{Prompt=p,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount}).ToArray(),Facts=g.Events.TakeLast(15).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Thoughts=g.AiThoughts.TakeLast(5).ToArray()}));else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Ding Shang Wan did not reach the true boundary: "+State(g));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves all private selections, source and native parent proofs, actual movement/HP children and fixed record.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal",bool stopAtWatch=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        Reach(g,()=>stopAtWatch?P(g)?.SkillPrompt?.SkillId==Watch:Current(g)is{Stage:DingShangWanStage.OpeningGiftChoice});return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ding-shang-wan",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ding-shang-wan");
            if(scenario=="counterspell")CharacterFixtureContent.Register(b,"boundary:hua-xiong");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"counterspell","inputKinds":["slash"],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true},{"id":"dodge","inputKinds":["slash"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}],"activations":[
              {"id":"give","minCards":1,"maxCards":20,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
              {"id":"foreign","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
              {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
              {"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"strip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Late}}","revision":1,"triggers":[{"id":"native-end-hand","window":"turnEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"draw","target":"owner","amount":7}]}]},
             {"id":"{{Peer}}","revision":1,"triggers":[{"id":"real-retaliation","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
             {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"actual-source","skillId":"{{(scenario=="feng-loss"?Fengyan:Fudao)}}","minimumHp":{{(scenario=="feng-loss"?8:9)}}}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ding-shang-wan.draw"],"optional":false,"usageScope":"game","usageLimit":1{{(scenario=="feng-loss"?",\"condition\":{\"kind\":\"compare\",\"left\":{\"kind\":\"currentHp\"},\"operator\":\"lessThan\",\"right\":{\"kind\":\"integerConstant\",\"value\":9}}":"")}},"effects":[{{(scenario=="opening-death"?"{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]},":"")}}{"op":"loseHp","target":"owner","amount":{{(scenario=="opening-death"?20:1)}}}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"native-lion-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Late,Peer,Conditional,Watch,Health};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="丁尚涴真实原生边界夹具",optionLabels=id==Health||id==Watch&&scenario=="opening-death"?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实命令边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:dsw-pick-owner","本人公开评分","固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            var skills=new List<string>{Driver};skills.Add(scenario=="opening-loss"?Conditional:Fudao);skills.Add(scenario=="feng-loss"?Conditional:Fengyan);if(scenario is"opening-loss" or"opening-death" or"feng-loss")skills.Add(Watch);if(scenario=="equipment")skills.Add(Health);if(scenario=="counterspell")skills.Add("boundary:shizhan-current");
            b.AddGeneral(new(Owner,"丁尚涴真实本人","ol-ding-shang-wan","fixture:dsw-pick-owner","wei",8,skills,GeneralGender.Female));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:dsw-peer-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {var pick="fixture:dsw-pick-"+index;b.AddSkill(new(pick,"其他公开评分","固定顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100003d-index)});b.AddGeneral(new(id,"其他真实角色","supporter",pick,"wei",8,scenario=="multiple"?[Quiet,Fudao]:scenario=="ends"?[Quiet,Late]:scenario=="dodge"?[Quiet,Peer]:scenario=="equipment-foreign"?[Quiet,Health]:[Quiet]));}
            b.AddCard(new("fixture:dsw-lion","白银狮子","装备牌","真实原生装备移除回复",CardKind.SilverLion));
            b.AddCard(new("fixture:dsw-ox","木牛流马","装备牌","真实原生存粮及移除粮区",CardKind.WoodenOx));
            b.AddDeck(new("fixture:dsw-deck","固定小实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scenario=="reshuffle"?24:96).Select(i=>new ContentDeckPhysicalCard(scenario.StartsWith("equipment",StringComparison.Ordinal)&&i%5==0?scenario=="equipment-ox"?"fixture:dsw-ox":"fixture:dsw-lion":"standard:slash",Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new(Mode,"丁尚涴原生规则",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:dsw-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
