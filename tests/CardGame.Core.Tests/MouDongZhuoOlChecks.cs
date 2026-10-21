using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouDongZhuoOlChecks
{
    private const string Hero="mou:dong-zhuo-ol", Plain="fixture:mdzol-plain", Player="fixture:mdzol-player", Quiet="fixture:mdzol-quiet", Hold="fixture:mdzol-wine-hold", Opening="fixture:mdzol-real-opening-use";
    public static void MarkerCountDirectionalModifierSourceRemoval()
    {
        var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:marker-distance","revision":1,"modifiers":[
              {"id":"out","query":"outgoingDistance","operation":"add","valueExpression":"ownerMarkerCount","valueMarker":"yi","priority":0},
              {"id":"in","query":"incomingDistance","operation":"add","valueExpression":"ownerMarkerCount","valueMarker":"yi","priority":0}]}]}
            """;
        var labels=JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{{"fixture:marker-distance",new{name="标记距离",description="原生双向标记距离"}}}});
        var p=SkillProgramCatalog.Load(rules,labels).Programs["fixture:marker-distance"];
        SkillProgramRuleSource[] sources=[new(p.Id,"actual-instance",p)];var count=4;
        var context=new SkillProgramRuleContext(new(0,4,4,0,TurnPhase.Play),1){OwnerMarkerCount=m=>m==PlayerMarkerKind.Yi?count:0};
        long Distance(IReadOnlyList<SkillProgramRuleSource> active)=>((FiniteRuleQueryValue)RuleQueryService.EvaluateDirectionalDistance([new("native-seats",1)],
            SkillProgramRules.CollectContributions(SkillRuleQuery.OutgoingDistance,context,active),SkillProgramRules.CollectContributions(SkillRuleQuery.IncomingDistance,context,active)).Value).Value;
        if(Distance(sources)!=9)throw new InvalidOperationException("The shared loader and native direction reducer use the same captured marker on both distance stages.");
        count=0;if(Distance(sources)!=1)throw new InvalidOperationException("Removing the actual marker removes both numeric additions.");
        count=4;if(Distance([])!=1)throw new InvalidOperationException("Removing the actual active source removes both directional modifiers despite retained public marker history.");
        try{SkillProgramCatalog.Load(rules.Replace("\"operation\":\"add\"","\"operation\":\"set\"",StringComparison.Ordinal),labels);throw new InvalidOperationException("Owner marker directional Set must be rejected.");}
        catch(InvalidOperationException ex)when(!ex.Message.StartsWith("Owner marker directional Set",StringComparison.Ordinal)){}
    }
    public static void ActualXiongniFengshangWineColdAndRepeatedPreparing()
    {
        var s=Scene.Start(false);
        s.Reach(()=>s.P is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"&&c.Parameters.GetValueOrDefault("skill-id")=="mou-ol:xiongni"));
        s.Need(s.V(0).Markers?.Single(m=>m.Kind==PlayerMarkerKind.MouDongZhuoGuanbian).Count==4&&s.V(0).HandLimit==8,
            "The real GameStart freezes four participants in the native Guanbian HandLimit modifier.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        s.Reach(()=>s.P is{PlayerSeat:0}&&s.R()?.Stage==MouDongZhuoOlStage.OwnCost);
        var cost=s.P!.Choices.Single(c=>c.Cards.Count==1&&s.V(0).Hand.Any(h=>h.Id==c.Cards[0]&&h.Suit==Suit.Spade)).Cards.Single();
        s.Answer(c=>c.Cards.SequenceEqual([cost]));s.Play();
        s.Need(s.E<MouDongZhuoOlPaidEvent>().Length==2&&s.E<MouDongZhuoOlPaidEvent>().Any(e=>e.Payer==0&&e.CardId==cost)&&
            s.E<MouDongZhuoOlPaidEvent>().Any(e=>e.Payer==1)&&s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.Amount==1)==2&&
            s.V(0).Skills?.All(k=>k.Id!="mou-ol:guanbian")==true&&s.V(0).Markers?.All(m=>m.Kind!=PlayerMarkerKind.MouDongZhuoGuanbian)!=false&&s.V(0).HandLimit==4,
            "Xiongni really pays its HE entity, loses Guanbian, then each peer independently discards the same suit or takes native Damage1.");
        var pair=s.E<MouDongZhuoOlPaidEvent>().Select(e=>e.CardId).ToArray();
        s.Submit(new UseProgramSkillCommand(0,"mou-ol:fengshang","distribute-pair",[],[],s.G.Revision,s.P!.PromptId));
        for(var i=0;i<2;i++){
            s.Reach(()=>s.P is{PlayerSeat:0}&&s.R()?.Stage==MouDongZhuoOlStage.GiftCard);var id=pair[i];s.Answer(c=>c.Cards.SequenceEqual([id]));
            // Keep the later Slash recipient's hand empty: giving it this
            // genuine Dodge would correctly prevent the damage being tested.
            var target=i+1;s.Answer(c=>c.Targets.SequenceEqual([target]));
        }
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==Hold);
        var issued=s.E<MouDongZhuoOlWineIssuedEvent>().Single();
        s.Need(s.E<MouDongZhuoOlGiftEvent>().Select(e=>e.CardId).Order().SequenceEqual(pair.Order())&&s.E<MouDongZhuoOlGiftEvent>().Select(e=>e.Recipient).Order().SequenceEqual([1,2])&&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==issued.UseId&&u.CardId==0&&u.Action?.EffectiveKind==CardKind.Alcohol&&u.MouDongZhuoWine?.ParentId==issued.FrameId)&&
            !s.E<CardUseDebitRecordedEvent>().Any(e=>e.Debit.CardActionId==issued.ActionId),
            "Two genuine same-suit discarded entities go to two different roles before the exact material-free Wine reaches a real committed-use child without a quota debit.");
        s.Cold();s.Answer(_=>true);s.Play();
        s.Need(s.E<MouDongZhuoOlWineReturnedEvent>()is[var returned]&&returned.UseId==issued.UseId&&s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==issued.UseId&&e.CardKind==CardKind.Alcohol)==1&&s.V(0).HasAlcoholEffect,
            "One representative Cold at the actual Wine child preserves paid gifts and returns the native Wine exactly once.");
        var slash=s.V(0).Hand.Single(c=>c.Kind==CardKind.Slash).Id;s.Native(slash,3);s.Play();
        var ordinary=s.V(0).Hand.Single(c=>c.Kind==CardKind.Alcohol).Id;
        s.Need(s.G.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Alcohol&&a.CardId==ordinary)&&
            s.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==2),
            "The native Wine supplies the real Slash bonus while leaving the ordinary Wine allowance available after that Slash consumes the effect.");

        // One genuine turn-zero equipment Use plus eight actual Qun DrawTwo
        // uses qualify the NPC's printed Lord skill and a second Preparing.
        var z=Scene.Start(true);z.Play();
        var opening=z.E<MouDongZhuoOlBlackUseEvent>().Single(e=>e.Actor==0&&e.Turn==0&&e.Black);
        z.Need(z.E<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==opening.FrameId&&e.CardKind==CardKind.Crossbow&&e.CardId>0)&&
            z.E<TurnStartedEvent>().Length==1,"The actual GameStarting physical black equipment Use is counted before the first true turn, without a manufactured turn ordinal.");
        foreach(var card in z.V(0).Hand.Where(c=>c.Kind==CardKind.DrawTwo).Select(c=>c.Id).ToArray()){z.Native(card);z.Play();}
        z.End();z.Reach(()=>z.V(1).MaxHp==5&&z.V(1).Hp==5&&z.V(1).Skills?.Any(k=>k.Id=="classic:benghuai")==true);
        z.Need(z.E<MouDongZhuoOlZhibingEvent>()is[{Owner:1,BlackCount:9}]&&z.V(1).Skills?.Any(k=>k.Id=="classic:fencheng")==true,
            "Nine real black card Uses by another Qun role qualify the genuine Preparing: native MaxHp+1, Recover1 and both existing derived skills.");
        z.Play();z.End();z.Reach(()=>z.V(1).MaxHp==6&&z.V(1).Hp==6);
        z.Need(z.E<MouDongZhuoOlZhibingEvent>().Length==2&&z.E<MouDongZhuoOlZhibingEvent>().All(e=>e.BlackCount==9)&&
            z.E<SkillsAcquiredEvent>().Count(e=>e.PlayerSeat==1&&e.SkillIds.Contains("classic:fencheng"))==1&&z.E<SkillsAcquiredEvent>().Count(e=>e.PlayerSeat==1&&e.SkillIds.Contains("classic:benghuai"))==1,
            "The user's chosen cumulative thresholds repeat at each actual Preparing, while native grants preserve the already acquired instances without duplicate acquisitions.");
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(s=>G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public MouDongZhuoOlReceipt? R()=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MouDongZhuoOl;
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diagnostic());}
        public void Submit(GameCommand c){var result=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}
        public void Answer(Func<PromptChoice,bool> test){var p=P!;var c=p.Choices.FirstOrDefault(test);Need(p.PlayerSeat==0&&c is not null,"Required genuine human choice missing.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,G.Revision));}
        public void End()=>Submit(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Native(int id,int? target=null){var a=G.GetHumanLegalActions().FirstOrDefault(a=>a.CardId==id&&a.Kind!=LegalActionKind.Recast&&a.ConversionSource is null&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));Need(a is not null,"Actual native action missing.");Submit(new PlayCardCommand(0,id,a!.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId));}
        public void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<240;i++){
                if(done())return;Need(G.State.Winner==Winner.None,"Unexpected terminal boundary at "+line);
                if(P is not{PlayerSeat:0}p)Submit(new AdvanceOneStepCommand(G.Revision));
                else if(p.Kind==DecisionKind.PlayCard)End();
                else if(p.Kind==DecisionKind.DiscardCards)Submit(new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,G.Revision));
                else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                else throw new InvalidOperationException("Unexpected actual human input at "+line+Diagnostic());
            }throw new InvalidOperationException("Bounded actual Dong Zhuo flow not reached at "+line+Diagnostic());
        }
        public void Cold(){var before=SnapshotJson.Serialize(G.CreateSnapshot(0));G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==SnapshotJson.Serialize(G.CreateSnapshot(0)),"Paid native Wine commitment and its original typed parent must survive Cold unchanged.");}
        private string Diagnostic()=>JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,G.State.Phase,Prompt=P,Receipt=R(),Players=Enumerable.Range(0,4).Select(V),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=G.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        public static Scene Start(bool zhibing)
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(zhibing));
            ShanheStartingCard[] hand=zhibing?Enumerable.Range(0,8).Select(i=>new ShanheStartingCard(CardKind.DrawTwo,Suit.Spade,i+1)).ToArray():[new(CardKind.Dodge,Suit.Spade,5),new(CardKind.Slash,Suit.Heart,6),new(CardKind.Alcohol,Suit.Heart,9)];
            var ally=zhibing?new ShanheCombatantDefinition(Hero,Hp:4,ExtraSkillIds:[Quiet]){StartingHand=[],QualifiedPrintedLordSkillIds=["mou-ol:zhibing"]}:new ShanheCombatantDefinition(Plain,Hp:12){StartingHand=[new(CardKind.Dodge,Suit.Spade,6)]};
            var setup=new ShanheBattleSetup(7,1,zhibing?"mdzol-lord":"mdzol-native",zhibing?Player:Hero,zhibing?12:4,0,0,0,0,zhibing?[Opening]:[Hold],hand,[],
                [new(Plain,Hp:12){StartingHand=[]},new(Plain,Hp:12){StartingHand=[]}],[ally],new string('d',64));
            var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},registry);
            var s=new Scene(g,registry);s.Submit(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture(bool opening):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-dong-zhuo-ol",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"classic:li-ru","classic:dong-zhuo");
            foreach(var kind in new[]{CardKind.Dodge,CardKind.Slash,CardKind.Alcohol,CardKind.DrawTwo,CardKind.Crossbow}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mdzol-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var definitions=new object[]{
                new{id=Quiet,revision=1,triggers=new[]{new{id="quiet",window="afterNormalDraw",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}}}},
                new{id=Hold,revision=1,triggers=new[]{new{id="real-wine-commit",window="cardUseCommitted",ownerRelation="actor",cardKinds=new[]{"alcohol"},optional=false,usageScope="game",usageLimit=1,effects=new[]{new{op="chooseOption",target="owner",resultBind="wine-hold",options=new[]{new{id="continue"}}}}}}},
                new{id=Opening,revision=1,triggers=new[]{new{id="real-opening-equipment",window="gameStarting",subject="owner",optional=false,effects=new[]{new{op="useRandomDeckEquipment",target="owner",targetRef=new{kind="owner"},resultBind="real-equipment"}}}}}};
            var labels=new Dictionary<string,object>{[Quiet]=new{name="安静真实同行",description="正常摸牌后跳过出牌阶段。"},[Hold]=new{name="真实酒提交子窗",description="第一次真实酒提交后暂停。",optionLabels=new Dictionary<string,string>{{"continue","继续真实酒"}}},[Opening]=new{name="真实开局装备",description="原生使用牌堆中唯一装备。"}};
            var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=definitions}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Plain,"真实群同行","","standard:none","qun",12,[Quiet]));
            b.AddGeneral(new(Player,"真实群用牌者","","standard:none","qun",12));
            b.AddDeck(new("fixture:mdzol-deck","固定真实闪牌堆",0,2,[]){PhysicalCards=(opening?new[]{new ContentDeckPhysicalCard("fixture:mdzol-"+CardKind.Crossbow,Suit.Spade,7)}:[])
                .Concat(Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:mdzol-"+CardKind.Dodge,Suit.Club,7))).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋董卓实际流程",4,4,new Dictionary<string,int>(),"fixture:mdzol-deck",GeneralPoolIds:[Hero,Plain,Player],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
