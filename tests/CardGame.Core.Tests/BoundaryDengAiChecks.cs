using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Deng Ai: docs/content/sources/boundary-deng-ai-2026-10-03.json.
internal static class BoundaryDengAiChecks
{
    private const string Skill="boundary:tuntian-current";
    private const string Awakening="boundary:zaoxian-current";
    private const string Snatch="boundary:jixi-current";

    public static void Definitions()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        var general=registry.Generals["boundary:deng-ai"];
        Require(general.Name=="界邓艾"&&general.FactionId=="wei"&&general.BaseHp==4&&
            general.SkillIds.SequenceEqual(["boundary:tuntian-current","boundary:zaoxian-current"])&&
            general.VariantId=="boundary"&&general.RulesetId=="sanguosha-ol",
            "The boundary Deng Ai general registers the current OL wei 4HP pair.");
        var tuntian=registry.GetSkill(Skill).Program!;
        Require(tuntian.Triggers.Count==4&&tuntian.Modifiers.Single().ValueExpression==SkillRuleValueExpression.NegatedOwnedZoneCount,
            "Tuntian keeps three outside-turn loss triggers plus the new own-turn slash discard and the authority distance modifier.");
        var discard=tuntian.Triggers.Single(t=>t.Id=="judge-on-own-turn-slash-discard");
        Require(discard.Window==SkillProgramTriggerWindow.DiscardPileReceived&&discard.MovementDiscardOnly&&
            discard.DiscardOwnerScope==SkillProgramDiscardOwnerScope.Own&&discard.MovementOccurrence is null&&
            discard.CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash])&&discard.Optional,
            "The own-turn discard branch requires own slash-family discards received by the discard pile, one offer per card.");
        var zaoxian=registry.GetSkill(Awakening).Program!;
        Require(zaoxian.Triggers.Single().Effects.Select(e=>e.Op).SequenceEqual(
            [SkillProgramEffectOp.ChangeMaximumHp,SkillProgramEffectOp.GrantSkills,SkillProgramEffectOp.PendExtraTurn]),
            "The awakening reduces maximum HP, grants Jixi and pends one extra turn in order.");
        var presentation=registry.GetSkill(Skill).ProgramPresentation!;
        Require(presentation.Name=="屯田"&&presentation.Description.Contains("回合内弃置【杀】",StringComparison.Ordinal),
            "The presentation carries the current OL Tuntian wording.");
    }

    public static void OwnTurnSlashDiscardJudgesIntoAuthority()
    {
        var(g,r)=Start();
        Driver(g,"discard");Answer(g,c=>c.Cards.Count==1);Activate(g);
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==1,"A discarded slash judges into one Tian card.");
        Require(g.GetCombatDistance(0,2)==1,"One Tian card shortens the public combat distance to seat two.");
        Replay(g,r);
        Driver(g,"discard");Answer(g,c=>c.Cards.Count==1);Skip(g);
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==1,"Skipping the optional offer keeps the Tian count frozen.");
        Driver(g,"discard");Answer(g,c=>c.Cards.Count==1);Activate(g);
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==2,"Each activated slash discard adds one Tian card.");
        Require(g.GetCombatDistance(0,2)==1,"Two Tian cards hold the seat-two distance at the one-yard floor.");
        Replay(g,r);
        var(g2,r2)=Start(scenario:"heart");
        Driver(g2,"discard");Answer(g2,c=>c.Cards.Count==1);Activate(g2);
        Require(g2.CreateSnapshot(0).Players[0].AuthorityCount==0,"A heart judgment stays out of the authority zone.");
        var judgmentCard=g2.CardMovements.Last(m=>m.To==CardLocation.Judgment(0)).CardId;
        Require(g2.CardMovements.Any(m=>m.To==CardLocation.DiscardPile&&m.CardId==judgmentCard),
            "The heart judgment card completes into the discard pile.");
        Replay(g2,r2);
    }

    public static void NonSlashDiscardAndActualUseStaySilent()
    {
        var(g,r)=Start(scenario:"peach");
        Driver(g,"discard");Answer(g,c=>c.Cards.Count==1);Settle(g);
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==0,"A discarded basic non-slash never offers Tuntian.");
        Replay(g,r);
        var(g2,r2)=Start();
        var slash=g2.State.Players[0].Hand.First(c=>c.Kind==CardKind.Slash).Id;
        Play(g2,slash,[1]);
        Finish(g2);
        Require(g2.CreateSnapshot(0).Players[0].AuthorityCount==0,"The used slash's cleanup stays outside the own-discard boundary.");
        Replay(g2,r2);
    }

    public static void OutsideTurnHandLossStillJudges()
    {
        var(g,r)=Start(offturn:true);
        AdvanceUntilOtherTurn(g);
        Require(P(g)!.PlayerSeat==0,"The forced off-turn discard reaches its owner across the seat boundary.");
        Answer(g,c=>c.Cards.Count==1);
        Activate(g);
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==1,"Losing a hand card outside the turn judges into one Tian card.");
        Replay(g,r);
    }

    public static void AwakeningGrantsSnatchAndExtraTurn()
    {
        var(g,r)=Start();
        for(var i=0;i<3;i++){Driver(g,"discard");Answer(g,c=>c.Cards.Count==1);Activate(g);}
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==3,"Three slash discards stage three Tian cards.");
        while(!g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber>1)) AdvanceQuiet(g);
        Settle(g);
        var view=g.CreateSnapshot(0).Players[0];
        Require(view.AuthorityCount==3&&view.MaxHp==4,"The awakening keeps three Tian cards and reduces the lord maximum HP to four.");
        Require(g.CreateSnapshot(0).Players[0].Skills!.Any(s=>s.ContentId==Snatch),"The awakening grants the snatch conversion skill.");
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramExtraTurnPendedEvent>().Any(e=>e.Seat==0&&e.SkillId==Awakening),
            "The awakening pends one extra turn for the owner.");
        Replay(g,r);
        var startsAtAwakening=g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Count();
        Finish(g);
        var starts=g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().ToList();
        Require(starts.Count==startsAtAwakening+1&&starts[^1].ActorSeat==0,
            "The pended extra turn starts immediately after the awakening turn without another seat in between.");
        Require(g.CreateSnapshot(0).Players[0].MaxHp==4,"The once-per-game awakening does not repeat on the extra turn.");
        Settle(g);
        var snatch=g.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Snatch&&a.TargetSeats.Contains(1));
        if(snatch is not {CardId: { } snatchCard})
            throw new InvalidOperationException("Jixi publishes each authority card as a real snatch action against a hand-bearing target.");
        Require(g.CreateSnapshot(0).Players[0].AuthorityCards!.Any(c=>c.Id==snatchCard),
            "The snatch action uses the exact staged authority card.");
        Play(g,snatchCard,[1],snatch);
        Require(g.CreateSnapshot(0).Players[0].AuthorityCount==2,"The spent Tian card leaves the authority zone with the snatch.");
        Replay(g,r);
    }

    private static void AdvanceUntilOtherTurn(GameEngine g)
    {
        for(var i=0;i<300;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat!=0))return;
            if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}){Accept(g,new EndPlayPhaseCommand(0,g.Revision));continue;}
            if(P(g) is {Kind:DecisionKind.ProgramTrigger} p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException("The fixture never reached another seat's turn.");
    }

    private static void AdvanceQuiet(GameEngine g)
    {
        for(var i=0;i<300;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber>1))return;
            if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}){Accept(g,new EndPlayPhaseCommand(0,g.Revision));continue;}
            if(P(g) is {Kind:DecisionKind.ProgramTrigger} p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException("The fixture never reached the owner's next turn.");
    }

    private static void Finish(GameEngine g)
    {
        var before=g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Count();
        for(var i=0;i<300;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Count()>before)return;
            if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}){Accept(g,new EndPlayPhaseCommand(0,g.Revision));continue;}
            if(P(g) is {Kind:DecisionKind.ProgramTrigger} p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException("The human turn never finished.");
    }

    internal static (GameEngine,ContentRegistry) Start(string scenario="",int maxTurns=8,bool offturn=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario,offturn));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:da",UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,MaxTurns=maxTurns},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,"fixture:da",g.Revision,P(g)!.PromptId));
        Settle(g);return(g,r);
    }

    private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(command);Require(result.Accepted,result.Error?.Message??"Command rejected.");}
    private static void Driver(GameEngine g,string id,int[]? targets=null)
    {
        if(P(g) is null) Settle(g);
        var p=P(g);
        if(p is null){foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);throw new InvalidOperationException("No pending decision for the fixture driver.");}
        Accept(g,new UseProgramSkillCommand(0,"fixture:da-driver",id,[],targets??[],g.Revision,p.PromptId));
    }
    private static void Play(GameEngine g,int card,int[]targets,LegalAction? a=null)=>Accept(g,new PlayCardCommand(0,card,targets,g.Revision,P(g)!.PromptId,a?.PlayedCardKind){ConversionSource=a?.ConversionSource,AdditionalConversionSources=a?.AdditionalConversionSources});
    private static SkillProgram Load(string id,string members)=>SkillProgramCatalog.Load("{\"schemaVersion\":"+SkillProgramCatalog.RulesSchemaVersion+",\"skills\":[{\"id\":\""+id+"\",\"revision\":1,"+members+"}]}","{\"schemaVersion\":3,\"skills\":{\""+id+"\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private static string Activation(string id,string effects,int targets=0)=>"{\"id\":\""+id+"\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\"anyLiving\",\"effects\":"+effects+"}";

    private sealed class Fixture(string scenario,bool offturn):IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-da", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try{typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryDengAiContent")!.GetMethod("Register",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[b]);}catch(Exception diag){Console.WriteLine("DIAG-INNER: "+(diag.InnerException?.ToString()??diag.ToString()));throw;}
            b.AddSkill(new("fixture:da-driver","fixture","fixture"){Program=Load("fixture:da-driver","\"activations\":["+Activation("discard","[{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"amount\":1,\"resultBind\":\"removed\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"removed\",\"destination\":\"discardPile\"}]")+"]")});
            b.AddSkill(new("fixture:da-offturn","offturn","offturn"){Program=Load("fixture:da-offturn","\"triggers\":[{\"id\":\"offturn\",\"window\":\"judgmentPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":false,\"effects\":[{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"amount\":1,\"resultBind\":\"removed\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"removed\",\"destination\":\"discardPile\"}]}]")});
            b.AddGeneral(new("fixture:da","邓艾","boundary_deng_ai","boundary:tuntian-current","wei",4,
                offturn?new[]{"boundary:zaoxian-current","fixture:da-driver","fixture:da-offturn"}:new[]{"boundary:zaoxian-current","fixture:da-driver"}));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:da-{i}","其他"+i,"supporter","standard:none","wei",5,null));
            b.AddDeck(new("fixture:da-deck","固定",4,1,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(
                scenario=="peach"?"standard:peach":"standard:slash",
                scenario=="heart"?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:da","固定",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:da-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:da","fixture:da-1","fixture:da-2","fixture:da-3"]));
        }
    }
}
