using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaSuChecks
{
    private const string General = "classic:ma-su";
    private const string Sanyao = "classic:sanyao";
    private const string Ziman = "classic:ziman";
    private const string SanyaoMode = "identity:classic-ma-su-sanyao-check-5";
    private const string ZimanMode = "identity:classic-ma-su-ziman-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "shu" } general &&
                general.SkillIds.SequenceEqual([Sanyao, Ziman]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Ma Su must be in the current Shu roster with three HP.");

        var sanyao = current.Skills[Sanyao].Program!;
        var spread = sanyao.Activations.Single();
        Require(spread.Id == "spread-rumor" &&
                spread.MinCards == 1 && spread.MaxCards == 1 &&
                spread.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                spread.TargetKind == SkillProgramTargetKind.LivingMaxHp &&
                spread.MinTargets == 1 && spread.MaxTargets == 1 &&
                spread.UsesPerTurn == 1 &&
                spread.Condition.Kind == SkillProgramConditionKind.OwnTurn,
            "Sanyao must be a once-per-turn play activation discarding one card at a maximum-HP character.");
        Require(spread.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.DiscardSelected,
                SkillProgramEffectOp.Damage]),
            "Sanyao must discard the selected cost card and then deal one damage.");
        Require(spread.Effects[0] is
            {
                Target: SkillProgramEffectTarget.Owner, Amount: 1,
                Condition.Kind: SkillProgramConditionKind.Always
            },
            "The Sanyao cost must discard the owner's selected card unconditionally.");
        Require(spread.Effects[1] is
            {
                Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1,
                Condition.Kind: SkillProgramConditionKind.Always
            },
            "The Sanyao damage must hit the selected maximum-HP target for one.");

        var ziman = current.Skills[Ziman].Program!;
        var prevent = ziman.Triggers.Single(item => item.Id == "prevent-and-take");
        Require(prevent.Window == SkillProgramTriggerWindow.BeforeDamageApplied &&
                prevent.Subject == SkillProgramTriggerSubject.Owner &&
                prevent.Optional &&
                prevent.Condition.Kind == SkillProgramTriggerConditionKind.DamageSourceIsOwner,
            "Ziman must watch every before-damage window whose source is the owner.");
        Require(prevent.Effects.Count == 2 &&
                prevent.Effects[0] is { Op: SkillProgramEffectOp.PreventCurrentDamage, Target: SkillProgramEffectTarget.Owner },
            "Ziman must first prevent the pending damage.");
        var take = prevent.Effects[1];
        Require(take.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard &&
                take.ChooserRef is { Kind: ProgramParticipantRef.Owner } &&
                take.CardOwnerRef is { Kind: ProgramParticipantRef.EventTarget } &&
                take.Zones.SequenceEqual([CardZoneKind.Equipment, CardZoneKind.Judgment]) &&
                take.Destination == SkillProgramCardDestination.OwnerHand &&
                take.SkipIfNoCards,
            "Ziman must then let the owner take one equipment or judgment card from the damage target.");

        const string spreadTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:spread","revision":1,
            "minimumRulesVersion": 194,
            "activations":[{"id":"spread-rumor","minCards":1,"maxCards":1,
            "sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,
            "targetKind":"livingMaxHp","usesPerTurn":1,"condition":{"kind":"ownTurn"},
            "effects":[
            {"op":"discardSelected","target":"owner","amount":1},
            {"op":"damage","target":"selectedTarget","amount":1}]}],
            "triggers":[]}]}
            """;
        const string spreadPresentation = """
            {"schemaVersion":3,"skills":{"fixture:spread":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(spreadTemplate, spreadPresentation)
                .Programs["fixture:spread"].Activations.Single().Effects.Count == 2,
            "The Sanyao activation composition must load through the shared program parser.");
        Reject(spreadTemplate.Replace(
                "\"sourceZones\":[\"hand\",\"equipment\"]",
                "\"sourceZones\":[\"hand\",\"judgment\"]"),
            spreadPresentation, "the activation cost must read owner-scoped selectable zones only");
        Reject(spreadTemplate.Replace(
                "\"usesPerTurn\":1,", ""),
            spreadPresentation, "a play activation must declare its per-turn usage limit");
        Reject(spreadTemplate.Replace(
                "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}",
                "{\"op\":\"discardSelected\",\"target\":\"selectedTarget\",\"amount\":1}"),
            spreadPresentation, "the discard step must act through its owner");
        Reject(spreadTemplate.Replace(
                "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}",
                "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1,\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"answer\",\"optionId\":\"declined\"}}"),
            spreadPresentation, "the discard cost must stay unconditional");

        const string zimanTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:ziman","revision":1,
            "minimumRulesVersion": 194,
            "activations":[],
            "triggers":[
            {"id":"prevent-and-take","window":"beforeDamageApplied","subject":"owner",
            "optional":true,"priority":0,
            "condition":{"kind":"damageSourceIsOwner"},
            "effects":[
            {"op":"preventCurrentDamage","target":"owner"},
            {"op":"selectAndMoveOwnedCard","target":"owner",
            "chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"eventTarget"},
            "zones":["equipment","judgment"],"count":1,
            "destination":"ownerHand","skipIfNoCards":true}]}]}]}
            """;
        const string zimanPresentation = """
            {"schemaVersion":3,"skills":{"fixture:ziman":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(zimanTemplate, zimanPresentation)
                .Programs["fixture:ziman"].Triggers.Count == 1,
            "The Ziman trigger composition must load through the shared program parser.");
        Reject(zimanTemplate.Replace(
                "\"window\":\"beforeDamageApplied\"",
                "\"window\":\"turnEnding\""),
            zimanPresentation, "the damage-source-is-owner fact requires a damage or before-damage window");
        Reject(zimanTemplate.Replace(
                "\"destination\":\"ownerHand\",",
                "\"destination\":\"ownerHand\",\"targetRef\":{\"kind\":\"eventTarget\"},"),
            zimanPresentation, "an owner-hand destination must not name a target reference");
        Reject(zimanTemplate.Replace(
                "\"zones\":[\"equipment\",\"judgment\"]",
                "\"zones\":[\"equipment\",\"discardPile\"]"),
            zimanPresentation, "the take must read the target's visible character zones");
        Reject(zimanTemplate.Replace(
                "\"count\":1",
                "\"count\":2"),
            zimanPresentation, "the take must move exactly one card");
        Reject(zimanTemplate.Replace(
                "\"cardOwnerRef\":{\"kind\":\"eventTarget\"},",
                "\"cardOwnerRef\":{\"kind\":\"eventTarget\"},\"cardCategories\":[\"equipment\"],"),
            zimanPresentation, "category filtering requires the chooser and card owner to match");
    }

    public static void SanyaoDamagesMaxHpTargetAndReplays()
    {
        var registry = Registry(SanyaoMode, SlashDeck());
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, SanyaoMode);
            DriveToHumanPlayPhase(game);
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) continue;
            var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == Sanyao && candidate.ProgramActivationId == "spread-rumor");
            if (action is null || action.SelectableCardIds.Count == 0 ||
                action.SelectableTargetSeats.Count == 0) continue;

            var view = game.CreateSnapshot(0, revealAll: true);
            var livingMaxHp = view.Players.Where(player => player.IsAlive)
                .Select(player => player.Hp).Max();
            Require(action.SelectableTargetSeats.All(seat =>
                    view.Players[seat].IsAlive && view.Players[seat].Hp == livingMaxHp && seat != 0),
                "Sanyao must offer exactly the living maximum-HP characters other than nobody.");

            var cardId = action.SelectableCardIds[0];
            var targetSeat = action.SelectableTargetSeats[0];
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            var used = game.Submit(new UseProgramSkillCommand(0, Sanyao, "spread-rumor",
                [cardId], [targetSeat], game.Revision, game.PendingDecision!.PromptId));
            Require(used.Accepted, used.Error?.Message ?? "Sanyao activation failed.");
            var replayUsed = replay.Submit(new UseProgramSkillCommand(0, Sanyao, "spread-rumor",
                [cardId], [targetSeat], replay.Revision, replay.PendingDecision!.PromptId));
            Require(replayUsed.Accepted, replayUsed.Error?.Message ?? "Replay Sanyao activation failed.");
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "Sanyao must replay identically from the paused play-phase prompt.");
            Require(game.CardMovements.Any(item =>
                    item.CardId == cardId &&
                    item.From == CardLocation.Hand(0) &&
                    item.To == CardLocation.DiscardPile &&
                    item.Reason.Value.Contains("sanyao", StringComparison.Ordinal)),
                "Sanyao must move the selected cost card from the owner's hand to the discard pile.");
            var damages = game.Events.Select(item => item.Payload)
                .OfType<DamageAppliedEvent>()
                .Where(item => item.SourceSeat == 0 && item.TargetSeat == targetSeat)
                .ToArray();
            Require(damages.Length == 1 && damages[0].Amount == 1,
                "Sanyao must deal exactly one damage to the selected maximum-HP target.");
            Require(view.Players[targetSeat].Hp == game.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hp + 1,
                "The selected target must have lost exactly one HP from Sanyao.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Sanyao activation scene.");
    }

    public static void ZimanPreventsDamageAndTakesFieldCardAndReplays()
    {
        var outcome = RunZimanScene(ZimanMode, decline: false);
        Require(outcome is not null, "No seeded setup produced an activable Ziman window.");
        var scene = outcome!.Value;
        var game = scene.Game;
        var replay = scene.Replay;
        var bankSeat = scene.BankSeat;
        var bankHpAtPrompt = scene.BankHpAtPrompt;
        DriveUntilSettled(game);
        DriveUntilSettled(replay);

        Require(Events(game).SequenceEqual(Events(replay)) &&
                State(game) == State(replay),
            "The accepted Ziman must replay identically from the paused before-damage prompt.");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramDamagePreventedEvent>()
                .Any(item => item.SkillId == Ziman && item.OwnerSeat == 0 &&
                    item.SourceSeat == 0 && item.TargetSeat == bankSeat),
            "Ziman must record the prevention of the owner's own damage.");
        Require(!game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Any(item => item.SourceSeat == 0 && item.TargetSeat == bankSeat),
            "The prevented slash damage must never apply to the target.");
        Require(game.CreateSnapshot(0, revealAll: true).Players[bankSeat].Hp == bankHpAtPrompt,
            "The target's HP must be unchanged once Ziman prevented the damage.");
        var takes = game.CardMovements.Where(item =>
                item.Reason.Value.Contains("ziman", StringComparison.Ordinal) &&
                item.From == CardLocation.Equipment(bankSeat) &&
                item.To == CardLocation.Hand(0)).ToArray();
        Require(takes.Length == 1,
            "Ziman must move exactly one equipment card from the damage target into the owner's hand.");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                .Any(item => item.SkillId == Ziman &&
                    item.Window == SkillProgramTriggerWindow.BeforeDamageApplied && item.Activated),
            "Ziman must resolve as an activated beforeDamageApplied binding.");
    }

    public static void DeclinedZimanLetsDamageThroughAndReplays()
    {
        var outcome = RunZimanScene(ZimanMode, decline: true);
        Require(outcome is not null, "No seeded setup produced a declinable Ziman window.");
        var scene = outcome!.Value;
        var game = scene.Game;
        var replay = scene.Replay;
        var bankSeat = scene.BankSeat;
        var bankHpAtPrompt = scene.BankHpAtPrompt;
        DriveUntilSettled(game);
        DriveUntilSettled(replay);

        Require(Events(game).SequenceEqual(Events(replay)) &&
                State(game) == State(replay),
            "The declined Ziman must replay identically from the paused before-damage prompt.");
        Require(!game.CardMovements.Any(item =>
                item.Reason.Value.Contains("ziman", StringComparison.Ordinal)),
            "Declining Ziman must not move any card.");
        var damages = game.Events.Select(item => item.Payload)
            .OfType<DamageAppliedEvent>()
            .Where(item => item.SourceSeat == 0 && item.TargetSeat == bankSeat)
            .ToArray();
        Require(damages.Length == 1 && damages[0].Amount == 1,
            "The unprevented slash must apply exactly one damage to the target.");
        Require(game.CreateSnapshot(0, revealAll: true).Players[bankSeat].Hp == bankHpAtPrompt - 1,
            "The target must lose exactly one HP after the declined Ziman.");
    }

    private static (GameEngine Game, GameEngine Replay, int BankSeat, int BankHpAtPrompt)? RunZimanScene(
        string mode, bool decline)
    {
        var registry = Registry(mode, MixedSlashCrossbowDeck());
        for (var seed = 1; seed <= 200; seed++)
        {
            var game = Start(registry, seed, mode);
            if (!DriveToZimanWindow(game, out var bankSeat)) continue;

            var bankHpAtPrompt = game.CreateSnapshot(0, revealAll: true).Players[bankSeat].Hp;
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            if (decline)
            {
                DeclineTrigger(game);
                DeclineTrigger(replay);
            }
            else
            {
                AcceptTrigger(game);
                AcceptTrigger(replay);
                AnswerFieldCardTake(game);
                AnswerFieldCardTake(replay);
            }
            return (game, replay, bankSeat, bankHpAtPrompt);
        }
        return null;
    }

    private static bool DriveToZimanWindow(GameEngine game, out int bankSeat)
    {
        bankSeat = -1;
        for (var step = 0; step < 4000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (IsProgramPrompt(game, Ziman))
            {
                bankSeat = prompt.TargetSeat ?? -1;
                return bankSeat > 0;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                {
                    var view = game.CreateSnapshot(0, revealAll: true);
                    var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Slash && action.CardId is not null &&
                        action.ConversionSource is null && action.TargetSeats.Count == 1 &&
                        view.Players[action.TargetSeats[0]].IsAlive &&
                        view.Players[action.TargetSeats[0]].Seat != 0 &&
                        view.Players[action.TargetSeats[0]].Equipment.Count > 0);
                    if (slash is null)
                    {
                        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                        continue;
                    }
                    bankSeat = slash.TargetSeats[0];
                    Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
                        game.Revision, prompt.PromptId, slash.PlayedCardKind, slash.TargetCardId)));
                    continue;
                }
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                {
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.FirstOrDefault();
                    if (choice is null)
                    {
                        Advance(game);
                        continue;
                    }
                    Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                        choice.Id, game.Revision)));
                    continue;
                }
            }
        }
        return false;
    }

    private static void AnswerFieldCardTake(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The field-card take prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") ??
            throw new InvalidOperationException("The take prompt lost its card choices.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void AcceptTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The program trigger prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") != "skip") ??
            throw new InvalidOperationException("The program trigger lost its accept option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void DeclineTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The program trigger prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "skip") ??
            throw new InvalidOperationException("The program trigger lost its decline option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveToHumanPlayPhase(GameEngine game)
    {
        for (var step = 0; step < 900 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0) return;
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    return;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                        choice.Id, game.Revision)));
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game)
    {
        for (var step = 0; step < 900 && game.ResolutionStack.Count > 0; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            var choice = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.First();
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                choice.Id, game.Revision)));
        }
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new MaSuScenario(mode, deck));

    private static ContentDeckRecipe SlashDeck() =>
        DeckCore(index => "standard:slash");

    private static ContentDeckRecipe MixedSlashCrossbowDeck() =>
        DeckCore(index => index % 2 == 0 ? "standard:slash" : "standard:crossbow");

    private static ContentDeckRecipe DeckCore(Func<int, string> kindOf) =>
        new("fixture:ma-su-deck", "马谡测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kindOf(index), Suit.Spade, index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Ma Su fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Ma Su selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Ma Su fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Ma Su command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Ma Su composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class MaSuScenario(string modeId, ContentDeckRecipe deck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ma-su-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 165, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skill is Ma Su on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ma-su-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ma-su-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ma-su-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ma-su-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "马谡测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:ma-su-bank-a",
                    "fixture:ma-su-bank-b",
                    "fixture:ma-su-bank-c",
                    "fixture:ma-su-bank-d"]));
        }
    }
}
