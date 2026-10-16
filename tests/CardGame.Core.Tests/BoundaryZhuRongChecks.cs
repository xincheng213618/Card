using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Zhu Rong: docs/content/sources/boundary-zhu-rong-2026-10-04.json.
internal static class BoundaryZhuRongChecks
{
    private const string Juxiang="boundary:juxiang-current";
    private const string Lieren="boundary:lieren-current";
    private const string Changbiao="boundary:changbiao-current";
    private const string DriverSkill="fixture:zr-driver";

    public static void JuxiangImmuneAndClaimsResolvedBarbarian()
    {
        var(g,r)=Start(scenario:"barbarian");
        var before=g.CreateSnapshot(0).Players[0].Hand!.Count;
        var hp=g.CreateSnapshot(0).Players[0].Hp;
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        for(var i=0;i<300&&g.Events.Select(e=>e.Payload).OfType<JuxiangCardClaimedEvent>().All(e=>e.OwnerSeat!=0);i++)
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(g.Events.Select(e=>e.Payload).OfType<JuxiangCardClaimedEvent>().Any(e=>e.OwnerSeat==0),
            "A resolved Savage Assault used by another character ends up with the immune owner.");
        var view=g.CreateSnapshot(0).Players[0];
        Require(view.Hp==hp&&view.Hand!.Count==before+1,
            "Juxiang never lets the Savage Assault affect the owner, who gains exactly the resolved card.");
        Replay(g,r);
    }

    private static (GameEngine,ContentRegistry) Start(string scenario="",bool plain=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(scenario,plain));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:zr",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(g,new SelectGeneralCommand(0,plain?"fixture:zr-plain":"fixture:zr",g.Revision,P(g)!.PromptId));
        Settle(g);return(g,r);
    }

    private sealed class Fixture(string scenario,bool plain):IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zr", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(new("fixture:zr-driver","驱动","测试"){Program=Load(DriverSkill,
                "\"modifiers\":[],\"viewAs\":[],\"activations\":["+Activation("dump","[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]")+"]"+
                ",\"cardPolicies\":[{\"id\":\"boost\",\"kind\":\"pindianRankBySuit\",\"inputSuit\":\"heart\",\"value\":13}]")});
            b.AddSkill(new("fixture:zr-driver-plain","驱动","测试"){Program=Load("fixture:zr-driver-plain",
                "\"modifiers\":[],\"viewAs\":[],\"activations\":["+Activation("dump","[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]")+"]"+
                ",\"cardPolicies\":[{\"id\":\"boost\",\"kind\":\"pindianRankBySuit\",\"inputSuit\":\"heart\",\"value\":2}]")});
            b.AddGeneral(new("fixture:zr","界祝融","boundary_zhu_rong",DriverSkill,"shu",4,
                new[]{Juxiang,Lieren,Changbiao},GeneralGender.Female));
            b.AddGeneral(new("fixture:zr-plain","界祝融","boundary_zhu_rong","fixture:zr-driver-plain","shu",4,
                new[]{Juxiang,Lieren,Changbiao},GeneralGender.Female));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:zr-{i}","其他"+i,"supporter","standard:none","wei",5,null));
            b.AddDeck(new("fixture:zr-deck","固定",4,1,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(
                scenario=="barbarian"?"standard:barbarian_assault"
                    :scenario=="dodgy"?(i%2==0?"standard:slash":"standard:dodge"):"standard:slash",
                Suit.Heart,2)).ToArray()});
            b.AddMode(new("identity:zr","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},
                "fixture:zr-deck",GeneralCandidateCount:4,
                GeneralPoolIds:[plain?"fixture:zr-plain":"fixture:zr","fixture:zr-1","fixture:zr-2","fixture:zr-3"]));
        }
    }

    private static string Activation(string id,string effects)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";

    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
}
