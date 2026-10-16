using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class BoundaryDaQiaoChecks
{
    public static void CompletedPlacementDrawsBeforeDelayedNullification()
    {
        var(g,r)=Create("nullification");var a=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="boundary:guose");var id=a.SelectableCardIds.First();var before=g.CreateSnapshot(0).Players[0].Hand.Count;
        Accept(g,new UseProgramSkillCommand(0,"boundary:guose","diamond-delayed",[id],[],g.Revision,Prompt(g)!.PromptId));Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Targets.SequenceEqual([1]));
        Require(Prompt(g)?.SkillPrompt?.SkillId=="fixture:dq-counter" && !g.ResolutionStack.OfType<NullificationWindowFrame>().Any(),
            "A committed-use child can suspend the exact Guose placement before its effect, without a counterspell window."); Replay(g,r);
        var owning=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.DiamondDelayed is not null);
        var checker=typeof(GameEngine).GetMethod("AssertDiamondDelayed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        foreach(var invalid in new[]{owning with{DiamondDelayed=owning.DiamondDelayed! with{UseFrameId=owning.DiamondDelayed.UseFrameId+1}},owning with{DiamondDelayed=owning.DiamondDelayed! with{Source=CardLocation.Equipment(0)}}})
        {
            var unchanged=State(g);var rejected=false;
            try{checker.Invoke(g,[invalid,r.GetSkill("boundary:guose").Program!.Activations.Single().Effects.Single()]);}
            catch(System.Reflection.TargetInvocationException e) when(e.InnerException is InvalidOperationException){rejected=true;}
            Require(rejected&&State(g)==unchanged,"Wrong child identity or frozen physical cost source is rejected without mutating the true owning frame.");
        }
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue"); ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before && g.CreateSnapshot(0).Players[1].Judgment.Any(c=>c.Id==id) &&
            !g.Events.Select(e=>e.Payload).OfType<NullificationRequestedEvent>().Any(),
            "Completed Guose placement earns exactly one refill and enters Judgment before any counterspell."); Replay(g,r);
        Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
        for(var i=0;i<64&&!(Prompt(g) is {Kind:DecisionKind.Nullification,PlayerSeat:0});i++)Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(Prompt(g) is {Kind:DecisionKind.Nullification,PlayerSeat:0} && g.State.CurrentSeat==1,
            "The converted delayed effect offers its real counterspell only in the holder's Judgment turn."); Replay(g,r);
        Answer(g,c=>c.Cards.Count==1);
        for(var i=0;i<64&&g.ResolutionStack.OfType<NullificationWindowFrame>().Any();i++)
            if(Prompt(g) is {Kind:DecisionKind.Nullification,PlayerSeat:0}) Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");
            else Accept(g,new AdvanceOneStepCommand(g.Revision));
        Require(g.Events.Select(e=>e.Payload).OfType<NullificationResolvedEvent>().Any(e=>e.EffectCardId==id&&e.EffectNullified),
            "Actual Judgment-stage Nullification cancels the already placed converted effect.");
        Require(g.CardMovements.Any(m=>m.CardId==id&&m.From==CardLocation.Judgment(1)&&m.To==CardLocation.DiscardPile)&&
            g.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.Judgment(1))==1,
            "The canceled physical delayed card leaves Judgment once after its completed placement.");
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==before-1,"Delayed cancellation pays one response without a second Guose refill.");Replay(g,r);
        Require(!JsonSerializer.Serialize(new ProgramSkillFrame(1,0,"old","old","hash",0,[],[])).Contains("DiamondDelayed"),"The new nullable draft is omitted from old frame JSON.");
    }
    private static SkillProgramCatalog LoadFixture(string rules,string presentation)
    {
        var r=JsonNode.Parse(rules)!;r["schemaVersion"]=SkillProgramCatalog.RulesSchemaVersion;
        var p=JsonNode.Parse(presentation)!;p["schemaVersion"]=SkillProgramCatalog.PresentationSchemaVersion;
        return SkillProgramCatalog.Load(r.ToJsonString(),p.ToJsonString());
    }
    private static void Require(bool v,string m){if(!v)throw new InvalidOperationException(m);}
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Command rejected.");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> find){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(find).Id,g.Revision));}
    private static void ReachPlay(GameEngine g){for(var i=0;i<64;i++){if(Prompt(g)?.Kind==DecisionKind.PlayCard&&g.CreateSnapshot(0).CurrentSeat==0)return;if(Prompt(g) is {PlayerSeat:0,Kind:DecisionKind.Nullification} n) Accept(g,new AnswerPromptCommand(0,n.PromptId,n.Choices.First(c=>c.Cards.Count==0).Id,g.Revision)); else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Fixed fixture did not reach actual Play.");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Four views, typed frames, events, movements and journal restore identically.");
    private static (GameEngine,ContentRegistry) Create(string mode="default")
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=1,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-dq-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,"fixture:dq-owner",g.Revision,Prompt(g)!.PromptId));ReachPlay(g);return(g,r);
    }
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-dq",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            if(mode=="nullification")
            {
                var counter=LoadFixture("""{"skills":[{"id":"fixture:dq-counter","revision":1,"viewAs":[{"id":"diamond-counter","sourceZones":["hand"],"inputKinds":["dodge"],"inputSuits":["diamond"],"outputKind":"nullification","forPlay":false,"forResponse":true}],"triggers":[{"id":"committed-placement","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["indulgence"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"placement-seen","options":[{"id":"continue"}]}]}]}]}""","""{"skills":{"fixture:dq-counter":{"name":"固定无懈","description":"仅本人固定实体响应","optionLabels":{"continue":"继续放置"}}}}""");
                b.AddSkill(new("fixture:dq-counter","固定无懈","固定实体响应"){Program=counter.Programs["fixture:dq-counter"]});
            }
            if(mode.StartsWith("equipment",StringComparison.Ordinal))
            {
                var childRules="""{"skills":[{"id":"fixture:dq-child","revision":1,"triggers":[{"id":"movement","window":"cardsMoved","subject":"owner","movementOccurrence":"perBatch","sourceZones":["equipment"],"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""";
                if(mode=="equipment-death")childRules=childRules.Replace("{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}");
                if(mode=="equipment-terminal")
                {
                    var terminal=JsonNode.Parse(childRules)!;var triggers=terminal["skills"]![0]!["triggers"]!.AsArray();var template=triggers[0]!.DeepClone();triggers.Clear();
                    for(var n=0;n<3;n++){var trigger=template.DeepClone();trigger["id"]="terminal-"+n;trigger["effects"]=JsonNode.Parse("""[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":20}]""");triggers.Add(trigger);}
                    childRules=terminal.ToJsonString();
                }
                var cat=LoadFixture(childRules,"""{"skills":{"fixture:dq-child":{"name":"付款子链","description":"装备真实离区摸一张"}}}""");
                b.AddSkill(new("fixture:dq-child","付款子链","付款子链"){Program=cat.Programs["fixture:dq-child"]});
            }
            if(mode=="effective")
            {
                var rewrite=LoadFixture("""{"skills":[{"id":"fixture:dq-suit","revision":1,"cardPolicies":[{"id":"rewrite","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"diamond"}]}]}""","""{"skills":{"fixture:dq-suit":{"name":"固定有效花色","description":"黑桃有效方片"}}}""");
                b.AddSkill(new("fixture:dq-suit","固定有效花色","固定有效花色"){Program=rewrite.Programs["fixture:dq-suit"]});
            }
            b.AddGeneral(new("fixture:dq-owner","固定大乔","supporter","boundary:guose","wu",mode=="equipment-death"?3:20,new[]{"classic:guose","boundary:liuli"}.Concat(mode.StartsWith("equipment",StringComparison.Ordinal)?["fixture:dq-child"]:mode=="nullification"?["fixture:dq-counter"]:mode=="effective"?["fixture:dq-suit"]:mode is "extra" or "self"?["classic:dangxian"]:Array.Empty<string>()).ToArray(),GeneralGender.Female));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:dq-target-{i}","固定目标","supporter","standard:none","wei",20,[]));
            b.AddDeck(new("fixture:dq-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,80).Select(i=>new ContentDeckPhysicalCard(mode=="source-equipment"?"special:xingtian-axe":mode.StartsWith("equipment",StringComparison.Ordinal)?"standard:crossbow":mode=="self"?"standard:indulgence":"standard:dodge",mode is "effective" or "non-diamond"?Suit.Spade:Suit.Diamond,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-dq-fixture","固定",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[mode is "self" or "equipment-terminal"?nameof(Role.Rebel):nameof(Role.Renegade)]=3},"fixture:dq-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:dq-owner","fixture:dq-target-1","fixture:dq-target-2","fixture:dq-target-3"]));
        }
    }
}
