using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouCaoAngAnniversaryChecks
{
    private const string Hero="mou:cao-ang",Plain="fixture:mca-anniv-plain",Driver="fixture:mca-anniv-driver",Quiet="fixture:mca-anniv-quiet",Deck="fixture:mca-anniv-deck";
    public static void GlobalEquipmentLossDiscardOriginalForeignUseSourceLessAndCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mou-cao-ang-anniversary-native",Hero,7,4,0,0,0,[Driver],
            [new(CardKind.GudingBlade,Suit.Spade,1),new(CardKind.Dismantlement,Suit.Spade,3),new(CardKind.OffensiveHorse,Suit.Heart,5),new(CardKind.ArrowBarrage,Suit.Heart,1)],
            [new(CardKind.SilverLion,Suit.Club,1)],
            [new(Plain,Hp:12){StartingHand=[]},new(Plain,Hp:12){StartingHand=[]}],
            [new(Plain,Hp:12){StartingHand=[]}],new string('e',64));
        var game=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=3},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        PlayerSnapshot V(int s=0)=>game.CreateSnapshot(0).Players[s];T[] E<T>()=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        ProgramSkillFrame? F()=>game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.MouCaoAng is not null);
        string Diagnostic()=>JsonSerializer.Serialize(new{game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=P(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.Hp,p.HandCount,p.Equipment}),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(35).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+Diagnostic());}
        void Submit(GameCommand command){try{var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}}
        void Answer(Func<PromptChoice,bool> match){var p=P();Need(p?.PlayerSeat==0,"Only the actual Human chooser is answered.");var choice=p!.Choices.FirstOrDefault(match);Need(choice is not null,"The required native Cao Ang choice is published.");Submit(new AnswerPromptCommand(0,p.PromptId,choice!.Id,game.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<140;i++)
            {
                if(done())return;Need(game.State.Winner==Winner.None,"Unexpected terminal result at "+line);
                if(P()is{PlayerSeat:0})throw new InvalidOperationException("Unexpected actual Human boundary at "+line+Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded actual Cao Ang flow not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        Submit(new StartGameCommand());Play();
        var turn=game.State.TurnNumber;var lion=V().Equipment.Single(c=>c.Kind==CardKind.SilverLion).Id;
        var weapon=V().Hand.Single(c=>c.Kind==CardKind.GudingBlade).Id;var arrow=V().Hand.Single(c=>c.Kind==CardKind.ArrowBarrage).Id;var horse=V().Hand.Single(c=>c.Kind==CardKind.OffensiveHorse).Id;
        Submit(new UseProgramSkillCommand(0,Driver,"place",[weapon],[1],game.Revision,P()!.PromptId));Play();
        Need(V(1).Equipment.Any(c=>c.Id==weapon)&&E<MouCaFengminPaidEvent>().Length==0,"Actual equipment entry does not invent an equipment-loss trigger.");
        var dismantlement=game.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.Dismantlement&&a.TargetSeats.SequenceEqual([1])&&a.TargetCardId==weapon&&a.ConversionSource is null);
        Submit(new PlayCardCommand(0,dismantlement.CardId!.Value,dismantlement.TargetSeats,game.Revision,P()!.PromptId,dismantlement.PlayedCardKind,dismantlement.TargetCardId));Play();
        var weaponExit=game.CardMovements.Where(m=>m.CardId==weapon&&m.From==CardLocation.Equipment(1)&&m.To==CardLocation.Processing).ToArray();
        var weaponDiscard=game.CardMovements.Where(m=>m.CardId==weapon&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile).ToArray();
        Need(E<MouCaFengminPaidEvent>()is[{Owner:0,Loser:1,Count:5}]&&E<MouCaDrawReturnedEvent>()is[{Requested:5,Actual:5}]&&weaponExit.Length==1&&weaponDiscard.Length==1&&weaponExit[0].Sequence<weaponDiscard[0].Sequence,
            "Fengmin observes another role's genuine native equipment departure and draws the resulting five effective empty slots once for this actual turn.");
        var equip=game.GetHumanLegalActions().Single(a=>a.Kind==LegalActionKind.Equip&&a.CardId==horse&&a.ConversionSource is null);
        Submit(new PlayCardCommand(0,horse,equip.TargetSeats,game.Revision,P()!.PromptId,equip.PlayedCardKind,equip.TargetCardId));Play();
        Submit(new UseProgramSkillCommand(0,Driver,"drop",[arrow],[],game.Revision,P()!.PromptId));Play();
        Need(V().Equipment.Select(c=>c.Id).Order().SequenceEqual(new[]{lion,horse}.Order())&&game.CardMovements.Count(m=>m.CardId==arrow&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile)==1&&V().Hp==7,
            "The original ArrowBarrage is truly discarded and the still-wounded owner retains two real equipment entities before the actual Ending.");
        Submit(new EndPlayPhaseCommand(0,game.Revision,P()!.PromptId));Reach(()=>F()?.MouCaoAng?.Stage==MouCaoAngStage.Material&&P()?.PlayerSeat==0);
        var root=F()!;var paid=E<MouCaDiscardedEvent>().Where(e=>e.FrameId==root.Id).ToArray();
        Need(paid.Length==2&&paid.Select(e=>e.CardId).Order().SequenceEqual(new[]{lion,horse}.Order())&&paid.All(e=>e.To==CardLocation.DiscardPile)&&V().Equipment.Count==0&&V().Hp==8&&E<SilverLionRemovedRecoveryEvent>().Any(e=>e.PlayerSeat==0&&e.Reason.Value=="skill-program.mou:zhiwang.equipment-discard"&&e.RecoveredAmount==1)&&E<MouCaFengminPaidEvent>().Length==1,
            "Zhiwang actually logically discards the complete equipment group, returns the native SilverLion recovery and does not spend Fengmin twice in the same actual turn.");
        var useMovementStart=game.CardMovements.Last().Sequence;
        Answer(c=>c.Cards.SequenceEqual([arrow])&&c.Targets.SequenceEqual([1]));
        Need(F()?.MouCaoAng is{Stage:MouCaoAngStage.UseChoice,Actor:1,CardId:var selected}&&selected==arrow&&P()?.PlayerSeat==1&&P()!.Choices.All(c=>c.Cards.SequenceEqual([arrow])),
            "Cao Ang chooses the current discard entity and other actor, then that genuine actor receives its own legal native Use choices.");
        Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});
        var use=game.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.MouCaoAngZhiwang?.FrameId==root.Id);var origin=use.MouCaoAngZhiwang!;
        Need(use.CardId==arrow&&use.CardKind==CardKind.ArrowBarrage&&use.SourceSeat==1&&use.TargetSeats.SequenceEqual([2,3,0])&&use.Action is{Type:CardActionType.Use,ActorSeat:1,ProviderSeat:1,RequesterSeat:null,PhysicalCards:[var material]}&&material.CardId==arrow&&material.From==CardLocation.DiscardPile&&E<MouCaUseIssuedEvent>().Length==1&&game.CardMovements.Count(m=>m.CardId==arrow&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Processing)==1,
            "The actual foreign original card Use keeps actor/provider 1, the canonical all-other target order and its one physical DiscardPile-to-Processing payment without a Hand transfer or virtual substitute.");
        var ownerView=SnapshotJson.Serialize(game.CreateSnapshot(0));var actorView=SnapshotJson.Serialize(game.CreateSnapshot(1));
        game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Need(ownerView==SnapshotJson.Serialize(game.CreateSnapshot(0))&&actorView==SnapshotJson.Serialize(game.CreateSnapshot(1))&&F()?.MouCaoAng?.Native is{}restored&&restored.UseId==origin.UseId&&restored.ActionId==origin.ActionId&&E<MouCaUseIssuedEvent>().Length==1&&E<MouCaDiscardedEvent>().Length==2,
            "Representative Cold in the genuine paid foreign Use's human native response preserves both views, original Use/action identity, the spent equipment group and one payment.");
        Answer(c=>c.Cards.Count==0);Reach(()=>E<MouCaUseReturnedEvent>().Any(e=>e.UseId==origin.UseId)&&E<MouCaCompletedEvent>().Any(e=>e.FrameId==root.Id));
        var hits=E<DamageAppliedEvent>();
        Need(hits.Length==3&&hits.Count(e=>e.SourceSeat==1&&e.TargetSeat==0&&e.Amount==1&&e.SourceLess)==1&&hits.Count(e=>e.SourceSeat==1&&e.TargetSeat is 2 or 3&&e.Amount==1&&!e.SourceLess)==2&&E<MouCaSourceLessEvent>()is[{UseId:var sourceLessUse,ActionId:var sourceLessAction,Target:0,CardId:var sourceLessCard}]&&sourceLessUse==origin.UseId&&sourceLessAction==origin.ActionId&&sourceLessCard==arrow,
            "Only this original card's damage against Cao Ang is source-less; the same real AOE retains its actual foreign damage source against both other targets.");
        Need(E<MouCaUseReturnedEvent>().Count(e=>e.FrameId==root.Id&&e.UseId==origin.UseId&&e.ActionId==origin.ActionId)==1&&E<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==origin.UseId&&e.CardId==arrow)==1&&E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==origin.UseId&&e.CardId==arrow)==1&&E<MouCaCompletedEvent>().Count(e=>e.FrameId==root.Id)==1,
            "The single original issued Use declares, finishes, returns and completes its paid Ending instruction exactly once after Cold.");
        var returnedMovementEnd=game.CardMovements.Last().Sequence;
        var actualUseMoves=game.CardMovements.Where(m=>m.CardId==arrow&&m.Sequence>useMovementStart&&m.Sequence<=returnedMovementEnd).ToArray();
        Need(actualUseMoves.Length==2&&actualUseMoves[0].From==CardLocation.DiscardPile&&actualUseMoves[0].To==CardLocation.Processing&&actualUseMoves[0].Reason==CardMoveReasons.Use&&actualUseMoves[1].From==CardLocation.Processing&&actualUseMoves[1].To==CardLocation.DiscardPile&&actualUseMoves[1].Reason==CardMoveReasons.UseFinished&&actualUseMoves[0].Sequence<actualUseMoves[1].Sequence&&actualUseMoves.All(m=>m.From.Zone!=CardZoneKind.Hand&&m.To.Zone!=CardZoneKind.Hand),
            "This exact issued native Use pays its discard entity once and retires it once, never routing it through Hand or paying again after Cold.");
        Need(E<MouCaFengminPaidEvent>()is[{Turn:var paidTurn,Owner:0,Loser:1,Count:5}]&&paidTurn==turn,
            "The original actual-turn Fengmin allowance remains spent once across the owner's later equipment departure, foreign Use and Cold.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-cao-ang-anniversary",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var kind in new[]{CardKind.Dodge,CardKind.GudingBlade,CardKind.Dismantlement,CardKind.OffensiveHorse,CardKind.ArrowBarrage,CardKind.SilverLion}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mca-anniv-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                    {"id":"drop","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"drop"},{"op":"moveBoundCards","target":"owner","sourceBind":"drop","destination":"discardPile","awaitMovementTriggers":true}]}
                  ]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                ]}
                """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="真实装备输入",description="原生安置装备及移动选定实体到弃牌堆。"},[Quiet]=new{name="安静同行",description="正常摸牌后真实弃手并跳过出牌。"}}}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Plain,"实际安静角色","",Quiet,"wei",12));
            b.AddDeck(new(Deck,"固定黑闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("fixture:mca-anniv-"+CardKind.Dodge,Suit.Club,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋曹昂真实失装与弃牌实体Use",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Plain],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
