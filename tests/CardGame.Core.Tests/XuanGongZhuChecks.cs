using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class XuanGongZhuChecks
{
    private const string Owner="fixture:xuan-owner",Driver="fixture:xuan-driver",Quiet="fixture:xuan-quiet",Mode="identity:classic-xuan-fixture";
    public static void ActualQimeiForeignHandChangesSharedQuotaAndDamageHpCold()
    {
        var(g,r)=Start(role:Role.Rebel);var target=Enumerable.Range(1,3).First(s=>V(g,s).Role!=Role.Lord);Pair(g,target);Play(g);if(V(g,0).HandCount==V(g,target).HandCount){Use(g,"draw-self",[]);Play(g);}var hand=V(g,0).HandCount;var peer=V(g,target).HandCount;
        Need(hand==peer+2,"The fixed native deck provides the exact two-card hand gap without a seed search.");
        Use(g,"draw-other",[target]);Play(g);g=Cold(g,r);
        Need(V(g,0).HandCount==hand+1 && E<QimeiEqualityConsumedEvent>(g).Single().Receipt.Attribute==QimeiAttribute.Hand && E<QimeiEqualityConsumedEvent>(g).Single().Receipt.ChangedSeat==target && E<QimeiEqualityConsumedEvent>(g).Single().Receipt.BeneficiarySeat==0,
            "A real foreign endpoint gain equalizes hands and awards the other endpoint one actual draw.");
        Use(g,"discard-one",[]);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards")==true);Answer(g,c=>c.Cards.Count==1);Play(g);
        Need(E<QimeiEqualityConsumedEvent>(g).Count(e=>e.Receipt.Attribute==QimeiAttribute.Hand)==1,"Re-equality after the other endpoint's native loss shares the same hand quota for this actual turn.");
        Use(g,"damage-other",[target]);Play(g);g=Cold(g,r);
        Need(V(g,target).Hp==V(g,0).Hp && E<QimeiEqualityConsumedEvent>(g).Count(e=>e.Receipt.Attribute==QimeiAttribute.Hp && e.Receipt.ChangedSeat==target && e.Receipt.BeneficiarySeat==0)==1,
            "An actual foreign endpoint damage delta receives an independent once-turn HP payoff without requiring that endpoint to own Qimei.");
    }
    private static void Pair(GameEngine g,int target)
    {Reach(g,()=>Optional(P(g),"ol:qimei"));Activate(g);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target")==true);Answer(g,c=>c.Targets.SequenceEqual([target]));}
    private static bool Optional(PendingDecision? p,string id)=>p?.SkillPrompt?.SkillId==id && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual Xuan command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets,g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<180;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"The fixed native Xuan fixture ended before its expected boundary.");var p=P(g);
            if(p is{PlayerSeat:0}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"take-damage"or"let-die");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Xuan native fixture bound exceeded. "+JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Cold accepted commands reconstruct the exact concealed template, native appearance, private choices and paid phase/pair receipts.");return cold;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-xuan-gong-zhu.{suffix}")!;using var r=new StreamReader(s);return r.ReadToEnd();}
    private static(GameEngine,ContentRegistry)Start(Role role=Role.Lord,bool peerHidden=false,string scenario="normal")
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(peerHidden,scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=role,ModeId=Mode,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=8},registry);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,registry);
    }
    private sealed class Fixture(bool peerHidden,string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("xuan-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
            {"id":"remove-qimei","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:qimei"],"sourceBind":"standard:none"}]},
            {"id":"reacquire-qimei","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:qimei"]}]},
            {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"draw-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
            {"id":"draw-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
            {"id":"damage-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
            {"id":"clamp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-2}]},
            {"id":"discard-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"discarded"},{"op":"moveBoundCards","target":"owner","sourceBind":"discarded","destination":"discardPile"}]}]},
            {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var node=JsonNode.Parse(rules)!;var labels=new Dictionary<string,object>{[Driver]=new{name="native mutation driver",description="native"},[Quiet]=new{name="quiet",description="quiet"}};
            if(scenario.StartsWith("paid-draw",StringComparison.Ordinal))
            {
                node["skills"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"fixture:xuan-watch","revision":1,"triggers":[{"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,
                "movementReasons":["skill-program.ol:zhuiji-tj.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"watch","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """));labels["fixture:xuan-watch"]=new{name="paid native child",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};
            }
            if(scenario=="paid-draw-loss")
            {
                node["skills"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"fixture:xuan-loss","revision":1,"triggers":[{"id":"source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zhuiji-tj"],"sourceBind":"standard:none"}]}]}
                """));labels["fixture:xuan-loss"]=new{name="native rescue and loss",description="native"};
            }
            if(scenario=="inserted")
            {
                node["skills"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"fixture:xuan-extra","revision":1,"triggers":[{"id":"insert","window":"playEnding","subject":"owner","priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}
                """));labels["fixture:xuan-extra"]=new{name="native inserted play",description="native"};
            }
            Add(SkillProgramCatalog.Load(node.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
            var skills=peerHidden?new List<string>{"ol:qimei"}:new List<string>{"ol:gaoling","ol:qimei","ol:zhuiji-tj"};if(scenario.StartsWith("paid-draw",StringComparison.Ordinal))skills.Add("fixture:xuan-watch");if(scenario=="paid-draw-loss")skills.Add("fixture:xuan-loss");if(scenario=="inserted")skills.Add("fixture:xuan-extra");
            b.AddGeneral(new(Owner,"真实宣公主规则","supporter",Driver,"jin",3,skills,GeneralGender.Female));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:xuan-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"真实其他角色","supporter",Quiet,"wei",scenario=="peer-two-hp"?2:peerHidden?3:4,peerHidden?["ol:gaoling","ol:qimei","ol:zhuiji-tj"]:[],GeneralGender.Female));
            b.AddDeck(new("fixture:xuan-deck","fixed native entities",4,0,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:dodge",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"actual Xuan identity",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xuan-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
