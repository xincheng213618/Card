using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouLuXunMobileChecks
{
    private const string Qianxun = "mou-mobile:qianxun", Lianying = "mou-mobile:lianying",
        Driver = "fixture:mlx-driver", Quiet = "fixture:mlx-quiet", Owner = "fixture:mlx-owner",
        Ally = "fixture:mlx-ally", Enemy = "fixture:mlx-enemy", Far = "fixture:mlx-far", Deck = "fixture:mlx-deck";

    public static void ActualTrickHoldReturnPrivateLossDistributionAndRecordedNativeUse()
    {
        var s = Scene.Start();
        var held = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        s.Accept(new UseProgramSkillCommand(0, Driver, "incoming-duel", [], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(s.HoldChoice);
        var first = s.E<MouLuXunNameRecordedEvent>().Single();
        s.Need(first.Kind == CardKind.Duel && first.OwnerSeat == 0 && first.Count == 1,
            "The real other-actor Duel effect records its name before offering the optional bounded HE hold.");
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "toggle" && c.Cards.SequenceEqual([held]));
        s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "confirm");
        s.Play();
        var hold = s.E<MouLuXunEntityMovedEvent>().Single(e => e.FrameId == first.FrameId);
        s.Need(hold.CardId == held && hold.From == CardLocation.Hand(0) && hold.To.Zone == CardZoneKind.PrivateTurnHold &&
            !s.V().Hand.Any(c => c.Id == held) && s.V().Hp == 7 && s.E<MouLuXunCompletedEvent>().Count(e => e.FrameId == first.FrameId) == 1,
            "Holding one real original entity returns the original Duel exactly once and does not cancel its native damage.");

        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.HoldChoice() && s.E<MouLuXunNameRecordedEvent>().Any(e => e.Kind == CardKind.ArrowBarrage));
        s.Need(s.G.CardMovements.Count(m => m.CardId == held && m.From == hold.To && m.To == CardLocation.Hand(0)) == 1,
            "The first actual turn returns the exact held original through the existing native private-hold return.");
        var second = s.E<MouLuXunNameRecordedEvent>().Single(e => e.Kind == CardKind.ArrowBarrage);
        var areaUse = s.E<CardUseDeclaredEvent>().Single(e => e.CardKind == CardKind.ArrowBarrage && e.SourceSeat == 2);
        var toHold = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "toggle" && c.Cards.SequenceEqual([toHold]));
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "confirm");
        s.Reach(() => s.P is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
        var dodge = s.V().Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Answer(c => c.Cards.SequenceEqual([dodge]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Lianying && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var otherTurn = s.G.State.TurnNumber;
        var lost = s.G.CardMovements.Count(m => m.TurnNumber == otherTurn && m.From.OwnerSeat == 0 && m.From != m.To &&
            m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment);
        s.Need(lost == 2 && s.E<MouLuXunCompletedEvent>().Count(e => e.FrameId == second.FrameId) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == areaUse.ResolutionId && e.CardKind == CardKind.ArrowBarrage && e.CardId == areaUse.CardId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == dodge && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.V(1).Hp == 7 && s.V(3).Hp == 7 && s.V().Hp == 7,
            "The real group effect records at its exact target, resumes into one paid Dodge, resolves the other two native target attacks and returns the same whole ArrowBarrage once.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(s.Distribution);
        var view = s.E<MouLuXunStartedEvent>().Last(e => e.Operation == SkillProgramEffectOp.DistributeMouLuXunLianying);
        var viewed = s.E<MouLuXunTopViewedEvent>().Where(e => e.FrameId == view.FrameId).OrderBy(e => e.Index).Select(e => e.CardId).ToArray();
        s.Need(view.Limit == 2 && viewed.Length == 2 && (s.G.CreateSnapshot(0).PrivateRevealedCards ?? []).Select(c => c.Id).Order().SequenceEqual(viewed.Order()) &&
            (s.G.CreateSnapshot(1).PrivateRevealedCards ?? []).All(c => !viewed.Contains(c.Id)),
            "Lianying privately reserves the real two top entities from this actual turn's loss count without exposing them to another viewer.");
        s.Cold();
        s.Answer(c => c.Targets.SequenceEqual([0]));
        s.Reach(s.Distribution);
        s.Answer(c => c.Targets.SequenceEqual([1]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Qianxun && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Need(s.E<MouLuXunCompletedEvent>().Count(e => e.FrameId == view.FrameId) == 1 &&
            viewed.Select((id, index) => (id, index)).All(x => s.G.CardMovements.Count(m => m.CardId == x.id && m.From == CardLocation.Processing && m.To == CardLocation.Hand(x.index)) == 1),
            "Each viewed original is given to its chosen living recipient once, then the native other-turn ending returns.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "name") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("value") == nameof(CardKind.Duel));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "use") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "use" && c.Targets.SequenceEqual([2]));
        s.Play();
        var issued = s.E<MouLuXunNativeIssuedEvent>().Single();
        s.Need(issued.Kind == CardKind.Duel && issued.OwnerSeat == 0 &&
            s.E<MouLuXunNameRemovedEvent>().Count(e => e.FrameId == issued.FrameId && e.Kind == CardKind.Duel) == 1 &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardId == 0 && e.SourceSeat == 0 && e.CardKind == CardKind.Duel) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel) == 1 &&
            s.E<MouLuXunNativeReturnedEvent>().Count(e => e.FrameId == issued.FrameId && e.UseId == issued.UseId) == 1 &&
            s.E<MouLuXunCompletedEvent>().Count(e => e.FrameId == issued.FrameId) == 1 && s.V(2).Hp == 7,
            "Removing one recorded name issues and completes one actual native zero-material Duel, including its original target damage and exact Play-start return.");
    }

    public static void RealWenheChaosParticipantRecordsAndReturnsItsPhysicalParent()
    {
        var s = Scene.Start(wenhe: true);
        var action = s.G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.WenheChaos);
        var material = action.CardId!.Value;
        s.Accept(new PlayCardCommand(0, material, action.TargetSeats, s.G.Revision, s.P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        s.Reach(() => s.G.CreateSnapshot(1).PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-xun") == true);
        var record = s.E<MouLuXunNameRecordedEvent>().Single();
        var chaos = s.E<WenheChaosIssuedEvent>().Single();
        var parent = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == material && e.CardKind == CardKind.WenheChaos);
        var effect = s.G.ResolutionStack.OfType<CardEffectBeforeApplyFrame>().Single();
        s.Need(record.OwnerSeat == 1 && record.Kind == CardKind.WenheChaos && record.ActionId == chaos.CardActionId &&
            chaos.ParentFrameId == parent.ResolutionId && chaos.CardActionId is not null &&
            effect.NativeTargetReturn is { TargetSeat: 1, WenheProgramFrameId: not null } returned && returned.WenheProgramFrameId == chaos.ProgramFrameId &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == parent.ResolutionId),
            "A real physical Wenhe Chaos records at the other actual participant's effect, preserving the original card Use and native actor-cursor parent rather than the round rule.");
        var privateChoice = SnapshotJson.Serialize(s.G.CreateSnapshot(1));
        s.Cold();
        s.Need(privateChoice == SnapshotJson.Serialize(s.G.CreateSnapshot(1)), "The actual nonhuman participant's pending effect and native Wenhe cursor restore together.");
        // Advance the native AI; never submit a command on behalf of its chooser.
        s.Play();
        s.Need(s.E<MouLuXunNameRecordedEvent>().Count(e => e.OwnerSeat == 1 && e.Kind == CardKind.WenheChaos) == 1 &&
            s.E<MouLuXunCompletedEvent>().Count(e => e.FrameId == record.FrameId) == 1 &&
            s.E<WenheCardChaosReturnedEvent>().Count(e => e.CardUseFrameId == parent.ResolutionId) == 1 &&
            s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == chaos.ProgramFrameId && e.Completed) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == parent.ResolutionId && e.CardId == material) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            s.V().Hp == 8 && Enumerable.Range(1, 3).All(seat => s.V(seat).Hp == 7),
            "After the native AI declines its optional hold, all three actual empty-Hand actors lose one HP, then the exact physical Wenhe parent and its actor program return once without repaying the card.");
    }

    private sealed class Fixture(bool wenhe) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-lu-xun-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:lu-xun-mobile");
            if (wenhe)
            {
                static string Read(string suffix)
                {
                    using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity." + suffix)!;
                    using var reader = new StreamReader(stream); return reader.ReadToEnd();
                }
                var real = SkillProgramCatalog.Load(Read("rules.json"), Read("presentation.json"));
                b.AddSkill(new("wenhe:chaos-resolution", "文和乱武原生处理", "真实官方活动卡程序")
                { Program = real.Programs["wenhe:chaos-resolution"], ProgramPresentation = real.Presentations["wenhe:chaos-resolution"] });
                b.AddCard(new("fixture:mlx-chaos", "文和乱武", "锦囊牌", "真实活动锦囊", LegacyKind: CardKind.WenheChaos));
            }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"incoming-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实决斗", description = "另一使用者的原生决斗" }, [Quiet] = new { name = "静默", description = "跳过无关出牌" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "原生主链") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "谋陆逊主链", "", "standard:none", "wu", 8, wenhe ? [Driver, "wenhe:chaos-resolution"] : [Qianxun, Lianying, Driver]));
            b.AddGeneral(new(Ally, "真实队友", "", "standard:none", "wu", 8, wenhe ? [Quiet, Qianxun] : [Quiet]));
            b.AddGeneral(new(Enemy, "真实敌方", "", "standard:none", "qun", 8, []));
            b.AddGeneral(new(Far, "真实静默敌方", "", "standard:none", "qun", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定真实小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋陆逊原生团队小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool HoldChoice() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-xun" && c.Parameters.GetValueOrDefault("verb") == "confirm") == true;
        public bool Distribution() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-lu-xun" && c.Parameters.GetValueOrDefault("verb") == "give") == true;
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> condition)
        {
            for (var i = 0; i < 180; i++)
            {
                if (condition()) return;
                Need(G.State.Winner == Winner.None, "Unexpected native terminal cutoff.");
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected native Lu Xun boundary: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Missing bounded native Lu Xun boundary: " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0));
            var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements),
                "The new real held or privately viewed pending selection restores without a second physical payment or view.");
        }
        public void Need(bool ok, string text)
        {
            if (!ok) throw new InvalidOperationException(text + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, P,
                Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.HandCount }),
                Started = E<MouLuXunStartedEvent>(), Recorded = E<MouLuXunNameRecordedEvent>(), Moved = E<MouLuXunEntityMovedEvent>() }));
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Message); }
        public static Scene Start(bool wenhe = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(wenhe));
            static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);
            var setup = new ShanheBattleSetup(83, 1, "mlx-main", Owner, 8, 0, 0, 0, 0, [],
                wenhe ? [C(CardKind.WenheChaos)] : [C(CardKind.Slash), C(CardKind.Dodge), C(CardKind.Peach), C(CardKind.Nullification)], [],
                [new(Enemy) { StartingHand = wenhe ? [] : [C(CardKind.ArrowBarrage)] }, new(Far) { StartingHand = [] }],
                [new(Ally) { StartingHand = [] }], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
                HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
