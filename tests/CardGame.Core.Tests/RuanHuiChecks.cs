using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RuanHuiChecks
{
    private const string Ming="ol:mingcha",Jing="ol:jingzhong",Source="fixture:rh-source",Player="fixture:rh-player",Driver="fixture:rh-driver",Watch="fixture:rh-watch",Health="fixture:rh-health",Mode="identity:classic-ruan-hui-fixture";
    public static void MingchaPublicRevealNormalReplacementZeroAndReshuffleCold()
    {
        foreach(var scene in new[]{"low","high","zero","short","reshuffle"})
        {
            var(g,r)=Start(scene);Reach(g,()=>Stage(g,RuanHuiStage.DrawChoice));var f=Root(g);var entities=f.RuanHui!.Entities.ToArray();var before=V(g,0).HandCount;
            Need(entities.Length==(scene=="short"?1:3)&&Enumerable.Range(0,4).All(s=>entities.All(e=>g.CreateSnapshot(s).PublicRevealedCards.Any(c=>c.Id==e.CardId))),"Mingcha really publishes its bounded top-three entities to every player before choosing whether to replace normal draw.");
            var normalOrder=g.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.DrawPile).OrderByDescending(c=>c.ZoneIndex).Take(2).Select(c=>c.CardId).ToArray();
            var state=State(g);var p=P(g)!;Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted&&State(g)==state,"Another seat cannot choose the source's already-public draw replacement.");g=Cold(g,r);
            var replace=scene is"low"or"zero"or"short";Answer(g,c=>c.Parameters.GetValueOrDefault("branch")== (replace?"replace":"normal"));
            if(replace){Reach(g,()=>Stage(g,RuanHuiStage.TargetChoice));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")== (scene=="zero"?"target.1":"skip"));}
            Play(g);var gained=g.CardMovements.Where(m=>m.Reason.Value=="skill-program.ruan-hui.mingcha.gain"&&m.To==CardLocation.Hand(0)).ToArray();
            Need(gained.Length==(scene=="low"?3:scene=="short"?1:0)&&V(g,0).HandCount==before+(scene=="low"?3:scene is"zero"or"short"?1:2),"The real optional replacement obtains only actual <=8 reveal entities, while a zero-match replacement can take its separate current-text random HE benefit.");
            if(!replace){var actual=g.CardMovements.Where(m=>m.Reason==CardMoveReasons.Draw&&m.To==CardLocation.Hand(0)).Select(m=>m.CardId).ToArray();Need(actual.Take(normalOrder.Length).SequenceEqual(normalOrder)&&entities.All(e=>g.CardMovements.Any(m=>m.CardId==e.CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile)),"Declining replacement settles the revealed cards and preserves the original native order of subsequent normal draws.");}
            if(scene=="reshuffle")Need(g.CardMovements.Any(m=>m.Reason==CardMoveReasons.Reshuffle),"The genuine normal draw reshuffles actual reveal cleanup cards when the original pile runs out.");
            Need(E<RuanHuiCompletedEvent>(g).Count(e=>e.FrameId==f.Id)==1,"The exact public reveal and draw plan finish once after cold recovery.");_=Cold(g,r);
        }
    }
    private static int Human(GameEngine g)=>Enumerable.Range(0,4).Single(s=>V(g,s).IsHuman);
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.RuanHui is not null);
    private static bool Stage(GameEngine g,RuanHuiStage s)=>P(g)?.SkillPrompt?.SkillId is Ming or Jing&&g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.RuanHui?.Stage==s;
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected real RuanHui command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==Human(g)&&c is not null,"Missing exact human choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Continue(GameEngine g){if(P(g)!.PlayerSeat==Human(g))Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(Human(g),g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g,[CallerMemberName]string caller="",[CallerLineNumber]int line=0)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard}p&&p.PlayerSeat==Human(g),$"{caller}:{line} actual human Play");
    private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string caller="",[CallerLineNumber]int line=0,[CallerArgumentExpression(nameof(done))]string? condition=null)
    {
        var boundary=$"{caller}:{line} {condition}";
        for(var i=0;i<320;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"RuanHui ended before boundary "+boundary+": "+Diagnostic(g));var p=P(g);
            if(p is null||p.PlayerSeat!=Human(g)){Accept(g,new AdvanceOneStepCommand(g.Revision));continue;}
            if(p.Kind==DecisionKind.PlayCard)End(g);
            else if(p.SkillPrompt?.SkillId is Watch or Health)Continue(g);
            else if(Stage(g,RuanHuiStage.DrawChoice))Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="normal");
            else if(Stage(g,RuanHuiStage.TargetChoice)&&p.SkillPrompt?.SkillId==Ming)Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="skip");
            else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)Answer(g,c=>c.Cards.Count==0);
            else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else throw new InvalidOperationException("Unexpected RuanHui native choice while waiting for "+boundary+": "+Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded RuanHui boundary "+boundary+": "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Winner,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).GeneralId,V(g,s).IsHuman,V(g,s).Hp,V(g,s).HandCount}),Prompt=P(g)is{}p?new{p.Kind,p.PlayerSeat,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(5).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(5).Select(f=>new{f.Id,Type=f.GetType().Name}),Discards=E<JingzhongDiscardEvent>(g).TakeLast(8),Leases=E<JingzhongLeaseEvent>(g).TakeLast(4),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId==Jing).TakeLast(4),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var c=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(c),"Cold replay preserves all four privacy views, phase leases, original material invoices, seeded random choice and exact native child returns.");return c;}
    private static(GameEngine,ContentRegistry)Start(string scene,bool jing=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene,jing));var human=jing?1:0;var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=human,HumanRole=scene=="random-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral},$"Start[{scene}] select general");Accept(g,new SelectGeneralCommand(human,jing?Player:Source,g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(string scene,bool jing):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-ruan-hui",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:ruan-hui");
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"slash-room","query":"slashLimit","operation":"add","value":8,"priority":0}],"viewAs":[
             {"id":"one","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"two","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"dodge","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true},
             {"id":"nullification","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}],"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
             {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
             {"id":"pair","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two","outputKind":"slash"}]},
             {"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"discard-equipment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["equipment"],"resultBind":"equipment"},{"op":"moveBoundCards","target":"owner","sourceBind":"equipment","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
             {"id":"transfer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["equipment"],"count":1,"destination":"selectedTargetCorrespondingZone","awaitMovementTriggers":true}]}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}"""};
            var emptyPeer=scene is"lion"or"ox"or"random-loss"or"random-death"?",{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"peer-hand\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"peer-hand\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}":"";
            defs.Add($$"""{"id":"fixture:rh-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}{{emptyPeer}}]}]}""");
            if(jing)defs.Add("""{"id":"fixture:rh-small-limit","revision":1,"modifiers":[{"id":"native-limit","query":"handLimit","operation":"add","value":-19,"priority":0}]}""");
            if(scene is"jing-loss"or"random-loss")defs.Add($$"""{"id":"fixture:rh-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{(jing?Jing:Ming)}}","minimumHp":{{(jing?20:21)}}}]}""");
            if(scene is"jing-loss"or"jing-death"or"random-loss"or"random-death")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{(jing?"skill-program.ruan-hui.jingzhong.gain":"skill-program.ruan-hui.mingcha.random")}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scene.EndsWith("death",StringComparison.Ordinal)?20:1)}}}]}]}""");
            if(scene=="responses")
            {
                defs.Add("""{"id":"fixture:rh-trick","revision":1,"triggers":[{"id":"counter-trick","window":"playEnding","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"owner"},"outputKind":"dismantlement"}]}]}""");
            }
            if(scene=="actor")defs.Add("""{"id":"fixture:rh-actor","revision":1,"triggers":[{"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="阮慧真实原生夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:rh-pick","固定来源","固定选将公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>jing?10000000d:-10000000d)});
            var sourceSkills=new List<string>{scene=="random-loss"?"fixture:rh-conditional":Ming,scene=="jing-loss"?"fixture:rh-conditional":Jing,Health};
            if(jing){sourceSkills.Add("fixture:rh-small-limit");sourceSkills.Add("fixture:rh-quiet");}else sourceSkills.Add(Driver);if(scene is"jing-loss"or"jing-death"or"random-loss"or"random-death")sourceSkills.Add(Watch);
            b.AddGeneral(new(Source,"实际阮慧","ol-ruan-hui","fixture:rh-pick","wei",scene=="random-death"?19:20,sourceSkills,GeneralGender.Female));
            var humanSkills=new List<string>{Driver,Health};if(scene=="actor")humanSkills.Add("fixture:rh-actor");if(scene=="responses")humanSkills.Add("fixture:rh-trick");b.AddGeneral(new(Player,"实际受约束角色","supporter",jing?"standard:none":"fixture:rh-quiet","qun",20,jing?humanSkills:[Health]));
            var peers=new[]{"fixture:rh-peer-a","fixture:rh-peer-b"};foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter","fixture:rh-quiet","qun",20,[Health]));
            var kind=scene=="ox"?CardKind.WoodenOx:scene is"lion"or"random-loss"or"random-death"?CardKind.SilverLion:CardKind.Crossbow;var rank=scene is"high"or"zero"or"reshuffle"||jing?13:7;
            b.AddCard(new("fixture:rh-card","真实原始装备","装备牌","固定公开实体",kind));b.AddDeck(new("fixture:rh-deck","阮慧固定小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,scene=="short"?17:scene=="reshuffle"?20:100).Select(_=>new ContentDeckPhysicalCard("fixture:rh-card",Suit.Spade,rank)).ToArray()});
            b.AddMode(new(Mode,"阮慧原生行为",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:rh-deck",GeneralCandidateCount:4,GeneralPoolIds:[Source,Player,..peers]));
        }
    }
}
