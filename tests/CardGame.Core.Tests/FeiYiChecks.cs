using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FeiYiChecks
{
    private const string Owner="fixture:fy-owner",Driver="fixture:fy-driver",Quiet="fixture:fy-quiet",Peer="fixture:fy-peer",Watch="fixture:fy-watch",Conditional="fixture:fy-conditional",Swap="fixture:fy-swap";
    private const string Mode="identity:classic-fei-yi-fixture",Yanru="ol:yanru",Hezhong="ol:hezhong";
    public static void YanruIndependentParityMinimumAndZeroHandCold()
    {
        var(g,r)=Start();var initial=V(g).Hand.Count;Need(initial%2==0,"The verified seed enters an actual even-hand Play.");
        Begin(g);Need(Current(g) is{Odd:false,Minimum:3},"Even Yanru freezes floor half of the actual six hand cards before drawing.");
        Pick(g,3);g=Cold(g,r);Finish(g);Play(g);
        Need(V(g).Hand.Count==initial&&E<FeiYiMovementIssuedEvent>(g).Where(e=>e.FrameId==E<FeiYiYanruPaidEvent>(g).Single().FrameId).Select(e=>e.Stage).SequenceEqual([FeiYiStage.DiscardChildren,FeiYiStage.DrawChildren]),"Even Yanru pays three genuine hand entities before the three-card native draw.");
        RejectYanru(g);Use(g,"give",[V(g).Hand[0].Id],[1]);Play(g);Begin(g);Reach(g,()=>Current(g)is{Stage:FeiYiStage.DiscardChoice});
        Need(Current(g)is{Odd:true,HandCount:8,Minimum:4},"Odd Yanru first draws three and then freezes the true post-child hand count, rather than the original five.");
        Need(Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"The discard selection remains private to the real owner.");
        Pick(g,7);g=Cold(g,r);Finish(g);Play(g);
        Need(V(g).Hand.Count==1&&E<FeiYiYanruPaidEvent>(g).Count()==2&&E<FeiYiYanruPaidEvent>(g).Select(e=>e.Odd).Order().SequenceEqual([false,true])&&E<FeiYiMovementIssuedEvent>(g).Any(e=>e.Stage==FeiYiStage.DiscardChildren&&e.Count==7&&e.Minimum==4),"At least half permits a larger real discard; both parity budgets are independent and each paid once.");RejectYanru(g);
        var turn=g.State.TurnNumber;End(g);Reach(g,()=>g.State.TurnNumber>turn&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        Need(g.GetHumanLegalActions().Any(a=>a.ProgramSkillId==Yanru),"A new real Play has fresh parity budgets.");_=Cold(g,r);
        (g,r)=Start();Use(g,"give",V(g).Hand.Select(c=>c.Id).ToArray(),[1]);Play(g);Need(V(g).Hand.Count==0,"A native transfer empties the real hand.");Begin(g);Need(Current(g)is{Odd:false,Minimum:0},"Zero is a valid even hand with a genuine zero minimum.");g=Cold(g,r);Finish(g);Play(g);Need(V(g).Hand.Count==3&&E<FeiYiYanruPaidEvent>(g).Count()==1,"Zero-card discard does not invent material and still draws three once.");_=Cold(g,r);
    }
    private static FeiYiReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.FeiYi is not null)?.FeiYi;
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real Fei Yi command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool>pick){var p=P(g)!;var c=p.Choices.First(pick);if(p.PlayerSeat!=0){Accept(g,new AdvanceOneStepCommand(g.Revision));return;}Accept(g,new AnswerPromptCommand(0,p.PromptId,c.Id,g.Revision));}
    private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Begin(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,Yanru,"parity-draw-discard",[],[],g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,IReadOnlyList<int>?cards=null,IReadOnlyList<int>?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Pick(GameEngine g,int n){for(var i=0;i<n;i++)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="pick");}
    private static void Finish(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void RejectYanru(GameEngine g){var revision=g.Revision;var count=g.AcceptedCommands.Count;var before=State(g);var result=g.Submit(new UseProgramSkillCommand(0,Yanru,"parity-draw-discard",[],[],g.Revision,P(g)!.PromptId));Need(!result.Accepted&&g.Revision==revision&&g.AcceptedCommands.Count==count&&State(g)==before,"A consumed current-parity activation is rejected atomically.");}
    private static void Reach(GameEngine g,Func<bool>done)
    {
        for(var i=0;i<400;i++)
        {
            if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Fei Yi ended before expected boundary: "+State(g));var p=P(g);
            if(Optional(p,Hezhong)||Optional(p,"ol:fengzi"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")==true)Continue(g);
            else if(p is{PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
            else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Fei Yi prompt: "+JsonSerializer.Serialize(p));else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fei Yi did not reach the expected native boundary: "+JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves exact private choices, genuine movement/HP children, source loss, direction invoices and native effect rounds.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-fei-yi",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:fei-yi");if(scenario.Contains("fengzi",StringComparison.Ordinal))CharacterFixtureContent.Register(b,"ol:wang-rong");if(scenario=="claims")CharacterFixtureContent.Register(b,"classic:cao-cao");
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"counterspell","inputKinds":{{(scenario=="claims"?"[]":"[\"dodge\"]")}},"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true},{"id":"delayed","inputKinds":["drawTwo"],"inputSuits":[],"outputKind":"indulgence","forPlay":true,"forResponse":false}],"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                {"id":"give","minCards":1,"maxCards":20,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
                {"id":"strip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"lose-hezhong","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Hezhong}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-yanru","skillId":"{{Yanru}}","minimumHp":6}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{(scenario.StartsWith("earlier-",StringComparison.Ordinal)?"skill-program.fixture:fy-driver.GiveSelected":"skill-program.fei-yi.yanru.discard")}}"],"optional":false,"priority":100,"usageScope":"game","usageLimit":1,"effects":[{{(scenario=="loss"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},":scenario=="earlier-gain"?"{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},":scenario=="earlier-loss"?"{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"amount\":1,\"zones\":[\"hand\"],\"resultBind\":\"original\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"original\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2},":"")}}{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}{{(scenario=="death"?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}":"")}}]}]},
              {"id":"{{Peer}}","revision":1,"triggers":[{"id":"foreign-native-use","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingLeastHandCount"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"drawTwo"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"selectedTarget"},"outputKind":"drawTwo"}]}]},
              {"id":"{{Swap}}","revision":1,"triggers":[{"id":"real-role-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["unexpectedAssault"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Conditional,Watch,Peer,Swap};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="费祎真实命令夹具",optionLabels=id is Watch or Swap?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实费祎边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:fy-pick-owner","固定本人","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?100000d:-100000d)});
            var own=new List<string>{Driver,Hezhong,scenario=="loss"?Conditional:Yanru};if(scenario is"loss" or"death"||scenario.StartsWith("earlier-",StringComparison.Ordinal))own.Add(Watch);if(scenario.Contains("fengzi",StringComparison.Ordinal))own.Add("ol:fengzi");if(scenario.StartsWith("actor",StringComparison.Ordinal))own.Add(Swap);
            b.AddGeneral(new(Owner,"费祎实际本人","ol-fei-yi","fixture:fy-pick-owner","shu",5,own));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:fy-other-{i}").ToArray();
            foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
            {var pick="fixture:fy-pick-"+index;b.AddSkill(new(pick,"固定其他顺序","公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==Role.Lord?-100000d:100003d-index)});b.AddGeneral(new(id,"其他实际角色","supporter",pick,"wei",8,scenario=="offturn"&&index==0?[Peer]:scenario=="claims"?[Quiet,"classic:jianxiong"]:scenario.StartsWith("actor",StringComparison.Ordinal)&&index==1?[Quiet,Hezhong]:[Quiet]));}
            b.AddDeck(new("fixture:fy-deck","真实固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,128).Select(i=>new ContentDeckPhysicalCard(scenario=="offturn"?"standard:dodge":scenario=="claims"?"standard:arrow_barrage":scenario.StartsWith("assault",StringComparison.Ordinal)||scenario.StartsWith("actor",StringComparison.Ordinal)?"standard:unexpected_assault":"standard:draw_two",Suit.Spade,scenario=="offturn"?7:i%13+1)).ToArray()});
            b.AddMode(new(Mode,"费祎真实行为",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:fy-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
