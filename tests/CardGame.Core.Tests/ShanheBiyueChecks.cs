using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheBiyueChecks
{
    private const string Skill = "shanhe:s5-biyue-14213", Boss = "shanhe:s5-diao-chan-biyue", Liuli = "boundary:liuli";
    private const string Driver = "fixture:biyue-driver", Gain = "fixture:biyue-gain", Cost = "fixture:biyue-cost";
    private const string Ending = "fixture:biyue-ending", Start = "fixture:biyue-start", Foreign = "fixture:biyue-foreign";
    private const string HpGate = "fixture:biyue-hp-source", Insert = "fixture:biyue-insert";
    private const string Pool = "ol:shanduan", PoolGrant = "fixture:biyue-expiring-pool";
    private const string Owner = "fixture:biyue-owner", Attacker = "fixture:biyue-attacker", Peer = "fixture:biyue-peer", Deck = "fixture:biyue-deck";
    private const string DrawReason = "skill-program." + Skill + ".Draw", CostReason = "skill-program." + Liuli + ".SelectAndMoveOwnedCard";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-biyue-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new(Boss)], [], new string('a', 64))));

    public static void EmptyHandRefillsLiveLimitAndNativeGainChildrenCold()
    {
        VerifyActualRecipeAndLoaderBoundaries();
        var s = Scene.Create("empty");
        Need(s.V() is { Hp: 4, HandCount: 0, HandLimit: 6 }, "The real empty-hand owner begins with a public two-point hand-limit modifier.");
        s.Use("lose-one");
        Need(s.V() is { Hp: 3, HandCount: 0, HandLimit: 5 }, "An actual HP loss changes the live hand limit before Ending; the refill cannot use printed maximum HP or a stale opening limit.");
        var turn = s.G.State.TurnNumber; s.EndPlay(); s.At(Gain);
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Skill);
        var child = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Gain);
        var materials = s.Draws().Select(move => move.CardId).ToArray();
        Need(materials.Length == 5 && materials.Distinct().Count() == 5 && s.V().HandCount == 5 &&
            child.WindowContext?.MovementBatch is { } batch && batch.Movements.Any(move => move.To == CardLocation.Hand(0) && move.Reason.Value == DrawReason) &&
            s.G.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(frame => frame.OwnerSeat == 0 && frame.TurnNumber == turn) &&
            s.E<ProgramTurnSkillsGrantedEvent>().All(item => item.SkillId != Skill) && !s.Has(Liuli),
            "The empty branch really moves five original deck entities, then pauses its real gain child under the owning Ending program, without granting Liuli after the refill: " + s.Diagnostic());
        s.Cold(); s.Continue(); s.At(Ending);
        Need(s.Draws().Select(move => move.CardId).SequenceEqual(materials) &&
            s.E<ProgramBindingResolvedEvent>().Count(item => item.FrameId == root.Id && item.Completed) == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(item => item.SkillId == Skill) == 1 &&
            s.E<ProgramTurnSkillsGrantedEvent>().All(item => item.SkillId != Skill),
            "Cold gain-child return neither redraws the frozen five entities nor revisits the nonempty branch of the same mandatory Ending binding.");
        s.Cold();

        var zero = Scene.Create("zero");
        Need(zero.V() is { HandCount: 0, HandLimit: 0 }, "The second actual owner has a zero live hand limit, not a fabricated negative draw.");
        zero.EndPlay(); zero.At(Ending);
        Need(zero.Draws().Length == 0 && !zero.Has(Liuli) &&
            zero.E<ProgramBindingResolvedEvent>().Single(item => item.SkillId == Skill) is { Activated: true, Completed: true },
            "An empty hand at zero limit completes mandatory Biyue without inventing a card or falling into the nonempty grant branch.");
        zero.Cold();
    }

    public static void NonemptyGrantRedirectsPhysicalSlashAndExpiresAtNextActualTurnCold()
    {
        var s = Scene.Create("redirect"); var originalTurn = s.G.State.TurnNumber;
        Need(!s.Has(Liuli) && s.V().HandCount == 1, "The nonempty Ending premise owns one real cost card and no preexisting Liuli.");
        s.EndPlay(); s.At(Ending); var grant = s.BiyueGrant(); AssertGrant(grant, originalTurn);
        Need(s.Has(Liuli) && s.Draws().Length == 0 && s.V().HandCount == 1 &&
            s.E<ProgramBindingResolvedEvent>().Single(item => item.SkillId == Skill) is { Activated: true, Completed: true },
            "The locked nonempty branch issues one exact next-owner-turn grant without drawing or asking for an optional activation.");
        s.Cold(); s.Continue(); s.At(Foreign);
        Need(s.G.State.CurrentSeat == 1 && s.G.State.TurnNumber > originalTurn && s.Has(Liuli) && s.Expiry().Length == 0,
            "Another character's actual turn does not expire a grant whose next-start recipient is the original owner.");
        s.Cold(); s.Continue(); s.SelectRequestedAttacker(); s.ResolveRedirect();
        Need(s.Has(Liuli) && s.Expiry().Length == 0 && s.E<TurnStartedEvent>().Last().ActorSeat == 1,
            "The paid native Liuli remains enabled throughout the real foreign turn after its exact Slash and discard children return.");
        s.Continue(); s.NextOwnerStart(originalTurn);
        AssertExpired(s, grant, shouldRetain: false); s.Cold();
    }

    public static void IndependentSourcesExtraTurnsAndOriginalSourceLossCold()
    {
        var lost = Scene.Create("loss"); var turn = lost.G.State.TurnNumber;
        lost.EndPlay(); lost.At(Ending); var issued = lost.BiyueGrant(); AssertGrant(issued, turn);
        lost.Cold(); lost.Continue(); lost.At(Foreign);
        Need(lost.V().Hp == 3 && !lost.Has(Skill) && lost.Has(Liuli) && lost.Expiry().Length == 0,
            "A real foreign-turn HP-loss child withdraws the original conditional Biyue source, while its already-issued independent Liuli lease survives.");
        lost.Cold(); lost.Continue(); lost.SelectRequestedAttacker(); lost.ResolveRedirect(); lost.Continue(); lost.NextOwnerStart(turn);
        AssertExpired(lost, issued, shouldRetain: false); lost.Cold();

        foreach (var scene in new[] { "extra", "independent" })
        {
            var s = Scene.Create(scene); var creationTurn = s.G.State.TurnNumber;
            Need(s.Has(Liuli) == (scene == "independent"), "Only the independent-source control originally prints Liuli.");
            s.Use("extra-turn"); s.EndPlay(); s.At(Ending); var grant = s.BiyueGrant(); AssertGrant(grant, creationTurn);
            s.Cold(); s.Continue(); s.NextOwnerStart(creationTurn);
            var turns = s.E<TurnStartedEvent>().Where(item => item.TurnNumber > creationTurn).ToArray();
            Need(turns is [{ ActorSeat: 0 }] && s.E<ProgramExtraTurnPendedEvent>().Single() is { Seat: 0 },
                "The existing extra-turn instruction starts a new actual owner turn immediately; expiry cannot wait for a seat cycle or natural round.");
            AssertExpired(s, grant, shouldRetain: scene == "independent"); s.Cold();
            if (scene == "independent")
            {
                var expired = s.Expiry().Single(); s.Continue(); s.Play();
                s.BeginRequest(); s.ResolveRedirect();
                Need(s.E<ProgramBindingStartedEvent>().Single(item => item.SkillId == Liuli).SkillInstanceId == expired.Grant.SkillInstanceId &&
                    s.E<ProgramBindingStartedEvent>().Count(item => item.SkillId == Liuli) == 1 && s.Has(Liuli),
                    "Expiry removes only the Biyue grant: the original printed source keeps the same effective instance and can still pay and redirect a real physical Slash once.");
                s.Cold();
            }
        }
        VerifyInsertedPlayIsNotAnotherActualTurn();
        VerifyExpiredPoolNeverInitializesAtRecipientStart();
    }

    private static void AssertGrant(ProgramTurnSkillsGrantedEvent grant, int turn) => Need(grant is
        { SkillId: Skill, OwnerSeat: 0, RecipientSeat: 0, TurnExpiry: null, NextActualTurnStartExpiry: { RecipientSeat: 0, GrantedTurnOwnerSeat: 0 } expiry } &&
        expiry.GrantedTurnNumber == turn && grant.GrantedSkillIds.SequenceEqual([Liuli]) && !string.IsNullOrEmpty(grant.GrantSourceId),
        "The actual grant freezes its recipient and creation actual turn under a distinct source, rather than expiring at another character's turn or at the current Ending.");

    private static void AssertExpired(Scene s, ProgramTurnSkillsGrantedEvent grant, bool shouldRetain)
    {
        Need(s.Expiry() is [var expired] && expired.RecipientSeat == 0 && expired.TurnOwnerSeat == 0 &&
            expired.TurnNumber > grant.NextActualTurnStartExpiry!.GrantedTurnNumber &&
            expired.Grant.SkillId == Liuli && expired.Grant.SourceId == grant.GrantSourceId &&
            expired.Grant.NextActualTurnStartExpiry == grant.NextActualTurnStartExpiry && s.Has(Liuli) == shouldRetain &&
            s.E<ProgramTurnSkillsGrantedEvent>().Count(item => item.SkillId == Skill) == 1,
            "The next actual recipient TurnStarted expires exactly the issued source before preparation, preserving a separate printed source and never reissuing the completed Ending grant: " + s.Diagnostic());
        var history = s.G.Events.Select(item => item.Payload).ToArray();
        var started = Array.FindLastIndex(history, item => item is TurnStartedEvent start && start.ActorSeat == 0);
        var expiration = Array.FindLastIndex(history, item => item is ProgramNextActualTurnStartSkillGrantExpiredEvent);
        var observer = Array.FindLastIndex(history, item => item is ProgramBindingStartedEvent binding && binding.SkillId == Start);
        Need(started >= 0 && expiration > started && observer > expiration,
            "The expiry is committed after the real owner-start fact and before its preparation observer, with no dependence on a future Play or natural turn.");
    }

    private static void VerifyInsertedPlayIsNotAnotherActualTurn()
    {
        var s = Scene.Create("inserted"); var turn = s.G.State.TurnNumber; var starts = s.E<TurnStartedEvent>().Length;
        var issued = s.E<ProgramTurnSkillsGrantedEvent>().Single(item => item.SkillId == Insert);
        Need(s.Has(Liuli) && issued.NextActualTurnStartExpiry is { RecipientSeat: 0 } &&
            s.E<ProgramPhaseScheduledEvent>().Any(item => item.SkillId == Insert && item.Started),
            "The shared expiry control grants through a real preparation instruction before entering an actual inserted Play.");
        s.Cold(); s.EndPlay(); s.At(Insert);
        Need(s.G.State.TurnNumber == turn && s.E<TurnStartedEvent>().Length == starts && s.Has(Liuli) && s.Expiry().Length == 0,
            "Ending the inserted Play returns to its real parent without a TurnStarted fact or premature lease removal.");
        s.Cold(); s.Continue(); s.At(Start); s.Continue(); s.Play();
        Need(s.G.State.TurnNumber == turn && s.E<TurnStartedEvent>().Length == starts && s.Has(Liuli) && s.Expiry().Length == 0,
            "The same actual turn's normal Play retains the identical leased capability; phases do not approximate actual-turn start.");
        s.Cold();
    }

    private static void VerifyExpiredPoolNeverInitializesAtRecipientStart()
    {
        var s = Scene.Create("pool"); var turn = s.G.State.TurnNumber;
        Need(!s.Has(Pool) && s.E<TurnDefaultStatPoolInitializedEvent>().Length == 0,
            "The complete registered native stat-pool program is initially unowned and has created no durable pool.");
        s.EndPlay(); s.At(Ending);
        var issued = s.E<ProgramTurnSkillsGrantedEvent>().Single(item => item.SkillId == PoolGrant);
        Need(s.Has(Pool) && issued.GrantedSkillIds.SequenceEqual([Pool]) && issued.NextActualTurnStartExpiry == new SkillGrantNextActualTurnStartExpiry(turn, 0, 0) &&
            s.E<TurnDefaultStatPoolInitializedEvent>().Length == 0,
            "A true Ending instruction grants the complete native pool skill after this turn's allocation boundaries, with a precise next-owner-start expiry.");
        s.Cold(); s.Continue(); s.At(Foreign);
        Need(s.G.State.CurrentSeat == 1 && s.Has(Pool) && s.Expiry().Length == 0 && s.E<TurnDefaultStatPoolInitializedEvent>().Length == 0,
            "The real foreign turn retains the grant without initializing a pool for another turn owner.");
        s.Cold(); s.Continue(); s.NextOwnerStart(turn);
        Need(!s.Has(Pool) && s.Expiry() is [{ RecipientSeat: 0, TurnOwnerSeat: 0 } expired] &&
            expired.Grant.SkillId == Pool && expired.Grant.SourceId == issued.GrantSourceId &&
            expired.Grant.NextActualTurnStartExpiry == issued.NextActualTurnStartExpiry && expired.TurnNumber == s.G.State.TurnNumber &&
            s.E<TurnDefaultStatPoolInitializedEvent>().All(item => item.Source.SkillId != Pool),
            "The expired capability cannot initialize a durable stat pool in an earlier BeforeEvent observer; its exact source still receives one real expiry fact: " + s.Diagnostic());
        s.Cold();
    }

    private static void VerifyActualRecipeAndLoaderBoundaries()
    {
        foreach (var difficulty in new[] { 1, 2, 3 })
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(difficulty, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounter = definition.Nodes.Single(node => node.Id == "luoyang").Encounters!.Single(item => item.Id == "luoyang:lvbu-diaochan-s5");
            var member = encounter.Enemies.Single(enemy => enemy.GeneralId == Boss);
            Need(member.MaxHpBonus == difficulty - 1 && member.InitialHandBonus == difficulty - 1 && member.DrawBonus == (difficulty == 3 ? 1 : 0) &&
                member.StartingHand is null && encounter.Enemies.Any(enemy => enemy.GeneralId == "shanhe:s5-lvbu-baji") && encounter.Allies is { Count: 1 } &&
                StandardShanheCampaignCatalog.CreateDefinition(difficulty).Nodes.SelectMany(node => node.Encounters ?? []).All(item => item.Enemies.All(enemy => enemy.GeneralId != Boss)),
                "Only the current-edition local encounter combines this six-HP Diao Chan with Baji Lu Bu and its genuine companion, using the stated local growth and ordinary random opening cards.");
            var setup = new ShanheBattleSetup(83, 1, "luoyang", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, encounter.Allies ?? [], ShanheCampaignContent.GetContentHash(definition));
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var general = registry.Generals[Boss];
            Need(general is { BaseHp: 6, FactionId: "qun", Gender: GeneralGender.Female } && general.SkillIds.Contains(Skill) &&
                general.SkillIds.Count == 2 && general.SkillIds.Contains("shanhe:s5-lijian-68") &&
                registry.GetDeck(registry.Modes[ShanheBattleSetup.BattleModeId].DeckId!).InitialHandSize == 4,
                "The actual source form prints just the S5 Lijian and Biyue on six HP; six denotes HP rather than an invented fixed six-card opening.");
            var game = StandardShanheCampaignCatalog.CreateBattle(setup); var accepted = game.Submit(new StartGameCommand()); Need(accepted.Accepted, accepted.Error?.Message ?? "Actual Diao Chan recipe rejected.");
            var seat = 1 + setup.AllyRoster.Count + encounter.Enemies.ToList().FindIndex(enemy => enemy.GeneralId == Boss); var view = game.CreateSnapshot(seat).Players[seat];
            Need(view.GeneralId == Boss && view.Hp == 6 + difficulty - 1 && view.MaxHp == view.Hp && view.Armor == 0 && view.Equipment.Count == 0 &&
                view.Skills!.Select(item => item.ContentId).Order().SequenceEqual(general.SkillIds.Order()),
                "The catalogue's real engine starts the actual two-skill female form with its local HP growth and no unverified Armor or equipment."); ColdActual(game, registry);
        }
        var old = StandardShanheCampaignCatalog.CreateDefinition(); var oldEncounter = old.Nodes.Single(node => node.Id == "luoyang").Encounters!.First();
        var oldRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(new(83, 1, "luoyang", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], oldEncounter.Enemies, oldEncounter.Allies ?? [], ShanheCampaignContent.GetContentHash(old)));
        Need(!oldRegistry.Skills.ContainsKey(Skill) && !oldRegistry.Generals.ContainsKey(Boss), "An actual 2024 recipe does not import this dedicated S5 skill or form.");
        var assembly = typeof(StandardShanheS5BossPackage).Assembly;
        string Read(string suffix) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("shanhe-s5-biyue.rules.json"); var presentation = Read("shanhe-s5-biyue.presentation.json");
        foreach (var edit in new Action<JsonObject>[] { effect => effect["expiry"] = "nextCharacterTurn", effect => effect["target"] = "selectedTarget" })
        {
            var copy = JsonNode.Parse(rules)!; edit(copy["skills"]![0]!["triggers"]![0]!["effects"]![0]!.AsObject());
            var rejected = false; try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { rejected = true; }
            Need(rejected, "The new explicit recipient-start expiry rejects unknown time domains and unsupported recipients rather than guessing a turn number.");
        }
        Need(!JsonSerializer.Serialize(new SkillGrant("plain", "standard:none", "plain", "plain")).Contains(nameof(SkillGrant.NextActualTurnStartExpiry), StringComparison.Ordinal),
            "Ordinary grants omit the additive nullable expiry field instead of changing their serialized shape.");
    }

    private static string Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action") ?? "";
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements, Facts = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())), Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static GameEngine ColdActual(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(State(game) == State(restored), "Cold replay preserves real entities, exact source-owned grants, expiry and the original parent/child cursors without paying again."); return restored;
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(item => item is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(item => item.Payload).OfType<T>().ToArray();
        public bool Has(string id) => V().Skills?.Any(item => item.ContentId == id) == true;
        public CardMovementRecord[] Draws() => G.CardMovements.Where(move => move.Reason.Value == DrawReason && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)).ToArray();
        public ProgramTurnSkillsGrantedEvent BiyueGrant() => E<ProgramTurnSkillsGrantedEvent>().Single(item => item.SkillId == Skill);
        public ProgramNextActualTurnStartSkillGrantExpiredEvent[] Expiry() => E<ProgramNextActualTurnStartSkillGrantExpiredEvent>();
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual Biyue command rejected."); }
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Continue() => Answer(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Use(string id) { Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId)); Play(); }
        public void BeginRequest() => Accept(new UseProgramSkillCommand(0, Driver, "request", [], [1], G.Revision, P!.PromptId));
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void At(string skill) => Reach(() => P?.PlayerSeat == 0 && P.SkillPrompt?.SkillId == skill && P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void NextOwnerStart(int after) => Reach(() => G.State.TurnNumber > after && G.State.CurrentSeat == 0 && P?.SkillPrompt?.SkillId == Start);
        public void SelectRequestedAttacker()
        {
            Reach(() => P?.SkillPrompt?.SkillId == Foreign && P.Choices.Any(choice => Action(choice) == "select-target"));
            Answer(choice => choice.Targets.SequenceEqual([1]));
        }
        public void ResolveRedirect()
        {
            Reach(() => P is { PlayerSeat: 0, SkillPrompt.SkillId: Liuli } && P.Choices.Any(choice => Action(choice) == "activate"));
            var source = E<CardUseDeclaredEvent>().Last(item => item.SourceSeat == 1 && item.CardKind == CardKind.Slash); var cost = V().Hand.Single().Id;
            var damageBefore = E<DamageAppliedEvent>().Length;
            Need(G.GetCombatDistance(0, 2) == 2 && G.GetCombatDistance(0, 3) == 1, "The native four-seat distance premise keeps seat two out of the recipient's one-range Liuli and seat three in range.");
            Cold(); Answer(choice => Action(choice) == "activate");
            Reach(() => P?.SkillPrompt?.SkillId == Liuli && P.Choices.Any(choice => Action(choice) == "select-target"));
            Need(P!.Choices.Any(choice => choice.Targets.SequenceEqual([3])) && P.Choices.All(choice => !choice.Targets.Contains(1) && !choice.Targets.Contains(2)),
                "Real Liuli excludes both the actual physical Slash source and the owner-out-of-range seat, rather than widening range to match the attacker's legal targets.");
            Cold(); Answer(choice => choice.Targets.SequenceEqual([3]));
            Reach(() => P?.SkillPrompt?.SkillId == Liuli && P.Choices.Any(choice => choice.Cards.Contains(cost)));
            Cold(); Answer(choice => choice.Cards.Contains(cost)); At(Cost);
            Need(G.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile && move.Reason.Value == CostReason) == 1 &&
                E<DamageAppliedEvent>().Length == damageBefore,
                "The actual owned-card cost is paid exactly once before its movement child; the original physical attack has not yet paid damage.");
            Cold(); Continue();
            Reach(() => E<CardUseFinishedEvent>().Any(item => item.ResolutionId == source.ResolutionId));
            Need(E<DamageAppliedEvent>().Skip(damageBefore).ToArray() is [{ SourceSeat: 1, TargetSeat: 3, Amount: 1 }] &&
                E<CardUseFinishedEvent>().Count(item => item.ResolutionId == source.ResolutionId) == 1 &&
                G.CardMovements.Count(move => move.CardId == source.CardId && move.From == CardLocation.Hand(1) && move.To == CardLocation.Processing) == 1 &&
                G.CardMovements.Count(move => move.CardId == cost && move.Reason.Value == CostReason) == 1,
                "The native requested Slash pays its original material once, redirects only to the valid recipient and finishes once after the real discard child.");
            if (scene is "redirect" or "loss") At(Foreign); else Play();
            Cold();
        }
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 380; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } prompt)
                {
                    if (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected actual Biyue boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The bounded Biyue scenario ended before its true boundary: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded actual Biyue boundary: " + Diagnostic());
        }
        public void Cold() { G = ColdActual(G, registry); var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every real card remains in exactly one native zone."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase,
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Actions = P.Choices.Take(4).Select(Action) },
            Owner = new { V().Hp, V().HandCount, V().HandLimit, Skills = V().Skills?.Select(item => item.ContentId) },
            Grants = E<ProgramTurnSkillsGrantedEvent>(), Expired = Expiry(), Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) });
        public static Scene Create(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            ShanheStartingCard[] cards = scene is "empty" or "zero" ? [] : [new(CardKind.Dodge, Suit.Heart, 2)];
            var setup = new ShanheBattleSetup(83, 1, "fixture-biyue-" + scene, Owner, 4, 0, 0, 0, 0, [], cards, [],
                [new(Attacker) { StartingHand = scene == "pool" ? [] : [new(CardKind.Slash, Suit.Spade, 7)] }, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [], new string('a', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(game, registry, scene); s.Accept(new StartGameCommand());
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } || s.P?.SkillPrompt?.SkillId == Start);
            if (s.P?.SkillPrompt?.SkillId == Start) { s.Continue(); s.Play(); }
            return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-biyue", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Skill, Liuli }) b.AddSkill(Production.Value.GetSkill(id));
            if (scene == "pool") CharacterFixtureContent.Register(b, "ol:zhou-chu");
            object Pause(string bind) => new { op = "chooseOption", target = "owner", resultBind = bind, options = new[] { new { id = "continue" } } };
            object A(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets,
                targetKind = "otherLiving", usesPerTurn = (int?)null, effects };
            var handLimitModifiers = new[] { new { id = "public-limit", query = "handLimit", operation = "add", value = scene == "empty" ? 2 : -9, priority = 0 } }
                .Where(_ => scene is "empty" or "zero").ToArray();
            var definitions = new List<object> {
                new { id = Driver, revision = 1, modifiers = handLimitModifiers,
                    activations = new[] { A("lose-one", 0, new { op = "loseHp", target = "owner", amount = 1 }),
                        A("extra-turn", 0, new { op = "pendExtraTurn", target = "owner" }),
                        A("request", 1, new { op = "requestSlashByTarget", target = "selectedTarget", resultBind = "requested-physical" }) } },
                new { id = Gain, revision = 1, triggers = new[] { new { id = "paid-refill-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                    movementReasons = new[] { DrawReason }, movementOccurrence = "perBatch", usageScope = "game", usageLimit = 1, optional = false, effects = new[] { Pause("gain-child") } } } },
                new { id = Cost, revision = 1, triggers = new[] { new { id = "real-liuli-cost-child", window = "cardsMoved", subject = "owner", sourceZones = new[] { "hand" },
                    movementReasons = new[] { CostReason }, movementOccurrence = "perBatch", optional = false, effects = new[] { Pause("cost-child") } } } },
                new { id = Ending, revision = 1, triggers = new[] { new { id = "after-real-biyue", window = "turnEnding", subject = "owner", optional = false, priority = -1000, effects = new[] { Pause("ending-child") } } } },
                new { id = Start, revision = 1, triggers = new[] { new { id = "after-expiry-before-preparation", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false, priority = -1000, effects = new[] { Pause("start-child") } } } },
                new { id = HpGate, revision = 1, hpSkillGrants = new[] { new { id = "healthy-biyue-source", skillId = Skill, minimumHp = 4 } } }
            };
            if (scene is "redirect" or "loss") definitions.Add(new { id = Foreign, revision = 1, triggers = new[] { new { id = "actual-foreign-play", window = "playPhaseStarting", subject = "owner", turnOwnerScope = "otherLiving", optional = false,
                usageScope = "game", usageLimit = 1, effects = (scene == "loss" ? new object[] { new { op = "loseHp", target = "owner", amount = 1 } } : Array.Empty<object>()).Concat(new object[] {
                    Pause("foreign-ready"), new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "requestSlashByTarget", target = "selectedTarget", resultBind = "foreign-physical" }, Pause("foreign-return") }).ToArray() } } });
            if (scene == "inserted") definitions.Add(new { id = Insert, revision = 1, triggers = new[] { new { id = "real-same-turn-extra-play", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false,
                usageScope = "game", usageLimit = 1, priority = 100, effects = new object[] { new { op = "grantTurnSkills", target = "owner", skillIds = new[] { Liuli }, expiry = "recipientNextActualTurnStart" },
                    new { op = "insertPhase", target = "owner", phase = "play", phaseContinuation = "beforeNormalPreparation" }, Pause("insert-return") } } } });
            if (scene == "pool")
            {
                definitions.Add(new { id = PoolGrant, revision = 1, triggers = new[] { new { id = "real-ending-pool-grant", window = "turnEnding", subject = "owner", optional = false,
                    effects = new[] { new { op = "grantTurnSkills", target = "owner", skillIds = new[] { Pool }, expiry = "recipientNextActualTurnStart" } } } } });
                definitions.Add(new { id = Foreign, revision = 1, triggers = new[] { new { id = "observe-real-foreign-play", window = "playPhaseStarting", subject = "owner", turnOwnerScope = "otherLiving", optional = false,
                    usageScope = "game", usageLimit = 1, effects = new[] { Pause("foreign-pool-held") } } } });
            }
            var ids = new[] { Driver, Gain, Cost, Ending, Start, HpGate }.Concat(scene is "redirect" or "loss" ? [Foreign] : scene == "inserted" ? [Insert] : scene == "pool" ? [Foreign, PoolGrant] : []).ToArray();
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "真实结束、原牌支付和实际回合期限", optionLabels =
                id is Gain or Cost or Ending or Start or Foreign or Insert ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var skills = new List<string> { Gain, Cost, Ending, Start }; if (scene != "pool") skills.Add(scene == "loss" ? HpGate : Skill);
            if (scene is "redirect" or "loss") skills.Add(Foreign); if (scene == "inserted") skills.Add(Insert); if (scene == "independent") skills.Add(Liuli);
            if (scene == "pool") skills.AddRange([PoolGrant, Foreign]);
            b.AddGeneral(new(Owner, "实际闭月本人", "", Driver, "qun", 4, skills, GeneralGender.Female));
            b.AddGeneral(new(Attacker, "真实持杀来源", "", "standard:none", "wei", 20)); b.AddGeneral(new(Peer, "真实其他角色", "", "standard:none", "wei", 20));
            b.AddDeck(new(Deck, "无自然补牌的小真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实闭月与下一本角色回合", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Attacker, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
