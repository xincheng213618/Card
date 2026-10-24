using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class BoundaryZhuZhiChecks
{
    private const string Hero="boundary:zhu-zhi-mobile", Skill="boundary:anguo-mobile", Driver="fixture:boundary-zhu-zhi-driver",
        Quiet="fixture:boundary-zhu-zhi-quiet", Watch="fixture:boundary-zhu-zhi-watch", Peer="fixture:boundary-zhu-zhi-peer";

    public static void InitialMarkMoveMaximumHandLimitLethalProtectionAndHpRescueCold()
    {
        var s=Scene.Start(); s.Initial(1); s.Play();
        s.Need(s.Marked(1)&&s.V(1).Hp==3&&s.V(1).MaxHp==8&&s.Limit(1)==8,
            "The real game-start mark sets its holder's hand limit to MaxHP, overriding current HP and the fixture's additive limit.");
        s.Driver("lethal-owner",2); s.Play();
        s.Need(s.V(0).Hp==4&&s.E<ProgramDamagePreventedEvent>().Any(e=>e.SkillId==Skill)&&
            !s.E<DamageAppliedEvent>().Any(e=>e.TargetSeat==0),
            "With a living marked participant, lethal damage from an unmarked real source is prevented before actual HP loss.");
        s.End(); s.Reach(()=>s.P?.SkillPrompt?.SkillId==Skill&&s.P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        s.Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"); s.AtBranch("mark");
        s.Need(s.P!.Choices.All(c=>!c.Targets.Contains(0)&&!c.Targets.Contains(1))&&s.P.Choices.Any(c=>c.Targets.SequenceEqual([2])),
            "A real later Play-start move excludes the owner and every participant who previously received Anguo.");
        s.Cold(); s.Answer(c=>c.Targets.SequenceEqual([2])); s.Play();
        s.Need(!s.Marked(1)&&s.Marked(2)&&s.Limit(1)==23&&s.Limit(2)==8,
            "The one actual mark moves to its selected fresh holder; the former holder immediately regains its ordinary HP-plus-modifier limit.");
        s.Driver("hurt-target",2); s.AtBranch("hp");
        var rescue=s.E<BoundaryZhuZhiRescueIssuedEvent>().Single();
        s.Need(!s.Marked(2)&&s.V(2).Hp==1&&s.V(2).Armor==0&&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==rescue.FrameId).SelectedTargetSeats.SequenceEqual([2]),
            "Entering genuine dying removes the mark and really recovers the original victim to one before the owner's payment and later armor.");
        s.Cold(); s.Answer(c=>Branch(c)=="hp"); s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
        s.Need(s.V(0).Hp==1&&s.V(0).MaxHp==6&&s.V(2).Armor==0&&
            s.E<BoundaryZhuZhiPaymentEvent>() is [{Choice:"hp",HpBefore:4,HpAfter:1,MaximumBefore:6,MaximumAfter:6}]&&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==rescue.FrameId).BoundaryZhuZhi?.Stage==BoundaryZhuZhiStage.PaymentChildren,
            "The exact owner pays native HP loss to one once; its actual HP observer child suspends the original rescue before granting armor.");
        s.Cold(); s.Answer(_=>true); s.Play();
        s.Need(s.V(2).Hp==1&&s.V(2).Armor==1&&s.E<BoundaryZhuZhiPaymentEvent>().Length==1&&
            s.E<ArmorGrantedEvent>().Count(e=>e.ProgramFrameId==rescue.FrameId&&e.PlayerSeat==2&&e.Amount==1)==1,
            "Cold resuming the paid native HP child returns to the same saved victim and grants one armor without paying again.");
    }

    public static void MaximumHpPaymentAndMarkedSourceLethalBypass()
    {
        var s=Scene.Start(); s.Initial(1); s.Play(); s.Driver("hurt-target",1); s.AtBranch("maximum");
        s.Cold(); s.Answer(c=>Branch(c)=="maximum"); s.Play();
        s.Need(s.V(0).MaxHp==1&&s.V(0).Hp==1&&s.V(1).Hp==1&&s.V(1).Armor==1&&
            s.E<BoundaryZhuZhiPaymentEvent>() is [{Choice:"maximum",HpBefore:4,HpAfter:1,MaximumBefore:6,MaximumAfter:1}],
            "The alternative genuinely reduces maximum HP to one, uses the native HP clamp and then grants the original rescued participant one armor.");
        s.Cold();
        var bypass=Scene.Start(); bypass.Initial(1); bypass.Play(); bypass.Driver("lethal-owner",1);
        bypass.Reach(()=>bypass.V(0).Hp<=0);
        bypass.Need(bypass.V(0).Hp==0&&bypass.Marked(1)&&
            bypass.E<DamageAppliedEvent>().Any(e=>e.SourceSeat==1&&e.TargetSeat==0&&e.Amount==4)&&
            bypass.E<ProgramDamagePreventedEvent>().All(e=>e.SkillId!=Skill),
            "A genuinely marked source bypasses Anguo and its lethal native damage still enters the ordinary dying flow.");
    }

    internal static (ContentRegistry Registry,GameEngine Game) CreateScene()
    {
        var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
        var setup=new ShanheBattleSetup(44,1,"boundary-zhu-zhi",Hero,4,2,0,0,0,[Driver,Watch],[],[],
            [new(Peer,Hp:3){StartingHand=[]},new(Peer,Hp:3){StartingHand=[]}], [new(Peer,Hp:3){StartingHand=[]}],new string('c',64));
        var game=GameEngine.CreateStandard(new GameOptions{Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,
            HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,
            UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);
        Accept(game.Submit(new StartGameCommand())); return(registry,game);
    }
    private static string? Branch(PromptChoice c)=>c.Parameters.GetValueOrDefault("branch");
    private static void Accept(CommandResult r){if(!r.Accepted)throw new InvalidOperationException(r.Error?.Message??"Rejected Anguo fixture command.");}
    private sealed class Scene(ContentRegistry registry,GameEngine game)
    {
        public GameEngine G{get;private set;}=game;
        public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
        public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
        public int Limit(int seat)
        {
            var players=(System.Collections.IList)typeof(GameEngine).GetField("_players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(G)!;
            return(int)typeof(GameEngine).GetMethod("GetHandLimit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(G,[players[seat]])!;
        }
        public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
        public bool Marked(int seat)=>(V(seat).Markers??[]).Any(m=>m.Kind==PlayerMarkerKind.MobileBoundaryAnguo&&m.Count>0);
        public void Initial(int target){AtBranch("mark");Answer(c=>c.Targets.SequenceEqual([target]));}
        public void Play()=>Reach(()=>P is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
        public void End()=>Accept(G.Submit(new EndPlayPhaseCommand(0,G.Revision,P!.PromptId)));
        public void AtBranch(string branch)=>Reach(()=>P?.Choices.Any(c=>Branch(c)==branch)==true);
        public void Driver(string activation,int target)=>Accept(G.Submit(new UseProgramSkillCommand(0,BoundaryZhuZhiChecks.Driver,activation,[],[target],G.Revision,P!.PromptId)));
        public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.First(predicate);Accept(G.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,G.Revision)));}
        public void Reach(Func<bool> done)
        {
            for(var i=0;i<240&&!done();i++)
            {
                if(P is{Kind:DecisionKind.ProgramTrigger,PlayerSeat:0} p)
                {if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")){Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="skip");continue;}
                    throw new InvalidOperationException("Unexpected Anguo fixture decision: "+JsonSerializer.Serialize(p));}
                Accept(G.Submit(new AdvanceOneStepCommand(G.Revision)));
            }
            Need(done(),"Bounded native Anguo flow not reached.");
        }
        public void Cold()
        {
            var views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray();
            G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);
            Need(views.SequenceEqual(Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))))&&
                frames.SequenceEqual(G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType()))),
                "Cold preserves the original mark, first-acquisition history, rescued victim, paid native HP child and pending armor.");
        }
        public void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public static Scene Start(){var created=CreateScene();return new(created.Registry,created.Game);}
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-boundary-zhu-zhi",new(1,0,0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder,Hero);
            var catalog=SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],"activations":[
                {"id":"lethal-owner","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":4}]},
                {"id":"hurt-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3}]}]},
              {"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"unrelated-limit","query":"handLimit","operation":"add","value":20,"priority":0}],"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-loss","window":"afterHpLost","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{
                [Driver]=new{name="安国真实输入",description="原生伤害及固定手牌"},[Quiet]=new{name="安静同桌",description="真实轮转"},
                [Watch]=new{name="已付失血观察者",description="失血后的原生子窗",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}}}));
            foreach(var(id,program)in catalog.Programs)builder.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description)
                {Program=program,ProgramPresentation=catalog.Presentations[id]});
            builder.AddGeneral(new(Peer,"固定同桌","supporter",Quiet,"wei",8));
            const string deck="fixture:boundary-zhu-zhi-deck";
            builder.AddDeck(new(deck,"固定空起手",0,0,[]){PhysicalCards=Enumerable.Range(0,60).Select(i=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,i%13+1)).ToArray()});
            builder.AddMode(new(ShanheBattleSetup.BattleModeId,"安国真实结算",4,4,new Dictionary<string,int>(),deck,GeneralPoolIds:[Hero,Peer],ModeKind:ContentModeKind.Team,
                TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
        }
    }
}
