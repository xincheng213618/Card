using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenPangTongChecks
{
    private const string Hero="happy:shen-pang-tong",Lunce="happy:lunce",Driver="fixture:happy-pt-driver",Quiet="fixture:happy-pt-quiet",Watch="fixture:happy-pt-watch",Peer="fixture:happy-pt-peer",Deck="fixture:happy-pt-deck";
    public static void IndependentPlansNativeSlashTheftLowerActualTurnRewardsAndFailureGrowthCold()
    {
        var s=Scene.Start();s.Grant(HappyPangTongPlanKind.Middle,0);s.Grant(HappyPangTongPlanKind.Upper,0);s.Play();
        s.Need(s.E<HappyPangTongPlanIssuedEvent>().Select(e=>e.Kind).SequenceEqual([HappyPangTongPlanKind.Middle,HappyPangTongPlanKind.Upper])&&
            s.V(0) is{Hp:3,MaxHp:3},"Round and actual owner-turn starts issue two independent plans at the printed three HP.");
        s.Use("stock");s.Play();var before=s.V(0).HandCount;var otherHand=s.V(1).HandCount;
        s.Slash(1);s.Menu(HappyPangTongStage.MiddleCard);s.Cold();
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="take"&&c.Targets.SequenceEqual([1]));s.Hold();
        var root=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.HappyPangTong is{Stage:HappyPangTongStage.GainChildren});var paid=root.HappyPangTong!;
        s.Need(s.V(0).HandCount==before&&s.V(1).HandCount==otherHand-1&&paid.GainFrom==CardLocation.Hand(1)&&
            s.G.CardMovements.Count(m=>m.CardId==paid.GainCardId&&m.From==paid.GainFrom&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.happy:lunce.middle-gain")==1,
            "The first real Slash pays its own material, then the middle plan really obtains one opaque opponent hand entity before native movement children.");
        s.Cold();s.Continue();s.Grant(HappyPangTongPlanKind.Lower,1,false);s.Play();
        s.Need(s.E<HappyPangTongPlanSettledEvent>().Count(e=>e.Success&&e.Kind==HappyPangTongPlanKind.Middle)==1&&
            s.E<HappyPangTongBenefitIssuedEvent>().Where(e=>e.Branch=="draw").Select(e=>e.Amount).SequenceEqual([1])&&s.V(1).Hp==9,
            "The same original Slash resumes after one Lanhai native draw and one independent reissued plan, and middle cannot retrigger its own first Slash.");
        var peerHp=s.V(1).Hp;s.End();s.Menu(HappyPangTongStage.UpperTarget);s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="slash-target"&&c.Targets.SequenceEqual([1]));
        s.Menu(HappyPangTongStage.UpperCard);var upperPayment=s.P!.Choices.First(c=>c.Parameters.GetValueOrDefault("mode")=="use").Cards.ToArray();s.Cold();
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="use"&&c.Cards.SequenceEqual(upperPayment));s.Grant(HappyPangTongPlanKind.Upper,2,false);
        s.Need(upperPayment.Length==1&&s.E<HappyPangTongUpperBoundEvent>().Length==1&&s.E<HappyPangTongUpperReturnedEvent>().Length==1&&
            s.E<CardUseDeclaredEvent>().Count(e=>e.SourceSeat==0&&e.CardKind==CardKind.Slash&&e.CardId>0)==2&&s.V(1).Hp==peerHp-1&&
            s.E<HappyPangTongBenefitIssuedEvent>().Where(e=>e.Branch=="draw").Select(e=>e.Amount).SequenceEqual([1,2]),
            "Upper uses a second real paid Slash in that same actual turn despite the ordinary one-Slash quota, and Lanhai's second success really requests two cards.");
        s.Menu(HappyPangTongStage.LowerChoice);var lowerHand=s.V(0).HandCount;var lowerHp=s.V(1).Hp;s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="lower");
        s.Grant(HappyPangTongPlanKind.Upper,2,false);
        s.Need(s.V(0).HandCount==lowerHand+3&&s.V(1).Hp==lowerHp+1&&
            s.E<RecoveryAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.Amount==1)==1&&
            s.E<HappyPangTongBenefitIssuedEvent>().Where(e=>e.Branch=="draw").Select(e=>e.Amount).SequenceEqual([1,2,1]),
            "The quiet target's actual ending accepts lower: native draw two precedes its real one-HP recovery, then Lanhai restarts X at one in that different actual turn.");
        s.Offer();
        s.Need(s.E<HappyPangTongPlanSettledEvent>().Count(e=>!e.Success&&e.Kind==HappyPangTongPlanKind.Upper&&e.TargetSeat==2)==2&&
            s.E<HappyPangTongBenefitIssuedEvent>().Count(e=>e.Branch=="grow")==1&&s.V(0) is{Hp:4,MaxHp:4},
            "Both independently reissued upper plans remain outstanding until target two's real ending, fail once each and produce one native HP/maximum growth.");
        s.Replay();

        var cap=Scene.Start();
        for(var n=0;n<4;n++)
        {
            cap.Grant(HappyPangTongPlanKind.Upper,0);cap.Grant(HappyPangTongPlanKind.Upper,0);cap.Play();
            if(n==3){cap.Use("wound");cap.Play();cap.Need(cap.V(0) is{Hp:5,MaxHp:6},"Before the capped fourth pair, a genuine native loss leaves one recoverable HP.");}
            cap.End();cap.Offer();
            cap.Need(cap.E<HappyPangTongPlanSettledEvent>().Count(e=>!e.Success)==2*(n+1)&&
                cap.E<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Lunce&&e.BindingId=="round-plan")==n+1&&
                cap.E<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Lunce&&e.BindingId=="turn-plan")==n+1&&
                cap.E<HappyPangTongBenefitIssuedEvent>().Count(e=>e.Branch=="grow")==n+1&&
                cap.E<HappyPangTongBenefitIssuedEvent>().Count(e=>e.Branch=="grow"&&e.MaximumAfter>e.MaximumBefore)==Math.Min(3,n+1)&&cap.V(0).MaxHp==3+Math.Min(3,n+1)&&cap.V(0).Hp==3+Math.Min(3,n+1)&&
                (n<3||cap.E<RecoveryAppliedEvent>().Any(e=>e.TargetSeat==0&&e.Amount==1&&e.RemainingHp==6)),
                "Every two genuine failed plans still recover one HP after maximum growth reaches the official cumulative three-point cap.");
        }
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public void Offer()=>Reach(()=>P?.SkillPrompt?.SkillId==Lunce&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        public void Grant(HappyPangTongPlanKind kind,int target,bool optional=true)
        {
            if(optional){Offer();Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
            Menu(HappyPangTongStage.Grant);Answer(c=>c.Parameters.GetValueOrDefault("mode")=="grant"&&c.Parameters.GetValueOrDefault("value")==kind.ToString()&&c.Targets.SequenceEqual([target]));
        }
        public void Menu(HappyPangTongStage stage)=>Reach(()=>P is{PlayerSeat:0}&&G.ResolutionStack.LastOrDefault() is ProgramSkillFrame{HappyPangTong:{}r}&&r.Stage==stage);
        public void Hold()=>Reach(()=>P?.SkillPrompt?.SkillId==Watch);
        public void Continue()=>Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Use(string id)=>Accept(new UseProgramSkillCommand(0,Driver,id,[],[],G.Revision,P!.PromptId));
        public void Slash(int target)
        {
            var a=G.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
        }
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Answer(Func<PromptChoice,bool> match){var p=P!;Need(p.PlayerSeat==0,"Only the true human answers this native plan prompt.");Accept(new AnswerPromptCommand(0,p.PromptId,p.Choices.First(match).Id,G.Revision));}
        public void Reach(Func<bool> ready)
        {
            for(var n=0;n<240;n++)
            {
                if(ready())return;
                if(P is{PlayerSeat:0}p)throw new InvalidOperationException("Unexpected Happy Pang Tong boundary "+JsonSerializer.Serialize(p));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Pang Tong missed its bounded native boundary.");
        }
        private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),
            G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
        public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"Private native Slash selection, exact obtained entity and same-plan cursor recover without repaying.");}
        public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"Replay preserves independent issuance, native Slash and card movement returns, actual-turn success counts and cumulative failures.");}
        private void Accept(GameCommand c){var result=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Happy Pang Tong command.");}
        public void Need(bool condition,string text){if(!condition)throw new InvalidOperationException(text+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Frames=G.ResolutionStack.Select(f=>new{f.Id,Kind=f.GetType().Name}),Facts=G.Events.TakeLast(8).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(53,1,"happy-pang-tong",Hero,3,0,0,0,0,[Driver,Watch],[],[],
                [new(Peer){StartingHand=[]},new(Peer){StartingHand=[]}],
                [new(Peer){StartingHand=[new(CardKind.Slash,Suit.Club,7),new(CardKind.Slash,Suit.Spade,8),new(CardKind.Slash,Suit.Heart,9)]}],new string('c',64)){DefaultGodFactionId="shu"};
            var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
                ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,MaxTurns=24},registry);
            var s=new Scene(game,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-pang-tong",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":8}]},
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"middle-paid","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy:lunce.middle-gain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{{Driver,new{name=Driver,description="Native draw source"}},{Quiet,new{name=Quiet,description="Retain actual quiet turns"}},
                {Watch,new{name=Watch,description="Pause after the real obtained entity",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Peer,"真实安静角色","",Quiet,"wei",10));
            b.AddDeck(new(Deck,"真实杀小局",0,0,[]){PhysicalCards=Enumerable.Range(0,80).Select(n=>new ContentDeckPhysicalCard("standard:slash",new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[n%4],n%13+1)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"Happy Pang Tong native plan fixture",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
