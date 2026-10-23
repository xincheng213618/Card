using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenLuBuChecks
{
    private const string Hero="happy:shen-lu-bu",Driver="fixture:happy-lb-driver",Watch="fixture:happy-lb-watch",Quiet="fixture:happy-lb-quiet",Help="fixture:happy-lb-help",Legacy="fixture:happy-lb-legacy";
    public static void WuqianActualStartAndFirstSlashDuelUseNativeTargetsCold()
    {
        var(g,r)=Start(false);Basic(g);var first=Root(g)!;
        Need(first.HappyLuBu is{Stage:HappyLuBuStage.BasicChildren,Paid.Count:1}&&first.PendingMovementContinuation is not null&&E<TurnStartedEvent>(g).Length==1,
            "The very first actual turn obtains one genuine basic entity before normal phases and awaits its original real gain child.");
        g=Cold(g,r);Continue(g);LegacyOffer(g,false);Answer(g,c=>ActionKey(c)=="skip");LegacyOffer(g,true);Answer(g,c=>ActionKey(c)=="skip");Play(g);EquipOther(g,CardKind.RenwangShield,1);
        Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip&&V(g).Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.Crossbow));Play(g);
        var hp1=V(g,1).Hp;var hp2=V(g,2).Hp;var hp3=V(g,3).Hp;
        Native(g,Action(g,CardKind.Slash,1));Target(g,3);Play(g);
        var slash=E<HappyLuBuUseAppliedEvent>(g).Single();
        Need(V(g,1).Hp==hp1-1&&V(g,3).Hp==hp3&&V(g,1).HandCount==0&&V(g,3).HandCount==0&&V(g,1).Equipment.Any(c=>c.Kind==CardKind.RenwangShield)&&
            E<RequiredResponseProgressEvent>(g).Any(e=>e.ResolutionId==slash.UseId&&e.ResponderSeat==1&&e.RequiredResponseCount==2&&e.ResponseCount==1)&&
            E<RequiredResponseProgressEvent>(g).Any(e=>e.ResolutionId==slash.UseId&&e.ResponderSeat==3&&e.RequiredResponseCount==2&&e.ResponseCount==2),
            "The first black Slash ignores the real Renwang Shield, adds one normally reachable target and requires two genuine Dodges from each target.");
        Native(g,Action(g,CardKind.Duel,2));Target(g,1);Play(g);
        var duel=E<HappyLuBuUseAppliedEvent>(g).Last();
        Need(E<HappyLuBuUseAppliedEvent>(g).Length==2&&duel.UseId!=slash.UseId&&V(g,2).Hp==hp2-1&&V(g,1).Hp==hp1-2&&
            E<RequiredResponseProgressEvent>(g).Any(e=>e.ResolutionId==duel.UseId&&e.ResponderSeat==2&&e.RequiredCardKind==CardKind.Slash&&e.RequiredResponseCount==2&&e.ResponseCount==1),
            "The first Duel has its own independent first-use slot and its very first native response already requires two Slashes. Actual="+JsonSerializer.Serialize(new{InitialHp=new[]{hp1,hp2,hp3},Hp=Enumerable.Range(0,4).Select(s=>V(g,s).Hp),Hands=Enumerable.Range(0,4).Select(s=>V(g,s).HandCount),Applied=E<HappyLuBuUseAppliedEvent>(g),DuelResponses=E<RequiredResponseProgressEvent>(g).Where(e=>e.IncomingCard==CardKind.Duel),Damage=E<DamageAppliedEvent>(g),Declarations=E<CardUseDeclaredEvent>(g).Where(e=>e.CardKind==CardKind.Duel)}));
        Native(g,Action(g,CardKind.Duel,2));Play(g);
        var before=V(g,1).Hp;Native(g,Action(g,CardKind.FireSlash,1));Play(g);
        Need(E<HappyLuBuUseAppliedEvent>(g).Length==2&&V(g,2).Hp==hp2-2&&V(g,1).Hp==before,
            "A later Duel receives no extra target or double-response policy, and a later black fire Slash shares the consumed Slash slot and is stopped by Renwang Shield.");
        Use(g,"face-down");Play(g);End(g);Basic(g);
        Need(V(g).IsFaceDown&&E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0)==2&&E<HappyLuBuBasicPaidEvent>(g).Length==2,
            "Wuqian obtains its one basic even on the real face-down turn before the native skip is processed.");
        var skipped=E<TurnStartedEvent>(g).Last().TurnNumber;Continue(g);Basic(g);
        Need(!V(g).IsFaceDown&&E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0&&e.TurnNumber==skipped)&&E<HappyLuBuActualStartReturnedEvent>(g).Length==2,
            "The paid actual-start child returns once and the genuine face-up skip then ends that same actual turn.");
        Continue(g);LegacyOffer(g,false);Answer(g,c=>ActionKey(c)=="activate");Reach(g,p=>p.SkillPrompt?.SkillId==Legacy&&p.Choices.Any(c=>c.Targets.SequenceEqual([1])));Answer(g,c=>c.Targets.SequenceEqual([1]));
        Reach(g,p=>Root(g)?.HappyLuBu?.Stage==HappyLuBuStage.AdditionalTarget&&p.Choices.Any(c=>ActionKey(c)=="happy-lu-bu-target"));
        var legacyUse=Root(g)!.HappyLuBu!.UseId;var legacyProducer=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Legacy).Id;var primaryHp=V(g,1).Hp;var secondaryHp=V(g,3).Hp;
        Need(g.ResolutionStack.OfType<CardUseFrame>().Single(f=>f.Id==legacyUse) is{CardId:0,PhysicalCardIds.Count:0,Action.Type:CardActionType.Use},
            "A registered legacy useVirtualCard(false) producer opts into the existing native Use windows for the actual Wuqian holder instead of silently consuming the first Slash slot.");
        g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([3]));LegacyOffer(g,true);Answer(g,c=>ActionKey(c)=="skip");Play(g);
        Need(V(g,1).Hp==primaryHp-1&&V(g,3).Hp==secondaryHp-1&&E<HappyLuBuUseAppliedEvent>(g).Count(e=>e.UseId==legacyUse)==1&&
            E<CurrentCardEnhancedEvent>(g).Count(e=>e.CardUseFrameId==legacyUse&&e.Enhancement==CurrentCardEnhancement.ExtraTarget)==1&&
            E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==legacyProducer&&e.SkillId==Legacy&&e.Completed)==1,
            "The restored zero-entity Slash gains real armor bypass and one additional target, completes both native targets, then returns the original producer's single primary target once.");
        End(g);Basic(g);Continue(g);LegacyOffer(g,true);Answer(g,c=>ActionKey(c)=="activate");Reach(g,p=>p.SkillPrompt?.SkillId==Legacy&&p.Choices.Any(c=>c.Targets.SequenceEqual([1])));Answer(g,c=>c.Targets.SequenceEqual([1]));
        Reach(g,p=>Root(g)?.HappyLuBu?.Stage==HappyLuBuStage.AdditionalTarget&&p.Choices.Any(c=>ActionKey(c)=="happy-lu-bu-target"));
        var nativeUse=Root(g)!.HappyLuBu!.UseId;var nativeProducer=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Legacy).Id;primaryHp=V(g,1).Hp;secondaryHp=V(g,3).Hp;
        Answer(g,c=>c.Targets.SequenceEqual([3]));Play(g);
        Need(V(g,1).Hp==primaryHp-1&&V(g,3).Hp==secondaryHp-1&&E<HappyLuBuUseAppliedEvent>(g).Count(e=>e.UseId==nativeUse)==1&&
            E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==nativeProducer&&e.SkillId==Legacy&&e.Completed)==1,
            "An already explicit native useVirtualCard(true) producer also finishes its first Slash with two targets and returns the original single primary target exactly once.");
        Need(State(g)==State(GameReplay.Replay(Options(false),g.AcceptedCommands,r)),"Actual-start payment, independent first names, native multi-target responses and a face-down skipped turn replay from accepted commands.");
    }
    public static void ShenfenPaidHpGroupsAndForeignTurnFullHpResetCold()
    {
        var(g,r)=Start(true);Basic(g);Continue(g);Play(g);EquipOther(g,CardKind.RenwangShield,1);EquipOther(g,CardKind.BaguaFormation,2);
        var hp=V(g).Hp;var hpOthers=Enumerable.Range(1,3).Select(s=>V(g,s).Hp).ToArray();var start=g.Events.Count;
        Shenfen(g);Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        var paid=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="happy:shenfen");
        Need(V(g).Hp==hp-3&&E<ProgramSkillHpLostEvent>(g).Count(e=>e.FrameId==paid.Id&&e.Amount==3)==1&&E<DamageAppliedEvent>(g).All(e=>e.SourceSeat!=0),
            "Limited Shenfen first really pays three HP and waits for the actual HP-loss child before anyone takes skill damage.");
        g=Cold(g,r);Continue(g);Play(g);
        var facts=g.Events.Skip(start).Select(e=>e.Payload).ToArray();
        var damageLast=Array.FindLastIndex(facts,e=>e is DamageAppliedEvent d&&d.SourceSeat==0);
        var equipmentFirst=Array.FindIndex(facts,e=>e is CardMovedEvent m&&m.Reason.Value=="program.participants.discard-equipment");
        var equipment=g.CardMovements.Where(m=>m.Reason.Value=="program.participants.discard-equipment").ToArray();
        var hands=g.CardMovements.Where(m=>m.Reason.Value=="program.participants.discard-hand").ToArray();
        Need(V(g).Hp==hp-3&&Enumerable.Range(1,3).All(s=>V(g,s).Hp==hpOthers[s-1]-1&&V(g,s).Equipment.Count==0&&V(g,s).HandCount==2)&&
            damageLast>=0&&equipmentFirst>damageLast&&equipment.Length==2&&hands.Length==12&&equipment.Max(m=>m.Sequence)<hands.Min(m=>m.Sequence)&&
            E<ProgramSkillHpLostEvent>(g).Count(e=>e.FrameId==paid.Id&&e.Amount==3)==1&&!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="happy:shenfen"),
            "After cold return Shenfen pays no second cost, completes all three native damage targets before all equipment discards, then each character's actual four hand-card discard.");
        End(g);Offer(g,Help);Answer(g,c=>ActionKey(c)=="activate");Reach(g,p=>p.SkillPrompt?.SkillId==Help&&p.Choices.Any(c=>c.Targets.SequenceEqual([0])));Answer(g,c=>c.Targets.SequenceEqual([0]));
        Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        Need(V(g).Hp==V(g).MaxHp&&E<HappyLuBuShenfenResetEvent>(g).Length==0&&E<TurnStartedEvent>(g).Last().ActorSeat==1,
            "A real other character's native turn-ending recovery reaches full HP before that actual turn has completed; Shenfen has not reset early.");
        Continue(g);Basic(g);Continue(g);Play(g);
        Need(E<HappyLuBuShenfenResetEvent>(g) is[{EndedOwner:1,Before:1}]&&g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="happy:shenfen"),
            "The completed foreign turn really clears only Shenfen's consumed native game usage; later full-HP turns do not manufacture further refunds.");
        Shenfen(g);Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
        Need(E<ProgramSkillHpLostEvent>(g).Count(e=>e.SkillId=="happy:shenfen"&&e.TargetSeat==0&&e.Amount==3)==2&&V(g).Hp==hp-3,
            "The genuinely reset limited skill can be accepted again and pays its own new three-HP cost exactly once.");
    }
    private static GameOptions Options(bool groups)
    {
        ShanheStartingCard[] many=Enumerable.Range(0,6).Select(i=>new ShanheStartingCard(CardKind.Slash,(Suit)(i%4),i+1)).ToArray();
        var setup=new ShanheBattleSetup(7,1,"happy-lu-bu",Hero,6,0,0,0,0,[],
            [new(CardKind.Crossbow,Suit.Club,1),new(CardKind.RenwangShield,Suit.Club,2),new(CardKind.BaguaFormation,Suit.Spade,2),new(CardKind.Slash,Suit.Spade,7),new(CardKind.FireSlash,Suit.Club,4),new(CardKind.Duel,Suit.Spade,1),new(CardKind.Duel,Suit.Heart,1)],[],
            [new(Peer(2),Hp:groups?8:4){StartingHand=groups?many:[new(CardKind.Slash,Suit.Club,7)]},new(Peer(3)){StartingHand=groups?many:[new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Dodge,Suit.Diamond,6)]}],
            [new(Peer(1)){StartingHand=groups?many:[new(CardKind.Dodge,Suit.Heart,9)]}],new string('f',64)){DefaultGodFactionId="qun"};
        return new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=14};
    }
    private static(GameEngine,ContentRegistry)Start(bool groups){var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(groups));var g=GameEngine.CreateStandard(Options(groups),r);Accept(g,new StartGameCommand());return(g,r);}
    private static string Peer(int seat)=>"fixture:happy-lb-peer-"+seat;
    private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.HappyLuBu is not null);
    private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static string? ActionKey(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool valid,string why){if(!valid)throw new InvalidOperationException(why);}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Happy Lu Bu command.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool>pick){var p=P(g)!;var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing Happy Lu Bu published choice: "+JsonSerializer.Serialize(p));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
    private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static LegalAction Action(GameEngine g,CardKind kind,int target)=>g.GetHumanLegalActions().First(a=>a.CardId is{}id&&V(g).Hand.Single(c=>c.Id==id).Kind==kind&&a.TargetSeats.SequenceEqual([target]));
    private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Use(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
    private static void Shenfen(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"happy:shenfen","limited-destruction",[],[],g.Revision,P(g)!.PromptId));
    private static void Basic(GameEngine g)=>Reach(g,p=>p.SkillPrompt?.SkillId==Watch&&Root(g)?.HappyLuBu?.Operation==SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic);
    private static void Play(GameEngine g)=>Reach(g,p=>p is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
    private static void Offer(GameEngine g,string skill)=>Reach(g,p=>p.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>ActionKey(c)=="activate"));
    private static void LegacyOffer(GameEngine g,bool native)=>Reach(g,p=>p.SkillPrompt?.SkillId==Legacy&&p.Choices.Any(c=>ActionKey(c)=="activate"&&c.Parameters.GetValueOrDefault("binding-id")== (native?"registered-native-slash":"registered-legacy-slash")));
    private static void Target(GameEngine g,int target){Reach(g,p=>Root(g)?.HappyLuBu?.Stage==HappyLuBuStage.AdditionalTarget&&p.Choices.Any(c=>ActionKey(c)=="happy-lu-bu-target"));Answer(g,c=>c.Targets.SequenceEqual([target]));}
    private static void EquipOther(GameEngine g,CardKind kind,int target){var card=V(g).Hand.First(c=>c.Kind==kind).Id;Use(g,"equip-other",[target]);Reach(g,p=>p.Choices.Any(c=>c.Cards.SequenceEqual([card])));Answer(g,c=>c.Cards.SequenceEqual([card]));Play(g);}
    private static void Step(GameEngine g)
    {
        if(P(g) is{}p){if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);return;}if(p.SkillPrompt?.SkillId==Watch){Continue(g);return;}if(p.PlayerSeat==0)throw new InvalidOperationException("Unexpected Happy Lu Bu boundary: "+JsonSerializer.Serialize(new{Prompt=p,Root=Root(g)?.HappyLuBu,Hp=Enumerable.Range(0,4).Select(s=>V(g,s).Hp),Applied=E<HappyLuBuUseAppliedEvent>(g),Reset=E<HappyLuBuShenfenResetEvent>(g)}));}
        Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g,Func<PendingDecision,bool>ready){for(var n=0;n<280;n++){if(P(g) is{}p&&ready(p))return;Step(g);}throw new InvalidOperationException("Happy Lu Bu missed its bounded native boundary.");}
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold restore preserves all real pending payments, original native parents, four views and typed return state.");return restored;}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private sealed class Fixture(bool groups):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-happy-lu-bu",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b,groups),Hero);
            var json=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"face-down","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"turnOver","target":"owner"}]},
                {"id":"equip-other","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"native-basic","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.happy-wuqian.basic"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]},
                {"id":"native-hp-loss","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"lost","options":[{"id":"continue"}]}]},
                {"id":"native-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovered","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Help}}","revision":1,"triggers":[{"id":"real-foreign-heal","window":"turnEnding","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"recover","target":"selectedTarget","amount":3}]}]},
              {"id":"{{Legacy}}","revision":1,"triggers":[
                {"id":"registered-legacy-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"priority":1,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":false}]},
                {"id":"registered-native-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]}]}
            """;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实装备及翻面",description="原生装备转交及实际翻面。"},[Watch]=new{name="真实支付子窗",description="真实获牌、HP流失及回复后暂停。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},[Quiet]=new{name="安静角色",description="跳过出牌。"},[Help]=new{name="实际外回合回复",description="在另一个实际回合结束前进行真正回复。"}};
            labels[Legacy]=new{name="既有虚拟杀生产者",description="使用声明关闭一般卡窗的既有真实生产者。"};
            var catalog=SkillProgramCatalog.Load(json,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"Native Happy Lu Bu fixture"){Program=p,ProgramPresentation=catalog.Presentations[id]});
            for(var s=1;s<4;s++)b.AddGeneral(new(Peer(s),"真实目标","supporter",Quiet,"wei",8){AdditionalSkillIds=groups&&s==1?[Help]:[]});
            b.AddDeck(new("fixture:happy-lb-deck","小型固定实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard("standard:slash",(Suit)(i%4),i%13+1)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"固定配牌欢乐吕布",4,4,new Dictionary<string,int>(),"fixture:happy-lb-deck",GeneralPoolIds:[Hero,Peer(1),Peer(2),Peer(3)],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{{ShanheBattleSetup.PlayerTeamId,2},{ShanheBattleSetup.EnemyTeamId,2}}));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b,bool groups):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==Hero?d with{AdditionalSkillIds=[..d.AdditionalSkillIds!,Driver,Watch,..(groups?Array.Empty<string>():new[]{Legacy})]}:d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
    }
}
