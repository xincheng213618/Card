using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class DamageTargetBudgetChecks
{
    private const string Skill = "ol:luanzhan", Driver = "fixture:damage-budget-driver", Watch = "fixture:damage-budget-watch";
    private const string Owner = "fixture:damage-budget-owner", Quiet = "fixture:damage-budget-quiet", Mode = "identity:classic-damage-budget", Loss = "fixture:damage-budget-loss";
    public static void ActualDamageOccurrencesSelfSourceLessAndTwoPoints()
    {
        var (g, registry) = Start("damage");
        foreach (var (id, targets, source, target, amount) in new[] {
            ("self-two", Array.Empty<int>(), (int?)0, 0, 2), ("incoming-two", new[]{1}, (int?)1, 0, 2),
            ("outgoing-two", new[]{1}, (int?)0, 1, 2), ("source-less", Array.Empty<int>(), (int?)null, 0, 1) })
        {
            var before = Count(g); var facts = E<DamageTargetBudgetGainedEvent>(g).Count(); Use(g, id, targets);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            Need(Count(g) == before + 1, "Each actual native damage occurrence gains one, including two HP points and the one OR self-damage participant.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
            var issued = E<DamageTargetBudgetGainedEvent>(g).Skip(facts).Single();
            Need(issued.DamageSourceSeat == source && issued.DamageTargetSeat == target && issued.DamageAmount == amount && Count(g) == before + 1,
                "Cold recovery executes the original real damage child once, retaining null source and exact amount.");
        }
        var old = Count(g); Use(g, "foreign-self", [1]); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
        Need(Count(g) == old && E<DamageTargetBudgetGainedEvent>(g).Count() == 4, "A real damage occurrence involving neither owner direction does not manufacture a mark.");
    }
    private static string Embedded(string suffix,string package="ol-ta-dun") { using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{package}.{suffix}")!; using var r=new StreamReader(s); return r.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>g.CreateSnapshot(0).PendingDecision;
    private static int Count(GameEngine g)=>(V(g,0).Markers??[]).SingleOrDefault(m=>m.Kind==PlayerMarkerKind.LuanZhan)?.Count??0;
    private static IEnumerable<T> E<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool good,string message) { if(!good)throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g,GameCommand c) { var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted,r.Error?.Message??"Real budget command rejected."); }
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose) { var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision)); }
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var i=0;i<120;i++)
        {
            if(done())return;Need(g.State.Winner==Winner.None,"The native budget fixture ended prematurely.");
            if(P(g) is {PlayerSeat:0} p)
            {
                if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if(p.Kind==DecisionKind.SelectTargetCard)Answer(g,_=>true);
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else throw new InvalidOperationException($"Unexpected real budget prompt: {p.Kind} {p.Prompt}");
            }
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The native budget fixture exceeded its bounded command sequence.");
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new { Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements, Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(), Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r) { var c=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(c),"The actual restored engine preserves frozen X, ordered added targets and native continuation.");return c; }
    private static (GameEngine,ContentRegistry) Start(string scenario,bool stopAtVirtual=false)
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
        Accept(g,new StartGameCommand());Reach(g,()=>P(g) is {Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));
        if(stopAtVirtual)Reach(g,()=>P(g)?.SkillPrompt?.SkillId==Driver&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));else Play(g);return(g,registry);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest {get;}=new("damage-target-budget-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var content=SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json"));foreach(var(id,p)in content.Programs)b.AddSkill(new(id,content.Presentations[id].Name,content.Presentations[id].Description){Program=p,ProgramPresentation=content.Presentations[id]});
            if(scenario=="late-gain")
            {
                var benefit=SkillProgramCatalog.Load(Embedded("rules.json","ol-ma-yun-lu"),Embedded("presentation.json","ol-ma-yun-lu"));
                foreach(var(id,p)in benefit.Programs)b.AddSkill(new(id,benefit.Presentations[id].Name,benefit.Presentations[id].Description){Program=p,ProgramPresentation=benefit.Presentations[id]});
            }
            var rules=JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"self-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
                {"id":"incoming-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"outgoing-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"foreign-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"wide","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"unlimited"}]},
                {"id":"draw20","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}
            ]}
            """)!;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实规则准备",description="实际damage及native装备"},[Quiet]=new{name="其他角色",description="真实限制主动杀"}};
            if(scenario is "damage" or "loss")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-child","window":"{{(scenario=="damage"?"afterDamageApplied":"cardUseTargetsFinalized")}}",{{(scenario=="damage"?"\"subject\":\"any\",\"damageOccurrence\":\"perDamage\"":"\"ownerRelation\":\"actor\"" )}},"priority":{{(scenario=="damage"?-100:950)}},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}{{(scenario=="loss"?",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}":scenario=="late-gain"?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}":"")}}]}]}
                """));labels[Watch]=new{name="实际子窗",description="原生边界实际选择与失源",optionLabels=new Dictionary<string,string>{{"continue","继续"}}};
            }
            if(scenario=="late-gain")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-first-use-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.first-slash-duel.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]}
                """)); labels[Watch]=new{name="实际首用收益子窗",description="凤魄已支付摸牌的真实获得牌子窗伤害",optionLabels=new Dictionary<string,string>{{"continue","继续"}}};
            }
            if(scenario=="loss")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-dying-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"return","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Skill}}"],"sourceBind":"standard:none"}]}]}
                """)); labels[Loss]=new{name="实际濒死失源",description="实际HP子窗口恢复并失去乱战",optionLabels=new Dictionary<string,string>{{"continue","继续"}}};
            }
            if(scenario=="red")
            {rules["skills"]![0]!["cardPolicies"]=JsonNode.Parse("""[{"id":"effective-red","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]""");}
            if(scenario=="actor")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-actor-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":950,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}
                """)); labels[Watch]=new{name="实际使用者变更",description="乱战完成后真实变更使用者"};
            }
            if(scenario=="virtual")rules["skills"]![0]!["triggers"]=JsonNode.Parse("""
                [{"id":"real-bank-before-play","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"damage","target":"owner","amount":1},{"op":"damage","target":"owner","amount":1},{"op":"damage","target":"owner","amount":1},{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"unlimited"}]},
                 {"id":"native-virtual","window":"playPhaseStarting","subject":"owner","usageScope":"game","usageLimit":1,"optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]
                """);
            var fixture=SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in fixture.Programs)b.AddSkill(new(id,fixture.Presentations[id].Name,fixture.Presentations[id].Description){Program=p,ProgramPresentation=fixture.Presentations[id],SelectionWeights=id==Quiet?Enum.GetValues<Role>().ToDictionary(r=>r,_=>10000d):new Dictionary<Role,double>()});
            b.AddGeneral(new(Owner,"实际标记拥有者","supporter",Driver,"qun",12,scenario == "loss"?[Skill,Watch,Loss]:scenario == "late-gain"?[Skill,Watch,"ol:fengpo"]:scenario is "damage" or "actor"?[Skill,Watch]:[Skill]));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:damage-budget-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"实际其他角色","supporter",Quiet,"wei",12,[]));
            b.AddCard(new("fixture:damage-budget-blade","古锭刀","装备牌","原生范围2武器，无双手牌转杀",CardKind.GudingBlade));
            b.AddCard(new("fixture:damage-budget-borrowed","借刀杀人","锦囊牌","实际原生借刀杀人",CardKind.BorrowedSword));
            var cards=scenario switch{"borrowed"=>new[]{"fixture:damage-budget-borrowed","fixture:damage-budget-blade"},"chain"=>["standard:iron_chain"],"red"=>["standard:snatch"],_=>["standard:slash"]};
            b.AddDeck(new("fixture:damage-budget-deck","固定实体牌堆",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(cards[i%cards.Length],scenario == "late-gain" ? Suit.Diamond : Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"真实标记预算",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:damage-budget-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
