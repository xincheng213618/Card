using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryZhenJiChecks
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Lazy<ContentRegistry> Classic=new(()=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage()));
    public static void ExactReceiptStopRedAndDiscard()
    {
        var(g,r)=Start("black");Replay(g,r);Activate(g);Reach(g,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);
        var suits=g.ResolutionStack.OfType<ProgramSkillFrame>().Single().RepeatedJudgment!.SuccessSuits;Require(suits is IList<Suit> {IsReadOnly:true},"Owning repeated success-suit collection is frozen at the exposed diagnostic boundary.");
        var grant=Grants(g).Single();Require(g.State.Players[0].Hand.Any(c=>c.Id==grant.CardId)&&!Eligible(g,0).Any(c=>c.Id==grant.CardId),"Only the actual finalized Judgment→owner Hand receipt is exempt.");
        Require(g.CardMovements.Count(m=>m.CardId==grant.CardId&&m.From==CardLocation.Judgment(0)&&m.To==CardLocation.Hand(0))==1,"The exact entity is acquired once.");
        Require(Eligible(g,0).Count==4,"All four original hand cards still count toward the hand limit.");
        Replay(g,r);Reject(g,new AnswerPromptCommand(0,P(g)!.PromptId,new ChoiceId("fake-repeat"),g.Revision));Answer(g,c=>Action(c)=="stop");Settle(g);Use(g,"draw");Settle(g);
        Require(Eligible(g,0).Count==g.State.Players[0].Hand.Count-1,"Ordinary normal Draw and skill gains remain eligible.");
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,p=>p.Kind==DecisionKind.DiscardCards);
        Require(P(g)!.ValidCardIds.All(id=>id!=grant.CardId),"Human discard candidates exclude exactly the actual claimed entity.");Replay(g,r);
        var(mixed,mr)=Start("black-red");Activate(mixed);Reach(mixed,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);Replay(mixed,mr);Answer(mixed,c=>Action(c)=="continue");Settle(mixed);
        Require(Grants(mixed).Count==1&&mixed.Events.Select(e=>e.Payload).OfType<JudgmentResolvedEvent>().Count(e=>e.Reason=="skill.luoshen")==2,"One true black result continues into final red; only the black actual receipt is exempt.");Replay(mixed,mr);
        var(many,manyRegistry)=Start("black");Activate(many);
        for(var i=0;i<3;i++){Reach(many,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);Require(Grants(many).Count==i+1&&Grants(many).Select(x=>x.CardId).Distinct().Count()==i+1,"Each repeat earns only its distinct physical receipt once.");Replay(many,manyRegistry);Answer(many,c=>Action(c)==(i<2?"continue":"stop"));}Settle(many);Replay(many,manyRegistry);
        var(ai,aiRegistry)=Start("ai");Answer(ai,c=>Action(c)=="skip");Settle(ai);Accept(ai,new EndPlayPhaseCommand(0,ai.Revision,P(ai)!.PromptId));
        Reach(ai,p=>p.Kind==DecisionKind.DiscardCards);var discard=P(ai)!;Accept(ai,new DiscardCardsCommand(0,discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),discard.PromptId,ai.Revision));
        for(var i=0;i<80;i++){if(ai.CreateSnapshot(0).CurrentSeat==2)break;Accept(ai,new AdvanceOneStepCommand(ai.Revision));}
        Require(ai.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Any(e=>e.OwnerSeat==1&&e.SkillId=="boundary:luoshen"),"The formal optional new Luoshen actually activates through native AI public estimation.");var aiReceipts=ai.Events.Select(e=>e.Payload).OfType<TurnHandLimitExemptCardGrantedEvent>().Where(e=>e.Grant.BeneficiarySeat==1).Select(e=>e.Grant.CardId).ToArray();
        Require(aiReceipts.Length==3&&aiReceipts.All(id=>ai.CreateSnapshot(1).Players[1].Hand.Any(c=>c.Id==id))&&ai.CardMovements.Count(m=>m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile)==2,"Native AI discard keeps all three actual claimed entities exempt (including the real reshuffled discarded card) and still discards two original cards to its numeric limit.");Replay(ai,aiRegistry);
        var(red,rr)=Start("red");Activate(red);Settle(red);Require(Grants(red).Count==0&&!red.Events.Select(e=>e.Payload).OfType<ProgramJudgmentCardClaimedEvent>().Any(e=>e.SkillId=="boundary:luoshen"),"Final red entity is discarded with no receipt, reward or repeat.");
        Require(red.CardMovements.Any(m=>m.From==CardLocation.Judgment(0)&&m.To==CardLocation.DiscardPile),"Failed red judgment is physically cleaned.");Replay(red,rr);
    }
    public static void FrozenFinalOutcomeReplacementAndClaim()
    {
        var(replace,rr)=Start("replacement");Activate(replace);Reach(replace,p=>p.Kind==DecisionKind.ProgramJudgmentReplacement);
        Replay(replace,rr);
        Reach(replace,p=>p.Choices.Any(c=>c.Cards.Count==1));var old=replace.ResolutionStack.OfType<JudgmentFrame>().Single().CardId;
        var chosen=P(replace)!.Choices.First(c=>c.Cards.Count==1&&replace.State.Players[0].Hand.Single(h=>h.Id==c.Cards.Single()).Suit==Suit.Heart);var id=chosen.Cards.Single();Answer(replace,c=>c.Id==chosen.Id);Settle(replace);
        Require(Grants(replace).Count==0&&!replace.State.Players[0].Hand.Any(c=>c.Id==id)&&replace.CardMovements.Any(m=>m.CardId==id&&m.From==CardLocation.Judgment(0)&&m.To==CardLocation.DiscardPile),"Final replacement red entity fails even though the original judgment was black.");
        Require(old!=id,"Replacement changed the actual physical entity.");Replay(replace,rr);
        var(cleanupAudit,_)=Start("final-audit");Activate(cleanupAudit);Reach(cleanupAudit,p=>p.Kind==DecisionKind.ProgramJudgmentTrigger);
        var owning=cleanupAudit.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="boundary:luoshen");var unclaimed=owning.RepeatedJudgment!.FinalOutcome!.CardId;
        // Host-only early cancellation cleanup audit against an actual owning finalized outcome.
        typeof(GameEngine).GetMethod("CleanupProgramBoundCards",Flags)!.Invoke(cleanupAudit,[owning,false]);typeof(GameEngine).GetMethod("CleanupProgramBoundCards",Flags)!.Invoke(cleanupAudit,[owning,false]);
        Require(Grants(cleanupAudit).Count==0&&cleanupAudit.CardMovements.Count(m=>m.CardId==unclaimed&&m.From==CardLocation.Judgment(0)&&m.To==CardLocation.DiscardPile)==1,"New opt-in owning cleanup removes exactly one still-unclaimed final entity and is idempotent.");
        var(colorAudit,_)=Start("final-audit");Activate(colorAudit);Reach(colorAudit,p=>p.Kind==DecisionKind.ProgramJudgmentTrigger);
        var frozen=colorAudit.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="boundary:luoshen").RepeatedJudgment!.FinalOutcome!;
        Require(frozen is {FinalSuit:Suit.Spade,Matched:true},"Actual finalized child sees the owning final outcome frozen before it runs.");
        // Host-only qualification mutation audit, not a command replay or production grant witness.
        Players(colorAudit)[0].SkillGrants.Grant(new("host-audit:hongyan","classic:hongyan","host-audit:hongyan","host-audit:hongyan"));
        Activate(colorAudit);Reach(colorAudit,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);
        Require(Grants(colorAudit).Single().CardId==frozen.CardId,"Changing current EffectiveSuit after finalization cannot change this owning frozen black outcome.");
        var(claim,cr)=Start("claim");Activate(claim);Reach(claim,p=>p.Kind==DecisionKind.ProgramJudgmentTrigger);Replay(claim,cr);Activate(claim);
        Reach(claim,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);Require(Grants(claim).Count==0&&claim.Events.Select(e=>e.Payload).OfType<ProgramJudgmentCardClaimedEvent>().Any(e=>e.SkillId=="fixture:claim"),"Another real finalized child claims black entity; Luoshen keeps final black continuation but earns no own receipt grant.");
        Answer(claim,c=>Action(c)=="stop");Settle(claim);Replay(claim,cr);
    }
    public static void ReceiptBeforeGainChildAndSourceLoss()
    {
        var(g,r)=Start("gain-child");Activate(g);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gain-child");var grant=Grants(g).Single();var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="boundary:luoshen");
        Require(frame.RepeatedJudgment?.LastClaimReceipt is { } receipt&&receipt.GrantSequence==grant.GrantSequence&&!Eligible(g,0).Any(c=>c.Id==grant.CardId),"Typed actual receipt and exemption exist before the real gain child.");Replay(g,r);Activate(g);Answer(g,c=>c.Cards.SequenceEqual(new[]{grant.CardId}));
        Reach(g,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);Answer(g,c=>Action(c)=="stop");Settle(g);
        Require(Grants(g).Count==1&&!g.State.Players[0].Hand.Any(c=>c.Id==grant.CardId),"A real child discards the claimed entity without erasing or duplicating its reward.");Replay(g,r);
        foreach(var scenario in new[]{"before-loss","after-loss"})
        {
            var(loss,lr)=Start(scenario);Activate(loss);Reach(loss,p=>scenario=="before-loss"?p.Kind==DecisionKind.ProgramJudgmentTrigger:p.SkillPrompt?.SkillId=="fixture:loss");Replay(loss,lr);Activate(loss);Settle(loss);
            Require(loss.State.Players[0].Hp==1&&!SourceLive(loss),"Real child HP loss enables existing Chanyuan skill suppression. scenario="+scenario+" hp="+loss.State.Players[0].Hp+" skills="+string.Join(",",loss.CreateSnapshot(0).Players[0].Skills!.Select(s=>s.ContentId)));
            Require(Grants(loss).Count==(scenario=="after-loss"?1:0),"After-receipt source invalidation keeps reward; before-receipt source invalidation earns none.");
            Require(!loss.CreateCardZoneDiagnostics().Any(c=>c.Location==CardLocation.Judgment(0))&&!loss.ResolutionStack.OfType<ProgramSkillFrame>().Any(),"Invalid issuing source finishes finitely and cleans its unclaimed real judgment exactly once.");Replay(loss,lr);
        }
        RealOwnerDeath("before-death");RealOwnerDeath("after-death");RealOwnerRescue("before-rescue");RealOwnerRescue("after-rescue");
    }
    private static void RealOwnerDeath(string scenario)
    {
        var(g,r)=Start(scenario);Activate(g);Reach(g,p=>scenario=="before-death"?p.Kind==DecisionKind.ProgramJudgmentTrigger:p.SkillPrompt?.SkillId=="fixture:death");Replay(g,r);var finalCard=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.SkillId=="boundary:luoshen").RepeatedJudgment!.FinalOutcome!.CardId;
        Activate(g);
        for(var i=0;i<40&&g.State.Players[0].IsAlive;i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(!g.State.Players[0].IsAlive&&g.Events.Select(e=>e.Payload).OfType<TurnHandLimitExemptCardGrantedEvent>().Count()==(scenario=="after-death"?1:0),"Real owning death is finite, preserves only an already-paid actual receipt, and never grants before receipt.");
        Require(!g.CreateCardZoneDiagnostics().Any(c=>c.Location==CardLocation.Judgment(0)),"Death cleanup cannot leave the final physical judgment entity behind.");Require(g.CardMovements.Count(m=>m.CardId==finalCard&&m.From==CardLocation.Judgment(0))==1&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="boundary:luoshen"),"The real death path retires its owning repeated frame and consumes the judgment entity once.");Replay(g,r);
    }
    private static void RealOwnerRescue(string scenario)
    {
        var(g,r)=Start(scenario);Activate(g);Reach(g,p=>scenario=="before-rescue"?p.Kind==DecisionKind.ProgramJudgmentTrigger:p.SkillPrompt?.SkillId=="fixture:death");Replay(g,r);Activate(g);
        Reach(g,p=>p.Kind==DecisionKind.RescueDying&&p.PlayerSeat==0);Replay(g,r);var peach=g.State.Players[0].Hand.Single(c=>c.Kind==CardKind.Peach).Id;
        Answer(g,c=>c.Cards.Contains(peach));Reach(g,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);
        Require(g.State.Players[0] is {IsAlive:true,Hp:1}&&Grants(g).Count==1,"Real self rescue returns through the exact owning judgment/gain subtree and issues exactly one reward.");Replay(g,r);Answer(g,c=>Action(c)=="stop");Settle(g);Replay(g,r);
    }
    public static void ExactIdentityReturnActualTurnExpiryAndPrivacy()
    {
        var(g,r)=Start("black");Activate(g);Reach(g,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);var grant=Grants(g).Single();
        for(var viewer=1;viewer<4;viewer++)Require(g.CreateSnapshot(viewer).PendingDecision is null,"Private continue/stop decision is hidden from other viewers.");
        Replay(g,r);Answer(g,c=>Action(c)=="stop");Settle(g);Use(g,"discard",[grant.CardId]);Settle(g);Require(!g.State.Players[0].Hand.Any(c=>c.Id==grant.CardId),"Exact entity physically leaves Hand.");
        // A separate host identity audit: existing turn fact follows the entity on leave/return, never all turn gains.
        var zones=typeof(GameEngine).GetField("_cardZones",Flags)!.GetValue(g)!;var card=((IReadOnlyList<Card>)zones.GetType().GetMethod("CardsAt")!.Invoke(zones,[CardLocation.DiscardPile])!).Single(c=>c.Id==grant.CardId);
        typeof(GameEngine).GetMethod("MoveCard",Flags)!.Invoke(g,[card,CardLocation.DiscardPile,CardLocation.Hand(1),new CardMoveReason("host-audit.identity-transfer"),null,true]);
        Require(Eligible(g,1).Any(c=>c.Id==grant.CardId),"The same entity in another owner's Hand is not exempt.");
        typeof(GameEngine).GetMethod("MoveCard",Flags)!.Invoke(g,[card,CardLocation.Hand(1),CardLocation.Hand(0),new CardMoveReason("host-audit.identity-return"),null,true]);
        Require(!Eligible(g,0).Any(c=>c.Id==grant.CardId),"Engineering default: the exact entity returning to original owner within actual turn remains exempt.");
        var(extra,xr)=Start("extra-play");Activate(extra);Reach(extra,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);var actualTurn=extra.CreateSnapshot(0).TurnNumber;Answer(extra,c=>Action(c)=="stop");Settle(extra);
        Require(extra.Events.Select(e=>e.Payload).OfType<ProgramPhaseScheduledEvent>().Any(e=>e.Phase==TurnPhase.Play&&e.Started),"A real extra Play precedes normal preparation flow.");Replay(extra,xr);Accept(extra,new EndPlayPhaseCommand(0,extra.Revision,P(extra)!.PromptId));Settle(extra);
        Require(extra.CreateSnapshot(0).TurnNumber==actualTurn&&Grants(extra).Count==1&&!extra.Events.Select(e=>e.Payload).OfType<TurnCardUseEffectsExpiredEvent>().Any(),"Finishing extra Play retains exact exemption through normal Draw/Play within the same actual turn.");Replay(extra,xr);
        var(expiry,er)=Start("black");Activate(expiry);Reach(expiry,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);var eid=Grants(expiry).Single().GrantSequence;Answer(expiry,c=>Action(c)=="stop");Settle(expiry);
        Accept(expiry,new EndPlayPhaseCommand(0,expiry.Revision,P(expiry)!.PromptId));
        for(var i=0;i<65&&expiry.CreateSnapshot(0).CurrentSeat==0;i++){if(P(expiry)?.Kind==DecisionKind.DiscardCards){var p=P(expiry)!;Accept(expiry,new DiscardCardsCommand(0,p.ValidCardIds.Take(p.RequiredCardCount).ToArray(),p.PromptId,expiry.Revision));}else Accept(expiry,new AdvanceOneStepCommand(expiry.Revision));}
        Require(Grants(expiry).Count==0&&expiry.Events.Select(e=>e.Payload).OfType<TurnCardUseEffectsExpiredEvent>().Any(e=>e.GrantSequences.Contains(eid)),"Exact grant expires at actual end and reports its existing scalar grant identity.");Replay(expiry,er);
    }
    public static void NarrowParserLegacyNullAndOwningInvariants()
    {
        const string valid="""{"id":"fixture:narrow","revision":1,"triggers":[{"id":"prep","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"effects":[{"op":"repeatJudgment","target":"owner","judgmentReason":"skill.luoshen","resultBind":"judgment","suits":["spade","club"],"claimHandLimitExemption":"actualTurn"}]}]}""";
        var effect=Load(valid).Programs["fixture:narrow"].Triggers.Single().Effects.Single();Require(effect.ClaimHandLimitExemption==SkillProgramClaimHandLimitExemption.ActualTurn,"Only the named narrow claim receipt policy opts in.");
        foreach(var bad in new[]{valid.Replace("\"actualTurn\"","null"),valid.Replace("\"actualTurn\"","true"),valid.Replace("actualTurn","allGains"),valid.Replace("turnStartBeforeNormalFlow","drawPhaseStarting"),valid.Replace("\"subject\":\"owner\"","\"subject\":\"any\""),valid.Replace("\"op\":\"repeatJudgment\"","\"op\":\"startJudgment\"")})
        {var rejected=false;try{Load(bad);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Malformed policy, wrong op/window/subject rejects before play.");}
        var legacy=Load(valid.Replace(",\"claimHandLimitExemption\":\"actualTurn\"","")).Programs["fixture:narrow"].Triggers.Single().Effects.Single();Require(legacy.ClaimHandLimitExemption is null&&!JsonSerializer.Serialize(legacy).Contains("ClaimHandLimitExemption",StringComparison.Ordinal),"Old nullable field stays absent in serialization.");
        var(aiOld,aiOldRegistry)=Start("ai-legacy");Answer(aiOld,c=>Action(c)=="skip");Settle(aiOld);Accept(aiOld,new EndPlayPhaseCommand(0,aiOld.Revision,P(aiOld)!.PromptId));Reach(aiOld,p=>p.Kind==DecisionKind.DiscardCards);var oldDiscard=P(aiOld)!;Accept(aiOld,new DiscardCardsCommand(0,oldDiscard.ValidCardIds.Take(oldDiscard.RequiredCardCount).ToArray(),oldDiscard.PromptId,aiOld.Revision));
        for(var i=0;i<40&&aiOld.CreateSnapshot(0).CurrentSeat!=2;i++)Accept(aiOld,new AdvanceOneStepCommand(aiOld.Revision));
        Require(!aiOld.Events.Select(e=>e.Payload).OfType<ProgramBindingStartedEvent>().Any(e=>e.OwnerSeat==1&&e.SkillId=="classic:luoshen")&&!aiOld.Events.Select(e=>e.Payload).OfType<TurnHandLimitExemptCardGrantedEvent>().Any(e=>e.Grant.BeneficiarySeat==1),"Old nullable repeat preserves its existing inert AI estimate and legitimate optional skip.");Replay(aiOld,aiOldRegistry);
        var(g,r)=Start("legacy");Activate(g);Reach(g,p=>p.Kind==DecisionKind.ProgramRepeatJudgment);Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single().RepeatedJudgment is {ClaimHandLimitExemption:null}&&Grants(g).Count==0,"Real classic Luoshen retains old repeated judgment with no exact-card reward.");Replay(g,r);Answer(g,c=>Action(c)=="stop");Settle(g);Replay(g,r);
    }
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action")??c.Parameters.GetValueOrDefault("action");
    private static void Activate(GameEngine g)=>Answer(g,c=>Action(c)?.EndsWith("activate",StringComparison.Ordinal)==true);
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> stop){for(var i=0;i<65;i++){if(P(g) is {} p&&stop(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Boundary not reached: "+P(g)?.Kind+" "+P(g)?.SkillPrompt?.SkillId+" "+string.Join(',',P(g)?.Choices.Select(Action)??[]));}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Use(GameEngine g,string action,int[]?cards=null)=>Accept(g,new UseProgramSkillCommand(0,"fixture:driver",action,cards??[],[],g.Revision,P(g)!.PromptId));
    private static bool SourceLive(GameEngine g){var grant=Players(g)[0].SkillGrants.Grants.Single(s=>s.SkillId=="boundary:luoshen");return (bool)typeof(GameEngine).GetMethod("HasRuntimeSkillInstance",Flags)!.Invoke(g,[Players(g)[0],grant.SkillId,grant.SkillInstanceId])!;}
    private static uint Rng(GameEngine g){var random=typeof(GameEngine).GetField("_random",Flags)!.GetValue(g)!;return (uint)random.GetType().GetProperty("State")!.GetValue(random)!;}
    private static IReadOnlyList<CharacterState> Players(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",Flags)!.GetValue(g)!;
    private static IReadOnlyList<Card> Eligible(GameEngine g,int seat)=>(IReadOnlyList<Card>)typeof(GameEngine).GetMethod("GetDiscardEligibleHand",Flags)!.Invoke(g,[Players(g)[seat]])!;
    private static IReadOnlyList<TurnHandLimitExemptCardGrant> Grants(GameEngine g){var store=typeof(GameEngine).GetField("_turnCardUseEffects",Flags)!.GetValue(g)!;return (IReadOnlyList<TurnHandLimitExemptCardGrant>)store.GetType().GetProperty("HandLimitExemptCards",Flags)!.GetValue(store)!;}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))==SnapshotJson.Serialize(restored.CreateSnapshot(s)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack)&&Rng(g)==Rng(restored)&&Grants(g).SequenceEqual(Grants(restored))&&Enumerable.Range(0,4).All(s=>Eligible(g,s).Select(c=>c.Id).SequenceEqual(Eligible(restored,s).Select(c=>c.Id))),"Four viewer cold replay preserves exact frames, movement, scalar grants, RNG and discard eligibility.");}
    private static void Reject(GameEngine g,GameCommand c){var revision=g.Revision;var random=Rng(g);var snapshot=SnapshotJson.Serialize(g.CreateSnapshot(0));Require(!g.Submit(c).Accepted&&g.Revision==revision&&Rng(g)==random&&SnapshotJson.Serialize(g.CreateSnapshot(0))==snapshot,"Illegal input rejects atomically.");}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static SkillProgramCatalog Load(string skill)=>SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{skill}}]}""","""{"schemaVersion":3,"skills":{"fixture:narrow":{"name":"narrow","description":"narrow"}}}""");
    private static (GameEngine,ContentRegistry) Start(string scenario)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-zhen-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=4},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:zhen-owner",g.Revision,P(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId==(scenario=="legacy"?"classic:luoshen":"boundary:luoshen"));return(g,r);
    }
    private sealed class Fixture(string scenario):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:zhen",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            foreach(var id in new[]{"classic:qingguo","classic:chanyuan","classic:guicai","classic:luoshen","classic:hongyan"})b.AddSkill(Classic.Value.GetSkill(id));
            typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryZhenJiContent")!.GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[b]);
            var definitions=new List<string>{"""{"id":"fixture:driver","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},{"id":"discard","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"selected"},{"op":"moveBoundCards","target":"owner","sourceBind":"selected","destination":"discardPile","awaitMovementTriggers":true}]}]}"""};
            if(scenario=="claim")definitions.Add("""{"id":"fixture:claim","revision":1,"triggers":[{"id":"claim","window":"judgmentFinalized","subject":"owner","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"claimJudgmentCard","target":"owner"}]}]}""");
            if(scenario=="gain-child")definitions.Add("""{"id":"fixture:gain-child","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:luoshen.repeatJudgment"],"optional":true,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"amount":1,"resultBind":"owned"},{"op":"moveBoundCards","target":"owner","sourceBind":"owned","destination":"discardPile"}]}]}""");
            if(scenario is "before-loss" or "after-loss")definitions.Add(scenario=="before-loss"?"""{"id":"fixture:loss","revision":1,"triggers":[{"id":"loss","window":"judgmentFinalized","subject":"owner","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]}""":"""{"id":"fixture:loss","revision":1,"triggers":[{"id":"loss","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:luoshen.repeatJudgment"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]}""");
            if(scenario=="final-audit")definitions.Add("""{"id":"fixture:final-observer","revision":1,"triggers":[{"id":"observer","window":"judgmentFinalized","subject":"owner","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"recover","target":"owner","amount":1}]}]}""");
            if(scenario is "before-death" or "after-death" or "before-rescue" or "after-rescue")definitions.Add((scenario is "before-death" or "before-rescue"?"""{"id":"fixture:death","revision":1,"triggers":[{"id":"death","window":"judgmentFinalized","subject":"owner","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":12}]}]}""":"""{"id":"fixture:death","revision":1,"triggers":[{"id":"death","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:luoshen.repeatJudgment"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":12}]}]}""").Replace("\"amount\":12",scenario.EndsWith("rescue",StringComparison.Ordinal)?"\"amount\":5":"\"amount\":12"));
            if(scenario=="extra-play")definitions.Add("""{"id":"fixture:extra-play","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}""");
            var presentation=JsonSerializer.Serialize(new{schemaVersion=3,skills=definitions.Select(d=>JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!).ToDictionary(id=>id,id=>new{name=id,description=id})});
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',definitions)}}]}""",presentation);foreach(var pair in c.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            var extras=new List<string>{"fixture:driver"};if(scenario=="extra-play")extras.Add("fixture:extra-play");if(scenario=="final-audit")extras.Add("fixture:final-observer");if(scenario is "before-death" or "after-death" or "before-rescue" or "after-rescue")extras.Add("fixture:death");if(scenario=="replacement")extras.Add("classic:guicai");if(scenario=="claim")extras.Add("fixture:claim");if(scenario=="gain-child")extras.Add("fixture:gain-child");if(scenario is "before-loss" or "after-loss"){extras.Add("fixture:loss");extras.Add("classic:chanyuan");}
            b.AddGeneral(new("fixture:zhen-owner","owner","supporter",scenario=="legacy"?"classic:luoshen":"boundary:luoshen","wei",4,extras));
            b.AddSkill(new("fixture:none","none","none"));for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:zhen-{i}","other","supporter",scenario=="ai"?"boundary:luoshen":scenario=="ai-legacy"?"classic:luoshen":"fixture:none","wei",scenario.StartsWith("ai",StringComparison.Ordinal)?2:6,[]));
            b.AddDeck(new("fixture:zhen-deck","fixed",4,2,[]){PhysicalCards=Enumerable.Range(0,scenario.StartsWith("ai",StringComparison.Ordinal)?20:80).Select(i=>new ContentDeckPhysicalCard(scenario.EndsWith("rescue",StringComparison.Ordinal)&&i==44?"standard:peach":"standard:dodge",scenario=="red"||scenario=="black-red"&&i!=55||scenario=="replacement"&&i==44?Suit.Heart:Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-zhen-fixture","zhen",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:zhen-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:zhen-owner","fixture:zhen-1","fixture:zhen-2","fixture:zhen-3"]));
        }
    }
}
