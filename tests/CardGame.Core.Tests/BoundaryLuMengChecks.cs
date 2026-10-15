using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLuMengChecks
{
    public static void AllTablePrintedSuitsAndReplay()
    {
        var (g,r)=Create("mask");
        Use(g,"grow");
        Accept(g,new UseProgramSkillCommand(0,"fixture:lm-driver","other",[],[],g.Revision,Prompt(g)!.PromptId));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lm-driver");
        var choice=Prompt(g)!.Choices.First(c=>c.Targets.SequenceEqual([1]));
        Require(choice.Cards.Count==0,"The other hand cost is represented by an opaque slot.");
        Replay(g,r); Reject(g);
        var source=PhysicalHand(g,1)[int.Parse(choice.Parameters["slot-index"])];
        Answer(g,c=>c.Id==choice.Id); ReachPlay(g);
        Require(g.CardMovements.Any(m=>m.CardId==source.Id && m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile),"A true other-table entity reaches Discard.");
        foreach(var suit in Suits.Where(s=>s!=source.Suit)) Discard(g,suit);
        var duplicate=g.CreateSnapshot(0).Players[0].Hand.First();
        DiscardCard(g,duplicate.Id);
        End(g); Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:botu");
        var f=(DeferredTurnEndFrame)g.ResolutionStack.First();
        Require(f.AfterTurnEnded!.Items.Single(i=>i.Candidate?.SkillId=="boundary:botu").Facts!.TurnDiscardSuitMask==15,
            "One owning actual turn freezes all four printed suits, including an opaque other-player discard and duplicates.");
        Require(f.AfterTurnEnded.Items is IList<TurnEndingBoundaryItem> items&&items.IsReadOnly,"Frozen window collections remain read-only.");
        Require(g.Events.Select(e=>e.Payload).OfType<TurnDiscardSuitMaskChangedEvent>().All(e=>e.TurnNumber==1),"The mask is keyed by the actual turn.");
        Replay(g,r); Reject(g); Activate(g); ReachPlay(g);
        Require(g.CreateSnapshot(0).TurnNumber==2 && Round(g)==1,"A self extra turn does not refresh Round.");
        Require(!g.Events.Select(e=>e.Payload).OfType<TurnDiscardSuitMaskChangedEvent>().Any(e=>e.TurnNumber==2),"The new actual turn starts with no inherited suit mask.");
        Replay(g,r);
    }
    public static void CrossOwnerSkipChainAndResumeDeath()
    {
        foreach(var mode in new[]{"cross","skip","chain","dead-resume"})
        {
            var(g,r)=Create(mode); if(mode=="skip")Use(g,"flip",[3]); Use(g,"extra",[3]); End(g);
            if(mode is "chain" or "dead-resume")
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lm-observer");
                var turn=g.CreateSnapshot(0).TurnNumber;
                Require(turn==2 && g.CreateSnapshot(0).CurrentSeat==3,"A different owner completes the inserted actual extra turn first.");
                Replay(g,r);Activate(g);
                Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lm-observer"&&p.Choices.Any(c=>c.Targets.Count>0));
                Answer(g,c=>c.Targets.SequenceEqual([mode=="chain"?2:1]));
                if(mode=="chain")
                {
                    Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lm-observer");
                    Require(g.CreateSnapshot(0).CurrentSeat==2&&g.CreateSnapshot(0).TurnNumber==3,"A chained extra turn keeps the original normal anchor.");Skip(g);
                }
            }
            var expected=mode=="dead-resume"?2:1;
            AdvanceTo(g,x=>x.CreateSnapshot(0).CurrentSeat==expected&&x.CreateSnapshot(0).Phase==TurnPhase.NotStarted);
            Require(Round(g)==1 && g.Events.Select(e=>e.Payload).OfType<TurnEndedEvent>().Count()==(mode=="chain"?3:2),"Normal, skipped and chained actual ends return to the saved normal position without Round refresh.");
            Replay(g,r);
        }
    }
    public static void QinxueWindowsAndSharedUsage()
    {
        var(g,r)=Create("awake-start",false);
        Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:qinxue");
        Require(g.CreateSnapshot(0).Players[0].MaxHp==3,"Preparation awakening pays maximum HP before its choice.");
        Replay(g,r);Reject(g);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="draw");ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].HandCount==10&&g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId=="classic:gongxin"),"Draw branch grants the actual Gongxin after two draws.");
        Use(g,"grow");End(g);Reach(g,p=>p.Kind==DecisionKind.SkipDiscardPolicy);Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="discard-policy-use");
        AdvanceTo(g,x=>x.CreateSnapshot(0).CurrentSeat==1);
        Require(Started(g,"boundary:qinxue").Count()==1,"Preparation and Ending bindings share one named game awakening.");Replay(g,r);
        var(e,er)=Create("awake-ending");Use(e,"hurt");Use(e,"grow");End(e);
        Reach(e,p=>p.Kind==DecisionKind.SkipDiscardPolicy);Answer(e,c=>c.Parameters.GetValueOrDefault("action")=="discard-policy-use");
        Reach(e,p=>p.SkillPrompt?.SkillId=="boundary:qinxue");
        Require(e.CreateSnapshot(0).Players[0] is {Hp:2,MaxHp:3},"Ending qualification uses the actual hand-minus-HP state.");Replay(e,er);
        Answer(e,c=>c.Parameters.GetValueOrDefault("option-id")=="recover");AdvanceTo(e,x=>x.CreateSnapshot(0).CurrentSeat==1);
        Require(e.CreateSnapshot(0).Players[0] is {Hp:3,MaxHp:3}&&Started(e,"boundary:qinxue").Single().Window==SkillProgramTriggerWindow.TurnEnding,"Recovery and the Ending awakening commit exactly once.");Replay(e,er);
    }
    public static void OptInUsageCatalogAndLegacyShape()
    {
        var r=Registry("botu"); var p=r.GetSkill("boundary:botu").Program!;
        Require(p.Triggers.Single().DynamicUsageLimit==new SkillProgramDynamicUsageLimit(SkillProgramDynamicUsageLimitKind.AlivePlayersCapped,3),"The live cap is an explicit typed policy.");
        var old=r.GetSkill("classic:dangxian").Program!;
        var legacy=JsonSerializer.Serialize(old);
        Require(!legacy.Contains("DynamicUsageLimit")&&!legacy.Contains("NamedUsageGroup"),"Old descriptors omit both nullable additions.");
        Require(!JsonSerializer.Serialize(new SkillProgramTriggerFacts(0,4,true)).Contains("TurnDiscardSuitMask"),"Old trigger facts omit the new nullable scalar.");
        var baseTrigger="\"id\":\"end\",\"window\":\"afterTurnEnded\",\"subject\":\"owner\",\"optional\":true,\"usageScope\":\"round\",\"dynamicUsageLimit\":{\"kind\":\"alivePlayersCapped\",\"cap\":3},\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]";
        SkillProgram Load(string trigger)=>SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:limit","revision":1,"triggers":[{ {{trigger}} }]}]}""",Presentation("""{"schemaVersion":0,"skills":{"fixture:limit":{"name":"限额","description":"通用机制"}}}""")).Programs["fixture:limit"];
        var independent=ContentRegistry.Build(new IsolatedCatalog(r,false));
        var standalone=CreateFromRegistry(independent);
        Require(Round(standalone)==1,"Dynamic Round opt-in works without the Classic package manifest.");Replay(standalone,independent);
        var without=ContentRegistry.Build(new IsolatedCatalog(r,true));var negative=CreateFromRegistry(without);
        Use(negative,"grow");Discard(negative,Suit.Spade);
        Require(!negative.Events.Any(e=>e.Payload is RoundStartedEvent or TurnDiscardSuitMaskChangedEvent),"A registry without Classic or the new cap/history emits neither new Round nor suit-history events.");
        var before=State(negative);
        var facts=(SkillProgramTriggerFacts)typeof(GameEngine).GetMethod("CaptureProgramTriggerFacts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,[typeof(CharacterState)],null)!.Invoke(negative,[Players(negative)[0]])!;
        Require(facts.TurnDiscardSuitMask is null&&before==State(negative),"The old registry leaves nullable history facts omitted and pure reads cannot mutate state.");Replay(negative,without);
        var first=Load(baseTrigger);
        Require(first.GameplayHash!=Load(baseTrigger.Replace("\"cap\":3","\"cap\":2")).GameplayHash && first.GameplayHash!=Load(baseTrigger+",\"namedUsageGroup\":\"shared\"").GameplayHash,"Both new configuration fields participate in canonical gameplay fingerprints.");
        foreach(var invalid in new[]{baseTrigger.Replace("\"round\"","\"turn\""),baseTrigger.Replace("\"cap\":3","\"cap\":0"),baseTrigger+",\"usageLimit\":1",baseTrigger+",\"namedUsageGroup\":\"not a key\""})
        {
            var rejected=false;try{Load(invalid);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Invalid dynamic scope/cap/dual-limit/group identifiers reject at catalog load: "+invalid);
        }
    }
    private static List<CharacterState> Players(GameEngine g)=>(List<CharacterState>)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
    private static string Presentation(string json)
    {
        var root=JsonNode.Parse(json)!;root["schemaVersion"]=SkillProgramCatalog.PresentationSchemaVersion;return root.ToJsonString();
    }
    private static readonly Suit[] Suits=[Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond];
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Require(bool c,string m){if(!c)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var q=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(q.Accepted,q.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> f){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(f).Id,g.Revision));}
    private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> f)
    {
        for(var i=0;i<80;i++)
        {
            var p=Prompt(g);if(p is not null&&f(p))return;
            if(p is {Kind:DecisionKind.ProgramTrigger,PlayerSeat:0}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Skip(g);
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed fixture prompt not reached: "+JsonSerializer.Serialize(Prompt(g)));
    }
    private static void AdvanceTo(GameEngine g,Func<GameEngine,bool> f)
    {
        for(var i=0;i<80;i++){if(f(g))return;var p=Prompt(g);if(p is {Kind:DecisionKind.ProgramTrigger,PlayerSeat:0})Skip(g);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed fixture state not reached: "+SnapshotJson.Serialize(g.CreateSnapshot(0)));
    }
    private static void Use(GameEngine g,string id,IReadOnlyList<int>? targets=null)
    {Accept(g,new UseProgramSkillCommand(0,"fixture:lm-driver",id,[],targets??[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);}
    private static void DiscardCard(GameEngine g,int id){Accept(g,new UseProgramSkillCommand(0,"fixture:lm-driver","discard",[id],[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);}
    // Printed entities are trusted diagnostics here; player prompts still use opaque slots.
    private static IReadOnlyList<Card> PhysicalHand(GameEngine g,int seat)
    {
        var zones=typeof(GameEngine).GetField("_cardZones",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
        return (IReadOnlyList<Card>)zones.GetType().GetMethod("CardsAt")!.Invoke(zones,[CardLocation.Hand(seat)])!;
    }
    private static void Discard(GameEngine g,Suit s)
    {
        var h=PhysicalHand(g,0); Require(h.Any(c=>c.Suit==s),$"Printed {s} missing at actual turn {g.CreateSnapshot(0).TurnNumber}: {string.Join(',',h.Select(c=>$"{c.Id}:{c.Suit}"))}");
        DiscardCard(g,h.First(c=>c.Suit==s).Id);
    }
    private static int Round(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<RoundStartedEvent>().Last().RoundNumber;
    private static IEnumerable<ProgramBindingStartedEvent> Started(GameEngine g,string id)=>g.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Where(e=>e.SkillId==id);
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"All views, owning frames, events, physical movement and command records restore identically.");
    private static void Reject(GameEngine g){var before=State(g);var p=Prompt(g)!;Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("fixture:bad"),g.Revision)).Accepted&&before==State(g),"Illegal answers are atomic.");}
    private static ContentRegistry Registry(string mode)=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
    private static (GameEngine,ContentRegistry) Create(string mode,bool reach=true)
    {
        var r=Registry(mode);return(CreateFromRegistry(r,reach),r);
    }
    private static GameEngine CreateFromRegistry(ContentRegistry r,bool reach=true)
    {
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-lm-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:lm-owner",g.Revision,Prompt(g)!.PromptId));if(reach)ReachPlay(g);return g;
    }
    private sealed class IsolatedCatalog(ContentRegistry source,bool removeOptIn):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-independent-catalog",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            foreach(var c in source.Cards.Values)b.AddCard(c);
            foreach(var s in source.Skills.Values)
            {
                var optsIntoRound = s.Program is { } program &&
                    (program.Triggers.Any(t=>t.Window == SkillProgramTriggerWindow.RoundStarting || t.Window == SkillProgramTriggerWindow.TurnBeforeStart || t.UsageScope == SkillUsageScope.Round ||
                         t.DynamicUsageLimit is not null || UsesDiscardSuitHistory(t.Condition)) ||
                     program.ViewAs.Any(rule=>rule.TieredRoundConversion is not null || rule.RoundOnceCardlessBasic is not null || rule.RoundDistinctBasicUse is not null) ||
                     program.Activations.SelectMany(a=>a.Effects).Any(e=>e.Op==SkillProgramEffectOp.ScheduleFirstRoundGameUsageRefund) ||
                     program.Triggers.SelectMany(t=>t.Effects).Any(e=>e.Op is
                         SkillProgramEffectOp.HoldRoundFocusCards or
                         SkillProgramEffectOp.BeginPhaseNamePrediction or
                         SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach or
                         SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol or
                         SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal or
                         SkillProgramEffectOp.ZecaiRoundSettlement or
                         SkillProgramEffectOp.FengjiRoundChoice or
                         SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget or
                         SkillProgramEffectOp.KanggeHealVictim));
                b.AddSkill(removeOptIn&&optsIntoRound?s with{Program=null}:s);
            }
            foreach(var g in source.Generals.Values)b.AddGeneral(g);
            foreach(var d in source.Decks.Values)b.AddDeck(d);
            foreach(var m in source.Modes.Values)b.AddMode(m);
        }
        private static bool UsesDiscardSuitHistory(SkillProgramTriggerCondition condition) =>
            condition.Kind==SkillProgramTriggerConditionKind.TurnDiscardIncludesAllSuits ||
            condition.Children.Any(UsesDiscardSuitHistory);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-lm",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var observer= mode is "chain" or "dead-resume" ? $$"""
                ,{"id":"fixture:lm-observer","revision":1,"triggers":[{"id":"ended","window":"afterTurnEnded","turnOwnerScope":"otherLiving","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{{(mode=="chain"?"{\"op\":\"pendExtraTurn\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"selectedTarget\"}}":"{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}")}}]}]}
                """ : "";
            var movement=mode=="batch" ? """
                ,{"id":"fixture:lm-movement","revision":1,"triggers":[{"id":"pause","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"step","options":[{"id":"continue"}]}]}]}
                """ : "";
            var presentation=JsonNode.Parse(Presentation("""{"schemaVersion":0,"skills":{"fixture:lm-driver":{"name":"驱动","description":"真实实体和回合"},"fixture:lm-observer":{"name":"结束后观察","description":"真实结束后选人"}}}"""))!;
            if(movement.Length>0)presentation["skills"]!["fixture:lm-movement"]=JsonNode.Parse("""{"name":"移动暂停","description":"四花色实体支付子链","optionLabels":{"continue":"继续"}}""");
            if(observer.Length==0)presentation["skills"]!.AsObject().Remove("fixture:lm-observer");
            var cat=SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:lm-driver","revision":1,"activations":[
                {"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":{{{(mode is "mask" or "botu" or "batch" ? 18 : 8)}}}}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
                {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"batch","minCards":4,"maxCards":4,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":4}]},
                {"id":"other","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand"]}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner","targetRef":{"kind":"selectedTarget"}}]},
                {"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]}]}{{{observer}}}{{{movement}}}]}
                """,presentation.ToJsonString());
            foreach(var pair in cat.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            var skills=new List<string>();
            if(mode is "mask" or "botu" or "batch")skills.Add("boundary:botu");
            if(mode=="mask")skills.Add("classic:hongyan");
            if(mode.StartsWith("awake")){skills.Add("boundary:qinxue");skills.Add("boundary:keji");}
            if(mode.StartsWith("keji")){skills.Add("boundary:keji");skills.Add("classic:dangxian");}
            if(movement.Length>0)skills.Add("fixture:lm-movement");
            if(observer.Length>0)skills.Add("fixture:lm-observer");
            b.AddGeneral(new("fixture:lm-owner","固定主人","supporter","fixture:lm-driver","wu",mode.StartsWith("awake")?3:20,skills));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:lm-target-{i}","固定目标","supporter","standard:none","wei",20,[]));
            b.AddDeck(new("fixture:lm-deck","实体",mode=="awake-start"?6:4,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(mode.StartsWith("keji")?"standard:slash":"standard:dodge",Suits[i/40],i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-lm-fixture","固定",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:lm-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:lm-owner","fixture:lm-target-1","fixture:lm-target-2","fixture:lm-target-3"]));
        }
    }
}
