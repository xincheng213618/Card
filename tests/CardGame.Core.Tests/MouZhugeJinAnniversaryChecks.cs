using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhugeJinAnniversaryChecks
{
    private const string Hero = "mou:zhuge-jin", Driver = "fixture:mzj-anniv-damage", Quiet = "fixture:mzj-anniv-quiet",
        Ally = "fixture:mzj-anniv-ally", Enemy = "fixture:mzj-anniv-enemy", Source = "fixture:mzj-anniv-source", Deck = "fixture:mzj-anniv-deck";
    public static void PrivateGiftFiniteDamageAndDerivedTrueUseWithHoudeCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-zhuge-jin-anniversary-native", Hero, 3, 0, 0, 0, 0, [Driver],
            [new(CardKind.Slash, Suit.Club, 8)], [],
            [new(Enemy, Hp: 10) { StartingHand = [] }, new(Source, Hp: 10, SlashBonus: 1) { StartingHand = [
                new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Club, 5),
                new(CardKind.Slash, Suit.Heart, 6), new(CardKind.Slash, Suit.Diamond, 7)] }],
            [new(Ally, Hp: 10) { StartingHand = [] }], new string('f', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        PlayerSnapshot Player(int s) => game.CreateSnapshot(0).Players[s];
        MouZhugeJinReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.MouZhugeJin is not null)?.MouZhugeJin;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.MaxHp, p.HandCount }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); }
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual published Human choice is answered.");
            var c = p!.Choices.FirstOrDefault(match); Need(c is not null, "The exact native choice is published."); Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision));
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human boundary at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Zhuge Jin anniversary flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Submit(new StartGameCommand()); Play();
        Need(Player(0).GeneralId == Hero && Player(3).GeneralId == Source && Events<MouZhugeJinZijinGrantedEvent>().Length == 0, "The real roster starts with no derived Zijin grant.");
        Submit(new UseProgramSkillCommand(0, "mou:taozhou", "secret-number-and-give", [], [], game.Revision, Prompt()!.PromptId));
        Reach(() => Receipt()?.Stage == MouZhugeJinStage.Target && Prompt()?.PlayerSeat == 0); Answer(c => c.Targets.SequenceEqual([3]));
        Reach(() => Receipt()?.Stage == MouZhugeJinStage.SecretNumber && Prompt()?.PlayerSeat == 0); Answer(c => c.Parameters.GetValueOrDefault("branch") == "number-2");
        Reach(() => Receipt()?.Stage == MouZhugeJinStage.Give && Prompt()?.PlayerSeat == 3);
        Need(Events<MouZhugeJinCooldownIssuedEvent>().Length == 0 && Events<MouZhugeJinBudgetIssuedEvent>().Length == 0 && Events<MouZhugeJinGivenEvent>().Length == 0 &&
            Prompt()!.Choices.All(c => !c.Parameters.ContainsKey("number") && !c.Parameters.ContainsKey("due-round")) && !Prompt()!.Prompt.Contains("2"),
            "The giver receives only the actual private up-to-three-card decision; neither a public cooldown nor a secret-number field exists before committing.");
        var humanBefore = SnapshotJson.Serialize(game.CreateSnapshot(0)); var giverBefore = SnapshotJson.Serialize(game.CreateSnapshot(3));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(humanBefore == SnapshotJson.Serialize(game.CreateSnapshot(0)) && giverBefore == SnapshotJson.Serialize(game.CreateSnapshot(3)) && Receipt() is { Stage: MouZhugeJinStage.Give, SecretNumber: 2, Cards.Count: 0 },
            "One representative Cold retains the private secret and exact giver commitment on the owning activation frame without publishing or paying it twice.");
        Play();
        Need(Events<MouZhugeJinCooldownIssuedEvent>() is [var cooldown] && cooldown.DueRound == cooldown.Round + 2 &&
            Events<MouZhugeJinBudgetIssuedEvent>() is [{ Target: 3, Count: 2 }] && Events<MouZhugeJinZijinGrantedEvent>() is [{ Recipient: 3 }] &&
            Events<CardActionAcceptedEvent>().All(e => e.Action.ActorSeat != 3) && !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "mou:taozhou"),
            "The enemy's actual zero-card commitment creates exactly two future damage hits and native derived Zijin, with no obsolete extra Slash and with real round cooldown.");
        var hp = 10;
        foreach (var amount in new[] { 2, 2, 1 })
        {
            Submit(new UseProgramSkillCommand(0, Driver, "outgoing", [], [3], game.Revision, Prompt()!.PromptId)); Play(); hp -= amount;
            Need(Player(3).Hp == hp && Events<DamageAppliedEvent>().Last(e => e.TargetSeat == 3).Amount == amount,
                "Each accepted native damage command consumes only its actual positive hit; the third receives no exhausted bonus.");
        }
        Need(Events<MouZhugeJinBudgetHitEvent>().Length == 2, "The future-damage count expires after exactly two actual hits.");
        var slash = Player(0).Hand.Single(c => c.Kind == CardKind.Slash);
        var action = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == slash.Id && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]));
        Need(action is not null, "The real physical Slash has its canonical native action.");
        Submit(new PlayCardCommand(0, slash.Id, action!.TargetSeats, game.Revision, Prompt()!.PromptId, action.PlayedCardKind, action.TargetCardId)); Play();
        Need(Player(3).Hp == 5 && Events<MouZhugeJinStartedEvent>().Any(e => e.Operation == SkillProgramEffectOp.ResolveMouZhugeJinZijin && e.Source.OwnerSeat == 3) &&
            Events<MouZhugeJinCostPaidEvent>().Any(e => e.Executor == 3 && e.Holder == 3) &&
            Events<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.ActorSeat == 3 && e.EffectiveCardKind == CardKind.Dodge && e.WholeActualTurnUseSequence is > 0),
            "A genuine native Dodge Use prevents Slash damage and then pays derived Zijin through a real logical discard; ordinary card output is not substituted for Use.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId));
        Reach(() => Receipt() is { Operation: SkillProgramEffectOp.ResolveMouZhugeJinHoude, Stage: MouZhugeJinStage.Cost, Red: true, Target: 3 } && Prompt()?.PlayerSeat == 0);
        var paid = Prompt()!.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == "card").Cards.Single();
        Answer(c => c.Cards.SequenceEqual([paid]));
        Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        Need(Player(0).Hp == 3 && Events<ProgramCardEffectNullifiedEvent>().Count(e => e.SkillId == "mou:houde" && e.OwnerSeat == 0) == 1 &&
            Events<MouZhugeJinCostPaidEvent>().Any(e => e.Executor == 0 && e.Holder == 0 && e.CardId == paid),
            "The first real red Slash in the other actual Play pays Houde before canceling only its effect on this owner; a second red Slash reaches the normal Dodge boundary without another Houde choice.");
        var secondUse = game.ResolutionStack.OfType<CardUseFrame>().Last(f => f.Action?.ActorSeat == 3).Id;
        Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        Reach(() => Events<CardUseFinishedEvent>().Any(e => e.ResolutionId == secondUse) && game.ResolutionStack.All(f => f.Id != secondUse));
        Need(Player(0).Hp == 2 && Events<MouZhugeJinStartedEvent>().Count(e => e.Operation == SkillProgramEffectOp.ResolveMouZhugeJinHoude) == 1 &&
            Events<MouZhugeJinCostPaidEvent>().Count(e => e.Executor == 3 && e.Holder == 3) == 2 && Events<MouZhugeJinZijinGrantedEvent>().Length == 1,
            "Only the undamaging native Dodge and canceled first Slash pay Zijin; the second Slash really deals one damage, does not pay Zijin, and cannot reuse this Play's first-red-target Houde eligibility.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-zhuge-jin-anniversary", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash }) { var c = CardCatalog.Get(kind); builder.AddCard(new("fixture:mzj-anniv-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实伤害", description = "对所选角色造成原生伤害1" }, [Quiet] = new { name = "安静同行", description = "正常摸牌后跳过出牌" } } }));
            foreach (var id in new[] { Driver, Quiet }) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            foreach (var id in new[] { Ally, Enemy }) builder.AddGeneral(new(id, "真实同行", "supporter", Quiet, "wei", 10));
            builder.AddGeneral(new(Source, "真实用牌者", "supporter", "standard:none", "wei", 10));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("fixture:mzj-anniv-" + CardKind.Dodge, Suit.Club, 2)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "谋诸葛瑾十周年真实主流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Ally, Enemy, Source], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
