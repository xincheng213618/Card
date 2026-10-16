using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryZhenJiChecks
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Lazy<ContentRegistry> Classic=new(()=>ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage()));
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
    private static string? Action(PromptChoice c)=>c.Parameters.GetValueOrDefault("program-action")??c.Parameters.GetValueOrDefault("action");
    private static void Activate(GameEngine g)=>Answer(g,c=>Action(c)?.EndsWith("activate",StringComparison.Ordinal)==true);
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision));}
    private static void Reach(GameEngine g,Func<PendingDecision,bool> stop){for(var i=0;i<65;i++){if(P(g) is {} p&&stop(p))return;Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Boundary not reached: "+P(g)?.Kind+" "+P(g)?.SkillPrompt?.SkillId+" "+string.Join(',',P(g)?.Choices.Select(Action)??[]));}
    private static void Settle(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static uint Rng(GameEngine g){var random=typeof(GameEngine).GetField("_random",Flags)!.GetValue(g)!;return (uint)random.GetType().GetProperty("State")!.GetValue(random)!;}
    private static IReadOnlyList<CharacterState> Players(GameEngine g)=>(IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",Flags)!.GetValue(g)!;
    private static IReadOnlyList<Card> Eligible(GameEngine g,int seat)=>(IReadOnlyList<Card>)typeof(GameEngine).GetMethod("GetDiscardEligibleHand",Flags)!.Invoke(g,[Players(g)[seat]])!;
    private static IReadOnlyList<TurnHandLimitExemptCardGrant> Grants(GameEngine g){var store=typeof(GameEngine).GetField("_turnCardUseEffects",Flags)!.GetValue(g)!;return (IReadOnlyList<TurnHandLimitExemptCardGrant>)store.GetType().GetProperty("HandLimitExemptCards",Flags)!.GetValue(store)!;}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(Enumerable.Range(0,4).All(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))==SnapshotJson.Serialize(restored.CreateSnapshot(s)))&&g.CardMovements.SequenceEqual(restored.CardMovements)&&JsonSerializer.Serialize(g.ResolutionStack)==JsonSerializer.Serialize(restored.ResolutionStack)&&Rng(g)==Rng(restored)&&Grants(g).SequenceEqual(Grants(restored))&&Enumerable.Range(0,4).All(s=>Eligible(g,s).Select(c=>c.Id).SequenceEqual(Eligible(restored,s).Select(c=>c.Id))),"Four viewer cold replay preserves exact frames, movement, scalar grants, RNG and discard eligibility.");}
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
            CharacterFixtureContent.Register(b, "boundary:zhen-ji");
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
