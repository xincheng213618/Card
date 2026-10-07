using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

// Focused behavior drafts. No compiler, loader, seed search, game or test was run while authoring them.
internal static class DirectedDistanceDebtChecks
{
    private const string Mode = "identity:classic-fixed-distance-fixture", Driver = "fixture:fixed-distance-driver";
    public static void FixedOneActualTurnZeroCostEndingPhysicalOnce()
    {
        var (g,r) = Start(); var before = g.CardMovements.Count; var hand = View(g).Hand.Count;
        Require(g.GetCombatDistance(0,2) == 2 && g.GetCombatDistance(2,0) == 2, "The original directed pair is genuinely distance two.");
        Grant(g,2); ReachPlay(g);
        Require(View(g).Hand.Count == hand && g.CardMovements.Count == before && g.GetCombatDistance(0,2) == 1 &&
            g.GetCombatDistance(2,0) == 2 && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == "ol:fenxun"),
            "The real Play grant pays no upfront card, fixes only its one direction and consumes one actual-Play quota.");
        Cold(g,r); End(g); Reach(g, p => p.SkillPrompt?.SkillId == "ol:fenxun");
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FixedDistanceDebtPayment is not null);
        Require(frame.WindowContext?.FixedDistanceDebt == Facts<FixedDistanceOneTurnGrantedEvent>(g).Single().Grant &&
            Facts<FixedDistanceOneDebtPaidEvent>(g).Length == 0 && P(g)!.Choices.All(c => c.Cards.Count == 1),
            "Entering the real Ending owns one unpaid, original-source HE choice."); Cold(g,r);
        var id = P(g)!.Choices[0].Cards.Single(); Choose(g,_ => true); ReachTurnDeparture(g);
        var paid = Facts<FixedDistanceOneDebtPaidEvent>(g).Single();
        Require(paid.CardId == id && g.CardMovements.Count(m => m.CardId == id && m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter &&
            m.From.OwnerSeat == 0 && m.To == CardLocation.DiscardPile && m.Reason.Value == "program.fixed-distance-one.ending-discard") == 1 &&
            g.GetCombatDistance(0,2) == 2 && Facts<FixedDistanceOneDebtConsumedEvent>(g).Single().RequiredDiscard,
            "One actual entity pays exactly once, and directed Set=1 ends at the real turn departure."); Cold(g,r);
    }
    public static void ShortRangeOneTailActualSlashAndNativeDoubleDodge()
    {
        var (g,r) = Start();
        Require(Response(g,1) == 2 && Response(g,2) == 1 && Response(g,3) == 2,
            "The native Slash response query distinguishes actual directed distance one from distance two.");
        Grant(g,2); ReachPlay(g); Require(Response(g,2) == 2, "The final fixed-one rule also feeds the native response query.");
        var card = View(g).Hand.First(c => c.Kind == CardKind.Slash).Id;
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == card && a.TargetSeats.SequenceEqual([1]));
        Accept(g,new PlayCardCommand(0,card,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind));
        Reach(g,p => p.SkillPrompt?.SkillId == "ol:duanbing" && p.Choices.Any(c => c.Targets.SequenceEqual([2])));
        Require(P(g)!.Choices.All(c => c.Targets.Count <= 1) && P(g).Choices.Any(c => c.Targets.Count == 0),
            "The real final-use opportunity exposes at most one tail target and a decline, rather than a larger initial target cap."); Cold(g,r);
        Choose(g,c => c.Targets.SequenceEqual([2])); ReachPlay(g);
        var added = Facts<ShortRangeSlashTargetResolvedEvent>(g).Single(e => e.Added);
        Require(added.Receipt is { } receipt && receipt.OriginalTargets.SequenceEqual([1]) && receipt.AddedTargetSeat == 2 && receipt.ActionId is not null &&
            Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat is 1 or 2 && e.Amount > 0) == 2 &&
            Facts<CardUseFinishedEvent>(g).Count(e => e.CardKind == CardKind.Slash) == 1 &&
            Facts<CardUseDebitRecordedEvent>(g).Length == 1 && g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "Both actual targets resolve before the single whole-use finish, physical payment and ordinary Slash debit.");
        var targets = added.Receipt!.OriginalTargets;
        if (targets is IList<int> list) { try { list[0]=3; throw new InvalidOperationException("Mutable receipt."); } catch (NotSupportedException) { } }
        Cold(g,r);
    }
    public static void WholeActualTurnPriorDamageAndIssuedSourceLossHostBoundary()
    {
        var (g,r) = Start();
        Accept(g,new UseProgramSkillCommand(0,Driver,"damage",[],[2],g.Revision,P(g)!.PromptId)); ReachPlay(g);
        Require(Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount > 0 && !e.SourceLess),
            "The source actually damaged the target before distance issuance."); Grant(g,2); ReachPlay(g); Cold(g,r);
        End(g); ReachTurnDeparture(g);
        Require(Facts<FixedDistanceOneDebtConsumedEvent>(g).Single().RequiredDiscard == false && Facts<FixedDistanceOneDebtPaidEvent>(g).Length == 0,
            "Whole-turn positive applied damage before the grant fulfils the original debt."); Cold(g,r);
        var (lost,lr) = Start(); Grant(lost,2); ReachPlay(lost); Cold(lost,lr);
        // Narrow host audit following command-replayed issuance. These host mutations carry no replay-acceptance claim.
        var owner = HostPlayers(lost)[0]; var original = owner.SkillGrants.Grants.Single(s => s.SkillId == "ol:fenxun");
        owner.SkillGrants.RemoveGrant(original.GrantId);
        Require(lost.GetCombatDistance(0,2) == 1, "An issued fixed policy retains its original source after skill loss.");
        HostPlayers(lost)[2].IsAlive=false;
        var items=new List<TurnEndingBoundaryItem>();
        typeof(GameEngine).GetMethod("AppendFixedDistanceOneEndingItems",Flags)!.Invoke(lost,[items]);
        Require(items is [{ FixedDistanceDebt.TargetSeat: 2 }], "Original target death does not erase an unfulfilled debt.");
        HostPlayers(lost)[2].IsAlive=true;
        End(lost); Reach(lost,p => p.SkillPrompt?.SkillId == "ol:fenxun");
        Require(lost.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FixedDistanceDebtPayment is not null).SkillInstanceId == original.SkillInstanceId,
            "The original issued Ending resolver is retained without a current source grant.");
        Choose(lost,_ => true); ReachTurnDeparture(lost);
        Require(Facts<FixedDistanceOneDebtPaidEvent>(lost).Length == 1, "The retained original binding pays one real card once.");
    }
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
        try { InvokeHost(plain,"RedirectCardUseTarget",current.Id,current.TargetSeats[current.TargetIndex],1); }
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
            var (g,r)=Start(fire:fire,redirects:true); Grant(g,2); ReachPlay(g);
            Accept(g,new UseProgramSkillCommand(0,Driver,"virtual",[],[1],g.Revision,P(g)!.PromptId));
            if(fire)
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:lihuo" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
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
                Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==originalParent).SelectedTargetSeats.SequenceEqual([1]),
                    "The true converted-fire finish writes the original producer singleton before its real mandatory payment.");
                Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="completed-use-payment" && c.Parameters.GetValueOrDefault("branch")=="lose-hp");
            }
            Reach(g,p=>p.SkillPrompt?.SkillId==Driver && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
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
    }
    private static void HostAnswer(GameEngine g,Func<PromptChoice,bool> predicate)
    { InvokeHost(g,"ResolveProgramTriggerChoice",P(g)!.Choices.First(predicate)); InvokeHost(g,"AdvanceRulesAndPublishState"); }
    private static object? InvokeHost(GameEngine g,string method,params object?[] args) => typeof(GameEngine).GetMethod(method,Flags)!.Invoke(g,args);
    private static int Response(GameEngine g,int target) => (int)typeof(GameEngine).GetMethod("GetProgramRequiredResponseCount",Flags)!
        .Invoke(g,[HostPlayers(g)[0],0,target,CardKind.Slash,CardKind.Dodge])!;
    private static (GameEngine,ContentRegistry) Start(bool fire=false,bool redirects=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(fire,redirects));
        var g=GameEngine.CreateStandard(new GameOptions { Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,
            UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=6 },r);
        Accept(g,new StartGameCommand()); Reach(g,p=>p.Kind==DecisionKind.SelectGeneral && p.PlayerSeat==0);
        Accept(g,new SelectGeneralCommand(0,"fixture:fixed-owner",g.Revision,P(g)!.PromptId)); ReachPlay(g); return(g,r);
    }
    private static void Grant(GameEngine g,int target) => Accept(g,new UseProgramSkillCommand(0,"ol:fenxun","fixed-one-actual-turn",[],[target],g.Revision,P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static PlayerSnapshot View(GameEngine g)=>g.CreateSnapshot(0).Players[0];
    private static T[] Facts<T>(GameEngine g) where T:IGameEvent => g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Choose(GameEngine g,Func<PromptChoice,bool> predicate)
    { var p=P(g)!; Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision)); }
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard && p.PlayerSeat==0);
    private static void ReachTurnDeparture(GameEngine g)=>ReachState(g,()=>Facts<TurnStartedEvent>(g).Any(e=>e.ActorSeat==1));
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)=>ReachState(g,()=>P(g) is {} p && predicate(p));
    private static void ReachState(GameEngine g,Func<bool> predicate)
    { for(var n=0;n<128;n++){if(predicate())return; Advance(g);} throw new InvalidOperationException("Fixed distance fixture missed its named boundary."); }
    private static void Advance(GameEngine g)
    {
        var p=P(g);
        if(p is {PlayerSeat:0,Kind:DecisionKind.DiscardCards}) Accept(g,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,g.Revision));
        else if(p is {PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")) Choose(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        else Accept(g,new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g,GameCommand command)
    { var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted,r.Error?.Message??"Distance command rejected."); }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static void Cold(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Cold replay preserves directed grants and exact original debt/target/payment frames.");
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static IReadOnlyList<CharacterState> HostPlayers(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",Flags)!.GetValue(g)!;
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
                "effects":[{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"virtual-return","options":[{"id":"continue"}]}]}]}]}
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
