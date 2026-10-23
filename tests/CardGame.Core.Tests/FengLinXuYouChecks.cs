using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinXuYouChecks
{
    public static void SingleEntityAutomaticTopAndConversionEligibility()
    {
        foreach (var kind in new[] { CardKind.Crossbow, CardKind.Slash, CardKind.Nullification })
        {
            var (g, r) = Create(nested: true, response: kind == CardKind.Nullification); ReachPlay(g);
            if (kind == CardKind.Crossbow)
            {
                var card = g.CreateSnapshot(0).Players[0].Hand[0];
                Accept(g, new PlayCardCommand(0, card.Id, [], g.Revision, Prompt(g)!.PromptId));
            }
            else if (kind == CardKind.Nullification)
            {
                End(g);
                Reach(g, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0);
                Answer(g, c => c.Cards.Count == 1 && g.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == c.Cards[0]).Kind != CardKind.Nullification);
            }
            else
            {
                var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "fixture:xu-driver" && a.ConversionSource.BindingId == "slash");
                Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, Prompt(g)!.PromptId, action.PlayedCardKind)
                    { ConversionSource = action.ConversionSource });
            }
            Reach(g, p => p.SkillPrompt?.SkillId == "classic:shicai");
            var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
            Require(window.Action.EffectiveKind == kind && window.Action.PhysicalCards.Count == 1 &&
                (kind == CardKind.Crossbow || window.Action.ConversionChain.Count > 0),
                "Single-card conversions qualify by the effective card category, including Slash and Nullification.");
            var material = window.Action.PhysicalCards.Single().CardId;
            Require(Prompt(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"), "Shicai remains optional.");
            Replay(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, p => p.SkillPrompt?.SkillId == "fixture:xu-observer");
            Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:shicai" && f.AlternatingSuitTop is { Stage: "reward-draw" }) &&
                g.CardMovements.Count(m => m.CardId == material && m.To == CardLocation.DrawPile && m.Reason.Value == "skill-program.completed-top.place") == 1 &&
                g.CardMovements.Count(m => m.Reason.Value == "skill-program.completed-top.draw") == 1,
                "Activation directly places its sole entity and draws once, without a redundant ordering decision.");
            Replay(g, r);
            g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            if (kind == CardKind.Nullification)
            {
                for (var step = 0; step < 32 && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:shicai"); step++)
                    Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
            else ReachPlay(g);
            Replay(g, r);
            Require(g.CardMovements.Count(m => m.Reason.Value == "skill-program.completed-top.draw") == 1, "Cold return never repeats the paid top-deck or reward.");
        }

        foreach (var spear in new[] { false, true })
        {
            var (g, r) = Create(spear: spear); ReachPlay(g);
            if (spear)
            {
                var weapon = g.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.ZhangbaSerpentSpear);
                Accept(g, new PlayCardCommand(0, weapon.Id, [], g.Revision, Prompt(g)!.PromptId));
                Reach(g, p => p.SkillPrompt?.SkillId == "classic:shicai");
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); ReachPlay(g);
                var costs = g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
                Accept(g, new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, costs, [1], g.Revision, Prompt(g)!.PromptId));
            }
            else Activate(g, "fixture:xu-driver", "virtual", [], [1]);
            for (var step = 0; step < 80 && Prompt(g) is not { PlayerSeat: 0, Kind: DecisionKind.PlayCard }; step++)
            {
                Require(Prompt(g)?.SkillPrompt?.SkillId != "classic:shicai", "Zero-entity virtual Slash and two-entity Zhangba Slash do not offer Shicai.");
                Drive(g);
            }
            Require(Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                !g.CardMovements.Any(m => m.Reason.Value is "skill-program.completed-top.place" or "skill-program.completed-top.draw"),
                "Ineligible virtual or multi-entity cards neither top-deck nor draw a Shicai reward.");
            Replay(g, r);
        }
    }

    public static void BottomDrawAndAlternatingReplay()
    {
        var (control,_)=Create(bottom:false);ReachPlay(control);var (g,r)=Create();var before=Pile(control);ReachPlay(g);Require(control.CardMovements.Where(m=>m.Reason==CardMoveReasons.InitialDeal).Select(m=>m.CardId).SequenceEqual(g.CardMovements.Where(m=>m.Reason==CardMoveReasons.InitialDeal).Select(m=>m.CardId)),"Initial deal preserves its normal direction for all players.");
        var draw=g.CardMovements.Where(m=>m.Reason==CardMoveReasons.Draw&&m.To==CardLocation.Hand(0)).Select(m=>m.CardId).ToArray();
        Require(draw.SequenceEqual(before.Take(2)),"Normal draw takes bottom in actual order.");
        var bottom=Pile(g)[0];var start=g.CreateSnapshot(0).Players[0].Hand.Count;
        Activate(g,"classic:chenglue","alternate",[],[]);
        var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PendingDecision is null),"Other ordinary viewers cannot inspect the owner's private discard choices.");Require(f.AlternatingSuitTop is {Stage:"discard",Required:2},"Yang commits then draws once before real hand cost.");
        Require(g.CreateSnapshot(0).PendingDecision is { RequiredCardCount: 2, MinimumCardCount: 1 },
            "Chenglue publishes its full remaining cost while allowing incremental confirmations.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Any(c=>c.Id==bottom)&&g.CreateSnapshot(0).Players[0].Hand.Count==start+1,"Skill draw also takes the actual bottom.");Replay(g,r);
        var state=State(g);var p=Prompt(g)!;var rejected=g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision));
        Require(rejected.Error is not null&&State(g)==state,"Wrong actor rejects atomically.");
        Answer(g,c=>c.Cards.Count==1);Replay(g,r);
        Require(g.CreateSnapshot(0).PendingDecision is { RequiredCardCount: 1, MinimumCardCount: 1 } &&
            !g.CardMovements.Any(m => m.Reason.Value == "skill-program.alternating-suit.discard"),
            "A partial confirmation reduces the published quota without moving an incomplete discard set.");
        Answer(g,c=>c.Cards.Count==1);ReachPlay(g);
        Require(g.Events.Select(e=>e.Payload).OfType<PlayPhaseSuitAllowanceGrantedEvent>().Last().DiscardedCardIds.Count==2,"Only actual two hand costs grant suits.");
        var again=g.Submit(new UseProgramSkillCommand(0,"classic:chenglue","alternate",[],[],g.Revision,Prompt(g)!.PromptId));Require(again.Error is not null,"Own actual Play quota rejects second activation.");
        var turn=g.CreateSnapshot(0).TurnNumber;End(g);Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard&&g.CreateSnapshot(0).TurnNumber>turn);
        Activate(g,"classic:chenglue","alternate",[],[]);Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().AlternatingSuitTop is {Required:1},"Next activation is Yin, with one actual discard.");Answer(g,c=>c.Cards.Count==1);Replay(g,r);
    }
    private static int[] Pile(GameEngine g)=>g.CreateCardZoneDiagnostics().Where(d=>d.Location==CardLocation.DrawPile).OrderBy(d=>d.ZoneIndex).Select(d=>d.CardId).ToArray();
    private static PendingDecision? Prompt(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).SelectMany(s=>new[]{g.CreateSnapshot(s),g.CreateSnapshot(s,true)}))+JsonSerializer.Serialize(g.CreateCardZoneDiagnostics());
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All-viewer checkpoint replay preserves exact paused state.");Require(g.CreateCardZoneDiagnostics().Count==100&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"Physical entities conserved.");}
    private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(x.Error is null,x.Error?.Message??"Rejected");}
    private static void Require(bool b,string s){if(!b)throw new InvalidOperationException(s);}
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var n=0;n<160;n++){if(Prompt(g)is{}p&&predicate(p))return;Drive(g);}throw new InvalidOperationException("Missing boundary "+Prompt(g));}
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
    private static void Activate(GameEngine g,string skill,string id,int[] cards,int[] targets)=>Accept(g,new UseProgramSkillCommand(0,skill,id,cards,targets,g.Revision,Prompt(g)!.PromptId));
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Drive(GameEngine g){if(Prompt(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(Prompt(g)is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Cards.Count<=1);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static (GameEngine,ContentRegistry) Create(bool nested=false,bool mixed=false,bool native=false,bool aiOwner=false,bool bottom=true,bool response=false,bool claimed=false,bool spear=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(nested,mixed,native,aiOwner,bottom,response,claimed,spear));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-xu-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,native?"classic:xu-you":"fixture:xu-owner",g.Revision,Prompt(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(bool nested,bool mixed,bool native,bool aiOwner,bool bottom,bool response,bool claimed,bool spear):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-xu",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xu-driver","revision":1,"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"multi","inputKinds":[],"inputSuits":[],"inputCount":2,"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[{"id":"lose-allowance-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:chenglue"],"sourceBind":"standard:none"}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},{"id":"multi","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"multi","outputKind":"slash"}]}]}]}""";
            var definition = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
            var driver = definition["skills"]![0]!;
            driver["viewAs"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"nullification","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}"""));
            driver["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}"""));
            var c=SkillProgramCatalog.Load(definition.ToJsonString(),"""{"schemaVersion":3,"skills":{"fixture:xu-driver":{"name":"真实动作","description":"测试"}}}""");b.AddSkill(new("fixture:xu-driver","真实动作","测试"){Program=c.Programs["fixture:xu-driver"]});
            if(nested){var n=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xu-observer","revision":1,"triggers":[{"id":"placed","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.completed-top.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:xu-observer":{"name":"移动暂停","description":"测试","optionLabels":{"continue":"继续"}}}}""");b.AddSkill(new("fixture:xu-observer","移动暂停","测试"){Program=n.Programs["fixture:xu-observer"]});}
            b.AddGeneral(new("fixture:xu-owner","测试拥有者","supporter","classic:chenglue","qun",20,nested?["classic:shicai","classic:cunmu","fixture:xu-driver","fixture:xu-observer"]:bottom?["classic:shicai","classic:cunmu","fixture:xu-driver"]:["classic:shicai","fixture:xu-driver"]));
            if (response)
            {
                var attacker = SkillProgramCatalog.Load($$"""
                    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xu-trick","revision":1,
                    "triggers":[{"id":"duel","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
                    "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingHighestHp"},
                    {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"duel"}]}]}]}
                    """, """{"schemaVersion":3,"skills":{"fixture:xu-trick":{"name":"对手锦囊","description":"测试"}}}""");
                b.AddSkill(new("fixture:xu-trick", "对手锦囊", "测试") { Program = attacker.Programs["fixture:xu-trick"] });
            }
            b.AddSkill(new("fixture:xu-idle","测试静态","测试"));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:xu-target-{i}","目标","supporter",response&&i==1?"fixture:xu-trick":aiOwner?"classic:chenglue":claimed&&i==1?"classic:jianxiong":"fixture:xu-idle","qun",response?3:20));
            b.AddDeck(new("fixture:xu-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(response?i%3==0?"standard:slash":i%3==1?"standard:dodge":"standard:nullification":spear?"classic:zhangba-serpent-spear":"standard:crossbow",mixed?(i%2==0?Suit.Spade:Suit.Heart):Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-xu-check","测试",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xu-deck",GeneralCandidateCount:4,GeneralPoolIds:[native?"classic:xu-you":"fixture:xu-owner","fixture:xu-target-1","fixture:xu-target-2","fixture:xu-target-3"]));
        }
    }
}
