using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2016StateChecks
{

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











