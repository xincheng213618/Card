using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Jiang Wei: docs/content/sources/boundary-jiang-wei-2026-10-03.json.
internal static class BoundaryJiangWeiChecks
{
    private const string Skill="boundary:tiaoxin-current";
    private const string Awakening="boundary:zhiji-current";
    private const string Guanxing="classic:guanxing";

    public static void TauntDamagingSlashSuppressesDiscard()
    {
        var(g,r)=Start(scenario:"slash");
        var hp=g.CreateSnapshot(0).Players[0].Hp;
        Accept(g,new UseProgramSkillCommand(0,Skill,"taunt",[],[1],g.Revision,P(g)!.PromptId));
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].Hp==hp-1,
            "The unanswered requested slash damages the owner.");
        Require(g.CreateSnapshot(1).Players[1].Hand!.Count==3,
            "A slash that damaged the owner spares the attacker from the discard branch.");
        Require(g.CardMovements.Count(m=>m.From==CardLocation.Hand(1))==1,
            "Only the requested slash itself leaves the attacker's hand when it dealt damage.");
        Replay(g,r);
    }

    public static void ZhijiWakesOnceAcrossPrepareAndEndPhases()
    {
        var(g,r)=Start(scenario:"slash",offturn:true,maxTurns:14);
        ReachOwnerTurn(g,turnNumber:2);
        if(P(g) is {PlayerSeat:0} wake&&wake.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="draw-two"))
            Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="draw-two");
        var view=g.CreateSnapshot(0).Players[0];
        Require(view.MaxHp==4&&view.Skills!.Any(s=>s.ContentId==Guanxing),
            "The prepare-phase awakening lowers maximum HP and grants Guanxing before the second turn draws.");
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramOptionChosenEvent>().Count(e=>e.SkillId==Awakening)==1,
            "The prepare-phase awakening is the first and only recorded Zhi Ji choice.");
        Replay(g,r);
        ReachOwnerTurn(g,turnNumber:3);
        if(P(g) is {PlayerSeat:0} repeat&&repeat.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="draw-two"))
            Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="draw-two");
        Require(g.CreateSnapshot(0).Players[0].MaxHp==4&&
            g.Events.Select(e=>e.Payload).OfType<ProgramOptionChosenEvent>().Count(e=>e.SkillId==Awakening)==1,
            "The shared once-per-game group keeps Zhi Ji silent at later prepare and end phases.");
        Replay(g,r);
    }

    private static void ReachOwnerTurn(GameEngine g,int turnNumber)
    {
        for(var i=0;i<400;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber>=turnNumber))return;
            if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0}) Answer(g,c=>c.Cards.Count==1);
            else if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}) Accept(g,new EndPlayPhaseCommand(0,g.Revision));
            else if(P(g) is {Kind:DecisionKind.ProgramTrigger,PlayerSeat:0} prompt&&
                prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="draw-two"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="draw-two");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException($"The fixture never reached the owner's turn {turnNumber}.");
    }

    private const string DriverSkill="fixture:jw-driver";

    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Require(result.Accepted,result.Error?.Message??"Command rejected.");}

    private static (GameEngine,ContentRegistry) Start(string scenario="",bool offturn=false,int maxTurns=12)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(scenario,offturn));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:jw",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=maxTurns},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(g,new SelectGeneralCommand(0,"fixture:jw",g.Revision,P(g)!.PromptId));
        Settle(g);return(g,r);
    }

    private static string Activation(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";

    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];

    private sealed class Fixture(string scenario,bool offturn):IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-jw", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(new("fixture:jw-driver","驱动","测试"){Program=Load("fixture:jw-driver",
                "\"activations\":["+Activation("dump","[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]")+"]")});
            if(offturn)b.AddSkill(new("fixture:jw-offturn","卸牌","测试"){Program=Load("fixture:jw-offturn",
                "\"triggers\":[{\"id\":\"offturn\",\"window\":\"judgmentPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]")});
            b.AddGeneral(new("fixture:jw","界姜维","boundary_jiang_wei","boundary:tiaoxin-current","shu",4,
                offturn?new[]{"boundary:zhiji-current","fixture:jw-driver","fixture:jw-offturn"}
                    :new[]{"boundary:zhiji-current","fixture:jw-driver"}));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:jw-{i}","其他"+i,"supporter","standard:none","wei",5,null));
            b.AddDeck(new("fixture:jw-deck","固定",4,1,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(
                scenario=="peach"?"standard:peach":scenario=="dodgy"?(i%2==0?"standard:slash":"standard:dodge"):"standard:slash",
                Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:jw","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},
                "fixture:jw-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:jw","fixture:jw-1","fixture:jw-2","fixture:jw-3"]));
        }
    }
}
