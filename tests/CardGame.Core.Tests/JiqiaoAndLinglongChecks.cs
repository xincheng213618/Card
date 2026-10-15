using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class JiqiaoAndLinglongChecks
{
    private const string Owner="fixture:sp-yueying",Driver="fixture:sp-yueying-driver",Quiet="fixture:sp-yueying-quiet",Mode="identity:classic-sp-yueying-fixture";
    public static void ActualPaidEquipmentDoubleRevealAllNonEquipmentCold()
    {
        var(g,r)=Start();SelectJiqiao(g,2);Reach(g,()=>E<JiqiaoCostPaidEvent>(g).Any());g=Cold(g,r);Play(g);g=Cold(g,r);
        var paid=E<JiqiaoCostPaidEvent>(g).Single();var reveal=E<ProgramCardsRevealedEvent>(g).Single(e=>e.FrameId==paid.ProgramFrameId);
        var non=reveal.Cards.Where(c=>!EquipmentCatalog.IsEquipment(c.Kind)).Select(c=>c.Id).ToArray();var equip=reveal.Cards.Where(c=>EquipmentCatalog.IsEquipment(c.Kind)).Select(c=>c.Id).ToArray();
        Need(reveal.Cards.Count==2*paid.Invoice.PaidCount && E<JiqiaoCompletedEvent>(g).Single() is {} completed && completed.ClaimedCount==non.Length && completed.RevealedCount==reveal.Cards.Count &&
            non.All(id=>V(g).Hand.Any(c=>c.Id==id)) && equip.All(id=>g.CardMovements.Any(m=>m.CardId==id && m.To==CardLocation.DiscardPile && m.Reason.Value=="program.jiqiao.remainder")),"Exactly twice the actual paid equipment is publicly revealed, every non-equipment is obtained once and every remaining equipment is natively discarded.");
        Need(E<JiqiaoStageIssuedEvent>(g).Select(e=>e.Stage).SequenceEqual(Enum.GetValues<JiqiaoPaidStage>()),"Native stages retain their exact once-issued order across cold commands.");
    }
    public static void ActualVirtualBaguaAndQinggangNativeArmorIgnoreCold()
    {
        var(g,r)=Start();Play(g);var target=Enumerable.Range(1,3).First(s=>g.CreateSnapshot(s).Players[s].Role==Role.Rebel);
        Need((bool)Query(g,"HasConditionalVirtualBaguaForAi",Player(g,target)),"The genuinely selected peer has an active live conditional virtual Bagua before its incoming Slash: "+SnapshotJson.Serialize(g.CreateSnapshot(target)));
        Slash(g,target);Play(g);g=Cold(g,r);Need(E<JudgmentResolvedEvent>(g).Any(e=>e.TargetSeat==target && e.Reason==JudgmentReasons.BaguaDefense),"A genuine native Slash against the vacant-armor LingLong target opens actual Bagua judgment. "+JsonSerializer.Serialize(new{Views=g.CreateSnapshot(target),Thoughts=g.AiThoughts,Events=g.Events.Select(e=>e.Payload).TakeLast(20)}));
        EnsureCard(g,CardKind.QinggangSword);Equip(g,CardKind.QinggangSword);End(g);Play(g);var judgments=E<JudgmentResolvedEvent>(g).Count(e=>e.TargetSeat==target && e.Reason==JudgmentReasons.BaguaDefense);
        Slash(g,target);Play(g);g=Cold(g,r);Need(E<JudgmentResolvedEvent>(g).Count(e=>e.TargetSeat==target && e.Reason==JudgmentReasons.BaguaDefense)==judgments,"Actual Qinggang armor-ignore prevents the conditional virtual Bagua, without creating physical equipment or special response shortcuts.");
    }
    private static object Player(GameEngine g,int seat=0)=>((IList)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(g)!)[seat]!;
    private static object Query(GameEngine g,string name,params object?[] args)=>typeof(GameEngine).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name==name && m.GetParameters().Length==args.Length && (args.Length==0 || m.GetParameters()[0].ParameterType==args[0]?.GetType())).Invoke(g,args)!;
    private static void EnsureCard(GameEngine g,CardKind kind){if(!V(g).Hand.Any(c=>c.Kind==kind)){Use(g,"stock",[]);Play(g);}Need(V(g).Hand.Any(c=>c.Kind==kind),"The single verified native fixture draw supplies the required equipment.");}
    private static void Equip(GameEngine g,CardKind kind)
    {EnsureCard(g,kind);var card=V(g).Hand.First(c=>c.Kind==kind);var action=g.GetHumanLegalActions().First(a=>a.CardId==card.Id && a.Kind==LegalActionKind.Equip);Accept(g,new PlayCardCommand(0,card.Id,action.TargetSeats,g.Revision,P(g)!.PromptId,action.PlayedCardKind,action.TargetCardId));Play(g);}
    private static void Slash(GameEngine g,int target)
    {EnsureCard(g,CardKind.Slash);var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.Contains(target));Accept(g,new PlayCardCommand(0,action.CardId!.Value,new[]{target},g.Revision,P(g)!.PromptId,action.PlayedCardKind,action.TargetCardId));}
    private static void SelectJiqiao(GameEngine g,int desired)
    {
        Reach(g,()=>Optional(g));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards")==true);
        var ids=P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards").Select(c=>c.Cards.Single()).Take(desired).ToArray();Need(ids.Length>0,"The fixed physical fixture has genuine equipment for Jiqiao.");
        foreach(var id in ids)Answer(g,c=>c.Cards.SequenceEqual(new[]{id}));FinishEquipmentSelectionIfPublished(g);
    }
    private static void FinishEquipmentSelectionIfPublished(GameEngine g)
    {if(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards");}
    private static bool Optional(GameEngine g)=>P(g)?.SkillPrompt?.SkillId=="ol:jiqiao" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    private static PlayerSnapshot V(GameEngine g)=>g.CreateSnapshot(0).Players[0];
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
    private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual SP Yueying command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets,g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Reach(GameEngine g,Func<bool> done)
    {for(var i=0;i<140;i++){if(done())return;Need(g.State.Winner==Winner.None,"The small native fixture ended prematurely.");var p=P(g);if(p is{PlayerSeat:0}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"take-damage"or"let-die");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("SP Yueying fixture boundary missing: "+JsonSerializer.Serialize(P(g))+". "+RuntimeFacts(g));}
    private static string RuntimeFacts(GameEngine g)=>JsonSerializer.Serialize(new
    {
        Status=g.CreateSnapshot(0).Status,Pending=P(g),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),
        Paid=E<JiqiaoCostPaidEvent>(g).ToArray(),Stages=E<JiqiaoStageIssuedEvent>(g).ToArray(),Completed=E<JiqiaoCompletedEvent>(g).ToArray(),
        Reveals=E<ProgramCardsRevealedEvent>(g).ToArray(),Movements=g.CardMovements.TakeLast(40).ToArray(),
        RecentEvents=g.Events.TakeLast(40).Select(e=>new{Type=e.Payload.GetType().Name,Json=JsonSerializer.Serialize(e.Payload,e.Payload.GetType())}).ToArray(),
        Commands=CommandJson.Serialize(g.AcceptedCommands.TakeLast(12).ToArray()),Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray()
    });
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var c=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(c)==State(g),"Cold accepted commands preserve all native movements, private selection, paid stage invoices, conditional equipment and public reveals.");return c;}
    private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-sp-huang-yueying.{suffix}")!;using var r=new StreamReader(s);return r.ReadToEnd();}
    private static(GameEngine,ContentRegistry)Start(string scenario="normal")
    {var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario.Contains("death",StringComparison.Ordinal)?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);}
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("sp-yueying-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
            {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":16}]},
            {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["equipment"],"resultBind":"removed"},{"op":"moveBoundCards","target":"owner","sourceBind":"removed","destination":"discardPile"}]}
            ]},
            {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var node=JsonNode.Parse(rules)!;var labels=new Dictionary<string,object>{[Driver]=new{name="native driver",description="native"},[Quiet]=new{name="quiet",description="quiet"}};
            var extra=new List<string>();if(scenario is "cost-loss" or "cost-death" or "claim-death")
            {
                var gain=scenario=="claim-death";node["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"fixture:sp-yueying-watch","revision":1,"triggers":[{"id":"native-paid-child","window":"{{(gain?"cardsGained":"cardsMoved")}}","subject":"owner",{{(gain?"\"destinationZones\":[\"hand\"],":"\"sourceZones\":[\"hand\"],")}}"movementOccurrence":"perBatch","movementReasons":["{{(gain?"program.jiqiao.obtain":"program.jiqiao.cost")}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """));labels["fixture:sp-yueying-watch"]=new{name="native paid child",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};extra.Add("fixture:sp-yueying-watch");
                if(scenario=="cost-loss")
                {node["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:sp-yueying-loss","revision":1,"triggers":[{"id":"rescue-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:jiqiao"],"sourceBind":"standard:none"}]}]}"""));labels["fixture:sp-yueying-loss"]=new{name="real rescue source loss",description="native"};extra.Add("fixture:sp-yueying-loss");}
            }
            Add(SkillProgramCatalog.Load(node.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
            b.AddGeneral(new(Owner,"真实SP黄月英","supporter",Driver,"qun",3,["ol:jiqiao","ol:linglong",..extra],GeneralGender.Female));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:sp-yueying-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"实际其他角色","supporter",Quiet,"wei",4,["ol:linglong"]));
            b.AddCard(new("fixture:sp-yueying-silver-lion","白银狮子","装备牌","真实移除回复",CardKind.SilverLion));
            var kinds=scenario=="empty"?new[]{"standard:dodge"}:new[]{"fixture:sp-yueying-silver-lion","standard:qinggang_sword","standard:offensive_horse","standard:defensive_horse","standard:jade_seal","standard:slash","standard:duel","standard:slash"};
            b.AddDeck(new("fixture:sp-yueying-deck","small fixed native deck",4,0,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(scenario=="claim-death" && i==48?"standard:slash":kinds[i%kinds.Length],i%2==0?Suit.Heart:Suit.Spade,7)).ToArray()});b.AddMode(new(Mode,"native SP Yueying",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:sp-yueying-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
