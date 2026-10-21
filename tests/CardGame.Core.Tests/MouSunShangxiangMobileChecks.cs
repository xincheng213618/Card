using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouSunShangxiangMobileChecks
{
    private const string Hero="mou:sun-shangxiang-mobile",Driver="fixture:mssx-driver",Hold="fixture:mssx-obtain-hold",Quiet="fixture:mssx-quiet",
        Ally="fixture:mssx-ally",EnemyA="fixture:mssx-enemy-a",EnemyB="fixture:mssx-enemy-b",Deck="fixture:mssx-deck";
    public static void ActualGiftZhuangRepeatedHolderFailureAndWuXiaojiCold()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(7,1,"mssx-real-mission",Hero,3,0,0,0,0,[Driver,Hold],
            [new(CardKind.SilverLion,Suit.Club,1),new(CardKind.Tengjia,Suit.Spade,2),new(CardKind.ZhangbaSerpentSpear,Suit.Spade,12)],
            [new(CardKind.Crossbow,Suit.Club,1)],
            [new(EnemyA,Hp:8){StartingHand=[]},new(EnemyB,Hp:8){StartingHand=[]}],
            [new(Ally,Hp:3){StartingHand=[new(CardKind.Dodge,Suit.Heart,3)]}],new string('c',64));
        var g=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,
            ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=16},registry);
        PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
        T[] E<T>()=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        PlayerSnapshot V(int seat=0)=>g.CreateSnapshot(0).Players[seat];
        IEnumerable<CardSnapshot> Zhuang()=>(V().PublicPersistentPileSkillId=="mou-mobile:liangzhu"?V().PublicPersistentPileCards??[]:[])
            .Concat(V().PublicPersistentPiles?.Where(p=>p.SourceSkillId=="mou-mobile:liangzhu").SelectMany(p=>p.Cards)??[]);
        string Diagnostic()=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Prompt=P(),Players=g.CreateSnapshot(0).Players,
            Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=g.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
        void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message+Diagnostic());}
        void Submit(GameCommand command)
        {
            try{var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(r.Accepted,r.Error?.Message??"Actual command rejected.");}
            catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}
        }
        void Answer(Func<PromptChoice,bool> predicate)
        {var p=P()!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"The exact published human choice is required.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
        void Reach(Func<bool> done,[CallerLineNumber]int line=0)
        {
            for(var i=0;i<140;i++)
            {
                if(done())return;Need(g.State.Winner==Winner.None,"Actual mission ended before "+line);
                if(P()is{PlayerSeat:0}p)
                {
                    if(p.Kind==DecisionKind.DiscardCards)Submit(new DiscardCardsCommand(0,p.ValidCardIds.OrderBy(id=>V().Hand.Single(c=>c.Id==id).Kind==CardKind.ZhangbaSerpentSpear?1:0).Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
                    else throw new InvalidOperationException("Unexpected actual human boundary at "+line+Diagnostic());
                }
                else Submit(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Bounded native Sun Shangxiang flow not reached at "+line+Diagnostic());
        }
        void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        void End()=>Submit(new EndPlayPhaseCommand(0,g.Revision,P()!.PromptId));
        void Place(CardKind kind,int target)
        {
            var id=V().Hand.Single(c=>c.Kind==kind).Id;
            Submit(new UseProgramSkillCommand(0,Driver,"place",[id],[target],g.Revision,P()!.PromptId));Play();
        }
        bool Transfer()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="remove");
        Submit(new StartGameCommand());
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="holder"));
        Need(P()!.Choices.All(c=>!c.Targets.Contains(0)),"The actual initial Zhu recipients exclude Sun Shangxiang herself.");
        Answer(c=>c.Targets.SequenceEqual([1]));Play();
        Need(E<MouSsxGiftCommittedEvent>()is[{HolderSeat:1,Count:1}]&&V(1).Armor==1&&V(1).HandCount==0&&
            g.CardMovements.Count(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.mou-mobile:jieyin.give")==1,
            "The first actual Play asks the marked holder, transfers its sole physical Hand card and then grants exactly one native armor.");
        Place(CardKind.SilverLion,1);Place(CardKind.Tengjia,2);
        var lion=V(1).Equipment.Single(c=>c.Kind==CardKind.SilverLion).Id;var field=V(2).Equipment.Single(c=>c.Kind==CardKind.Tengjia).Id;
        Submit(new UseProgramSkillCommand(0,"mou-mobile:liangzhu","deposit-zhuang",[],[],g.Revision,P()!.PromptId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="deposit"));
        Answer(c=>c.Cards.SequenceEqual([lion]));Play();
        Need(Zhuang().Any(c=>c.Id==lion)&&V(1).Hp==4&&
            E<SilverLionRemovedRecoveryEvent>().Any(e=>e.PlayerSeat==1&&e.Reason.Value=="skill-program.mou-mobile:liangzhu.zhuang"&&e.RecoveredAmount==1)&&
            E<MouSsxDrawReturnedEvent>()is[{RecipientSeat:1,Requested:2,Actual:2}]&&!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="mou-mobile:liangzhu"),
            "Liangzhu deposits the real foreign SilverLion, preserves its native removal recovery, then pays the marked holder's selected Draw2 and the current Play debit.");
        End();Reach(Transfer);
        Need(P()!.Choices.Where(c=>c.Parameters.GetValueOrDefault("branch")=="holder").All(c=>!c.Targets.Contains(0)&&!c.Targets.Contains(1)),
            "The first holder's genuine empty-Hand request offers another role or removal, excluding self and the existing holder.");
        Answer(c=>c.Targets.SequenceEqual([2]));Play();End();Reach(Transfer);
        Answer(c=>c.Targets.SequenceEqual([1]));Play();End();
        Reach(()=>P()?.SkillPrompt?.SkillId==Hold);
        Need(E<MouSsxZhuChangedEvent>().Select(e=>e.CurrentSeat).SequenceEqual([1,2,1,-1])&&E<MouSsxMissionFailedEvent>()is[{OwnerSeat:0,LastHolderSeat:1}]&&
            V().Hp==4&&V().MaxHp==4&&(V().FactionId??registry.Generals[V().GeneralId].FactionId)=="shu"&&V().Hand.Any(c=>c.Id==lion)&&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.MouSsx is{Stage:MouSsxStage.FailureObtainChildren,Paid:true})&&E<MouSsxFailureStepEvent>().Select(e=>e.Step).SequenceEqual([1,2]),
            "The regranted holder's empty-Hand request forces removal; native recovery precedes real Zhuang retrieval, whose actual gain child suspends faction and max-HP payment.");
        var before=SnapshotJson.Serialize(g.CreateSnapshot(0));
        g=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(before==SnapshotJson.Serialize(g.CreateSnapshot(0)),"A representative Cold restores the once-paid failure and its exact real Zhuang gain child.");
        Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Play();
        Need(V().Hp==3&&V().MaxHp==3&&V().FactionId=="wu"&&E<MouSsxMissionFailedEvent>().Length==1&&
            E<MouSsxFailureStepEvent>().Select(e=>e.Step).SequenceEqual([1,2,3,4])&&E<NativeEffectiveFactionChangedEvent>()is[{TargetSeat:0,PreviousFactionId:"shu",CurrentFactionId:"wu",Cause:"jieyin-failure"}]&&
            E<MaximumHpChangedEvent>().Count(e=>e.PlayerSeat==0&&e.SkillId=="mou-mobile:jieyin"&&e.Delta==-1)==1&&
            !Zhuang().Any(c=>c.Id==lion)&&!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="mou-mobile:liangzhu"),
            "After Cold the exact original failure returns once, changes real effective faction to Wu and reduces max HP once; current faction disables Liangzhu.");
        var spear=V().Hand.Single(c=>c.Kind==CardKind.ZhangbaSerpentSpear).Id;
        var action=g.GetHumanLegalActions().Single(a=>a.CardId==spear&&a.ConversionSource is null);
        Submit(new PlayCardCommand(0,spear,action.TargetSeats,g.Revision,P()!.PromptId,action.PlayedCardKind,action.TargetCardId));
        Reach(()=>P()is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="discard"));
        Need(E<MouSsxDrawReturnedEvent>().Any(e=>e.RecipientSeat==0&&e.Requested==2&&e.Actual==2),"Actual Wu Equipment replacement pays native Xiaoji Draw2 before the optional field discard.");
        Answer(c=>c.Cards.SequenceEqual([field]));Play();
        var discards=g.CardMovements.Where(m=>m.CardId==field&&m.From==CardLocation.Equipment(2)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.mou-mobile:xiaoji.discard").ToArray();
        var completed=E<MouSsxCompletedEvent>().Where(e=>e.Operation==SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji).ToArray();
        Need(discards is[var actual]&&completed is[var finished]&&
            E<MouSsxMovedEvent>().Count(e=>e.FrameId==finished.FrameId&&e.CardId==actual.CardId&&e.From==actual.From&&e.To==actual.To&&e.Sequence==actual.Sequence)==1,
            "Xiaoji discards the chosen actual field entity through one native logical discard and returns the original Equipment Use once.");
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-mou-sun-shangxiang-mobile",new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b,Hero);
            foreach(var kind in new[]{CardKind.Dodge,CardKind.SilverLion,CardKind.Tengjia,CardKind.Crossbow,CardKind.ZhangbaSerpentSpear})
            {var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mssx-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
            var rules=$$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                {"id":"{{Hold}}","revision":1,"triggers":[{"id":"actual-zhuang-retrieval","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mou-mobile:jieyin.failure-obtain"],"optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"failure-hold","options":[{"id":"continue"}]}]}]}]}
                """;
            var labels=new Dictionary<string,object>{[Driver]=new{name="真实装备安置",description="将实际手牌装备置于其他角色装备区"},[Quiet]=new{name="安静空手同行",description="正常摸牌后实际弃置全部手牌并跳过出牌阶段"},
                [Hold]=new{name="真实取妆子窗",description="失败实际取回妆后暂停",optionLabels=new Dictionary<string,string>{["continue"]="继续原使命失败"}}};
            var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
            foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
            b.AddGeneral(new(Ally,"实际盟友","",Quiet,"shu",4));foreach(var id in new[]{EnemyA,EnemyB})b.AddGeneral(new(id,"实际敌方","",Quiet,"wei",8));
            b.AddDeck(new(Deck,"固定真实闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,90).Select(_=>new ContentDeckPhysicalCard("fixture:mssx-"+CardKind.Dodge,Suit.Diamond,2)).ToArray()});
            b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋孙尚香实际使命流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,EnemyA,EnemyB],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
