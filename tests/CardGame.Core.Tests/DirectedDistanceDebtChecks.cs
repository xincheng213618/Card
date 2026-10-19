using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

// Focused behavior drafts. No compiler, loader, seed search, game or test was run while authoring them.
internal static class DirectedDistanceDebtChecks
{
    private const string Mode = "identity:classic-fixed-distance-fixture", Driver = "fixture:fixed-distance-driver";
    public static void ShortRangeNativeRedirectCursorAndFireOrdering()
    {
        // Real command/replay prefix ends at the first native Liuli opportunity.
        // First-target redirect precedes preparation/finalized ShortRange; AI
        // answers below are explicitly host answers and carry no Cold claim.
        var (plain,pr)=Start(redirects:true); Grant(plain,2); ReachPlay(plain); PlayPrimarySlash(plain);
        Reach(plain,p=>p.SkillPrompt?.SkillId=="boundary:liuli" && p.PlayerSeat==1 &&
            p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")); Cold(plain,pr);
        var initial=plain.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.CardAttack is not null);
        RedirectHost(plain,1,2);
        Reach(plain,p=>p.SkillPrompt?.SkillId=="ol:duanbing" && p.Choices.Any(c=>c.Targets.SequenceEqual([3])));
        Choose(plain,c=>c.Targets.SequenceEqual([3]));
        var issued=Facts<ShortRangeSlashTargetResolvedEvent>(plain).Single(e=>e.Added).Receipt!;
        Require(issued.OriginalTargets.SequenceEqual([2]) && Facts<CardActionAcceptedEvent>(plain).Any(e=>
            e.Action.ActionId==issued.ActionId && e.Action.TargetSeats.SequenceEqual([2])) &&
            Facts<ProgramActualSlashTargetRedirectedEvent>(plain).Length==0,
            "The first native redirect finishes before the exact accepted actual prefix and its finalized short-range tail.");
        RedirectHost(plain,3,2);
        var current=plain.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==initial.Id);
        var redirected=Facts<ProgramActualSlashTargetRedirectedEvent>(plain).Single();
        Require(redirected.TargetIndex==1 && redirected.OriginalTargetSeat==3 && redirected.NewTargetSeat==2 &&
            current.TargetSeats.SequenceEqual([2,2]) && current.Action!.TargetSeats.SequenceEqual([2,3]) &&
            (bool)InvokeHost(plain,"HasShortRangeSlashTail",current)!,
            "Only the true tail receives its later native redirect; actual seats may duplicate while the action retains its frozen finalization.");
        var unchanged=State(plain);
        Require(!(bool)InvokeHost(plain,"IsShortRangeSlashRedirectFact",current,redirected with {TargetIndex=0},new[]{2,3})! && State(plain)==unchanged,
            "A forged tail redirect index fails without mutating the authentic cursor or paid facts.");
        var rejected=false;
        InvokeHost(plain,"ReplaceRuntimeFrame",current.Id,current with {TargetIndex=current.TargetSeats.Count});
        try { InvokeHost(plain,"RedirectCardUseTarget",current.Id,current.TargetSeats[0],1); }
        catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException error && error.Message.Contains("redirected Slash target",StringComparison.Ordinal)) { rejected=true; }
        finally { InvokeHost(plain,"ReplaceRuntimeFrame",current.Id,current); }
        Require(rejected && State(plain)==unchanged,"A bad native cursor throws the exact redirect error and preserves the authentic state.");
        ReachPlay(plain);
        Require(Facts<CardUseFinishedEvent>(plain).Count(e=>e.ResolutionId==initial.Id)==1 &&
            Facts<ProgramCurrentSlashFireRedirectedEvent>(plain).Length==0,
            "The physical whole-use finishes once after first-before-finalized and true-tail-after-finalized redirects.");

        // These three branches are the required generic virtual return cases:
        // plain, converted fire, and fire-extra before the short-range tail.
        foreach(var branch in new[]{0,1,2})
        {
            var fire=branch>0; var fireExtra=branch==2;
            FeibaiDeclaredNameEvent? originalFeibaiName=null;
            CurrentTurnCardUseKindsRecordedEvent? originalUseSequence=null;
            var (g,r)=Start(fire:fire,redirects:true); Grant(g,2); ReachPlay(g);
            Accept(g,new UseProgramSkillCommand(0,Driver,"virtual",[],[1],g.Revision,P(g)!.PromptId));
            if(fire)
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:lihuo" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
                var declaring=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.CardAttack is not null);
                var actionId=declaring.Action!.ActionId;
                originalFeibaiName=Facts<FeibaiDeclaredNameEvent>(g).Single(e=>e.ActionId==actionId);
                originalUseSequence=Facts<CurrentTurnCardUseKindsRecordedEvent>(g).Single(e=>e.CardActionId==actionId);
                Require(originalFeibaiName.EffectiveKind==CardKind.Slash && declaring.Action.EffectiveKind==CardKind.Slash &&
                    originalUseSequence.WholeActualTurnUseSequence is >0,
                    "Before true Lihuo conversion, Feibai records the actual ordinary Slash declaration at its issued whole-turn use position.");
                Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
                Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="current-slash-fire"));
                Choose(g,c=>c.Parameters.GetValueOrDefault("branch")== (fireExtra?"extra":"convert") &&
                    (fireExtra?c.Targets.SequenceEqual([2]):c.Targets.Count==0));
            }
            Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:liuli" && p.PlayerSeat==1 &&
                p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")); Cold(g,r);
            var originalUse=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.CardAttack is not null);
            var originalParent=originalUse.CardAttack!.ProgramSkillCardUseFrameId!.Value;
            RedirectHost(g,1,2);
            var tail=3;
            Reach(g,p=>p.SkillPrompt?.SkillId=="ol:duanbing" && p.Choices.Any(c=>c.Targets.SequenceEqual([tail])));
            Choose(g,c=>c.Targets.SequenceEqual([tail]));
            var use=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==originalUse.Id);
            var producer=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==originalParent);
            var prefix=fireExtra?new[]{2,2}:new[]{2};
            Require(use.ShortRangeSlashTarget!.OriginalTargets.SequenceEqual(prefix) && Facts<CardActionAcceptedEvent>(g).Any(e=>
                e.Action.ActionId==use.Action!.ActionId && e.Action.TargetSeats.SequenceEqual(prefix)),
                "The complete native finalized prefix, including a legitimate repeated actual seat, proves the tail issuance.");
            object?[] arguments=[use,producer,null];
            Require((bool)typeof(GameEngine).GetMethod("TryGetOriginalTargetVirtualSlashReturn",Flags)!.Invoke(g,arguments)! &&
                arguments[2] is IReadOnlyList<int> returned && returned.SequenceEqual([1]) && producer.SelectedTargetSeats.SequenceEqual([1]),
                "Generic virtual completion retains the declared original singleton after actual first-target redirection and short-range finalization.");
            if(fire)
            {
                var rebuilt=((int[] Designated,int[] Actual,int[] Planned))InvokeHost(g,"RebuildCurrentSlashFireTargets",use)!;
                Require(rebuilt.Designated.SequenceEqual(prefix.Append(tail)) && rebuilt.Actual.SequenceEqual(prefix.Append(tail)) &&
                    rebuilt.Planned.SequenceEqual(fireExtra?new[]{1,2}:new[]{1}) &&
                    Facts<ProgramCurrentSlashFireRedirectedEvent>(g).Count(e=>e.CardUseFrameId==use.Id && e.TargetIndex==0)==1 &&
                    Facts<ProgramActualSlashTargetRedirectedEvent>(g).Length==0,
                    "Fire reconstruction consumes the exact accepted prefix and tail once, while retaining the original prepared source plan.");
                Reach(g,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="completed-use-payment"));
                var completedUse=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==use.Id);
                var actionId=completedUse.Action!.ActionId;
                var feibai=Facts<FeibaiUsePrefixCapturedEvent>(g).Single(e=>e.ActionId==actionId);
                var recorded=Facts<CurrentTurnCardUseKindsRecordedEvent>(g).Single(e=>e.CardActionId==actionId);
                var actualTurnUses=Facts<CurrentTurnCardUseKindsRecordedEvent>(g).Where(e=>
                    e.TurnNumber==recorded.TurnNumber && e.TurnOwnerSeat==recorded.TurnOwnerSeat).ToArray();
                Require(completedUse.CardKind==CardKind.FireSlash && completedUse.Action.EffectiveKind==CardKind.FireSlash &&
                    feibai is {ActorSeat:0,EffectiveKind:CardKind.FireSlash,NameLength:2} &&
                    feibai.ActionId==use.Action!.ActionId && feibai.ActualTurn==recorded.TurnNumber &&
                    feibai.UseSequence==recorded.WholeActualTurnUseSequence && recorded==originalUseSequence &&
                    Array.IndexOf(actualTurnUses,recorded)+1==recorded.WholeActualTurnUseSequence &&
                    Facts<FeibaiDeclaredNameEvent>(g).Single(e=>e.ActionId==actionId)==originalFeibaiName &&
                    originalFeibaiName is {EffectiveKind:CardKind.Slash},
                    "At the real completed-fire payment child, Feibai freezes the exact same action as two-character FireSlash while retaining its original Slash name fact and unchanged whole-actual-turn use position. "+
                    JsonSerializer.Serialize(new{ActionId=actionId,feibai,Declared=originalFeibaiName,recorded}));
                Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==originalParent).SelectedTargetSeats.SequenceEqual([1]),
                    "The true converted-fire finish writes the original producer singleton before its real mandatory payment.");
                Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="completed-use-payment" && c.Parameters.GetValueOrDefault("branch")=="lose-hp");
            }
            Reach(g,p=>p.SkillPrompt?.SkillId==Driver && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue" &&
                c.Parameters.GetValueOrDefault("result-bind")=="virtual-return"));
            Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==originalParent).SelectedTargetSeats.SequenceEqual([1]) &&
                Facts<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==use.Id && e.CardKind==(fire?CardKind.FireSlash:CardKind.Slash))==1,
                "The native whole-use return reaches its actual next producer instruction once with the original selected singleton.");
            Choose(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue"); ReachPlay(g);
        }
    }
    private static void PlayPrimarySlash(GameEngine g)
    {
        var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]) &&
            a.ConversionSource is null && a.PlayedCardKind is null or CardKind.Slash);
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind));
    }
    private static void RedirectHost(GameEngine g,int owner,int target)
    {
        Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:liuli" && p.PlayerSeat==owner && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        var use=g.ResolutionStack.OfType<CardUseFrame>().Last(u=>u.CardAttack is not null); var index=use.TargetIndex;
        HostAnswer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Reach(g,p=>p.PlayerSeat==owner && p.Choices.Any(c=>c.Targets.SequenceEqual([target])));
        HostAnswer(g,c=>c.Targets.SequenceEqual([target]));
        Reach(g,p=>p.PlayerSeat==owner && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));
        HostAnswer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card" && c.Parameters.GetValueOrDefault("skip")!="true");
        // Before ShortRange issuance the native normal redirect has no new
        // opt-in scalar fact. Wait on the exact owning actual cursor instead.
        ReachState(g,()=>g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==use.Id && u.TargetIndex==index &&
            index>=0 && index<u.TargetSeats.Count && u.TargetSeats[index]==target));
        // Host answers queue facts outside Submit; commit them at the held
        // human boundary before reading the committed event history.
        Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void HostAnswer(GameEngine g,Func<PromptChoice,bool> predicate)
    {
        var router=typeof(GameEngine).GetProperty("ProgramInteractions",Flags)!.GetValue(g)!;
        router.GetType().GetMethod("Resolve",Flags)!.Invoke(router,[P(g)!.Choices.First(predicate)]);
        InvokeHost(g,"AdvanceRulesAndPublishState");
    }
    private static object? InvokeHost(GameEngine g,string method,params object?[] args) => typeof(GameEngine).GetMethod(method,Flags)!.Invoke(g,args);
    private static (GameEngine,ContentRegistry) Start(bool fire=false,bool redirects=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(fire,redirects));
        var g=GameEngine.CreateStandard(new GameOptions { Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,
            UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=6 },r);
        Accept(g,new StartGameCommand()); Reach(g,p=>p.Kind==DecisionKind.SelectGeneral && p.PlayerSeat==0);
        Accept(g,new SelectGeneralCommand(0,"fixture:fixed-owner",g.Revision,P(g)!.PromptId)); ReachPlay(g); return(g,r);
    }
    private static void Grant(GameEngine g,int target) => Accept(g,new UseProgramSkillCommand(0,"ol:fenxun","fixed-one-actual-turn",[],[target],g.Revision,P(g)!.PromptId));
    private static T[] Facts<T>(GameEngine g) where T:IGameEvent => g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Choose(GameEngine g,Func<PromptChoice,bool> predicate)
    { var p=P(g)!; Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision)); }
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard && p.PlayerSeat==0);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)=>ReachState(g,()=>P(g) is {} p && predicate(p));
    private static void ReachState(GameEngine g,Func<bool> predicate)
    { for(var n=0;n<128;n++){if(predicate())return; Advance(g);} throw new InvalidOperationException($"Fixed distance fixture missed its named boundary: turn={g.State.TurnNumber}, phase={g.State.Phase}, prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId}, uses={JsonSerializer.Serialize(g.ResolutionStack.OfType<CardUseFrame>())}, redirects={JsonSerializer.Serialize(Facts<ProgramActualSlashTargetRedirectedEvent>(g))}."); }
    private static void Advance(GameEngine g)
    {
        var p=P(g);
        if(p is {PlayerSeat:0,Kind:DecisionKind.DiscardCards}) Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else if(p is {PlayerSeat:0} && p.SkillPrompt?.SkillId==Driver && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue")) Choose(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        else if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")) Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g,GameCommand command)
    { var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted,r.Error?.Message??"Distance command rejected."); }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static void Cold(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Cold replay preserves directed grants and exact original debt/target/payment frames.");
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class Fixture(bool fire=false,bool redirects=false):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-directed-distance-debt",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,
              "activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,
                "effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"virtual-return","options":[{"id":"continue"}]}]}],
              "triggers":[{"id":"hold-damage-cursor","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"damage-return","options":[{"id":"continue"}]}]}]}]}
            """;
            var c=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=3,skills=new Dictionary<string,object>{[Driver]=new{name=Driver,description="实际回合伤害",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}}}));
            foreach(var p in c.Programs)b.AddSkill(new(p.Key,p.Key,"实际回合伤害"){Program=p.Value});
            b.AddSkill(new("fixture:fixed-idle","空闲角色","无触发"));
            var auxiliary=new List<string>{"ol:fenxun",Driver}; if(fire)auxiliary.Add("boundary:lihuo");
            b.AddGeneral(new("fixture:fixed-owner","固定距离","supporter","ol:duanbing","wu",12,auxiliary,GeneralGender.Male));
            for(var n=1;n<4;n++)b.AddGeneral(new($"fixture:fixed-peer-{n}","固定对手","supporter",redirects?"boundary:liuli":"fixture:fixed-idle","wei",12,[],GeneralGender.Male));
            b.AddDeck(new("fixture:fixed-deck","同质有限杀",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Heart,7)).ToArray()});
            b.AddMode(new(Mode,"真实固定距离",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:fixed-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:fixed-owner","fixture:fixed-peer-1","fixture:fixed-peer-2","fixture:fixed-peer-3"]));
        }
    }
}
