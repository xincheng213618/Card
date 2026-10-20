using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MoDiaoChanUiChecks
{
    public static void LockedHandStillRequiresConfirmation(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mo-diao-chan-ui", Owner, 8, 0, 0, 0, 0, [],
            [C(CardKind.Slash, Suit.Heart), C(CardKind.Slash, Suit.Spade)], [],
            [new(Source, Hp: 8) { StartingHand = [C(CardKind.Dodge, Suit.Spade), C(CardKind.Dodge, Suit.Club)] },
                new(Enemy, Hp: 8)], [new(Ally, Hp: 8)], new string('a', 64));
        var prepared = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(prepared, new StartGameCommand());
        ReachPlay(prepared);
        var promise = Facts<MoDiaoChanPromiseEvent>(prepared).FirstOrDefault(e => e.TargetSeat == 0);
        Program.Assert(promise is not null && Facts<TurnStartedEvent>(prepared).Skip(promise.AfterTurnStartOrdinal).All(e => e.ActorSeat != 0) &&
            prepared.CreateSnapshot(0).PendingDecision!.ForcedHandSelection is null,
            "The paid round-start promise follows the first TurnStarted and must wait for the next actual human turn. " + HuanhuoDiagnostic(prepared));
        Accept(prepared, new EndPlayPhaseCommand(0, prepared.Revision, prepared.CreateSnapshot(0).PendingDecision!.PromptId));
        ReachPlay(prepared);
        var hint = prepared.CreateSnapshot(0).PendingDecision!.ForcedHandSelection;
        Program.Assert(hint is { SkillId: "ol:mo-huanhuo", RemainingUses: 2 } &&
            Facts<TurnStartedEvent>(prepared).Skip(promise!.AfterTurnStartOrdinal).First(e => e.ActorSeat == 0).TurnNumber == prepared.State.TurnNumber,
            "The real enemy source must issue a private forced hand selection at its actual promised turn. " + HuanhuoDiagnostic(prepared));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, 7, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        hint = game.CreateSnapshot(0).PendingDecision!.ForcedHandSelection!;
        var commands = game.AcceptedCommands.Count;
        var revision = game.Revision;
        var movements = game.CardMovements.Count;
        var selected = model.Hand.Single(card => card.Id == hint.CardId);
        Program.Assert(selected.IsSelected && !model.CanConfirmSelected && model.CanEndTurn,
            "Loading the actual hint selects its hand entity without choosing a Slash target or using the card.");
        model.SelectCardCommand.Execute(model.Hand.First(card => card.Id != hint.CardId));
        model.SelectCardCommand.Execute(selected);
        Program.Assert(model.Hand.Where(card => card.IsSelected).Select(card => card.Id).SequenceEqual([hint.CardId]),
            "Clicking another card or the locked card cannot replace or cancel the forced ordinary-card draft.");
        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 3));
        Program.Assert(model.CanConfirmSelected && model.Seats.Single(seat => seat.Seat == 3).IsSelectedTarget,
            "The player still chooses the locked Slash's actual legal target.");
        model.IsDeveloperView = true;
        model.IsDeveloperView = false;
        Program.Assert(model.Hand.Single(card => card.Id == hint.CardId).IsSelected &&
            model.Seats.Single(seat => seat.Seat == 3).IsSelectedTarget && model.CanConfirmSelected &&
            game.AcceptedCommands.Count == commands && game.Revision == revision && game.CardMovements.Count == movements,
            "Repeated refresh preserves the target and never sends an automatic Use or any other command.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(model.Hand.Single(card => card.Id == hint.CardId).IsSelected && !model.CanConfirmSelected &&
            !model.Seats.Any(seat => seat.IsSelectedTarget),
            "Cancellation clears the target while keeping the actual forced card selected.");
        var skill = model.HumanActiveSkillActions.Single(action => action.ProgramSkillId == Driver);
        model.SelectActiveSkillCommand.Execute(skill);
        ReachPlay(game);
        Program.Assert(game.AcceptedCommands.OfType<UseProgramSkillCommand>().Any(command => command.SkillId == Driver) &&
            game.CreateSnapshot(0).PendingDecision!.ForcedHandSelection?.CardId == hint.CardId &&
            model.Hand.Single(card => card.Id == hint.CardId).IsSelected,
            "The forced card draft leaves real active-skill use available, and returns to the same still-usable lock.");
        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 3));
        var beforeConfirm = game.AcceptedCommands.Count;
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.AcceptedCommands.Count == beforeConfirm + 1 && game.AcceptedCommands.Last() is PlayCardCommand play &&
            play.CardId == hint.CardId && play.TargetSeats.SequenceEqual([3]),
            "Only explicit confirmation submits one actual card command with the player's chosen target.");
        ReachPlay(game);
        Program.Assert(model.CanEndTurn, "Ending Play remains available after the player's actual card use.");
        model.EndTurnCommand.Execute(null);
        Program.Assert(game.AcceptedCommands.Last() is EndPlayPhaseCommand,
            "The actual End Play command remains available independently of the hand hint.");
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 160; step++)
        {
            var pending = game.CreateSnapshot(0).PendingDecision;
            if (pending is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
            Program.Assert(pending is null || pending.PlayerSeat != 0,
                "The UI fixture encountered an unexpected human prompt: " + pending?.Prompt);
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded real Huanhuo UI fixture did not reach human Play.");
    }

    private static T[] Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string HuanhuoDiagnostic(GameEngine game) => JsonSerializer.Serialize(new {
        game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase,
        Turns = Facts<TurnStartedEvent>(game), Promises = Facts<MoDiaoChanPromiseEvent>(game),
        Starts = Facts<MoDiaoChanStartedEvent>(game), Costs = Facts<MoDiaoChanCostEvent>(game),
        Locks = Facts<MoDiaoChanLockEvent>(game), Phases = Facts<ActualDiscardRecoveryPhaseStartedEvent>(game)
    });

    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static ShanheStartingCard C(CardKind kind, Suit suit) => new(kind, suit, 7);
    private const string Owner = "fixture:mo-diao-ui-owner", Source = "fixture:mo-diao-ui-source",
        Ally = "fixture:mo-diao-ui-ally", Enemy = "fixture:mo-diao-ui-enemy",
        Driver = "fixture:mo-diao-ui-driver", Quiet = "fixture:mo-diao-ui-quiet", Deck = "fixture:mo-diao-ui-deck";
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mo-diao-chan-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:mo-diao-chan");
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge })
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mo-diao-ui-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Driver + "\",\"revision\":1,\"activations\":[{\"id\":\"draw\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]}," +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var labels = new Dictionary<string, string> { [Driver] = "真实技能", [Quiet] = "静候" };
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = labels.ToDictionary(pair => pair.Key, pair => new { name = pair.Value, description = pair.Value }) }));
            foreach (var (id, program) in programs.Programs)
                b.AddSkill(new(id, labels[id], labels[id]) { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "受幻惑者", "supporter", "standard:none", "shu", 8, [Driver]));
            b.AddGeneral(new(Source, "魔貂蝉", "supporter", "standard:none", "qun", 8, ["ol:mo-huanhuo", Quiet]));
            foreach (var id in new[] { Ally, Enemy }) b.AddGeneral(new(id, "其他角色", "supporter", "standard:none", "wei", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40)
                .Select(_ => new ContentDeckPhysicalCard("fixture:mo-diao-ui-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "幻惑界面小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, Source, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
