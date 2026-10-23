using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenHuaTuoChecks
{
    private const string Hero="happy:shen-hua-tuo",Jishi="happy:jishi",Needle="happy:shenzhen",Peach="happy:taoxian",
        Driver="fixture:happy-ht-driver",Foreign="fixture:happy-ht-foreign",Watch="fixture:happy-ht-health",Quiet="fixture:happy-ht-quiet",Peer="fixture:happy-ht-peer",Deck="fixture:happy-ht-deck";
    public static void MedicineDyingHeartEquipmentAndPaidNeedleTargetsCold()
    {
        var s=Scene.Start();s.Offer(Needle);s.Activate();s.Menu("amount");
        s.Need(s.Medicine==3&&s.V(0) is{Hp:1,MaxHp:1},"Happy Hua Tuo starts at the real printed one HP with exactly three medicines.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("value")=="0");s.Play();
        s.Need(s.Medicine==3&&s.V(0).HandLimit==4&&!s.E<HappyHuaMedicinePaidEvent>().Any()&&!s.E<HappyHuaNeedleTargetEvent>().Any(),
            "Choosing zero medicine is a legal no-op without payment or artificial HP changes; Jishi adds three to the actual hand limit.");
        var heart=s.V(0).Hand.Single(c=>c.Suit==Suit.Heart).Id;var equipment=s.V(0).Equipment.Single(c=>c.Suit==Suit.Heart).Id;
        var allyPeach=Enumerable.Range(1,3).SelectMany(seat=>s.V(seat).Hand.Where(c=>c.Kind==CardKind.Peach).Select(c=>(Seat:seat,CardId:c.Id))).Single();
        s.Use("deep-wound");s.Offer(Jishi);s.Activate();s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
        var rescue=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.HappyHuaRescue is not null);var receipt=rescue.HappyHuaRescue!;
        s.Need(receipt is{VictimSeat:0,HpBefore:-2,MedicineBefore:3,MedicineAfter:2}&&s.V(0).Hp==1&&s.Medicine==2&&
            s.G.ResolutionStack.OfType<DyingFrame>().Any(f=>f.Id==receipt.DyingId)&&rescue.SelectedTargetSeats.SequenceEqual([0]),
            "The exact real dying entry pays one medicine before recovering from negative two to one, and retains its original native health child.");
        s.Cold();s.Continue();s.Play();
        s.Need(s.E<HappyHuaMedicinePaidEvent>().Count(e=>e.Purpose=="rescue")==1&&s.E<RecoveryAppliedEvent>().Count(e=>e.TargetSeat==0&&e.Amount==3)==1,
            "Cold recovery neither pays medicine twice nor repeats the full recovery from negative HP.");

        s.Use("wound");s.RescuePrompt();
        s.Need(s.P!.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="peach"&&c.Cards.SequenceEqual([equipment])),
            "UseOnly with forResponse false still offers the real owned red-heart equipment through the engine's native dying-use conversion path.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("response")=="peach"&&c.Cards.SequenceEqual([equipment]));
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
        s.Need(s.G.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.CardId==equipment&&u.CardKind==CardKind.Peach&&
            u.Action?.ConversionChain.Any(c=>c.SkillId==Peach&&c.BindingId=="heart-as-peach")==true)&&s.Medicine==2,
            "The genuine Peach rescue retains the equipment material and Taoxian conversion source; equipment loss does not replenish hand-loss medicine.");
        s.Continue();s.Play();s.End();

        s.Offer(Foreign);s.Activate();s.RescuePrompt();
        s.Answer(c=>c.Parameters.GetValueOrDefault("response")=="peach"&&c.Cards.SequenceEqual([heart]));
        s.Offer(Foreign);
        var moved=s.G.CardMovements.Single(m=>m.CardId==heart&&m.From==CardLocation.Hand(0));
        s.Need(s.Medicine==3&&moved.HappyMedicineHandLoss is{OwnerSeat:0,EffectiveSuit:Suit.Heart,ActualTurnOwnerSeat:1}&&
            s.E<HappyHuaMedicinePaidEvent>().Count(e=>e.Purpose=="rescue")==1,
            "The next actual foreign turn loses the red hand entity once and replenishes medicine to the cap, without confusing its later Processing movement or the declined rescue.");
        var beforeOtherPeach=s.V(0).HandCount;
        s.Activate();
        var foreignFrame=s.E<ProgramBindingStartedEvent>().Last(e=>e.SkillId==Foreign).FrameId;
        s.Reach(()=>s.E<ProgramBindingResolvedEvent>().Any(e=>e.FrameId==foreignFrame));
        s.Need(s.V(0).IsAlive&&s.V(0).Hp==1,
            "The second actual foreign loss must finish its real rescue before waiting for a future owner turn. "+s.RescueFacts());
        s.Need(s.E<CardUseDeclaredEvent>().Count(e=>e.SourceSeat==allyPeach.Seat&&e.CardId==allyPeach.CardId&&e.CardKind==CardKind.Peach)==1&&
            s.V(0).HandCount==beforeOtherPeach+1,"The actual ally Peach and Taoxian draw must already exist when that original foreign-loss binding completes. "+s.RescueFacts());
        s.Offer(Needle);
        s.Need(s.V(0).Hp==1&&s.V(0).HandCount==beforeOtherPeach+1&&
            s.E<CardUseDeclaredEvent>().Count(e=>e.SourceSeat==allyPeach.Seat&&e.CardId==allyPeach.CardId&&e.CardKind==CardKind.Peach)==1&&
            s.E<DyingResponseEvent>().Count(e=>e.ResponderSeat==0&&!e.UsedPeach&&!e.UsedAlcohol)==1&&
            s.E<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Peach&&e.BindingId=="other-peach-use")==1,
            "With no rescue material left, Hua Tuo automatically passes the native responder cursor; the ally's real rescue Peach gives exactly one native draw, while Hua Tuo's own converted Peaches give none.");

        s.Activate();s.Menu("amount");s.Answer(c=>c.Parameters.GetValueOrDefault("value")=="2");s.Menu("targets");
        var paid=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.HappyHuaNeedle is{Stage:HappyHuaNeedleStage.TargetChoice});var paidId=paid.Id;
        s.Need(s.Medicine==1&&paid.HappyHuaNeedle is{Amount:2,Before:3,After:1}&&
            s.P!.Choices.All(c=>c.Targets.Count==2&&c.Targets.Distinct().Count()==2)&&s.P.Choices.Any(c=>c.Targets.Contains(0)),
            "Positive medicine is genuinely paid before choosing exactly its many distinct living characters, and the real menu permits Hua Tuo himself.");
        var hp2=s.V(2).Hp;var hp3=s.V(3).Hp;s.Cold();
        s.Answer(c=>c.Targets.SequenceEqual([2,3])&&c.Parameters.GetValueOrDefault("value")=="lose-2-3");s.Play();
        s.Need(s.V(2).Hp==hp2-1&&s.V(3).Hp==hp3-1&&s.Medicine==1&&
            s.E<HappyHuaMedicinePaidEvent>().Count(e=>e.FrameId==paidId&&e.Purpose=="needle")==1&&
            s.E<HappyHuaNeedleTargetEvent>().Where(e=>e.FrameId==paidId).Select(e=>e.Seat).SequenceEqual([2,3])&&
            s.E<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==paidId&&e.Amount==1)==2,
            "The paid cold menu returns to its original frame and the native participant cursor loses exactly one HP per chosen character, without repaying medicine.");
        s.End();s.Offer(Needle);s.Activate();s.Menu("amount");s.Answer(c=>c.Parameters.GetValueOrDefault("value")=="1");s.Menu("targets");
        s.Answer(c=>c.Targets.SequenceEqual([2])&&c.Parameters.GetValueOrDefault("value")=="recover-2");s.Play();
        s.Need(s.Medicine==0&&s.V(2).Hp==hp2&&s.V(3).Hp==hp3-1&&
            s.E<RecoveryAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1)==1,
            "The other mode consumes the remaining real medicine and heals exactly its chosen participant once through native recovery.");
        s.Replay();
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public int Medicine=>V(0).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.HappyMedicine)?.Count??0;
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public string RescueFacts()=>JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,OwnerAlive=V(0).IsAlive,OwnerHp=V(0).Hp,
            PeachHolders=Enumerable.Range(0,4).Select(seat=>new{Seat=seat,Cards=V(seat).Hand.Where(c=>c.Kind==CardKind.Peach).Select(c=>c.Id).ToArray()}),
            Facts=G.Events.Select(e=>e.Payload).Where(e=>e is PlayerDiedEvent or PlayerDyingEvent or DyingResponseEvent or HappyHuaMedicinePaidEvent||
                e is CardUseDeclaredEvent{CardKind:CardKind.Peach}||e is ProgramBindingStartedEvent a&&a.SkillId is Jishi or Foreign||
                e is ProgramBindingResolvedEvent b&&b.SkillId is Jishi or Foreign).Select(e=>JsonSerializer.Serialize(e,e.GetType())).ToArray()});
        public void Offer(string skill)=>Reach(()=>P?.SkillPrompt?.SkillId==skill&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        public void Activate()=>Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        public void Menu(string branch)=>Reach(()=>P?.SkillPrompt?.SkillId==Needle&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")==branch));
        public void Continue()=>Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        public void RescuePrompt()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.RescueDying});
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Use(string id)=>Accept(new UseProgramSkillCommand(0,Driver,id,[],[],G.Revision,P!.PromptId));
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,G.Revision));}
        public void Reach(Func<bool> ready)
        {
            for(var i=0;i<220;i++)
            {
                if(ready())return;
                if(P is{PlayerSeat:0}p)
                {
                    if(p.SkillPrompt?.SkillId==Watch){Continue();continue;}
                    var skip=p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
                    if(skip is not null){Answer(c=>c.Id==skip.Id);continue;}
                    throw new InvalidOperationException("Unexpected Happy Hua Tuo boundary "+JsonSerializer.Serialize(p));
                }
                Need(G.State.Winner==Winner.None,"The fixed short fixture retains its actual next turn.");Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Hua Tuo missed its bounded native occurrence.");
        }
        private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),
            G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
        public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"The exact paid medicine, native dying recovery child and target menu restore without another payment.");}
        public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"Replay retains actual medicine sources, original Peach materials and exact needle participants.");}
        private void Accept(GameCommand c){var result=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Happy Hua Tuo command.");}
        public void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Facts=G.Events.TakeLast(5).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(37,1,"happy-hua-tuo",Hero,1,0,0,0,0,[Driver,Foreign,Watch],
                [new(CardKind.Slash,Suit.Heart,7)],[new(CardKind.BaguaFormation,Suit.Heart,2)],
                [new(Peer){StartingHand=[]},new(Peer){StartingHand=[]}],[new(Peer){StartingHand=[new(CardKind.Peach,Suit.Heart,3)]}],new string('b',64)){DefaultGodFactionId="qun"};
            // Current policy knows the public teams; legacy policy 1 judges unknown identity roles and declines this ally's rescue.
            var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,
                ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},registry);
            var s=new Scene(game,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-hua-tuo",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"deep-wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":3}]},
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Foreign}}","revision":1,"triggers":[{"id":"foreign-loss","isSkillInvocation":false,"window":"turnStartBeforeNormalFlow","subject":"owner","turnOwnerScope":"otherLiving","optional":true,"usageScope":"game","usageLimit":2,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-recovered","isSkillInvocation":false,"window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{[Driver]=new{name=Driver,description="原生健康驱动"},[Foreign]=new{name=Foreign,description="真实他人回合的自身体力损失"},
                [Watch]=new{name=Watch,description="付款后的原生回复子窗口",optionLabels=new Dictionary<string,string>{{"continue","继续"}}},[Quiet]=new{name=Quiet,description="安静的真实角色回合"}};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Peer,"安静的真实角色","",Quiet,"wei",4));
            b.AddDeck(new(Deck,"固定真实牌",0,0,[]){PhysicalCards=Enumerable.Range(0,48).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐药济神针小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
