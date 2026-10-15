using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class IdentityConcealmentViewChecks
{
    public static void ActualProjectedConcealmentAndAppearanceBridge(string output)
    {
        foreach(var own in new[]{true,false})
        {
            var registry=ContentRegistry.Build(new StandardContentPackage(),new AppearanceFixture());
            var game=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Rebel,ModeId=AppearanceFixture.Mode,
                UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6},registry);
            Accept(game,new StartGameCommand());Reach(game,()=>Prompt(game) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});
            var general=own?"ol:xuan-gong-zhu":Prompt(game)!.Choices.Select(c=>c.Parameters.GetValueOrDefault("general-id")).OfType<string>().First(id=>id.StartsWith("fixture:conceal-ui-peer-",StringComparison.Ordinal));
            Accept(game,new SelectGeneralCommand(0,general,game.Revision,Prompt(game)!.PromptId));
            Reach(game,()=>game.CreateSnapshot(0).Players.Any(p=>p.IdentityConcealed && (own?p.Seat==0:p.Seat!=0)));
            var hidden=game.CreateSnapshot(0).Players.First(p=>p.IdentityConcealed && (own?p.Seat==0:p.Seat!=0));
            Program.Assert(hidden.HpUndefined && hidden.Hp==0 && hidden.MaxHp==0 && hidden.IsAlive &&
                (own?hidden.GeneralId=="ol:xuan-gong-zhu":hidden.GeneralId=="concealment:ol" && hidden.GeneralName=="隐匿" && hidden.Skills is null or { Count:0 }),
                "A real selected concealed identity supplies projected undefined HP to every viewer, with only owner knowing its real template.");
            var store=new MemorySaveStore();store.Write(GameSaveSlot.Manual,new(1,DateTimeOffset.UtcNow,false,game.CreateCheckpoint()));
            using var model=new MainViewModel(false,7,true,store,useExpandedContent:false,contentRegistry:registry){IsMotionEnabled=false,IsSoundEnabled=false};model.LoadManualGameCommand.Execute(null);
            var restored=Program.Engine(model);var seat=model.Seats.Single(s=>s.Seat==hidden.Seat);
            Program.Assert(seat.IdentityConcealed && seat.HpUndefined && seat.IsAlive && seat.HpText.Contains("未定义",StringComparison.Ordinal) && !seat.HealthImages.Any() &&
                seat.Portrait.GeneralId==hidden.GeneralId && !model.HumanSummary.Contains("0/0",StringComparison.Ordinal) &&
                (own?model.HumanActiveSkillActions.Count==0:seat.Kingdom=="隐匿" && seat.SkillName.Length==0),
                "Checkpoint-restored MainViewModel copies only projected identity/HP fields, exposes no concealed public skills, and offers no inactive owner skill operation. "+
                JsonSerializer.Serialize(new{own,HiddenSeat=hidden.Seat,seat.IdentityConcealed,seat.HpUndefined,seat.IsAlive,seat.HpText,HealthImages=seat.HealthImages.Count(),
                    Portrait=seat.Portrait.GeneralId,ExpectedGeneral=hidden.GeneralId,model.HumanSummary,seat.Kingdom,seat.SkillName,
                    Actions=model.HumanActiveSkillActions,model.CanUseActiveSkill,UseCommandCanExecute=model.UseActiveSkillCommand.CanExecute(null),
                    Restored=restored.CreateSnapshot(0).Players[hidden.Seat],Prompt=restored.CreateSnapshot(0).PendingDecision}));
            var window=new MainWindow(model){Width=1120,Height=820,ShowActivated=false,ShowInTaskbar=false};var root=(FrameworkElement)window.Content;
            try
            {
                Program.Render(root,1120,820,Path.Combine(output,$"identity-concealment-{(own?"own":"public")}-before-appearance.png"));
                for(var step=0;step<100 && restored.CreateSnapshot(0).Players[hidden.Seat].IdentityConcealed;step++)model.StepAiCommand.Execute(null);
                Program.Assert(!restored.CreateSnapshot(0).Players[hidden.Seat].IdentityConcealed && restored.Events.Select(e=>e.Payload).OfType<IdentityGeneralAppearedEvent>().Any(e=>e.Receipt.OwnerSeat==hidden.Seat),
                    "Actual native own-turn progression produces appearance, without mutating a checkpoint or injecting a player snapshot.");
                var appeared=model.Seats.Single(s=>s.Seat==hidden.Seat);
                Program.Render(root,1120,820,Path.Combine(output,$"identity-concealment-{(own?"own":"public")}-after-appearance.png"));
                Program.Assert(ReferenceEquals(seat,appeared) && !appeared.IdentityConcealed && !appeared.HpUndefined && appeared.Hp>0 && appeared.MaxHp>0 &&
                    appeared.HealthImages.Any() && appeared.GeneralId=="ol:xuan-gong-zhu" && appeared.Portrait.GeneralId=="ol:xuan-gong-zhu" && appeared.Portrait.Image is not null,
                    "Real appearance refreshes the retained visible seat, its defined health and its newly public official portrait.");
            }
            finally{window.Content=null;window.DataContext=null;window.Close();}
        }
    }
    private static PendingDecision?Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(c);Program.Assert(r.Accepted,r.Error?.Message??"Rejected actual concealment UI fixture command.");}
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for(var step=0;step<100;step++){if(done())return;Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed concealment fixture did not reach its native setup boundary. "+JsonSerializer.Serialize(new{Prompt=Prompt(g),Players=g.CreateSnapshot(0).Players,Frames=g.ResolutionStack}));
    }
    private static SeatViewModel Seat(string id,string name,bool hidden,int hp,int maximum)=>new()
    {
        Seat=1,DisplaySeatNumber=2,GeneralId=id,GeneralName=name,Portrait=new(id),Name=name,Kingdom=hidden?"隐匿":"晋",RoleLabel="?",
        Hp=hp,MaxHp=maximum,IdentityConcealed=hidden,HpUndefined=hidden,HpText=hidden?"隐匿中 · 体力未定义":$"体力 {hp}/{maximum}",
        HandCount=4,HandText="手牌4",EquipmentText="装备 —",JudgmentText="判定 —",DistanceText="距离1",SkillText=hidden?string.Empty:"高陵、齐眉、追姬",
        SkillName=hidden?string.Empty:"高陵、齐眉、追姬",IsAlive=true,IsCurrent=false,IsHuman=false
    };
    private sealed class AppearanceFixture:IGameContentPackage
    {
        internal const string Mode="identity:classic-concealment-ui",Quiet="fixture:conceal-ui-quiet";
        public PackageManifest Manifest{get;}=new("concealment-ui-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-xuan-gong-zhu."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
            void Add(SkillProgramCatalog cat){foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,cat.Presentations[id].Name,cat.Presentations[id].Description){Program=p,ProgramPresentation=cat.Presentations[id],
                SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,r=>id==Quiet?10000d:r==Role.Lord?-20000d:20000d)});}
            Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}""";
            Add(SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静身份同桌",description="固定跳过出牌"}}})));
            b.AddGeneral(new("ol:xuan-gong-zhu","宣公主·OL","ol-xuan-gong-zhu","ol:gaoling","jin",3,["ol:qimei","ol:zhuiji-tj"],GeneralGender.Female));
            var peers=Enumerable.Range(0,5).Select(i=>$"fixture:conceal-ui-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"普通同桌"+id[^1],"supporter",Quiet,"wei",12));
            b.AddDeck(new("fixture:conceal-ui-deck","固定身份四杀",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray()});
            b.AddMode(new(Mode,"真实隐匿出现界面",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:conceal-ui-deck",GeneralCandidateCount:6,GeneralPoolIds:["ol:xuan-gong-zhu",..peers]));
        }
    }
}
