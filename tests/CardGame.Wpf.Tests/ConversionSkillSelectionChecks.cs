using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ConversionSkillSelectionChecks
{
    public static void SingleCardRules(string output)
    {
        foreach (var scenario in new[]
        {
            new Scenario("classic:qixi", "classic:gan-ning", "standard:crossbow", CardKind.Dismantlement),
            new Scenario("classic:guose", "classic:da-qiao", "standard:peach", CardKind.Indulgence),
            new Scenario("standard:longdan", null, "standard:dodge", CardKind.Slash),
            new Scenario("classic:jiuchi", "classic:dong-zhuo", "standard:slash", CardKind.Alcohol),
            new Scenario("classic:lianhuan", "classic:pang-tong", "standard:peach", CardKind.IronChain),
            new Scenario("classic:lihuo", "classic:cheng-pu", "standard:dodge", CardKind.FireSlash, true)
        })
        {
            var (model, registry) = Create(scenario);
            using (model)
            {
                var window = new MainWindow(model);
                try
                {
                    var root = (FrameworkElement)window.Content;
                    var game = Program.Engine(model);
                    int? equipmentId = null;
                    if (scenario.Skill == "classic:qixi")
                    {
                        var weapon = model.Hand.First(card => card.SuitGlyph is "♠" or "♣");
                        model.SelectCardCommand.Execute(weapon);
                        model.ConfirmSelectedCommand.Execute(null);
                        for (var step = 0; step < 8 && model.CanStepAi; step++) model.StepAiCommand.Execute(null);
                        equipmentId = weapon.Id;
                        Program.Assert(game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == equipmentId),
                            "The native equipped-material fixture must equip its actual black card first.");
                    }
                    var actions = game.GetHumanLegalActions().Where(action => action.PlayedCardKind == scenario.Output &&
                        (action.ConversionSource?.SkillId == scenario.Skill ||
                         action.AdditionalConversionSources?.Any(source => source.SkillId == scenario.Skill) == true)).ToArray();
                    Program.Assert(actions.Length > 0, $"Native fixture did not publish {scenario.Skill}.");
                    var before = game.Revision;
                    var commandCount = game.AcceptedCommands.Count;
                    var printedFaces = model.Hand.ToDictionary(card => card.Id, card =>
                        (card.Kind, card.Name, card.Artwork, card.VerticalName, card.SuitGlyph, card.Rank));
                    model.SelectCardCommand.Execute(model.Hand.First(card => card.IsPlayable));
                    Layout(root);
                    Program.Assert(model.EquipmentPlayChoices.Count == 0 && !model.HasCenterChoices &&
                        ((Border)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed,
                        "Every ordinary material selection must omit the removed conversion shortcut panel.");
                    var skill = model.HumanSkillCards.Single(item => item.ContentId == scenario.Skill);
                    Program.Assert(skill.IsAvailable, $"{scenario.Skill} must be enabled before a material card is selected.");
                    Layout(root);
                    Click(Button(root, model.ActivateHumanSkillCommand, skill));
                    var materialIds = actions.Select(action => action.CardId!.Value).ToHashSet();
                    Program.Assert(model.HasSelection && model.Hand.All(card => card.IsPlayable == materialIds.Contains(card.Id)) &&
                        model.Hand.All(card => !card.IsSelected) && !model.CanConfirmSelected,
                        "A single-output conversion must start a legal material draft without selecting or playing a card.");
                    AssertPrintedFaces();
                    var action = actions.First(candidate => (equipmentId is null || candidate.CardId == equipmentId) && candidate.Kind != LegalActionKind.Recast &&
                        candidate.TargetSeats.Count <= 1 && candidate.TargetCardId is null);
                    Layout(root);
                    if (equipmentId is null)
                        Click(Button(root, model.SelectCardCommand, model.Hand.Single(item => item.Id == action.CardId)));
                    else
                    {
                        Program.Assert(!model.HasCenterChoices && model.EquipmentPlayChoices.Count == 0 &&
                            model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId).IsSelectable,
                            "Equipment conversion materials must use the equipment rail without a duplicate central picker.");
                        var retired = model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId);
                        Click(Button(root, model.SelectEquipmentMaterialCommand, retired));
                        Layout(root);
                        Program.Assert(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId).IsSelected &&
                            game.Revision == before, "Selecting conversion equipment must retain a local rail highlight without paying.");
                        model.SelectEquipmentMaterialCommand.Execute(retired);
                        Program.Assert(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId).IsSelected,
                            "A retired equipment control must not change the current draft.");
                        Click(Button(root, model.SelectCardCommand, model.Hand.First(card => card.IsPlayable)));
                        Layout(root);
                        Program.Assert(model.Hand.Count(card => card.IsSelected) == 1 &&
                            !model.HumanEquipmentSlots.Any(slot => slot.IsSelected),
                            "Switching to a hand material must remove the previous equipment highlight.");
                        Click(Button(root, model.SelectEquipmentMaterialCommand,
                            model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId)));
                        Layout(root);
                        Program.Assert(model.Hand.All(card => !card.IsSelected) &&
                            model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId).IsSelected,
                            "Switching to equipment must remove the previous hand material highlight.");
                        Click(Button(root, model.SelectEquipmentMaterialCommand,
                            model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId)));
                        Layout(root);
                        Program.Assert(!model.HumanEquipmentSlots.Any(slot => slot.IsSelected) && !model.CanConfirmSelected &&
                            !model.HasCenterChoices && game.Revision == before,
                            "Clicking the selected conversion equipment again must clear only its draft.");
                        Click(Button(root, model.SelectEquipmentMaterialCommand,
                            model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId)));
                    }
                    foreach (var seat in action.TargetSeats.Where(seat => !model.Seats.Single(item => item.Seat == seat).IsSelectedTarget))
                        model.SelectTargetCommand.Execute(model.Seats.Single(item => item.Seat == seat));
                    if (scenario.Output == CardKind.IronChain)
                        Program.Assert(model.ShowRecastAction && model.CanRecastSelected == (action.TargetSeats.Count == 0),
                            "Converted Iron Chain must retain its genuine Recast action and reversible target draft.");
                    Program.Assert(model.CanConfirmSelected == (scenario.Output != CardKind.Dismantlement) &&
                        game.Revision == before && game.AcceptedCommands.Count == commandCount,
                        $"{scenario.Skill} selected its printed card kind or committed before confirmation.");
                    AssertPrintedFaces();
                    if (scenario.Skill == "standard:longdan")
                        Program.Render(root, 1120, 740, Path.Combine(output, "conversion-original-faces-selected.png"));
                    if (equipmentId is not null)
                        Program.Render(root, 1120, 740, Path.Combine(output, "equipment-conversion-rail.png"));
                    VerifyCommit(model, registry, action, before, commandCount);

                    void AssertPrintedFaces() => Program.Assert(model.Hand.All(card =>
                        (card.Kind, card.Name, card.Artwork, card.VerticalName, card.SuitGlyph, card.Rank) == printedFaces[card.Id]),
                        "Skill, material and target selection must retain the original physical card artwork, name, suit and rank.");
                }
                finally { window.Content = null; window.Close(); }
            }
        }
    }

    public static void MultipleOutputs(string output)
    {
        foreach (var scenario in new[]
        {
            new Scenario("classic:guhuo", "classic:yu-ji", "standard:dodge", CardKind.Slash),
            new Scenario("classic:taoluan", "classic:zhang-rang", "standard:dodge", CardKind.Alcohol)
        })
        {
            var (model, registry) = Create(scenario);
            using (model)
            {
                var window = new MainWindow(model);
                try
                {
                    var root = (FrameworkElement)window.Content;
                    var game = Program.Engine(model);
                    var before = game.Revision;
                    var commands = game.AcceptedCommands.Count;
                    var printedFaces = model.Hand.ToDictionary(card => card.Id, card =>
                        (card.Kind, card.Name, card.Artwork, card.VerticalName, card.SuitGlyph, card.Rank));
                    var skill = model.HumanSkillCards.Single(item => item.ContentId == scenario.Skill);
                    Layout(root);
                    Click(Button(root, model.ActivateHumanSkillCommand, skill));
                    Program.Assert(model.HasSelection && model.Hand.All(card => !card.IsPlayable && !card.IsSelected) &&
                        !model.CanConfirmSelected && model.EquipmentPlayChoices.Count > 1,
                        "A multi-output skill must ask for its output kind before enabling materials or targets.");
                    var retired = Mode(model, CardKind.IronChain);
                    Layout(root);
                    Click(Button(root, model.SelectEquipmentPlayChoiceCommand, retired));
                    Program.Assert(!model.HasCenterChoices && model.EquipmentPlayChoices.Count == 0,
                        "The output chooser must close as soon as a conversion kind is selected.");
                    var chainCard = model.Hand.First(card => card.IsPlayable);
                    model.SelectCardCommand.Execute(chainCard);
                    Program.Assert(model.IsMultiTargetCardSelected &&
                        (scenario.Skill != "classic:guhuo" || !model.ShowRecastAction),
                        "The chosen conversion output must own target geometry and its actual Recast permission.");
                    model.SelectTargetCommand.Execute(model.Seats.First(seat => seat.IsLegalTarget));
                    Layout(root);
                    ChooseOutput(root, model, scenario.Skill, CardKind.BarbarianAssault);
                    model.SelectCardCommand.Execute(model.Hand.First(card => card.IsPlayable));
                    var nativeAoe = game.GetHumanLegalActions().First(candidate => candidate.ConversionSource?.SkillId == scenario.Skill &&
                        candidate.PlayedCardKind == CardKind.BarbarianAssault);
                    Program.Assert(!model.IsMultiTargetCardSelected && model.HasTargetCombinationChoices &&
                        model.TargetCombinationChoices.All(choice => choice.Targets.SequenceEqual(nativeAoe.TargetSeats)),
                        "A changed AOE output must show only its exact whole-target combination.");
                    var retiredTargets = model.TargetCombinationChoices[0];
                    Layout(root);
                    var selectedOutput = ChooseOutput(root, model, scenario.Skill, scenario.Output);
                    Program.Assert(model.Hand.All(card => !card.IsSelected) && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                        !model.IsMultiTargetCardSelected && !model.CanConfirmSelected,
                        "Changing conversion output must clear the previous material and target draft.");
                    model.SelectEquipmentPlayChoiceCommand.Execute(retired);
                    model.SelectEquipmentPlayChoiceCommand.Execute(selectedOutput with { });
                    model.SelectTargetCombinationChoiceCommand.Execute(retiredTargets);
                    Program.Assert(!model.IsMultiTargetCardSelected && model.Hand.All(card => !card.IsSelected),
                        "Retired and copied output buttons must not replace the current selected conversion.");
                    var action = game.GetHumanLegalActions().First(candidate => candidate.PlayedCardKind == scenario.Output &&
                        candidate.ConversionSource?.SkillId == scenario.Skill && candidate.TargetSeats.Count <= 1 && candidate.TargetCardId is null);
                    var card = model.Hand.Single(item => item.Id == action.CardId);
                    model.SelectCardCommand.Execute(card);
                    foreach (var seat in action.TargetSeats) model.SelectTargetCommand.Execute(model.Seats.Single(item => item.Seat == seat));
                    Layout(root);
                    Program.Assert(model.CanConfirmSelected && game.Revision == before && game.AcceptedCommands.Count == commands,
                        "Output selection and material selection must never silently play the first conversion kind.");
                    Program.Assert(model.Hand.All(card =>
                        (card.Kind, card.Name, card.Artwork, card.VerticalName, card.SuitGlyph, card.Rank) == printedFaces[card.Id]),
                        "Changing between declared output kinds must preserve every physical card face and identity.");
                    Program.Render(root, 1440, 860, Path.Combine(output, scenario.Skill.Replace(':', '-') + "-skill-first.png"));
                    VerifyCommit(model, registry, action, before, commands);
                }
                finally { window.Content = null; window.Close(); }
            }
        }
    }

    private static PromptChoice Mode(MainViewModel model, CardKind kind) => model.EquipmentPlayChoices.First(choice =>
        choice.Parameters.GetValueOrDefault("action") == "select-play-conversion-mode" &&
        choice.Parameters.GetValueOrDefault("played-kind") == kind.ToString());

    private static PromptChoice ChooseOutput(FrameworkElement root, MainViewModel model, string skillId, CardKind kind)
    {
        model.ClearSelectionCommand.Execute(null);
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == skillId));
        Layout(root);
        var choice = Mode(model, kind);
        model.SelectEquipmentPlayChoiceCommand.Execute(choice with { });
        Program.Assert(model.Hand.All(card => !card.IsPlayable) && model.EquipmentPlayChoices.Any(item => ReferenceEquals(item, choice)),
            "A copied current output choice must not select a conversion mode.");
        Click(Button(root, model.SelectEquipmentPlayChoiceCommand, choice));
        return choice;
    }

    private static void VerifyCommit(MainViewModel model, ContentRegistry registry, LegalAction action, long revision, int commands)
    {
        var game = Program.Engine(model);
        var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var expected = new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources,
            TargetHandSlot = action.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch ? 0 : null };
        Accept(reference, expected);
        if (expected.TargetHandSlot is not null)
            model.SelectPublicTargetChoiceCommand.Execute(model.PublicTargetChoices.Single(choice => choice.Parameters.GetValueOrDefault("target-hand-slot") == "0"));
        else model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
            CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
            game.CardMovements.SequenceEqual(reference.CardMovements) && !model.HasSelection &&
            Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(reference.CreateSnapshot(seat))),
            "Confirmation must submit the exact output, physical card, conversion chain and targets once, matching native execution.");
    }

    private static (MainViewModel, ContentRegistry) Create(Scenario scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The native fixture must reach human play.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return (model, registry);
    }

    private sealed record Scenario(string Skill, string? General, string Material, CardKind Output, bool Chained = false);
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        internal const string Mode = "identity:conversion-skill-ui";
        internal const string Owner = "fixture:conversion-skill-owner";
        public PackageManifest Manifest { get; } = new("fixture-conversion-skill-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (scenario.General is { } general) CharacterFixtureContent.Register(builder, general);
            builder.AddGeneral(new(Owner, "转换技能", "supporter", scenario.Skill, "qun", 6,
                AdditionalSkillIds: scenario.Chained ? ["standard:wusheng"] : []));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:conversion-skill-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "合法目标", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:conversion-skill-deck", "转换材料", 8, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(scenario.Material,
                    (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "转换选牌", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:conversion-skill-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static Button Button(FrameworkElement root, ICommand command, object value) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, value));
    private static void Click(Button button) { Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The real bound button must be enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null); }
    private static void Layout(FrameworkElement root) { root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740));
        root.UpdateLayout(); root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout(); }
}
