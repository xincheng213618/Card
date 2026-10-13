using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DiscardRecommendationUiChecks
{
    private const string Skill = "fixture:discard-advice";
    private const string Owner = "fixture:discard-advice-owner";
    private const string Mode = "identity:classic-discard-advice";

    public static void PublishedMaterialAndLimit(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(new StartGameCommand());
        Reach(DecisionKind.SelectGeneral);
        Accept(new SelectGeneralCommand(0, Owner, original.Revision, original.PendingDecision!.PromptId));
        Reach(DecisionKind.PlayCard);
        Accept(new UseProgramSkillCommand(0, Skill, "exempt", [], [], original.Revision, original.PendingDecision!.PromptId));
        Reach(DecisionKind.PlayCard);
        Accept(new EndPlayPhaseCommand(0, original.Revision, original.PendingDecision!.PromptId));
        Reach(DecisionKind.DiscardCards);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, original.CreateCheckpoint()));
        using var model = new MainViewModel(false, original.Seed, true, store, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var game = Program.Engine(model);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        var human = game.CreateSnapshot(0).Players[0];
        var exempt = human.Hand.Where(c => !prompt.ValidCardIds.Contains(c.Id)).Select(c => c.Id).ToArray();
        Program.Assert(!model.HasSaveError && model.IsDiscardSelectionPending && exempt.Length > 0 &&
            prompt.ValidCardIds.Count > 0 && prompt.RequiredCardCount == prompt.ValidCardIds.Count &&
            human.Hp > 0 && prompt.Prompt.Contains("上限为 0") && human.TurnHandLimitCardKindExemptions is { Count: 1 },
            "The fixed real-command fixture must restore mixed Hand material with issued Slash exemptions and a zero hand limit distinct from HP.");
        var before = Views();
        var commands = game.AcceptedCommands.Count;
        var movements = game.CardMovements.Count;
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 740, Path.Combine(output, "discard-recommendation-before.png"));
            var recommend = Program.Find<Button>(root).Single(b => ReferenceEquals(b.Command, model.RecommendDiscardCommand));
            Program.Assert(recommend.Visibility == Visibility.Visible && recommend.IsEnabled,
                "The fixture must use the visible enabled discard recommendation button.");
            typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(recommend, null);
            var selected = model.Hand.Where(c => c.IsSelected).ToArray();
            var legal = selected.Length == prompt.RequiredCardCount && selected.All(c => prompt.ValidCardIds.Contains(c.Id) && c.IsPlayable);
            var hint = model.ActionHint.StartsWith(prompt.Prompt, StringComparison.Ordinal) &&
                model.ActionHint.Contains($"已选 {selected.Length} 张");
            Program.Assert(Views().SequenceEqual(before) && game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements,
                "Recommending discard must remain a reversible draft and preserve all player views.");
            model.OpenContextGuideCommand.Execute(null);
            var guide = model.CurrentGuideBody.Contains(prompt.Prompt);
            Console.WriteLine($"Discard recommendation: HP={human.Hp}, required={prompt.RequiredCardCount}, eligible={string.Join(',', prompt.ValidCardIds)}, exempt={string.Join(',', exempt)}, selected={string.Join(',', selected.Select(c => c.Id))}, legal={legal}, nativeHint={hint}, nativeGuide={guide}; hint={model.ActionHint}; guide={model.CurrentGuideBody}.");
            Program.Assert(legal && hint && guide,
                $"Recommendation must select only published discard material and show the actual native limit on both surfaces: legal={legal}, nativeHint={hint}, nativeGuide={guide}.");
            model.ToggleHelpCommand.Execute(null);
            var ids = selected.Select(c => c.Id).Order().ToArray();
            model.ClearSelectionCommand.Execute(null);
            Program.Assert(model.SelectedDiscardCount == 0 && !model.CanConfirmSelected && model.Hand.All(c => !c.IsSelected),
                "Cancelling the recommendation must clear only the draft.");
            model.RecommendDiscardCommand.Execute(null);
            Program.Assert(model.Hand.Where(c => c.IsSelected).Select(c => c.Id).Order().SequenceEqual(ids) && model.CanConfirmSelected,
                "Recommending again must retain the same complete legal set.");
            var prepared = Boundary(game);
            var preparedHint = model.ActionHint;
            model.StartTutorialCommand.Execute(null);
            Program.Assert(model.IsTutorialActive && !ReferenceEquals(Program.Engine(model), game),
                "The actual tutorial command must suspend the prepared native discard independently.");
            model.ExitTutorialCommand.Execute(null);
            Console.WriteLine($"Tutorial resume discard: selected={string.Join(',', model.Hand.Where(card => card.IsSelected).Select(card => card.Id).Order())}, required={model.RequiredDiscardCount}, confirm={model.CanConfirmSelected}.");
            Program.Assert(!model.IsTutorialActive && ReferenceEquals(Program.Engine(model), game) && Boundary(game) == prepared &&
                game.CreateSnapshot(0).PendingDecision!.PromptId == prompt.PromptId && model.IsDiscardSelectionPending &&
                model.RequiredDiscardCount == prompt.RequiredCardCount && model.SelectedDiscardCount == ids.Length &&
                model.Hand.Where(card => card.IsSelected).Select(card => card.Id).Order().SequenceEqual(ids) &&
                model.ActionHint == preparedHint && model.CanConfirmSelected,
                "Returning from teaching must preserve the complete eligible discard draft and its original prompt/limit without touching any safe view or journal.");
            Program.Render(root, 1120, 740, Path.Combine(output, "discard-recommendation-ready.png"));
            var confirm = (Button)window.FindName("PlayCardButton");
            Program.Assert(confirm.IsEnabled && confirm.Command!.CanExecute(confirm.CommandParameter),
                "The real confirmation button must accept the legal recommended set.");
            var nativeBefore = game.CreateCheckpoint();
            var reference = GameReplay.Restore(nativeBefore, registry);
            var nativeResult = reference.Submit(new DiscardCardsCommand(0, ids, prompt.PromptId, nativeBefore.Revision));
            Program.Assert(nativeResult.Accepted, nativeResult.Error?.Message ?? "The independent original recommended discard must be natively accepted.");
            typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(confirm, null);
            Program.Assert(!model.IsDiscardSelectionPending && model.SelectedDiscardCount == 0 &&
                game.AcceptedCommands.Count == commands + 1 && game.Revision == nativeBefore.Revision + 1 &&
                Boundary(game) == Boundary(reference) && game.AcceptedCommands.Last() is DiscardCardsCommand paid &&
                paid.ActorSeat == 0 && paid.PromptId == prompt.PromptId && paid.ExpectedRevision == nativeBefore.Revision &&
                paid.CardIds.SequenceEqual(ids) && game.Events.Select(e => e.Payload).OfType<HandLimitDiscardedEvent>().Single().CardIds.SequenceEqual(ids) &&
                ids.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.HandLimitDiscard) == 1) &&
                exempt.All(id => game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id)),
                "Confirm must submit one exact native discard, pay only the eligible entities once and retain all exempt Slash material.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(restored.CreateSnapshot(seat)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(seat))), "Cold replay must retain the actual recommended discard and every private player view.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }

        string[] Views() => Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        void Accept(GameCommand command)
        {
            var result = original.Submit(command);
            Program.Assert(result.Accepted, result.Error?.Message ?? "The real discard fixture command was rejected.");
        }
        void Reach(DecisionKind kind)
        {
            for (var step = 0; step < 40; step++)
            {
                if (original.PendingDecision is { PlayerSeat: 0 } current && current.Kind == kind) return;
                Program.Assert(original.PendingDecision is null, "The fixed discard fixture reached an unexpected private decision.");
                Accept(new AdvanceOneStepCommand(original.Revision));
            }
            throw new InvalidOperationException("The fixed discard fixture did not reach its native prompt.");
        }
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-discard-advice", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Skill}}","revision":1,
                 "modifiers":[{"id":"limit","query":"handLimit","operation":"set","value":0,"priority":0}],
                 "activations":[{"id":"exempt","usesPerTurn":1,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                   "targetKind":"anyLiving","effects":[{"op":"grantTurnHandLimitCardKindExemption","target":"owner","cardKinds":["slash"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Skill] = new { name = "弃牌边界", description = "真实推荐弃牌夹具" } } }));
            builder.AddSkill(new(Skill, "弃牌边界", "杀不计入当前回合手牌上限") { Program = catalog.Programs[Skill] });
            builder.AddSkill(new("fixture:discard-advice-idle", "固定选将", "无主动技能")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new(Owner, "弃牌夹具", "supporter", Skill, "wei", 3));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:discard-advice-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "其他角色", "supporter", "fixture:discard-advice-idle", "shu", 4));
            builder.AddDeck(new("fixture:discard-advice-deck", "固定混合手牌", 10, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(i % 2 == 0 ?
                "standard:slash" : "standard:peach", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "弃牌边界", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:discard-advice-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
