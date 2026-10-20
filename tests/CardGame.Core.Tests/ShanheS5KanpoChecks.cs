using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5KanpoChecks
{
    private const string Skill = StandardShanheCampaignCatalog.S5KanpoSkillId;
    private const string Reward = StandardShanheCampaignCatalog.S5KanpoRewardId;
    private const string Owner = "fixture:s5-kanpo-owner", Peer = "fixture:s5-kanpo-peer", Deck = "fixture:s5-kanpo-deck";
    private const string Driver = "fixture:s5-kanpo-driver", Paid = "fixture:s5-kanpo-paid";
    private const string Accepted = "fixture:s5-kanpo-accepted", Completed = "fixture:s5-kanpo-completed";
    private const string Rescue = "fixture:s5-kanpo-rescue", Spread = "fixture:s5-kanpo-spread";
    private static readonly Lazy<ContentRegistry> Production = new(() =>
        StandardShanheCampaignCatalog.CreateBattleRegistry(new(113, 1, "fixture:s5-kanpo-production", "classic:guan-yu", 4,
            0, 0, 0, 0, [], [], [], [new("classic:zhang-fei")], [], new string('a', 64))));

    public static void PurchasedHeAndNativeCounterspellsKeepSecondLayerAndFreshTargetWindowsCold()
    {
        VerifyCodexBinding();
        foreach (var scene in new[] { "hand", "equipment", "native", "second-layer" })
        {
            var s = Scene.Start(scene);
            var material = s.V().Hand.Single(c => c.Kind == (scene == "native" ? CardKind.Nullification :
                scene == "equipment" ? CardKind.RenwangShield : CardKind.Dodge) &&
                (scene is "native" or "equipment" || c.Suit == Suit.Club)).Id;
            if (scene == "equipment") { s.Use(material); s.Play(); }
            var from = scene == "equipment" ? CardLocation.Equipment(0) : CardLocation.Hand(0);
            var card = s.BeginDrawTwo();
            s.Nullification();
            var window = s.Window;
            Need(window.ChainDepth == (scene == "second-layer" ? 1 : 0) &&
                (scene != "second-layer" || s.E<NullificationRespondedEvent>() is [{ ResponderSeat: 3, ChainDepth: 1 }]),
                "The native NPC pays its own genuine first counterspell before the human's second-layer response; no NPC command or hidden card lookup is synthesized.");
            Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([material])) &&
                !s.P.Choices.Any(c => c.Cards.Any(id => s.V().Hand.Any(h => h.Id == id && h.Kind == CardKind.Dodge && h.Suit == Suit.Heart))),
                "The bought production skill publishes genuine black HE materials and excludes the owner's real red hand card.");
            s.Private(); s.Cold(); s.Respond(material, native: scene == "native"); s.At(Paid);
            var paid = s.Window;
            var action = paid.CounterspellPayment!.Action;
            var receipt = paid.UnrespondableCounterspell!;
            Need(receipt is { Source.SkillId: Skill, ResponderSeat: 0 } && receipt.WindowFrameId == window.Id &&
                receipt.ParentCardUseFrameId == paid.ParentFrameId && receipt.ActionId == action.ActionId &&
                receipt.ChainDepth == (scene == "second-layer" ? 2 : 1) &&
                action is { Type: CardActionType.Response, EffectiveKind: CardKind.Nullification, ActorSeat: 0, ProviderSeat: 0, ResponderSeat: 0, RequesterSeat: null } &&
                action.TargetSeats.Count == 0 && action.PhysicalCards is [var cost] && cost.CardId == material && cost.From == from &&
                (scene == "native" ? action.ConversionChain.Count == 0 : action.ConversionChain is [{ SkillId: Skill, BindingId: "black-he-as-nullification" }]),
                "Actual native and converted uses retain their exact paid actor/material, original trick and policy instance; response storage does not turn them into a played Dodge or invent a character target.");
            Need(s.G.CardMovements.Count(m => m.CardId == material && m.From == from && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1 &&
                s.E<UnrespondableCounterspellIssuedEvent>().Single().ActionId == action.ActionId &&
                s.E<NullificationResolvedEvent>().Length == 0,
                "The exact no-response node is issued once, while its real movement child still owns the original effect's unfinished return.");
            s.Private(); s.RejectUnpublished(); s.Cold(); s.Continue(); s.At(Accepted); s.Cold(); s.Continue(); s.At(Completed); s.Cold(); s.Continue(); s.Play();
            Need(s.E<NullificationResolvedEvent>() is [var finished] && finished.ResolutionId == paid.ParentFrameId &&
                finished.ChainDepth == receipt.ChainDepth && finished.EffectNullified == (scene != "second-layer") &&
                s.E<NullificationRespondedEvent>().Length == receipt.ChainDepth &&
                s.E<NullificationRequestedEvent>().All(e => e.ChainDepth < receipt.ChainDepth) &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == card) == 1 &&
                s.E<UnrespondableCounterspellIssuedEvent>().Length == 1 &&
                s.E<ProgramCardTriggerResolvedEvent>().Count(e => e.SkillId == Accepted) == 1 &&
                s.E<ProgramCardTriggerResolvedEvent>().Count(e => e.SkillId == Completed) == 1,
                "Payment, accepted-use and completed-use children return once before the last counterspell ends that exact layer; a depth-two reply restores the original Draw Two.");
            s.AssertPaidOnce(material, from, CardMoveReasons.Nullification, CardMoveReasons.NullificationFinished);
            s.AssertPaidOnce(card, CardLocation.Hand(0), CardMoveReasons.Use, CardMoveReasons.UseFinished); s.Cold();
        }
        VerifyNextTargetWindow();
    }

    public static void PaidCounterspellChildrenRetainPolicyAfterSourceLossOrDeathCold()
    {
        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var s = Scene.Start(scene); var material = s.V().Hand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Club).Id;
            var original = s.BeginDrawTwo(); s.Nullification(); s.Respond(material, native: false); s.At(Paid);
            var receipt = s.Window.UnrespondableCounterspell!; var actionId = s.Window.CounterspellPayment!.Action.ActionId;
            Need(s.HasSkill && receipt.Source.SkillId == Skill && s.V().Hp == 12,
                "The acquired skill is genuinely live when the actual black material is paid and its exact no-response receipt is frozen.");
            s.Cold(); s.Continue();
            if (scene == "paid-loss")
            {
                s.Until(() => s.V().Hp == 1 && !s.HasSkill && s.P?.SkillPrompt?.SkillId == Accepted);
                Need(s.Window.UnrespondableCounterspell == receipt && s.E<ProgramOwnerSkillsReplacedEvent>().Single().LostSkillIds.SequenceEqual([Skill]) &&
                    s.E<RecoveryAppliedEvent>() is [{ TargetSeat: 0, Amount: 1, RemainingHp: 1 }],
                    "A real payment-child dying entry recovers one and permanently removes the purchased source, while the already-issued policy remains attached to its original response.");
                s.Private(); s.Cold(); s.Continue(); s.At(Completed); s.Cold(); s.Continue(); s.Play();
            }
            else
            {
                s.Until(() => s.G.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == 0));
                Need(s.Window.UnrespondableCounterspell == receipt && s.Window.CounterspellPayment?.Action.ActionId == actionId,
                    "The real zero-HP source is suspended under the same paid counterspell rather than paying again or borrowing another use.");
                s.Cold();
                s.Until(() => !s.V().IsAlive && s.E<CardUseFinishedEvent>().Any(e => e.CardId == original));
                Need(s.G.CreateSnapshot(0).Players[1].IsAlive && s.G.State.Winner == Winner.None,
                    "The paid source can die without ending the team battle or concealing the native return behind terminal-game cleanup.");
            }
            Need(s.E<UnrespondableCounterspellIssuedEvent>() is [var issued] && issued.ActionId == actionId &&
                s.E<NullificationResolvedEvent>() is [{ ChainDepth: 1, EffectNullified: true }] &&
                s.E<NullificationRequestedEvent>().All(e => e.ChainDepth == 0) &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == original) == 1,
                "A paid source loss or death does not reopen counter-counterspell candidates, repeat the original material, or lose the original trick's completion.");
            s.AssertPaidOnce(material, CardLocation.Hand(0), CardMoveReasons.Nullification, CardMoveReasons.NullificationFinished);
            s.AssertPaidOnce(original, CardLocation.Hand(0), CardMoveReasons.Use, CardMoveReasons.UseFinished); s.Cold();
        }
    }

    private static void VerifyNextTargetWindow()
    {
        var s = Scene.Start("multi"); var original = s.V().Hand.Single(card => card.Kind == CardKind.Duel).Id;
        Need(s.G.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Duel && action.CardId == original &&
                action.ConversionSource is null && (action.TargetSeat == 1 || action.TargetSeats.SequenceEqual([1]))),
            "The multi-target regression starts with a published physical Duel legally designating the real ally.");
        s.Accept(new PlayCardCommand(0, original, [1], s.G.Revision, s.P!.PromptId));
        s.Until(() => s.P is { } prompt && prompt.SkillPrompt?.SkillId == Spread && prompt.Choices.Any(c => c.Targets.SequenceEqual([2])));
        s.Private(); s.Cold(); s.Answer(c => c.Targets.SequenceEqual([2])); s.Nullification();
        var first = s.Window; var target = first.TargetSeats.Single();
        Need(target == 1 && first.EffectCardKind == CardKind.Duel &&
            s.E<ProgramCardUseTargetAddedEvent>() is [{ TargetSeat: 2 }],
            "The first native Duel target is ally one, and an actual finalized-target instruction appends the independently legal enemy two.");
        var material = s.V().Hand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Club).Id;
        s.Respond(material, native: false); s.At(Paid); s.Cold(); s.Continue(); s.At(Accepted); s.Continue(); s.At(Completed); s.Continue();
        s.Until(() => s.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } && s.Window.Id != first.Id);
        Need(s.Window.ParentFrameId == first.ParentFrameId && s.Window.TargetSeats.SequenceEqual([2]) && s.Window.ChainDepth == 0 &&
            s.Window.UnrespondableCounterspell is null && s.Window.CounterspellPayment is null &&
            s.E<NullificationResolvedEvent>() is [{ ChainDepth: 1, EffectNullified: true }],
            "A genuinely added Duel target opens its own fresh window; the previous target's no-response node does not mark the original use globally unrespondable.");
        s.Private(); s.Cold(); s.Answer(c => Response(c) == "pass"); s.Play();
        Need(s.E<NullificationResolvedEvent>() is [{ ChainDepth: 1, EffectNullified: true }, { ChainDepth: 0, EffectNullified: false }] &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == original) == 1 &&
            s.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 2, Amount: 1, RemainingHp: 19 }] &&
            s.G.CreateSnapshot(0).Players[1].Hp == 20 && s.G.CreateSnapshot(0).Players[2].Hp == 19,
            "Only the first target is canceled; the empty-handed enemy suffers the next target's actual native Duel damage, the ally is unharmed and the physical use finishes once.");
        s.AssertPaidOnce(material, CardLocation.Hand(0), CardMoveReasons.Nullification, CardMoveReasons.NullificationFinished);
        s.AssertPaidOnce(original, CardLocation.Hand(0), CardMoveReasons.Use, CardMoveReasons.UseFinished); s.Cold();
    }

    private static void VerifyCodexBinding()
    {
        var current = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var old = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        Need(Skill == "boundary:kanpo-current" && current.Rewards.Single(r => r.Id == Reward).SkillId == Skill &&
            StandardShanheCampaignCatalog.CodexRewardIdsByNodeId[48] == Reward &&
            StandardShanheCampaignCatalog.ShopMetadataByRewardId[Reward] is { SourceRewardId: 3018, Price: 300, Rarity: 2, Kind: "技能" } &&
            old.Rewards.All(r => r.Id != Reward) &&
            StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId).Rewards.All(r => r.Id != Reward),
            "Node 48 buys the existing verified 3261 program at its real skill price and rarity, only in the current codex; the old hand-only Kanpo is not substituted.");
    }

    private static string Response(PromptChoice choice) => choice.Parameters.GetValueOrDefault("response") ?? "";
    private static bool IsContinue(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "choose-option" && c.Parameters.GetValueOrDefault("option-id") == "continue";
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V() => G.CreateSnapshot(0).Players[0];
        public bool HasSkill => V().Skills?.Any(s => s.ContentId == Skill) == true;
        public NullificationWindowFrame Window => G.ResolutionStack.OfType<NullificationWindowFrame>().Single();
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "The real Kanpo fixture command was rejected.");
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P ?? throw new InvalidOperationException("No human prompt.");
            Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision));
        }
        public void Continue() => Answer(IsContinue);
        public void At(string skill) => Until(() => P is { } prompt && prompt.SkillPrompt?.SkillId == skill && prompt.Choices.Any(IsContinue));
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Nullification() => Until(() => P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        public int BeginDrawTwo()
        {
            var card = V().Hand.First(c => c.Kind == CardKind.DrawTwo).Id; Use(card); return card;
        }
        public void Use(int card)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId == card && a.ConversionSource is null && a.Kind != LegalActionKind.Recast);
            var targets = action.TargetSeats.Count != 0 ? action.TargetSeats : action.TargetSeat is { } target ? [target] : Array.Empty<int>();
            Accept(new PlayCardCommand(0, card, targets, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Respond(int material, bool native) => Answer(c => c.Cards.SequenceEqual([material]) &&
            (native ? !c.Parameters.ContainsKey("conversion-skill-id") : c.Parameters.GetValueOrDefault("conversion-skill-id") == Skill));
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 180 && !done(); i++)
            {
                if (P is { Kind: DecisionKind.Nullification }) Answer(c => Response(c) == "pass");
                else if (P?.Choices.Any(IsContinue) == true) Continue();
                else if (P is { Kind: DecisionKind.RescueDying }) Answer(c => Response(c) == "let-die");
                else if (P is { Kind: DecisionKind.PlayCard }) throw new InvalidOperationException("Returned to Play before the expected Kanpo boundary: " + scene);
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(done(), "The genuine Kanpo boundary did not arrive: " + JsonSerializer.Serialize(new { scene, Prompt = P?.Kind, Skill = P?.SkillPrompt?.SkillId,
                Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }), Events = E<NullificationRespondedEvent>() }));
        }
        public void AssertPaidOnce(int card, CardLocation from, CardMoveReason paid, CardMoveReason finished) =>
            Need(G.CardMovements.Count(m => m.CardId == card && m.From == from && m.To == CardLocation.Processing && m.Reason == paid) == 1 &&
                G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == finished) == 1,
                "The exact original material must be paid and cleaned up once across all cold returns.");
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))).ToArray(),
            Frames = JsonSerializer.Serialize(G.ResolutionStack), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves the original effect, exact policy node, paid material, private prompt and all four views.");
        }
        public void Private()
        {
            var before = State();
            for (var seat = 0; seat < 4; seat++)
            {
                var view = G.CreateSnapshot(seat);
                Need(view.Players.Where(p => p.Seat != seat).All(p => p.Hand.Count == 0), "Counterspell choices cannot reveal another player's hidden hand.");
                if (seat != 0) Need(view.PendingDecision is null, "Only the real responding owner sees its material or program choices.");
            }
            if (P is { } prompt)
            {
                Need(prompt.Choices is IList<PromptChoice> { IsReadOnly: true } && prompt.ValidCardIds is IList<int> { IsReadOnly: true }, "Published response choices and material IDs are frozen.");
                foreach (var choice in prompt.Choices)
                    Need(choice.Cards is IList<int> { IsReadOnly: true } && choice.Parameters is IDictionary<string, string> { IsReadOnly: true }, "Nested material and parameter collections are frozen.");
            }
            Need(before == State(), "Privacy and snapshot queries do not mutate the accepted prefix.");
        }
        public void RejectUnpublished()
        {
            var before = State();
            Need(!G.Submit(new AnswerPromptCommand(0, P!.PromptId, new("unpublished-kanpo-response"), G.Revision)).Accepted && before == State(),
                "An unpublished response cannot mutate the receipt, material ledger, RNG or command prefix.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var realReward = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId)
                .Rewards.Single(r => r.Id == Reward);
            ShanheStartingCard[] cards = [new(CardKind.Dodge, Suit.Club, 8), new(CardKind.Dodge, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 9),
                new(CardKind.RenwangShield, Suit.Club, 2), new(CardKind.Nullification, Suit.Spade, 11), new(scene == "multi" ? CardKind.Duel : CardKind.DrawTwo, Suit.Diamond, 3)];
            var enemies = new[] { new ShanheCombatantDefinition(Peer + "-2") { StartingHand = [] },
                new ShanheCombatantDefinition(Peer + "-3") { StartingHand = scene == "second-layer" ? [new(CardKind.Nullification, Suit.Club, 12)] : [] } };
            var allies = new[] { new ShanheCombatantDefinition(Peer + "-1") { StartingHand = [] } };
            var definition = new ShanheCampaignDefinition("fixture:s5-kanpo-purchase-" + scene, "真实看破购买", [Owner], ["shop"],
                [new("shop", "真实铜钱购买", ShanheNodeKind.Shop, ["battle"], Choices:
                    [new("buy", "传习看破", GoldCost: 300, RewardId: Reward, LeavesNode: false),
                     new("entities", "原生实体夹具", RewardId: "fixture:kanpo-entities", LeavesNode: false)]),
                 new("battle", "已购买技能的真实首战", ShanheNodeKind.Battle, [], Encounters: [new("actual", "原生四席战斗", enemies, allies)])],
                [realReward, new("fixture:kanpo-entities", "原生实体夹具", StartingCards: cards)], StartingGold: 300, ExtraSkillSlots: 1);
            var run = ShanheCampaignSession.Create(definition, new(113, Owner) { UnlockedRewardIds = [Reward, "fixture:kanpo-entities"] }, _ => registry);
            void Submit(ShanheCampaignCommand command)
            { var result = run.Submit(command); Need(result.Accepted, result.Error ?? "Real Kanpo purchase rejected."); }
            Submit(new MoveShanheNodeCommand("shop", run.Revision));
            Need(!run.Snapshot.ExtraSkillIds.Contains(Skill) && run.Snapshot.ShopItems.Single(i => i.Reward?.Id == Reward) is
                { State: ShanheShopItemState.Available, GoldCost: 300, RequiresSkillReplacement: false }, "The unlocked source is initially unearned and costs its real 300 copper.");
            Submit(new ChooseShanheNodeOptionCommand("buy", null, run.Revision));
            Need(run.Snapshot.Gold == 0 && run.Snapshot.ExtraSkillIds.SequenceEqual([Skill]) && run.Snapshot.TacticIds.Count == 0,
                "A real shop command pays copper and occupies exactly one extra-skill slot, rather than directly granting the engine a test skill.");
            var snapshot = JsonSerializer.Serialize(run.Snapshot);
            run = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(run.CreateCheckpoint())), definition, _ => registry);
            Need(snapshot == JsonSerializer.Serialize(run.Snapshot), "Campaign cold recovery preserves the paid reward and purchased stock.");
            Submit(new ChooseShanheNodeOptionCommand("entities", null, run.Revision)); Submit(new ChooseShanheNodeOptionCommand("$leave", null, run.Revision));
            Submit(new MoveShanheNodeCommand("battle", run.Revision));
            var setup = run.PendingBattleSetup!;
            Need(setup.ExtraSkillIds.SequenceEqual([Skill]) && setup.TacticSkillIds.Count == 0, "Only the purchased real skill is passed to the native battle.");
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); result.Play(); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-kanpo", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(Production.Value.GetSkill(Skill));
            var choiceLabels = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            object Pause(string skillId)
            {
                choiceLabels[skillId] = new() { ["continue"] = "继续" };
                return new { op = "chooseOption", target = "owner", resultBind = "pause", options = new[] { new { id = "continue" } } };
            }
            Dictionary<string, object> Presentation(string skillId)
            {
                var value = new Dictionary<string, object> { ["name"] = skillId, ["description"] = "真实原生回返夹具" };
                if (choiceLabels.TryGetValue(skillId, out var labels)) value["optionLabels"] = labels;
                return value;
            }
            var paidEffects = new List<object> { Pause(Paid) };
            if (scene is "paid-loss" or "paid-death") paidEffects.Add(new { op = "loseHp", target = "owner", amount = 12 });
            var definitions = new List<object>
            {
                new { id = Driver, revision = 1, modifiers = new[] { new { id = "keep-materials", query = "handLimit", operation = "add", value = 20, priority = 0 } } },
                new { id = Paid, revision = 1, triggers = new[] { new { id = "real-counterspell-cost", window = "cardsMoved", subject = "owner",
                    sourceZones = new[] { "hand", "equipment" }, movementReasons = new[] { CardMoveReasons.Nullification.Value }, movementOccurrence = "perOwnerBatch",
                    optional = false, usageScope = "game", usageLimit = 1, effects = paidEffects } } },
                new { id = Accepted, revision = 1, triggers = new[] { new { id = "accepted", window = "cardResponseAccepted", ownerRelation = "actor",
                    cardKinds = new[] { "nullification" }, optional = false, effects = new[] { Pause(Accepted) } } } },
                new { id = Completed, revision = 1, triggers = new[] { new { id = "completed", window = "cardUseCompleted", ownerRelation = "actor",
                    cardKinds = new[] { "nullification" }, includeResponseUses = true, optional = false, effects = new[] { Pause(Completed) } } } }
            };
            var own = new List<string> { Paid, Accepted, Completed };
            if (scene == "paid-loss")
            {
                definitions.Add(new { id = Rescue, revision = 1, triggers = new[] { new { id = "actual-dying-awakening", window = "dyingEntering", subject = "owner",
                    optional = false, usageScope = "game", usageLimit = 1, effects = new object[] { new { op = "recover", target = "owner", amount = 1 },
                        new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Skill }, sourceBind = "standard:none" } } } } });
                own.Add(Rescue);
            }
            if (scene == "multi")
            {
                definitions.Add(new { id = Spread, revision = 1, triggers = new[] { new { id = "genuine-added-target", window = "cardUseTargetsFinalized", ownerRelation = "actor",
                    cardKinds = new[] { "duel" }, optional = false, priority = 100, effects = new object[] {
                        new { op = "selectTarget", target = "owner", targetKind = "otherLegalCurrentCardTarget" }, new { op = "addCurrentCardUseTarget", target = "selectedTarget" } } } } });
                own.Add(Spread);
            }
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = own.Append(Driver).ToDictionary(id => id, Presentation) }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "共享原生付款子流程") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实购买看破的角色", "", Driver, "qun", 12, own));
            foreach (var seat in new[] { 1, 2, 3 }) builder.AddGeneral(new(Peer + "-" + seat, "无测试技能的实际角色", "", "standard:none", "wei", 20));
            builder.AddDeck(new(Deck, "无随机初手的真实实体牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Diamond, 6)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "购买后真实看破与原生无懈", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer + "-1", Peer + "-2", Peer + "-3"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
