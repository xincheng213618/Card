using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TacticalAiChecks
{
    public static void LegacyCompatibility()
    {
        using var stream = typeof(TacticalAiChecks).Assembly.GetManifestResourceStream("CardGame.Core.Tests.Fixtures.legacy-ai-checkpoints.json")!;
        var fixtures = JsonSerializer.Deserialize<LegacyFixture[]>(stream)!;
        Require(fixtures.Length == 4, "Expected three old full matches and one genuine WPF paused checkpoint.");
        foreach (var fixture in fixtures)
        {
            Require(!fixture.CheckpointJson.Contains("AiPolicyVersion"), "Golden fixture must remain pre-upgrade JSON.");
            var checkpoint = GameCheckpointJson.Deserialize(fixture.CheckpointJson);
            Require(checkpoint.Options.AiPolicyVersion == 1, "Old JSON must select legacy decisions.");
            Require(checkpoint.RulesVersion == 1, "Old JSON must select legacy rules event semantics.");
            var restored = GameReplay.Restore(checkpoint, StandardContentRegistry.Create());
            var current = Fixture(fixture.Name, restored);
            Require(current.SnapshotHash == fixture.SnapshotHash, $"{fixture.Name}: legacy final state changed.");
            Require(current.EventHash == fixture.EventHash, $"{fixture.Name}: legacy event stream changed.");
        }
    }

    public static void PlayDecisions()
    {
        foreach (var test in Cases())
        {
            var choice = new SimpleAiBrain(0, 271, 2).ChoosePlay(test.View, test.Actions, 1);
            Require(choice.Action.Kind == test.Expected, $"{test.Name}: chose {choice.Action.Kind}.");
        }
        // Counterexamples prevent a policy that passes every group card or wine.
        var rebel = View(Role.Rebel, CardKind.BarbarianAssault);
        var lord = rebel.Players.Single(player => player.Role == Role.Lord).Seat;
        rebel = Change(rebel, lord, player => player with { Hp = 1, HandCount = 0 });
        Pick(rebel, [Action(LegalActionKind.BarbarianAssault, 1000), End()], LegalActionKind.BarbarianAssault);
        var heal = Change(View(Role.Lord, CardKind.PeachGarden), 0, player => player with { Hp = 1 });
        Pick(heal, [Action(LegalActionKind.PeachGarden, 1000), End()], LegalActionKind.PeachGarden);
        var wine = View(Role.Rebel, CardKind.Alcohol, CardKind.Slash);
        lord = wine.Players.Single(player => player.Role == Role.Lord).Seat;
        Pick(wine, [Action(LegalActionKind.Alcohol, 1000, 0), Action(LegalActionKind.Slash, 1001, lord), End()], LegalActionKind.Alcohol);
        var redWine = Change(View(Role.Rebel, CardKind.Alcohol), 0, player => player with { Skill = SkillKind.Wusheng });
        var wineConversion = Action(LegalActionKind.Slash, 1000, lord) with { PlayedCardKind = CardKind.Slash };
        Pick(redWine, [Action(LegalActionKind.Alcohol, 1000, 0), wineConversion, End()], LegalActionKind.Slash);
        var converting = Change(View(Role.Rebel, CardKind.Peach, CardKind.Slash), 0, player => player with { Skill = SkillKind.Wusheng });
        var converted = Action(LegalActionKind.Slash, 1000, lord) with { PlayedCardKind = CardKind.Slash };
        var selected = new SimpleAiBrain(0, 271, 2).ChoosePlay(converting, [converted, Action(LegalActionKind.Slash, 1001, lord), End()], 1);
        Require(selected.Action.CardId == 1001, "Physical Slash should preserve Peach when both can hit the same enemy.");

        foreach (var kind in new[] { LegalActionKind.Dismantlement, LegalActionKind.Snatch })
        {
            var view = View(Role.Rebel, kind == LegalActionKind.Snatch ? CardKind.Snatch : CardKind.Dismantlement);
            var card = new CardSnapshot(2000, CardKind.Indulgence, Suit.Heart, 6, "乐不思蜀", "6");
            view = Change(view, lord, player => player with { Judgment = [card] });
            var remove = Action(kind, 1000, lord) with { TargetCardId = 2000 };
            Pick(view, [remove, End()], LegalActionKind.EndPlay);
            view = Change(view, 0, player => player with { Role = Role.Loyalist });
            Pick(view, [remove, End()], kind);
        }
        var vulnerable = Change(wine, 0, player => player with { Hp = 1 });
        Pick(vulnerable, [Action(LegalActionKind.Alcohol, 1000, 0), Action(LegalActionKind.Slash, 1001, lord), End()], LegalActionKind.Slash);
    }

    public static void RescueDecisions()
    {
        var view = View(Role.Renegade, CardKind.Peach, CardKind.Alcohol);
        var lord = view.Players.Single(player => player.Role == Role.Lord).Seat;
        view = Change(view, lord, player => player with { Hp = 0 });
        Card[] peaches = [new(1000, CardKind.Peach, Suit.Heart, 7)];
        Card[] wines = [new(1001, CardKind.Alcohol, Suit.Spade, 7)];
        Require(new SimpleAiBrain(0, 271, 2).ChooseDyingResponse(view, lord, peaches, 1).UsePeach, "Renegade must save the Lord while other opponents live.");
        var duel = view with { Players = view.Players.Select(player => player with { IsAlive = player.Seat == 0 || player.Seat == lord }).ToArray() };
        Require(!new SimpleAiBrain(0, 271, 2).ChooseDyingResponse(duel, lord, peaches, 1).UsePeach, "Renegade must not save the final opponent.");
        var rebel = Change(view, 0, player => player with { Role = Role.Rebel });
        Require(!new SimpleAiBrain(0, 271, 2).ChooseDyingResponse(rebel, lord, peaches, 1).UsePeach, "Rebel should not save the enemy Lord.");
        var selfRescue = new SimpleAiBrain(0, 271, 2).ChooseDyingResponseWithAlcohol(Change(view, 0, player => player with { Hp = 0 }), 0, peaches, wines, 1);
        Require(selfRescue.UseAlcohol && !selfRescue.UsePeach, "Self-rescue should use wine before flexible Peach.");
        var otherRescue = new SimpleAiBrain(0, 271, 2).ChooseDyingResponseWithAlcohol(view, lord, [], wines, 1);
        Require(otherRescue.UseAlcohol && otherRescue.AlcoholCardId == wines[0].Id && !otherRescue.UsePeach,
            "Wine should rescue a high-value allied Lord from another seat.");
    }

    public static void PublicEvidence()
    {
        var view = View(Role.Rebel, CardKind.Slash, CardKind.Peach);
        var lord = view.Players.Single(player => player.Role == Role.Lord).Seat;
        var unknown = view.Players.First(player => player.Seat != 0 && player.Seat != lord).Seat;
        Require(view.Players.Where(player => player.Seat != 0).All(player => player.Hand.Count == 0), "Scenario must originate from a filtered view.");
        Require(view.Players.Single(player => player.Seat == unknown).Role is null, "Living non-Lord identity must be hidden.");
        LegalAction[] actions = [Action(LegalActionKind.Slash, 1000, unknown), End()];
        var brain = new SimpleAiBrain(0, 271, 2);
        Require(brain.ChoosePlay(view, actions, 1).Action.Kind == LegalActionKind.Slash, "An unknown seat is not an established ally.");
        brain.ObserveSlash(unknown, lord, lord, Role.Lord);
        Require(brain.ChoosePlay(view, actions, 2).Action.Kind == LegalActionKind.EndPlay, "Rebel should avoid hitting a likely ally after observing an attack on the Lord.");
        var fireAttackBrain = new SimpleAiBrain(0, 271, 3);
        fireAttackBrain.ObserveFireAttack(new PublicAttackEvidence(
            PublicAttackKind.FireAttack,
            unknown,
            lord,
            lord,
            Role.Lord));
        Require(fireAttackBrain.ChoosePlay(view, actions, 2).Action.Kind == LegalActionKind.EndPlay,
            "FireAttack public evidence must use the same identity inference boundary.");
        var tacticalV2 = new SimpleAiBrain(0, 271, 2);
        tacticalV2.ObservePublicAttack(new PublicAttackEvidence(
            PublicAttackKind.FireAttack,
            unknown,
            lord,
            lord,
            Role.Lord));
        Require(tacticalV2.ChoosePlay(view, actions, 2).Action.Kind == LegalActionKind.Slash,
            "AI policy v2 must retain its pre-v3 FireAttack behavior.");
        var dying = Change(view, unknown, player => player with { Hp = 0 });
        Require(brain.ChooseDyingResponse(dying, unknown, [new Card(1001, CardKind.Peach, Suit.Heart, 7)], 3).UsePeach, "Public evidence should also inform allied rescue.");
        Require(new SimpleAiBrain(0, 271, 2).ChoosePlay(view, actions, 1).Action.Kind == LegalActionKind.Slash, "Suspicion must not leak between games.");
    }

    public static void NationalPublicEvidence()
    {
        var baseView = NationalView();
        var unknown = 1;
        var knownAlly = 2;
        var knownEnemy = 3;
        var action = Action(LegalActionKind.Slash, 1000, unknown);
        var brain = new SimpleAiBrain(0, 271, 2);
        var before = brain.ChoosePlay(baseView, [action, End()], 1).Thought.Candidates
            .Single(candidate => candidate.Action.Kind == LegalActionKind.Slash).Score;

        brain.ObserveNationalAttack(knownAlly, unknown, "wei", "wei", null);
        var afterAllyAttack = brain.ChoosePlay(baseView, [action, End()], 2).Thought.Candidates
            .Single(candidate => candidate.Action.Kind == LegalActionKind.Slash).Score;
        Require(afterAllyAttack > before && brain.NationalEnemySuspicion[unknown] > 0,
            "A public attack by the AI faction did not make a hidden target more suspicious.");

        var sourceAttack = Action(LegalActionKind.Slash, 1000, unknown);
        var sourceBrain = new SimpleAiBrain(0, 271, 2);
        sourceBrain.ObserveNationalAttack(unknown, knownAlly, "wei", null, "wei");
        Require(sourceBrain.NationalEnemySuspicion[unknown] > 0 &&
                sourceBrain.ChoosePlay(baseView, [sourceAttack, End()], 1).Action.Kind == LegalActionKind.Slash,
            "A hidden source attacking a public ally was not treated as a likely enemy.");

        var conservative = new SimpleAiBrain(0, 271, 2);
        conservative.ObserveNationalAttack(unknown, knownEnemy, "wei", null, "shu");
        Require(conservative.NationalEnemySuspicion[unknown] < 0 &&
                conservative.ChoosePlay(baseView, [sourceAttack, End()], 1).Action.Kind == LegalActionKind.EndPlay,
            "An unknown source attacking a public enemy was over-trusted as a target.");

        var dying = Change(baseView, unknown, player => player with { Hp = 0 });
        var likelyEnemy = new SimpleAiBrain(0, 271, 2);
        likelyEnemy.ObserveNationalAttack(knownAlly, unknown, "wei", "wei", null);
        Require(!likelyEnemy.ChooseDyingResponse(
                dying,
                unknown,
                [new Card(1001, CardKind.Peach, Suit.Heart, 7)],
                1).UsePeach,
            "Public evidence should discourage rescuing a likely enemy in national war.");

        var likelyAlly = new SimpleAiBrain(0, 271, 2);
        likelyAlly.ObserveNationalAttack(unknown, knownEnemy, "wei", null, "shu");
        Require(likelyAlly.ChooseDyingResponse(
                dying,
                unknown,
                [new Card(1001, CardKind.Peach, Suit.Heart, 7)],
                1).UsePeach,
            "Public evidence should encourage rescuing a likely ally in national war.");

        for (var i = 0; i < 20; i++) brain.ObserveNationalAttack(knownAlly, unknown, "wei", "wei", null);
        Require(brain.NationalEnemySuspicion[unknown] == 6d, "National suspicion was not bounded for replay-safe diagnostics.");
        var legacy = new SimpleAiBrain(0, 271, 1);
        legacy.ObserveNationalAttack(knownAlly, unknown, "wei", "wei", null);
        Require(legacy.NationalEnemySuspicion.Count == 0 &&
                legacy.ChoosePlay(baseView, [action, End()], 1).Action.Kind == LegalActionKind.Slash,
            "Legacy AI policy must ignore national public-evidence updates.");
        Require(new SimpleAiBrain(0, 271, 2).NationalEnemySuspicion.Count == 0,
            "National suspicion leaked between AI instances.");
    }

    public static void PolicyReplay()
    {
        foreach (var version in new[] { 1, 2, 3 })
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 561923, HumanSeat = -1, HumanRole = null, AiPolicyVersion = version, UseInteractiveSetup = true }, StandardContentRegistry.Create());
            Require(game.Submit(new StartGameCommand()).Accepted, "Full AI match failed.");
            Require(game.State.Status == EngineStatus.Completed, "Full AI match did not complete.");
            var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            Require(checkpoint.Options.AiPolicyVersion == version, "Checkpoint lost its decision policy.");
            var restored = GameReplay.Restore(checkpoint, StandardContentRegistry.Create());
            Require(EventHash(restored) == EventHash(game) && SnapshotJson.Serialize(restored.State) == SnapshotJson.Serialize(game.State), "Policy replay changed a match.");
        }
        foreach (var version in new[] { 0, 4, -1 })
        {
            try { GameEngine.CreateStandard(new GameOptions { AiPolicyVersion = version }, StandardContentRegistry.Create()); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Unsupported AI policy was accepted.");
        }
    }

    public static void EndgameAndNullification()
    {
        foreach (var role in new[] { Role.Lord, Role.Loyalist })
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 721019, HumanSeat = 0, HumanRole = role }, StandardContentRegistry.Create());
            var full = game.CreateSnapshot(0, true);
            var renegade = full.Players.Single(player => player.Role == Role.Renegade).Seat;
            var view = View(role, CardKind.Slash) with
            {
                Players = View(role, CardKind.Slash).Players.Select(player =>
                {
                    var identity = full.Players.Single(fullPlayer => fullPlayer.Seat == player.Seat).Role;
                    return player.Seat == 0 || identity is Role.Lord or Role.Renegade ? player
                        : player with { IsAlive = false, Hp = 0, Role = identity, IsRoleRevealed = true, HandCount = 0 };
                }).ToArray()
            };
            var brain = new SimpleAiBrain(0, 271, 2);
            for (var i = 0; i < 12; i++) brain.ObserveDeath(renegade, Role.Rebel);
            Require(brain.RebelSuspicion[renegade] == -6, "Old evidence must have bounded confidence.");
            Require(view.Players.Single(player => player.Seat == renegade).Role is null, "Endgame opponent must remain hidden.");
            Require(brain.ChoosePlay(view, [Action(LegalActionKind.Slash, 1000, renegade), End()], 1).Action.Kind == LegalActionKind.Slash,
                "Public eliminated roles should prevent permanent friendship with the remaining Renegade.");
        }
        var self = View(Role.Lord, CardKind.Nullification);
        var brain2 = new SimpleAiBrain(0, 271, 2);
        var ownDraw = brain2.ChooseNullification(self, CardKind.DrawTwo, 0, 0, false, 0, [1000], 1);
        Require(ownDraw.CardId is null, "Do not nullify your own draw card.");
        var restore = brain2.ChooseNullification(self, CardKind.DrawTwo, 0, 0, true, 1, [1000], 2);
        Require(restore.CardId == 1000, "Counter-nullification should restore your beneficial card.");
        var injured = Change(self, 0, player => player with { Hp = 1 });
        Require(brain2.ChooseNullification(injured, CardKind.PeachGarden, 1, 0, false, 0, [1000], 3).CardId is null,
            "Receiving healing is not a hostile self-targeted effect.");
        Require(brain2.ChooseNullification(self, CardKind.Duel, 1, 0, false, 0, [1000], 4).CardId == 1000,
            "Self-targeted hostile Duel should still be nullified.");
    }

    public static void SupportSkills()
    {
        var view = View(Role.Rebel, CardKind.Slash, CardKind.Dodge);
        var lord = view.Players.Single(player => player.Role == Role.Lord).Seat;
        view = Change(view, lord, player => player with { Hp = 1, HandCount = 0 });
        var brain = new SimpleAiBrain(0, 271, 2);
        Require(brain.ChooseYuanhuCard(view, [1000, 1001], lord, 1).CardId is null, "Rebel must not spend a card healing the Lord.");
        Require(brain.ChooseJiemingTarget(view, [lord], 2).TargetSeat is null, "Rebel must not replenish the Lord's hand.");
        Require(brain.ChooseYijiGift(view, [1000, 1001], [lord], 3).TargetSeat is null, "Rebel must not give cards to the Lord.");
        var loyal = Change(view, 0, player => player with { Role = Role.Loyalist });
        Require(brain.ChooseYuanhuCard(loyal, [1000, 1001], lord, 4).CardId == 1000, "Loyalist should use a lower-value card to heal the Lord.");
        Require(brain.ChooseJiemingTarget(loyal, [lord], 5).TargetSeat == lord, "Loyalist should replenish the Lord's hand.");
        Require(brain.ChooseYijiGift(loyal, [1000, 1001], [lord], 6).CardId == 1000, "Loyalist should donate the lower-value card.");
    }

    public static void GuicaiJudgments()
    {
        var loyal = JudgmentView(Role.Loyalist);
        var lord = loyal.Players.Single(player => player.Role == Role.Lord).Seat;
        var loyalBrain = new SimpleAiBrain(0, 271, 2);
        Require(loyalBrain.ChooseGuicaiReplacement(
                loyal, lord, JudgmentReasons.Indulgence, [1000, 1001], CardKind.Dodge,
                Suit.Spade, 1, 7, rulesVersion: 11).CardId == 1000,
            "Guicai did not turn an allied Indulgence judgment into Heart.");
        Require(new SimpleAiBrain(0, 271, 2).ChooseGuicaiReplacement(
                loyal, lord, JudgmentReasons.SupplyShortage, [1000, 1001], CardKind.Dodge,
                Suit.Spade, 1, 7, rulesVersion: 11).CardId == 1001,
            "Guicai did not turn an allied Supply Shortage judgment into Club.");
        Require(new SimpleAiBrain(0, 271, 2).ChooseGuicaiReplacement(
                loyal, lord, JudgmentReasons.Lightning, [1000, 1001], CardKind.Dodge,
                Suit.Spade, 1, 5, rulesVersion: 11).CardId == 1000,
            "Guicai did not turn an allied Lightning hit into a miss.");
        Require(new SimpleAiBrain(0, 271, 2).ChooseGuicaiReplacement(
                loyal, lord, JudgmentReasons.Ganglie, [1000, 1001], CardKind.Dodge,
                Suit.Heart, 1, 7, rulesVersion: 11, usesClassicGanglieJudgment: true).CardId == 1001,
            "Guicai did not use the classic non-Heart Ganglie success suit.");

        var rebel = JudgmentView(Role.Rebel);
        lord = rebel.Players.Single(player => player.Role == Role.Lord).Seat;
        Require(new SimpleAiBrain(0, 271, 2).ChooseGuicaiReplacement(
                rebel, lord, JudgmentReasons.Indulgence, [1000, 1001], CardKind.Dodge,
                Suit.Heart, 1, 7, rulesVersion: 11).CardId == 1001,
            "Guicai did not turn an enemy Indulgence safe suit into a failed judgment.");
        Require(new SimpleAiBrain(0, 271, 2).ChooseGuicaiReplacement(
                rebel, lord, JudgmentReasons.Indulgence, [1000, 1001], CardKind.Dodge,
                Suit.Spade, 1, 7, rulesVersion: 10).CardId == 1000,
            "Rules 10 Guicai no longer preserves its historical red-card scoring.");
        Require(new SimpleAiBrain(0, 271, 2).ChooseGuicaiReplacement(
                loyal, lord, JudgmentReasons.Ganglie, [1000, 1001], CardKind.Dodge,
                Suit.Spade, 1, 7, rulesVersion: 10, usesClassicGanglieJudgment: true).CardId == 1000,
            "Rules 10 classic Ganglie no longer preserves its historical Guicai scoring.");
    }

    private static void Pick(GameSnapshot view, IReadOnlyList<LegalAction> actions, LegalActionKind expected) =>
        Require(new SimpleAiBrain(0, 271, 2).ChoosePlay(view, actions, 1).Action.Kind == expected, $"Expected {expected} for {string.Join(',', actions.Select(action => action.Kind))}.");

    private static LegacyFixture Fixture(string name, GameEngine game) => new(name, GameCheckpointJson.Serialize(game.CreateCheckpoint()),
        Hash(SnapshotJson.Serialize(game.CreateSnapshot(game.State.HumanSeat, true))), EventHash(game));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string EventHash(GameEngine game) => Hash(string.Join('\n', game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")));
    private sealed record LegacyFixture(string Name, string CheckpointJson, string SnapshotHash, string EventHash);

    private static IReadOnlyList<PlayCase> Cases()
    {
        var loyalist = View(Role.Loyalist, CardKind.BarbarianAssault);
        var lordSeat = loyalist.Players.Single(player => player.Role == Role.Lord).Seat;
        loyalist = Change(loyalist, lordSeat, player => player with { Hp = 1, HandCount = 0 });
        var rebel = View(Role.Rebel, CardKind.PeachGarden);
        lordSeat = rebel.Players.Single(player => player.Role == Role.Lord).Seat;
        rebel = Change(rebel, lordSeat, player => player with { Hp = 1 });
        var alcohol = View(Role.Lord, CardKind.Alcohol, CardKind.Slash);
        return
        [
            new("群伤不应杀死己方濒危主公", loyalist, [Action(LegalActionKind.BarbarianAssault, 1000), End()], LegalActionKind.EndPlay),
            new("桃园不应只回复敌方主公", rebel, [Action(LegalActionKind.PeachGarden, 1000), End()], LegalActionKind.EndPlay),
            new("没有合法杀时不应饮酒", alcohol, [Action(LegalActionKind.Alcohol, 1000, 0), End()], LegalActionKind.EndPlay)
        ];
    }

    private static GameSnapshot View(Role role, params CardKind[] cards)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 721019, HumanSeat = 0, HumanRole = role }, StandardContentRegistry.Create());
        var view = game.CreateSnapshot(0) with { Status = EngineStatus.AwaitingHumanPlay, Phase = TurnPhase.Play, CurrentSeat = 0, TurnNumber = 1 };
        return Change(view, 0, player => player with
        {
            Hand = cards.Select((kind, index) => new CardSnapshot(1000 + index, kind, Suit.Heart, 7, CardCatalog.Get(kind).DisplayName, "7")).ToArray(),
            HandCount = cards.Length
        });
    }

    private static GameSnapshot JudgmentView(Role role)
    {
        var view = View(role, CardKind.Slash, CardKind.Slash);
        return Change(view, 0, player => player with
        {
            Hand =
            [
                new CardSnapshot(1000, CardKind.Slash, Suit.Heart, 7, "杀", "7"),
                new CardSnapshot(1001, CardKind.Slash, Suit.Club, 7, "杀", "7")
            ],
            HandCount = 2
        });
    }

    private static GameSnapshot NationalView()
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 721019,
            PlayerCount = 4,
            HumanSeat = 0,
            HumanRole = null,
            ModeId = "national:lite-4",
            UseInteractiveSetup = false
        }, StandardContentRegistry.CreateWithNationalWarLite());
        var full = game.CreateSnapshot(0, true);
        var players = full.Players.Select(player => player with
        {
            Role = null,
            IsRoleRevealed = false,
            FactionId = player.Seat switch
            {
                0 => "wei",
                1 => null,
                2 => "wei",
                3 => "shu",
                _ => null
            },
            IsFactionRevealed = player.Seat is 2 or 3,
            Hand = player.Seat == 0 ? [new CardSnapshot(1000, CardKind.Slash, Suit.Spade, 7, "杀", "7")] : [],
            HandCount = player.Seat == 0 ? 1 : player.HandCount
        }).ToArray();
        return game.CreateSnapshot(0) with
        {
            Status = EngineStatus.AwaitingHumanPlay,
            Phase = TurnPhase.Play,
            CurrentSeat = 0,
            TurnNumber = 1,
            Players = players
        };
    }
    private static GameSnapshot Change(GameSnapshot view, int seat, Func<PlayerSnapshot, PlayerSnapshot> change) =>
        view with { Players = view.Players.Select(player => player.Seat == seat ? change(player) : player).ToArray() };
    private static LegalAction Action(LegalActionKind kind, int card, int? target = null) => new(kind, card, target, kind.ToString());
    private static LegalAction End() => new(LegalActionKind.EndPlay, null, null, "结束出牌");
    private sealed record PlayCase(string Name, GameSnapshot View, IReadOnlyList<LegalAction> Actions, LegalActionKind Expected);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
