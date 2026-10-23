using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheStartingResourceDeclarationChecks
{
    private const string Player = "classic:yu-ji", Guhuo = "classic:guhuo";
    private const string Requester = "fixture:shanhe-declaration-requester", Target = "fixture:shanhe-declaration-target";
    private const string Request = "fixture:shanhe-declaration-request", Watch = "fixture:shanhe-declaration-use-child";

    public static void SuppliedGuhuoTracksItsNewUseOwnerAndFailedDeclarationStaysDiscardedCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var truthful in new[] { true, false })
        {
            var setup = new ShanheBattleSetup(83, 1, "supplied-resource-declaration", Player, 3, 0, 0, 0, 0, [Watch],
                [new(truthful ? CardKind.Slash : CardKind.Dodge, Suit.Heart, 7)], [],
                [new(Target)], [new(Requester)], new string('c', 64)) { StartingResourcesExpireOnUse = true };
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 3, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var material = game.CreateSnapshot(0).Players[0].Hand.Single().Id;
            Accept(game, new StartGameCommand());
            Need(Events<ShanheStartingResourceCreatedEvent>(game) is [{ CardId: var created, EquipmentResource: false }] && created == material,
                "The declaration material must be the actual generated player starting resource, before any transfer or prior use.");
            Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
            Until(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
            var supply = game.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual([material]) &&
                choice.Parameters.GetValueOrDefault("conversion-skill-id") == Guhuo &&
                choice.Parameters.GetValueOrDefault("conversion-binding-id") == "declare-slash");
            Answer(game, supply);
            Until(game, () => Pending(game)?.Choices.Any(choice => choice.Parameters.ContainsKey("declaration-answer")) == true);
            var declaration = game.ResolutionStack.OfType<CardDeclarationFrame>().Single();
            var parentId = declaration.Return.ParentFrameId;
            Need(declaration.OwnerSeat == 0 && declaration.Return is { Purpose: CardDeclarationPurpose.ProvidedSlash, ActorSeat: 1 } &&
                declaration.Payment.Cost.CardId == material && declaration.Payment.Cost.From == CardLocation.Hand(0) &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == parentId && frame.OwnerSeat == 1 && frame.SkillId == Request),
                "The genuine human Guhuo supplier prepays its one resource for the different allied requester's existing program parent.");
            game = Cold(game, registry);
            Answer(game, Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("declaration-answer") == "challenge"));
            if (truthful)
            {
                Until(game, () => game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: Watch });
                var use = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Action?.PhysicalCards.Any(materialCost => materialCost.CardId == material) == true);
                var parent = game.ResolutionStack.Single(frame => frame.Id == parentId);
                Need(use.Id != parentId && parent.AcceptedDeclarationPayment is { Claimed: true, OwnerFrameId: var paidOwner } && paidOwner == parentId &&
                    use.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 0, PhysicalCards: [var physicalCost] } action &&
                    physicalCost.CardId == material && physicalCost.From == CardLocation.Hand(0) && action.ConversionChain.Any(source => source.SkillId == Guhuo && source.OwnerSeat == 0) &&
                    Events<ShanheStartingResourceUseEnteredEvent>(game) is [{ CardId: var entered, UseFrameId: var issuedUse, ActionId: var issuedAction }] &&
                    entered == material && issuedUse == use.Id && issuedAction == action.ActionId,
                    "A successful supplied declaration keeps payment on its requester parent but records resource lifetime on the separate real Use and action.");
                var useId = use.Id;
                game = Cold(game, registry);
                Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"));
                Until(game, () => Events<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == useId));
                Need(game.CreateCardZoneDiagnostics().Single(card => card.CardId == material).Location == CardLocation.OutsideGame &&
                    Events<ShanheStartingResourceDestroyedEvent>(game) is [{ CardId: var destroyed, ParentFrameId: var destructionUse }] &&
                    destroyed == material && destructionUse == useId &&
                    game.CardMovements.Count(move => move.CardId == material && move.From == CardLocation.Processing && move.To == CardLocation.OutsideGame) == 1 &&
                    !game.CardMovements.Any(move => move.CardId == material && move.To == CardLocation.DiscardPile) &&
                    Events<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1,
                    "The successful native supplied Use destroys its exact prepaid starting entity once at ordinary use completion, without a discard-pile detour.");
            }
            else
            {
                Until(game, () => Events<ProgramBindingResolvedEvent>(game).Any(e => e.FrameId == parentId && e.Completed));
                Need(Events<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == parentId && e.OwnerSeat == 1 && e.SkillId == Request &&
                        e.BindingId == "real-ally-request" && e.Activated && e.Completed) == 1 &&
                    !game.ResolutionStack.Any(frame => frame.Id == parentId || frame.Id == declaration.Id) &&
                    Events<CardDeclarationRevealedEvent>(game) is [{ Succeeded: false }] &&
                    game.CreateCardZoneDiagnostics().Single(card => card.CardId == material).Location == CardLocation.DiscardPile &&
                    game.CardMovements.Count(move => move.CardId == material && move.From == CardLocation.Processing &&
                        move.To == CardLocation.DiscardPile && move.Reason.Value == "conversion.declaration.failed") == 1 &&
                    Events<ShanheStartingResourceUseEnteredEvent>(game).Length == 0 && Events<ShanheStartingResourceDestroyedEvent>(game).Length == 0 &&
                    !Events<CardUseDeclaredEvent>(game).Any(e => e.CardId == material),
                    "A genuinely disproved declaration returns through its original request program once without a native Use, so its paid starting resource follows ordinary failure discard rather than expiring.");
            }
            Need(game.CardMovements.Count(move => move.CardId == material && move.From == CardLocation.Hand(0) &&
                move.To == CardLocation.Processing && move.Reason.Value == "conversion.declaration.pay") == 1,
                "Both successful and failed declarations pay the actual supplier's original hand entity only once.");
            _ = Cold(game, registry);
        }
    }

    private static T[] Events<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "The native declaration fixture command was rejected."); }
    // Trusted-host test driver: NPC declaration choices remain private to their own seat views.
    private static PendingDecision? Pending(GameEngine game) => Enumerable.Range(0, game.PlayerCount)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(pending => pending is not null);
    private static void Answer(GameEngine game, PromptChoice choice)
    { var pending = Pending(game)!; Accept(game, new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId, choice.Id, game.Revision)); }
    private static void Until(GameEngine game, Func<bool> arrived)
    {
        for (var step = 0; step < 100; step++)
        {
            if (arrived()) return;
            Need(game.PendingDecision?.PlayerSeat != 0, "An unexpected human choice interrupted the small declaration witness: " + JsonSerializer.Serialize(game.PendingDecision));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The small declaration witness did not reach its native boundary.");
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(checkpoint), registry);
        Need(GameCheckpointJson.Serialize(restored.CreateCheckpoint()) == checkpoint && game.CardMovements.SequenceEqual(restored.CardMovements) &&
            Enumerable.Range(0, 3).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
            "Cold replay retains the actual declaration payment parent, separate native Use, physical resource movements and all three player views.");
        return restored;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-resource-declaration", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Player);
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Request}}","revision":1,"triggers":[{"id":"real-ally-request","window":"turnStartBeforeNormalFlow","subject":"owner",
                    "usageScope":"game","usageLimit":1,"optional":false,"effects":[
                      {"op":"selectTarget","target":"owner","targetKind":"publicTeamEnemies"},
                      {"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"qun","requiredKind":"slash"}]}]},
                  {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-supplied-use","window":"cardUseBeforeTargetEffects","ownerRelation":"observer",
                    "cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"use-child","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Request, Watch }.ToDictionary(id => id, id => new { name = id, description = "真实供杀声明与资源生命周期",
                    optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() })
            }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "原生声明小夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Requester, "请求者", "", Request, "shu", 12));
            builder.AddGeneral(new(Target, "目标", "", "standard:none", "wei", 12));
            builder.AddDeck(new("fixture:shanhe-resource-declaration-deck", "固定空起手闪牌堆", 0, 0, [new("standard:dodge", 40)]));
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实初始资源声明", 3, 3, new Dictionary<string, int>(),
                "fixture:shanhe-resource-declaration-deck", GeneralPoolIds: [Player, Requester, Target], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
