using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class OlChengJiChengCuiChecks
{
    private const string Tousui = "ol:tousui", Xuming = "ol:xuming", Driver = "fixture:cjcc-driver",
        Watch = "fixture:cjcc-watch", Quiet = "fixture:cjcc-quiet", Owner = "fixture:cjcc-owner",
        Ally = "fixture:cjcc-ally", Enemy = "fixture:cjcc-enemy", Far = "fixture:cjcc-far", Deck = "fixture:cjcc-deck";

    public static void NativeBottomCostMultiDodgeAndOriginalWholeTurnMaterialPromise()
    {
        var s = Scene.Start();
        var cost = s.V().Hand.Where(c => c.Kind == CardKind.Indulgence).Take(2).Select(c => c.Id).ToArray();
        s.Use(Tousui, "native-bottom-slash", cost, [3]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var issued = s.E<TousuiUseIssuedEvent>().Single();
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId);
        s.Need(issued.MaterialCount == 2 && use.CardAttack is { RequiredDodgeResponses: 2 } && use.Tousui is { BottomPaid: true } &&
            cost.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Use) == 1) &&
            cost.All(id => !s.G.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.Processing)),
            "Tousui pays two actual mixed-suit Hand entities to the native bottom and requires two Dodges on that one real Slash.");
        s.Need(s.E<XumingDamageIncreasedEvent>() is [{ Before: 1, After: 2 }] && s.E<XumingPromiseIssuedEvent>().Length == 0,
            "Bottom-payment entities are costs of the virtual Slash, so Xuming increases its one damage attempt rather than inventing a physical-card debt.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.V(3).Hp == 6 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1 &&
            cost.All(id => !s.G.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile)) &&
            !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash || a.SkillId == Tousui),
            "The single native Slash returns once with its genuine +1 damage; bottom costs never become discards and the ordinary Slash quota remains spent.");

        var debt = Scene.Start();
        var slash = debt.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        debt.NativeSlash(3); debt.Reach(() => debt.P?.SkillPrompt?.SkillId == Watch);
        var promise = debt.E<XumingPromiseIssuedEvent>().Single();
        debt.Need(promise.ActorSeat == 3 && promise.TargetSeat == 0 && promise.MaterialCount == 1 &&
            debt.E<XumingMaterialFrozenEvent>().Single().CardId == slash && debt.E<XumingDamageIncreasedEvent>().Length == 0,
            "A genuine physical Slash freezes its whole original material group as one counterparty promise without increasing damage.");
        debt.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); debt.Play();
        debt.Use(Driver, "lose-xuming", [], []); debt.Play();
        debt.Need(debt.G.CardMovements.Count(m => m.CardId == slash && m.To == CardLocation.DiscardPile) == 1 &&
            !debt.G.CardMovements.Any(m => m.CardId == slash && m.To == CardLocation.Hand(3)),
            "Xuming issues an obligation without prematurely obtaining the spent damage card, including after source loss.");
        debt.Accept(new EndPlayPhaseCommand(0, debt.G.Revision, debt.P!.PromptId));
        debt.Reach(() => debt.G.CreateSnapshot(3).PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "xuming-issued-use") == true);
        var pending = debt.G.CreateSnapshot(3).PendingDecision!;
        debt.Need(pending.Choices.All(c => c.Cards.SequenceEqual([slash]) && c.Targets.SequenceEqual([0])) &&
            debt.E<XumingNativeUseIssuedEvent>().Length == 0,
            "At the original actual turn end the actual other participant receives legal complete-material Dismantlement plans; no weapon means no fabricated Borrowed Sword plan.");
        debt.Cold(); debt.Reach(() => debt.E<XumingCompletedEvent>().Length == 1);
        var native = debt.E<XumingNativeUseIssuedEvent>().Single();
        debt.Need(native.ActorSeat == 3 && native.TargetSeat == 0 && native.Kind == CardKind.Dismantlement && native.MaterialCount == 1 &&
            debt.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == native.UseId && e.SourceSeat == 3 && e.CardKind == CardKind.Dismantlement) == 1,
            "The issued original-source obligation makes one native counterparty Dismantlement from the complete original material set. " + JsonSerializer.Serialize(native));
        debt.Need(debt.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == native.UseId) == 1 &&
            debt.E<XumingNativeUseReturnedEvent>().Count(e => e.UseId == native.UseId) == 1 && debt.E<XumingPromiseSettledEvent>() is [{ Used: true }],
            "The native trick and original source-loss promise each return exactly once. " + JsonSerializer.Serialize(new { Finished = debt.E<CardUseFinishedEvent>(), Returned = debt.E<XumingNativeUseReturnedEvent>(), Settled = debt.E<XumingPromiseSettledEvent>() }));
        var originalPayment = debt.G.CardMovements.Single(m => m.CardId == slash && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing).Sequence;
        debt.Need(debt.G.CardMovements.Count(m => m.CardId == slash && m.From == CardLocation.DiscardPile && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            debt.G.CardMovements.Count(m => m.CardId == slash && m.To == CardLocation.DiscardPile) == 2 &&
            !debt.G.CardMovements.Any(m => m.CardId == slash && m.Sequence > originalPayment && m.To.Zone == CardZoneKind.Hand),
            "After the genuine original payment, Xuming reuses the Discard entity once and returns it once, without inventing an intermediate Hand gain. " + JsonSerializer.Serialize(debt.G.CardMovements.Where(m => m.CardId == slash)));


        var forced = Scene.Start(incomingBorrowed: true);
        var weapon = forced.V().Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        var incoming = forced.V(2).Hand.Single(c => c.Kind == CardKind.BorrowedSword).Id;
        forced.Accept(new EndPlayPhaseCommand(0, forced.G.Revision, forced.P!.PromptId));
        forced.Reach(() => forced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "tousui-request-open") == true);
        var borrowed = forced.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardKind == CardKind.BorrowedSword);
        forced.Answer(c => c.Parameters.GetValueOrDefault("response") == "tousui-request-open");
        forced.Reach(() => forced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "tousui-request-pick") == true);
        var selected = forced.V().Hand.Where(c => c.Kind == CardKind.Indulgence).Take(2).Select(c => c.Id).ToArray();
        forced.Answer(c => c.Cards.SequenceEqual([selected[0]]) && c.Parameters.GetValueOrDefault("branch") == "card");
        forced.Need(!forced.G.CardMovements.Any(m => selected.Contains(m.CardId) && m.From == CardLocation.Hand(0)),
            "Opening a genuine Borrowed Sword Use request and privately selecting a material never pays it early.");
        forced.Cold();
        forced.Answer(c => c.Cards.SequenceEqual([selected[1]]) && c.Parameters.GetValueOrDefault("branch") == "card");
        forced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        forced.Reach(() => forced.P?.SkillPrompt?.SkillId == Watch);
        var requestedUse = forced.E<TousuiUseIssuedEvent>().Single();
        forced.Need(forced.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == requestedUse.UseId).CardAttack is { RequiredDodgeResponses: 2 } &&
            forced.E<TousuiRequestMaterialSelectedEvent>().Length == 2 && forced.E<TousuiRequestFinishedEvent>() is [{ Accepted: true, MaterialCount: 2 }] &&
            forced.E<TousuiBottomPaidEvent>().Single().Count == 2,
            "The restored private request accepts its complete two-card payment once and issues the real native forced Slash with two required Dodges.");
        forced.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        forced.Reach(() => forced.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == borrowed.Id));
        forced.Need(forced.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == requestedUse.UseId) == 1 &&
            forced.E<BorrowedSwordResolvedEvent>().Single(e => e.ResolutionId == borrowed.Id) is { UsedSlash: true, WeaponOwnerSeat: 0, TransferredWeaponCardId: null } &&
            forced.G.CardMovements.Count(m => m.CardId == incoming && m.From == CardLocation.Hand(2) && m.To == CardLocation.Processing) == 1 &&
            selected.All(id => forced.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile) == 1) &&
            forced.V().Equipment.Any(c => c.Id == weapon),
            "Native forced Tousui returns both real parent and child Uses once, preserves the original weapon and never repeats its bottom cost after the selection checkpoint.");
    }

    private sealed class Fixture(bool incomingBorrowed) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-cheng-ji-cheng-cui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cheng-ji-cheng-cui");
            var borrowed = CardCatalog.Get(CardKind.BorrowedSword);
            b.AddCard(new("fixture:cjcc-borrowed-sword", borrowed.DisplayName, borrowed.CategoryName, borrowed.Description, CardKind.BorrowedSword));
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"lose-xuming","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Xuming}}"],"sourceBind":"standard:none"}]}]},
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"original-damage","window":"beforeDamageApplied","subject":"damageSource","priority":-1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage","options":[{"id":"continue"}]}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var presentations = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实失源", description = "原生失技" },
                [Watch] = new { name = "真实伤害边界", description = "原生待结算伤害", optionLabels = new Dictionary<string, string> { ["continue"] = "继续原生伤害" } },
                [Quiet] = new { name = "静默", description = "跳过无关出牌" }
            };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "原生固定小局") { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Owner, "成济成倅本人", "", "standard:none", "wei", 6, [Tousui, Xuming, Driver, Watch]));
            foreach (var id in new[] { Ally, Enemy, Far }) b.AddGeneral(new(id, id, "", "standard:none", "qun", 8, id == Enemy && incomingBorrowed ? [] : [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:indulgence", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "成济成倅原生团队小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine g, ContentRegistry r)
    {
        public GameEngine G { get; private set; } = g;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string skill, string id, int[] cards, int[] targets) => Accept(new UseProgramSkillCommand(0, skill, id, cards, targets, G.Revision, P!.PromptId));
        public void NativeSlash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> pred) { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(pred).Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 120; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Unexpected native cutoff.");
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected Cheng Ji Cheng Cui native prompt: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Missing bounded native Cheng Ji Cheng Cui boundary: " + JsonSerializer.Serialize(G.ResolutionStack));
        }
        public void Cold()
        {
            var view = SnapshotJson.Serialize(G.CreateSnapshot(0)); var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), r);
            Need(view == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements), "The actual pending receipt restores without duplicate native cost or return.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Message); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, G.State.Winner, P, FrameTypes = G.ResolutionStack.Select(f => f.GetType().Name), Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.HandCount }) })); }
        public static Scene Start(bool incomingBorrowed = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(incomingBorrowed));
            static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Spade) => new(kind, suit, 7);
            var setup = new ShanheBattleSetup(83, 1, "cjcc-main", Owner, 6, 0, 0, 0, 0, [],
                [C(CardKind.Indulgence, Suit.Heart), C(CardKind.Indulgence, Suit.Diamond), C(CardKind.Slash), C(CardKind.Indulgence, Suit.Club)], incomingBorrowed ? [C(CardKind.Crossbow)] : [],
                [new(Enemy) { StartingHand = incomingBorrowed ? [C(CardKind.BorrowedSword)] : [C(CardKind.Indulgence)] }, new(Far) { StartingHand = [C(CardKind.Indulgence)] }],
                [new(Ally) { StartingHand = [C(CardKind.Indulgence)] }], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
