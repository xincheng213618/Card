using System.IO;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ChainedStateBasicUiChecks
{
    public static void FalseMarkerSelectsAndConfirmsExactSource()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            Program.Assert(step < 40, "The fixed UI fixture did not reach its real Play prompt.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion,
            DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var zero = model.HumanActiveSkillActions.First(a => a.CardId == 0 &&
            a.ChainedStateBasicUse == false && a.PlayedCardKind == CardKind.Slash);
        Program.Assert(zero.ConversionSource is { SkillId: Fixture.Xianwan, BindingId: "unchain-slash", OwnerSeat: 0 } &&
                       zero.TieredRoundZeroUse is null && zero.DrawFundedDistinctBasicUse is null &&
                       model.SupplementalActiveSkillActions.Contains(zero) && model.Hand.All(c => c.Id > 0),
            "A nullable marker whose value is false exposes the real zero-entity Slash through active-action controls.");
        var count = Save(model, store).Commands.Count;

        model.SelectActiveSkillCommand.Execute(zero with
        {
            ConversionSource = zero.ConversionSource! with { SkillInstanceId = "fixture:unpublished-chain-source" }
        });
        Program.Assert(!model.IsActiveSkillSelectionPending && Save(model, store).Commands.Count == count,
            "An action with a different source instance cannot select the currently published chain-payment action.");
        model.SelectActiveSkillCommand.Execute(zero with { ChainedStateBasicUse = true });
        Program.Assert(!model.IsActiveSkillSelectionPending && Save(model, store).Commands.Count == count,
            "Changing the nullable marker from false to true cannot alias the published release cost.");

        model.SelectActiveSkillCommand.Execute(zero);
        Program.Assert(model.IsActiveSkillSelectionPending && !model.CanConfirmSelected &&
                       model.Hand.All(c => !c.IsPlayable && !c.IsSelected) &&
                       model.Seats.Where(s => s.IsLegalTarget).Select(s => s.Seat).ToHashSet().SetEquals(zero.TargetSeats),
            "False enters the existing target draft without selecting an entity or submitting a command.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(!model.IsActiveSkillSelectionPending && Save(model, store).Commands.Count == count,
            "Cancelling the target draft neither submits a Use nor changes the real chain state.");

        model.SelectActiveSkillCommand.Execute(zero);
        foreach (var target in zero.TargetSeats.Reverse())
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == target));
        Program.Assert(model.CanConfirmSelected, "Selecting the published target set enables the ordinary confirmation control.");
        model.ConfirmSelectedCommand.Execute(null);
        var checkpoint = Save(model, store);
        var command = checkpoint.Commands.OfType<PlayCardCommand>().Last();
        Program.Assert(checkpoint.Commands.Count == count + 1 && command.CardId == 0 &&
                       command.PlayedCardKind == zero.PlayedCardKind && command.TargetCardId == zero.TargetCardId &&
                       command.TargetSeats!.SequenceEqual(zero.TargetSeats) && command.ConversionSource == zero.ConversionSource &&
                       (command.AdditionalConversionSources ?? []).SequenceEqual(zero.AdditionalConversionSources ?? []),
            "Confirmation submits one real PlayCard command retaining the exact source and ordered targets.");

        for (var step = 0; !model.CanEndTurn; step++)
        {
            Program.Assert(step < 40 && model.CanStepAi, "The actual zero-Slash child did not return to the human Play prompt.");
            model.StepAiCommand.Execute(null);
        }
        checkpoint = Save(model, store);
        // Read the public save through the normal replay API rather than reflecting
        // the view model's private engine or injecting any prepared state.
        var replayed = GameReplay.Restore(checkpoint, registry);
        var paid = replayed.Events.Select(e => e.Payload).OfType<ChainedStateBasicPaidEvent>().Single();
        var issued = replayed.Events.Select(e => e.Payload).OfType<ChainedStateBasicIssuedEvent>().Single();
        var actual = replayed.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>()
            .Single(e => e.Action.ActionId == issued.Receipt.CardActionId).Action;
        Program.Assert(paid.Payment.WasChained && !paid.Payment.DesiredChained &&
                       paid.Payment.Source == zero.ConversionSource && issued.Receipt.Payment == paid.Payment &&
                       actual.Type == CardActionType.Use && actual.ActorSeat == 0 && actual.ProviderSeat == 0 &&
                       actual.EffectiveKind == CardKind.Slash && actual.PhysicalCards.Count == 0 &&
                       actual.TargetSeats.SequenceEqual(zero.TargetSeats) && actual.ConversionChain.SequenceEqual([zero.ConversionSource!]) &&
                       replayed.CardMovements.All(m => m.CardId > 0),
            "The saved UI command replays as one paid chain release and one genuine zero-material Slash with the original source.");
        count = checkpoint.Commands.Count;
        model.SelectActiveSkillCommand.Execute(zero);
        Program.Assert(!model.IsActiveSkillSelectionPending && !model.CanConfirmSelected &&
                       model.HumanActiveSkillActions.All(a => !a.ChainedStateBasicUse.HasValue) &&
                       Save(model, store).Commands.Count == count && model.Hand.All(c => c.Id > 0),
            "After the real chain cost is paid, the stale action cannot open another draft or submit another command.");
    }

    private static GameCheckpoint Save(MainViewModel model, MemorySaveStore store)
    {
        model.SaveGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return store.Read(GameSaveSlot.Manual).Checkpoint;
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected the real fixture command to be accepted.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:classic-chained-state-basic-ui";
        internal const string Owner = "fixture:chain-ui-owner";
        internal const string Xianwan = "ol:xianwan";
        public PackageManifest Manifest { get; } = new("fixture:chained-state-basic-ui", "1.0.0", "连环费用零实体界面小夹具");

        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream(
                "CardGame.Content.Standard.SkillPrograms.ordinary-yang-yan.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream(
                "CardGame.Content.Standard.SkillPrograms.ordinary-yang-yan.presentation.json")!);
            var production = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            builder.AddSkill(new(Xianwan, "娴婉", "使用当前嵌入规则的连环状态费用") { Program = production.Programs[Xianwan] });
            var fixture = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"fixture:chain-ui-start","revision":1,"triggers":[{"id":"chain","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"fixture:chain-ui-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    ["fixture:chain-ui-start"] = new { name = "真实初始连环", description = "游戏开始时通过原生节点进入连环状态" },
                    ["fixture:chain-ui-quiet"] = new { name = "安静回合", description = "固定小夹具跳过其他角色的出牌阶段" }
                }
            }));
            foreach (var (id, program) in fixture.Programs)
                builder.AddSkill(new(id, id, "真实小夹具") { Program = program });
            builder.AddSkill(new("fixture:chain-ui-idle", "固定候选", "无运行能力")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new(Owner, "连环界面来源", "supporter", Xianwan, "jin", 8,
                ["fixture:chain-ui-start"], GeneralGender.Female));
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:chain-ui-target-{i}", "固定真实目标", "supporter",
                    "fixture:chain-ui-idle", "qun", 8, ["fixture:chain-ui-quiet"], GeneralGender.Male));
            builder.AddDeck(new("fixture:chain-ui-deck", "固定实体装备", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "连环零实体动作", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:chain-ui-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, "fixture:chain-ui-target-1", "fixture:chain-ui-target-2", "fixture:chain-ui-target-3"]));
        }
    }
}
