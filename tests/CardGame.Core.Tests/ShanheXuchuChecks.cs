using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheXuchuChecks
{
    private const string Luoyi = StandardShanheS5XuchuPackage.LuoyiId;
    private const string Driver = "fixture:shanhe-xuchu-driver", Quiet = "fixture:shanhe-xuchu-quiet";
    private const string Watch = "fixture:shanhe-xuchu-damage-watch", Enemy = "fixture:shanhe-xuchu-enemy";
    private const string Deck = "fixture:shanhe-xuchu-deck";

    public static void HpRevealChoiceNativeSlashDuelAndOwnerExpiryCold()
    {
        var s = Scene.Start(); s.AtLuoyi();
        var hand = s.V().Hand.Select(c => c.Id).ToArray();
        s.Activate(); s.AtDrawChoice();
        var freeze = s.Events<ShanheS5LuoyiFrozenEvent>().Single();
        var reveal = s.Events<ProgramCardsRevealedEvent>().Single(e => e.SkillId == Luoyi);
        Need(freeze.X == 4 && reveal.Cards.Count == 4 && s.V().Hand.Select(c => c.Id).SequenceEqual(hand) &&
            reveal.Cards.Select(c => c.Kind).Order().SequenceEqual(new[] { CardKind.Peach, CardKind.Duel, CardKind.Crossbow, CardKind.BaguaFormation }.Order()) &&
            s.G.CreateSnapshot(1).PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(reveal.Cards.Select(c => c.Id).Order()),
            "The first optional activation freezes current HP4 and publicly reveals the four actual top entities before the separate draw choice.");
        s.Cold(); s.Option("skip-draw"); s.Play();
        var granted = s.Events<CardDamageModifierGrantedEvent>().Single(e => e.Modifier.Source.SkillId == Luoyi).Modifier;
        var gained = reveal.Cards.Where(c => c.Kind != CardKind.BaguaFormation).Select(c => c.Id).ToArray();
        Need(gained.All(id => s.V().Hand.Any(c => c.Id == id)) &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == reveal.Cards.Single(c => c.Kind == CardKind.BaguaFormation).Id).Location == CardLocation.DiscardPile &&
            s.Events<ProgramTurnPhaseSubstitutedEvent>().Single(e => e.SkillId == Luoyi).SkippedPhase == SkillProgramTurnPhase.Draw &&
            !s.G.CardMovements.Any(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) &&
            granted.Amount == 4 && granted.Expiration == SkillProgramDamageModifierExpiration.NextOwnerTurnStart &&
            granted.SourceScope == SkillProgramDamageModifierSourceScope.DamageSource,
            "Choosing the actual skip obtains basic, weapon and Duel entities, discards only the armor, performs no normal draw and grants native +4 through the next own turn start.");
        s.UseDriver("hurt");
        Need(s.V().Hp == 3, "A genuine later HP loss changes the source to HP3 without changing the paid X4.");
        s.Cold(); s.BeginCard(CardKind.Slash, 1); s.AtDamageWatch(); s.Cold(); s.Option("continue"); s.Play();
        Need(s.DamageTo(1).Last().Amount == 5 && s.DamageTo(1).Last().SourceCardUseResolutionId is not null,
            "A real native Slash deals base1 plus frozen X4 after the source's HP changed.");
        s.BeginCard(CardKind.Duel, 2); s.AtDamageWatch(); s.Cold(); s.Option("continue"); s.Play();
        Need(s.DamageTo(2).Last().Amount == 5 && s.DamageTo(2).Last().SourceCardUseResolutionId is not null,
            "A real Duel also deals base1 plus X4 and returns once from its actual damage child.");
        s.EndTurn(); s.AtLuoyi();
        Need(s.Events<TurnCardUseEffectsExpiredEvent>().Any(e => e.TurnSeat == 0 && e.GrantSequences.Contains(granted.GrantSequence)) &&
            s.Events<ShanheS5LuoyiFrozenEvent>().Length == 1,
            "The modifier survives foreign turns and expires at the next actual owner turn before a fresh optional Luoyi activation.");
        s.Cold(); s.SkipActivation(); s.Play();
        s.BeginCard(CardKind.Slash, 1); s.AtDamageWatch(); s.Option("continue"); s.Play();
        s.BeginCard(CardKind.Duel, 2); s.AtDamageWatch(); s.Option("continue"); s.Play();
        Need(s.DamageTo(1).Last().Amount == 1 && s.DamageTo(2).Last().Amount == 1 &&
            s.Events<CardDamageModifierGrantedEvent>().Count(e => e.Modifier.Source.SkillId == Luoyi) == 1,
            "After real owner-start expiry, another native Slash and Duel both return to base damage without a new grant.");
        s.Cold();
    }

    public static void RevealedCardsCanDeclineReplacementAndStillDrawNormallyCold()
    {
        var s = Scene.Start(); s.AtLuoyi(); var hand = s.V().HandCount;
        s.Activate(); s.AtDrawChoice(); var revealed = s.Events<ProgramCardsRevealedEvent>().Single(e => e.SkillId == Luoyi);
        s.Cold(); s.Option("normal-draw"); s.Play();
        var discarded = s.G.CardMovements.Where(m => revealed.Cards.Any(c => c.Id == m.CardId) &&
            m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile).ToArray();
        Need(discarded.Length == 4 && s.V().HandCount == hand + 2 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 2 &&
            !s.Events<ProgramTurnPhaseSubstitutedEvent>().Any(e => e.SkillId == Luoyi) &&
            !s.Events<CardDamageModifierGrantedEvent>().Any(e => e.Modifier.Source.SkillId == Luoyi),
            "Declining the second choice cleans all publicly revealed cards, then performs exactly the native normal Draw2 without skip or damage bonus.");
        s.Cold();
        var skipped = Scene.Start(); skipped.AtLuoyi(); skipped.SkipActivation(); skipped.Play();
        Need(!skipped.Events<ShanheS5LuoyiFrozenEvent>().Any() && !skipped.Events<ProgramCardsRevealedEvent>().Any(e => e.SkillId == Luoyi) &&
            skipped.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 2,
            "Declining the initial activation leaves the deck unexposed and performs ordinary Draw2.");
    }

    public static void NpcRevealedChoiceWeighsActualCardsAgainstNormalDrawCold()
    {
        foreach (var armorOnly in new[] { false, true })
        {
            var s = Scene.Start(npc: true, armorOnly: armorOnly);
            s.NpcUntil(() => s.Events<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Luoyi && e.Completed));
            var chosen = s.Events<ProgramOptionChosenEvent>().Single(e => e.SkillId == Luoyi);
            var resolved = s.Events<ProgramBindingResolvedEvent>().Single(e => e.SkillId == Luoyi);
            Need(!s.V().IsHuman && s.Events<ShanheS5LuoyiFrozenEvent>() is [{ X: 4 }] &&
                chosen.OwnerSeat == 0 && chosen.ChooserSeat == 0 &&
                resolved.Activated && resolved.Completed && resolved.FrameId == chosen.FrameId &&
                s.Events<ShanheS5LuoyiFrozenEvent>().Single().FrameId == chosen.FrameId &&
                chosen.ResultBind == "luoyi-draw-choice" && chosen.BindingId == resolved.BindingId &&
                chosen.OptionId == (armorOnly ? "normal-draw" : "skip-draw"),
                "The real NPC activates Luoyi, then its actual named-choice fact records the second decision from the public HP4 reveal.");
            if (armorOnly)
                s.NpcUntil(() => s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 2);
            var modifiers = s.Events<CardDamageModifierGrantedEvent>().Where(e => e.Modifier.Source.SkillId == Luoyi).ToArray();
            var normalDraws = s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw);
            var reveal = s.Events<ProgramCardsRevealedEvent>().Single(e => e.SkillId == Luoyi);
            var revealedMoves = s.G.CardMovements.Where(m => reveal.Cards.Any(c => c.Id == m.CardId) &&
                m.From == CardLocation.Processing && m.Reason.Value == $"skill-program.{Luoyi}.MoveBoundCards").ToArray();
            Need(reveal.Cards.Count == 4 && revealedMoves.Length == 4 &&
                (armorOnly
                    ? revealedMoves.All(m => m.To == CardLocation.DiscardPile)
                    : revealedMoves.Where(m => m.To == CardLocation.Hand(0)).Select(m => m.CardId).Order()
                        .SequenceEqual(reveal.Cards.Where(c => c.Kind != CardKind.BaguaFormation).Select(c => c.Id).Order()) &&
                        revealedMoves.Single(m => m.To == CardLocation.DiscardPile).CardId == reveal.Cards.Single(c => c.Kind == CardKind.BaguaFormation).Id),
                "The NPC's unique completed Luoyi moves every revealed physical entity once through the selected native partition.");
            Need(armorOnly
                    ? modifiers.Length == 0 && normalDraws == 2 &&
                        !s.Events<ProgramTurnPhaseSubstitutedEvent>().Any(e => e.SkillId == Luoyi)
                    : modifiers is [{ Modifier.Amount: 4 }] && modifiers[0].Modifier.ParentFrameId == chosen.FrameId &&
                        normalDraws == 0 && s.Events<ProgramTurnPhaseSubstitutedEvent>().Single(e => e.SkillId == Luoyi).FrameId == chosen.FrameId,
                armorOnly
                    ? "An empty-handed NPC declines an all-armor public reveal and takes the real normal Draw2 instead of valuing an unusable immediate bonus."
                    : "The NPC takes the actual mixed basic/weapon/Duel reveal and its frozen +4 benefit instead of always choosing normal draw.");
            s.Cold();
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        private PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public DamageAppliedEvent[] DamageTo(int seat) => Events<DamageAppliedEvent>().Where(e => e.SourceSeat == 0 && e.TargetSeat == seat).ToArray();
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "The real Xuchu command was rejected."); }
        private void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.Single(predicate).Id, G.Revision)); }
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void SkipActivation() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Option(string option) => Answer(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void AtLuoyi() => Reach(() => P?.SkillPrompt?.SkillId == Luoyi && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void AtDrawChoice() => Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "skip-draw") == true);
        public void AtDamageWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void UseDriver(string activation)
        { Accept(new UseProgramSkillCommand(0, Driver, activation, [], [], G.Revision, P!.PromptId)); Play(); }
        public void BeginCard(CardKind kind, int target)
        {
            var card = V().Hand.First(c => c.Kind == kind);
            var action = G.GetHumanLegalActions().First(a => a.CardId == card.Id && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, card.Id, action.TargetSeats, G.Revision, P!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        }
        public void EndTurn() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void NpcUntil(Func<bool> done)
        {
            for (var step = 0; step < 100; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded NPC Xuchu choice must not finish a match. Luoyi returns: " +
                    JsonSerializer.Serialize(Events<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Luoyi)
                        .Select(e => new { e.FrameId, e.Activated, e.Completed })) +
                    "; public HP freezes: " + Events<ShanheS5LuoyiFrozenEvent>().Length);
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The real NPC did not reach its expected Luoyi choice return.");
        }
        private void Reach(Func<bool> done)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small Xuchu fixture must keep its high-HP enemies alive.");
                if (P?.PlayerSeat == 0)
                    throw new InvalidOperationException("Unexpected Xuchu boundary: " + JsonSerializer.Serialize(new { P.Kind, P.Prompt, Skill = P.SkillPrompt?.SkillId }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded native Xuchu flow did not reach its expected decision.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the frozen HP, public reveal choice, exact native damage children and owner-start expiry.");
        }
        public static Scene Start(bool npc = false, bool armorOnly = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardShanheS5XuchuPackage(), new Fixture(armorOnly));
            var setup = new ShanheBattleSetup(71, 1, "fixture-xuchu", StandardShanheS5XuchuPackage.XuchuId, 4, 0, 0, 0, 0,
                [Driver, Watch], armorOnly ? [] : [new(CardKind.Slash), new(CardKind.Slash), new(CardKind.Duel), new(CardKind.Duel)], [],
                Enumerable.Range(1, 3).Select(_ => new ShanheCombatantDefinition(Enemy, MaxHpBonus: 30) { StartingHand = [] }).ToArray(),
                [], new string('a', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = npc ? -1 : 0, HumanRole = null,
                HumanTeamId = npc ? null : ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = npc ? 1 : 12
            }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture(bool armorOnly) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-xuchu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],
                "activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                  "effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]},{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"after-real-damage","window":"afterDamageApplied","subject":"damageSource","optional":false,
                "damageOccurrence":"perDamage","condition":{"kind":"damageTargetIsOther"},"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-damage",
                  "options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "许褚真实失血", description = "发动后改变体力" },
                    [Quiet] = new { name = "静默守军", description = "跳过出牌" },
                    [Watch] = new { name = "原生伤害证人", description = "在已造成伤害后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Enemy, "守军", "supporter", Quiet, "qun", 4));
            b.AddDeck(new(Deck, "固定四张实体", 0, 2, []) { PhysicalCards = armorOnly
                ? Enumerable.Range(0, 4).Select(_ => new ContentDeckPhysicalCard("standard:bagua", Suit.Diamond, 6)).ToArray() : [
                new("standard:peach", Suit.Heart, 3), new("standard:duel", Suit.Spade, 4),
                new("standard:crossbow", Suit.Club, 5), new("standard:bagua", Suit.Diamond, 6)] });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "许褚机制", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5XuchuPackage.XuchuId, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
