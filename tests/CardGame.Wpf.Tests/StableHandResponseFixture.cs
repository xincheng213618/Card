using System.IO;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class StableHandResponseFixture
{
    private const string ModeId = "identity:stable-hand-responses";
    private const string OwnerId = "fixture:response-owner";
    private const string DriverId = "fixture:response-driver";
    private readonly record struct RegistryKey(DecisionKind Kind, bool Jijiu, bool ArrowBarrage,
        bool Zhefu, bool RecoveryChild, bool Wushuang, bool LifeOutcome);
    private static readonly Dictionary<RegistryKey, ContentRegistry> Registries = [];
    private static readonly ContentSkillDefinition Jijiu = ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage()).GetSkill("standard:jijiu");
    private static readonly Lazy<(ContentSkillDefinition Zhefu, ContentSkillDefinition Wushuang)> ResponseSkills = new(() =>
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        return (registry.GetSkill("ol:zhefu"), registry.GetSkill("classic:wushuang"));
    });

    internal static (MainViewModel Model, ContentRegistry Registry) Create(DecisionKind kind, string output, bool jijiu = false, bool arrowBarrage = false,
        bool zhefu = false, bool recoveryChild = false, bool wushuang = false, bool lifeOutcome = false)
    {
        Program.Assert(!lifeOutcome || kind == DecisionKind.RespondDodge && !arrowBarrage && !jijiu && !wushuang && !zhefu && !recoveryChild,
            "The optional native life fixture keeps the fixed physical Slash setup and its native Peach rescue.");
        Program.Assert(!wushuang || kind == DecisionKind.RespondDodge && !arrowBarrage && !jijiu,
            "The optional Wushuang fixture uses only the existing physical Slash/Dodge setup.");
        var label = lifeOutcome ? "LifeOutcome" : wushuang ? "WushuangDodge" : recoveryChild ? "RescueRecoveryChild" : zhefu ? "Zhefu" : jijiu ? "Jijiu" : arrowBarrage ? "ArrowBarrage" : kind.ToString();
        var registry = GetRegistry(new(kind, jijiu, arrowBarrage, zhefu, recoveryChild, wushuang, lifeOutcome));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = lifeOutcome ? Role.Loyalist : Role.Lord, ModeId = ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        try
        {
            Accept(game, new StartGameCommand()); Reach(game, DecisionKind.SelectGeneral);
            var selectedGeneral = lifeOutcome ? game.PendingDecision!.Choices.Select(choice => choice.Parameters.GetValueOrDefault("general-id"))
                .First(id => id is OwnerId or "fixture:response-3")! : OwnerId;
            Accept(game, new SelectGeneralCommand(0, selectedGeneral, game.Revision, game.PendingDecision!.PromptId));
            Reach(game, DecisionKind.PlayCard);
            if (lifeOutcome)
            {
                Console.WriteLine("  Native life fixed setup: " + string.Join(",", game.CreateSnapshot(0).Players.Select(player =>
                    $"seat{player.Seat}/HP{player.Hp}/alive{player.IsAlive}")) + "; human Hand=" +
                    string.Join(",", game.CreateSnapshot(0).Players[0].Hand.Select(card => $"{card.Id}:{card.Kind}")) + ".");
                for (var step = 0; step < 6 && game.CreateSnapshot(0).Players[0].Hp > 1; step++)
                {
                    Accept(game, new UseProgramSkillCommand(0, DriverId, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
                    Reach(game, DecisionKind.PlayCard);
                }
                Program.Assert(game.CreateSnapshot(0).Players[0] is { Hp: 1, IsAlive: true } &&
                    game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == 45 && card.Kind == CardKind.Peach),
                    "Recorded one-point HP costs must leave the fixed owner alive at one HP with native Peach45.");
            }
            if (jijiu || kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.FireAttackReveal)
            {
                if (kind == DecisionKind.FireAttackReveal)
                {
                    Accept(game, new UseProgramSkillCommand(0, DriverId, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
                    Reach(game, DecisionKind.PlayCard);
                }
                Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
                if (lifeOutcome) ReachLifeAttack(game);
                else Reach(game, kind);
            }
            else if (kind == DecisionKind.RescueDying)
            {
                Accept(game, new UseProgramSkillCommand(0, DriverId, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
                Reach(game, kind);
            }
            else
            {
                if (kind == DecisionKind.Nullification)
                {
                    Accept(game, new UseProgramSkillCommand(0, DriverId, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
                    Reach(game, DecisionKind.PlayCard);
                }
                var cardKind = kind == DecisionKind.FireAttackDiscard ? LegalActionKind.FireAttack : LegalActionKind.PeachGarden;
                var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == cardKind &&
                    (kind == DecisionKind.Nullification || candidate.TargetSeats.SequenceEqual([1])));
                Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind));
                Reach(game, kind);
            }
            var expectedHandKind = jijiu ? CardKind.Dodge : kind switch
            {
                DecisionKind.RespondDodge => CardKind.Dodge,
                DecisionKind.RespondSlash => CardKind.Slash,
                DecisionKind.RescueDying => CardKind.Peach,
                DecisionKind.Nullification => CardKind.Nullification,
                DecisionKind.FireAttackDiscard => CardKind.Slash,
                _ => CardKind.FireAttack
            };
            Program.Assert(lifeOutcome ? game.CreateSnapshot(0) is { CurrentSeat: 1, Phase: TurnPhase.Play, PendingDecision: null } &&
                           game.CreateSnapshot(0).Players[0] is { Hp: 1, IsAlive: true, Hand.Count: 1 } owner &&
                           owner.Hand[0] is { Id: 45, Kind: CardKind.Peach } :
                           game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind &&
                           prompt.Choices.Any(choice => choice.Cards.Count == 1 && game.CreateCardZoneDiagnostics().Any(card =>
                               card.CardId == choice.Cards[0] && card.CardKind == expectedHandKind && card.Location == CardLocation.Hand(0))),
                "The fixed fixture must reach a real human response with its declared physical Hand card.");
            if (!jijiu && kind == DecisionKind.RespondSlash)
                Program.Assert(game.PendingDecision!.IncomingCard == CardKind.BarbarianAssault &&
                    game.Events.Select(item => item.Payload).OfType<GroupCardUsedEvent>().Any(item => item.CardKind == CardKind.BarbarianAssault && item.SourceSeat == 1) &&
                    game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Kind == CardKind.Slash),
                    "A real native physical Barbarian Assault from AI1 must request the human's physical Slash.");
            if (jijiu)
            {
                var nativePeach = game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Kind == CardKind.Peach);
                Program.Assert(game.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual([nativePeach.Id]))
                    .Parameters.GetValueOrDefault("conversion-skill-id") is null,
                    "A real red Peach must retain the engine's single native rescue source alongside red Dodge conversions.");
                Program.Assert(game.CreateSnapshot(0).CurrentSeat == 1 && game.PendingDecision!.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "peach" && choice.Description.Contains("当作【桃】")),
                    "Real outside-turn dying must offer the registered Jijiu conversion.");
            }
            if (!lifeOutcome) AssertPhysicalOrigin(game, kind, arrowBarrage);
            RecordAndReplay(game, registry, output, label + ".before");
            Program.Assert(Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).Players[0].Hand.Count == 0),
                "Response setup must keep the human's undisclosed Hand private from other viewers.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
                saveStore: store, useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && (lifeOutcome || model.Hand.Any(card => card.IsPlayable)) &&
                           SnapshotJson.Serialize(Program.Engine(model).CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
                "Public registry injection and manual load must restore the exact live playable response.");
            var witnesses = new
            {
                Label = label,
                InvalidCardIds = model.Hand.Where(card => !card.IsPlayable).Select(card => card.Id).ToArray(),
                AmbiguousCardIds = model.Hand.Where(card => game.PendingDecision?.Choices.Count(choice =>
                    choice.Cards.Count == 1 && choice.Cards[0] == card.Id) > 1).Select(card => card.Id).ToArray()
            };
            File.WriteAllText(Path.Combine(output, "stable-hand-response-setup", label + ".branch-witnesses.json"),
                JsonSerializer.Serialize(witnesses, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"  Original VerifyBoundary branches {label}: invalid {witnesses.InvalidCardIds.Length}, ambiguous {witnesses.AmbiguousCardIds.Length}.");
            Console.WriteLine($"  Stable fixture {label}: seed 31, {game.AcceptedCommands.Count} accepted setup commands, actual turn seat {game.CreateSnapshot(0).CurrentSeat}.");
            return (model, registry);
        }
        catch
        {
            WriteEvidence(game, output, label + ".failure");
            throw;
        }
    }

    private static void AssertPhysicalOrigin(GameEngine game, DecisionKind kind, bool arrowBarrage)
    {
        if (kind == DecisionKind.RescueDying) return;
        var incoming = kind switch
        {
            DecisionKind.RespondDodge => arrowBarrage ? CardKind.ArrowBarrage : CardKind.Slash,
            DecisionKind.RespondSlash => CardKind.BarbarianAssault,
            DecisionKind.Nullification => CardKind.PeachGarden,
            _ => CardKind.FireAttack
        };
        var sourceSeat = kind is DecisionKind.Nullification or DecisionKind.FireAttackDiscard ? 0 : 1;
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardKind == incoming && frame.SourceSeat == sourceSeat);
        Program.Assert(game.PendingDecision!.IncomingCard == incoming &&
                       use.Action is { Type: CardActionType.Use } action && action.ActorSeat == sourceSeat &&
                       action.ProviderSeat == sourceSeat && action.PhysicalCards.Count == 1 &&
                       action.PhysicalCards[0].CardId == use.CardId &&
                       game.CreateCardZoneDiagnostics().Single(card => card.CardId == use.CardId) is
                           { Location.Zone: CardZoneKind.Processing } entity && entity.CardKind == incoming &&
                       game.CardMovements.Any(move => move.CardId == use.CardId && move.From == CardLocation.Hand(sourceSeat) &&
                           move.To == CardLocation.Processing),
            "The native response must retain its real physical incoming CardUse, printed kind, actor/provider and Hand cost origin.");
    }

    internal static void RecordAndReplay(GameEngine game, ContentRegistry registry, string output, string label)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
                       SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
                       JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
            "Four viewer snapshots and the owning typed resolution stack must cold replay exactly.");
        Program.Assert(game.CreateCardZoneDiagnostics().Select(card => card.CardId).Distinct().Count() == 80,
            "Every declared physical card remains in a real card zone.");
        WriteEvidence(game, output, label);
    }

    private static void WriteEvidence(GameEngine game, string output, string label)
    {
        var directory = Path.Combine(output, "stable-hand-response-setup"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, label + ".checkpoint.json"), GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        File.WriteAllText(Path.Combine(directory, label + ".commands.json"), CommandJson.Serialize(game.AcceptedCommands));
        File.WriteAllText(Path.Combine(directory, label + ".origin.json"), JsonSerializer.Serialize(new
        {
            CurrentSeat = game.CreateSnapshot(0).CurrentSeat, game.PendingDecision, game.ResolutionStack,
            CardZones = game.CreateCardZoneDiagnostics(), game.CardMovements,
            Events = game.Events.Select(item => new { PayloadType = item.Payload.GetType().Name, Payload = (object)item.Payload }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        for (var seat = 0; seat < 4; seat++)
            File.WriteAllText(Path.Combine(directory, label + $".viewer-{seat}.json"), SnapshotJson.Serialize(game.CreateSnapshot(seat)));
    }

    private static void ReachLifeAttack(GameEngine game)
    {
        for (var step = 0; step < 12; step++)
        {
            if (game.CreateSnapshot(0) is { CurrentSeat: 1, Phase: TurnPhase.Play, PendingDecision: null }) return;
            if (game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards } discard)
                Accept(game, new DiscardCardsCommand(0, game.CreateSnapshot(0).Players[0].Hand.Where(card => card.Id != 45)
                    .Take(discard.RequiredCardCount).Select(card => card.Id).ToArray(), discard.PromptId, game.Revision));
            else
            {
                Program.Assert(game.CreateSnapshot(0).PendingDecision is null && game.CreateSnapshot(0).Players[0].Hp == 1,
                    "The fixed native life setup passed its AI action boundary or opened an unexpected human prompt.");
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
        }
        throw new InvalidOperationException("The fixed seed31 life fixture did not reach AI1's next real Play action within 12 steps.");
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards } discard)
                Accept(game, new DiscardCardsCommand(0, game.CreateSnapshot(0).Players[0].Hand.Take(discard.RequiredCardCount)
                    .Select(card => card.Id).ToArray(), discard.PromptId, game.Revision));
            else
                Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"Fixed response {kind} was not reached: actual {game.PendingDecision?.Kind}, revision {game.Revision}.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Program.Assert(result.Accepted, result.Error?.Message ?? "The fixed fixture rejected a real command.");
    }

    private static ContentRegistry GetRegistry(RegistryKey key)
    {
        lock (Registries)
        {
            if (Registries.TryGetValue(key, out var registry)) return registry;
            registry = ContentRegistry.Build(new StandardContentPackage(), new Package(key.Kind, key.Jijiu,
                key.ArrowBarrage, key.Zhefu, key.RecoveryChild, key.Wushuang, key.LifeOutcome));
            Registries.Add(key, registry);
            return registry;
        }
    }

    private sealed class Package(DecisionKind kind, bool jijiu, bool arrowBarrage, bool zhefu, bool recoveryChild, bool wushuang, bool lifeOutcome) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-stable-hand-response", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var loss = kind == DecisionKind.FireAttackReveal ? 4 : kind == DecisionKind.RescueDying ? 5 : 1;
            var recoveryTrigger = recoveryChild ? """
                ,"triggers":[{"id":"recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]
                """ : string.Empty;
            var optionLabels = recoveryChild ? ",\"optionLabels\":{\"continue\":\"继续\"}" : string.Empty;
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"fixture:response-driver","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,
                 "minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":{{loss}}}]}]{{recoveryTrigger}}},
                {"id":"fixture:response-dying","revision":1,"triggers":[{"id":"actual-outside-turn-dying","window":"turnStartBeforeNormalFlow",
                 "subject":"owner","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:response-driver":{"name":"真实体力成本","description":"实际失去体力"RECOVERY_OPTIONS},"fixture:response-dying":{"name":"真实回合开始","description":"实际回合外濒死"}}}""".Replace("RECOVERY_OPTIONS", optionLabels, StringComparison.Ordinal));
            foreach (var id in catalog.Programs.Keys)
                builder.AddSkill(new(id, id, id) { Program = catalog.Programs[id] });
            if (jijiu) builder.AddSkill(Jijiu);
            if (zhefu) builder.AddSkill(ResponseSkills.Value.Zhefu);
            if (wushuang) builder.AddSkill(ResponseSkills.Value.Wushuang);
            builder.AddGeneral(new(OwnerId, "固定响应者", "supporter", jijiu ? "standard:jijiu" : "standard:none", "qun", 4,
                zhefu ? [DriverId, "ol:zhefu"] : [DriverId]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:response-{seat}", "固定行动者" + seat, "supporter", "standard:none", "wei", 4,
                    jijiu ? ["fixture:response-dying"] : wushuang ? ["classic:wushuang"] : lifeOutcome && seat == 3 ? [DriverId] : []));
            var pair = jijiu ? new[] { "standard:dodge", "standard:dodge" } : kind switch
            {
                DecisionKind.RespondDodge => ["standard:slash", "standard:dodge"],
                DecisionKind.RespondSlash => ["standard:slash", "standard:slash"],
                DecisionKind.RescueDying => ["standard:peach", "standard:slash"],
                DecisionKind.Nullification => ["standard:peach_garden", "standard:nullification"],
                DecisionKind.FireAttackReveal => ["standard:fire_attack", "standard:fire_attack"],
                DecisionKind.FireAttackDiscard => ["standard:slash", "standard:fire_attack"],
                _ => throw new InvalidOperationException("Unsupported fixed native response.")
            };
            // Verified Seed31 gives Human0 CardId45: keep a native red Peach beside the red Dodge conversions.
            // Verified Seed31 deals CardIds 21, 37, 61 and 80 to AI1. Give that seat native physical Nanman only.
            builder.AddDeck(new("fixture:response-deck", "固定实体响应牌库", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(
                    arrowBarrage ? new[] { 21, 37, 61, 80 }.Contains(index + 1) ? "standard:arrow_barrage" : "standard:dodge" :
                    (jijiu || lifeOutcome) && index + 1 == 45 ? "standard:peach" : kind == DecisionKind.RespondSlash ?
                    (new[] { 21, 37, 61, 80 }.Contains(index + 1) ? "standard:barbarian_assault" : "standard:slash") : pair[index % 2], Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "固定真实响应", 4, 4,
                lifeOutcome ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 } :
                    new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:response-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [OwnerId, "fixture:response-1", "fixture:response-2", "fixture:response-3"]));
        }
    }
}
