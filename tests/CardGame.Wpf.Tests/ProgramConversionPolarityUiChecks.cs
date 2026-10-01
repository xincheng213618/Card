using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramConversionPolarityUiChecks
{
    public static void PolarityRemainsVisibleWithActiveEntryAndRestore(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "identity:classic-conversion-polarity-ui", UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:conversion-owner", game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard }, "The fixed actual fixture reaches Play.");
        using var initial = Restore(game, registry);
        AssertState(initial, "当前：阳", available: true);
        Render(initial, output, "271-conversion-polarity-active-yang.png", "当前：阳");
        Accept(game, new UseProgramSkillCommand(0, "fixture:conversion", "flip", [], [], game.Revision, game.PendingDecision!.PromptId));
        using var paused = Restore(game, registry);
        AssertState(paused, "当前：阴", available: false);
        Render(paused, output, "272-conversion-polarity-paused-yin.png", "当前：阴");
        var choice = paused.SkillChoices.Single();
        paused.SelectSkillChoiceCommand.Execute(choice);
        for (var step = 0; step < 30 && Program.Engine(paused).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            Program.Assert(paused.CanStepAi, "The native restored parent can continue after its choice.");
            paused.StepAiCommand.Execute(null);
        }
        Program.Assert(Program.Engine(paused).PendingDecision is { Kind: DecisionKind.PlayCard }, "Actual choice completes its original frame.");
        using var resumed = Restore(Program.Engine(paused), registry);
        AssertState(resumed, "当前：阴", available: true);
        Render(resumed, output, "273-conversion-polarity-active-yin.png", "当前：阴");
        Program.Assert(Program.Engine(resumed).Events.Count(e => e.Payload is ProgramConversionPolarityCommittedEvent) == 1,
            "A restored UI choice does not repeat the already committed conversion.");
    }
    private static void AssertState(MainViewModel model, string text, bool available)
    {
        var skill = model.HumanSkillCards.Single(item => item.ContentId == "fixture:conversion");
        Program.Assert(skill.StateText.Contains(text) && skill.Tooltip.Contains(text),
            "The shared skill card and tooltip show actual polarity even with an active entry.");
        Program.Assert(model.HumanActiveSkillActions.Any(action => action.ProgramSkillId == "fixture:conversion") == available,
            "The tested polarity states coexist with the true published availability.");
        if (available) Program.Assert(skill.StateText.Contains("可发动"), "The shared text also retains availability.");
    }
    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus); return model;
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted actual command.");
    }
    private static void Render(MainViewModel model, string output, string file, string text)
    {
        var window = new MainWindow(model); window.ApplyTemplate(); var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, file));
        Program.Assert(Program.Find<TextBlock>(root).Any(block => block.Text.Contains(text) &&
            block.Visibility == Visibility.Visible && block.ActualWidth > 0), "Actual polarity renders in the native skill card.");
        window.Content = null; window.Close();
    }
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
            b.AddMode(new("identity:classic-conversion-polarity-ui","测试",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:conversion-deck",GeneralCandidateCount:4,
                GeneralPoolIds:["fixture:conversion-owner","fixture:conversion-target-1","fixture:conversion-target-2","fixture:conversion-target-3"]));
        }
    }
}
