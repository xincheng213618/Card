using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Guan Xing & Zhang Bao: docs/content/sources/boundary-guan-xing-zhang-bao-2026-10-04.json.
internal static class BoundaryGuanXingZhangBaoChecks
{
    private const string Skill="boundary:fuhun-current";
    private const string Wusheng="classic:wusheng";
    private const string Paoxiao="classic:paoxiao";

    public static void Definitions()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        var general=registry.Generals["boundary:guan-xing-zhang-bao"];
        Require(general.Name=="界关兴张苞"&&general.FactionId=="shu"&&general.BaseHp==4&&
            general.SkillIds.SequenceEqual(["boundary:fuhun-current"])&&
            general.VariantId=="boundary"&&general.RulesetId=="sanguosha-ol",
            "The boundary Guan Xing Zhang Bao general registers the current OL shu 4HP single skill.");
        var program=registry.GetSkill(Skill).Program!;
        var conversion=program.ViewAs.Single();
        Require(conversion.InputCount==2&&conversion.OutputKind==CardKind.Slash&&conversion.ForPlay&&conversion.ForResponse,
            "Fuhun converts exactly two cards into a slash for use or response.");
        var policy=program.CardPolicies.Single();
        Require(policy.Kind==SkillProgramCardPolicyKind.ConvertedSlashSameColorResponseOnly&&
            policy.CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash]),
            "The converted slash carries the same-color response restriction on the slash family.");
        var grant=program.Triggers.Single();
        Require(grant.Window==SkillProgramTriggerWindow.AfterDamageApplied&&
            grant.Effects.Single().Op==SkillProgramEffectOp.GrantTurnSkills&&
            grant.Effects.Single().SkillIds.SequenceEqual([Wusheng,Paoxiao]),
            "Own-turn slash damage grants Wusheng and Paoxiao for the turn.");
        Require(registry.GetSkill(Skill).ProgramPresentation!.Name=="父魂",
            "The presentation carries the current OL Fuhun name.");
    }

    public static void ConvertedSlashResponseFollowsPairColor()
    {
        var(g,r)=Start();
        var hand=OwnHand(g);
        var colors=new[]{Color.Red,Color.Black}.Where(c=>hand.Count(card=>CardColor(card)==c)>=2).ToList();
        Require(colors.Count>0,"The fixture deals the owner two same-color cards.");
        var pairColor=colors[0];
        var pair=hand.Where(card=>CardColor(card)==pairColor).Take(2).Select(card=>card.Id).ToArray();
        var targetDodges=TargetHand(g).Where(card=>card.Kind==CardKind.Dodge).ToList();
        var answerable=targetDodges.Any(card=>CardColor(card)==pairColor);
        var hpBefore=g.CreateSnapshot(1).Players[1].Hp;
        Accept(g,new UseProgramSkillCommand(0,Skill,"two-hand-cards-as-slash",pair,[1],g.Revision,P(g)!.PromptId));
        Settle(g);
        if(answerable)
        {
            Require(g.CreateSnapshot(1).Players[1].Hp==hpBefore,
                "A same-color Dodge still answers the converted slash.");
            var spent=SpentCards(g,1,targetDodges);
            Require(spent.Count==1&&CardColor(spent[0])==pairColor,
                "The answering Dodge shares the converted pair's color.");
        }
        else
        {
            Require(g.CreateSnapshot(1).Players[1].Hp==hpBefore-1,
                "Without a same-color Dodge the converted slash lands despite other Dodges.");
        }
        if(g.CreateSnapshot(1).Players[1].Hp<hpBefore)
            Require(g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Wusheng)&&
                g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Paoxiao),
                "The damaging converted slash grants both turn skills.");
        else
            Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Wusheng),
                "An answered converted slash grants nothing.");
        Replay(g,r);Settle(g);
    }

    public static void NaturalSlashStaysAnswerableByAnyColor()
    {
        var(g,r)=Start();
        var slash=OwnHand(g).First(card=>card.Kind==CardKind.Slash);
        var targetDodges=TargetHand(g).Where(card=>card.Kind==CardKind.Dodge).ToList();
        var hpBefore=g.CreateSnapshot(1).Players[1].Hp;
        Accept(g,new PlayCardCommand(0,slash.Id,[1],g.Revision,P(g)!.PromptId));
        Settle(g);
        if(targetDodges.Count>0)
        {
            Require(g.CreateSnapshot(1).Players[1].Hp==hpBefore,
                "A natural slash stays answerable regardless of colors.");
            Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Wusheng),
                "An answered natural slash grants nothing.");
        }
        else
        {
            Require(g.CreateSnapshot(1).Players[1].Hp==hpBefore-1,
                "Without any Dodge the natural slash lands and grants the turn skills.");
            Require(g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Wusheng)&&
                g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Paoxiao),
                "The damaging natural slash grants both turn skills.");
        }
        Replay(g,r);Settle(g);
    }

    public static void TurnSkillsExpireWithTheTurn()
    {
        var(g,r)=Start();
        var hand=OwnHand(g);
        var colors=new[]{Color.Red,Color.Black}.Where(c=>hand.Count(card=>CardColor(card)==c)>=2).ToList();
        Require(colors.Count>0,"The fixture deals the owner two same-color cards.");
        var targetDodges=TargetHand(g).Where(card=>card.Kind==CardKind.Dodge).ToList();
        // Prefer a color the target cannot answer so the damage grant is exercised.
        var pairColor=colors.FirstOrDefault(c=>!targetDodges.Any(card=>CardColor(card)==c),colors[0]);
        var pair=hand.Where(card=>CardColor(card)==pairColor).Take(2).Select(card=>card.Id).ToArray();
        var answerable=targetDodges.Any(card=>CardColor(card)==pairColor);
        var hpBefore=g.CreateSnapshot(1).Players[1].Hp;
        Accept(g,new UseProgramSkillCommand(0,Skill,"two-hand-cards-as-slash",pair,[1],g.Revision,P(g)!.PromptId));
        Settle(g);
        if(!answerable)
        {
            Require(g.CreateSnapshot(1).Players[1].Hp==hpBefore-1,
                "The unanswerable converted slash deals its damage.");
            Require(g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Wusheng)&&
                g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Paoxiao),
                "The damaging converted slash grants both turn skills.");
        }
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"dump",[],[],g.Revision,P(g)!.PromptId));
        Settle(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        var before=g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Count();
        for(var i=0;i<200;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Count()>before)break;
            if(P(g) is {PlayerSeat:0,Kind:DecisionKind.PlayCard}){Accept(g,new EndPlayPhaseCommand(0,g.Revision));continue;}
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        Require(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Count()>before,
            "The owner's turn finishes after the dump and ending.");
        Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Wusheng)&&
            !g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Paoxiao),
            "The granted skills leave with the owner's turn.");
        Replay(g,r);
    }

    private enum Color { Red, Black }

    private static Color CardColor(CardSnapshot card)=>card.Suit is Suit.Heart or Suit.Diamond?Color.Red:Color.Black;

    private static IReadOnlyList<CardSnapshot> OwnHand(GameEngine g)=>g.CreateSnapshot(0,true).Players[0].Hand!;

    private static IReadOnlyList<CardSnapshot> TargetHand(GameEngine g)=>g.CreateSnapshot(1,true).Players[1].Hand!;

    private static IReadOnlyList<CardSnapshot> SpentCards(GameEngine g,int seat,IEnumerable<CardSnapshot> before)=>before
        .Where(card=>g.CreateSnapshot(seat,true).Players[seat].Hand!.All(now=>now.Id!=card.Id))
        .ToList();


    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Require(result.Accepted,result.Error?.Message??"Command rejected.");}

    private const string DriverSkill="fixture:gxb-driver";

    private static (GameEngine,ContentRegistry) Start()
    {
        ContentRegistry r;
        try
        {
            r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture());
        }
        catch(Exception diagnostic)
        {
            for(var cause=(Exception?)diagnostic;cause is not null;cause=cause.InnerException)
                Console.WriteLine("DIAG-CHAIN: "+cause.GetType().Name+": "+cause.Message);
            throw;
        }
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:gxb",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(g,new SelectGeneralCommand(0,"fixture:gxb",g.Revision,P(g)!.PromptId));
        Settle(g);return(g,r);
    }

    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-gxb", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddGeneral(new("fixture:gxb","界关兴张苞","boundary_guan_xing_zhang_bao","boundary:fuhun-current","shu",4,
                [DriverSkill]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:gxb-{i}","其他"+i,"supporter","standard:none","wei",5,null));
            b.AddDeck(new("fixture:gxb-deck","固定",4,1,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard(
                i%2==0?"standard:slash":"standard:dodge",
                i%4==1?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddSkill(new(DriverSkill,"驱动","测试"){Program=Load(DriverSkill,
                "\"activations\":["+Activation("dump","[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]")+"]")});
            b.AddMode(new("identity:gxb","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},
                "fixture:gxb-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:gxb","fixture:gxb-1","fixture:gxb-2","fixture:gxb-3"]));
        }
    }

    private static string Activation(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";

    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
}
