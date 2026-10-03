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

    public static void Definitions()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage());
        var general=registry.Generals["boundary:zhu-rong"];
        Require(general.Name=="界祝融"&&general.FactionId=="shu"&&general.BaseHp==4&&general.Gender==GeneralGender.Female&&
            general.SkillIds.SequenceEqual([Juxiang,Lieren,Changbiao])&&
            general.VariantId=="boundary"&&general.RulesetId=="sanguosha-ol"&&general.CharacterId=="character:zhu-rong",
            "The boundary Zhu Rong general registers the current OL shu 4HP female triple.");
        var juxiang=registry.GetSkill(Juxiang);
        Require(juxiang.Tags==SkillTag.Locked,"Juxiang registers as a locked skill.");
        Require(juxiang.Program!.CardPolicies.Select(p=>p.Kind).SequenceEqual(
                [SkillProgramCardPolicyKind.ExcludeGlobalTarget,SkillProgramCardPolicyKind.ClaimResolvedGlobalCard]),
            "Juxiang carries the barbarian immunity and resolved-card claim policies.");
        Require(juxiang.Program.CardPolicies.All(p=>p.CardKinds.SequenceEqual([CardKind.BarbarianAssault])),
            "Both Juxiang policies target only the Savage Assault.");
        var lieren=registry.GetSkill(Lieren).Program!;
        var trigger=lieren.Triggers.Single();
        Require(trigger.Window==SkillProgramTriggerWindow.AfterDamageApplied&&trigger.Optional&&
            trigger.Subject==SkillProgramTriggerSubject.DamageSource&&lieren.MinimumRulesVersion==193&&
            trigger.Effects.Select(e=>e.Op).SequenceEqual(
                [SkillProgramEffectOp.StartPindian,SkillProgramEffectOp.SelectSourceCard,SkillProgramEffectOp.MoveBoundCards]),
            "Lieren offers a pindian against the damaged target and takes one card only when won.");
        var changbiao=registry.GetSkill(Changbiao).Program!;
        var conversion=changbiao.ViewAs.Single();
        Require(conversion.VariableInputCount&&conversion.DistanceUnlimited&&
            conversion.OutputKind==CardKind.Slash&&conversion.ForPlay&&!conversion.ForResponse&&
            conversion.SourceZones.SequenceEqual([CardZoneKind.Hand]),
            "Changbiao converts any count of hand cards into one distance-unlimited slash.");
        var activation=changbiao.Activations.Single();
        Require(activation.MinCards==1&&activation.MaxCards==int.MaxValue&&activation.UsesPerPhase==1&&
            activation.TargetKind==SkillProgramTargetKind.OtherLiving&&
            activation.Effects.Select(e=>e.Op).SequenceEqual(
                [SkillProgramEffectOp.UseSelectedCardsAs,SkillProgramEffectOp.AccumulateSelectedCardCount]),
            "The activation spends any hand-card count on one target once per play phase and records the count.");
        var mark=changbiao.Triggers.Single(t=>t.Window==SkillProgramTriggerWindow.AfterDamageApplied);
        Require(mark.SourceSkillId==Changbiao&&mark.SourceViewAsId==conversion.Id&&!mark.Optional,
            "Only damage dealt by the converted slash marks the phase-end draw.");
        var ending=changbiao.Triggers.Single(t=>t.Window==SkillProgramTriggerWindow.PlayEnding);
        var draw=ending.Effects.Single(e=>e.Op==SkillProgramEffectOp.Draw);
        Require(draw.NumberExpression==SkillProgramNumberExpression.PhaseSkillUsage&&
            draw.SourceBind=="changbiao-converted"&&
            ending.Effects.Select(e=>e.Op).SequenceEqual([SkillProgramEffectOp.Draw,SkillProgramEffectOp.SetBooleanState]),
            "The play-ending branch draws the recorded converted count and clears the damage flag.");
        Require(changbiao.BooleanStates.Single() is { ResetScope: SkillProgramStateResetScope.PlayPhase, InitialValue: false },
            "The damage flag resets with the play phase.");
        Require(registry.GetSkill(Juxiang).ProgramPresentation!.Name=="巨象"&&
            registry.GetSkill(Lieren).ProgramPresentation!.Name=="烈刃"&&
            registry.GetSkill(Changbiao).ProgramPresentation!.Name=="长标"&&
            registry.GetSkill(Changbiao).ProgramPresentation!.Description.Contains("计入使用次数")&&
            registry.GetSkill(Changbiao).ProgramPresentation!.ActivationLabels.ContainsKey(activation.Id),
            "The presentation carries the official names, verbatim description and activation label.");
    }

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

    public static void LierenWinningPindianTakesTargetCard()
    {
        var(g,r)=Start(scenario:"slash");
        var before=g.CreateSnapshot(1).Players[1].Hand!.Count;
        Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.Contains(1)));
        Require(g.CreateSnapshot(1).Players[1].Hp==g.CreateSnapshot(1).Players[1].MaxHp-1,
            "The real slash damages the dodgeless target.");
        Activate(g);
        Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-source-card"));
        Require(g.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Single().Result.SourceWon,
            "The boosted owner wins the contest against the fixed rank two opponent.");
        Pick(g,"select-source-card");
        Settle(g);
        Require(g.CreateSnapshot(1).Players[1].Hand!.Count==before-2,
            "A won contest takes one card beyond the target's own pindian payment.");
        Require(g.CardMovements.Any(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0)),
            "The taken card joins the owner's hand.");
        Replay(g,r);Settle(g);Replay(g,r);
    }

    public static void LierenLostPindianTakesNothing()
    {
        var(g,r)=Start(scenario:"slash",plain:true);
        var before=g.CreateSnapshot(1).Players[1].Hand!.Count;
        var ownerHand=g.CreateSnapshot(0).Players[0].Hand!.Count;
        Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.Contains(1)));
        Activate(g);
        Answer(g,c=>c.Cards.Count==1);
        Require(g.CreateSnapshot(1).Players[1].Hand!.Count==before,
            "A tied contest is not a win and leaves the target's cards alone.");
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==ownerHand-1,
            "Only the pindian payment leaves the owner's hand when the contest is not won.");
        Replay(g,r);
    }

    public static void ChangbiaoUnlimitedSlashDamageDrawsAtPhaseEnd()
    {
        var(g,r)=Start(scenario:"slash");
        var hand=g.CreateSnapshot(0).Players[0].Hand!.Select(c=>c.Id).ToArray();
        var targetHp=g.CreateSnapshot(2).Players[2].Hp;
        Accept(g,new UseProgramSkillCommand(0,Changbiao,"any-hand-cards-as-unlimited-slash",
            hand.Take(2).ToArray(),[2],g.Revision,P(g)!.PromptId));
        Require(g.CreateSnapshot(2).Players[2].Hp==targetHp-1,
            "The converted slash ignores the seat distance and damages the opposite target.");
        SkipLieren(g);
        Settle(g);
        var afterSlash=g.CreateSnapshot(0).Players[0].Hand!.Count;
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==afterSlash+2,
            "The play phase ends with a draw equal to the two converted hand cards.");
        Require(g.CreateSnapshot(0).Players[0].MaxHp==5,
            "The lord keeps the identity plus one maximum HP while drawing.");
        ReplayFourViews(g,r);Replay(g,r);
    }

    public static void ChangbiaoDodgedSlashSkipsPhaseEndDraw()
    {
        var(g,r)=Start(scenario:"dodgy");
        var hand=g.CreateSnapshot(0).Players[0].Hand!.Select(c=>c.Id).ToArray();
        var targetHp=g.CreateSnapshot(1).Players[1].Hp;
        Accept(g,new UseProgramSkillCommand(0,Changbiao,"any-hand-cards-as-unlimited-slash",
            hand.Take(1).ToArray(),[1],g.Revision,P(g)!.PromptId));
        Settle(g);
        Require(g.CreateSnapshot(1).Players[1].Hp==targetHp,
            "The defender's real dodge keeps the converted slash off the target.");
        var afterSlash=g.CreateSnapshot(0).Players[0].Hand!.Count;
        Accept(g,new EndPlayPhaseCommand(0,g.Revision));
        Require(g.CreateSnapshot(0).Players[0].Hand!.Count==afterSlash,
            "A dodged converted slash deals no damage and draws nothing at the phase end.");
        Replay(g,r);
    }

    public static void ChangbiaoRunsOncePerPhase()
    {
        var(g,r)=Start(scenario:"slash");
        var hand=g.CreateSnapshot(0).Players[0].Hand!.Select(c=>c.Id).ToArray();
        Accept(g,new UseProgramSkillCommand(0,Changbiao,"any-hand-cards-as-unlimited-slash",
            hand.Take(1).ToArray(),[1],g.Revision,P(g)!.PromptId));
        SkipLieren(g);
        Settle(g);
        var remaining=g.CreateSnapshot(0).Players[0].Hand!.Select(c=>c.Id).ToArray();
        var second=g.Submit(new UseProgramSkillCommand(0,Changbiao,"any-hand-cards-as-unlimited-slash",
            remaining.Take(1).ToArray(),[1],g.Revision,P(g)!.PromptId));
        Require(!second.Accepted,
            "The second activation in the same play phase is refused by the per-phase use count.");
        Replay(g,r);
    }

    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,
        g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});

    private static void Activate(GameEngine g)
    {
        Reach(g,p=>p.SkillPrompt?.SkillId==Lieren&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    }

    // A converted slash that dealt damage also offers Lieren; the Changbiao checks skip it.
    private static void SkipLieren(GameEngine g)
    {
        Reach(g,p=>p.SkillPrompt?.SkillId==Lieren&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
    }

    private static void Pick(GameEngine g,string action)=>Answer(g,c=>
        c.Parameters.GetValueOrDefault("program-action")==action&&c.Parameters.GetValueOrDefault("source-zone")=="Hand");

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
