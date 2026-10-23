using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenDiaoChanChecks
{
    private const string Hero="happy:shen-diao-chan",Meihun="happy:meihun",Huoxin="happy:huoxin",Driver="fixture:happy-dc-driver",Quiet="fixture:happy-dc-quiet",Counter="fixture:happy-dc-counter",Watch="fixture:happy-dc-watch",Peer="fixture:happy-dc-peer",Deck="fixture:happy-dc-deck";
    public static void NativeTiedContestSuitBanOwnEndingAndWholeSuitPrivateTakeCold()
    {
        var s=Scene.Start();s.Play();s.Need(s.V(0) is{Hp:3,MaxHp:3},"Happy Diao Chan starts at the real three HP.");
        s.Accept(new UseProgramSkillCommand(0,Driver,"equip",[],[1],s.G.Revision,s.P!.PromptId));s.Reach(()=>s.P!.Choices.Any(c=>c.Cards.Any(id=>s.V(0).Hand.Any(x=>x.Id==id&&x.Kind==CardKind.BaguaFormation))));
        s.Answer(c=>c.Cards.Any(id=>s.V(0).Hand.Any(x=>x.Id==id&&x.Kind==CardKind.BaguaFormation)));s.Play();
        var cost=s.V(0).Equipment.Single().Id;var ownContest=s.V(0).Hand.Single(c=>c.Suit==Suit.Spade).Id;
        var heartDodge=s.V(0).Hand.Single(c=>c.Kind==CardKind.Dodge&&c.Suit==Suit.Heart&&c.Rank==2).Id;var heartSlash=s.V(0).Hand.Single(c=>c.Kind==CardKind.Slash&&c.Suit==Suit.Heart&&c.Rank==4).Id;
        var clubDodge=s.V(0).Hand.Single(c=>c.Kind==CardKind.Dodge&&c.Suit==Suit.Club&&c.Rank==8).Id;
        s.Accept(new UseProgramSkillCommand(0,Huoxin,"charm-contest",[],[],s.G.Revision,s.P!.PromptId));s.Menu(HappyDiaoChanStage.Cost);s.Cold();
        s.Answer(c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Equipment));s.Hold();
        var paid=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.HappyDiaoChan is{Stage:HappyDiaoChanStage.CostChildren});
        s.Need(paid.HappyDiaoChan!.Cost?.CardId==cost&&s.G.CardMovements.Count(m=>m.CardId==cost&&m.From==CardLocation.Equipment(0)&&m.Reason.Value=="skill-program.happy:huoxin.cost")==1,
            "Huoxin pays exactly one genuine owned equipment discard before its native movement observer.");s.Cold();s.Continue();
        s.Menu(HappyDiaoChanStage.Pair);s.Answer(c=>c.Targets.SequenceEqual([0,2]));
        s.Reach(()=>s.P?.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="pindian-source-card")==true);
        s.Answer(c=>c.Cards.SequenceEqual([ownContest]));s.Menu(HappyDiaoChanStage.HuoxinSuit);
        var contest=s.E<PindianResultDeterminedEvent>().Single(e=>e.SkillId==Huoxin);
        s.Need(contest.Result is{SourceSeat:0,OpponentSeat:2,SourceRank:5,OpponentRank:5}&&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.HappyDiaoChan is{Stage:HappyDiaoChanStage.HuoxinSuit}).HappyDiaoChan!.Nonwinners.SequenceEqual([0,2]),
            "Both real native rank-five cards finish the contest and both tied participants are nonwinners before one unified suit declaration.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("value")==nameof(Suit.Heart));s.Menu(HappyDiaoChanStage.Nonwinner);s.Cold();
        s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="charm");s.Play();
        s.Need(s.E<HappyDiaoChanDeclaredEvent>().Count(e=>e.FrameId==paid.Id&&e.Suit==Suit.Heart)==1&&
            s.E<HappyDiaoChanNonwinnerChosenEvent>().Where(e=>e.FrameId==paid.Id).Select(e=>e.Seat).SequenceEqual([0,2])&&
            s.Charmed(0)&&s.Charmed(2)&&!s.G.GetHumanLegalActions().Any(a=>a.CardId==heartSlash&&a.Kind==LegalActionKind.Slash)&&
            s.G.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.DrawTwo),
            "Each tied participant independently chooses its own charm; the actual heart Slash is forbidden while another suit remains playable.");
        s.End();s.Offer(Meihun,"first-slash-target");var firstSlashSkip=s.P!.Choices.Single(c=>c.Parameters.GetValueOrDefault("program-action")=="skip").Id;
        s.Answer(c=>c.Id==firstSlashSkip);
        for(var n=0;n<2;n++)
        {
            s.Reach(()=>s.P is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
            s.Need(!s.P!.ValidCardIds.Contains(heartDodge)&&!s.P.Choices.Any(c=>c.Cards.Contains(heartDodge))&&s.P.ValidCardIds.Contains(clubDodge)&&s.P.Choices.Any(c=>c.Cards.Contains(clubDodge)),"The still-active heart charm excludes the actual heart Dodge from a native pure response while the real club Dodge remains playable.");
            s.Answer(c=>c.Cards.Count==0);
        }
        s.Offer(Meihun,"owner-ended");
        var firstSlashAnswers=s.G.AcceptedCommands.OfType<AnswerPromptCommand>().Count(c=>c.ActorSeat==0&&c.Choice==firstSlashSkip);
        var responseCount=s.E<ResponseRequestedEvent>().Count(e=>e.SourceSeat==1&&e.TargetSeat==0&&e.RequiredCardKind==CardKind.Dodge);
        var expiredOwner=s.E<HappyDiaoChanCharmExpiredEvent>().Count(e=>e.TargetSeat==0);var expiredOther=s.E<HappyDiaoChanCharmExpiredEvent>().Count(e=>e.TargetSeat==2);
        s.Need(firstSlashAnswers==1&&responseCount==2&&!s.Charmed(0)&&s.Charmed(2)&&expiredOwner==1&&expiredOther==0,
            "Declining the actual turn's first Slash opportunity does not offer the second; only the actor's own ending clears its charm, leaving the other recipient's charm active. "+JsonSerializer.Serialize(new{firstSlashAnswers,responseCount,OwnerCharmed=s.Charmed(0),OtherCharmed=s.Charmed(2),expiredOwner,expiredOther}));
        var allHe=s.V(1).Hand.Where(c=>c.Suit==Suit.Heart).Select(c=>c.Id).Concat(s.V(1).Equipment.Where(c=>c.Suit==Suit.Heart).Select(c=>c.Id)).ToArray();
        s.Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");s.Menu(HappyDiaoChanStage.Target);s.Answer(c=>c.Targets.SequenceEqual([1]));
        s.Menu(HappyDiaoChanStage.MeihunSuit);s.Answer(c=>c.Parameters.GetValueOrDefault("value")==nameof(Suit.Heart));s.Hold();
        s.Need(allHe.Length==2&&allHe.All(id=>s.V(0).Hand.Any(c=>c.Id==id))&&s.G.CardMovements.Count(m=>allHe.Contains(m.CardId)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.happy:diao-chan.give")==2,
            "Meihun's declared suit really moves every matching hand and equipped entity in one paid operation.");s.Cold();s.Continue();
        s.Reach(()=>s.E<HappyDiaoChanCharmExpiredEvent>().Any(e=>e.TargetSeat==2));
        s.Need(!s.Charmed(2)&&s.E<TurnEndedEvent>().Any(e=>e.ActorSeat==2),"The other participant's charm ends at that participant's genuine actual ending.");
        s.Play();s.End();s.Offer(Meihun,"owner-ended");s.Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        s.Menu(HappyDiaoChanStage.Target);s.Answer(c=>c.Targets.SequenceEqual([3]));s.Menu(HappyDiaoChanStage.MeihunSuit);
        s.Answer(c=>c.Parameters.GetValueOrDefault("value")==nameof(Suit.Heart));s.Menu(HappyDiaoChanStage.ViewHand);
        var revealed=s.P!.Choices.SelectMany(c=>c.Cards).ToArray();var take=revealed.First();s.Cold();s.Answer(c=>c.Cards.SequenceEqual([take]));s.Hold();
        s.Need(revealed.Length==2&&s.V(0).Hand.Any(c=>c.Id==take)&&s.E<ProgramPrivateHandViewedEvent>().Count(e=>e.SkillId==Meihun&&e.ViewerSeat==0&&e.SourceSeat==3&&e.Count==2)==1&&
            s.G.CardMovements.Count(m=>m.CardId==take&&m.From==CardLocation.Hand(3)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.happy:diao-chan.take")==1,
            "Only a genuinely absent suit opens the original private hand list and obtains one real selected entity.");s.Cold();s.Continue();s.Play();
        var contestsBefore=s.E<PindianResultDeterminedEvent>().Count(e=>e.SkillId==Huoxin);
        s.Accept(new UseProgramSkillCommand(0,Huoxin,"charm-contest",[],[],s.G.Revision,s.P!.PromptId));s.Menu(HappyDiaoChanStage.Cost);
        s.Answer(c=>c.Parameters.GetValueOrDefault("source-zone")==nameof(CardZoneKind.Hand));s.Hold();s.Continue();s.Menu(HappyDiaoChanStage.Pair);
        s.Answer(c=>c.Targets.SequenceEqual([2,3]));s.Menu(HappyDiaoChanStage.HuoxinSuit);
        s.Need(s.E<PindianResultDeterminedEvent>().Count(e=>e.SkillId==Huoxin)==contestsBefore+1&&s.E<PindianResultDeterminedEvent>().Last(e=>e.SkillId==Huoxin).Result is{SourceSeat:2,OpponentSeat:3},
            "The skill also makes two genuine other characters choose and settle their own native Pindian cards before Diao Chan may declare.");
        var declarations=s.E<HappyDiaoChanDeclaredEvent>().Length;s.Answer(c=>c.Parameters.GetValueOrDefault("mode")=="decline");s.Play();
        s.Need(s.E<HappyDiaoChanDeclaredEvent>().Length==declarations,"Declining the one post-contest declaration does not issue a suit or nonwinner consequence.");s.Replay();
    }
    private sealed class Scene(GameEngine game,ContentRegistry registry)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public bool Charmed(int seat)=>(V(seat).Markers?.SingleOrDefault(m=>m.Kind==PlayerMarkerKind.HappyCharmHeart)?.Count??0)>0;
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void Menu(HappyDiaoChanStage stage)=>Reach(()=>P is{PlayerSeat:0}&&G.ResolutionStack.LastOrDefault() is ProgramSkillFrame{HappyDiaoChan:{}r}&&r.Stage==stage);
        public void Offer(string skill,string binding)=>Reach(()=>P?.SkillPrompt?.SkillId==skill&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")&&
            (G.ResolutionStack.LastOrDefault() switch{ProgramCardTriggerWindowFrame w=>w.Candidates[w.CandidateIndex].TriggerId==binding,DeferredTurnEndFrame=>binding=="owner-ended",_=>true}));
        public void Hold()=>Reach(()=>P?.SkillPrompt?.SkillId==Watch);
        public void Continue()=>Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Answer(Func<PromptChoice,bool> test){var p=P!;Need(p.PlayerSeat==0,"Only the true human answers this choice.");Accept(new AnswerPromptCommand(0,p.PromptId,p.Choices.First(test).Id,G.Revision));}
        public void Reach(Func<bool> done)
        {
            for(var n=0;n<220;n++)
            {
                if(done())return;
                if(P is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Happy Diao Chan native boundary "+JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Happy Diao Chan missed its bounded native boundary.");
        }
        private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),G.CardMovements,Commands=CommandJson.Serialize(G.AcceptedCommands)});
        public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"Cold preserves the exact native contest, private list, recipient choice and paid movement without repaying.");}
        public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"Replay retains tied nonwinners, one declared suit, each recipient's own actual expiry, and real private-hand/give payments.");}
        public void Accept(GameCommand c){var result=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected Happy Diao Chan command.");}
        public void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Facts=G.Events.TakeLast(8).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));}
        public static Scene Start()
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
            var setup=new ShanheBattleSetup(37,1,"happy-diao-chan",Hero,3,0,0,0,0,[Watch,Driver],
                [new(CardKind.Dodge,Suit.Spade,5),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Slash,Suit.Heart,4),new(CardKind.DrawTwo,Suit.Diamond,3),new(CardKind.BaguaFormation,Suit.Heart,2),new(CardKind.Dodge,Suit.Club,8)],
                [new(CardKind.Crossbow,Suit.Club,1)],
                [new(Peer){StartingHand=[new(CardKind.Dodge,Suit.Heart,5),new(CardKind.Slash,Suit.Heart,2),new(CardKind.Peach,Suit.Club,3)]},new(Peer){StartingHand=[new(CardKind.Slash,Suit.Spade,6),new(CardKind.Dodge,Suit.Spade,6)]}],
                [new(Peer,ExtraSkillIds:[Counter]){StartingHand=[new(CardKind.Dodge,Suit.Heart,5)]}],new string('c',64)){DefaultGodFactionId="qun"};
            var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,MaxTurns=12},registry);
            var s=new Scene(game,registry);s.Accept(new StartGameCommand());return s;
        }
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-shen-diao-chan",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Counter}}","revision":1,"triggers":[{"id":"two-slashes","isSkillInvocation":false,"window":"playEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"cost","isSkillInvocation":false,"window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill-program.happy:huoxin.cost"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]},
                {"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy:diao-chan.give","skill-program.happy:diao-chan.take"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels=new Dictionary<string,object>{{Driver,new{name=Driver,description="实际赠送装备"}},{Quiet,new{name=Quiet,description="保留正常实际回合"}},{Counter,new{name=Counter,description="同实际回合两次原生杀目标与回应"}},{Watch,new{name=Watch,description="真实付款与获得后子窗",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Peer,"真实安静角色","",Quiet,"wei",10));
            b.AddDeck(new(Deck,"无额外摸牌的小局",0,0,[]){PhysicalCards=Enumerable.Range(0,40).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"欢乐神貂蝉原生拼点与花色小局",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
}
