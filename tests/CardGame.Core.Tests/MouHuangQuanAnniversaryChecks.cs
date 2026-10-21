using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouHuangQuanAnniversaryChecks
{
    private const string Hero="mou:huang-quan",Quiet="fixture:mhq-quiet",Ally="fixture:mhq-ally",EnemyA="fixture:mhq-enemy-a",EnemyB="fixture:mhq-enemy-b",Aggressor="fixture:mhq-aggressor",Deck="fixture:mhq-deck";
    public static void NativeGiftsEqualReplyOriginalEffectWholeUseAndCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mhq-native-give",Hero,3,0,0,0,0,["classic:rende"],
            [new(CardKind.Slash,Suit.Spade,7),new(CardKind.Snatch,Suit.Spade,3),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Dodge,Suit.Diamond,3)],[],
            [new(EnemyA,Hp:12){StartingHand=[]},new(EnemyB,Hp:12){StartingHand=[new(CardKind.ArrowBarrage,Suit.Heart,1)]}],
            [new(Ally,Hp:12){StartingHand=[new(CardKind.Peach,Suit.Heart,4),new(CardKind.Peach,Suit.Heart,5)]}],new string('a',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=5},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();PlayerSnapshot V(int seat=0)=>g.CreateSnapshot(0).Players[seat];
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Prompt=P(),Players=g.CreateSnapshot(0).Players,
            Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=g.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+Diagnostic());}
        void Submit(GameCommand command){try{var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual command.");}catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}}
        void Answer(Func<PromptChoice,bool> predicate){var p=P()!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"Only the exact current human choice may be answered.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<150;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Unexpected actual cutoff before "+line);
                if(P()is{PlayerSeat:0}p)
                {
                    if(p.Kind==DecisionKind.DiscardCards)Submit(new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
                    else throw new InvalidOperationException("Unexpected actual human boundary at "+line+Diagnostic());
                }
                else Submit(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Bounded Huang Quan actual flow not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Native(CardKind kind,int target)
        {
            var id=V().Hand.First(c=>c.Kind==kind).Id;var action=g.GetHumanLegalActions().FirstOrDefault(a=>a.CardId==id&&a.ConversionSource is null&&a.TargetSeats.SequenceEqual([target]));
            Need(action is not null,"The actual physical Hand card has a canonical target action.");Submit(new PlayCardCommand(0,id,action!.TargetSeats,g.Revision,P()!.PromptId,action.PlayedCardKind,action.TargetCardId));
        }
        Submit(new StartGameCommand());Play();Native(CardKind.Slash,3);
        Reach(()=>P()?.SkillPrompt?.SkillId=="mou:qiaodui");var gifts=V().Hand.Where(c=>c.Kind==CardKind.Dodge).Take(2).Select(c=>c.Id).ToArray();
        foreach(var id in gifts)Answer(c=>c.Parameters.GetValueOrDefault("branch")=="card"&&c.Cards.SequenceEqual([id]));
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="confirm");Answer(c=>c.Parameters.GetValueOrDefault("branch")=="recipient"&&c.Targets.SequenceEqual([1]));
        Reach(()=>P()is{PlayerSeat:1}p&&p.SkillPrompt?.SkillId=="mou:tuicheng");
        var pending=g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.MouHuangQuan is{Stage:MouHqStage.Reply});
        Need(pending.MouHuangQuan is{Count:2,Recipient:1,Gift.Movements.Count:2}&&P()!.Choices.All(c=>!c.Cards.Any(gifts.Contains))&&
            !P()!.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="refuse")&&E<MouHqEffectIssuedEvent>().Length==0&&E<MouHqReplyEvent>().Length==0,
            "The original two real gifts freeze one equal-count private reply; newly received entities cannot be returned, sufficient payment is mandatory, and the original Use still awaits Tuicheng.");
        var before=SnapshotJson.Serialize(g.CreateSnapshot(0));g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"Representative Cold preserves the actual frozen Give and its original native Use parent without paying a second time.");Play();
        var outgoing=E<MouHqEffectIssuedEvent>().Single(e=>e.Repeats).Grant;
        Need(V(3).Hp==10&&E<MouHqReplyEvent>()is[{Recipient:1,RequiredCount:2,Gave:true}]&&E<MouHqEffectRoundStartedEvent>()is[{UseId:var repeated}]&&repeated==outgoing.UseId&&
            E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==outgoing.UseId)==1&&E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==1)==2&&
            g.CardMovements.Count(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.mou:tuicheng.reply")==2&&
            E<NativeCardsGivenEvent>().Count(e=>e.Giver==0&&e.Recipient==1&&e.Count==2)==1,
            "The original actual Slash resolves twice with one declaration/payment and one finish, after the recipient really gives its two older legal entities back.");
        var givenBefore=E<NativeCardsGivenEvent>().Length;var tuichengBefore=E<MouHqStartedEvent>().Count(e=>e.Source.SkillId=="mou:tuicheng");
        Native(CardKind.Snatch,1);Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.SelectTargetCard});
        Answer(c=>c.Parameters.GetValueOrDefault("action")=="target-card-slot");Play();
        Need(E<NativeCardsGivenEvent>().Length==givenBefore&&E<MouHqStartedEvent>().Count(e=>e.Source.SkillId=="mou:tuicheng")==tuichengBefore&&
            E<TargetCardTakenEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==1&&e.FromZone==CardZoneKind.Hand),
            "A real native Snatch uses HE-to-Processing-to-Hand obtain provenance and must not be misclassified as a Give or create a false Tuicheng request.");
        var rende=V().Hand.First(c=>c.Kind==CardKind.Dodge).Id;var count=V().HandCount;
        Submit(new UseProgramSkillCommand(0,"classic:rende","give-and-heal-after-two",[rende],[1],g.Revision,P()!.PromptId));Play();
        Need(E<NativeCardsGivenEvent>().Any(e=>e.Giver==0&&e.Recipient==1&&e.Count==1)&&E<MouHqReplyEvent>().Last()is{RequiredCount:1,Gave:false}&&
            E<MouHqDrawReturnedEvent>()is[{RequestedCount:1,ActualCount:1}]&&V().HandCount==count&&
            g.CardMovements.Count(m=>m.CardId==rende&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(1))==1,
            "Existing native Rende GiveSelected is observed independently of Qiaodui; the recipient has only forbidden current-turn gifted entities, so the exact required count becomes one real Draw instead of a partial or optional reply.");
        var hp=g.CreateSnapshot(0).Players.Select(p=>p.Hp).ToArray();var damageBefore=E<DamageAppliedEvent>().Length;Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId=="mou:qiaodui");
        var incoming=g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f=>f.MouHuangQuan is not null).MouHuangQuan!;
        Need(!incoming.Outgoing&&g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==incoming.UseId).TargetSeats.Count==3,"A genuine later other-actor native Archery targets multiple roles and resets the actual-turn Qiaodui allowance.");
        givenBefore=E<NativeCardsGivenEvent>().Length;Answer(c=>c.Parameters.GetValueOrDefault("branch")=="confirm");Answer(c=>c.Parameters.GetValueOrDefault("branch")=="recipient"&&c.Targets.SequenceEqual([1]));
        Reach(()=>E<CardUseFinishedEvent>().Any(e=>e.ResolutionId==incoming.UseId));
        Need(E<MouHqEffectIssuedEvent>().Single(e=>!e.Repeats).Grant is{GivenCount:0}nullified&&nullified.UseId==incoming.UseId&&
            E<NativeCardsGivenEvent>().Length==givenBefore&&E<MouHqCommittedEvent>().Count(e=>e.Count==0)==1&&
            g.CreateSnapshot(0).Players.Select(p=>p.Hp).SequenceEqual(hp)&&E<DamageAppliedEvent>().Length==damageBefore,
            "Zero-card incoming Qiaodui consumes its once right without creating Give/Tuicheng facts; the original multirole card becomes ineffective for every target and completes once.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-huang-quan",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"classic:liu-bei");
            foreach(var kind in new[]{CardKind.Slash,CardKind.Snatch,CardKind.Dodge,CardKind.Peach,CardKind.ArrowBarrage}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mhq-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="正常摸牌后真实弃手并跳过出牌"}}}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddSkill(new(Aggressor,"实际万箭同行","保留当前手牌并按原生AI出牌"));b.AddGeneral(new(Ally,"实际盟友","",Quiet,"wei",12));b.AddGeneral(new(EnemyA,"实际敌方","",Quiet,"wei",12));b.AddGeneral(new(EnemyB,"实际万箭敌方","",Aggressor,"wei",12));
            b.AddDeck(new(Deck,"固定真实闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("fixture:mhq-"+CardKind.Dodge,Suit.Diamond,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋黄权原生交牌与原牌结算",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,EnemyA,EnemyB],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
