using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouHuCheErChecks
{
    private const string Hero = "mou:hu-che-er", Plain = "fixture:mhce-plain", Driver = "fixture:mhce-place", Extra = "fixture:mhce-extra", Quiet = "fixture:mhce-quiet", Hold = "fixture:mhce-hold", Deck = "fixture:mhce-deck";
    public static void ActualRevealSlashChainColdPhaseTailEquipmentAndNextTurn()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        CardKind[] gear = [CardKind.ZhangbaSerpentSpear, CardKind.Tengjia, CardKind.OffensiveHorse, CardKind.DefensiveHorse, CardKind.WoodenOx];
        var starting = new[] { new ShanheStartingCard(CardKind.Slash, Suit.Spade, 3), new(CardKind.Slash, Suit.Club, 4) }.Concat(gear.Select((k,i) => new ShanheStartingCard(k, Suit.Diamond, i + 5))).ToArray();
        var setup = new ShanheBattleSetup(7, 1, "mou-hu-che-er-native", Hero, 4, 0, 0, 0, 0, [Driver,Extra,Hold], starting, [new(CardKind.GudingBlade, Suit.Spade, 2)],
            [new(Plain, Hp: 4) { StartingHand = [] }, new(Plain, Hp: 4) { StartingHand = [new(CardKind.Dodge, Suit.Spade, 5),new(CardKind.Slash, Suit.Spade, 6),new(CardKind.Peach, Suit.Heart, 7),new(CardKind.Peach, Suit.Diamond, 8)] }],
            [new(Plain, Hp: 4) { StartingHand = [] }], new string('c',64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0,4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouHuCheErReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouHuCheEr is not null)?.MouHuCheEr;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=Prompt(),Receipt=Receipt(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.GeneralId,p.Hp,p.HandCount}),
            Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())) });
        void Need(bool ok,string why) { if(!ok) throw new InvalidOperationException(why+Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted,result.Error?.Message??"Native command rejected."); }
            catch(Exception ex) { throw new InvalidOperationException(ex.Message+Diagnostic(),ex); }
        }
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<160;i++)
            {
                if(done())return; Need(game.State.Winner==Winner.None,"Unexpected terminal result at "+line);
                if(Prompt()is{PlayerSeat:0,Kind:DecisionKind.DiscardCards}discard)
                {
                    var ids=discard.ValidCardIds.OrderBy(id=>Player(0).Hand.Single(c=>c.Id==id).Kind==CardKind.Slash?1:0).Take(discard.RequiredCardCount).ToArray();
                    Submit(new DiscardCardsCommand(0,ids,discard.PromptId,game.Revision));continue;
                }
                if(Prompt()is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Human input at "+line+Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded actual Kongwu boundary not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>Prompt()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void Answer(Func<PromptChoice,bool> match)
        {
            var p=Prompt();Need(p?.PlayerSeat==0,"Only the actual current Human chooser is answered.");var choice=p!.Choices.FirstOrDefault(match);Need(choice is not null,"Actual Kongwu choice missing.");
            Submit(new AnswerPromptCommand(0,p.PromptId,choice!.Id,game.Revision));
        }
        Submit(new StartGameCommand());Play();
        foreach(var kind in gear)
        {
            var id=Player(0).Hand.Single(c=>c.Kind==kind).Id;Submit(new UseProgramSkillCommand(0,Driver,"place",[id],[3],game.Revision,Prompt()!.PromptId));Play();
        }
        var equipmentIds=Player(3).Equipment.Select(c=>c.Id).Order().ToArray();
        Need(equipmentIds.Length==5&&Player(0).HandCount==2&&game.GetAttackRange(3)==3&&game.GetCombatDistance(0,3)==2&&game.GetCombatDistance(3,1)==1,
            "All five original physical equipment slots really transfer to the target through native placement before the inserted actual Play's Kongwu.");
        Submit(new UseProgramSkillCommand(0,"mou:kongwu","show-hand",[],[],game.Revision,Prompt()!.PromptId));
        Reach(()=>Receipt()?.Stage==MouHuCheErStage.Target&&Prompt()?.PlayerSeat==0);Answer(c=>c.Targets.SequenceEqual([3]));
        Answer(c=>c.Parameters.GetValueOrDefault("slot-index")=="0");
        Reach(()=>Prompt()?.SkillPrompt?.SkillId==Hold);
        var original=Receipt()!;
        Need(original.Count==2&&original.Index==0&&original.Stage==MouHuCheErStage.NativeSlash&&original.UseId>0&&Events<MouHuCheErDiscardedEvent>().Length==2&&Player(3).HandCount==2&&
            game.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==original.UseId&&u.MouHuCheErSlash?.FrameId>0&&u.CardId==0&&u.Action?.PhysicalCards.Count==0),
            "The actual anonymous position reveals one real card, discards both current Spades, counts two remaining basic HE cards after payment and opens a genuine zero-material Slash.");
        var before=SnapshotJson.Serialize(game.CreateSnapshot(0));
        game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(game.CreateSnapshot(0))&&Events<MouHuCheErDiscardedEvent>().Length==2&&Events<MouHuCheErSlashIssuedEvent>().Length==1,
            "Cold at the real committed Slash child preserves the paid suit group, frozen X and first native use without re-reveal or duplicate payment.");
        Answer(_=>true);Play();
        Need(Events<MouHuCheErSlashIssuedEvent>().Length==2&&Events<MouHuCheErSlashReturnedEvent>().Length==2&&Events<MouHuCheErSlashIssuedEvent>().All(e=>Events<CardUseFinishedEvent>().Count(f=>f.ResolutionId==e.Origin.UseId)==1)&&
            Events<MouHuCheErDamageEvent>().Length==0&&Events<MouHuCheErDrawReturnedEvent>()is[{Requested:2,Actual:2}]&&Player(0).HandCount==4&&Player(3).Hp==4&&Events<MouHuCheErDebtEvent>().Length==0&&
            Events<MouHuCheErSlashIssuedEvent>().All(e=>!Events<CardUseDebitRecordedEvent>().Any(d=>d.Debit.CardActionId==e.Origin.ActionId)),
            "Both real native Slash effects meet the still-effective Tengjia and fully return without damage, so the owner genuinely refills to maxHP; the delayed comparison has not run inside the original Play.");
        var paidPhase=Events<MouHuCheErStartedEvent>().Single().Phase;
        Submit(new EndPlayPhaseCommand(0,game.Revision,Prompt()!.PromptId));Play();
        var debt=Events<MouHuCheErDebtEvent>().Single();
        Need(Events<ActualPhaseEndedEvent>().Any(e=>e.Phase==paidPhase)&&Events<MouHuCheErPhaseSettledEvent>()is[{Applied:true}]&&debt.PhaseToken==paidPhase.Token&&
            Player(3).Equipment.Select(c=>c.Id).Order().SequenceEqual(equipmentIds)&&game.GetAttackRange(3)==1&&game.GetCombatDistance(0,3)==1&&game.GetCombatDistance(3,1)==2,
            "The original inserted Play truly ends before the comparison; its issued debt suppresses the equipped weapon and both mounts immediately while all five physical equipment entities remain present.");
        var slash=Player(0).Hand.First(c=>c.Kind==CardKind.Slash).Id;
        Need(game.GetHumanLegalActions().Any(a=>a.CardId==slash&&a.Kind==LegalActionKind.Slash&&a.TargetSeats.SequenceEqual([3])&&a.ConversionSource is null),"The forced Slash chain preserves the actual ordinary Slash allowance in the following actual Play.");
        Submit(new PlayCardCommand(0,slash,[3],game.Revision,Prompt()!.PromptId));Play();
        Need(Player(3).Hp==3&&Events<DamageAppliedEvent>().Any(e=>e.SourceSeat==0&&e.TargetSeat==3&&e.Amount==1),"The same physical Tengjia is now ineffective: the genuine ordinary Slash deals its native damage.");
        var targetHand=Player(3).HandCount;
        Submit(new EndPlayPhaseCommand(0,game.Revision,Prompt()!.PromptId));
        Reach(()=>Events<MouHuCheErDebtExpiredEvent>().Any(e=>e.FrameId==debt.FrameId));
        var targetTurn=Events<MouHuCheErDebtTurnEvent>().Single(e=>e.FrameId==debt.FrameId);
        Need(Events<TurnStartedEvent>().Any(e=>e.ActorSeat==3&&e.TurnNumber==targetTurn.Turn)&&Events<TurnEndedEvent>().Any(e=>e.ActorSeat==3&&e.TurnNumber==targetTurn.Turn)&&
            Player(3).HandCount==targetHand+1&&Player(3).Equipment.Select(c=>c.Id).Order().SequenceEqual(equipmentIds)&&game.GetAttackRange(3)==3&&game.GetCombatDistance(0,3)==2&&game.GetCombatDistance(3,1)==1,
            "The debt binds only the target's next actual turn, reduces its native normal draw from2 to1 and restores equipment at that real TurnEnded without moving or recreating any equipment.");
        Play();slash=Player(0).Hand.First(c=>c.Kind==CardKind.Slash).Id;var hp=Player(3).Hp;
        Submit(new PlayCardCommand(0,slash,[3],game.Revision,Prompt()!.PromptId));Play();
        var restoredUse=Events<CardUseDeclaredEvent>().Last(e=>e.SourceSeat==0&&e.CardId==slash).ResolutionId;
        Need(Player(3).Hp==hp&&Events<ArmorEffectAppliedEvent>().Any(e=>e.ResolutionId==restoredUse&&e.TargetSeat==3&&e.ArmorCard==CardKind.Tengjia),"After the genuine expiration, the same native physical Tengjia again prevents this exact ordinary Slash's damage.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-hu-che-er",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var kind in new[]{CardKind.Slash,CardKind.Dodge,CardKind.Peach,CardKind.GudingBlade,CardKind.ZhangbaSerpentSpear,CardKind.Tengjia,CardKind.OffensiveHorse,CardKind.DefensiveHorse,CardKind.WoodenOx}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mhce-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[{"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
                  {"id":"{{Extra}}","revision":1,"triggers":[{"id":"actual-extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"actual-native-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels=JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="原生装备安置",description="安置实际手牌装备。"},[Extra]=new{name="真实插入出牌",description="准备前原生插入一次出牌。"},[Quiet]=new{name="安静同行",description="正常摸牌后跳过出牌。"},[Hold]=new{name="真实杀子返回",description="真实用杀子窗口。",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}});
            var catalog=SkillProgramCatalog.Load(rules,labels);foreach(var id in new[]{Driver,Extra,Quiet,Hold})b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=catalog.Programs[id],ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Plain,"真实孔武目标","","standard:none","wei",4,[Quiet]));
            b.AddDeck(new(Deck,"固定真实闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("fixture:mhce-"+CardKind.Dodge,Suit.Club,7)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋胡车儿真实主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Plain],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
