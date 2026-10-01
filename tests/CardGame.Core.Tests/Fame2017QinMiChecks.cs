using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class Fame2017QinMiChecks
{
    public static void PindianRealTopPrivateAndTiedReplay()
    {
        var (game,registry)=Create("both-top",Suit.Spade);StartContest(game,1);
        var offered=Prompt(game)!;var top=offered.Choices.Single(c=>c.Parameters.GetValueOrDefault("action")=="pindian-top");
        Require(top.Cards.Count==0 && !top.Description.Contains("2") && Enumerable.Range(1,3).All(s=>game.CreateSnapshot(s).PendingDecision is null && game.CreateSnapshot(s).PublicRevealedCards.Count==0),"An unchosen top source publishes no actual identity, face or rank to any observer.");
        var originalTop=game.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.DrawPile).OrderBy(c=>c.ZoneIndex).Last().CardId;
        RejectUnknown(game);Replay(game,registry);Answer(game,c=>c.Id==top.Id);
        var frame=(PindianFrame)game.ResolutionStack[^1];Require(frame.SourceUsesDrawPileTop && frame.SourceCardId==originalTop && game.CreateCardZoneDiagnostics().Single(c=>c.CardId==originalTop).Location==CardLocation.Processing,"The optional source reserves the actual top entity once, leaving the opponent a different top card.");
        Require(Enumerable.Range(0,4).All(s=>game.CreateSnapshot(s).PublicRevealedCards.Count==0),"The committed top entity stays concealed until both participants commit.");Replay(game,registry);
        Accept(game,new AdvanceOneStepCommand(game.Revision));Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
        var result=game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Single().Result;
        Require(result.SourceCardId==originalTop && result.SourceCardId!=result.OpponentCardId && result is {SourceRank:2,OpponentRank:2,SourceWon:false} &&
            new[]{result.SourceCardId,result.OpponentCardId}.All(id=>game.CardMovements.Any(m=>m.CardId==id && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing) && game.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location==CardLocation.DiscardPile),"Both source and normal AI opponent use distinct real top cards; a real tied contest is not won and cleans up both cards.");
        Require(!game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat==1),"The opponent's optional top source is selected by native AI.");Replay(game,registry);Answer(game,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Replay(game,registry);
    }
    public static void HandHeartAndEffectiveSuitRank()
    {
        foreach(var suit in new[]{Suit.Heart,Suit.Spade})
        {
            var (game,registry)=Create(suit==Suit.Spade?"hongyan":"offense",suit);var input=game.CreateSnapshot(0).Players[0].Hand.First();StartContest(game,1);Answer(game,c=>c.Cards.Contains(input.Id));
            Replay(game,registry);Accept(game,new AdvanceOneStepCommand(game.Revision));Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
            var result=game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Single().Result;
            Require(result.SourceCardId==input.Id && result.SourceRank==13 && result.OpponentRank==2 && result.SourceWon && input.Rank==2 && game.CardMovements.Any(m=>m.CardId==input.Id && m.From==CardLocation.Hand(0) && m.To==CardLocation.Processing),"The real hand entity remains physical rank two; its configured effective Heart suit produces rank thirteen in the contest.");Replay(game,registry);
        }
    }
    public static void OffensePerTargetCannotRespond()
    {
        var (game,registry)=Create("offense",Suit.Heart);var action=game.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.Count==2);Require(action.TargetSeats.All(t=>game.CreateSnapshot(t).Players[t].Hand.Count(c=>c.Kind==CardKind.Dodge)>=2),"Both real defenders retain actual Dodge alternatives beyond their one Pindian payment.");Play(game,action);
        var opponents=new List<int>(); var before=game.CreateSnapshot(0).Players.Select(p=>p.Hp).ToArray();
        for(var i=0;i<2;i++)
        {
            Reach(game,p=>p.PlayerSeat==0 && p.SkillPrompt?.SkillId=="classic:zhuandui" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Replay(game,registry);RejectUnknown(game);Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
            Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="pindian-source-card"));Answer(game,c=>c.Cards.Count==1);Replay(game,registry);
            Accept(game,new AdvanceOneStepCommand(game.Revision));
            opponents.Add(game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Last().Result.OpponentSeat);
        }
        Reach(game,p=>p.Kind==DecisionKind.PlayCard);var results=game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().ToArray();
        Require(results.Length==2 && results.All(e=>e.Result.SourceWon) && opponents.SequenceEqual(action.TargetSeats) && game.CardMovements.Count(m=>m.CardId==action.CardId && m.To==CardLocation.Processing)==1,"Each frozen Slash target receives an independent winning contest with a single actual Slash payment.");
        Require(action.TargetSeats.All(s=>game.CreateSnapshot(0).Players[s].Hp==before[s]-1),$"Winning offensive contests prohibit each defender's response and preserve each Slash hit. HP={string.Join(",",action.TargetSeats.Select(s=>game.CreateSnapshot(0).Players[s].Hp))}");Replay(game,registry);
    }
    public static void DefenseOwnTargetOnlyAndNoEmptyHandContest()
    {
        var (game,registry)=Create("defense",Suit.Heart);var initialHp=game.CreateSnapshot(0).Players[0].Hp;Use(game,"request",[1]);Reach(game,p=>p.PlayerSeat==0 && p.SkillPrompt?.SkillId=="classic:zhuandui" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(game,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(game,c=>c.Cards.Count==1);Replay(game,registry);Accept(game,new AdvanceOneStepCommand(game.Revision));Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        Require(game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Single().Result.SourceWon && game.CreateSnapshot(0).Players[0].Hp==initialHp && game.Events.Select(e=>e.Payload).OfType<ProgramCardEffectNullifiedEvent>().Single().OwnerSeat==0,$"A defensive win makes only this target's real incoming Slash ineffective. HP={game.CreateSnapshot(0).Players[0].Hp}; nullified={game.Events.Select(e=>e.Payload).OfType<ProgramCardEffectNullifiedEvent>().Count()}");Replay(game,registry);
        Use(game,"discard-all",[]);Reach(game,p=>p.Kind==DecisionKind.PlayCard);Require(game.CreateSnapshot(0).Players[0].HandCount==0,"The fixture empties the real hand before the next request.");
        Use(game,"request",[2]);Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        Require(game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Count()==1 && game.CreateSnapshot(0).Players[0].Hp==initialHp-1,"Top-card replacement never creates Pindian eligibility for an empty hand.");Replay(game,registry);
    }
    public static void DefensiveAiNullifiesOnlyOneOfMultipleTargets()
    {
        var (game,registry)=Create("ai-defense",Suit.Heart);var owner=game.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:qinmi-ai").Seat;
        var action=game.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.Count==2 && a.TargetSeats.Contains(owner));var other=action.TargetSeats.Single(t=>t!=owner);var initial=game.CreateSnapshot(0).Players.Select(p=>p.Hp).ToArray();Play(game,action);
        Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="pindian-card"));Replay(game,registry);RejectUnknown(game);Answer(game,c=>c.Cards.Count==1);Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0).Players[owner].Hp==initial[owner] && game.CreateSnapshot(0).Players[other].Hp==initial[other]-1 && game.Events.Select(e=>e.Payload).OfType<ProgramCardEffectNullifiedEvent>().Single().OwnerSeat==owner,$"Native defensive Pindian nullifies only its actual owner while the other original Slash target still resolves. HP={game.CreateSnapshot(0).Players[owner].Hp},{game.CreateSnapshot(0).Players[other].Hp}; nullified={game.Events.Select(e=>e.Payload).OfType<ProgramCardEffectNullifiedEvent>().Count()}");
        Require(!game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat==owner),"The defensive owner performs both activation and physical Pindian choice through normal AI.");Replay(game,registry);
    }
    public static void InterceptAllTargetsActualPaymentAndAi()
    {
        foreach(var suit in new[]{Suit.Spade,Suit.Heart})
        {
            var (game,registry)=Create("intercept",suit);var owner=game.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:qinmi-ai").Seat;
            var action=game.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && a.TargetSeats.Count==2 && !a.TargetSeats.Contains(owner));var before=Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).Players[s].Hp).ToArray();Play(game,action);
            Reach(game,p=>p.SkillPrompt?.SkillId=="classic:jianzheng");Replay(game,registry);Accept(game,new AdvanceOneStepCommand(game.Revision));
            Reach(game,p=>p.SkillPrompt?.SkillId=="fixture:qinmi-payment-observer");Replay(game,registry);RejectUnknown(game);Accept(game,new AdvanceOneStepCommand(game.Revision));Reach(game,p=>p.Kind==DecisionKind.PlayCard);
            var changed=game.Events.Select(e=>e.Payload).OfType<SlashTargetsReplacedEvent>().Single();var payment=game.CardMovements.Single(m=>m.From==CardLocation.Hand(owner) && m.To==CardLocation.DrawPile);
            Require(changed.PreviousTargets.SequenceEqual(action.TargetSeats) && changed.Targets.SequenceEqual(suit==Suit.Spade?Array.Empty<int>():new[]{owner}) && game.CreateCardZoneDiagnostics().Single(c=>c.CardId==payment.CardId).Location==CardLocation.DrawPile,"Native AI pays one real private hand entity to the true deck and cancels every original target; only a nonblack use adds the interceptor.");
            Require(action.TargetSeats.All(s=>game.CreateSnapshot(0).Players[s].Hp==before[s]) && game.CreateSnapshot(0).Players[owner].Hp==before[owner]-(suit==Suit.Heart?1:0) && !game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"All former targets are spared and the normal AI payment/observer continuations never require a manual AI answer.");Replay(game,registry);
        }
    }
    public static void InterceptRangeAndCurrentTargetQualification()
    {
        var (game,registry)=Create("intercept-far",Suit.Heart);var owner=game.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:qinmi-ai").Seat;Require(game.GetCombatDistance(0,owner)>game.GetAttackRange(0),"The interceptor is outside the physical user's attack range.");
        foreach(var target in Enumerable.Range(1,3).Where(s=>s!=owner)){Use(game,"ignore-range",[target]);Reach(game,p=>p.Kind==DecisionKind.PlayCard);}
        var action=game.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash && !a.TargetSeats.Contains(owner));Play(game,action);Reach(game,p=>p.Kind==DecisionKind.PlayCard);Require(!game.Events.Select(e=>e.Payload).OfType<SlashTargetsReplacedEvent>().Any(),"Slash distance exemption does not provide the interception's attack-range eligibility.");Replay(game,registry);
    }
    public static void EmptyDeckUsesRealReshuffle()
    {
        var (game,registry)=Create("empty-top",Suit.Spade);StartContest(game,1);Require(!Prompt(game)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="pindian-top"),"With neither draw pile nor discard entities, an optional top source is not invented.");Answer(game,c=>c.Cards.Count==1);Accept(game,new AdvanceOneStepCommand(game.Revision));Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));Answer(game,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Reach(game,p=>p.Kind==DecisionKind.PlayCard);
        StartContest(game,1);Replay(game,registry);Answer(game,c=>c.Parameters.GetValueOrDefault("action")=="pindian-top");Replay(game,registry);Accept(game,new AdvanceOneStepCommand(game.Revision));Reach(game,p=>p.PlayerSeat==0 && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
        Require(game.CardMovements.Count(m=>m.Reason==CardMoveReasons.Reshuffle)==2 && game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Count()==2,"A selected empty-deck source uses the existing real two-card reshuffle and deterministic RNG.");Replay(game,registry);
    }
    public static void NativeAiCardContestActivation()
    {
        var (game,registry)=Create("ai-contest",Suit.Heart);var owner=game.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:qinmi-ai").Seat;
        for(var step=0;step<180;step++)
        {
            if(game.Events.Select(e=>e.Payload).OfType<PindianResultDeterminedEvent>().Any(e=>e.SkillId=="classic:zhuandui" && e.Result.SourceSeat==owner && e.Result.SourceWon))
            {Require(!game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat==owner),"The Qin Mi AI activates its own Slash contest and selects the actual Pindian entity through AdvanceOneStep.");Replay(game,registry);return;}
            if(Prompt(game) is {PlayerSeat:0,Kind:DecisionKind.PlayCard} p)Accept(game,new EndPlayPhaseCommand(0,game.Revision,p.PromptId));
            else if(Prompt(game) is {PlayerSeat:0} human)Answer(game,c=>c.Cards.Count>0 || c.Parameters.GetValueOrDefault("response")=="pass" || c.Cards.Count==0);
            else Accept(game,new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Native Qin Mi AI did not reach its actual offensive/defensive Pindian.");
    }
    public static void ResourceContracts()
    {
        var a=typeof(StandardClassicGeneralPackage).Assembly;string Read(string type){using var st=a.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-qin-mi."+type+".json")!;using var rd=new StreamReader(st);return rd.ReadToEnd();}
        var rules=Read("rules");var p=Read("presentation");SkillProgramCatalog.Load(rules,p);
        void Reject(Action<System.Text.Json.Nodes.JsonNode> edit){var n=System.Text.Json.Nodes.JsonNode.Parse(rules)!;edit(n);try{SkillProgramCatalog.Load(n.ToJsonString(),p);}catch(InvalidOperationException){return;}throw new InvalidOperationException("Invalid card contest/top policy resource accepted.");}
        Reject(n=>n["skills"]![0]!["triggers"]![0]!["effects"]![0]!["destination"]="ownerHand");Reject(n=>n["skills"]![0]!["triggers"]![0]!["effects"]![1]!["sourceBind"]="other");
        Reject(n=>n["skills"]![1]!["triggers"]![0]!["effects"]![0]!["opponentRef"]!["kind"]="owner");Reject(n=>n["skills"]![2]!["cardPolicies"]![1]!["value"]=14);
    }
    private static void StartContest(GameEngine game,int seat)=>Use(game,"contest",[seat]);
    private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,"fixture:qinmi-driver",id,[],targets,g.Revision,Prompt(g)!.PromptId));
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    {
        for(var step=0;step<250;step++)
        {if(Prompt(g) is { } p && predicate(p))return;if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.RespondDodge})Answer(g,c=>c.Cards.Count==0);else if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed Qin Mi fixture did not reach requested prompt: "+JsonSerializer.Serialize(Prompt(g))+" frames="+JsonSerializer.Serialize(g.ResolutionStack));
    }
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Movements=g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All observer/private snapshots, typed frames, entities and command JSON replay identically.");}
    private static void RejectUnknown(GameEngine g){var before=State(g);var p=Prompt(g)!;var result=g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new ChoiceId("fixture:illegal"),g.Revision));Require(!result.Accepted&&before==State(g),"Illegal input leaves every view, entity, event and accepted command untouched.");}
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(r.Accepted,r.Error?.Message??"Rejected.");}
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static (GameEngine,ContentRegistry) Create(string mode,Suit suit)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Scenario(mode,suit));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:qinmi-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:qinmi-owner",g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.PlayCard);return(g,r);
    }
    private sealed class Scenario(string mode,Suit suit):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-qinmi",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:qinmi-driver","revision":1,"modifiers":[{"id":"two","priority":0,"query":"cardTargetCount","operation":"add","value":1,"cardKinds":["slash","fireSlash","thunderSlash"]},{"id":"range","priority":0,"query":"attackRange","operation":"set","value":{{(mode=="intercept-far"?1:3)}}}{{(mode=="intercept-far"?",{\"id\":\"distance\",\"priority\":0,\"query\":\"outgoingDistance\",\"operation\":\"add\",\"value\":4}":"")}}],"cardPolicies":[{"id":"distance","kind":"ignoreUseDistance","cardKinds":["slash","fireSlash","thunderSlash"]}],"activations":[{"id":"ignore-range","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantDirectedTurnCardPolicy","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"cardKinds":["slash"],"effects":["ignoreDistance"]}]},{"id":"contest","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"},{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]},{"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},{"id":"discard-all","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]},{"id":"fixture:qinmi-payment-observer","revision":1,"triggers":[{"id":"payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.classic:jianzheng.SelectAndMoveOwnedCard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:qinmi-driver":{"name":"规则驱动","description":"真拼点与实体杀","optionLabels":{"continue":"继续"}},"fixture:qinmi-payment-observer":{"name":"付款移动观察","description":"真付款移动嵌套","optionLabels":{"continue":"继续"}}}}""");
            foreach(var id in catalog.Programs.Keys)b.AddSkill(new(id,id,id){Program=catalog.Programs[id]});
            var intercept=mode.StartsWith("intercept") || mode is "ai-contest" or "ai-defense";
            b.AddGeneral(new("fixture:qinmi-owner","秦宓机制","supporter","fixture:qinmi-driver","shu",20,intercept?[]:mode=="hongyan"?["classic:zhuandui","classic:tianbian","classic:hongyan"]:["classic:zhuandui","classic:tianbian"]));
            b.AddGeneral(new("fixture:qinmi-ai","观察者","supporter","standard:none","shu",20,mode is "ai-contest" or "ai-defense"?["classic:zhuandui","classic:tianbian"]:intercept?["classic:jianzheng","fixture:qinmi-payment-observer"]:mode is "both-top" or "empty-top"?["classic:tianbian"]:[]));
            for(var i=2;i<4;i++)b.AddGeneral(new($"fixture:qinmi-{i}","目标","supporter","standard:none","wei",20,[]));
            b.AddDeck(new("fixture:qinmi-deck","实际牌堆",8,2,[]){PhysicalCards=Enumerable.Range(0,mode=="empty-top"?34:180).Select(i=>new ContentDeckPhysicalCard(mode=="offense" && i%2==1?"standard:dodge":"standard:slash",suit,2)).ToArray()});
            b.AddMode(new("identity:qinmi-fixture","机制",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:qinmi-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:qinmi-owner","fixture:qinmi-ai","fixture:qinmi-2","fixture:qinmi-3"]));
        }
    }
}
