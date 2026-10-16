using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Sun Ce: docs/content/sources/boundary-sun-ce-2026-10-04.json.
internal static class BoundarySunCeChecks
{
    private const string Jiang="boundary:jiang-current";
    private const string Hunzi="boundary:hunzi-current";
    private const string Zhiba="boundary:zhiba-current";
    private const string DriverSkill="fixture:sc-driver";

    public static void JiangReclaimsDiscardOncePerTurn()
    {
        var(g,r)=Start(scenario:"redslash",dumpAis:true);
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"dump",[],[],g.Revision,P(g)!.PromptId));
        Settle(g);
        Require(Claims(g)==0,
            "The owner's own discards never offer the reclaim: the window answers other players only.");
        var hp=g.CreateSnapshot(0).Players[0].Hp;
        ReachOtherTurn(g,turnNumber:2);
        Reach(g,p=>p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")==Jiang));
        Activate(g);
        ReachPlayOrOtherPrompt(g);
        Require(Claims(g)==1&&g.CreateSnapshot(0).Players[0].Hp==hp-1,
            "The first discarded red slash of the turn costs one HP and moves to the owner's hand exactly once.");
        var afterClaimHand=g.CreateSnapshot(0).Players[0].Hand!.Count;
        ReachOtherTurn(g,turnNumber:3);
        SkipAllJiangPrompts(g);
        Require(Claims(g)==1&&g.CreateSnapshot(0).Players[0].Hand!.Count==afterClaimHand&&
            g.CreateSnapshot(0).Players[0].Hp==hp-1,
            "The shared once-per-turn group renews next turn but a skipped offer claims nothing further.");
        ReplayFourViews(g,r);Replay(g,r);
    }

    private static int Claims(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<ProgramMovedCardsClaimedEvent>().Count();

    private static void ReachOtherTurn(GameEngine g,int turnNumber)
    {
        for(var i=0;i<400;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat!=0&&e.TurnNumber>=turnNumber))return;
            if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0}) Answer(g,c=>c.Cards.Count==1);
            else if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}) Accept(g,new EndPlayPhaseCommand(0,g.Revision));
            else if(P(g) is {PlayerSeat:0} prompt&&prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")==Jiang))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException($"The fixture never reached turn {turnNumber} after the owner's.");
    }

    private static void ReachPlayOrOtherPrompt(GameEngine g)
    {
        for(var i=0;i<400;i++)
        {
            var pending=P(g);
            if(pending is {Kind:DecisionKind.PlayCard,PlayerSeat:0})return;
            if(pending is {PlayerSeat:0} prompt&&prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")==Jiang))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture did not return past the reclaim batch.");
    }

    private static void SkipAllJiangPrompts(GameEngine g)
    {
        for(var i=0;i<40;i++)
        {
            if(P(g) is not {PlayerSeat:0} prompt||
               !prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")==Jiang))return;
            Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        }
        throw new InvalidOperationException("The fixture kept offering Jiang prompts.");
    }

    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Require(result.Accepted,result.Error?.Message??"Command rejected.");}

    private static (GameEngine,ContentRegistry) Start(string scenario="",bool dumpAis=false,int maxTurns=12)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(scenario,dumpAis));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:sc",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=maxTurns},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(g,new SelectGeneralCommand(0,"fixture:sc",g.Revision,P(g)!.PromptId));
        Settle(g);return(g,r);
    }

    private static string Activation(string id,string effects,int targets=0,string targetKind="anyLiving")=>
        "{\"id\":\""+id+"\",\"usesPerTurn\":null,\"usesPerPhase\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+
        ",\"maxTargets\":"+targets+",\"targetKind\":\""+targetKind+"\",\"effects\":"+effects+"}";

    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+
        ",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}",
        "{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];

    private sealed class Fixture(string scenario,bool dumpAis):IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sc", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(new("fixture:sc-driver","驱动","测试"){Program=Load("fixture:sc-driver",
                "\"activations\":["+
                Activation("dump","[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]")+","+
                Activation("hurt","[{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"owner\"},\"amount\":4}]")+","+
                Activation("provoke","[{\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"provoke-answer\"}]",1,"otherLivingWhoseAttackRangeIncludesOwner")+
                "]")});
            b.AddSkill(new("fixture:sc-dump","卸牌","测试"){Program=Load("fixture:sc-dump",
                "\"triggers\":[{\"id\":\"dump\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":["+
                "{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"numberExpression\":\"allOwnedZoneCards\",\"resultBind\":\"dump-hand\"},"+
                "{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"dump-hand\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}]")});
            b.AddGeneral(new("fixture:sc","界孙策","boundary_sun_ce",Jiang,"wu",4,
                new[]{Hunzi,Zhiba,DriverSkill}));
            b.AddGeneral(new("fixture:sc-1","吴将","supporter","standard:none","wu",4,
                dumpAis?new[]{"fixture:sc-dump"}:null));
            b.AddGeneral(new("fixture:sc-2","魏将","supporter","standard:none","wei",4,
                dumpAis?new[]{"fixture:sc-dump"}:null));
            b.AddGeneral(new("fixture:sc-3","魏将","supporter","standard:none","wei",4,
                dumpAis?new[]{"fixture:sc-dump"}:null));
            b.AddDeck(new("fixture:sc-deck","固定",4,1,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(
                scenario=="peach"?"standard:peach":
                scenario=="crossbow"?"standard:crossbow":
                scenario=="duel"?"standard:duel":"standard:slash",
                scenario=="blackslash"||scenario=="crossbow"?Suit.Spade:Suit.Heart,7)).ToArray()});
            b.AddMode(new("identity:sc","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},
                "fixture:sc-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:sc","fixture:sc-1","fixture:sc-2","fixture:sc-3"]));
        }
    }
}
