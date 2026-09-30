using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2015WeiChecks
{
    public static void AllCardFinalizedTargetsAndReplay()
    {
        foreach (var kind in new[] { CardKind.Crossbow, CardKind.Alcohol, CardKind.Peach, CardKind.Indulgence,
                     CardKind.SupplyShortage, CardKind.Lightning, CardKind.PeachGarden })
        {
            var (game, registry) = Create("standard:none", "standard:crossbow", 17, 8, true);
            if (kind is CardKind.Peach or CardKind.PeachGarden)
            {
                Accept(game, new UseProgramSkillCommand(0, "fixture:wei2015-damage", "wound", [], [], game.Revision, game.PendingDecision!.PromptId));
                Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            }
            var ids = game.CreateSnapshot(0, true).Players[0].Hand.Where(card => card.Kind == kind).Select(card => card.Id).ToHashSet();
            var action = game.GetHumanLegalActions().First(item => item.CardId is { } id && ids.Contains(id));
            Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
                action.PlayedCardKind) { ConversionSource = action.ConversionSource });
            var window = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
            Require(window.Action.EffectiveKind == kind && window.Action.EffectiveSuit == Suit.Spade &&
                window.Action.TargetSeats.Count > 0 && window.Candidates.All(candidate => candidate.OwnerSeat != 0),
                "Every card family freezes its real spade appearance and designated targets before its native effect.");
            if (kind is CardKind.Crossbow or CardKind.Alcohol or CardKind.Peach or CardKind.Lightning)
                Require(window.Action.TargetSeats.SequenceEqual([0]), "Self-targeted native cards expose the acting character as their designated target.");
            if (kind == CardKind.PeachGarden)
                Require(window.Action.TargetSeats.Count == 4, "Group recovery retains all of the real designated targets.");
            foreach (var seat in Enumerable.Range(0, 4))
            {
                var view = game.CreateSnapshot(seat);
                Require(view.PendingDecision is null || view.PendingDecision.PlayerSeat == seat || view.PendingDecision.Choices.Count == 0,
                    "Other players cannot read the optional observer's private choices.");
            }
            Replay(game, registry);
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
            Require(game.Events.Select(envelope => envelope.Payload).OfType<CardActionAcceptedEvent>()
                .Count(item => item.Action.ActionId == window.Action.ActionId) == 1,
                "The finalized-target boundary runs exactly once, including delayed-card and equipment continuations.");
            Require(game.Events.Select(envelope => envelope.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                .Any(item => item.SkillId == "classic:zuoding"), "An AI observer can complete the same support path for every card family.");
            Replay(game, registry);
        }
    }

    public static void TopDeckBasicUseNameLedgerAndReplay()
    {
        var (game, registry) = CreateWithAction("classic:huomo", "standard:crossbow", action => action.ConversionSource?.SkillId == "classic:huomo" && action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
        var action = game.GetHumanLegalActions().First(item => item.ConversionSource?.SkillId == "classic:huomo" &&
            item.Kind == LegalActionKind.Slash && item.TargetSeat == 1);
        var card = game.CreateSnapshot(0, true).Players[0].Hand.Single(item => item.Id == action.CardId);
        Require(card.Suit is Suit.Spade or Suit.Club && CardCatalog.Get(card.Kind).CategoryName != "基本牌",
            "Top-deck use requires an actual black nonbasic cost.");
        var before = State(game);
        var illegal = game.Submit(new PlayCardCommand(0, action.CardId!.Value, [0], game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        Require(!illegal.Accepted && State(game) == before, "An illegal virtual-basic target must not pay or reveal the top-deck cost.");
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind) { ConversionSource = action.ConversionSource });
        Replay(game, registry);
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Count(move => move.CardId == card.Id && move.From == CardLocation.Hand(0) && move.To == CardLocation.DrawPile) == 1 &&
            !game.CardMovements.Any(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile),
            "The same physical nonbasic cost is placed on draw-pile top and survives virtual-card cleanup.");
        Require(game.GetHumanLegalActions().All(item => item.ConversionSource?.SkillId != "classic:huomo" ||
            item.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)),
            "Used basic-name history blocks all three elemental Slash forms together.");
        Replay(game, registry);
    }

    public static void ObserverSpadePlayDamageGateAndReplay()
    {
        var (game, registry) = Create("classic:zuoding", "standard:crossbow", 17, 8);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zuoding" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var phase = game.CreateSnapshot(0).CurrentSeat;
        Require(phase != 0, "The observer only offers support during another character's play phase.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        RejectUnknown(game);
        Replay(game, registry);
        Answer(game, choice => choice.Targets.Count == 1);
        var ownerTurn = game.CreateSnapshot(0).TurnNumber;
        for (var step = 0; step < 180 && game.CreateSnapshot(0).TurnNumber == ownerTurn; step++)
        {
            if (Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger })
            {
                Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "decline");
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        var payloads = game.Events.Select(envelope => envelope.Payload).ToArray();
        var damageIndex = Array.FindIndex(payloads, item => item is DamageAppliedEvent);
        Require(damageIndex >= 0 && !payloads.Skip(damageIndex + 1).TakeWhile(item => item is not TurnStartedEvent)
            .OfType<ProgramSkillStartedEvent>().Any(item => item.SkillId == "classic:zuoding"),
            "Any actual damage during the play phase closes later spade-card support for that phase.");
        Replay(game, registry);
    }

    public static void WeaponDamageEntityCostsPrivacyAndReplay()
    {
        foreach (var weapon in new[] { "standard:crossbow", "classic:qilin-bow" })
        {
            var (game, registry) = CreateWithAction("classic:qingxi", weapon, action => action.Kind == LegalActionKind.Equip);
            var equip = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
            Accept(game, new PlayCardCommand(0, equip.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId));
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            var slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
            Accept(game, new PlayCardCommand(0, slash.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId));
            Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxi" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
            var draft = frame.WeaponDamageDraft!;
            Require(draft.WeaponCardId == equip.CardId, "The cost freezes the actual equipped weapon identity.");
            foreach (var seat in Enumerable.Range(0, 4).Where(seat => seat != draft.TargetSeat))
                Require(game.CreateSnapshot(seat).PendingDecision is null || game.CreateSnapshot(seat).PendingDecision!.Choices.Count == 0,
                    "Other viewers cannot see the victim's staged hand costs.");
            RejectUnknown(game);
            Replay(game, registry);
            var movementStart = game.CardMovements.Count;
            for (var step = 0; step < 128 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(item => item.WeaponDamageDraft is not null); step++)
            {
                Accept(game, new AdvanceOneStepCommand(game.Revision));
                Replay(game, registry);
            }
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            var choiceEvent = game.Events.Select(envelope => envelope.Payload).OfType<ProgramWeaponDamageChoiceEvent>().Last();
            if (choiceEvent.Discarded)
            {
                Require(choiceEvent.DiscardedCardIds.Count == draft.RequiredCount &&
                    game.CardMovements.Skip(movementStart).Count(move => move.CardId == equip.CardId && move.To == CardLocation.DiscardPile) == 1 &&
                    choiceEvent.DiscardedCardIds.All(id => game.CardMovements.Skip(movementStart).Count(move => move.CardId == id && move.From == CardLocation.Hand(1) && move.To == CardLocation.DiscardPile) == 1),
                    "Discard branch pays exact actual hand costs and the actual weapon once.");
            }
            else Require(choiceEvent.DamageAfter == choiceEvent.DamageBefore + 1 &&
                !game.CardMovements.Skip(movementStart).Any(move => move.CardId == equip.CardId && move.To == CardLocation.DiscardPile),
                "Bonus branch increases the active frozen damage and retains the weapon.");
            Require(game.Events.Select(envelope => envelope.Payload).OfType<DamageAppliedEvent>().Last().Amount == choiceEvent.DamageAfter,
                "The selected damage amount must reach actual damage resolution.");
            Replay(game, registry);
        }
        var (distanceGame, distanceRegistry) = Create("classic:qianju", "standard:crossbow", 17, 8);
        Require(distanceGame.GetCombatDistance(0, 2) == 2, "Full-health outgoing distance retains the printed seat distance.");
        Accept(distanceGame, new UseProgramSkillCommand(0, "fixture:wei2015-damage", "wound", [], [], distanceGame.Revision, distanceGame.PendingDecision!.PromptId));
        Reach(distanceGame, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(distanceGame.GetCombatDistance(0, 2) == 1 && distanceGame.GetCombatDistance(2, 0) == 2,
            "Actual lost HP reduces only the owner's outgoing distance and preserves the one-distance floor.");
        Replay(distanceGame, distanceRegistry);
    }

    public static void NextTurnAllHandGrantAndReplay()
    {
        var (game, registry) = Create("classic:mingjian", "standard:crossbow", 17, 8);
        var hand = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToArray();
        Accept(game, new UseProgramSkillCommand(0, "classic:mingjian", "give-all-hand-next-turn", [], [1], game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 128 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == "classic:mingjian"); step++)
        {
            Replay(game, registry);
            if (Prompt(game) is { Kind: DecisionKind.ProgramTrigger }) Answer(game, choice => true);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == 0 && hand.All(id =>
            game.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Hand(0) && move.To == CardLocation.Hand(1)) == 1),
            "Giving the all-hand set moves each existing physical card exactly once.");
        var scheduled = game.Events.Select(envelope => envelope.Payload).OfType<ProgramNextTurnRuleModifierQueuedEvent>().ToArray();
        Require(scheduled.Length == 2 && scheduled.All(item => item.Modifier.TargetSeat == 1), "Both independent next-turn grants retain their recipient.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 128 && game.Events.Select(envelope => envelope.Payload).OfType<TurnRuleModifierGrantedEvent>().Count() < 2; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        var applied = game.Events.Select(envelope => envelope.Payload).OfType<TurnRuleModifierGrantedEvent>().ToArray();
        Require(applied.Length == 2 && applied.All(item => item.Modifier.TurnSeat == 1 && item.Modifier.Amount == 1 &&
            item.Modifier.Source.OwnerSeat == 0 && item.Modifier.AffectedSeat == 1) &&
            applied.Select(item => item.Modifier.Query).ToHashSet().SetEquals([SkillRuleQuery.HandLimit, SkillRuleQuery.SlashLimit]),
            "The complete gift activates both rule bonuses during the recipient's next executable turn.");
        Require(ReadNumericRule(game, 1, "GetHandLimit") == game.CreateSnapshot(1).Players[1].Hp + 1 &&
            ReadNumericRule(game, 1, "GetSlashUseLimit") == 2 && game.GetAttackRange(1) == 1,
            "The next-turn gift changes the recipient's actual discard threshold and Slash allowance while preserving printed attack range.");
        Replay(game, registry);
    }

    public static void FactionRecoveryDebtAndReplay()
    {
        foreach (var fragileResponders in new[] { false, true })
        {
        var (game, registry) = CreateFactionRecoveryCase(fragileResponders);
        var beforeHp = game.CreateSnapshot(0).Players[0].Hp;
        Accept(game, new UseProgramSkillCommand(0, "fixture:wei2015-damage", "damage", [], [0], game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:xingshuai" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Require(game.CreateSnapshot(0).Players[0].Hp <= 0, "The lord is actually dying before faction aid.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var responders = game.ResolutionStack.OfType<ProgramSkillFrame>().Last().FactionRecoveryDraft!.ResponderSeats;
        Require(responders.All(seat =>
            registry.Generals[game.CreateSnapshot(seat, true).Players[seat].GeneralId].FactionId == "wei" && seat != 0), "Faction aid freezes only living other Wei characters.");
        Replay(game, registry);
        for (var step = 0; step < 160 && !game.Events.Any(envelope => envelope.Payload is DyingResolvedEvent { VictimSeat: 0 }); step++)
        {
            Accept(game, new AdvanceOneStepCommand(game.Revision));
            Replay(game, registry);
        }
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var accepted = game.Events.Select(envelope => envelope.Payload).OfType<ProgramFactionRecoveryChoiceEvent>().Where(item => item.Accepted).ToArray();
        Require(game.Events.Select(envelope => envelope.Payload).OfType<ProgramFactionRecoveryChoiceEvent>()
            .Select(item => item.ResponderSeat).SequenceEqual(responders),
            "Every frozen Wei responder chooses once in order, including responders reached after the lord already recovered above zero.");
        Require(accepted.Length > 0 && game.CreateSnapshot(0).Players[0].Hp > 0 && beforeHp > 0,
            "A loyal faction responder performs real recovery before the lord exits dying.");
        var exitIndex = game.Events.Select(envelope => envelope.Payload).ToList().FindIndex(item => item is DyingResolvedEvent { VictimSeat: 0 });
        Require(accepted.All(item => game.Events.Skip(exitIndex + 1).Any(envelope => envelope.Payload is DamageAppliedEvent damage &&
            damage.TargetSeat == item.ResponderSeat && damage.Amount == 1 && damage.SourceLess)), "Every accepting responder pays damage without source attribution strictly after the original dying resolution.");
        Require(accepted.All(item => game.Events.Skip(exitIndex + 1).Any(envelope => envelope.Payload is DamageRequestedEvent damage &&
            damage.TargetSeat == item.ResponderSeat && damage.SourceLess) &&
            game.Events.Skip(exitIndex + 1).Any(envelope => envelope.Payload is AfterDamageEvent completed &&
                completed.TargetSeat == item.ResponderSeat && completed.SourceLess)),
            "The entire deferred damage lifecycle retains its source-free public attribution.");
        Require(!game.Events.Skip(exitIndex + 1).Any(envelope => envelope.Payload is ProgramSkillStartedEvent { SkillId: "classic:xingshuai" }),
            "The limited skill is consumed once and debt settlement cannot reenter it.");
        Require(game.Events.Count(envelope => envelope.Payload is DyingResolvedEvent { VictimSeat: 0 }) == 1,
            "Nested debt damage must never resolve the original dying occurrence twice.");
        if (fragileResponders)
            Require(accepted.All(item => game.Events.Any(envelope => envelope.Payload is PlayerDiedEvent death &&
                death.VictimSeat == item.ResponderSeat && death.KillerSeat is null)),
                "Lethal source-free debts suspend through the responder's own dying sequence and retain no killer credit.");
        Replay(game, registry);
        }
    }

    public static void JudgmentColorDamageBenefitAndReplay()
    {
        var colors = new HashSet<bool>();
        for (var seed = 17; seed < 33 && colors.Count < 2; seed++)
        {
        var (game, registry) = Create("classic:huituo", "standard:crossbow", seed, 8);
        Accept(game, new UseProgramSkillCommand(0, "fixture:wei2015-damage", "damage", [], [0], game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:huituo" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Replay(game, registry);
        var beforeHp = game.CreateSnapshot(0).Players[0].Hp;
        var beforeHand = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        Answer(game, choice => choice.Targets.SequenceEqual([0]));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var judgment = game.Events.Select(envelope => envelope.Payload).OfType<JudgmentResolvedEvent>().Last(item => item.Reason == "huituo");
        var red = judgment.Suit is Suit.Heart or Suit.Diamond;
        colors.Add(red);
        Require(red ? game.CreateSnapshot(0).Players[0].Hp == beforeHp + 1 && game.CreateSnapshot(0, true).Players[0].Hand.Count == beforeHand :
            game.CreateSnapshot(0).Players[0].Hp == beforeHp && game.CreateSnapshot(0, true).Players[0].Hand.Count == beforeHand + 4,
            "The finalized judgment color applies real one-point recovery or four actual cards matching the original damage amount.");
        Require(game.CardMovements.Count(move => move.CardId == judgment.CardId && move.To == CardLocation.DiscardPile) == 1,
            "The real judgment card leaves processing once after its benefit.");
        Replay(game, registry);
        }
        Require(colors.Count == 2, "Both final red and black judgment benefits must be exercised.");
    }

    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    {
        for (var step = 0; step < 300; step++)
        { if (Prompt(game) is { } prompt && condition(prompt)) return; Accept(game, new AdvanceOneStepCommand(game.Revision)); }
        throw new InvalidOperationException("The Wei 2015 fixture did not reach its configured prompt.");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> condition)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(condition).Id, game.Revision));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        TrustedViews = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat, true))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(envelope => JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())).ToArray(),
        Movements = game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands)
    });
    private static void RejectUnknown(GameEngine game)
    {
        var before = State(game); var prompt = Prompt(game)!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new ChoiceId("fixture:illegal"), game.Revision));
        Require(!result.Accepted && State(game) == before, "Invalid answers must preserve every player's view, frames, events, movement ledger and command journal atomically.");
    }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(replay), "Checkpoint plus command JSON must reproduce every player's view, draft, typed event and real movement ledger.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Wei 2015 command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static int ReadNumericRule(GameEngine game, int seat, string method)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", flags)!.GetValue(game)!;
        return (int)typeof(GameEngine).GetMethod(method, flags)!.Invoke(game, [players[seat]])!;
    }
    private static (GameEngine Game, ContentRegistry Registry) CreateWithAction(string skill, string weapon, Func<LegalAction, bool> condition)
    {
        for (var seed = 17; seed < 33; seed++)
        {
            var result = Create(skill, weapon, seed, 8);
            if (result.Game.GetHumanLegalActions().Any(condition)) return result;
        }
        throw new InvalidOperationException("No deterministic fixture hand exposes the required real physical card action.");
    }
    private static (GameEngine Game, ContentRegistry Registry) CreateFactionRecoveryCase(bool fragileResponders)
    {
        for (var seed = 17; seed < 33; seed++)
        {
            var result = Create("classic:xingshuai", "standard:crossbow", seed, 3, fragileResponders: fragileResponders);
            if (result.Game.CreateSnapshot(0, true).Players.Any(player => player.Seat != 0 && player.Role == Role.Loyalist &&
                result.Registry.Generals[player.GeneralId].FactionId == "wei")) return result;
        }
        throw new InvalidOperationException("No deterministic fixture has a living loyal Wei responder.");
    }
    private static (GameEngine Game, ContentRegistry Registry) Create(string skill, string weapon, int seed, int hp, bool diverse = false, bool fragileResponders = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(skill, weapon, hp, diverse, fragileResponders));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-wei2015", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:wei2015-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        return (game, registry);
    }
    private sealed class Scenario(string skill, string weapon, int hp, bool diverse, bool fragileResponders) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wei2015", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wei2015-damage","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:wei2015-damage":{"name":"测试伤害","description":"造成4点伤害"}}}""");
            builder.AddSkill(new("fixture:wei2015-damage", "测试伤害", "造成4点伤害") { Program = catalog.Programs["fixture:wei2015-damage"], ProgramPresentation = catalog.Presentations["fixture:wei2015-damage"] });
            builder.AddGeneral(new("fixture:wei2015-owner", "魏将2015", "supporter", skill, "wei", BaseHp: hp, AdditionalSkillIds: ["fixture:wei2015-damage"]));
            foreach (var seat in Enumerable.Range(1, 3)) builder.AddGeneral(new($"fixture:wei2015-{seat}", "目标", "supporter", diverse ? "classic:zuoding" : "standard:none", seat == 3 ? "shu" : "wei", BaseHp: fragileResponders ? 1 : 9));
            var ids = new[] { "standard:crossbow", "standard:alcohol", "standard:peach", "standard:indulgence", "standard:supply_shortage", "standard:lightning", "standard:peach_garden" };
            builder.AddDeck(new("fixture:wei2015-deck", "实体杀及武器", diverse ? 35 : 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, diverse ? 350 : 160).Select(index => new ContentDeckPhysicalCard(diverse ? ids[index % ids.Length] : index % 3 == 0 ? weapon : "standard:slash", diverse || index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-wei2015", "魏将2015测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:wei2015-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wei2015-owner", "fixture:wei2015-1", "fixture:wei2015-2", "fixture:wei2015-3"]));
        }
    }
}
