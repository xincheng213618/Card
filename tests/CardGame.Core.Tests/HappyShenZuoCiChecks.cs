using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class HappyShenZuoCiChecks
{
    private const string Hero="happy:shen-zuo-ci",Transform="happy:huanhua",Limited="happy:huanjing",Driver="fixture:happy-zc-driver",Watch="fixture:happy-zc-draw",Quiet="fixture:happy-zc-quiet",Peer="fixture:happy-zc-peer",Deck="fixture:happy-zc-deck";
    public static void RealPersistentPhantomsNativeUseForeignGainAndOverflowCold()
    {
        var s=Scene.Start();s.Play();var init=s.E<HappyZuoCiPoolInitializedEvent>().Single();
        var phantoms=s.V(0).Hand.Where(c=>c.SkillMarkLabel=="幻术").OrderBy(c=>c.Id).ToArray();
        s.Need(phantoms.Length==2&&init.Count==72&&s.G.CreateCardZoneDiagnostics().Count(c=>c.Location.Zone==CardZoneKind.PrivatePersistentPile&&c.Location.OwnerSeat==0)==70,
            "The current real draw pile is copied into distinct private physical entities, and the first actual round deals two phantoms.");
        var a=phantoms[0].Id;var b=phantoms[1].Id;var drawTemplate=s.V(0).Hand.Single(c=>c.SkillMarkLabel is null&&c.Kind==CardKind.DrawTwo&&c.Suit==Suit.Heart).Id;
        s.Transform(a,drawTemplate,true);s.Play();
        s.Need(s.V(0).Hand.Single(c=>c.Id==a) is{Kind:CardKind.DrawTwo,Suit:Suit.Heart,Rank:5,SkillMarkLabel:"幻术"},"A completed transform persistently changes the same positive entity's actual name, suit and rank.");
        var slashTemplate=s.V(0).Hand.Single(c=>c.SkillMarkLabel is null&&c.Kind==CardKind.Slash&&c.Suit==Suit.Club).Id;
        s.Transform(b,slashTemplate);s.Play();
        s.Need(s.E<HappyZuoCiTransformedEvent>().Length==2&&s.E<HappyZuoCiTransformedEvent>().Last().SameSuit&&s.V(0).Hand.Count(c=>c.SkillMarkLabel=="幻术")==3&&
            !s.G.GetHumanLegalActions().Any(x=>x.ProgramSkillId==Transform),"The second same-suit transform really gains one phantom and consumes the base two-use actual-turn allowance.");

        var hand=s.V(0).HandCount;var action=s.G.GetHumanLegalActions().Single(x=>x.CardId==a&&x.Kind==LegalActionKind.DrawTwo);
        s.Accept(new PlayCardCommand(0,a,action.TargetSeats,s.G.Revision,s.P!.PromptId,action.PlayedCardKind,action.TargetCardId){ConversionSource=action.ConversionSource});s.Play();
        var used=s.G.CardMovements.Single(m=>m.CardId==a&&m.From==CardLocation.Processing&&m.To==CardLocation.OutsideGame);
        s.Need(used.HappyZuoCiPhantomDestination is{Face:{Kind:CardKind.DrawTwo,Suit:Suit.Heart,Rank:5}}&&s.V(0).HandCount==hand+1&&
            s.E<CardUseDeclaredEvent>().Any(e=>e.CardId==a&&e.CardKind==CardKind.DrawTwo),"The transformed real card follows native DrawTwo, draws two ordinary cards and finishes in its real OutsideGame phantom-discard subset.");

        var peerHand=s.V(2).HandCount;var draws=s.E<HappyZuoCiDrawReturnedEvent>().Length;hand=s.V(0).HandCount;
        s.Skill(Driver,"give",[b],[2]);s.Play();
        var gain=s.G.CardMovements.Single(m=>m.CardId==b&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(2));
        var destroy=s.G.CardMovements.Single(m=>m.CardId==b&&m.From==CardLocation.Hand(2)&&m.To==CardLocation.OutsideGame);
        s.Need(gain.Sequence<destroy.Sequence&&destroy.HappyZuoCiPhantomDestination is{}plan&&plan.OriginalForeignGainSequence==gain.Sequence&&plan.OriginalRecipient==2&&
            s.V(2).HandCount==peerHand&&s.V(0).HandCount==hand&&s.E<HappyZuoCiDrawReturnedEvent>().Length==draws+1&&s.E<HappyZuoCiDrawReturnedEvent>().Last() is{Requested:1,Actual:1},
            "The foreign player really gains the entity first, then a separate real destruction refers to that original gain; the original issuer draws exactly once.");
        var remaining=s.V(0).Hand.Single(c=>c.SkillMarkLabel=="幻术").Id;
        s.Skill(Driver,"hurt");s.Play();
        s.Need(s.V(0).Hp==1&&s.V(0).Hand.Count(c=>c.SkillMarkLabel=="幻术")==5&&s.E<HappyZuoCiGainIssuedEvent>().Count(e=>e.Requested==2)==3,
            "One real two-point self damage grants two phantoms for each point, independently of its single damage event.");
        hand=s.V(0).HandCount;s.Skill(Limited,"limited-phantoms");
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch&&s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f=>f.Context.RequestedCount==3));
        var paid=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Limited);
        s.Need(paid.HappyZuoCi is{Stage:HappyZuoCiStage.Drawing,Requested:4,Overflow:3,GainedIds.Count:1}&&s.V(0).HandCount==hand+1&&
            s.E<HappyZuoCiHuanjingIssuedEvent>().Single().Amount==4,"Huanjing pays its real once-per-game gain, fills the sixth phantom slot and pauses before the remaining three native draws.");
        s.Cold();s.Answer(_=>true);s.Play();
        s.Need(s.V(0).HandCount==hand+4&&s.V(0).Hand.Count(c=>c.SkillMarkLabel=="幻术")==6&&s.E<HappyZuoCiDrawReturnedEvent>().Last() is{Requested:3,Actual:3}&&
            !s.G.GetHumanLegalActions().Any(x=>x.ProgramSkillId==Limited),"Cold resumes the same paid limited gain and three ordinary draws, without another limited activation or phantom.");
        var phantomTemplate=s.V(0).Hand.First(c=>c.SkillMarkLabel=="幻术"&&c.Id!=remaining).Id;
        var beforeCount=s.V(0).HandCount;s.Transform(remaining,phantomTemplate);s.Play();
        var stable=s.V(0).Hand.Single(c=>c.Id==remaining);
        s.Need(s.E<HappyZuoCiTransformedEvent>().Length==3&&s.V(0).Hand.Count(c=>c.SkillMarkLabel=="幻术")==6&&s.V(0).HandCount==beforeCount+1&&
            s.E<HappyZuoCiDrawReturnedEvent>().Last() is{Requested:1,Actual:1},"The limited turn grants real additional transform uses and allows a phantom template; same-suit overflow draws one while the six-phantom cap stays exact.");
        s.End();s.Play();
        s.Need(s.V(0).Hand.Single(c=>c.Id==remaining) is{}after&&after.Kind==stable.Kind&&after.Suit==stable.Suit&&after.Rank==stable.Rank&&
            s.E<HappyZuoCiGainIssuedEvent>().Count(e=>e.Requested==2)==4,"At the next actual round the old manually transformed face is still protected from random refresh, and the round gain occurs once.");
        var first=s.V(0).Hand.First(c=>c.SkillMarkLabel=="幻术"&&c.Id!=remaining&&c.Id!=phantomTemplate).Id;
        s.Skill(Transform,"transform-phantom");s.Answer(c=>c.Cards.SequenceEqual([first]));
        s.Need(!s.P!.Choices.Any(c=>c.Cards.Contains(phantomTemplate))&&!s.P.Choices.Any(c=>c.Cards.Contains(drawTemplate))&&!s.P.Choices.Any(c=>c.Cards.Contains(slashTemplate)),
            "The previous actual turn's phantom-template permission expires, and the same real ordinary templates remain unavailable after returning to Play.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="cancel");s.Play();s.Replay();
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public void Skill(string id,string activation,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>Accept(new UseProgramSkillCommand(0,id,activation,cards??[],targets??[],G.Revision,P!.PromptId));
        public void Answer(Func<PromptChoice,bool> choose){var p=P!;Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,G.Revision));}
        public void Transform(int phantom,int template,bool cold=false){Skill(HappyShenZuoCiChecks.Transform,"transform-phantom");Answer(c=>c.Cards.SequenceEqual([phantom]));if(cold)Cold();Answer(c=>c.Cards.SequenceEqual([template]));}
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Reach(Func<bool> ready)
        {
            for(var n=0;n<180;n++)
            {
                if(ready())return;
                if(P is{PlayerSeat:0}p)
                {
                    if(p.SkillPrompt?.SkillId==Watch){Answer(_=>true);continue;}
                    if(p.Kind==DecisionKind.SelectFaction){Answer(_=>true);continue;}
                    throw new InvalidOperationException("Unexpected Happy Zuo Ci boundary "+JsonSerializer.Serialize(p));
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Zuo Ci missed its bounded native boundary.");
        }
        public void Accept(GameCommand command){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Zuo Ci command.");}
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Last=G.Events.TakeLast(6).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));}
        private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
        public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(State()==before,"Cold preserves physical face/deck identities, the exact private selection and the once-paid native overflow return.");}
        public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(State()==before,"Replay preserves all real phantom generation, native use, acquire-then-destroy movements and persistent transforms.");}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            ShanheStartingCard[] hand=[new(CardKind.DrawTwo,Suit.Heart,5),new(CardKind.Slash,Suit.Club,7),new(CardKind.Peach,Suit.Diamond,2),new(CardKind.Dodge,Suit.Spade,4)];
            var setup=new ShanheBattleSetup(37,1,"happy-zuo-ci",Hero,3,0,0,0,0,[Driver,Watch],hand,[],[new(Peer){StartingHand=[]},new(Peer){StartingHand=[]}],[new(Peer){StartingHand=[]}],new string('f',64)){DefaultGodFactionId="qun"};
            var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,MaxTurns=5},registry);
            var s=new Scene(game,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-zuo-ci",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"give","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{{Driver,new{name=Driver,description="真实手牌给出和两点伤害"}},{Quiet,new{name=Quiet,description="安静实际回合"}},{Watch,new{name=Watch,description="原生摸牌前保留原付款",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}};
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in c.Programs)b.AddSkill(new(id,id,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id]});
            b.AddGeneral(new(Peer,"安静角色","",Quiet,"wei",10));
            b.AddDeck(new(Deck,"同花色真实幻术原型",0,0,[]){PhysicalCards=Enumerable.Range(0,72).Select(n=>new ContentDeckPhysicalCard(n%2==0?"standard:slash":"standard:draw_two",Suit.Club,n%13+1)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐左慈真实实体小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
