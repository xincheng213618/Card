using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouHuangGaiAnniversaryChecks
{
    private const string Hero="mou:huang-gai",Driver="fixture:mhg-top",Quiet="fixture:mhg-quiet",Ally="fixture:mhg-ally",EnemyA="fixture:mhg-enemy-a",EnemyB="fixture:mhg-enemy-b",Deck="fixture:mhg-deck";
    public static void CompletedTrickEntityStacksActualTopSlashAndExpiryCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mhg-entity-reveal",Hero,4,0,0,0,0,[Driver],
            [new(CardKind.DrawTwo,Suit.Heart,7),new(CardKind.IronChain,Suit.Club,7),new(CardKind.Slash,Suit.Spade,7),new(CardKind.FireSlash,Suit.Heart,8),new(CardKind.Duel,Suit.Spade,9),new(CardKind.ThunderSlash,Suit.Club,10)],
            [new(CardKind.ZhangbaSerpentSpear,Suit.Spade,12)],
            [new(EnemyA,Hp:12){StartingHand=[]},new(EnemyB,Hp:12){StartingHand=[]}],[new(Ally,Hp:12){StartingHand=[]}],new string('d',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,
            ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=6},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        PlayerSnapshot V(int seat=0)=>g.CreateSnapshot(0).Players[seat];
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Prompt=P(),Players=g.CreateSnapshot(0).Players,
            Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=g.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+Diagnostic());}
        void Submit(GameCommand command)
        {
            try{var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Actual command rejected.");}
            catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}
        }
        void Answer(Func<PromptChoice,bool> predicate)
        {var p=P()!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"Only the exact published human choice is answered.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<120;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Unexpected actual cutoff before "+line);
                if(P()is{PlayerSeat:0}p)
                {
                    if(p.Kind==DecisionKind.DiscardCards)Submit(new DiscardCardsCommand(0,p.ValidCardIds.OrderBy(id=>V().Hand.Single(c=>c.Id==id).Kind==CardKind.ThunderSlash?1:0).Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
                    else throw new InvalidOperationException("Unexpected actual human boundary at "+line+Diagnostic());
                }
                else Submit(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Bounded Huang Gai actual flow not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Native(CardKind kind,int? target=null)
        {
            var id=V().Hand.Single(c=>c.Kind==kind).Id;var action=g.GetHumanLegalActions().FirstOrDefault(a=>a.CardId==id&&a.ConversionSource is null&&a.Kind!=LegalActionKind.Recast&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));
            Need(action is not null,"The real physical Hand card has a current canonical action.");Submit(new PlayCardCommand(0,id,action!.TargetSeats,g.Revision,P()!.PromptId,action.PlayedCardKind,action.TargetCardId));
        }
        void Lieji()
        {
            Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"&&c.Parameters.GetValueOrDefault("skill-id")=="mou:lieji"));
            Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"&&c.Parameters.GetValueOrDefault("skill-id")=="mou:lieji");Play();
        }
        void Top(int id){Submit(new UseProgramSkillCommand(0,Driver,"top",[id],[],g.Revision,P()!.PromptId));Play();}
        Submit(new StartGameCommand());Play();Native(CardKind.DrawTwo);Lieji();Native(CardKind.IronChain,3);Lieji();
        Need(E<MouHgLiejiGrantedEvent>().Length==2&&E<MouHgBonusIssuedEvent>().Length==0,"Two genuinely completed Tricks grant independent frozen Hand-entity benefits, without issuing a damage invoice before any material Use.");
        var pair=V().Hand.Where(c=>c.Kind is CardKind.FireSlash or CardKind.Duel).Select(c=>c.Id).ToArray();
        Submit(new UseEquipmentEffectCommand(0,CardKind.ZhangbaSerpentSpear,pair,[2],g.Revision,P()!.PromptId));Play();
        var grouped=E<CardActionAcceptedEvent>().Single(e=>e.Action.Type==CardActionType.Use&&e.Action.PhysicalCards.Select(c=>c.CardId).Order().SequenceEqual(pair.Order())).Action;
        Need(V(2).Hp==7&&E<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==5)&&
            E<MouHgBonusIssuedEvent>().Count(e=>e.ActionId==grouped.ActionId)==4&&pair.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1),
            "The real Spear uses both marked damage entities once; two Lieji grants on each material add four to this native Slash's base1.");
        var slash=V().Hand.Single(c=>c.Kind==CardKind.Slash).Id;var dodge=V().Hand.First(c=>c.Kind==CardKind.Dodge).Id;
        Top(slash);Top(dodge);
        Submit(new UseProgramSkillCommand(0,"mou:quzhou","reveal-and-sail",[],[],g.Revision,P()!.PromptId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="again"));
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="again");
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="slash"));
        var reveals=E<MouHgRevealedEvent>();
        Need(reveals.Select(e=>e.CardId).SequenceEqual([dodge,slash])&&reveals.All(e=>g.CardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing))&&
            E<MouHgNativeIssuedEvent>().Length==0&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.MouHuangGai is{Stage:MouHgStage.Target,Reveals.Count:2}),
            "Quzhou uses the real ordered top entities and suspends on its owning target-choice frame before declaring or paying the revealed Slash.");
        var before=SnapshotJson.Serialize(g.CreateSnapshot(0));g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"A representative Cold preserves both actual public reveals and the original unissued native target choice.");
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="slash"&&c.Targets.SequenceEqual([3]));Play();
        var issued=E<MouHgNativeIssuedEvent>().Single().Origin;
        Need(issued.CardId==slash&&issued.Kind==CardKind.Slash&&issued.Actor==0&&issued.Target==3&&V(3).Hp==9&&V().Hand.Any(c=>c.Id==dodge)&&
            E<MouHgBonusIssuedEvent>().Count(e=>e.UseId==issued.UseId&&e.CardId==slash)==2&&E<MouHgNativeReturnedEvent>()is[{Origin:var returned}]&&returned==issued&&
            E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==issued.UseId&&e.CardId==slash)==1&&E<MouHgCompletedEvent>()is[{RevealedCount:2}]&&
            g.CardMovements.Count(m=>m.CardId==dodge&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.mou:quzhou.obtain")==1&&
            !g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="mou:quzhou"),
            "The original actual Processing Slash retains its two entity grants after leaving Hand, completes genuine Damage3 once, then Quzhou obtains the preceding non-Slash and closes its once-Play entry.");
        Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));Reach(()=>g.State.TurnNumber==5&&P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        var bonuses=E<MouHgBonusIssuedEvent>().Length;Native(CardKind.ThunderSlash,3);Play();
        Need(V(3).Hp==8&&E<MouHgBonusIssuedEvent>().Length==bonuses&&E<TurnEndedEvent>().Any(e=>e.TurnNumber==1&&e.ActorSeat==0)&&
            E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==1)==1,
            "A previously marked damage entity kept in Hand loses both grants at the original actual turn end, so its later native Slash deals only base1.");

        // One physical non-Slash and no normal draw leaves both native stock
        // zones empty after the first reveal; another reveal must return.
        registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(empty:true));
        var emptySetup=new ShanheBattleSetup(7,1,"mhg-empty-after-reveal",Hero,4,0,0,0,0,[],[],[],
            [new(EnemyA,Hp:12){StartingHand=[]},new(EnemyB,Hp:12){StartingHand=[]}],[new(Ally,Hp:12){StartingHand=[]}],new string('e',64));
        g=GameEngine.CreateStandard(new(){Seed=emptySetup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,
            ShanheBattle=emptySetup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=2},registry);
        Submit(new StartGameCommand());Play();Submit(new UseProgramSkillCommand(0,"mou:quzhou","reveal-and-sail",[],[],g.Revision,P()!.PromptId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="again"));var lone=E<MouHgRevealedEvent>().Single().CardId;
        Answer(c=>c.Parameters.GetValueOrDefault("branch")=="again");Play();
        Need(E<MouHgRevealedEvent>().Length==1&&E<MouHgCompletedEvent>()is[{RevealedCount:1}]&&V().Hand.Any(c=>c.Id==lone)&&
            g.CardMovements.Count(m=>m.CardId==lone&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.mou:quzhou.obtain")==1,
            "An actual empty top-deck return obtains the prior real reveal once and returns to Play without offering an endless second reveal.");
    }
    private sealed class Fixture(bool empty=false):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-huang-gai",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var kind in new[]{CardKind.Dodge,CardKind.DrawTwo,CardKind.IronChain,CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash,CardKind.Duel,CardKind.ZhangbaSerpentSpear})
            {var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mhg-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"top","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                "effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="真实置顶",description="把所选真实手牌置于牌堆顶"},[Quiet]=new{name="安静同行",description="正常摸牌后跳过出牌阶段"}}}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            foreach(var id in new[]{Ally,EnemyA,EnemyB})b.AddGeneral(new(id,"实际同行","",Quiet,"wei",12));
            b.AddDeck(new(Deck,"固定真实闪牌堆",0,empty?0:2,[]){PhysicalCards=Enumerable.Range(0,empty?1:64).Select(_=>new ContentDeckPhysicalCard("fixture:mhg-"+CardKind.Dodge,Suit.Diamond,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋黄盖真实实体与顶牌流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,EnemyA,EnemyB],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
