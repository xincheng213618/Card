using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryXuJingChecks
{
    private const string Hero = "anniversary:xu-jing", Caixia = "anniversary:caixia", Peer = "fixture:anniversary-xujing-peer",
        NoDraw = "fixture:anniversary-xujing-no-draw", Quiet = "fixture:anniversary-xujing-quiet", GainHold = "fixture:anniversary-xujing-gain",
        DrawHold = "fixture:anniversary-xujing-draw", Deck = "fixture:anniversary-xujing-deck";

    public static void MarkedEntityCirculatesAndActualUsesRepayCaixiaResumeCold()
    {
        var s = Scene.Start(); s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold);
        var opening = s.Root!; var card = s.E<AnniversaryXuJingMarkedEvent>().Single().CardId;
        s.Need(opening.AnniversaryXuJing is { Mode: "opening", Stage: AnniversaryXuJingStage.MovementChildren } && s.V(0).Hand!.Any(c => c.Id == card) &&
            s.G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.anniversary.shangyu.entity") == 1,
            "The opening obtains and marks one existing Slash before its real gain child and recipient choice.");
        s.Cold(); s.Skip(); s.GiftMenu(); s.Gift(0); s.Play();
        s.Need(s.E<AnniversaryXuJingMarkedEvent>().Length == 1 && s.E<AnniversaryXuJingGiftIssuedEvent>() is [{ CardId: var marked, TargetSeat: 0 }] && marked == card,
            "Cold never obtains a replacement entity; the mandatory opening designation may choose Xu Jing himself.");

        s.Slash(card); s.CaixiaMenu();
        s.Need(s.P!.Choices.Select(c => c.Parameters.GetValueOrDefault("count")).SequenceEqual(["1", "2", "3", "4"]), "Caixia uses the original four-player game count, rather than damage points or a fixed five.");
        s.Count(3); s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawHold);
        var debtor = s.Root!; var debt = s.E<AnniversaryXuJingCaixiaIssuedEvent>().Single();
        s.Need(debtor.Id == debt.FrameId && debt.Count == 3 && debtor.AnniversaryXuJing is { Mode: "caixia", Stage: AnniversaryXuJingStage.Drawing, Count: 3 } &&
            s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == debt.FrameId && f.Context.RequestedCount == 3),
            "Caixia freezes its three-use debt on the original frame before a real native draw can suspend.");
        s.Cold(); s.Skip(); s.GiftMenu();
        var damageRewards = s.E<AnniversaryXuJingStartedEvent>().Where(e => e.Mode == "damage").ToArray();
        s.Need(damageRewards is [var first] && first.CardId == card && first.UserSeat == 0 &&
            s.E<AnniversaryXuJingDrawReturnedEvent>().Where(e => e.FrameId == first.FrameId).Select(e => (e.Ordinal, e.RecipientSeat, e.RequestedCount, e.ActualCount)).SequenceEqual([(1, 0, 1, 1), (2, 0, 1, 1)]) &&
            s.E<AnniversaryXuJingDrawReturnedEvent>().Count(e => e.FrameId == debt.FrameId && e.RequestedCount == 3 && e.ActualCount == 3) == 1 &&
            s.E<AnniversaryXuJingCaixiaUseEvent>().Length == 0,
            "The marked Slash causes two separate one-card draws when user and Xu Jing coincide; neither those draws nor the already-declared Slash repay newly issued debt.");
        s.Need(s.P!.Choices.Any(c => c.Targets.SequenceEqual([0])) && s.G.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
            "The same native Slash first enters the actual discard pile; the opening's previous-turn designation does not consume this turn's recipient.");
        s.Gift(0); s.Play(); s.Slash(card); s.GiftMenu();
        s.Need(!s.P!.Choices.Any(c => c.Targets.SequenceEqual([0])) && s.P.Choices.Any(c => c.Targets.SequenceEqual([1])) &&
            s.E<AnniversaryXuJingCaixiaUseEvent>().Count(e => e.DebtFrameId == debt.FrameId) == 1 && s.E<AnniversaryXuJingCaixiaIssuedEvent>().Length == 1,
            "Reusing the same entity is an actual card use, but one use cannot repay three; this turn's already-designated receiver is excluded.");
        s.Gift(1); s.Play();
        s.Need(s.G.CreateSnapshot(1).Players[1].Hand!.Any(c => c.Id == card) &&
            s.G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(0)) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(1)) == 1 &&
            s.E<AnniversaryXuJingStartedEvent>().Count(e => e.Mode == "damage") == 2,
            "Both whole native uses recycle the very same marked physical entity, with one exact transfer per accepted recipient.");
        var unmarked = s.V(0).Hand!.Where(c => c.Kind == CardKind.Slash && c.Id != card).Select(c => c.Id).Take(2).ToArray();
        s.Need(unmarked.Length == 2, "The native draws provided real distinct repayment materials.");
        s.Slash(unmarked[0]); s.Play();
        s.Need(s.E<AnniversaryXuJingCaixiaUseEvent>().Count(e => e.DebtFrameId == debt.FrameId) == 2 && s.E<AnniversaryXuJingCaixiaIssuedEvent>().Length == 1,
            "A second actual use leaves Caixia disabled and an unmarked Slash does not receive Shangyu rewards.");
        s.Slash(unmarked[1]); s.CaixiaMenu();
        s.Need(s.E<AnniversaryXuJingCaixiaUseEvent>().Count(e => e.DebtFrameId == debt.FrameId) == 3 && s.E<AnniversaryXuJingStartedEvent>().Count(e => e.Mode == "damage") == 2,
            "The third real declaration repays the original debt before that card's actual damage, enabling Caixia again without treating every Slash as marked.");
        s.Count(1); s.Play();
        s.Need(s.E<AnniversaryXuJingCaixiaIssuedEvent>().Select(e => e.Count).SequenceEqual([3, 1]) &&
            s.E<AnniversaryXuJingDrawReturnedEvent>().Where(e => e.RequestedCount == 3).Count() == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardKind == CardKind.Slash) == 4 && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 4,
            "Both cold returns finish once; four true Slash uses complete, and the new one-use Caixia debt is independent of the repaid debt.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-xujing", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "安静的真实目标", "", Quiet, "wei", 20));
            builder.AddDeck(new(Deck, "固定实体杀", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "周年许靖真实实体与用牌", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { NoDraw, Quiet, GainHold, DrawHold }.ToDictionary(id => id, id => new { name = id, description = "真实实体及原生摸牌暂停" }) });
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","isSkillInvocation":false,"window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.anniversary.shangyu.entity"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
          {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryXuJing is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void GiftMenu() => Reach(() => Root?.AnniversaryXuJing?.Stage == AnniversaryXuJingStage.GiftChoice && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("item") == "gift") == true);
        public void Gift(int seat) => Answer(c => c.Targets.SequenceEqual([seat]) && c.Parameters.GetValueOrDefault("item") == "gift");
        public void CaixiaMenu()
        {
            Reach(() => P?.SkillPrompt?.SkillId == Caixia && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => Root?.AnniversaryXuJing?.Stage == AnniversaryXuJingStage.CountChoice && P?.Choices.Any(c => c.Parameters.ContainsKey("count")) == true);
        }
        public void Count(int count) => Answer(c => c.Parameters.GetValueOrDefault("count") == count.ToString());
        public void Slash(int card)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == card && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Need(action is not null, "The original real Slash and exact adjacent target must be a legal current action.");
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        }
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing the exact current Xu Jing response."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 100; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId is GainHold or DrawHold or Caixia && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Xu Jing boundary. " + Dump());
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Xu Jing missed a bounded exact entity/draw/use continuation.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold preserves the exact entity, accepted recipient/debt, original movement parent and native draw invoice.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Xu Jing command."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryXuJing,
            Started = E<AnniversaryXuJingStartedEvent>(), Gifts = E<AnniversaryXuJingGiftIssuedEvent>(), Draws = E<AnniversaryXuJingDrawReturnedEvent>(), Debts = E<AnniversaryXuJingCaixiaIssuedEvent>(), Uses = E<AnniversaryXuJingCaixiaUseEvent>() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(47, 1, "anniversary-xujing", Hero, 3, 0, 0, 0, 0, [NoDraw, GainHold, DrawHold], [], [new(CardKind.Crossbow, Suit.Club, 1)],
                [new(Peer, Hp: 20, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Peer, Hp: 20, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, Hp: 20, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
