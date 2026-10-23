using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenGuanYuChecks
{
    private const string Hero="happy:shen-guan-yu",Wushen="happy:wushen",Wuhun="happy:wuhun",Driver="fixture:happy-gy-driver",
        Quiet="fixture:happy-gy-quiet",Peer="fixture:happy-gy-peer",Deck="fixture:happy-gy-deck";
    public static void VoluntaryHeartSlashAndSurvivedDeathFiveHpJudgmentsCold()
    {
        var s=Scene.Start();s.Play();s.Need(s.V(0) is{Hp:5,MaxHp:5},"The Happy general starts at the actual five printed HP.");
        s.Use("self-wound");s.Play();
        var peach=s.V(0).Hand.Single(c=>c.Kind==CardKind.Peach).Id;
        var heart=s.V(0).Hand.Single(c=>c.Kind==CardKind.Dodge).Id;
        var equipment=s.V(0).Equipment.Single(c=>c.Kind==CardKind.BaguaFormation).Id;
        s.Need(s.Actions.Any(a=>a.CardId==peach&&a.Kind==LegalActionKind.Peach)&&
            s.Actions.Any(a=>a.CardId==peach&&a.Kind==LegalActionKind.Slash&&a.ConversionSource?.SkillId==Wushen)&&
            !s.Actions.Any(a=>a.CardId==equipment&&a.Kind==LegalActionKind.Slash),
            "The heart Peach retains its native heal action alongside voluntary Wushen, while red-heart equipment is excluded from the hand-only conversion.");
        var responseSlash=s.V(0).Hand.Single(c=>c.Kind==CardKind.Slash).Id;
        var realDodge=s.V(3).Hand.Single(c=>c.Kind==CardKind.Dodge).Id;var responseHp=s.V(3).Hp;
        s.Card(s.Actions.Single(a=>a.CardId==responseSlash&&a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([3])));s.Play();
        var responseRequested=s.E<ResponseRequestedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.IncomingCard==CardKind.Slash&&e.RequiredCardKind==CardKind.Dodge);
        var responsePaid=s.E<CardRespondedEvent>().Count(e=>e.SourceSeat==0&&e.ResponderSeat==3&&e.CardId==realDodge&&e.EffectiveCardKind==CardKind.Dodge);
        var dodgeMoved=s.G.CardMovements.Any(m=>m.CardId==realDodge&&m.From==CardLocation.Hand(3)&&m.To==CardLocation.Processing);
        s.Need(s.V(3).Hp==responseHp&&responseRequested==1&&responsePaid==1&&dodgeMoved,
            "The heart Slash issues a normal Dodge request and is actually canceled by the other character's real paid Dodge. "+JsonSerializer.Serialize(new{responseHp,After=s.V(3).Hp,responseRequested,responsePaid,dodgeMoved}));
        var hp=s.V(2).Hp;var slash=s.Actions.Single(a=>a.CardId==heart&&a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2]));
        s.Card(slash);s.Play();
        var damageCount=s.E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==2);
        var declared=s.E<CardUseDeclaredEvent>().Any(e=>e.CardId==heart&&e.CardKind==CardKind.Slash&&e.SourceSeat==0);
        var moved=s.G.CardMovements.Any(m=>m.CardId==heart&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing);
        s.Need(s.V(2).Hp==hp-2&&damageCount==1&&declared&&moved,
            "The real heart hand entity becomes a native Slash against the nonadjacent seat and deals exactly two damage. "+JsonSerializer.Serialize(new{hp,After=s.V(2).Hp,damageCount,declared,moved}));

        var duel=s.V(0).Hand.Single(c=>c.Kind==CardKind.Duel).Id;
        s.Card(s.Actions.Single(a=>a.CardId==duel&&a.TargetSeats.SequenceEqual([2])));
        s.Reach(()=>s.P is{Kind:DecisionKind.RespondSlash,PlayerSeat:0});s.Answer(c=>c.Cards.Count==0);s.Play();
        s.Need(s.V(0).Hp==3&&s.Nightmares(0)==1&&s.Nightmares(2)==1&&
            s.E<DuelResponseEvent>().Any(e=>e.ResponderSeat==2&&e.UsedSlash)&&
            s.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==2&&e.TargetSeat==0&&e.Amount==1),
            "An actual other-source Duel wound gives exactly one attributed nightmare to its real source, while the earlier self-wound's own mark remains separate.");
        s.Use("enter-dying");s.Reach(()=>s.P is{Kind:DecisionKind.RescueDying,PlayerSeat:0});
        s.Answer(c=>c.Parameters.GetValueOrDefault("response")=="peach"&&c.Cards.SequenceEqual([peach]));s.NightmareChoice();
        var exited=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Wuhun&&f.TriggerId=="survived-nightmare");
        s.Need(s.V(0).Hp==1&&s.E<DyingResolvedEvent>().Any(e=>e.VictimSeat==0&&e.Survived)&&
            s.P!.Choices.Where(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target").All(c=>c.Targets.SequenceEqual([2]))&&
            s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f=>f.Id==exited.WindowContext!.ParentFrameId&&f.YanPuDyingExit is not null),
            "Surviving the genuine dying entry immediately creates Wuhun's typed exit window; the maximum-other target excludes Guan Yu even when his own mark is tied.");
        var exitId=exited.Id;s.Cold();s.Answer(c=>c.Targets.SequenceEqual([2]));s.Play();
        s.Need(s.V(2).IsAlive&&s.V(2).Hp==hp-7&&s.Nightmares(2)==1&&
            s.E<JudgmentResolvedEvent>().Count(e=>e.ParentResolutionId==exitId&&e.TargetSeat==2&&e.Reason=="skill.happy-wuhun"&&e.CardKind==CardKind.Slash)==1&&
            s.E<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==exitId&&e.TargetSeat==2&&e.Amount==5)==1,
            "The failed native judgment loses exactly five HP once after cold restoration, keeps its surviving target alive and preserves nightmare marks after surviving dying.");

        s.Use("heal-target",[2]);s.Play();var beforeDeath=s.V(2).Hp;
        s.Use("last-hp");s.NightmareChoice();
        var died=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Wuhun&&f.TriggerId=="death-nightmare");var deathId=died.Id;
        s.Need(!s.V(0).IsAlive&&s.Nightmares(2)==1&&s.P!.Choices.Any(c=>c.Targets.SequenceEqual([2])),
            "A later genuine death invokes Wuhun again with the still-retained nightmare, rather than requiring a new injury or clearing the previous mark.");
        s.Answer(c=>c.Targets.SequenceEqual([2]));s.Reach(()=>s.E<ProgramBindingResolvedEvent>().Any(e=>e.FrameId==deathId&&e.Completed));
        var finalTarget=s.V(2);var finalJudgments=s.E<JudgmentResolvedEvent>().Count(e=>e.Reason=="skill.happy-wuhun");
        var deathHpLoss=s.E<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==deathId&&e.TargetSeat==2&&e.Amount==5);
        s.Need(finalTarget.IsAlive&&finalTarget.Hp==beforeDeath-5&&finalJudgments==2&&deathHpLoss==1,
            "The dead owner's second native judgment also pays five HP, allowing the chosen character to survive normally and preserving the original death continuation. "+
            JsonSerializer.Serialize(new{beforeDeath,After=finalTarget.Hp,finalTarget.IsAlive,finalJudgments,deathHpLoss,Nightmares=s.Nightmares(2)}));
        var deathMarkerCleanups=s.E<PlayerMarkerChangedEvent>().Count(e=>e.SkillOwnerSeat==0&&e.PlayerSeat==2&&e.Marker==PlayerMarkerKind.Nightmare&&
            e.Delta==-1&&e.Count==0&&e.Reason=="program.attributed-marker.death-clear");
        s.Need(s.Nightmares(2)==0&&deathMarkerCleanups==1,
            "After both skill effects, the common owner-death cleanup removes exactly the original owner's attributed nightmare. "+
            JsonSerializer.Serialize(new{Nightmares=s.Nightmares(2),deathMarkerCleanups}));
        s.Replay();
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public IReadOnlyList<LegalAction> Actions=>G.GetHumanLegalActions();
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public int Nightmares(int seat)=>V(seat).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.Nightmare)?.Count??0;
        public void Use(string id,IReadOnlyList<int>? targets=null)=>Accept(new UseProgramSkillCommand(0,Driver,id,[],targets??[],G.Revision,P!.PromptId));
        public void Card(LegalAction a)=>Accept(new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources});
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void NightmareChoice()=>Reach(()=>P?.SkillPrompt?.SkillId==Wuhun&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        public void Answer(Func<PromptChoice,bool> match){var p=P!;Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(match).Id,G.Revision));}
        public void Reach(Func<bool> ready)
        {
            for(var i=0;i<140;i++)
            {
                if(ready())return;
                if(P is{PlayerSeat:0}p)throw new InvalidOperationException("Unexpected Happy Guan Yu boundary "+JsonSerializer.Serialize(p));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Guan Yu missed its bounded native boundary.");
        }
        private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),
            G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
        public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"The actual survived-Dying return, current target menu and original native HP parent restore identically.");}
        public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"Replay preserves optional heart conversions, both real nightmare judgments and exact five-HP losses.");}
        private void Accept(GameCommand c){var result=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Happy Guan Yu command.");}
        public void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Facts=G.Events.TakeLast(6).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(37,1,"happy-guan-yu",Hero,5,0,0,0,0,[Driver],
                [new(CardKind.Peach,Suit.Heart,3),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Duel,Suit.Diamond,1),new(CardKind.Slash,Suit.Heart,4)],
                [new(CardKind.BaguaFormation,Suit.Heart,2),new(CardKind.Crossbow,Suit.Club,1)],
                [new(Peer){StartingHand=[new(CardKind.Slash,Suit.Club,8)]},new(Peer){StartingHand=[new(CardKind.Dodge,Suit.Diamond,9)]}],
                [new(Peer){StartingHand=[]}],new string('c',64)){DefaultGodFactionId="shu"};
            var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
                ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,MaxTurns=4},registry);
            var s=new Scene(game,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-guan-yu",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"self-wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"enter-dying","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]},
                {"id":"last-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"heal-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":7}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{{Driver,new{name=Driver,description="原生伤害、体力及回复驱动"}},{Quiet,new{name=Quiet,description="保留真实回合的安静角色"}}};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Peer,"真实高体力角色","",Quiet,"wei",10));
            b.AddDeck(new(Deck,"固定失败判定实体",0,0,[]){PhysicalCards=Enumerable.Range(0,48).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐神关羽主流小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
