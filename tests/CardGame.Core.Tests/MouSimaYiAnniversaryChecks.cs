using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouSimaYiAnniversaryChecks
{
    private const string Hero="mou:sima-yi",Proxy="fixture:msm-proxy",Plain="fixture:msm-plain",Quiet="fixture:msm-quiet",Driver="fixture:msm-driver",Deck="fixture:msm-deck";
    public static void ActualGivePolarityPrivateOriginalSlashPlayedAndCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mou-sima-yi-native",Hero,3,0,0,0,0,[Driver,"classic:guhuo"],
            [new(CardKind.Slash,Suit.Spade,7),new(CardKind.Peach,Suit.Heart,6),new(CardKind.BorrowedSword,Suit.Spade,12),new(CardKind.ZhangbaSerpentSpear,Suit.Club,12)],
            [new(CardKind.QilinBow,Suit.Heart,5)],
            [new(Plain,Hp:12){StartingHand=[]},new(Proxy,Hp:12){StartingHand=[new(CardKind.Slash,Suit.Spade,4),new(CardKind.Slash,Suit.Club,5)]}],
            [new(Plain,Hp:12){StartingHand=[]}],new string('f',64));
        var game=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=3},registry);
        var delivered=new List<EventEnvelope>();var logs=new List<GameLogEntry>();void Observe(){game.EventCommitted+=e=>delivered.Add(e);game.LogAdded+=l=>logs.Add(l);}Observe();
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        PlayerSnapshot V(int s=0)=>game.CreateSnapshot(0).Players[s];T[] E<T>()=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        ProgramSkillFrame? F()=>game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.MouSimaYi is not null);
        string Diagnostic()=>JsonSerializer.Serialize(new{game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=P(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.Hp,p.HandCount}),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+Diagnostic());}
        void Submit(GameCommand command){try{var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}}
        void Answer(Func<PromptChoice,bool> pick){var p=P();Need(p?.PlayerSeat==0,"Only the actual private human choice is answered.");var c=p!.Choices.FirstOrDefault(pick);Need(c is not null,"Required Sima Yi canonical choice missing.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,game.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<170;i++)
            {
                if(done())return;Need(game.State.Winner==Winner.None,"Unexpected game completion at "+line);
                if(P()is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification}){Answer(c=>c.Cards.Count==0);continue;}
                if(P()is{PlayerSeat:0}p)throw new InvalidOperationException("Unexpected actual human input at "+line+Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded actual Sima Yi boundary not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Use(string skill,string binding,int target,IReadOnlyList<int>? cards=null)=>Submit(new UseProgramSkillCommand(0,skill,binding,cards??[],[target],game.Revision,P()!.PromptId));
        void Quanmou(int target){Submit(new UseProgramSkillCommand(0,"mou:quanmou","in-range-give",[],[],game.Revision,P()!.PromptId));Reach(()=>P()?.SkillPrompt?.SkillId=="mou:quanmou"&&F()?.MouSimaYi?.Stage==MouSmStage.Target);Answer(c=>c.Targets.SequenceEqual([target]));}
        Submit(new StartGameCommand());Reach(()=>F()?.MouSimaYi?.Stage==MouSmStage.Polarity&&P()?.PlayerSeat==0);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="yin");Play();
        Quanmou(3);Play();
        Need(E<MouSmDebtIssuedEvent>()is[{Target:3,Yin:true}]&&E<MouSmGivePaidEvent>()is[{Giver:3,CardId:var received}]&&V().Hand.Any(c=>c.Id==received)&&game.CardMovements.Any(m=>m.CardId==received&&m.From==CardLocation.Hand(3)&&m.To==CardLocation.Hand(0)),"Yin receives one exact genuine NPC-owned entity and freezes the actual Play's target debt.");
        Use(Driver,"damage",3);Reach(()=>F()?.MouSimaYi?.Stage==MouSmStage.SpreadTargets);Answer(c=>c.Targets.SequenceEqual([1]));Answer(c=>c.Targets.SequenceEqual([2]));Answer(c=>c.Parameters.GetValueOrDefault("branch")=="finish");Play();
        Need(V(1).Hp==11&&V(2).Hp==11&&V(3).Hp==11&&E<MouSmDamageIssuedEvent>().Select(e=>e.Target).SequenceEqual([1,2])&&E<DamageAppliedEvent>().Count(e=>e.SourceSeat==0&&e.Amount==1)==3,"The next actual source-to-debt-target damage consumes Yin once; the two selected other roles each receive real native damage.");
        Quanmou(2);Play();Need(E<MouSmDebtIssuedEvent>().Last()is{Target:2,Yin:false}&&E<MouSmGivePaidEvent>().Length==1,"A current empty HE target remains legal without fabricating a Give, and polarity still converts.");
        Use(Driver,"damage",2);Play();Need(V(2).Hp==11&&E<ProgramDamagePreventedEvent>().Any(e=>e.TargetSeat==2),"Yang prevents the next exact real damage occurrence.");
        Use(Driver,"damage",2);Play();Need(V(2).Hp==10&&E<MouSmDebtConsumedEvent>().Length==2,"The already consumed Yang effect cannot prevent a second damage occurrence.");
        var slash=game.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([2])&&a.ConversionSource is null);Need(slash is not null,"The current real long-range Slash has a canonical physical action.");
        Submit(new PlayCardCommand(0,slash!.CardId!.Value,slash.TargetSeats,game.Revision,P()!.PromptId,slash.PlayedCardKind,slash.TargetCardId));
        var secret=game.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.SourceSeat==0&&u.MouSimaYiSecret is not null);var early=secret.MouSimaYiSecret!;
        Need(!early.Released&&game.Events.All(e=>!early.EventSequences.Contains(e.Sequence))&&delivered.All(e=>!early.EventSequences.Contains(e.Sequence))&&game.Log.All(l=>!early.LogSequences.Contains(l.Sequence))&&logs.All(l=>!early.LogSequences.Contains(l.Sequence))&&game.CreateSnapshot(2).PendingDecision is null,"Before any target skill window, the actual source Slash's target events, committed notifications and target log remain private while the first unrelated range respondent chooses.");
        Play();Need(E<MouSmTargetsDisclosedEvent>().Single(d=>d.UseId==secret.Id).Targets.SequenceEqual([2])&&V(2).Hp==9&&E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==secret.Id)==1,"Declines reveal this original Slash exactly once and the same native Use then deals real damage and finishes once.");
        var spear=V().Hand.Single(c=>c.Kind==CardKind.ZhangbaSerpentSpear).Id;Use(Driver,"place",3,[spear]);Play();
        var borrowed=game.GetHumanLegalActions().FirstOrDefault(a=>a.Kind==LegalActionKind.BorrowedSword&&a.TargetSeats.SequenceEqual([3,1])&&a.ConversionSource is null);Need(borrowed is not null,"The real equipped other actor can be ordered to Slash its legal opposing role.");
        Submit(new PlayCardCommand(0,borrowed!.CardId!.Value,borrowed.TargetSeats,game.Revision,P()!.PromptId,borrowed.PlayedCardKind,borrowed.TargetCardId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId=="mou:pingliao"&&F()?.MouSimaYi is{Actor:0,Stage:MouSmStage.Respondent});
        var native=game.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.SourceSeat==3&&u.MouSimaYiSecret is not null);var lease=native.MouSimaYiSecret!;var use=native.Id;var action=native.Action!.ActionId;
        var peach=V().Hand.Single(c=>c.Kind==CardKind.Peach).Id;Need(native.TargetSeats.SequenceEqual([1])&&P()!.Choices.Any(c=>c.Cards.SequenceEqual([peach]))&&game.CreateSnapshot(1).PendingDecision is null&&game.Events.All(e=>!lease.EventSequences.Contains(e.Sequence))&&delivered.All(e=>!lease.EventSequences.Contains(e.Sequence)),"The non-target human sees only the private optional red Basic prompt; the true target sees no target skill inquiry or target-bearing Slash event.");
        var before0=SnapshotJson.Serialize(game.CreateSnapshot(0));var before1=SnapshotJson.Serialize(game.CreateSnapshot(1));var oldEvents=game.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray();
        game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);Observe();
        Need(before0==SnapshotJson.Serialize(game.CreateSnapshot(0))&&before1==SnapshotJson.Serialize(game.CreateSnapshot(1))&&oldEvents.SequenceEqual(game.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())))&&game.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==use&&u.Action?.ActionId==action&&u.MouSimaYiSecret is{Released:false}),"Representative Cold at the genuine private red Basic choice preserves both player views, projected history and the exact original paid Use lease.");
        var hand3=V(3).HandCount;Answer(c=>c.Cards.SequenceEqual([peach])&&c.Parameters.GetValueOrDefault("conversion-skill")=="classic:guhuo"&&c.Parameters.GetValueOrDefault("response-card-kind")==nameof(CardKind.Peach));Play();
        var played=E<MouSmPlayedEvent>().Where(e=>e.Actor==0).ToArray();var disclosed=E<MouSmTargetsDisclosedEvent>().Single(d=>d.UseId==use);
        Need(played is[{Completed:false},{Completed:true}]&&played[0].ActionId==played[1].ActionId&&E<CardActionAcceptedEvent>().Single(e=>e.Action.ActionId==played[0].ActionId).Action is{Type:CardActionType.Response,EffectiveKind:CardKind.Peach,EffectiveIsRed:true,ActorSeat:0,ProviderSeat:0,TargetSeats:[]},"The red Peach is a genuine native Played Response with accepted and completed observers, not a Use or a recovery.");
        Need(game.CardMovements.Count(m=>m.CardId==peach&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay")==1&&game.CardMovements.All(m=>m.CardId!=peach||m.Reason!=CardMoveReasons.Respond)&&E<MouSmDeclarationReturnedEvent>()is[{Succeeded:true}]&&E<CardDeclarationCommittedEvent>().Count(e=>e.OwnerSeat==0&&e.DeclaredKind==CardKind.Peach&&e.TargetSeats.Count==0)==1&&E<SkillUsageConsumedEvent>().Count(e=>e.SkillId=="classic:guhuo")==1&&game.CardMovements.Count(m=>m.CardId==peach&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.ResponseFinished)==1&&V().Hp==3,"The original red Peach passes the real Guhuo challenge and declaration quota, the prepaid Hand entity is claimed without a second Respond movement and discarded once, and Playing it grants no recovery.");
        Need(disclosed.ActionId==action&&disclosed.Targets.SequenceEqual([1])&&V(1).Hp==10&&V(3).HandCount==hand3+2&&E<MouSmSlashAllowanceEvent>().Count(e=>e.Source.OwnerSeat==3)==1&&E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use)==1&&game.Events.All(e=>!disclosed.HeldEventSequences.Contains(e.Sequence)),"One genuine non-target red Basic grants real Draw2 and a phase Slash allowance, then releases the original Slash exactly once without retro-delivering held raw envelopes.");
        Need(delivered.Zip(delivered.Skip(1)).All(p=>p.First.Sequence<p.Second.Sequence)&&logs.Zip(logs.Skip(1)).All(p=>p.First.Sequence<p.Second.Sequence),"Public committed event and log delivery remain strictly increasing through the current-command target disclosure.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-sima-yi-anniversary",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero,"classic:yu-ji");
            foreach(var kind in new[]{CardKind.Slash,CardKind.Dodge,CardKind.Peach,CardKind.BorrowedSword,CardKind.ZhangbaSerpentSpear,CardKind.QilinBow}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:msm-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                    {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}
                  ]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
                ]}
                """;
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="真实机制输入",description="真实原生伤害与安置装备。"},[Quiet]=new{name="安静同行",description="正常摸牌后真实弃手并跳过出牌。"}}}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Plain,"实际安静角色","",Quiet,"wei",12));b.AddGeneral(new(Proxy,"平辽实际持有者","","mou:pingliao","wei",12,[Quiet]));
            b.AddDeck(new(Deck,"固定黑闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("fixture:msm-"+CardKind.Dodge,Suit.Club,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋司马懿真实权谋与私密平辽",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Plain,Proxy],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
