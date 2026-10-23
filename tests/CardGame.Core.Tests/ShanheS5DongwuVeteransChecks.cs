using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheS5DongwuVeteransChecks
{
    private const string Skill = StandardShanheS5DongwuVeteransPackage.ChunlaoId;
    private const string Owner = "fixture:s5-chunlao-owner", Peer = "fixture:s5-chunlao-peer";
    private const string Driver = "fixture:s5-chunlao-driver", Child = "fixture:s5-chunlao-child";
    private const string Mode = "fixture:s5-chunlao-team", Deck = "fixture:s5-chunlao-deck";
    private const string OwnerTeam = "a-owner", OtherTeam = "z-other";

    public static void NineCardCapRechecksEachDiscardAndRefillsAfterNativeWineCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new()
        {
            Seed = 23, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = OwnerTeam, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, Prompt(g)!.PromptId));
        Play(g);
        var first = View(g).Hand.Take(10).Select(card => card.Id).ToArray();
        Need(first.Length == 10, "The small native deck deals enough real Slash entities for one ten-card discard batch.");
        Discard(g, first);
        Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Child);
        var originalBatch = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>()
            .Single(frame => frame.Batch.Movements.Count == 10);
        Need(View(g).ChunlaoCount == 9 && Events<ProgramAdjacentDiscardStoredEvent>(g).Count() == 9 &&
            originalBatch.Batch.Movements.Select(move => move.CardId).ToHashSet().SetEquals(first) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Driver && frame.ActivationId == "discard" &&
                frame.InstructionIndex == 2 && frame.PendingMovementContinuation is { SubjectSeat: 0 } &&
                originalBatch.Batch.ParentFrameId == frame.Id && frame.CardSetBindings.Any(binding =>
                    binding.Name == "discarded" && binding.CardIds.SequenceEqual(first))) &&
            first.Count(id => Zone(g, id) == CardLocation.DiscardPile) == 1,
            "One genuinely paid ten-card batch stores only nine: each queued per-card candidate rechecks the current pile before the original movement child returns. " +
            BatchEvidence(g, first));
        g = Cold(g, registry);
        Continue(g); Play(g);
        Need(View(g).ChunlaoCount == 9 && Events<ProgramAdjacentDiscardStoredEvent>(g).Count() == 9,
            "Restoring the original paid discard child cannot store the tenth entity or repeat an earlier storage.");

        Need(View(g).Hp == 4, "The team fixture reaches dying through a real four-HP loss without identity-mode HP bonuses.");
        Accept(g, new UseProgramSkillCommand(0, Driver, "wound", [], [], g.Revision, Prompt(g)!.PromptId));
        Reach(g, () => Prompt(g)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            choice.Parameters.GetValueOrDefault("skill-id") == Skill) == true);
        Answer(g, choice => choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            choice.Parameters.GetValueOrDefault("skill-id") == Skill);
        Reach(g, () => Prompt(g)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "round-pile-alcohol") == true);
        var wineMaterial = Prompt(g)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("branch") == "material").Cards.Single();
        Answer(g, choice => choice.Cards.SequenceEqual([wineMaterial]));
        Answer(g, choice => choice.Parameters.GetValueOrDefault("branch") == "use");
        Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Child);
        var wine = Events<ProgramRoundPileAlcoholIssuedEvent>(g).Single();
        Need(wine is { Price: 1, VictimSeat: 0 } && wine.Source.SkillId == Skill && View(g).ChunlaoCount == 8 && View(g).Hp == 0 &&
            Zone(g, wineMaterial) == CardLocation.Processing &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == wine.CardUseFrameId &&
                frame.RoundPileAlcoholReturn is not null && !frame.RoundPileAlcoholCostDrained),
            "The S5 skill pays one real Chun into the native Alcohol use and awaits its actual movement child before recovery.");
        g = Cold(g, registry);
        Continue(g); Play(g);
        Need(View(g).Hp == 1 && View(g).ChunlaoCount == 8 && Zone(g, wineMaterial) == CardLocation.DiscardPile &&
            Events<ProgramRoundPileAlcoholIssuedEvent>(g).Count() == 1 &&
            Events<CardUseFinishedEvent>(g).Count(fact => fact.ResolutionId == wine.CardUseFrameId && fact.CardKind == CardKind.Alcohol) == 1 &&
            Events<RecoveryAppliedEvent>(g).Count(fact => fact.TargetSeat == 0 && fact.Amount == 1) == 1,
            "Paid-child cold recovery issues and completes one native rescue; using its material never makes it a fresh discarded Slash.");

        var second = View(g).Hand.Select(card => card.Id).ToArray();
        Need(second.Length == 2, "The first batch leaves the two original hand entities for a real refill attempt.");
        Discard(g, second);
        Reach(g, () => Prompt(g)?.SkillPrompt?.SkillId == Child);
        Need(View(g).ChunlaoCount == 9 && Events<ProgramAdjacentDiscardStoredEvent>(g).Count() == 10 &&
            second.Count(id => Zone(g, id) == CardLocation.Chunlao(0)) == 1 &&
            second.Count(id => Zone(g, id) == CardLocation.DiscardPile) == 1,
            "After a real wine payment frees one place, a two-card discard refills exactly one and leaves the other in the discard pile.");
        Continue(g); Play(g);
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot View(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location;
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, g.PlayerCount)
        .Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static IEnumerable<T> Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(item => item.Payload).OfType<T>();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected actual S5 Chunlao command.");
    }
    private static void Discard(GameEngine g, int[] ids) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, "discard", ids, [], g.Revision, Prompt(g)!.PromptId));
    private static void Continue(GameEngine g) => Answer(g, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(g)!;
        Accept(g, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, g.Revision));
    }
    private static void Play(GameEngine g) => Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static string BatchEvidence(GameEngine g, int[] ids) => JsonSerializer.Serialize(new
    {
        PileCount = View(g).ChunlaoCount,
        Stored = Events<ProgramAdjacentDiscardStoredEvent>(g).TakeLast(10),
        Origins = Events<ProgramAdjacentDiscardOriginEvent>(g).TakeLast(10),
        CardZones = ids.Take(10).Select(id => new { CardId = id, Zone = Zone(g, id) }),
        Batches = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().TakeLast(3).Select(frame => new
        {
            frame.Id, frame.CandidateIndex, frame.ResumeProgramFrameId,
            frame.Batch.ParentFrameId, frame.Batch.AwaitingProgramFrameId,
            Cards = frame.Batch.Movements.Take(10).Select(move => new { move.CardId, move.From, move.To, move.Reason }),
            Candidates = frame.Candidates.Take(12).Select(candidate => new
                { candidate.SkillId, candidate.BindingId, candidate.OccurrenceIndex, candidate.Priority })
        }),
        Programs = g.ResolutionStack.OfType<ProgramSkillFrame>().TakeLast(4).Select(frame => new
        {
            frame.Id, frame.SkillId, frame.ActivationId, frame.InstructionIndex, frame.PendingMovementContinuation, frame.AdjacentDiscardStorage,
            Selected = frame.SelectedCardIds.Take(10),
            Bindings = frame.CardSetBindings.Take(3).Select(binding => new { binding.Name, Cards = binding.CardIds.Take(10) })
        }),
        Boundary = FailureState(g)
    });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 120; step++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "The small S5 Chunlao fixture ended before the expected native boundary. " +
                (g.State.Winner == Winner.None ? string.Empty : FailureState(g)));
            if (Prompt(g)?.SkillPrompt?.SkillId == Child) Continue(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("S5 Chunlao missed its native boundary: " + FailureState(g));
    }
    private static string FailureState(GameEngine g)
    {
        var prompt = Prompt(g);
        return JsonSerializer.Serialize(new
        {
            g.State.Winner, g.Revision,
            Players = g.CreateSnapshot(0).Players.Select(player => new
                { player.Seat, player.GeneralId, player.TeamId, player.IsHuman, player.Hp, player.IsAlive, player.HandCount, player.ChunlaoCount }),
            Prompt = prompt is null ? null : new
                { prompt.PlayerSeat, prompt.Kind, prompt.PromptId, Skill = prompt.SkillPrompt?.SkillId, Choices = prompt.Choices.Take(3).Select(choice => choice.Id) },
            Frames = g.ResolutionStack.TakeLast(4).Select(frame => new { Kind = frame.GetType().Name, frame.Id, frame.Step }),
            RecentEvents = g.Events.Select(item => item.Payload)
                .Where(item => item is TurnStartedEvent or TurnEndedEvent or PhaseChangedEvent or PlayerDiedEvent or GameEndedEvent)
                .TakeLast(8).Select(item => JsonSerializer.Serialize(item, item.GetType())),
            Discards = g.CardMovements.Where(move => move.To == CardLocation.DiscardPile).TakeLast(4)
        });
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, g.PlayerCount).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Events = g.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
        g.CardMovements, Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint())
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "The real capped pile, paid parent/child, Alcohol use and movement receipts cold-restore identically.");
        return restored;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("s5-chunlao-nine-card-native-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            string Embedded(string suffix)
            {
                using var stream = typeof(StandardShanheS5DongwuVeteransPackage).Assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms.shanhe-s5-dongwu-veterans." + suffix)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            void Add(SkillProgramCatalog catalog)
            {
                foreach (var (id, program) in catalog.Programs)
                    builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    {
                        Program = program, ProgramPresentation = catalog.Presentations[id],
                        SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -10000d)
                    });
            }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            Add(SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"discard","minCards":1,"maxCards":10,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                     "effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"discarded"},
                       {"op":"moveBoundCards","target":"owner","sourceBind":"discarded","destination":"discardPile","awaitMovementTriggers":true}]},
                    {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                     "effects":[{"op":"loseHp","target":"owner","amount":4}]}]},
                  {"id":"{{Child}}","revision":1,"triggers":[
                    {"id":"after-paid-hand-batch","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","sourceZones":["hand"],"movementOccurrence":"perCard",
                     "priority":-100,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hand-return","options":[{"id":"continue"}]}]},
                    {"id":"after-paid-wine-material","window":"cardsMoved","subject":"owner","sourceZones":["chunlao"],"movementOccurrence":"perBatch",
                     "movementReasons":["skill-program.{{Skill}}.round-pile-alcohol.pay"],"optional":false,
                     "effects":[{"op":"chooseOption","target":"owner","resultBind":"wine-return","options":[{"id":"continue"}]}]}]}
                ]}
                """, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实弃牌与濒死", description = "实际批次弃牌，随后真实失去四点体力" },
                    [Child] = new { name = "实际移动子步骤", description = "在原生移动完成前等待", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            })));
            builder.AddSkill(new("fixture:s5-chunlao-quiet", "普通对手", "无额外能力")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 10000d) });
            builder.AddGeneral(new(Owner, "九醇上限拥有者", "supporter", Skill, "wu", 4, [Driver, Child]));
            var peers = Enumerable.Range(1, 3).Select(index => Peer + "-" + index).ToArray();
            foreach (var peer in peers)
                builder.AddGeneral(new(peer, "普通对手", "supporter", "fixture:s5-chunlao-quiet", "wei", 4));
            // The ordinary team adapter removes one card from the first
            // team's starting seat: thirteen therefore deals twelve here.
            builder.AddDeck(new(Deck, "单批真实杀", 13, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "S5醇醪实际四人原生流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [OwnerTeam] = 1, [OtherTeam] = 3 }));
        }
    }
}
