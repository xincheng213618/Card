using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NiuFuChecks
{
    private const string Shi="ol:shisuan",Zong="ol:zonglue",Owner="fixture:nf-owner",Driver="fixture:nf-driver",Gain="fixture:nf-gain",Cost="fixture:nf-cost",Health="fixture:nf-health",Mode="identity:classic-niu-fu-fixture";
    private const string Take="skill-program.niu-fu.zonglue.obtain",Gift="skill-program.niu-fu.shisuan.gift";
    public static void ShisuanSourceBranchesNullDeadAndEmptyCold()
    {
        foreach(var branch in new[]{"lose-hp","turn-over"})
        {
            var(g,r)=Start();Use(g,"incoming",[0]);Reach(g,()=>Branch(g,"recast"));var root=Root(g);var material=V(g,0).Hand[0].Id;var hand=V(g,0).HandCount;
            Need(P(g)is{PlayerSeat:0,IsPrivate:true}&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"Only the real damaged owner chooses a concealed original HE recast material.");
            g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([material]));Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Gain);
            Need(V(g,0).HandCount==hand&&E<CardRecastEvent>(g).Count(e=>e.CardId==material)==1,"The actual one-card recast discards an original entity and draws one before its gain child.");
            g=Cold(g,r);Continue(g);Reach(g,()=>Branch(g,branch));var hp=V(g,0).Hp;var down=V(g,0).IsFaceDown;g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch);
            if(branch=="lose-hp")
            {
                Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Health);Need(V(g,0).Hp==hp-1&&g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w=>w.Change.Kind==HpChangeKind.Loss&&w.Change.TargetSeat==0),"The actual source HP loss opens its genuine native health child.");g=Cold(g,r);Continue(g);
            }
            Play(g);Need((branch=="lose-hp"?V(g,0).Hp==hp-1:V(g,0).IsFaceDown!=down)&&E<NiuFuSourceChoiceEvent>(g).Count(e=>e.FrameId==root.Id&&e.Branch==branch)==1&&E<NiuFuCompletedEvent>(g).Count(e=>e.FrameId==root.Id)==1,"Cold resumes the source's exact HP-loss or turn-over branch once.");_=Cold(g,r);
        }
        foreach(var scenario in new[]{"source-null","source-dead","empty"})
        {
            var(g,r)=Start(scenario);if(scenario=="empty"){Use(g,"empty");Play(g);Need(V(g,0).HandCount==0&&V(g,0).Equipment.Count==0,"The inability to recast is reached through a genuine native discard, without editing state.");}
            Use(g,scenario=="source-null"?"null":"incoming",scenario=="source-null"?[]:[1]);
            if(scenario!="empty")
            {
                Reach(g,()=>Branch(g,"recast"));g=Cold(g,r);Answer(g,c=>c.Cards.Count==1);Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Gain);g=Cold(g,r);Continue(g);
                if(scenario=="source-dead"){Reach(g,()=>Action(g,"select-target"));Answer(g,c=>c.Targets.SequenceEqual([1]));}
            }
            else
            {
                Reach(g,()=>Branch(g,"turn-over"));Need(E<CardRecastEvent>(g).Length==0&&P(g)!.PlayerSeat==1,"An empty recast does not fabricate draw but still permits the real living source's mandatory branch.");g=Cold(g,r);
            }
            Play(g);Need(E<NiuFuStartedEvent>(g).Length==1&&E<NiuFuCompletedEvent>(g).Length==1&&
                (scenario=="empty"?V(g,1).IsFaceDown:E<NiuFuSourceChoiceEvent>(g).Length==0)&&
                (scenario!="source-null"||E<NiuFuStartedEvent>(g).Single().DamageSourceSeat is null)&&
                (scenario!="source-dead"||!V(g,1).IsAlive),"Source-less or genuinely dead-source damage retains the owner's recast and skips unavailable source choices; empty HE retains the living source choice.");_=Cold(g,r);
        }
    }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.NiuFu is not null);
    private static bool Branch(GameEngine g,string branch)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="niu-fu"&&c.Parameters.GetValueOrDefault("branch")==branch)==true;
    private static bool Action(GameEngine g,string action)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")==action)==true;
    private static bool Option(GameEngine g,string option)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")==option)==true;
    private static bool Offer(GameEngine g,string skill)=>P(g)?.SkillPrompt?.SkillId==skill&&Action(g,"activate");
    private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real NiuFu command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var options=p.Choices.Where(predicate).ToArray();Need(V(g,p.PlayerSeat).IsHuman&&options.Length>0,"Missing exact human native choice: "+Diagnostic(g));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,options[0].Id,g.Revision));}
    private static void Continue(GameEngine g){if(V(g,P(g)!.PlayerSeat).IsHuman)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<260;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"NiuFu fixture ended before boundary: "+Diagnostic(g));var p=P(g);
            if(p is not null&&V(g,p.PlayerSeat).IsHuman)
            {
                if(p.Kind==DecisionKind.PlayCard)Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));
                else if(p.SkillPrompt?.SkillId is Gain or Cost or Health&&Option(g,"continue"))Continue(g);
                else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die");
                else if(Action(g,"skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else throw new InvalidOperationException("Unexpected NiuFu native choice: "+Diagnostic(g));
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing NiuFu native boundary: "+Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.Winner,Prompt=P(g)is{}p?new{p.Kind,p.PlayerSeat,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Id,c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name}),Facts=g.Events.TakeLast(4).Select(e=>e.Payload.ToString())});
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves all private player views, original material invoices, actual native children and exact accepted commands.");return restored;}
    private static(GameEngine,ContentRegistry)Start(string scenario="plain")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario is"recast-death"or"zone-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(scenario is"virtual"or"legacy")Reach(g,()=>Offer(g,Driver));else Play(g);return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-niu-fu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,"ol:niu-fu");
            var virtualTrigger=scenario is"virtual"or"legacy"?$$"""{"id":"{{scenario}}","window":"playPhaseStarting","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":{{(scenario=="virtual"?"true":"false")}}}]}""":"";
            var defs=new List<string>{$$"""
            {"id":"{{Driver}}","revision":1,"viewAs":[
             {"id":"one","inputKinds":[],"inputSuits":[],"sourceZones":["hand","equipment"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"two","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
             {"id":"fire","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"usesPerPhase":1}],
             "activations":[
             {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
             {"id":"null","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":2}]},
             {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
             {"id":"cardless","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
             {"id":"prepare-zones","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
             {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
             {"id":"pair","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two","outputKind":"slash"}]},
             {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]},
             {"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":5}]}],"triggers":[{{virtualTrigger}}]}
            """,$$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]},{"id":"real-loss","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}""","""{"id":"fixture:nf-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""};
            var tail=scenario is"recast-loss"or"zone-loss"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"loss\",\"options\":[{\"id\":\"after-loss\"}]}":scenario=="zone-death"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}":scenario=="source-dead"?",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}":"";
            defs.Add($$"""{"id":"{{Gain}}","revision":1,"triggers":[{"id":"real-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.draw","{{Take}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{tail}}]}]}""");
            if(scenario=="recast-death")defs.Add($$"""{"id":"{{Cost}}","revision":1,"triggers":[{"id":"real-recast-cost","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["card.recast.discard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
            if(scenario is"recast-loss"or"zone-loss")defs.Add($$"""{"id":"fixture:nf-conditional","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{(scenario=="recast-loss"?Shi:Zong)}}","minimumHp":{{(scenario=="recast-loss"?16:17)}}}]}""");
            var labels=defs.Select(d=>JsonNode.Parse(d)!).ToDictionary(d=>d["id"]!.GetValue<string>(),d=>new{name=d["id"]!.GetValue<string>(),description="真实原生牛辅夹具",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>d[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id=>id,id=>id)});
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实原生夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new("fixture:nf-pick","本人选将","固定公开评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000d)});b.AddSkill(new("fixture:nf-unused-pick","备用武将牌","固定备用评分"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-200000d)});
            var own=new List<string>{Driver,Gain,Health,scenario=="recast-loss"?"fixture:nf-conditional":Shi,scenario=="zone-loss"?"fixture:nf-conditional":Zong};if(scenario=="recast-death")own.Add(Cost);
            b.AddGeneral(new(Owner,"实际牛辅","ol-niu-fu","fixture:nf-pick","qun",16,own,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:nf-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"实际其他角色","supporter","fixture:nf-quiet","qun",12,[Health]));b.AddGeneral(new("fixture:nf-weapon-source","真实备用武将武器","supporter","fixture:nf-unused-pick","qun",4,[]));
            var kind=scenario switch{"lion-recast"or"source-lion"=>CardKind.SilverLion,"ox-recast"=>CardKind.WoodenOx,"plain-slash"or"multi-slash"=>CardKind.Slash,"elemental-slash"=>CardKind.ThunderSlash,"chain"=>CardKind.IronChain,_=>CardKind.Crossbow};
            b.AddCard(new("fixture:nf-card","真实实体材料",kind is CardKind.Crossbow or CardKind.SilverLion or CardKind.WoodenOx?"装备牌":kind==CardKind.IronChain?"锦囊牌":"基本牌","原生实体",kind));
            var zoneFixture=scenario is"zones"or"zone-loss"or"zone-death";if(zoneFixture)b.AddCard(new("fixture:nf-delayed","真实乐不思蜀","锦囊牌","实际使用并移入判定区的原始延时实体",CardKind.Indulgence));
            b.AddDeck(new("fixture:nf-deck","固定真实实体",4,2,[]){PhysicalCards=Enumerable.Range(0,zoneFixture?40:80).Select(i=>new ContentDeckPhysicalCard(zoneFixture&&i%2==1?"fixture:nf-delayed":"fixture:nf-card",Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"牛辅真实原生边界",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Loyalist)]=1},"fixture:nf-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers,"fixture:nf-weapon-source"]));
        }
    }
}
