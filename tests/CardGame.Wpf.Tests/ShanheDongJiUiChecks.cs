using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShanheDongJiUiChecks
{
    private const string Skill = StandardShanheS5DongshiPackage.LianzhuId;
    private const string Xiahui = "ol:xiahui", Human = "fixture:dongji-ui-human";
    private const string Giver = "fixture:dongji-ui-giver", Enemy = "fixture:dongji-ui-enemy";
    private const string Deck = "fixture:dongji-ui-deck";
    private const string GiftReason = "skill-program.lianzhu.GiftChildren";
    private const string DiscardReason = "skill-program.lianzhu.DiscardChildren";

    public static void DynamicRecipientCountConfirmsExactCardsAndExcludesLockedGift(string output)
    {
        var (prepared, registry) = Prepare();
        var oldCards = prepared.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToArray();
        Accept(prepared, new EndPlayPhaseCommand(0, prepared.Revision, prepared.PendingDecision!.PromptId));
        Until(prepared, () => prepared.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: Skill } prompt &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") == "discard"));
        var branch = prepared.CreateSnapshot(0).PendingDecision!;
        var discard = branch.Choices.Single(choice => choice.Parameters.GetValueOrDefault("branch") == "discard");
        Accept(prepared, new AnswerPromptCommand(0, branch.PromptId, discard.Id, prepared.Revision));
        var gift = prepared.CardMovements.Single(move => move.Reason.Value == GiftReason);
        var rootId = prepared.Events.Select(item => item.Payload).OfType<LianZhuChoiceAmountCapturedEvent>().Single().FrameId;
        Program.Assert(gift.From == CardLocation.Hand(1) && gift.To == CardLocation.Hand(0) &&
            prepared.Events.Select(item => item.Payload).OfType<XiaHuiRestrictionGrantedEvent>().Any(fact =>
                fact.Restriction is { RecipientSeat: 0 } restriction && restriction.CardId == gift.CardId) &&
            prepared.AiThoughts.Any(thought => thought.ActorSeat == 1),
            "The real NPC ally must give its original black entity to human0 and issue the actual Xiahui restriction; no NPC activation is injected. " + Diagnostic(prepared));

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var surface = (FrameworkElement)window.Content;
        try
        {
            Layout();
            AssertSelection();
            var commands = game.AcceptedCommands.Count;
            var movements = game.CardMovements.Count;
            var locked = model.Hand.Single(card => card.Id == gift.CardId);
            Program.Assert(!locked.IsPlayable && !CardButton(gift.CardId).IsEnabled &&
                oldCards.All(id => model.Hand.Single(card => card.Id == id).IsPlayable && CardButton(id).IsEnabled),
                "The bound hand buttons must enable the three old red materials and disable the newly gifted Xiahui-locked black card.");
            model.SelectCardCommand.Execute(locked);
            Program.Assert(!locked.IsSelected && model.Hand.All(card => !card.IsSelected) && game.AcceptedCommands.Count == commands,
                "Even a direct click command on the disabled locked card cannot enter the local draft or answer a native prompt.");
            foreach (var id in oldCards.Take(2)) model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == id));
            Program.Assert(model.Hand.Count(card => card.IsSelected) == 2 && !model.CanConfirmSelected &&
                !model.ConfirmSelectedCommand.CanExecute(null) && model.SelectedCardText.Contains("2/3"),
                "Two of the frozen three cards remain a local draft with confirmation disabled.");
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == rootId).LianZhu!.Selected.Count == 0,
                "Confirming an incomplete draft must submit no answer and pay no part of the actual discard.");

            var frozen = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            model.SaveGameCommand.Execute(null);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            Layout();
            Program.Assert(!model.HasSaveError && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == frozen &&
                model.Hand.All(card => !card.IsSelected),
                "Desktop cold restoration preserves the exact unpaid frozen-X prompt and clears only its uncommitted UI draft.");
            AssertSelection();

            var remaining = new List<int> { 3 };
            var earlyPayment = false;
            game.StateChanged += snapshot =>
            {
                if (snapshot.PendingDecision is not { PlayerSeat: 0, SkillPrompt.SkillId: Skill, RequiredCardCount: > 0 } current) return;
                if (remaining[^1] != current.RequiredCardCount) remaining.Add(current.RequiredCardCount);
                if (current.RequiredCardCount < 3 && game.CardMovements.Any(move => move.Reason.Value == DiscardReason)) earlyPayment = true;
            };
            commands = game.AcceptedCommands.Count;
            foreach (var id in oldCards) model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == id));
            Program.Assert(model.CanConfirmSelected && model.ConfirmSelectedCommand.CanExecute(null) &&
                model.Hand.Count(card => card.IsSelected) == 3 && model.SelectedCardText.Contains("3/3") && !model.HasCenterChoices &&
                game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements,
                "Selecting the complete set enables one shared confirmation while all three real entities remain unpaid.");
            model.ConfirmSelectedCommand.Execute(null);
            var answers = game.AcceptedCommands.Skip(commands).OfType<AnswerPromptCommand>().ToArray();
            Program.Assert(answers.Length == 3 && answers.Select(answer => answer.PromptId).Distinct().Count() == 3 &&
                remaining.SequenceEqual(new[] { 3, 2, 1 }) && !earlyPayment,
                "One UI confirmation must requery three distinct real prompts with remaining counts 3→2→1 and defer movement until the last answer. " +
                JsonSerializer.Serialize(new { Remaining = remaining, Answers = answers.Length, earlyPayment }));
            Until(game, () => game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Any(fact => fact.FrameId == rootId));
            AssertPaid(game);
            var completed = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            model.SaveGameCommand.Execute(null);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            Layout();
            Program.Assert(!model.HasSaveError && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == completed &&
                !model.IsOwnedCardSelectionPending && model.Hand.Single(card => card.Id == gift.CardId) is { IsSelected: false },
                "Loading the completed desktop save cannot republish the completed payment or select the surviving locked gift.");
            AssertPaid(game);
        }
        finally { window.Content = null; window.Close(); }

        void Layout() { surface.Measure(new Size(1120, 740)); surface.Arrange(new Rect(0, 0, 1120, 740)); surface.UpdateLayout(); }
        Button CardButton(int id) => Program.Find<Button>(surface).Single(button => button.Name == "CardButton" &&
            button.DataContext is CardViewModel card && card.Id == id);
        void AssertSelection()
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            Program.Assert(prompt is { PlayerSeat: 0, IsPrivate: true, RequiredCardCount: 3, SkillPrompt.SkillId: Skill } &&
                prompt.Choices.SelectMany(choice => choice.Cards).ToHashSet().SetEquals(oldCards) &&
                !prompt.Choices.Any(choice => choice.Cards.Contains(gift.CardId)) &&
                model.IsOwnedCardSelectionPending && !model.CanConfirmSelected && model.ActionHint.Contains("3 张手牌或装备") &&
                game.CreateSnapshot(1).PendingDecision is null && game.CreateSnapshot(1).Players[0].Hand.Count == 0,
                "The restored human view must show frozen X=3 through the shared owned-card selector, excluding the locked gift and hiding private payment choices from the NPC. " + Diagnostic(game));
        }
        void AssertPaid(GameEngine current)
        {
            var paid = current.CardMovements.Where(move => move.Reason.Value == DiscardReason).ToArray();
            Program.Assert(paid.Length == 3 && paid.Select(move => move.CardId).ToHashSet().SetEquals(oldCards) &&
                paid.All(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) &&
                current.Events.Select(item => item.Payload).OfType<LianZhuSegmentPaidEvent>().Count(fact =>
                    fact.FrameId == rootId && fact.Stage == LianZhuStage.DiscardChildren && fact is { ActorSeat: 0, Count: 3 }) == 1 &&
                current.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>().Count(fact => fact.FrameId == rootId && fact.Completed) == 1 &&
                current.Events.Select(item => item.Payload).OfType<LianZhuChoiceAmountCapturedEvent>().Count(fact => fact.FrameId == rootId) == 1 &&
                current.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).SequenceEqual([gift.CardId]) &&
                !current.ResolutionStack.Any(frame => frame.Id == rootId),
                "The exact three old hand entities must pay one native logical discard, resolve once and leave the locked gift untouched before and after cold replay. " + Diagnostic(current));
        }
    }

    private static (GameEngine Game, ContentRegistry Registry) Prepare()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(83, 1, "fixture:dongji-ui", Human, 3, 0, 0, 0, 0, [],
            [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3), new(CardKind.Dodge, Suit.Heart, 4)], [],
            [new(Enemy, Hp: 1) { StartingHand = [] }],
            [new(Giver) { StartingHand = [new(CardKind.Dodge, Suit.Spade, 7)] }], new string('c', 64));
        var game = GameEngine.CreateStandard(new()
        {
            Seed = setup.BattleSeed, PlayerCount = 3, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, registry);
        Accept(game, new StartGameCommand());
        Until(game, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }

    private static void Until(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 96; step++)
        {
            if (reached()) return;
            Program.Assert(game.State.Winner == Winner.None && game.PendingDecision?.PlayerSeat != 0,
                "Unexpected human boundary in the bounded actual NPC Dong Ji UI fixture. " + Diagnostic(game));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded actual NPC Dong Ji UI fixture did not reach its native boundary. " + Diagnostic(game));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException((result.Error?.Message ?? "Actual Dong Ji UI command rejected.") + " " + Diagnostic(game));
    }
    private static string Diagnostic(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, game.State.TurnNumber,
        Prompt = game.CreateSnapshot(0).PendingDecision,
        Players = game.CreateSnapshot(0).Players.Select(player => new { player.Seat, player.Hp, player.HandCount }),
        Paid = game.Events.Select(item => item.Payload).OfType<LianZhuSegmentPaidEvent>(),
        LastThought = game.AiThoughts.LastOrDefault()
    });

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-dongji-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var production = StandardShanheCampaignCatalog.CreateBattleRegistry(new(83, 1, "fixture:dongji-ui-content",
                StandardShanheS5DongshiPackage.DongJiQiaoxiaoId, 8, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('b', 64)));
            foreach (var id in new[] { Skill, Xiahui }) builder.AddSkill(production.GetSkill(id));
            builder.AddGeneral(new(Human, "真实受赠玩家", "", "standard:none", "qun", 8));
            builder.AddGeneral(new(Giver, "真实连诛同行", "", Skill, "qun", 8, [Xiahui], GeneralGender.Female));
            builder.AddGeneral(new(Enemy, "未持牌敌方", "", "standard:none", "wei", 8));
            builder.AddDeck(new(Deck, "零自然摸牌固定实体牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实同行赠牌与玩家弃牌界面", 3, 3, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Human, Giver, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
