using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2016StateChecks
{
    public static void GrowthLimitChainAndReplay()
    {
        var (liu, registry) = Create("classic:zongzuo", 2);
        var owner = liu.CreateSnapshot(0, true).Players[0];
        Require(owner.MaxHp == 7 && owner.Hp == 7, $"Four living factions add four HP and max HP to base two plus lord bonus: {owner.Hp}/{owner.MaxHp}, factions {string.Join(",",liu.CreateSnapshot(0,true).Players.Select(p=>p.FactionId))}.");
        Replay(liu, registry);
        var victim = liu.CreateSnapshot(0, true).Players.First(p => p.Seat != 0 && registry.Generals[p.GeneralId].FactionId != "qun");
        Use(liu, "fixture:population-driver", "kill", [victim.Seat]); Drain(liu);
        Require(liu.CreateSnapshot(0, true).Players[0].MaxHp == 6, "Last living faction death reduces max HP once.");
        Require(!liu.CardMovements.Any(m => m.Reason.Value.Contains("zongzuo") && m.From.Zone == CardZoneKind.DrawPile), "Faction extinction never draws cards.");
        Replay(liu, registry);

        var (cen, cr) = Create("classic:jishe", 3, additional: ["classic:lianhuo"]);
        var limit = HandLimit(cen);
        for (var i = 0; i < limit; i++)
        {
            var before = cen.CreateSnapshot(0, true).Players[0].Hand.Count;
            Use(cen, "classic:jishe", "draw-and-reduce-limit"); Drain(cen);
            Require(HandLimit(cen) == limit - i - 1 && cen.CreateSnapshot(0, true).Players[0].Hand.Count == before + 1, "Each paid activation draws once and stacks a negative turn modifier.");
            Replay(cen, cr);
        }
        Require(!cen.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:jishe"), "Zero hand limit blocks another activation.");
        var state = State(cen); var prompt = Pending(cen)!;
        var illegal = cen.Submit(new UseProgramSkillCommand(0, "classic:jishe", "draw-and-reduce-limit", [], [], cen.Revision, prompt.PromptId));
        Require(!illegal.Accepted && State(cen) == state, "Illegal extra activation is atomic.");
        Use(cen, "fixture:population-driver", "chain", [0]); Drain(cen);
        Use(cen, "fixture:population-driver", "chain", [1]); Drain(cen);
        var hp0 = cen.CreateSnapshot(0, true).Players[0].Hp; var hp1 = cen.CreateSnapshot(0, true).Players[1].Hp;
        Use(cen, "fixture:population-driver", "fire", [0]); Drain(cen);
        Require(cen.CreateSnapshot(0, true).Players[0].Hp == hp0 - 2 && cen.CreateSnapshot(0, true).Players[1].Hp == hp1 - 2, "Fire origin gains exactly one damage and passes the finalized amount to the other chained player.");
        Replay(cen, cr);
        Use(cen, "fixture:population-driver", "chain", [0]); Drain(cen);
        Use(cen, "fixture:population-driver", "chain", [1]); Drain(cen);
        hp0 = cen.CreateSnapshot(0, true).Players[0].Hp; hp1 = cen.CreateSnapshot(0, true).Players[1].Hp;
        Use(cen, "fixture:population-driver", "fire", [1]); Drain(cen);
        Require(cen.CreateSnapshot(0,true).Players[0].Hp == hp0-1 && cen.CreateSnapshot(0,true).Players[1].Hp == hp1-1,
            "Propagated fire received by the modifier owner must not amplify again.");
        Replay(cen, cr);
        Use(cen, "fixture:population-driver", "clear"); Drain(cen);
        Accept(cen, new EndPlayPhaseCommand(0, cen.Revision, Pending(cen)!.PromptId));
        for (var i = 0; i < 60 && Pending(cen)?.SkillPrompt?.SkillId != "classic:jishe"; i++) Advance(cen);
        Require(Pending(cen)?.SkillPrompt?.SkillId == "classic:jishe", "Empty-hand ending exposes chain choice.");
        Answer(cen, Pending(cen)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        for (var i = 0; i < 20 && !(Pending(cen)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets") == true); i++) Advance(cen);
        var selection = Pending(cen)!;
        Require(selection.Choices.All(c => c.Targets.Count <= cen.CreateSnapshot(0, true).Players[0].Hp), "Current HP caps every target choice.");
        Replay(cen, cr);
        var pick = selection.Choices.First(c => c.Targets.Count > 0);
        Answer(cen, pick);
        for(var i=0;i<30 && cen.ResolutionStack.Count > 0;i++) Advance(cen);
        Require(pick.Targets.All(seat => cen.CreateSnapshot(0, true).Players[seat].IsChained), "Ending chain sets the selected characters chained.");
        Replay(cen, cr);
    }

    public static void RangeActorEquipmentPaymentAndReplay()
    {
        var (game, registry) = Create("classic:zhige", 2, equipment: true);
        Use(game,"fixture:population-driver","equip-target",[1]);
        Answer(game,Pending(game)!.Choices.First()); Drain(game);
        var card=game.CreateSnapshot(0,true).Players[1].Equipment.Single();
        Use(game,"classic:zhige","request-slash-or-equipment");
        var target=Pending(game)!;
        Require(target.Choices.All(c=>c.Targets.Count == 1 && c.Targets[0] is 1 or 4),"Only another character whose attack range contains owner may be ordered.");
        Replay(game,registry);
        Answer(game,target.Choices.First(c=>c.Targets.SequenceEqual([1])));
        var request=Pending(game)!;
        Require(request.PlayerSeat==1,"The ordered actor selects the Slash target privately.");
        Require(game.CreateSnapshot(2,false).PendingDecision is null,"An uninvolved viewer cannot inspect the actor prompt.");
        Replay(game,registry);
        Answer(game,request.Choices.First(c=>c.Targets.SequenceEqual([0])));
        var decision=Pending(game)!;
        Answer(game,decision.Choices.Single(c=>c.Parameters.GetValueOrDefault("request-option")=="decline"));
        var pay=Pending(game)!;
        Require(pay.PlayerSeat==1 && pay.Choices.Any(c=>c.Cards.Contains(card.Id)),"The actor chooses its own actual equipped card.");
        var state=State(game);
        var invalid=game.Submit(new AnswerPromptCommand(0,pay.PromptId,pay.Choices.First().Id,game.Revision));
        Require(!invalid.Accepted && State(game)==state,"Wrong payer cannot mutate or move the equipment.");
        Replay(game,registry);
        Answer(game,pay.Choices.First(c=>c.Cards.Contains(card.Id))); Drain(game);
        Require(game.CardMovements.Any(m=>m.CardId==card.Id && m.From==CardLocation.Equipment(1) && m.To==CardLocation.Hand(0)),"Decline transfers the same physical equipped card to the owner.");
        Require(!game.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="classic:zhige"),"The phase usage debit survives payment.");
        Replay(game,registry);
    }

    public static void RealOrderedSlashAndReplay()
    {
        var (game, registry) = Create("classic:zhige",2);
        Use(game,"classic:zhige","request-slash-or-equipment");
        Answer(game,Pending(game)!.Choices.First(c=>c.Targets.SequenceEqual([1])));
        Answer(game,Pending(game)!.Choices.First(c=>c.Targets.SequenceEqual([2])));
        var request=Pending(game)!;
        var slash=request.Choices.First(c=>c.Parameters.GetValueOrDefault("request-option")=="use");
        var card=slash.Cards.Single();
        var hp=game.CreateSnapshot(0,true).Players[2].Hp;
        Replay(game,registry);
        Answer(game,slash); Drain(game);
        Require(game.CardMovements.Any(m=>m.CardId==card && m.From==CardLocation.Hand(1) && m.To==CardLocation.Processing),"Ordered Slash uses its real actor-owned physical card.");
        Require(game.CreateSnapshot(0,true).Players[2].Hp==hp-1,"The actor's chosen legal Slash target receives real damage.");
        Require(!game.CardMovements.Any(m=>m.From==CardLocation.Equipment(1) && m.To==CardLocation.Hand(0)),"Successful Slash never charges equipment payment.");
        Replay(game,registry);
    }

    public static void TargetPhaseSuitUseAndReplay()
    {
        var (game,registry)=Create("classic:jiyu",3);
        var card=game.CreateSnapshot(0,true).Players[1].Hand.First();
        Use(game,"classic:jiyu","hand-discard-restriction",[1]);
        var pay=Pending(game)!;
        Require(pay.PlayerSeat==1 && game.CreateSnapshot(2,false).PendingDecision is null,"Target alone chooses private discard.");
        Replay(game,registry);
        Answer(game,pay.Choices.First(c=>c.Cards.Contains(card.Id))); Drain(game);
        Require(game.CardMovements.Any(m=>m.CardId==card.Id && m.From==CardLocation.Hand(1) && m.To==CardLocation.DiscardPile),"Jiyu commits the target physical discard.");
        Require(game.GetHumanLegalActions().All(a=>a.CardId is not { } id || game.CreateSnapshot(0,true).Players[0].Hand.FirstOrDefault(c=>c.Id==id)?.Suit!=card.Suit),"Owner cannot use the frozen discarded suit.");
        var state=State(game);var prompt=Pending(game)!;
        var invalid=game.Submit(new UseProgramSkillCommand(0,"classic:jiyu","hand-discard-restriction",[],[1],game.Revision,prompt.PromptId));
        Require(!invalid.Accepted && State(game)==state,"Each participant usage is phase scoped and repeat rejection atomic.");
        Replay(game,registry);
    }
    public static void PrivateDamageOfferAndReplay()
    {
        var (game,registry)=Create("classic:huisheng",3,additional:["classic:zhige"]);
        Use(game,"classic:zhige","request-slash-or-equipment");
        Answer(game,Pending(game)!.Choices.First(c=>c.Targets.SequenceEqual([1])));
        Answer(game,Pending(game)!.Choices.First(c=>c.Targets.SequenceEqual([0])));
        Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("request-option")=="use"));
        for(var i=0;i<50 && Pending(game)?.SkillPrompt?.SkillId!="classic:huisheng";i++)
        {
            if(Pending(game) is { PlayerSeat:0,Kind:DecisionKind.RespondDodge } p) Answer(game,p.Choices.Last()); else Advance(game);
        }
        Require(Pending(game)?.SkillPrompt?.SkillId=="classic:huisheng","Real other-source Slash offers damage prevention.");
        Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        var own=Pending(game)!;var offered=own.Choices.First(c=>c.Cards.Count==1).Cards.Single();
        Answer(game,own.Choices.First(c=>c.Cards.Contains(offered)));
        Answer(game,Pending(game)!.Choices.Single(c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards"));
        var offer=Pending(game)!;
        Require(offer.PlayerSeat==1 && game.CreateSnapshot(1,false).PrivateRevealedCards?.Single().Id==offered,"Actual source normal snapshot contains private offered face.");
        Require(game.CreateSnapshot(2,false).PrivateRevealedCards is null && game.CreateSnapshot(2,false).PendingDecision is null,"Other viewers cannot inspect the offer.");
        Replay(game,registry);
        var state=State(game);var invalid=game.Submit(new AnswerPromptCommand(2,offer.PromptId,offer.Choices.First().Id,game.Revision));
        Require(!invalid.Accepted && State(game)==state,"Wrong source answer is atomic.");
        var hp=game.CreateSnapshot(0,true).Players[0].Hp;
        Answer(game,offer.Choices.First(c=>c.Parameters.GetValueOrDefault("option")=="gain"));Drain(game);
        Require(game.CreateSnapshot(0,true).Players[0].Hp==hp && game.CardMovements.Any(m=>m.CardId==offered && m.To==CardLocation.Hand(1)),"Real gain prevents damage.");
        Require(game.CreateSnapshot(1,false).PrivateRevealedCards is null,"Private visibility ends after offer resolves.");
        Use(game,"fixture:population-driver","request",[1]);
        Answer(game,Pending(game)!.Choices.First(c=>c.Targets.SequenceEqual([0])));
        Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("request-option")=="use"));
        for(var i=0;i<50 && game.ResolutionStack.Count>0;i++)
        {
            Require(Pending(game)?.SkillPrompt?.SkillId!="classic:huisheng","Successful actual source pair cannot offer again.");
            if(Pending(game) is {PlayerSeat:0,Kind:DecisionKind.RespondDodge} p) Answer(game,p.Choices.Last()); else Advance(game);
        }
        Require(game.CreateSnapshot(0,true).Players[0].Hp==hp-1,"Repeated same source deals damage after permanently successful offer.");
        Replay(game,registry);
    }

    public static void OrderedEndingDiscardDrawAndReplay()
    {
        var (game,registry)=Create("classic:qinqing",3);
        Accept(game,new EndPlayPhaseCommand(0,game.Revision,Pending(game)!.PromptId));
        for(var i=0;i<80 && Pending(game)?.SkillPrompt?.SkillId!="classic:qinqing";i++) Advance(game);
        Require(Pending(game)?.SkillPrompt?.SkillId=="classic:qinqing","Lord-range ending offers selection.");
        Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        var pick=Pending(game)!.Choices.First(c=>c.Targets.Count==2);
        Require(pick.Targets.All(t=>t!=0),"Lord is excluded from own attack range.");
        Answer(game,pick);var start=game.CardMovements.Count;
        for(var i=0;i<2;i++)
        {
            var prompt=Pending(game)!;
            Require(prompt.Choices.All(c=>c.Parameters.GetValueOrDefault("program-action")=="participant-discard" && c.Cards.Count==0),"Other hand discard choices use opaque slots.");
            Require(!game.CardMovements.Skip(start).Any(m=>m.From.Zone==CardZoneKind.DrawPile),"All selected discards precede any draws.");
            Replay(game,registry);Answer(game,prompt.Choices.First());
        }
        for(var i=0;i<50 && game.ResolutionStack.Count>0;i++) Advance(game);
        var moves=game.CardMovements.Skip(start).ToArray();
        Require(moves.Take(2).All(m=>m.To==CardLocation.DiscardPile) && pick.Targets.All(t=>moves.Any(m=>m.From.Zone==CardZoneKind.DrawPile && m.To==CardLocation.Hand(t))),"Discard group precedes draw group for every selected target.");
        Replay(game,registry);
    }

    public static void PrivateOfferRefusalAndLoaderProof()
    {
        foreach(var zero in new[]{false,true})
        {
            var (game,registry)=Create("classic:huisheng",3);
            Use(game,"fixture:population-driver","request",[1]);
            Answer(game,Pending(game)!.Choices.First(c=>c.Targets.SequenceEqual([0])));
            Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("request-option")=="use"));
            for(var i=0;i<50 && Pending(game)?.SkillPrompt?.SkillId!="classic:huisheng";i++)
                if(Pending(game) is {PlayerSeat:0,Kind:DecisionKind.RespondDodge} p) Answer(game,p.Choices.Last()); else Advance(game);
            Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
            if(!zero)
                for(var i=0;i<2;i++) Answer(game,Pending(game)!.Choices.First(c=>c.Cards.Count==1));
            Answer(game,Pending(game)!.Choices.Single(c=>c.Parameters.GetValueOrDefault("program-action")=="finish-owned-cards"));
            var offer=Pending(game)!;var hp=game.CreateSnapshot(0,true).Players[0].Hp;
            Require(!zero || offer.Choices.All(c=>c.Parameters.GetValueOrDefault("option")!="gain"),"Zero offer never creates a gain or prevention branch.");
            Replay(game,registry);Answer(game,offer.Choices.Single(c=>c.Parameters.GetValueOrDefault("option")=="refuse"));
            if(!zero) for(var i=0;i<2;i++){Replay(game,registry);Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("option")=="discard"));}
            Drain(game);
            Require(game.CreateSnapshot(0,true).Players[0].Hp==hp-1,"Refusal pays its cost and damage continues.");
            Require(!game.Events.Select(e=>e.Payload).OfType<SkillUsageConsumedEvent>().Any(e=>e.SkillId=="classic:huisheng" && e.Scope==SkillUsageScope.Game),"Refused source is never permanently blacklisted.");
            Replay(game,registry);
        }
        string ReadResource(string kind)
        {
            using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(
                $"CardGame.Content.Standard.SkillPrograms.classic-huang-hao.{kind}.json")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var rules = ReadResource("rules");
        var presentation = ReadResource("presentation");
        var invalid=rules.Replace("\"equipment\"","\"judgment\"");
        var rejected=false;try{SkillProgramCatalog.Load(invalid,presentation);}catch(InvalidOperationException){rejected=true;}
        Require(rejected,"Loader rejects a private offer from a judgment-zone producer.");
    }

    public static void FullDiscardPhaseSuitLedgerAndReplay()
    {
        var (game,registry)=Create("classic:guizao",3,interactiveDiscard:true,reclaim:true);
        Accept(game,new EndPlayPhaseCommand(0,game.Revision,Pending(game)!.PromptId));
        for(var i=0;i<50 && Pending(game)?.Kind!=DecisionKind.DiscardCards;i++) Advance(game);
        var prompt=Pending(game)!;Require(prompt.Kind==DecisionKind.DiscardCards && prompt.RequiredCardCount==2,"Fixture requires two real phase discards.");
        var selected=game.CreateSnapshot(0,true).Players[0].Hand.GroupBy(c=>c.Suit).Take(2).Select(g=>g.First().Id).ToArray();
        Replay(game,registry);Accept(game,new DiscardCardsCommand(0,selected,prompt.PromptId,game.Revision));
        for(var i=0;i<60 && Pending(game)?.SkillPrompt?.SkillId!="classic:guizao";i++) Advance(game);
        Require(Pending(game)?.SkillPrompt?.SkillId=="classic:guizao","Distinct complete-phase suits expose the benefit before movement continuation.");

        Replay(game,registry);Answer(game,Pending(game)!.Choices.First(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(game,Pending(game)!.Choices.Single(c=>c.Parameters.GetValueOrDefault("option-id")=="draw"));
        for(var i=0;i<50 && !selected.All(id=>game.CardMovements.Any(m=>m.CardId==id && m.From==CardLocation.DiscardPile && m.To==CardLocation.Hand(1)));i++) Advance(game);
        Require(selected.All(id=>game.CardMovements.Any(m=>m.CardId==id && m.From==CardLocation.DiscardPile && m.To==CardLocation.Hand(1))),"Movement triggers really reclaim both discarded physical entities.");
        Replay(game,registry);
    }

    private static (GameEngine, ContentRegistry) Create(string skill, int hp, string[]? additional = null, bool equipment = false, bool interactiveDiscard = false, bool reclaim = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(skill, hp, additional, equipment,reclaim));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 11, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-population", UseInteractiveSetup = true, UseInteractiveDiscard = interactiveDiscard, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:population-owner", game.Revision, Pending(game)!.PromptId));
        for(var i=0;i<100 && Pending(game)?.Kind != DecisionKind.PlayCard;i++) Advance(game);
        Require(Pending(game)?.Kind == DecisionKind.PlayCard, "Play fixture unavailable.");
        return (game, registry);
    }
    private static int HandLimit(GameEngine game)
    {
        var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, [players[0]])!;
    }
    private static void Use(GameEngine game, string skill, string action, int[]? seats = null) => Accept(game, new UseProgramSkillCommand(0, skill, action, [], seats ?? [], game.Revision, Pending(game)!.PromptId));
    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0,5).Select(s=>game.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game,new AnswerPromptCommand(Pending(game)!.PlayerSeat,Pending(game)!.PromptId,choice.Id,game.Revision));
    private static void Advance(GameEngine game) => Accept(game,new AdvanceOneStepCommand(game.Revision));
    private static void Drain(GameEngine game)
    {
        for(var i=0;i<100 && (game.ResolutionStack.Count > 0 || Pending(game)?.Kind != DecisionKind.PlayCard);i++)
        {
            if(Pending(game) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } p) Answer(game,p.Choices.Last());
            else Advance(game);
        }
        Require(game.ResolutionStack.Count == 0, "Continuation did not finish.");
    }
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0,true));
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(game.CreateCheckpoint(),registry);
        Require(State(game)==State(restored),"Paused checkpoint state differs.");
        var log=GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        Require(State(game)==State(GameReplay.Restore(log,registry)),"Command JSON replay differs.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result=game.Submit(command);Require(result.Accepted,result.Error?.Message ?? "Command rejected.");
    }
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string skill,int hp,string[]? additional,bool equipment,bool reclaim) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("population-fixture",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder builder)
        {
            var c=SkillProgramCatalog.Load("""
            {"schemaVersion":62,"skills":[{"id":"fixture:population-driver","revision":1,"activations":[
            {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"request","chooserRef":{"kind":"selectedTarget"}}]},
            {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
            {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
            {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
            {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},
            {"id":"clear","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}
            ]}]}
            ""","""{"schemaVersion":3,"skills":{"fixture:population-driver":{"name":"测试","description":"测试"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:population-driver","测试","测试") { Program=c.Programs["fixture:population-driver"] });
            if(reclaim)
            {
                var rc=SkillProgramCatalog.Load("""{"schemaVersion":62,"skills":[{"id":"fixture:reclaim","revision":1,"triggers":[{"id":"claim","window":"discardPileReceived","subject":"owner","suits":["spade","club","heart","diamond"],"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:reclaim":{"name":"回收","description":"回收"}}}""");
                builder.AddSkill(new ContentSkillDefinition("fixture:reclaim","回收","回收"){Program=rc.Programs["fixture:reclaim"]});
            }
            builder.AddGeneral(new ContentGeneralDefinition("fixture:population-owner","测试","supporter","fixture:population-driver","qun",hp, [skill,..additional ?? []]));
            var targets=Enumerable.Range(1,4).Select(i=>"fixture:population-target-"+i).ToArray();
            var factions=new[]{"wei","shu","wu","qun"};
            for(var i=0;i<4;i++) builder.AddGeneral(new ContentGeneralDefinition(targets[i],"目标","supporter",reclaim && i==0 ? "fixture:reclaim":"standard:none",factions[i],8));
            builder.AddDeck(new ContentDeckRecipe("fixture:population-deck","测试",4,2,[new ContentDeckCardCount(equipment ? "standard:crossbow" : "standard:slash",200)]));
            builder.AddMode(new ContentModeDefinition("identity:classic-population","测试",5,5,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2,[nameof(Role.Renegade)]=1},"fixture:population-deck",GeneralCandidateCount:5,GeneralPoolIds:["fixture:population-owner",..targets]));
        }
    }
}











