using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheObservedTacticChecks
{
    private const string Neihong = "shanhe:neihong", Ruoxi = "shanhe:ruoxi", Driver = "fixture:observed-tactic-driver";
    private const string Owner = "fixture:observed-tactic-owner", Quiet = "fixture:observed-tactic-quiet";
    private const string Watch = "fixture:observed-tactic-chain-watch", IdentityMode = "identity:classic-observed-tactic";
    private const string Deck = "fixture:observed-tactic-deck", IdentityDeck = "fixture:observed-tactic-identity-deck";
    private const int Seats = 5;

    public static void ThirdPartyNeihongObservesActualDuelsExcludesOtherDamageAndLosesNativeInstancesCold()
    {
        var s = Scenario.Start();
        s.Begin("duel", targets: [1, 2]);
        s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 2 });
        s.Cold();
        s.Play();
        s.Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 1, TargetSeat: 2, Amount: 2, SourceLess: false }] &&
            s.Events<DuelResponseEvent>().Count(e => e.UsedSlash) == 2,
            "The two actual enemy Duel participants exchange physical Slashes; a third-party holder increases their native damage from one to two.");
        var first = s.Events<ProgramCardDamageModifiedEvent>().Single(e => e.Source.SkillId == Neihong);
        s.Need(first.Source.OwnerSeat == 0 && first.TargetSeat == 2 && first.BaseAmount == 1 && first.ModifiedAmount == 2,
            "The modifier retains the actual observing owner and independent skill instance, rather than attributing it to either Duel participant.");

        var count = s.Events<DamageAppliedEvent>().Length;
        s.Begin("duel", targets: [1, 0]);
        s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage"); s.Play();
        s.Need(s.Events<DamageAppliedEvent>().Skip(count).ToArray() is [{ SourceSeat: 1, TargetSeat: 0, Amount: 1 }],
            "A real enemy-to-holder Duel is not enemy-on-enemy damage.");
        count = s.Events<DamageAppliedEvent>().Length;
        s.Use("enemy-self", targets: [2]);
        s.Use("incoming", targets: [1]);
        s.Use("source-less");
        var excluded = s.Events<DamageAppliedEvent>().Skip(count).ToArray();
        s.Need(excluded is [{ SourceSeat: 2, TargetSeat: 2, Amount: 1, SourceLess: false },
                { SourceSeat: 1, TargetSeat: 0, Amount: 1, SourceLess: false },
                { TargetSeat: 0, Amount: 1, SourceLess: true }] &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Neihong) == 1,
            "Genuine enemy self-damage, incoming participant-reference damage and source-less received damage do not acquire the observer bonus.");
        s.Use("lose-neihong");
        s.Need(s.V(0).Skills?.Any(skill => skill.ContentId == Neihong) != true,
            "The native skill-loss operation removes the actual observing instance.");
        count = s.Events<DamageAppliedEvent>().Length;
        s.Use("duel", targets: [1, 2]);
        s.Need(s.Events<DamageAppliedEvent>().Skip(count).ToArray() is [{ SourceSeat: 1, TargetSeat: 2, Amount: 1 }] &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Neihong) == 1,
            "A later native Duel cannot read a stale observer binding after skill loss.");
        s.Cold();

        var twice = Scenario.Start(copies: 2);
        twice.Use("duel", targets: [1, 2]);
        var modifiers = twice.Events<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId == Neihong).ToArray();
        twice.Need(twice.Events<DamageAppliedEvent>() is [{ SourceSeat: 1, TargetSeat: 2, Amount: 3 }] &&
            modifiers.Length == 2 && modifiers.All(e => e.Source.OwnerSeat == 0) &&
            modifiers.Select(e => e.Source.SkillInstanceId).Distinct(StringComparer.Ordinal).Count() == 2,
            "Printed ownership and an independent campaign tactic grant each contribute once through their actual distinct instance IDs.");
        twice.Cold();
    }

    public static void ObservedFireChainRechecksFrozenOriginBaseAndSilverLionCold()
    {
        // These amounts follow this engine's preserved chain interpretation;
        // the original Neihong source does not demonstrate these exact examples.
        foreach (var armorSeat in new[] { -1, 2, 3 })
        {
            var s = Scenario.Start(chain: true);
            s.Equip(CardKind.CrimsonSoulZither, 1);
            if (armorSeat >= 0) s.Equip(CardKind.SilverLion, armorSeat);
            foreach (var seat in new[] { 0, 2, 3, 4 }) s.Use("chain", targets: [seat]);
            s.Begin("duel", targets: [1, 2]);
            s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
            var frozen = armorSeat == 2 ? 1 : 2;
            s.Need(s.Events<DamageAppliedEvent>().Select(e => (e.SourceSeat, e.TargetSeat, e.Amount, e.Nature))
                    .SequenceEqual([(1, 2, frozen, DamageNature.Fire), (1, 0, frozen, DamageNature.Fire)]) &&
                s.Events<RecipientScopedDamageBaseEvent>() is [{ FirstTargetSeat: 2, Amount: var basis }] && basis == frozen,
                "A real equipped zither turns the actual enemy Duel into fire damage; the first finalized amount, including its Silver Lion cap, becomes the single propagation base.");
            s.Cold();
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
            var expected = new[] { (1, 2, frozen), (1, 0, frozen),
                (1, 3, armorSeat == 3 ? 1 : frozen + 1), (1, 4, frozen + 1) };
            var hits = s.Events<DamageAppliedEvent>();
            s.Need(hits.Select(e => (e.SourceSeat, e.TargetSeat, e.Amount)).SequenceEqual(expected) &&
                hits.All(e => e.Nature == DamageNature.Fire && !e.SourceLess) &&
                new[] { 0, 2, 3, 4 }.All(seat => !s.V(seat).IsChained),
                "Each enemy recipient rechecks Neihong from the same frozen base; a friendly recipient receives only the base, and a middle Silver Lion does not rewrite later recipients' base.");
            s.Need(s.Events<RecipientScopedDamageAdvancedEvent>().Select(e => (e.TargetSeat, e.Amount))
                    .SequenceEqual([(0, frozen), (3, frozen), (4, frozen)]) &&
                s.Events<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId == Neihong)
                    .Select(e => (e.Source.OwnerSeat, e.TargetSeat)).SequenceEqual([(0, 2), (0, 3), (0, 4)]),
                "The issued base advances once per actual recipient and every increased segment names the third-party skill source; amounts never roll forward to four.");
            s.Cold();
        }
        VerifyDormantObserverLeavesLegacySourceChain(loseObserver: false);
        VerifyDormantObserverLeavesLegacySourceChain(loseObserver: true);
        VerifyIneligibleActiveObserverLeavesLegacySourceChain(publicTeams: true);
        VerifyIneligibleActiveObserverLeavesLegacySourceChain(publicTeams: false);
        VerifyIssuedObserverBaseSurvivesSkillLoss();
        VerifyIssuedObserverBaseSurvivesDeath();
        VerifyConsecutiveVirtualDuelsKeepSeparateChainOccurrences();
    }

    private static void VerifyConsecutiveVirtualDuelsKeepSeparateChainOccurrences()
    {
        var s = Scenario.Start();
        s.Equip(CardKind.CrimsonSoulZither, 1);
        s.Begin("two-chained-duels", targets: [1, 2]);
        s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 2 });
        s.Cold();
        s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 1 });
        var paid = s.Events<DuelResponseEvent>().Single(e => e.UsedSlash);
        s.Need(paid.ResponderSeat == 2 && paid.SlashCardId is not null,
            "The first Duel really pays its target's physical Slash before the second ordered participant responds.");
        s.Cold(); s.Play();
        var uses = s.Events<CurrentTurnCardUseKindsRecordedEvent>()
            .Where(e => e.ActorSeat == 1 && e.ProgramInstructionIndex is not null).ToArray();
        var bases = s.Events<RecipientScopedDamageBaseEvent>();
        var responses = s.Events<DuelResponseEvent>().Where(e => e.UsedSlash).ToArray();
        s.Need(uses.Length == 2 && uses.Select(e => e.OriginFrameId).Distinct().Count() == 1 &&
            uses.Select(e => e.ProgramInstructionIndex).Distinct().Count() == 2 &&
            uses.Select(e => e.WholeActualTurnUseSequence).SequenceEqual(new int?[] { 1, 2 }),
            "Two Duel instructions inside one real activation retain one owning program frame but occupy two independent actual-use ordinals.");
        s.Need(bases.Length == 2 && bases.All(e => e.FirstTargetSeat == 2 && e.Amount == 2 && e.PublicEnemyObserver) &&
            bases.Select(e => e.AttackFrameId).Distinct().Single() == uses[0].OriginFrameId &&
            bases.Select(e => e.ProgramUseInstructionIndex).SequenceEqual(uses.Select(e => e.ProgramInstructionIndex)) &&
            s.Events<RecipientScopedDamageAdvancedEvent>().Select(e => e.ProgramUseInstructionIndex)
                .SequenceEqual(uses.Select(e => e.ProgramInstructionIndex)) &&
            s.Events<ChainedDamagePropagatedEvent>().Select(e => e.ProgramUseInstructionIndex)
                .SequenceEqual(uses.Select(e => e.ProgramInstructionIndex)) &&
            s.Events<RecipientScopedDamageAdvancedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 2), (1, 2)]) &&
            s.Events<DamageAppliedEvent>().Select(e => (e.SourceSeat, e.TargetSeat, e.Amount, e.Nature))
                .SequenceEqual([(1, 2, 2, DamageNature.Fire), (1, 1, 2, DamageNature.Fire),
                    (1, 2, 2, DamageNature.Fire), (1, 1, 2, DamageNature.Fire)]),
            "Each Duel re-chains the selected pair and issues its own first-recipient base; the later propagation to its source cannot collide with the earlier same-frame chain facts.");
        s.Need(responses.Length == 2 && responses.Select(e => e.SlashCardId).Distinct().Count() == 2 &&
            responses.Any(e => e.SlashCardId == paid.SlashCardId) &&
            responses.All(e => s.G.CardMovements.Count(m => m.CardId == e.SlashCardId &&
                m.From == CardLocation.Hand(e.ResponderSeat) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == e.SlashCardId && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile) == 1) &&
            !s.V(1).IsChained && !s.V(2).IsChained,
            "The already-paid first Duel responses survive replay exactly once; the second Duel uses the genuinely exhausted hands and performs a second complete elemental unchaining.");
        s.Cold();
    }

    private static void VerifyDormantObserverLeavesLegacySourceChain(bool loseObserver)
    {
        var s = Scenario.Start(copies: loseObserver ? 1 : 0, ruoxi: true);
        if (loseObserver) s.Use("lose-neihong");
        s.Need(s.Registered(Neihong) && s.V(0).Skills?.Any(skill => skill.ContentId == Neihong) != true &&
            s.V(0).Skills?.Any(skill => skill.ContentId == Ruoxi) == true && s.V(0).HandCount < s.V(0).Hp,
            "The catalog contains the observer capability, but no living actor has an enabled copy; the real outgoing low-hand policy remains active.");
        foreach (var seat in new[] { 1, 2, 3 }) s.Use("chain", targets: [seat]);
        s.Cold(); s.Begin("fire", targets: [1]); s.Cold(); s.Play();
        s.Need(s.Events<DamageAppliedEvent>().Select(e => (e.SourceSeat, e.TargetSeat, e.Amount, e.Nature))
                .SequenceEqual([(0, 1, 2, DamageNature.Fire), (0, 2, 3, DamageNature.Fire), (0, 3, 4, DamageNature.Fire)]) &&
            s.Events<RecipientScopedDamageBaseEvent>().Length == 0 &&
            s.Events<RecipientScopedDamageAdvancedEvent>().Length == 0 &&
            !s.Events<ProgramCardDamageModifiedEvent>().Any(e => e.Source.SkillId == Neihong),
            "The existing unscoped chain carries its previous finalized amount into the next low-hand check: two, three, four. Mere registration or prior lost ownership cannot issue a new fixed base.");
        s.Cold();
    }

    private static void VerifyIneligibleActiveObserverLeavesLegacySourceChain(bool publicTeams)
    {
        var s = Scenario.Start(ruoxi: true, publicTeams: publicTeams);
        s.Need(s.V(0).IsAlive && s.V(0).Skills?.Any(skill => skill.ContentId == Neihong) == true &&
            s.V(0).Skills?.Any(skill => skill.ContentId == Ruoxi) == true && s.V(0).HandCount < s.V(0).Hp &&
            (publicTeams ? s.V(0).TeamId == ShanheBattleSetup.PlayerTeamId :
                Enumerable.Range(0, Seats).All(seat => s.V(seat).TeamId is null)),
            "A real living actor retains both enabled Neihong and low-hand source damage; its own outgoing damage cannot be two public enemies' mutual damage.");
        foreach (var seat in new[] { 1, 2, 3 }) s.Use("chain", targets: [seat]);
        s.Cold(); s.Begin("fire", targets: [1]); s.Cold(); s.Play();
        s.Need(s.Events<DamageAppliedEvent>().Select(e => (e.SourceSeat, e.TargetSeat, e.Amount, e.Nature))
                .SequenceEqual([(0, 1, 2, DamageNature.Fire), (0, 2, 3, DamageNature.Fire), (0, 3, 4, DamageNature.Fire)]) &&
            s.Events<RecipientScopedDamageBaseEvent>().Length == 0 &&
            s.Events<RecipientScopedDamageAdvancedEvent>().Length == 0 &&
            !s.Events<ProgramCardDamageModifiedEvent>().Any(e => e.Source.SkillId == Neihong) &&
            s.V(0).Skills?.Any(skill => skill.ContentId == Neihong) == true,
            "An enabled but ineligible observer must not change a legacy source's two-three-four propagation, whether the source is its own team or public teams are unknown.");
        s.Cold();
    }

    private static void VerifyIssuedObserverBaseSurvivesSkillLoss()
    {
        var s = Scenario.Start(chain: true, loseDuringChain: true);
        s.Equip(CardKind.CrimsonSoulZither, 1);
        foreach (var seat in new[] { 0, 2, 3, 4 }) s.Use("chain", targets: [seat]);
        s.Begin("duel", targets: [1, 2]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.Events<RecipientScopedDamageBaseEvent>() is [{ Amount: 2, PublicEnemyObserver: true }],
            "An actual living observer issues the first finalized base before the later friendly recipient's skill-loss choice.");
        s.Need(s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true &&
            s.V(0).IsAlive && s.V(0).Hp == 0 && s.V(0).Skills?.Any(skill => skill.ContentId == Neihong) == true,
            "The mandatory game-once dying-entry program pauses before recovering one HP and performing its terminal native skill loss, with the original chain still suspended.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.V(0).Skills?.Any(skill => skill.ContentId == Neihong) != true);
        s.Cold(); s.Play();
        s.Need(s.V(0).IsAlive && s.V(0).Hp == 1 &&
            s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount))
                .SequenceEqual([(2, 2), (0, 2), (3, 2), (4, 2)]) &&
            s.Events<RecipientScopedDamageAdvancedEvent>().Select(e => (e.TargetSeat, e.Amount))
                .SequenceEqual([(0, 2), (3, 2), (4, 2)]) &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Neihong) == 1,
            "The issued base remains two after native skill loss, while later enemies receive no newly disabled observer contribution.");
        s.Cold();
    }

    private static void VerifyIssuedObserverBaseSurvivesDeath()
    {
        var s = Scenario.Start(chain: true, observerDeath: true);
        s.Equip(CardKind.CrimsonSoulZither, 2);
        foreach (var seat in new[] { 0, 1, 3, 4 }) s.Use("chain", targets: [seat]);
        s.Begin("duel", targets: [2, 3]);
        s.Reach(() => s.P is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
        s.Need(s.Events<RecipientScopedDamageBaseEvent>() is [{ FirstTargetSeat: 3, Amount: 2, PublicEnemyObserver: true }] &&
            s.V(0).Hp <= 0 && s.V(1).IsAlive,
            "The holder enters actual dying after the observer issued a base of two, while a real living teammate keeps the match running.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
        s.Reach(() => !s.V(0).IsAlive && s.Events<DamageAppliedEvent>().Length == 4);
        s.Need(s.G.State.Winner == Winner.None && s.V(1).IsAlive &&
            s.Events<DamageAppliedEvent>().Select(e => (e.SourceSeat, e.TargetSeat, e.Amount))
                .SequenceEqual([(2, 3, 2), (2, 0, 2), (2, 1, 2), (2, 4, 2)]) &&
            s.Events<RecipientScopedDamageAdvancedEvent>().Select(e => (e.TargetSeat, e.Amount))
                .SequenceEqual([(0, 2), (1, 2), (4, 2)]) &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Neihong) == 1,
            "An actual death removes the observer's new contributions but does not revoke its already-issued propagation base or stop the surviving teammate's encounter.");
        s.Cold();
    }

    public static void UnknownPublicTeamsDoNotInferHiddenIdentityOrFactionCold()
    {
        var s = Scenario.Start(publicTeams: false);
        void PrivateViews()
        {
            for (var viewer = 0; viewer < Seats; viewer++)
            {
                var players = s.G.CreateSnapshot(viewer).Players;
                s.Need(players.All(p => p.TeamId is null && !p.IsTeamRevealed) &&
                    players.Where(p => p.Seat != viewer && p.Seat != 0).All(p => p.Role is null && !p.IsRoleRevealed) &&
                    players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0),
                    "An unknown public team stays unknown; the observer does not disclose another seat's hidden role or physical hand.");
            }
        }
        PrivateViews();
        s.Begin("duel", targets: [1, 2]);
        s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 2 });
        s.Cold(); PrivateViews(); s.Play();
        s.Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 1, TargetSeat: 2, Amount: 1 }] &&
            !s.Events<ProgramCardDamageModifiedEvent>().Any(e => e.Source.SkillId == Neihong),
            "The two concealed Rebels share a printed faction different from the Lord's, but neither identity nor faction may substitute for explicit public teams.");
        PrivateViews(); s.Cold();
    }

    private static string Peer(int seat) => "fixture:observed-tactic-peer-" + seat;

    private sealed class Scenario(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, Seats).Select(seat => G.CreateSnapshot(seat).PendingDecision)
            .FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Registered(string skillId) => registry.Skills.ContainsKey(skillId);
        public void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Observed-tactic command rejected."); }
        public void Begin(string activation, int[]? cards = null, int[]? targets = null) => Accept(
            new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string activation, int[]? cards = null, int[]? targets = null)
        { Begin(activation, cards, targets); Play(); }
        public void Equip(CardKind kind, int target) => Use("equip", [V(0).Hand.Single(c => c.Kind == kind).Id], [target]);
        public void Answer(Func<PromptChoice, bool> select)
        {
            var prompt = P!; Need(prompt.PlayerSeat == 0, "NPC answers must remain native AI commands.");
            Accept(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(select).Id, G.Revision));
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 200; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Observed-tactic fixture ended before its intended boundary.");
                if (P is { PlayerSeat: 0 } human)
                {
                    var choice = human.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die" ||
                        c.Parameters.GetValueOrDefault("program-action") == "skip");
                    Need(choice is not null, "Unexpected human boundary: " + JsonSerializer.Serialize(human));
                    Answer(c => c.Id == choice!.Id);
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing observed-tactic boundary: " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, Seats).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves exact observer instances, frozen bases, native responses, private views and entity movements.");
        }
        public static Scenario Start(int copies = 1, bool chain = false, bool publicTeams = true,
            bool ruoxi = false, bool loseDuringChain = false, bool observerDeath = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(copies, chain, ruoxi, loseDuringChain, observerDeath));
            GameOptions options;
            if (publicTeams)
            {
                var cards = new List<ShanheStartingCard>
                { new(CardKind.Slash, Suit.Heart, 6), new(CardKind.CrimsonSoulZither, Suit.Diamond, 1), new(CardKind.SilverLion, Suit.Club, 1) };
                if (observerDeath) cards.Add(new(CardKind.Peach, Suit.Heart, 3));
                var setup = new ShanheBattleSetup(19, 1, "fixture-observed-tactic", Owner, observerDeath ? 1 : loseDuringChain ? 2 : 8, 0, 0, 0, 0, [], cards, [],
                    Enumerable.Range(observerDeath ? 2 : 1, observerDeath ? Seats - 2 : Seats - 1)
                        .Select(seat => new ShanheCombatantDefinition(Peer(seat), Hp: 6)
                        { StartingHand = [new(CardKind.Slash, Suit.Heart, seat + 1)] }).ToArray(),
                    observerDeath ? [new(Peer(1), Hp: 6) { StartingHand = [new(CardKind.Slash, Suit.Heart, 2)] }] : [], new string('c', 64))
                { TacticSkillIds = copies == 2 ? [Neihong] : [] };
                options = new GameOptions { Seed = setup.BattleSeed, PlayerCount = Seats, HumanSeat = 0, HumanRole = null,
                    HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                    UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 };
            }
            else options = new GameOptions { Seed = 19, PlayerCount = Seats, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = IdentityMode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8 };
            var s = new Scenario(GameEngine.CreateStandard(options, registry), registry);
            s.Accept(new StartGameCommand());
            if (!publicTeams)
            {
                s.Reach(() => s.P is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
                s.Accept(new SelectGeneralCommand(0, Owner, s.G.Revision, s.P!.PromptId));
            }
            s.Play(); return s;
        }
    }

    private sealed class Fixture(int copies, bool chain, bool ruoxi, bool loseDuringChain, bool observerDeath) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-observed-tactics", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            static string Resource(string suffix)
            {
                var assembly = typeof(StandardContentPackage).Assembly;
                using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
                    .Single(name => name.EndsWith("." + suffix, StringComparison.Ordinal)))!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var production = SkillProgramCatalog.Load(Resource("shanhe-passives.rules.json"), Resource("shanhe-passives.presentation.json"));
            foreach (var (id, program) in production.Programs)
                b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = program, ProgramPresentation = production.Presentations[id] });
            var watchTrigger = loseDuringChain
                ? $$"""{"id":"real-chain-dying-pause","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"resumed","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Neihong}}"],"sourceBind":"standard:none"}]}"""
                : """{"id":"real-chain-damage-pause","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}""";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"two-chained-duels","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTargets","chained":true},{"op":"startVirtualDuel","target":"owner"},{"op":"setChainedState","target":"selectedTargets","chained":true},{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"enemy-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","nature":"fire","amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                {"id":"lose-neihong","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Neihong}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{{watchTrigger}}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, Watch }.ToDictionary(id => id,
                    id => new
                    {
                        name = "真实观察者检查", description = "真实决斗、实体装备、技能失去与伤后冷恢复",
                        optionLabels = id == Watch && loseDuringChain
                            ? new Dictionary<string, string> { ["continue"] = "继续回复并失去内讧" } : []
                    })
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string>();
            if (copies > 0) own.Add(Neihong);
            if (ruoxi) own.Add(Ruoxi);
            if (chain) own.Add(Watch);
            b.AddGeneral(new(Owner, "第三方观察者", "", Driver, "qun", 8, own) { InitialHp = 6 });
            for (var seat = 1; seat < Seats; seat++)
                b.AddGeneral(new(Peer(seat), "真实敌军", "", Quiet, "shu", 8) { InitialHp = 6 });
            foreach (var kind in new[] { CardKind.CrimsonSoulZither, CardKind.SilverLion })
            {
                var equipment = CardCatalog.Get(kind);
                b.AddCard(new("fixture:observed-equipment-" + kind, equipment.DisplayName,
                    equipment.CategoryName, equipment.Description, kind));
            }
            foreach (var (id, initial) in new[] { (Deck, 0), (IdentityDeck, 1) })
                b.AddDeck(new(id, "固定真实杀牌堆", initial, 0, [])
                { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            var pool = new[] { Owner }.Concat(Enumerable.Range(1, Seats - 1).Select(Peer)).ToArray();
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "公开队伍观察者", Seats, Seats, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: pool, ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = observerDeath ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = observerDeath ? Seats - 2 : Seats - 1 }));
            b.AddMode(new(IdentityMode, "无公开队伍的观察者", Seats, Seats,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = Seats - 1 },
                IdentityDeck, GeneralCandidateCount: Seats, GeneralPoolIds: pool));
        }
    }
}
