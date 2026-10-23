using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenDianWeiChecks
{
    private const string Hero="happy:shen-dian-wei",Shenwei="happy:shenwei",Elai="happy:elai",Driver="fixture:happy-dw-driver",
        Quiet="fixture:happy-dw-quiet",Peer="fixture:happy-dw-peer",Deck="fixture:happy-dw-deck";
    public static void ProtectedChooserPaidElaiNativeDamageAndDeathQueueCold()
    {
        var s=Scene.Start();s.Grant([1]);s.Play();
        s.Need(s.V(0) is{Hp:5,MaxHp:5}&&s.Guards(1)==1&&Enumerable.Range(0,4).Where(i=>i!=1).All(i=>s.Guards(i)==0),
            "At printed five HP the actual turn-start Shenwei grants exactly one guard to the chosen living character.");
        s.Use("hurt-self");s.Play();
        s.Need(s.V(0).Hp==3&&s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==0&&e.Amount==2)==1,
            "A real self-directed native damage event also gains Kuangxi's one damage while any living field guard exists.");
        var protectedHp=s.V(1).Hp;s.Use("hurt-other",[1]);s.ElaiPrompt();
        var paid=s.E<HappyGuardPaidEvent>().Single();
        s.Need(paid.GuardedSeat==1&&paid.Source.OwnerSeat==0&&paid.Amount==2&&s.Guards(1)==0&&s.V(1).Hp==protectedHp&&s.V(0).Hp==3&&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.HappyGuard is{Stage:HappyGuardStage.RemovedChildren}r&&r.AttackId==paid.AttackId),
            "The protected ally's real AI choice pays its guard once and freezes the original two-damage occurrence before native Elai children.");
        s.Cold();s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="recover");s.Play();
        s.Need(s.V(0).Hp==2&&s.V(1).Hp==protectedHp&&s.E<HappyGuardPaidEvent>().Length==1&&
            s.E<HappyGuardTransferredEvent>().Count(e=>e.FrameId==paid.FrameId&&e.AttackId==paid.AttackId&&e.OriginalTarget==1&&e.Target==0&&e.Amount==2)==1&&
            s.E<RecoveryAppliedEvent>().Any(e=>e.TargetSeat==0&&e.Amount==1),
            "Cold continuation recovers one HP natively before the same paid damage is transferred exactly once; it never damages the protected ally.");

        s.Use("lose-one");s.Play();s.Need(s.V(0).Hp==1,"The next actual turn begins at exactly one HP.");s.End();
        s.Grant([0,1]);s.Play();
        s.Need(s.Guards(0)==1&&s.Guards(1)==1&&s.G.Events.Select(e=>e.Payload).OfType<HappyGuardStartedEvent>().Last(e=>e.Source.BindingId=="grant-guards").GrantLimit==2,
            "At exactly one HP Shenwei may grant two distinct guards, including oneself, without stacking a second guard on either character.");
        s.Use("recover-four");s.Play();s.Need(s.V(0).Hp==5,"The native recovery driver restores the ordinary printed maximum before the next protected wound.");
        var enemyHp=s.V(3).Hp;s.Use("hurt-other",[1]);s.ElaiPrompt();
        s.Need(s.Guards(1)==0&&s.Guards(0)==1,"The original protected character pays its own guard, leaving the separate self guard on the field.");
        var secondPaid=s.E<HappyGuardPaidEvent>().Last();
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="damage"&&c.Targets.SequenceEqual([3]));s.GuardPrompt();
        var ownGuard=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.HappyGuard is{Stage:HappyGuardStage.GuardChoice});
        s.Need(s.P!.PlayerSeat==0&&s.Guards(0)==1&&ownGuard.HappyGuard is{GuardedSeat:0}own&&own.AttackId==secondPaid.AttackId&&own.Amount==secondPaid.Amount&&
            s.V(0).Hp==5&&s.V(3).Hp==enemyHp-2,
            "The same transferred occurrence offers the recipient's independent self guard to its real human controller after Elai's native damage has completed.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="decline");s.Play();
        s.Need(s.V(3).Hp==enemyHp-2&&s.V(0).Hp==3&&s.V(1).Hp==protectedHp&&
            s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==2)==1,
            "Elai chooses one in-range other target and causes real native damage, which receives Kuangxi only because the other guard still exists.");
        s.Use("hurt-self");s.GuardPrompt();s.Cold();s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="protect");s.ElaiPrompt();
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="none");s.Play();
        s.Need(s.V(0).Hp==1&&Enumerable.Range(0,4).All(i=>s.Guards(i)==0)&&s.E<HappyGuardPaidEvent>().Length==3&&
            s.E<HappyGuardElaiChoiceEvent>().Count(e=>e.OwnerSeat==0&&e.Mode=="none")==1,
            "The guarded owner can remove its own last guard and choose no Elai effect; the original damage still occurs once without a fabricated self redirection.");
        var unguardedHp=s.V(2).Hp;s.Use("hurt-other",[2]);s.Play();
        s.Need(s.V(2).Hp==unguardedHp-1&&s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1)==1,
            "When the last field guard is gone the next real damage returns to its base amount.");

        s.End();s.Grant([1,2]);s.Play();var peerElaiBefore=s.E<HappyGuardElaiChoiceEvent>().Count(e=>e.OwnerSeat==1);
        var deathTargetHp=s.V(2).Hp;var peerDamageBefore=s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==1&&e.TargetSeat==2&&e.Amount==1);
        s.Use("lose-one");
        s.Reach(()=>s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w=>w.HappyGuardRemoval is{DeathParent:true}));
        var death=s.G.ResolutionStack.OfType<DeathFrame>().Single(d=>d.VictimSeat==0);
        s.Need(!s.V(0).IsAlive&&death.HappyGuardMarkerCleanupCompleted&&death.HappyGuardRemovals?.Count==2&&death.HappyGuardRemovalIndex==0&&
            s.Guards(1)==0&&s.Guards(2)==0,
            "Genuine owner death freezes both real guard-removal facts on that death frame before the surviving Elai observer resolves them.");
        s.Cold();s.Reach(()=>s.E<DyingResolvedEvent>().Any(e=>e.VictimSeat==0&&!e.Survived)&&!s.G.ResolutionStack.OfType<DeathFrame>().Any(d=>d.Id==death.Id));
        s.Need(s.E<HappyGuardElaiChoiceEvent>().Count(e=>e.OwnerSeat==1&&e.RemovalId==death.Id&&e.Mode=="damage"&&e.TargetSeat==2)==2&&
            s.E<HappyGuardElaiChoiceEvent>().Count(e=>e.OwnerSeat==1)==peerElaiBefore+2&&
            s.V(2).Hp==deathTargetHp-2&&s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==1&&e.TargetSeat==2&&e.Amount==1)==peerDamageBefore+2&&
            s.E<PlayerMarkerChangedEvent>().Count(e=>e.ResolutionId==death.Id&&e.Marker==PlayerMarkerKind.HappyGuard&&e.SkillOwnerSeat==0&&e.Delta==-1&&e.Reason=="program.attributed-marker.death-clear")==2,
            "Restoring the first death-removal window processes each original guard once, lets the surviving observer cause two real native damage events and returns to the same original death without repeating marker cleanup.");
        s.Replay();
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public int Guards(int seat)=>V(seat).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.HappyGuard)?.Count??0;
        public void Use(string id,IReadOnlyList<int>? targets=null)=>Accept(new UseProgramSkillCommand(0,Driver,id,[],targets??[],G.Revision,P!.PromptId));
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Grant(IReadOnlyList<int> seats)
        {
            Reach(()=>P?.SkillPrompt?.SkillId==Shenwei&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
            Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            Reach(()=>P?.SkillPrompt?.SkillId==Shenwei&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="grant"));
            Answer(c=>c.Parameters.GetValueOrDefault("mode")=="grant"&&c.Targets.SequenceEqual(seats));
        }
        public void ElaiPrompt()=>Reach(()=>P is{PlayerSeat:0}&&P.SkillPrompt?.SkillId==Elai&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="none"));
        public void GuardPrompt()=>Reach(()=>P is{PlayerSeat:0}&&P.SkillPrompt?.SkillId==Shenwei&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")=="protect"));
        public void Answer(Func<PromptChoice,bool> match){var p=P!;Need(p.PlayerSeat==0,"The fixture answers only the real human controller.");Accept(new AnswerPromptCommand(0,p.PromptId,p.Choices.First(match).Id,G.Revision));}
        public void Reach(Func<bool> ready)
        {
            for(var i=0;i<180;i++)
            {
                if(ready())return;
                if(P is{PlayerSeat:0}p)throw new InvalidOperationException("Unexpected Happy Dian Wei boundary "+JsonSerializer.Serialize(p));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Dian Wei missed its bounded native boundary.");
        }
        private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),
            G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
        public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"The exact real guard cost, original damage frame and active native child return restore identically.");}
        public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"Replay preserves original guard payments, native Elai children, damage redirections and the death-owned removal queue.");}
        private void Accept(GameCommand c){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Dian Wei command.");}
        public void Need(bool ok,string text){if(!ok)throw new InvalidOperationException(text+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Facts=G.Events.TakeLast(7).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(41,1,"happy-dian-wei",Hero,5,0,0,0,0,[Driver],[],[],
                [new(Peer){StartingHand=[]},new(Peer){StartingHand=[]}],
                [new(Peer){StartingHand=[],ExtraSkillIds=[Elai]}],new string('d',64)){DefaultGodFactionId="wei"};
            var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
                ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,MaxTurns=16},registry);
            var s=new Scene(game,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-dian-wei",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hurt-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"hurt-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"recover-four","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":4}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{{Driver,new{name=Driver,description="原生伤害、体力及回复驱动"}},{Quiet,new{name=Quiet,description="保留真实回合的安静角色"}}};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Peer,"真实高体力角色","",Quiet,"wei",10));
            b.AddDeck(new(Deck,"无额外摸牌的小局",0,0,[]){PhysicalCards=Enumerable.Range(0,12).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐神典韦主流小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
