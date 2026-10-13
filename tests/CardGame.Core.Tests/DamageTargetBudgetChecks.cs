using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class DamageTargetBudgetChecks
{
    private const string Skill = "ol:luanzhan", Driver = "fixture:damage-budget-driver", Watch = "fixture:damage-budget-watch";
    private const string Owner = "fixture:damage-budget-owner", Quiet = "fixture:damage-budget-quiet", Mode = "identity:damage-budget";
    public static void ExactContractsAndFrozenOrderedCollections()
    {
        var rules = Embedded("rules.json"); var labels = Embedded("presentation.json"); _ = SkillProgramCatalog.Load(rules, labels);
        foreach (var (index, field, value) in new[] { (0, "damageOccurrence", "perDamagePoint"), (0, "subject", "owner"),
            (1, "window", "cardUseCommitted"), (1, "ownerRelation", "target") })
        {
            var n = JsonNode.Parse(rules)!; n["skills"]![0]!["triggers"]![index]![field] = value;
            Reject(() => SkillProgramCatalog.Load(n.ToJsonString(), labels), "Only exact mandatory occurrence and native actor designation contracts load.");
        }
        var original = new List<int> { 1, 0 }; var added = new List<int> { 2, 0 }; var result = new List<int> { 1, 0, 2, 0 };
        var parameters = new Dictionary<string,string> { ["branch"] = "add" };
        var r = new DamageTargetBudgetReceipt { OriginalTargets = original, AddedTargets = added,
            Choices = [new(new("frozen"), "frozen", [], added, parameters)] };
        var frozen = (DamageTargetBudgetResolvedEvent)CommittedEventProjection.Freeze(new DamageTargetBudgetResolvedEvent(1, r, result));
        original.Clear(); added.Clear(); result.Clear(); parameters["branch"] = "done";
        Need(frozen.Receipt.OriginalTargets.SequenceEqual([1,0]) && frozen.Receipt.AddedTargets.SequenceEqual([2,0]) &&
            frozen.ResultTargets.SequenceEqual([1,0,2,0]) && frozen.Receipt.Choices[0].Parameters["branch"] == "add" &&
            frozen.ResultTargets is ICollection<int> { IsReadOnly: true } && frozen.Receipt.Choices[0].Parameters is IDictionary<string,string> { IsReadOnly: true },
            "Committed collections preserve ordered Borrowed Sword pairs and repeated victims without writable nested policy data.");
    }
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
    public static void ActualSlashFrozenBudgetSelectionsRecoveryAndOneUse()
    {
        var (g, registry) = Start("slash"); Bank(g, 3); Use(g, "wide"); Play(g);
        var before = Enumerable.Range(1,3).Select(s => V(g,s).Hp).ToArray(); Native(g, NativeAction(g, LegalActionKind.Slash, [1]));
        Reach(g, () => Budget(P(g))); var p = P(g)!; var revision = g.Revision; var state = State(g);
        Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, revision)).Accepted && State(g) == state,
            "Only the real user may answer its public frozen budget.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        Need(!g.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, revision)).Accepted,
            "A prior selection prompt cannot select again after one actual append.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3]));
        g = Cold(g, registry); Answer(g, c => c.Parameters["branch"] == "done"); Play(g);
        var settled = E<DamageTargetBudgetSettledEvent>(g).Single(); var resolved = E<DamageTargetBudgetResolvedEvent>(g).Single();
        Need(settled.FrozenBudget == 3 && settled.FinalTargetCount == 3 && settled.Removed == 0 &&
            resolved.ResultTargets.SequenceEqual([1,2,3]) && Count(g) == 6 && Enumerable.Range(1,3).All(s => V(g,s).Hp == before[s-1]-1),
            "A frozen X adds up to three legal native targets; the whole finalized count is checked before each distinct native damage later earns its own mark.");
        OneUse(g, settled.CardUseFrameId, settled.ActionId); g = Cold(g, registry);
        Need(E<DamageTargetBudgetSettledEvent>(g).Count() == 1, "Restoring the completed actual use cannot repeat the target debit.");
        var (limited, limitedRegistry) = Start("slash"); Bank(limited, 1); Native(limited, NativeAction(limited, LegalActionKind.Slash, [1]));
        Reach(limited, () => Budget(P(limited)));
        Need(!P(limited)!.Choices.Any(c => c.Targets.SequenceEqual([2])) && P(limited)!.Choices.Any(c => c.Targets.SequenceEqual([3])),
            "A real range-one Slash offers its adjacent extra target and excludes the opposite seat, without making the budget distance-free.");
        limited = Cold(limited, limitedRegistry); Answer(limited, c => c.Targets.SequenceEqual([3]));
        Need(P(limited)!.Choices.All(c => c.Parameters["branch"] == "done"), "Frozen X=1 cannot append a second extra target.");
        limited = Cold(limited, limitedRegistry); Answer(limited, c => c.Parameters["branch"] == "done"); Play(limited);
        var small = E<DamageTargetBudgetSettledEvent>(limited).Single();
        Need(small.FrozenBudget == 1 && small.FinalTargetCount == 2 && small.Removed == 0 && Count(limited) == 3,
            "The small budget completes two actual independent target effects with no extra cost.");
        OneUse(limited, small.CardUseFrameId, small.ActionId);
    }
    public static void ActualOddHalfAfterFinalizationAndIssuedSourceLoss()
    {
        foreach (var loss in new[] { false, true })
        {
            var (g, registry) = Start(loss ? "loss" : "slash"); Bank(g, 3); Native(g, NativeAction(g, LegalActionKind.Slash, [1]));
            Reach(g, () => Budget(P(g))); g = Cold(g, registry); Answer(g, c => c.Parameters["branch"] == "done");
            if (loss)
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var receipt = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.DamageTargetBudget is not null).DamageTargetBudget!;
                Need(receipt is { FrozenBudget: 3, Completed: true, Settled: false } && Count(g) == 3,
                    "Choosing no extra target does not debit before a later genuine finalized-target child finishes.");
                g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            Play(g); var settled = E<DamageTargetBudgetSettledEvent>(g).Single();
            Need(settled is { FrozenBudget: 3, FinalTargetCount: 1, Removed: 2, NewCount: 1 } &&
                Count(g) == (loss ? 1 : 2) && (!loss || !V(g,0).Skills!.Any(s => s.Id == Skill)),
                "The final sole target removes ceil(3/2), retaining one mark; losing the issued skill does not abandon or repay its debit.");
            OneUse(g, settled.CardUseFrameId, settled.ActionId); g = Cold(g, registry);
        }
        var (later, laterRegistry) = Start("late-gain"); Bank(later,2); Native(later, NativeAction(later, LegalActionKind.Slash, [1]));
        Reach(later, () => Budget(P(later))); Answer(later, c => c.Parameters["branch"] == "done");
        Reach(later, () => P(later)?.SkillPrompt?.SkillId == Watch); later = Cold(later, laterRegistry);
        Answer(later, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(later);
        var final = E<DamageTargetBudgetSettledEvent>(later).Single();
        Need(final is { FrozenBudget: 2, MarkersBeforeSettlement: 3, FinalTargetCount: 1, Removed: 2, NewCount: 1 } && Count(later)==2,
            "Actual damage in a later designation child adds a mark after the quota is frozen; the final half observes three actual marks, then the original Slash earns another mark.");
        OneUse(later,final.CardUseFrameId,final.ActionId); later = Cold(later,laterRegistry);
    }
    public static void ActualBorrowedSwordOrderedPairsAndIronChainSelf()
    {
        var (g, registry) = Start("borrowed"); Bank(g, 3); Use(g, "equip", [1]); Play(g); Use(g, "equip", [2]); Play(g);
        Use(g, "draw20"); Play(g); Native(g, NativeAction(g, LegalActionKind.BorrowedSword, [1,0])); Reach(g, () => Budget(P(g)));
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2,0])); g = Cold(g, registry); Answer(g, c => c.Parameters["branch"] == "done"); Play(g);
        var resolved = E<DamageTargetBudgetResolvedEvent>(g).Single(); var settled = E<DamageTargetBudgetSettledEvent>(g).Single();
        Need(resolved.ResultTargets.SequenceEqual([1,0,2,0]) && settled.FinalTargetCount == 2 && settled.Removed == 2 && Count(g) == 1,
            "Native Borrowed Sword preserves both ordered pairs sharing one victim; only two actual designated weapon holders count toward X.");
        OneUse(g, settled.CardUseFrameId, settled.ActionId); g = Cold(g, registry);
        var (chain, chainRegistry) = Start("chain"); Bank(chain, 2); Native(chain, NativeAction(chain, LegalActionKind.IronChain, [1])); Reach(chain, () => Budget(P(chain)));
        chain = Cold(chain, chainRegistry); Answer(chain, c => c.Targets.SequenceEqual([0])); chain = Cold(chain, chainRegistry); Answer(chain, c => c.Parameters["branch"] == "done"); Play(chain);
        var chainSettled = E<DamageTargetBudgetSettledEvent>(chain).Single();
        Need(V(chain,0).IsChained && V(chain,1).IsChained && chainSettled.FinalTargetCount == 2 && chainSettled.Removed == 0 && Count(chain) == 2,
            "Black Iron Chain may add its real user as one legal target and executes each target on its native cursor.");
        OneUse(chain, chainSettled.CardUseFrameId, chainSettled.ActionId);
        var recast = NativeAction(chain, LegalActionKind.Recast, []); Accept(chain, new RecastCardCommand(0, recast.CardId!.Value, chain.Revision, P(chain)!.PromptId)); Play(chain);
        Need(E<DamageTargetBudgetSettledEvent>(chain).Count() == 1 && Count(chain) == 2, "Actual Iron Chain recast is not a Use and neither spends nor offers the budget.");
        chain = Cold(chain, chainRegistry);
    }
    public static void ActualZeroMaterialSlashAndEffectiveRedExclusion()
    {
        var (g, registry) = Start("virtual", stopAtVirtual:true); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true); Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, () => Budget(P(g))); g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2])); Answer(g, c => c.Parameters["branch"] == "done"); Play(g);
        var settled = E<DamageTargetBudgetSettledEvent>(g).Single(); var action = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == settled.ActionId).Action;
        Need(action.PhysicalCards.Count == 0 && E<DamageAppliedEvent>(g).Count() == 5 && Count(g) == 3 && settled.Removed == 2,
            "A legitimate native zero-material Slash keeps its original program return while its added-target cursor resolves on the same actual Use.");
        OneUse(g, settled.CardUseFrameId, settled.ActionId, physical:false); g = Cold(g, registry);
        var (red, redRegistry) = Start("red"); Bank(red,3); Native(red, NativeAction(red, LegalActionKind.Snatch, [1])); Play(red);
        Need(!E<DamageTargetBudgetOfferedEvent>(red).Any() && !E<DamageTargetBudgetSettledEvent>(red).Any() && Count(red) == 3,
            "An actual black physical Snatch rewritten to effective red cannot offer or halve the black-trick budget."); red = Cold(red, redRegistry);
    }
    private static bool Budget(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "damage-target-budget") == true;
    private static string Embedded(string suffix) { using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-ta-dun.{suffix}")!; using var r=new StreamReader(s); return r.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g)=>g.CreateSnapshot(0).PendingDecision;
    private static int Count(GameEngine g)=>(V(g,0).Markers??[]).SingleOrDefault(m=>m.Kind==PlayerMarkerKind.LuanZhan)?.Count??0;
    private static IEnumerable<T> E<T>(GameEngine g) where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool good,string message) { if(!good)throw new InvalidOperationException(message); }
    private static void Reject(Action action,string message) { try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g,GameCommand c) { var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted,r.Error?.Message??"Real budget command rejected."); }
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose) { var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision)); }
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Bank(GameEngine g,int count) { for(var i=0;i<count;i++){Use(g,"self-one");Play(g);}Need(Count(g)==count,"Actual applied damage creates the entire bank without injected state."); }
    private static LegalAction NativeAction(GameEngine g,LegalActionKind kind,IReadOnlyList<int> targets)=>g.GetLegalActions().First(a=>a.Kind==kind && (targets.Count==0||a.TargetSeats.SequenceEqual(targets)));
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId) {ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
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
    private static void OneUse(GameEngine g,long id,long action,bool physical=true)
    {
        var accepted=E<CardActionAcceptedEvent>(g).Single(e=>e.Action.ActionId==action).Action;
        Need(E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==id)==1,"One accepted native Use finishes exactly once.");
        if(physical)foreach(var c in accepted.PhysicalCards)Need(g.CardMovements.Count(m=>m.CardId==c.CardId&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished)==1,"Original physical material settles once after every native target.");
    }
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
            var rules=JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"self-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
                {"id":"incoming-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"outgoing-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"foreign-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"wide","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"add","amount":10}]},
                {"id":"draw20","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}
            ]}
            """)!;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实规则准备",description="实际damage及native装备"},[Quiet]=new{name="其他角色",description="真实限制主动杀"}};
            if(scenario is "damage" or "loss" or "late-gain")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-child","window":"{{(scenario=="damage"?"afterDamageApplied":"cardUseTargetsFinalized")}}",{{(scenario=="damage"?"\"subject\":\"any\",\"damageOccurrence\":\"perDamage\"":"\"ownerRelation\":\"actor\"" )}},"priority":{{(scenario=="damage"?-100:950)}},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}{{(scenario=="loss"?",{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"ol:luanzhan\"],\"sourceBind\":\"standard:none\"}":scenario=="late-gain"?",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}":"")}}]}]}
                """));labels[Watch]=new{name="实际子窗",description="原生边界实际选择与失源",optionLabels=new Dictionary<string,string>{{"continue","继续"}}};
            }
            if(scenario=="red")
            {rules["skills"]![0]!["cardPolicies"]=JsonNode.Parse("""[{"id":"effective-red","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]""");}
            if(scenario=="virtual")rules["skills"]![0]!["triggers"]=JsonNode.Parse("""
                [{"id":"real-bank-before-play","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"damage","target":"owner","amount":1},{"op":"damage","target":"owner","amount":1},{"op":"damage","target":"owner","amount":1},{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"add","amount":10}]},
                 {"id":"native-virtual","window":"playPhaseStarting","subject":"owner","usageScope":"game","usageLimit":1,"optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]
                """);
            var fixture=SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in fixture.Programs)b.AddSkill(new(id,fixture.Presentations[id].Name,fixture.Presentations[id].Description){Program=p,ProgramPresentation=fixture.Presentations[id],SelectionWeights=id==Quiet?Enum.GetValues<Role>().ToDictionary(r=>r,_=>10000d):new Dictionary<Role,double>()});
            b.AddGeneral(new(Owner,"实际标记拥有者","supporter",Driver,"qun",12,scenario is "damage" or "loss" or "late-gain"?[Skill,Watch]:[Skill]));
            var peers=Enumerable.Range(1,3).Select(i=>$"fixture:damage-budget-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"实际其他角色","supporter",Quiet,"wei",12,[]));
            b.AddCard(new("fixture:damage-budget-spear","丈八蛇矛","装备牌","实际武器",CardKind.ZhangbaSerpentSpear));
            b.AddCard(new("fixture:damage-budget-borrowed","借刀杀人","锦囊牌","实际原生借刀杀人",CardKind.BorrowedSword));
            var cards=scenario switch{"borrowed"=>new[]{"fixture:damage-budget-borrowed","fixture:damage-budget-spear"},"chain"=>["standard:iron_chain"],"red"=>["standard:snatch"],_=>["standard:slash"]};
            b.AddDeck(new("fixture:damage-budget-deck","固定实体牌堆",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(cards[i%cards.Length],Suit.Spade,7)).ToArray()});
            b.AddMode(new(Mode,"真实标记预算",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:damage-budget-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
