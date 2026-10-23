using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class HappyShenZhouYuChecks
{
    private const string Hero="happy:shen-zhou-yu",Qinyin="happy:qinyin",Yeyan="happy:yeyan",Peer="fixture:happy-zy-peer",Deck="fixture:happy-zy-deck",
        NoDraw="fixture:happy-zy-no-draw",Quiet="fixture:happy-zy-quiet",Driver="fixture:happy-zy-discard",DrawHold="fixture:happy-zy-draw",HpHold="fixture:happy-zy-hp";
    public static void WholeTurnDiscardThreeMelodiesAndRecurringStartFireResumeCold()
    {
        foreach(var mode in new[]{"heal","lose","draw"})
        {
            var s=Scene.Start();s.Fire(1);s.Play();
            s.Need(s.V(1).Hp==2&&s.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.Amount==1&&e.Nature==DamageNature.Fire),"The first actual Play start uses the one-fire native Yeyan without a limited payment.");
            var card=s.V(0).Hand.Single(c=>c.Kind==CardKind.Slash).Id;
            s.Accept(new UseProgramSkillCommand(0,Driver,"discard-one",[card],[],s.G.Revision,s.P!.PromptId));s.Play();
            s.Need(s.V(0).HandCount==1&&s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e=>e.ExecutorSeat==0&&e.HandCount==1&&e.Reason.Value=="skill-program.fixture:happy-zy-discard.DiscardSelected"),"The qualification is a real one-card hand discard during Play, not the response/use of a card.");
            s.EndPlay();s.Reach(()=>s.P?.SkillPrompt?.SkillId==Qinyin&&s.P.Choices.Any(c=>Action(c)=="activate"));
            var ending=s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Last();
            s.Need(ending.Window==SkillProgramTriggerWindow.DiscardPhaseEnded&&ending.Facts.TurnOwnerDiscardPhaseHandDiscardCount==0&&!s.E<HandLimitDiscardedEvent>().Any(),"The actual discard ending qualifies from the whole turn while zero cards were discarded in this Discard phase.");
            s.Answer(c=>Action(c)=="activate");s.Reach(()=>s.P?.Choices.Any(c=>c.Parameters.GetValueOrDefault("mode")==mode)==true);s.Answer(c=>c.Parameters.GetValueOrDefault("mode")==mode);
            s.Reach(()=>s.P?.SkillPrompt?.SkillId==(mode=="draw"?DrawHold:HpHold));
            var root=s.Root!;
            s.Need(root.HappyZhouYu is{Cursor:1,Target:0}&&root.HappyZhouYu.Mode==mode&&root.HappyZhouYu.Seats.SequenceEqual([0,1,2,3])&&
                (mode=="draw"?root.HappyZhouYu.Stage==HappyZyStage.Drawing&&s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f=>f.Context.ParentFrameId==root.Id&&f.Context.RequestedCount==1):root.HappyZhouYu.Stage==HappyZyStage.Children),"The chosen melody freezes the actual living ring and pauses on the first recipient's real native draw or HP child after advancing only its original issued cursor.");
            if(mode!="lose")s.Cold();s.Answer(c=>Action(c)=="skip");
            s.Reach(()=>s.E<HappyZyCompletedEvent>().Any(e=>e.FrameId==root.Id));
            var expected=mode switch{"heal"=>new[]{4,3,4,4},"lose"=>[2,1,2,2],_=>[3,2,3,3]};
            s.Need(Enumerable.Range(0,4).Select(i=>s.V(i).Hp).SequenceEqual(expected)&&s.E<HappyZyIssuedEvent>().Where(e=>e.FrameId==root.Id).Select(e=>e.Target).SequenceEqual([0,1,2,3])&&
                s.E<HappyZyModeEvent>().Count(e=>e.FrameId==root.Id&&e.Mode==mode)==1&&s.E<HappyZyCompletedEvent>().Count(e=>e.FrameId==root.Id)==1,
                "Each accepted whole-field melody settles its native effect once per frozen living participant, including the owner, and retains the chosen mode across child return.");
            if(mode=="draw")s.Need(s.E<HappyZyDrawReturnedEvent>().Count(e=>e.FrameId==root.Id&&e.ActualCount==1)==4&&Enumerable.Range(0,4).Select(i=>s.V(i).HandCount).SequenceEqual([2,1,1,1]),"The new draw branch uses four independent real one-card producers, without repeating the first paid draw after cold restore.");
            if(mode=="lose")s.Need(s.E<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==root.Id&&e.Amount==1)==4&&s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0)==1,"Qinyin loses actual HP and creates no damage event.");
            s.Fire(3);s.Play();s.Need(s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.Amount==1&&e.Nature==DamageNature.Fire)==2,"A later actual Play start offers a new native Yeyan against another legal target; there is no old limited-game usage.");
            s.EndPlay();s.Reach(()=>s.E<TurnEndedEvent>().Count(e=>e.ActorSeat==0)==2);
            s.Need(s.E<HappyZyStartedEvent>().Length==1,"The next actual turn's zero-discard ending cannot reuse the preceding turn's qualifying hand discard.");
        }
    }
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-zhou-yu",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"discard-one","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerPhase":1,"usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{HpHold}}","revision":1,"triggers":[{"id":"hp","isSkillInvocation":false,"window":"afterHealthChanged","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
            ]}
            """;
            var labels=new[]{NoDraw,Quiet,Driver,DrawHold,HpHold}.ToDictionary(id=>id,id=>new{name=id,description="琴音全回合弃牌资格与真实逐人原生子流程"});
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Peer,"保留真实回合的角色","",Quiet,"wei",4));
            b.AddDeck(new(Deck,"固定实体牌",0,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐琴音小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public ProgramSkillFrame? Root=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.HappyZhouYu is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public void Fire(int target){Reach(()=>P?.SkillPrompt?.SkillId==Yeyan&&P.Choices.Any(c=>Action(c)=="activate"));Answer(c=>Action(c)=="activate");Reach(()=>P?.SkillPrompt?.SkillId==Yeyan&&P.Choices.Any(c=>c.Targets.SequenceEqual([target])));Answer(c=>c.Targets.SequenceEqual([target]));}
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void EndPlay()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"Missing exact current Happy Zhou Yu choice.");Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,G.Revision));}
        public void Reach(Func<bool> done)
        {
            for(var n=0;n<180;n++)
            {
                if(done())return;
                if(P is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId is DrawHold or HpHold)Answer(c=>Action(c)=="skip");
                else if(P is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Happy Zhou Yu boundary "+JsonSerializer.Serialize(new{P,Root=Root?.HappyZhouYu}));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Zhou Yu missed the bounded actual flow "+SnapshotJson.Serialize(G.CreateSnapshot(0)));
        }
        public void Cold()
        {
            var views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();var frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray();
            G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);
            Need(views.SequenceEqual(Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))))&&frames.SequenceEqual(G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType()))),"Cold retains the actual discard source, chosen mode, frozen ring, current participant and typed native child.");
        }
        public void Accept(GameCommand c){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Zhou Yu command.");}
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Root=Root?.HappyZhouYu,Modes=E<HappyZyModeEvent>(),Issued=E<HappyZyIssuedEvent>()}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var peer=new ShanheCombatantDefinition(Peer,Hp:3,ExtraSkillIds:[NoDraw]){StartingHand=[]};
            var setup=new ShanheBattleSetup(37,1,"happy-zhou-yu",Hero,3,0,0,0,0,[NoDraw,Driver,DrawHold,HpHold],[new(CardKind.Slash,Suit.Heart,7),new(CardKind.Dodge,Suit.Heart,2)],[],[peer,peer],[peer],new string('f',64)){DefaultGodFactionId="wei"};
            var g=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
            var s=new Scene(g,registry);s.Accept(new StartGameCommand());return s;
        }
    }
}
