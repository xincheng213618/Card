using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Sun Ce: docs/content/sources/boundary-sun-ce-2026-10-04.json.
internal static class BoundarySunCeChecks
{
    private const string Jiang="boundary:jiang-current";
    private const string Hunzi="boundary:hunzi-current";
    private const string Zhiba="boundary:zhiba-current";
    private const string Yingzi="classic:yingzi";
    private const string Yinghun="classic:yinghun";
    private const string DriverSkill="fixture:sc-driver";

    public static void Definitions()
    {
        ContentRegistry registry;
        try
        {
            registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        }
        catch(Exception diagnostic)
        {
            for(var cause=(Exception?)diagnostic;cause is not null;cause=cause.InnerException)
                Console.WriteLine("DIAG-CHAIN: "+cause.GetType().Name+": "+cause.Message+"\n"+cause.StackTrace);
            throw;
        }
        var general=registry.Generals["boundary:sun-ce"];
        Require(general.Name=="界孙策"&&general.FactionId=="wu"&&general.BaseHp==4&&
            general.SkillIds.SequenceEqual([Jiang,Hunzi,Zhiba])&&
            general.VariantId=="boundary"&&general.RulesetId=="sanguosha-ol"&&general.CharacterId=="character:sun-ce",
            "The boundary Sun Ce general registers the current OL wu 4HP triple.");
        var jiang=registry.GetSkill(Jiang).Program!;
        Require(jiang.Triggers.Select(t=>t.Window).SequenceEqual(
                Enumerable.Repeat(SkillProgramTriggerWindow.CardUseBeforeTargetEffects,4)
                    .Concat(Enumerable.Repeat(SkillProgramTriggerWindow.DiscardPileReceived,2)))&&
            jiang.Triggers.Take(4).All(t=>t.Optional)&&
            jiang.Triggers[0].CardKinds.SequenceEqual([CardKind.Duel])&&
            jiang.Triggers[1].CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash])&&
            jiang.Triggers[2].CardKinds.SequenceEqual([CardKind.Duel])&&
            jiang.Triggers[3].CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash])&&
            jiang.Triggers[0].OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&
            jiang.Triggers[1].OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&
            jiang.Triggers[2].OwnerRelation==SkillProgramCardActionOwnerRelation.Target&&
            jiang.Triggers[3].OwnerRelation==SkillProgramCardActionOwnerRelation.Target&&
            jiang.Triggers[1].Condition.Kind==SkillProgramTriggerConditionKind.CardActionCardIsRed&&
            jiang.Triggers[3].Condition.Kind==SkillProgramTriggerConditionKind.CardActionCardIsRed,
            "Jiang draws on using or being targeted by a duel or a red slash through four window triggers.");
        var reclaims=jiang.Triggers.Skip(4).ToArray();
        Require(reclaims.All(t=>t.UsageScope==SkillUsageScope.Turn&&t.UsageLimit==1&&
                t.NamedUsageGroup=="jiang-reclaim"&&t.Optional&&t.ExcludedMovementReasons.Count==11)&&
            reclaims[0].CardKinds.SequenceEqual([CardKind.Duel])&&
            reclaims[1].CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash])&&
            reclaims[1].Suits.SequenceEqual([Suit.Heart,Suit.Diamond])&&
            reclaims.All(t=>t.Effects.Select(e=>e.Op).SequenceEqual([SkillProgramEffectOp.LoseHp,SkillProgramEffectOp.ClaimMovedCards])),
            "The reclaim windows cover discarded duels and red slashes under one once-per-turn group, paying one HP.");
        var hunzi=registry.GetSkill(Hunzi);
        Require(hunzi.Tags.HasFlag(SkillTag.Awakening),"Hunzi carries the awakening tag.");
        var hunziProgram=hunzi.Program!;
        Require(hunziProgram.Triggers.Select(t=>t.Window).SequenceEqual([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.TurnEnding])&&
            hunziProgram.Triggers.All(t=>t.UsageScope==SkillUsageScope.Game&&t.UsageLimit==1&&!t.Optional)&&
            hunziProgram.BooleanStates.Single().Id=="woke"&&
            hunziProgram.Triggers[1].Condition.Kind==SkillProgramTriggerConditionKind.BooleanState,
            "Hunzi wakes once per game at one HP and gates the same-turn end effect on a boolean state.");
        var wake=hunziProgram.Triggers[0].Effects;
        Require(wake.Select(e=>e.Op).SequenceEqual([SkillProgramEffectOp.ChangeMaximumHp,SkillProgramEffectOp.GrantSkills,SkillProgramEffectOp.SetBooleanState])&&
            wake.Single(e=>e.Op==SkillProgramEffectOp.GrantSkills).SkillIds.SequenceEqual([Yingzi,Yinghun]),
            "The awakening lowers maximum HP by one, grants Yingzi and Yinghun and marks the turn state.");
        var end=hunziProgram.Triggers[1].Effects;
        Require(end.Select(e=>e.Op).SequenceEqual([SkillProgramEffectOp.ChooseOption,SkillProgramEffectOp.Draw,SkillProgramEffectOp.Recover,SkillProgramEffectOp.SetBooleanState]),
            "The awakened turn end offers draw two or recover one.");
        var zhiba=registry.GetSkill(Zhiba);
        Require(zhiba.Tags.HasFlag(SkillTag.Lord),"Zhiba carries the lord tag.");
        var challenge=zhiba.Program!.Activations.Single();
        Require(challenge.TargetKind==SkillProgramTargetKind.OtherLivingWuFactionWithHand&&
            challenge.UsesPerPhase==1&&challenge.MinCards==1&&challenge.MaxCards==1&&
            challenge.Effects.Select(e=>e.Op).SequenceEqual([SkillProgramEffectOp.Pindian])&&
            zhiba.Program!.CardPolicies.Single().Kind==SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins,
            "Zhiba challenges one Wu character with hand cards once per play phase and claims both contest cards.");
        Require(registry.GetSkill(Jiang).ProgramPresentation!.Name=="激昂"&&
            registry.GetSkill(Hunzi).ProgramPresentation!.Name=="魂姿"&&
            registry.GetSkill(Zhiba).ProgramPresentation!.Name=="制霸",
            "The presentation carries the current OL skill names.");
    }

    public static void JiangDrawsOnOwnDuelAndRedSlash()
    {
        var(g,r)=Start(scenario:"duel");
        var before=g.CreateSnapshot(0).Players[0].Hand!.Count;
        var duel=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel);
        Accept(g,new PlayCardCommand(0,duel.CardId!.Value,[1],g.Revision,P(g)!.PromptId));
        Activate(g);
        Settle(g);

        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==before&&
            g.CreateSnapshot(1).Players[1].Hp==g.CreateSnapshot(1).Players[1].MaxHp-1,
            "Using a duel draws one replacement card and the unanswered duel damages the Wu neighbour.");
        Replay(g,r);
        var(e,r2)=Start(scenario:"redslash");
        var slashBefore=e.CreateSnapshot(0).Players[0].Hand!.Count;
        var slash=e.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash);
        Accept(e,new PlayCardCommand(0,slash.CardId!.Value,[1],e.Revision,P(e)!.PromptId));
        Activate(e);
        Settle(e);
        Require(e.CreateSnapshot(0).Players[0].Hand!.Count==slashBefore&&
            e.CreateSnapshot(1).Players[1].Hp==e.CreateSnapshot(1).Players[1].MaxHp-1,
            "Using a red slash draws one replacement card and the unanswered slash damages the neighbour.");
        Replay(e,r2);
    }

    public static void JiangStaysSilentOnBlackSlash()
    {
        var(g,r)=Start(scenario:"blackslash");
        var before=g.CreateSnapshot(0).Players[0].Hand!.Count;
        var slash=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash);
        Accept(g,new PlayCardCommand(0,slash.CardId!.Value,[1],g.Revision,P(g)!.PromptId));
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==before-1,
            "A black slash draws nothing: the red condition gates the actor branch.");
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramSkillResolvedEvent>().All(e=>e.SkillId!=Jiang),
            "No Jiang program resolved during the black slash.");
        Replay(g,r);
    }

    public static void JiangDrawsWhenTargetedByRedSlash()
    {
        var(g,r)=Start(scenario:"redslash");
        var before=g.CreateSnapshot(0).Players[0].Hand!.Count;
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"provoke",[],[1],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")==Jiang));
        Activate(g);
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==before+1,
            "Becoming the target of a requested red slash draws one through the target branch.");
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].Hp==g.CreateSnapshot(0).Players[0].MaxHp-1,
            "The owner holds no dodge, so the requested red slash still deals its damage.");
        Replay(g,r);
    }

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

    public static void HunziWakesDrawsThenStaysSilent()
    {
        var(g,r)=Start(scenario:"peach",maxTurns:16);
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"hurt",[],[],g.Revision,P(g)!.PromptId));
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].MaxHp==5&&g.CreateSnapshot(0).Players[0].Hp==1,
            "The lord fixture sits at five maximum HP and the driver leaves exactly one HP.");
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"dump",[],[],g.Revision,P(g)!.PromptId));
        Settle(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        ReachOwnerTurn(g,turnNumber:2);
        SettleAuxiliarySkips(g);
        var view=g.CreateSnapshot(0).Players[0];
        Require(view.MaxHp==4&&view.Hp==1&&Options(g)==0&&
            view.Skills!.Any(s=>s.ContentId==Yingzi)&&view.Skills!.Any(s=>s.ContentId==Yinghun),
            "The prepare-phase awakening lowers maximum HP, grants Yingzi and Yinghun and defers the choice to the turn end.");
        Accept(g,new UseProgramSkillCommand(0,DriverSkill,"dump",[],[],g.Revision,P(g)!.PromptId));
        SettleAuxiliarySkips(g);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        ReachHunziChoice(g,"recover");
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="recover");
        Require(g.CreateSnapshot(0).Players[0].Hp==2&&Options(g)==1,
            "The awakened turn end recovers one HP as the turn's single recorded choice.");
        ReplayFourViews(g,r);
        ReachOwnerTurn(g,turnNumber:3);
        Require(g.CreateSnapshot(0).Players[0].MaxHp==4&&Options(g)==1,
            "The spent once-per-game group keeps the awakening silent at later prepares.");
        Replay(g,r);
    }

    public static void ZhibaTargetsWuOnlyAndClaimsBothOnTie()
    {
        var(g,r)=Start(scenario:"crossbow");
        var stake=g.CreateSnapshot(0).Players[0].Hand![0].Id;
        var wuSeat=FactionSeat(g,r,"wu");
        var otherSeat=Enumerable.Range(1,3).First(seat=>seat!=wuSeat);
        var rejected=g.Submit(new UseProgramSkillCommand(0,Zhiba,"challenge-wu",[stake],[otherSeat],g.Revision,P(g)!.PromptId));
        Require(!rejected.Accepted,
            "A non-Wu neighbour is not a legal Zhiba target: the Wu filter gates selection.");
        Require(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.UseProgramSkill&&
                a.ProgramSkillId==Zhiba&&a.ProgramActivationId=="challenge-wu"),
            "Zhiba is offered during the lord's play phase, and the non-Wu seat is not.");
        var before=g.CreateSnapshot(0).Players[0].Hand!.Count;
        var targetBefore=g.CreateSnapshot(wuSeat).Players[wuSeat].Hand!.Count;
        Accept(g,new UseProgramSkillCommand(0,Zhiba,"challenge-wu",[stake],[wuSeat],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="pindian-claim"));
        var result=g.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Single().Result;
        Require(result.SourceSeat==0&&result.OpponentSeat==wuSeat&&!result.SourceWon,
            "The identical fixture deck ties the pindian, so the Wu challenger did not win.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("take")=="true");
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==before+1&&
            g.CreateSnapshot(wuSeat).Players[wuSeat].Hand!.Count==targetBefore-1,
            "Claiming both contest cards nets the owner one card and costs the challenger theirs.");
        Require(g.ResolutionStack.Count==0&&
            g.Events.Select(e=>e.Payload).OfType<ProgramSkillResolvedEvent>().Any(e=>e.SkillId==Zhiba&&e.Completed),
            "The pindian child completes and resolves the Zhiba activation.");
        Replay(g,r);
    }

    public static void ZhibaDeclinedClaimDiscardsBoth()
    {
        var(g,r)=Start(scenario:"crossbow");
        var wuSeat=FactionSeat(g,r,"wu");
        var stake=g.CreateSnapshot(0).Players[0].Hand![0].Id;
        var before=g.CreateSnapshot(0).Players[0].Hand!.Count;
        var targetBefore=g.CreateSnapshot(wuSeat).Players[wuSeat].Hand!.Count;
        Accept(g,new UseProgramSkillCommand(0,Zhiba,"challenge-wu",[stake],[wuSeat],g.Revision,P(g)!.PromptId));
        Reach(g,p=>p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="pindian-claim"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("take")=="false");
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==before-1&&
            g.CreateSnapshot(wuSeat).Players[wuSeat].Hand!.Count==targetBefore-1,
            "A declined claim leaves both contest cards to the discard pile.");
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramMovedCardsClaimedEvent>().All(e=>e.SkillId!=Zhiba)&&
            g.CardMovements.Count(m=>m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill.pindian.finish")==2,
            "No claim event fired and both contest cards finished into the discard pile.");
        Replay(g,r);
    }

    private static int FactionSeat(GameEngine g,ContentRegistry r,string faction)=>Enumerable.Range(1,3)
        .First(seat=>r.Generals[g.CreateSnapshot(0).Players[seat].GeneralId].FactionId==faction);

    private static void ReachHunziChoice(GameEngine g,string optionId)
    {
        for(var i=0;i<400;i++)
        {
            if(P(g) is {PlayerSeat:0} pending&&pending.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")==optionId))return;
            if(P(g) is {PlayerSeat:0} prompt&&prompt.Choices.Any(c=>
                    c.Parameters.GetValueOrDefault("skill-id")=="classic:yingzi"||
                    c.Parameters.GetValueOrDefault("skill-id")=="classic:yinghun"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture did not reach the Hunzi choice "+optionId+".");
    }

    private static void SettleAuxiliarySkips(GameEngine g)
    {
        for(var i=0;i<400;i++)
        {
            if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0})return;
            if(P(g) is {PlayerSeat:0} prompt&&prompt.Choices.Any(c=>
                    c.Parameters.GetValueOrDefault("skill-id")=="classic:yingzi"||
                    c.Parameters.GetValueOrDefault("skill-id")=="classic:yinghun"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture did not settle past the auxiliary skill prompts.");
    }

    private static int Claims(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<ProgramMovedCardsClaimedEvent>().Count();
    private static int Options(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<ProgramOptionChosenEvent>().Count(e=>e.SkillId==Hunzi);

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

    private static void ReachOwnerTurn(GameEngine g,int turnNumber)
    {
        for(var i=0;i<400;i++)
        {
            if(g.Events.Select(e=>e.Payload).OfType<TurnStartedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber>=turnNumber))return;
            if(P(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0}) Answer(g,c=>c.Cards.Count==1);
            else if(P(g) is {Kind:DecisionKind.PlayCard,PlayerSeat:0}) Accept(g,new EndPlayPhaseCommand(0,g.Revision));
            else if(P(g) is {PlayerSeat:0} prompt&&prompt.Choices.Any(c=>
                    c.Parameters.GetValueOrDefault("skill-id")=="classic:yingzi"||
                    c.Parameters.GetValueOrDefault("skill-id")=="classic:yinghun"))
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        foreach(var entry in g.Log.TakeLast(6))Console.WriteLine("DIAG-LOG: "+entry.Message);
        throw new InvalidOperationException($"The fixture never reached the owner's turn {turnNumber}.");
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
