using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Meng Huo: docs/content/sources/boundary-meng-huo-2026-10-04.json.
internal static class BoundaryMengHuoChecks
{
    private const string Huoshou="boundary:huoshou-current";
    private const string Zaiqi="boundary:zaiqi-current";
    private const string DriverSkill="fixture:mh-driver";

    public static void Definitions()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        var general=registry.Generals["boundary:meng-huo"];
        Require(general.Name=="界孟获"&&general.FactionId=="shu"&&general.BaseHp==4&&
            general.SkillIds.SequenceEqual([Huoshou,Zaiqi])&&
            general.VariantId=="boundary"&&general.RulesetId=="sanguosha-ol",
            "The boundary Meng Huo general registers the current OL shu 4HP pair.");
        var huoshou=registry.GetSkill(Huoshou).Program!;
        Require(huoshou.CardPolicies.Select(p=>p.Kind).SequenceEqual(
                [SkillProgramCardPolicyKind.ExcludeGlobalTarget,SkillProgramCardPolicyKind.AttributeGlobalDamage])&&
            huoshou.CardPolicies.All(p=>p.CardKinds.SequenceEqual([CardKind.BarbarianAssault])),
            "Huo Shou keeps the shared barbarian target immunity and damage attribution policies.");
        var trigger=registry.GetSkill(Zaiqi).Program!.Triggers.Single();
        Require(trigger.Window==SkillProgramTriggerWindow.TurnEnding&&trigger.Subject==SkillProgramTriggerSubject.Owner&&
            !trigger.Optional&&trigger.UsageScope==SkillUsageScope.Turn&&trigger.UsageLimit==1&&
            trigger.Effects.Select(e=>e.Op).SequenceEqual([SkillProgramEffectOp.OfferRedDiscardRecoveryChoice]),
            "Zai Qi asks once at the owner's own ending through the red discard recovery operation.");
        Require(registry.GetSkill(Huoshou).ProgramPresentation!.Name=="祸首"&&
            registry.GetSkill(Zaiqi).ProgramPresentation!.Name=="再起",
            "The presentation carries the current OL skill names.");
    }

    public static void HuoshouImmuneAndAttributesBarbarianDamage()
    {
        var(g,r)=Start("barbarian");
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        ReachBarbarianResolution(g);
        Require(g.CreateSnapshot(0).Players[0].Hp==5,
            "The barbarian assault never damages the Huo Shou owner.");
        var damages=g.Events.Select(e=>e.Payload).OfType<DamageAppliedEvent>()
            .Where(d=>d.TargetSeat!=0).ToList();
        Require(damages.Count>=1&&damages.All(d=>d.SourceSeat==0),
            "Barbarian assault damage to the other characters is attributed to the Huo Shou owner.");
        Require(!g.Events.Select(e=>e.Payload).OfType<DamageAppliedEvent>().Any(d=>d.TargetSeat==0),
            "The Huo Shou owner is not a barbarian assault target at all.");
        Replay(g,r);
    }

    public static void ZaiqiAsksChosenParticipantsInOrder()
    {
        var(g,r)=Start("red");
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Wound(g);Wound(g);
        Burn(g);Burn(g);Burn(g);
        Require(g.CreateSnapshot(0).Players[0].Hp==3&&g.CreateSnapshot(0).Players[0].Hand!.Count==2,
            "Two wounds and three red discards set up an X of exactly three.");
        Replay(g,r);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        Reach(g,p=>p.Kind==DecisionKind.ProgramTrigger&&p.PlayerSeat==0&&
            p.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="seat:1"));
        var opening=P(g)!;
        Require(opening.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="skip")&&
            opening.Choices.Count(c=>c.Parameters.GetValueOrDefault("mode")!.StartsWith("seat:"))==3,
            "The ending prompt offers all three other characters and a decline.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="seat:1");
        var narrowed=P(g)!;
        Require(!narrowed.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="seat:1")&&
            narrowed.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="finish"),
            "An already chosen character cannot be picked twice and the selection can be confirmed.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="seat:2");
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="seat:3");
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="finish");
        Reach(g,p=>g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryChosenEvent>().Count()==3);
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryCommittedEvent>().Single() is
            {OwnerSeat:0,Budget:3,Seats.Count:3} committed&&committed.Seats.SequenceEqual([1,2,3]),
            "The committed selection names exactly the three chosen characters.");
        var chosen=g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryChosenEvent>()
            .Select(e=>(e.ChooserSeat,e.Option)).ToArray();
        Require(chosen.SequenceEqual([(1,"recovery"),(2,"draw"),(3,"draw")]),
            "The badly wounded owner is healed first, then the remaining participants draw.");
        Require(g.Events.Select(e=>e.Payload).OfType<RecoveryAppliedEvent>()
                .Single(e=>e.TargetSeat==0) is {SourceSeat:0,Amount:1},
            "The healing participant recovers the owner by exactly one.");
        ReplayFourViews(g,r);Replay(g,r);
    }

    public static void ZaiqiDrawOnlyAtFullOwner()
    {
        var(g,r)=Start("red");
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Burn(g);Burn(g);Burn(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        Reach(g,p=>p.Kind==DecisionKind.ProgramTrigger&&p.PlayerSeat==0&&
            p.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="seat:1"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="seat:1");
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="finish");
        Reach(g,p=>g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryChosenEvent>().Count()==1);
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryChosenEvent>().Single() is
            {ChooserSeat:1,Option:"draw"},
            "A full-HP owner offers no recovery branch, so the participant draws.");
        Require(!g.Events.Select(e=>e.Payload).OfType<RecoveryAppliedEvent>().Any(e=>e.TargetSeat==0)&&
            g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryCommittedEvent>().Single() is
                {OwnerSeat:0,Budget:3,Seats.Count:1},
            "The draw-only resolution never touches the owner's HP.");
        Replay(g,r);
    }

    public static void ZaiqiDeclineLeavesEveryoneAlone()
    {
        var(g,r)=Start("red");
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Burn(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        Reach(g,p=>p.Kind==DecisionKind.ProgramTrigger&&p.PlayerSeat==0&&
            p.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="seat:1"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="skip");
        Require(!g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryCommittedEvent>().Any()&&
            !g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryChosenEvent>().Any(),
            "A declined Zai Qi records no commitment and no participant choice.");
        Require(g.CreateSnapshot(0).Players[0].Hp==5&&g.CreateSnapshot(0).Players[0].Hand!.Count==4,
            "A declined Zai Qi changes no HP or hand counts.");
        Replay(g,r);
    }

    public static void ZaiqiStaysSilentWithoutRedDiscards()
    {
        var(g,r)=Start("black");
        Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
        Burn(g);Burn(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        Reach(g,p=>g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat!=0));
        Require(!g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryCommittedEvent>().Any()&&
            !g.Events.Select(e=>e.Payload).OfType<ProgramRedDiscardRecoveryChosenEvent>().Any()&&
            !g.Events.Select(e=>e.Payload).OfType<RecoveryAppliedEvent>().Any(e=>e.TargetSeat==0),
            "Black-only discards keep Zai Qi silent through the ending.");
    }

    private static void ReachBarbarianResolution(GameEngine g)
    {
        for(var i=0;i<400;i++)
        {
            if(g.Events.Select(e=>e.Payload).Any(payload=>payload switch
            {
                CardUsedEvent used=>used.CardKind==CardKind.BarbarianAssault&&used.SourceSeat!=0,
                GroupCardUsedEvent grouped=>grouped.CardKind==CardKind.BarbarianAssault&&grouped.SourceSeat!=0,
                _=>false
            }))return;
            if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0})Accept(g,new EndPlayPhaseCommand(0,g.Revision));
            else if(P(g) is {Kind:DecisionKind.DiscardCards,PlayerSeat:0} discard)
                Accept(g,new DiscardCardsCommand(0,discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),discard.PromptId,g.Revision));
            else if(P(g) is {Kind:DecisionKind.ProgramTrigger,PlayerSeat:0} zaiqi&&
                zaiqi.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="skip"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("mode")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException("The fixture never resolved an opponent barbarian assault.");
    }

    private static void Wound(GameEngine g)
    {
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"wound",[],[],g.Revision,P(g)!.PromptId));
        Settle(g);
    }
    private static void Burn(GameEngine g)
    {
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"burn",
            [g.CreateSnapshot(0).Players[0].Hand!.Select(c=>c.Id).First()],[],g.Revision,P(g)!.PromptId));
        Settle(g);
    }

    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Require(result.Accepted,result.Error?.Message??"Command rejected.");}

    private static (GameEngine,ContentRegistry) Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(scenario));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=37,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,
            ModeId="identity:mh",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);
        Accept(g,new SelectGeneralCommand(0,"fixture:mh",g.Revision,P(g)!.PromptId));
        Settle(g);return(g,r);
    }

    private static string Activation(string id,string effects,int cards)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":"+cards+",\"maxCards\":"+cards+",\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";

    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];

    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mh", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(new("fixture:mh-driver","驱动","测试"){Program=Load("fixture:mh-driver",
                "\"activations\":["+Activation("wound","[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]",0)+","+
                Activation("burn","[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}]",1)+"]")});
            b.AddGeneral(new("fixture:mh","界孟获","boundary_meng_huo","boundary:huoshou-current","shu",4,
                ["boundary:zaiqi-current","fixture:mh-driver"]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:mh-{i}","其他"+i,"supporter","standard:none","wei",5,null));
            b.AddDeck(new("fixture:mh-deck","固定",4,1,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(
                scenario=="barbarian"?"standard:barbarian_assault":"standard:slash",
                scenario=="black"?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:mh","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},
                "fixture:mh-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:mh","fixture:mh-1","fixture:mh-2","fixture:mh-3"]));
        }
    }
}
