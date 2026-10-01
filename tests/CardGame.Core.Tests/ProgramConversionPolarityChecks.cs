using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class ProgramConversionPolarityChecks
{
    public static void SharedStateAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "fixture:conversion-mode", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Submit(new StartGameCommand());
        Submit(new SelectGeneralCommand(0, "fixture:conversion-owner", game.Revision, game.PendingDecision!.PromptId));
        for (var i = 0; i < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Submit(new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard }, "Fixed fixture reached Play.");
        Require(Polarity(game) == SkillPolarity.Yang, "Opt-in program exposes initial Yang.");
        Activate();
        Require(Polarity(game) == SkillPolarity.Yin, "Duplicate commit in the same frame flips once.");
        var first = game.Events.Select(e => e.Payload).OfType<ProgramConversionPolarityCommittedEvent>().Single();
        Require(first.PreviousState == SkillPolarity.Yang && first.CurrentState == SkillPolarity.Yin && first.BindingId == "flip" &&
            !string.IsNullOrWhiteSpace(first.SkillInstanceId), "Fact retains exact grant and binding.");
        var paused = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
        Require(paused.ConversionPreviousPolarity == SkillPolarity.Yang, "Owning frame retains original polarity through pause.");
        Replay(); Answer(); for (var i = 0; i < 10 && game.PendingDecision is null; i++) Submit(new AdvanceOneStepCommand(game.Revision)); Activate();
        Require(Polarity(game) == SkillPolarity.Yang && game.Events.Count(e => e.Payload is ProgramConversionPolarityCommittedEvent) == 2,
            "Next binding flips back with exactly one new fact.");
        Replay(); Answer(); Replay();
        void Activate() => Submit(new UseProgramSkillCommand(0, "fixture:conversion", "flip", [], [], game.Revision, game.PendingDecision!.PromptId));
        void Answer() { var p = game.PendingDecision!; Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,game.Revision)); }
        void Submit(GameCommand c) { var result = game.Submit(c); Require(result.Error is null, result.Error?.Message ?? "Rejected"); }
        void Replay()
        {
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
            Require(State(game) == State(restored), "Actual accepted command replay preserves all viewer snapshots and paused frames.");
        }
    }
    private static SkillPolarity? Polarity(GameEngine g) => g.CreateSnapshot(0).Players[0].SkillRuntimeStates!.Single(s => s.SkillId == "fixture:conversion").Polarity;
    private static string State(GameEngine g) => JsonSerializer.Serialize(Enumerable.Range(0,4).SelectMany(s=>new[]{g.CreateSnapshot(s),g.CreateSnapshot(s,true)})) + JsonSerializer.Serialize(g.ResolutionStack);
    private static void Require(bool value,string text) { if (!value) throw new InvalidOperationException(text); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:conversion",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var c = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:conversion","revision":1,"activations":[{"id":"flip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"commitConversionPolarity","target":"owner"},{"op":"commitConversionPolarity","target":"owner"},{"op":"chooseOption","target":"owner","resultBind":"done","options":[{"id":"continue"}]},{"op":"commitConversionPolarity","target":"owner"}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:conversion":{"name":"转换","description":"测试","optionLabels":{"continue":"继续"}}}}""");
            b.AddSkill(new("fixture:conversion","转换","测试") { Program=c.Programs["fixture:conversion"], Tags=SkillTag.Conversion });
            b.AddSkill(new("fixture:idle","静态","测试"));
            b.AddGeneral(new("fixture:conversion-owner","转换拥有者","supporter","fixture:conversion","wei",4));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:conversion-target-{i}","目标","supporter","fixture:idle","wei",4));
            b.AddDeck(new("fixture:conversion-deck","固定",4,2,[]) { PhysicalCards=Enumerable.Range(0,60).Select(i=>new ContentDeckPhysicalCard("standard:crossbow",Suit.Spade,i%13+1)).ToArray() });
            b.AddMode(new("fixture:conversion-mode","测试",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:conversion-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:conversion-owner","fixture:conversion-target-1","fixture:conversion-target-2","fixture:conversion-target-3"]));
        }
    }
}
