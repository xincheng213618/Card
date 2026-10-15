using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryXiahouDunChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string GiftReason = "skill-program.boundary:qingjian.GiveShownBoundCardsAndGrantTurnHandLimit";

    public static void GanglieEachPointRealJudgmentBranches()
    {
        foreach (var black in new[] { false, true })
        {
            var (g,r)=Start(black ? "black" : "red"); var hp=g.State.Players[1].Hp;
            Use(g,"damage",1); var judgments=0; var discards=0;
            for(var i=0;i<65;i++)
            {
                if(P(g) is { Kind:DecisionKind.PlayCard, PlayerSeat:0 }) break;
                if(P(g) is { } p && p.SkillPrompt?.SkillId=="boundary:ganglie")
                {
                    Replay(g,r);
                    if(p.Choices.Any(c=>Action(c)=="activate")) { Answer(g,c=>Action(c)=="activate"); judgments++; }
                    else if(p.Choices.Any(c=>Action(c)=="select-source-card")) { Answer(g,c=>Action(c)=="select-source-card"); discards++; }
                    else Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="discard");
                }
                else Accept(g,new AdvanceOneStepCommand(g.Revision));
            }
            Require(judgments==2 && discards==(black?2:0),"Two actual damage points each offer their real judgment and only black selects source HE.");
            Require(g.Events.Select(e=>e.Payload).OfType<JudgmentResolvedEvent>().Count(e=>e.Reason=="skill.boundary-ganglie")==2,"Two complete real judgments, rather than one per damage event.");
            Require(g.State.Players[1].Hp==hp-(black?0:2),"Only final red results damage the actual source once per point.");
            Require(g.CardMovements.Count(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile)==(black?2:0),"Black chooses and discards physical source cards once, without the old two-card payment.");
            Settle(g); Replay(g,r);
        }
        var(self,selfRegistry)=Start("black");Use(self,"self-damage");
        Require(P(self)?.SkillPrompt?.SkillId=="boundary:ganglie","A real self source remains a source and offers Ganglie.");Replay(self,selfRegistry);Activate(self);
        Answer(self,c=>Action(c)=="select-source-card");Answer(self,c=>c.Parameters.GetValueOrDefault("option-id")=="discard");Settle(self);Replay(self,selfRegistry);
        var(empty,emptyRegistry)=Start("black");Use(empty,"empty-source",1);Reach(empty,p=>p.SkillPrompt?.SkillId=="boundary:ganglie");
        Require(empty.State.Players[1].Hand.Count==0,"The real prefix discards all four source Hand cards before the one damage point.");Replay(empty,emptyRegistry);Activate(empty);Settle(empty);
        Require(empty.Events.Select(e=>e.Payload).OfType<JudgmentResolvedEvent>().Count(e=>e.Reason=="skill.boundary-ganglie")==1&&!empty.ResolutionStack.OfType<ProgramSkillFrame>().Any(),"A black final judgment with no legal source card finishes and cleans its real judgment without a discard confirmation.");Replay(empty,emptyRegistry);
        var(deadAudit,_)=Start("black");Use(deadAudit,"damage",1);
        var pending=(ValueTuple<ProgramTriggerCandidate,ProgramSkillWindowContext>)typeof(GameEngine).GetMethod("GetPendingProgramTriggerCandidate",Flags)!.Invoke(deadAudit,[])!;
        var trigger=pending.Item1.SkillId=="boundary:ganglie"?deadAudit.ContentRegistry!.GetSkill("boundary:ganglie").Program!.Triggers.Single():throw new InvalidOperationException("Missing host audit trigger.");
        // A host qualification audit, separate from real command replay: source liveness is not source presence.
        Players(deadAudit)[1].IsAlive=false;
        var eligible=(bool)typeof(GameEngine).GetMethod("CanRunAfterDamageProgramTrigger",Flags)!.Invoke(deadAudit,[Players(deadAudit)[0],trigger,pending.Item2])!;
        Require(pending.Item2.SourceSeat==1&&eligible,"A frozen source seat remains present after that source dies; source-presence qualification must not add liveness or other-seat policy.");        var(lightning,lr)=Start("lightning");var delayed=lightning.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Lightning&&a.TargetSeat==0);
        Accept(lightning,new PlayCardCommand(0,delayed.CardId!.Value,[0],lightning.Revision,P(lightning)!.PromptId));Settle(lightning);Use(lightning,"order");
        var viewed=lightning.ResolutionStack.OfType<ProgramSkillFrame>().Single().TopReorder!.ViewedCardIds;
        var zone=lightning.CreateCardZoneDiagnostics().ToDictionary(c=>c.CardId,c=>c.CardKind);
        var blackId= viewed.First(id=>zone[id]==CardKind.Lightning);var redId=viewed.First(id=>zone[id]==CardKind.Peach);
        var rest=viewed.Where(id=>id!=blackId&&id!=redId).ToArray();var ordered=rest.Take(6).Concat(new[]{blackId,redId}).Concat(rest.Skip(6)).ToArray();
        foreach(var id in ordered)Answer(lightning,c=>c.Cards.SequenceEqual(new[]{id})&&c.Parameters.GetValueOrDefault("action")=="top");Settle(lightning);Replay(lightning,lr);
        var beforeLightningHp=lightning.State.Players[0].Hp;var beforeLightningRng=Rng(lightning);
        Accept(lightning,new EndPlayPhaseCommand(0,lightning.Revision,P(lightning)!.PromptId));
        for(var i=0;i<100;i++)
        {
            if(P(lightning) is {Kind:DecisionKind.PlayCard,PlayerSeat:0})break;
            if(P(lightning)?.SkillPrompt?.SkillId=="boundary:ganglie")
            {
                var window=lightning.ResolutionStack.OfType<DamageTriggerWindowFrame>().Last();
                var attack=typeof(GameEngine).GetMethod("GetDamageTriggerAttack",Flags)!.Invoke(lightning,[window])!;
                var isDelayedAttack=(bool)attack.GetType().GetProperty("IsDelayedJudgmentDamage")!.GetValue(attack)!;
                Require(!isDelayedAttack,"Actual source-less delayed Lightning must not offer Ganglie, even though legacy attack stores victim as SourceSeat.");
                Answer(lightning,c=>Action(c)=="skip");
            }
            else Accept(lightning,new AdvanceOneStepCommand(lightning.Revision));
        }
        Settle(lightning);
        Require(lightning.Events.Select(e=>e.Payload).OfType<JudgmentResolvedEvent>().Any(e=>e.Reason=="trick.lightning"&&e.Succeeded)&&lightning.State.Players[0].Hp==beforeLightningHp-3,"Real played Lightning hits through normal judgment and deals three actual damage points.");
        Require(!lightning.Events.Select(e=>e.Payload).OfType<JudgmentResolvedEvent>().Any(e=>e.Reason=="skill.boundary-ganglie"),"Source-less Lightning creates no Ganglie judgment.");
        Require(Rng(lightning)==beforeLightningRng&&lightning.CardMovements.Where(m=>m.TurnNumber==lightning.CreateSnapshot(0).TurnNumber&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(0)).First().CardId==redId,"Source-less Lightning spends no extra RNG or Ganglie judgment card before the next real Draw.");Replay(lightning,lr);
    }

    public static void QingjianActualReceiptInputPrivacyAndReplay()
    {
        var(g,r)=Start("gift"); var limit=Limit(g,0); Use(g,"draw");
        Require(P(g)?.SkillPrompt?.SkillId=="boundary:qingjian","Own normal Draw did not consume quota; actual Play gain offers Qingjian.");
        var q=g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        Require(q.Batch.MovementTiming is {ActualTurnOwnerSeat:0,Phase:TurnPhase.Play,PhaseActorSeat:0},"Gain qualification freezes actual atomic-start timing.");
        Replay(g,r); Activate(g); Reject(g,new AnswerPromptCommand(0,P(g)!.PromptId,new ChoiceId("forged-empty-selection"),g.Revision));
        var cards=g.State.Players[0].Hand.GroupBy(c=>Category(c.Kind)).Select(x=>x.First().Id).ToArray();
        Require(cards.Length==3,"Small fixed deck covers all three physical categories.");
        for(var viewer=1;viewer<4;viewer++) Require(g.CreateSnapshot(viewer).PendingDecision is null,"Private selection is not exposed to other viewers.");
        SelectOwned(g,cards,r);
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramCardsRevealedEvent>().Any(e=>e.SkillId=="boundary:qingjian"&&e.Cards.Select(c=>c.Id).Order().SequenceEqual(cards.Order())),"Exact owned selection is publicly revealed before recipient choice.");
        Replay(g,r); Reject(g,new AnswerPromptCommand(0,P(g)!.PromptId,new ChoiceId("self-recipient"),g.Revision));
        Answer(g,c=>c.Targets.SequenceEqual(new[]{1})); Settle(g);
        var receipt=g.Events.Select(e=>e.Payload).OfType<ShownBoundGiftCommittedEvent>().Single();
        Require(receipt is {ActualCount:3,CategoryCount:3,ActualTurnOwnerSeat:0} && Limit(g,0)==limit+3,"Three actual Hand receipts grant actual turn owner +3.");
        Require(cards.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.Hand(1)&&m.Reason.Value==GiftReason)==1),"Each selected entity transfers exactly once.");
        var events=g.Events.Select(e=>e.Payload).ToArray();var bonus=Array.FindIndex(events,e=>e is TurnRuleModifierGrantedEvent x&&x.Modifier.Source.SkillId=="boundary:qingjian");
        Require(bonus>=0&&events.Take(bonus).OfType<CardMovedEvent>().Count(e=>cards.Contains(e.CardId)&&e.To==CardLocation.Hand(1))==3,"All physical movement records precede the issued reward.");
        Replay(g,r);Use(g,"draw");Settle(g);Require(g.Events.Count(e=>e.Payload is ShownBoundGiftCommittedEvent)==1,"Accepted named actual-turn quota bounds nested and repeated gains.");Replay(g,r);
    }

    public static void QingjianNarrowParserAndLegacyNullBoundary()
    {
        var rules="""{"id":"fixture:narrow","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","gainPhaseQualification":"outsideOwnerDraw","optional":true,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand","equipment"],"minimumCards":1,"maximumCards":null,"resultBind":"gift"},{"op":"revealBoundCards","target":"owner","sourceBind":"gift"},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"giveShownBoundCardsAndGrantTurnHandLimit","target":"owner","sourceBind":"gift"}]}]}""";
        Load(rules);
        foreach(var bad in new[]{rules.Replace("\"outsideOwnerDraw\"","null"),rules.Replace("outsideOwnerDraw","outsideAnyDraw"),rules.Replace("\"window\":\"cardsGained\"","\"window\":\"cardsMoved\""),rules.Replace("{\"op\":\"revealBoundCards\",\"target\":\"owner\",\"sourceBind\":\"gift\"},",""),rules.Replace("\"minimumCards\":1","\"minimumCards\":0"),rules.Replace("\"targetKind\":\"otherLiving\"","\"targetKind\":\"anyLiving\""),rules.Replace("\"sourceBind\":\"gift\"}]}]}","\"sourceBind\":\"gift\",\"amount\":3}]}]}")})
        { var rejected=false;try{Load(bad);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Invalid null/enum/window/unrevealed/minimum/recipient/bonus nodes reject before runtime."); }
        var damageRules="""{"id":"fixture:narrow","revision":1,"triggers":[{"id":"damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamagePoint","requireDamageSource":true,"optional":true,"effects":[{"op":"recover","target":"owner","amount":1}]}]}""";
        Require(Load(damageRules).Programs["fixture:narrow"].Triggers.Single().RequireDamageSource==true,"Only explicit true opts into real source presence.");
        foreach(var bad in new[]{damageRules.Replace("\"requireDamageSource\":true","\"requireDamageSource\":false"),damageRules.Replace("\"requireDamageSource\":true","\"requireDamageSource\":null"),damageRules.Replace("\"requireDamageSource\":true","\"requireDamageSource\":1"),damageRules.Replace("afterDamageApplied","damageAppliedBeforeDying"),damageRules.Replace("\"subject\":\"owner\"","\"subject\":\"any\"")})
        {var rejected=false;try{Load(bad);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Source presence parser rejects false/null/number/wrong window/wrong subject.");}
        var oldDamage=Load(damageRules.Replace("\"requireDamageSource\":true,","")).Programs["fixture:narrow"].Triggers.Single();
        Require(oldDamage.RequireDamageSource is null&&!JsonSerializer.Serialize(oldDamage).Contains("RequireDamageSource",StringComparison.Ordinal),"Missing source presence preserves null and omits new serialization.");        var legacy=Load(rules.Replace("\"gainPhaseQualification\":\"outsideOwnerDraw\",", ""));Require(legacy.Programs["fixture:narrow"].Triggers.Single().GainPhaseQualification is null,"Old trigger leaves new qualification null.");
        var(g,r)=Start("legacy");Require(g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch.MovementTiming is null,"Registry without qualification keeps atomic timing absent.");Replay(g,r);for(var i=0;i<20&&P(g)?.SkillPrompt?.SkillId=="fixture:legacy";i++){Answer(g,c=>Action(c)=="skip");if(P(g)?.SkillPrompt?.SkillId!="fixture:legacy")Reach(g,p=>p.Kind==DecisionKind.PlayCard||p.SkillPrompt?.SkillId=="fixture:legacy");}Settle(g);
    }

    private static int Category(CardKind kind)=>CardCatalog.Get(kind).CategoryName switch{"基本牌"=>0,"装备牌"=>2,_=>1};
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private static void Activate(GameEngine g)=>Answer(g,c=>Action(c)=="activate");
    private static void SelectOwned(GameEngine g,int[] cards,ContentRegistry? r=null){foreach(var id in cards){Answer(g,c=>c.Cards.SequenceEqual(new[]{id}));if(r is not null)Replay(g,r);}if(P(g)?.Choices.Any(c=>Action(c)=="finish-owned-cards")==true)Answer(g,c=>Action(c)=="finish-owned-cards");}
    private static void Use(GameEngine g,string action,int? target=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:driver",action,[],target is null?[]:[target.Value],g.Revision,P(g)!.PromptId));
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> stop){for(var i=0;i<100;i++){if(P(g) is {} p&&stop(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Boundary not reached: "+P(g)?.Kind+" "+P(g)?.SkillPrompt?.SkillId+" "+string.Join(",",P(g)?.Choices.Select(Action)??[]));}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static IReadOnlyList<CharacterState> Players(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",Flags)!.GetValue(g)!;
    private static uint Rng(GameEngine g){var random=typeof(GameEngine).GetField("_random",Flags)!.GetValue(g)!;return (uint)random.GetType().GetProperty("State")!.GetValue(random)!;}
    private static int Limit(GameEngine g,int seat)=>(int)typeof(GameEngine).GetMethod("GetHandLimit",Flags)!.Invoke(g,[Players(g)[seat]])!;
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))==SnapshotJson.Serialize(restored.CreateSnapshot(s)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack),"Four viewer snapshots, movement and typed frames restore from accepted commands.");}
    private static void Reject(GameEngine g,GameCommand c){var revision=g.Revision;var snapshot=SnapshotJson.Serialize(g.CreateSnapshot(0));var result=g.Submit(c);Require(!result.Accepted&&revision==g.Revision&&snapshot==SnapshotJson.Serialize(g.CreateSnapshot(0)),"Rejected input is atomic.");}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static SkillProgramCatalog Load(string skill)=>SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{skill}}]}""","""{"schemaVersion":3,"skills":{"fixture:narrow":{"name":"narrow","description":"narrow"}}}""");
    private static (GameEngine,ContentRegistry) Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-xiahou-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=5},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:xiahou-owner",g.Revision,P(g)!.PromptId));
        for(var i=0;i<60;i++)
        {
            if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}||scenario=="legacy"&&P(g)?.SkillPrompt?.SkillId=="fixture:legacy")return(g,r);
            if(P(g)?.SkillPrompt?.SkillId=="fixture:draw-observer"){Activate(g);Answer(g,c=>c.Targets.SequenceEqual(new[]{0}));}else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Small setup did not reach play.");
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:xiahou",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            if(scenario=="silver")b.AddCard(new("classic:silver-lion","白银狮子","装备牌","Fixture armor",CardKind.SilverLion));
            if(scenario!="legacy")typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryXiahouDunContent")!.GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[scenario=="weapon-source"?new WeaponSourceBuilder(b):b]);
            var definitions=new List<string>{"""{"id":"fixture:driver","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},{"id":"empty-source","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":4},{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},{"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},{"id":"weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"equipSampledGenerals","target":"owner","amount":1}]},{"id":"order","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"reorderTopCards","target":"owner","amount":12}]}]}"""};
            if(scenario=="silver")definitions.Add("""{"id":"fixture:recover-child","revision":1,"triggers":[{"id":"recover","window":"afterHpRecovered","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}""");
            if(scenario is "phases" or "scheduled")definitions.Add("""{"id":"fixture:draw-observer","revision":1,"triggers":[{"id":"draw","window":"drawPhaseStarting","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"draw","target":"selectedTarget","amount":1}]}]}""");
            if(scenario=="scheduled")definitions.Add("""{"id":"fixture:extra-draw","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"replaceJudgmentPhase","target":"owner"}]}]}""");
            if(scenario=="extra-play")definitions.Add("""{"id":"fixture:extra-play","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}""");
            if(scenario=="death-child")definitions.Add($$"""{"id":"fixture:death-child","revision":1,"triggers":[{"id":"death","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{GiftReason}}"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":10}]}]}""");
            if(scenario=="legacy")definitions.Add("""{"id":"fixture:legacy","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":true,"effects":[{"op":"recover","target":"owner","amount":1}]}]}""");
            if(scenario is "child" or "weapon-source")definitions.Add($$"""{"id":"fixture:gift-child","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{GiftReason}}"],"optional":true,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"amount":1,"resultBind":"received"},{"op":"moveBoundCards","target":"owner","sourceBind":"received","destination":"discardPile"}]}]}""");
            var presentation=JsonSerializer.Serialize(new{schemaVersion=3,skills=definitions.Select(d=>JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!).ToDictionary(id=>id,id=>new{name=id,description=id})});
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',definitions)}}]}""",presentation);
            foreach(var pair in c.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            b.AddSkill(new("fixture:pick","pick","pick"){SelectionWeights=new Dictionary<Role,double>{[Role.Lord]=-1000}});
            b.AddGeneral(new("fixture:xiahou-owner","owner","supporter",scenario is "red" or "black" or "lightning"?"boundary:ganglie":scenario=="legacy"?"fixture:legacy":scenario=="weapon-source"?"fixture:pick":"boundary:qingjian","wei",4,["fixture:driver",..(scenario=="weapon-source"?Array.Empty<string>():new[]{"fixture:pick"}),..(scenario is "phases" or "scheduled"?new[]{"fixture:draw-observer"}:Array.Empty<string>()),..(scenario=="scheduled"?new[]{"fixture:extra-draw"}:Array.Empty<string>()),..(scenario=="extra-play"?new[]{"fixture:extra-play"}:Array.Empty<string>()),..(scenario=="death-child"?new[]{"fixture:death-child"}:Array.Empty<string>()),..(scenario=="silver"?new[]{"fixture:recover-child"}:Array.Empty<string>())]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:xiahou-{i}","other","supporter",(scenario is "child" or "weapon-source")&&i==1?"fixture:gift-child":scenario=="other-owner"&&i==1?"boundary:qingjian":scenario is "phases" or "scheduled"?"fixture:draw-observer":"fixture:pick","wei",6,[]));
            if(scenario is "weapon" or "weapon-source")b.AddGeneral(new("fixture:xiahou-unused","unused","supporter",scenario=="weapon-source"?"boundary:qingjian":"fixture:pick","wei",4,[]));
            var kinds=scenario=="silver"?new[]{"classic:silver-lion"}:scenario=="lightning"?new[]{"standard:lightning","standard:peach"}:new[]{"standard:slash","standard:duel","standard:crossbow","standard:indulgence"};
            b.AddDeck(new("fixture:xiahou-deck","fixed",4,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard(kinds[i%kinds.Length],scenario=="black"||scenario=="lightning"&&i%2==0?Suit.Spade:Suit.Heart,scenario=="lightning"?5:i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-xiahou-fixture","xiahou",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xiahou-deck",GeneralCandidateCount:scenario is "weapon" or "weapon-source"?5:4,GeneralPoolIds:["fixture:xiahou-owner","fixture:xiahou-1","fixture:xiahou-2","fixture:xiahou-3",..(scenario is "weapon" or "weapon-source"?new[]{"fixture:xiahou-unused"}:Array.Empty<string>())]));
        }
    }
    // Test-only donor metadata makes this production program a real dynamic weapon grant.
    // No production definition or official rule is changed.
    private sealed class WeaponSourceBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
    {
        public void AddCard(ContentCardDefinition d)=>b.AddCard(d);
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d);
        public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);
        public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d.Id=="boundary:qingjian"?d with{Description="【杀】 fixture donor",SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-1000000d)}:d);
    }
}
