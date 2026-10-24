using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundarySunXiuChecks
{
    private const string Yanzhu="boundary:yanzhu-mobile",Xingxue="boundary:xingxue-mobile",Upgraded="boundary:xingxue-mobile-upgraded",
        Zhaofu="boundary:zhaofu-mobile",Owner="fixture:boundary-sun-xiu-owner",Driver="fixture:boundary-sun-xiu-driver",
        Quiet="fixture:boundary-sun-xiu-quiet",Watch="fixture:boundary-sun-xiu-watch",Mode="identity:classic-boundary-sun-xiu";

    public static void WholeEquipmentUpgradeAndSequentialSelectedGiftsPaidCold()
    {
        var s=Scene.Start();s.Play();s.Driver("equip-target",1);
        s.Reach(()=>s.P?.Choices.Any(c=>c.Cards.Count==1&&c.Parameters.ContainsKey("source-zone"))==true);
        s.Answer(c=>c.Cards.Count==1);s.Play();var equipment=s.V(1).Equipment.Single().Id;
        s.Yanzhu(1);s.Option("equipment");s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
        s.Need(s.V(1).Equipment.Count==0&&s.V(0).Hand.Any(c=>c.Id==equipment)&&s.Has(Yanzhu)&&!s.Has(Upgraded),
            "The real complete equipment transfer finishes before the shared terminal skill replacement.");
        s.Cold();s.Answer(_=>true);s.Play();
        s.Need(!s.Has(Yanzhu)&&!s.Has(Xingxue)&&s.Has(Upgraded)&&s.E<ProgramOwnerSkillsReplacedEvent>().Count(e=>e.SkillId==Yanzhu)==1&&
            s.G.CardMovements.Count(m=>m.CardId==equipment&&m.From==CardLocation.Equipment(1)&&m.To==CardLocation.Hand(0))==1,
            "Cold returning the paid equipment child removes Yanzhu and replaces Xingxue exactly once through shared skill changes.");
        s.Driver("wound");s.Play();s.Need(s.V(0).Hp==1&&s.V(0).MaxHp==4,"The native fixture leaves current HP1 and maximum HP4 for the upgraded quota.");
        s.End();s.ActivateEnding(Upgraded);s.Branch("participant");
        foreach(var target in new[]{2,0,1})s.Answer(c=>Branch(c)=="participant"&&c.Targets.SequenceEqual([target]));
        s.Answer(c=>Branch(c)=="finish");s.AtCard(0);
        var frame=s.Root!;var selected=frame.BoundarySunXiu!.Participants.ToArray();
        s.Need(selected.SequenceEqual([0,1,2])&&frame.BoundarySunXiu.Quota==4&&s.P!.Choices.Where(c=>Branch(c)=="give").All(c=>c.Targets is[1]or[2])&&
            s.P.Choices.Any(c=>Branch(c)=="top"),
            "Three recipients exceed current HP1 but fit maximum HP4; selection freezes action order and only this invocation's other selected recipients may receive cards.");
        var card=s.P!.Choices.First(c=>Branch(c)=="give"&&c.Targets.SequenceEqual([1])).Cards.Single();
        s.Cold();s.Answer(c=>Branch(c)=="give"&&c.Targets.SequenceEqual([1])&&c.Cards.SequenceEqual([card]));
        s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
        s.Need(s.V(1).Hand.Any(c=>c.Id==card)&&s.E<BoundarySunXiuDrawReturnedEvent>().Select(e=>e.Participant).SequenceEqual([0])&&
            s.Root?.BoundarySunXiu?.Stage==BoundarySunXiuStage.MoveChildren&&s.E<BoundarySunXiuCardMovedEvent>() is[{Index:0,Participant:0,Recipient:1}],
            "The first chosen participant really gives its original card before the next participant's native draw; the same paid ending frame waits for the movement observer.");
        s.Cold();s.Answer(_=>true);s.AtCard(1);
        s.Answer(c=>Branch(c)=="give"&&c.Targets.SequenceEqual([2])&&c.Cards.SequenceEqual([card]));
        s.Reach(()=>s.E<BoundarySunXiuCompletedEvent>().Any(e=>e.FrameId==frame.Id));
        s.Need(s.E<BoundarySunXiuDrawReturnedEvent>().Where(e=>e.FrameId==frame.Id).Select(e=>(e.Index,e.Participant,e.Actual)).SequenceEqual([(0,0,1),(1,1,1),(2,2,1)])&&
            s.E<BoundarySunXiuCardMovedEvent>().Count(e=>e.FrameId==frame.Id)==3&&
            s.G.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(1)&&m.Reason.Value=="skill.boundary-sun-xiu.xingxue.give")==1&&
            s.G.CardMovements.Count(m=>m.CardId==card&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(2)&&m.Reason.Value=="skill.boundary-sun-xiu.xingxue.give")==1,
            "Each sealed participant draws and disposes of one real card in order, including a later selected participant passing on the actual original gift without duplicated payments after cold restore.");
        s.Cold();
    }

    public static void RegionObtainRetainsSkillBaseQuotaAndPassiveLordRange()
    {
        var s=Scene.Start(true);s.Play();
        s.Need(s.Distance(2,4)==2&&s.Within(2,4)&&!s.Within(1,3),
            "The qualified Sun Xiu lord puts a truly distant Wu attacker's target at lord-distance1 into attack range while ordinary distance and unqualified targets stay unchanged.");
        var delay=s.V(0).Hand.First().Id;
        var action=s.G.GetHumanLegalActions().First(a=>a.CardId==delay&&a.TargetSeats.SequenceEqual([1]));
        s.Accept(new PlayCardCommand(0,delay,action.TargetSeats,s.G.Revision,s.P!.PromptId,action.PlayedCardKind,action.TargetCardId));s.Play();
        s.Need(s.V(1).Judgment.Any(c=>c.Id==delay),"The fixture places an actual ordinary delayed trick in the target's judgment region.");
        s.Yanzhu(1);s.Option("obtain");s.Reach(()=>s.P?.Choices.Any(c=>c.Cards.SequenceEqual([delay]))==true);
        s.Answer(c=>c.Cards.SequenceEqual([delay]));s.Play();
        s.Need(!s.V(1).Judgment.Any(c=>c.Id==delay)&&s.V(0).Hand.Any(c=>c.Id==delay)&&s.Has(Yanzhu)&&s.Has(Xingxue)&&!s.Has(Upgraded)&&
            s.G.GetHumanLegalActions().All(a=>a.ProgramSkillId!=Yanzhu),
            "The mobile second option obtains a real judgment-region entity without discarding it or upgrading skills, and consumes the one phase use.");
        s.Driver("wound");s.Play();s.End();s.ActivateEnding(Xingxue);
        s.Reach(()=>s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId==Xingxue&&f.HandControlDraft?.Stage=="participants"));
        s.Answer(c=>c.Parameters.GetValueOrDefault("hand-control-action")=="participant"&&c.Targets.SequenceEqual([0]));
        var baseFrame=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId==Xingxue);
        s.Need(baseFrame.HandControlDraft is{Maximum:1,ParticipantSeats.Count:1}&&s.P!.Choices.All(c=>c.Parameters.GetValueOrDefault("hand-control-action")!="participant"),
            "Unupgraded Xingxue reuses its real HP1 quota and offers no second participant after one selection.");
        s.Answer(c=>c.Parameters.GetValueOrDefault("hand-control-action")=="finish");
        s.Reach(()=>s.P?.Choices.Any(c=>c.Parameters.GetValueOrDefault("stage")=="participant-top")==true);
        s.Answer(c=>c.Parameters.GetValueOrDefault("hand-control-action")=="card");
        s.Reach(()=>s.E<ProgramParticipantCardPlacedOnTopEvent>().Any(e=>e.ResolutionId==baseFrame.Id));
    }

    private static string? Branch(PromptChoice choice)=>choice.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(ContentRegistry registry,GameEngine game)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,5).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public ProgramSkillFrame? Root=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.BoundarySunXiu is not null);
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public bool Has(string skill)=>V(0).Skills!.Any(s=>s.Id==skill);
        public void Play()=>Reach(()=>P is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
        public void End()=>Accept(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId));
        public void Driver(string activation,int? target=null)=>Accept(new UseProgramSkillCommand(0,BoundarySunXiuChecks.Driver,activation,[],target is{}t?[t]:[],G.Revision,P!.PromptId));
        public void Yanzhu(int target)=>Accept(new UseProgramSkillCommand(0,BoundarySunXiuChecks.Yanzhu,"equipment-or-obtain",[],[target],G.Revision,P!.PromptId));
        public void Option(string option){Reach(()=>P?.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")==option)==true);Answer(c=>c.Parameters.GetValueOrDefault("option-id")==option);}
        public void ActivateEnding(string skill){Reach(()=>P?.SkillPrompt?.SkillId==skill&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
        public void Branch(string branch)=>Reach(()=>P?.Choices.Any(c=>BoundarySunXiuChecks.Branch(c)==branch)==true);
        public void AtCard(int actor)=>Reach(()=>P?.PlayerSeat==actor&&Root?.BoundarySunXiu?.Stage==BoundarySunXiuStage.Card);
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var answer=p.Choices.First(predicate);Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,answer.Id,G.Revision));}
        public int Distance(int actor,int target)=>G.GetCombatDistance(actor,target);
        public bool Within(int actor,int target)=>(bool)typeof(GameEngine).GetMethod("IsWithinAttackRange",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(G,[actor,target])!;
        public void Accept(GameCommand command){var result=G.Submit(command);Need(result.Accepted,result.Error?.Message??"Rejected Sun Xiu fixture command.");}
        public void Reach(Func<bool> done)
        {
            for(var i=0;i<220&&!done();i++)
            {
                if(P is{Kind:DecisionKind.SelectGeneral} select)
                {var id=select.PlayerSeat==0?Owner:select.ValidContentIds.First();Accept(new SelectGeneralCommand(select.PlayerSeat,id,G.Revision,select.PromptId));continue;}
                if(P is{Kind:DecisionKind.ProgramTrigger,PlayerSeat:0 or 1} prompt)
                {
                    var choice=prompt.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")??
                        (prompt.Choices.All(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target")?prompt.Choices.SingleOrDefault():null);
                    if(choice is not null){Answer(c=>ReferenceEquals(c,choice));continue;}
                    if(prompt.SkillPrompt?.SkillId==BoundarySunXiuChecks.Yanzhu&&prompt.PlayerSeat==1&&prompt.Choices.Any(c=>c.Cards.Count==1))
                    {Answer(c=>c.Parameters.GetValueOrDefault("owned-selection-role")=="confirm"||c.Cards.Count==1);continue;}
                    throw new InvalidOperationException("Unexpected native Sun Xiu fixture boundary: "+JsonSerializer.Serialize(prompt));
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(done(),"The bounded native Sun Xiu fixture missed its expected boundary.");
        }
        public void Cold()
        {
            var views=Enumerable.Range(0,5).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray();
            G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);
            Need(views.SequenceEqual(Enumerable.Range(0,5).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))))&&frames.SequenceEqual(G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType()))),
                "Cold preserves the once-paid native equipment upgrade or sealed participant/card/gift cursor on its original ending frame.");
        }
        public void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public static Scene Start(bool trickDeck=false)
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(trickDeck));
            var game=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=5,HumanSeat=0,HumanSeats=[0,1],HumanRole=Role.Lord,
                ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);
            var scene=new Scene(registry,game);scene.Accept(new StartGameCommand());scene.Play();return scene;
        }
    }
    private sealed class Fixture(bool trickDeck):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-boundary-sun-xiu",new(1,0,0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder,"boundary:sun-xiu-mobile");
            var catalog=SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],"activations":[
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]},
                {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"equipment-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:yanzhu-mobile.MoveBoundCards"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"upgrade-paid","options":[{"id":"continue"}]}]},
                {"id":"gift-paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.boundary-sun-xiu.xingxue.give"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift-paid","options":[{"id":"continue"}]}]}
              ]}
            ]}
            """,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{
                [Driver]=new{name="孙休真实输入",description="原生装备转移和失血"},[Quiet]=new{name="安静同桌",description="保留原生行动顺序"},
                [Watch]=new{name="已付卡牌观察者",description="实际装备/交牌后的原生子窗",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}}}));
            foreach(var(id,program)in catalog.Programs)builder.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=program,ProgramPresentation=catalog.Presentations[id]});
            builder.AddGeneral(new(Owner,"孙休固定输入","supporter",Yanzhu,"wu",3,AdditionalSkillIds:[Xingxue,Zhaofu,Driver,Watch]));
            var peers=Enumerable.Range(1,4).Select(i=>"fixture:boundary-sun-xiu-peer-"+i).ToArray();
            foreach(var peer in peers)builder.AddGeneral(new(peer,"吴势力同桌","supporter",Quiet,"wu",8));
            const string deck="fixture:boundary-sun-xiu-deck";
            builder.AddDeck(new(deck,"固定孙休实体",4,0,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(trickDeck?"standard:indulgence":"standard:crossbow",Suit.Heart,i%13+1)).ToArray()});
            builder.AddMode(new(Mode,"孙休真实双人选择",5,5,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2},{nameof(Role.Renegade),1}},deck,
                GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
