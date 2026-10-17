using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheTrickOrdinalTacticChecks
{
    private const string Driver = "fixture:shanhe-trick-driver", Watch = "fixture:shanhe-trick-damage-child";
    private const string Owner = "fixture:shanhe-trick-owner", Enemy = "fixture:shanhe-trick-enemy";
    private const string Deck = "fixture:shanhe-trick-deck", Fengzi = "ol:fengzi", Luanji = "classic:luanji";

    public static void NonDamagingFirstTrickConsumesSlotAndNextActualTurnResetsCold()
    {
        foreach (var tier in new[] { 1, 2 })
        {
            var s = Scenario.Start("physical", tier);
            s.PlayCard(CardKind.DrawTwo);
            s.Need(s.Events<DamageAppliedEvent>().Length == 0 &&
                s.Events<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 0 && e.CardKind == CardKind.DrawTwo) == 1,
                "The first slot is one genuine non-damaging DrawTwo use, rather than the first damage occurrence.");
            s.Cold();
            s.PlayCard(CardKind.Duel, 1);
            s.PlayCard(CardKind.Duel, 2);
            var hits = s.Events<DamageAppliedEvent>();
            s.Need(hits.Length == 2 && hits[0] is { SourceSeat: 0, TargetSeat: 1 } &&
                hits[0].Amount == (tier == 2 ? 2 : 1) && hits[1] is { SourceSeat: 0, TargetSeat: 2, Amount: 1 },
                "Miaoji I has already spent its first-use slot; Miaoji II still boosts the second use, and neither boosts the third trick.");
            var turn = s.Events<TurnStartedEvent>().Last().TurnNumber;
            s.EndTurn();
            s.Need(s.Events<TurnStartedEvent>().Last().TurnNumber > turn &&
                s.Events<TurnStartedEvent>().Last().ActorSeat == 0,
                "The fixture crosses a real native turn cycle rather than manually clearing the tactic.");
            s.PlayCard(CardKind.Duel, 3);
            s.Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 3, Amount: 2 },
                "The first trick of the next actual turn gets exactly one bonus for either tier.");
            s.Cold();
        }
    }

    public static void PaidNullificationPrecedesOwnVirtualDuelAndKeepsActorIsolationCold()
    {
        foreach (var tier in new[] { 1, 2 })
        {
            var s = Scenario.Start("virtual", tier);
            var counter = s.V().Hand.Single().Id;
            s.Reach(() => s.P?.SkillPrompt?.SkillId == Driver && s.P.Choices.Any(c => c.Targets.SequenceEqual([1])));
            s.Answer(c => c.Targets.SequenceEqual([1]));
            s.Reach(() => s.NullificationFor(0));
            s.Need(s.Events<CardUseDeclaredEvent>().Single() is { SourceSeat: 1, CardKind: CardKind.Duel, CardId: 0 } &&
                s.Events<DamageAppliedEvent>().Length == 0,
                "The first Duel really belongs to the selected enemy and occupies none of the owner's trick slots.");
            s.Cold();
            s.Answer(c => c.Cards.SequenceEqual([counter]) && c.Parameters.GetValueOrDefault("response") != "pass");
            s.Reach(() => s.IsDamagePause(1));
            var ownUse = s.G.ResolutionStack.OfType<CardUseFrame>().Last(f => f.SourceSeat == 0 && f.CardKind == CardKind.Duel);
            var nullification = s.Events<CardActionAcceptedEvent>().Single(e =>
                e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Nullification).Action;
            s.Need(ownUse is { CardId: 0, PhysicalCardIds.Count: 0, Action: not null } &&
                ownUse.Action.ActorSeat == 0 && ownUse.Action.ActionId > nullification.ActionId &&
                nullification.Type == CardActionType.Response && nullification.ProviderSeat == 0 &&
                s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1 }] &&
                s.Events<DamageAppliedEvent>().Single().Amount == (tier == 2 ? 2 : 1) && s.V().Hp == 12,
                "One real Response-use Nullification occupies the owner's first trick slot; the later virtual own Duel is second, independently of the foreign actor's first use.");
            s.Cold(); s.ContinueDamage(); s.Play();
            s.Need(s.Events<CardUseDeclaredEvent>().Count(e => e.CardKind == CardKind.Duel) == 2 &&
                s.Events<CardUseFinishedEvent>().Count(e => e.CardKind == CardKind.Duel) == 2 &&
                s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Driver && e.Activated && e.Completed) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == counter && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == counter && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Both genuine virtual uses and their original preparation program finish once, while the physical Nullification pays and settles only once.");
            s.Cold();
        }
    }

    public static void ConvertedRepeatedAoeKeepsOriginalOrdinalAcrossOwnCounterspellCold()
    {
        var s = Scenario.Start("repeat", 1);
        var pair = s.V().Hand.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).ToArray();
        var cost = s.V().Hand.First(c => c.Kind == CardKind.ArrowBarrage).Id;
        var later = s.V().Hand.Last(c => c.Kind == CardKind.ArrowBarrage).Id;
        var counter = s.V().Hand.Single(c => c.Kind == CardKind.Nullification).Id;
        s.Accept(new UseProgramSkillCommand(0, Luanji, "same-suit-pair-as-arrow-barrage", pair, [],
            s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Fengzi &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([cost])) == true);
        s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([cost]));
        // The retained engine exposes one whole-group Nullification window.
        // Cancel this round, then observe the native paid extra round of the same use.
        s.Reach(() => s.NullificationFor(1, 2, 3));
        var original = s.G.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.ArrowBarrage);
        s.Need(original.Action is { PhysicalCards.Count: 2 } action &&
            action.ConversionChain.Any(source => source.SkillId == Luanji) &&
            s.Events<DamageAppliedEvent>().Length == 0 &&
            s.Events<ExtraCardEffectIssuedEvent>().Single().PaidCardId == cost,
            "Two real same-suit Luanji materials declare one converted first trick; native Fengzi has already paid another real trick for an extra effect before any hit.");
        var useId = original.Id;
        var actionId = original.Action!.ActionId;
        s.Cold();
        s.Answer(c => c.Cards.SequenceEqual([counter]));
        s.Reach(() => s.IsDamagePause(1));
        s.Need(s.Events<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 0 &&
                e.Action.EffectiveKind == CardKind.Nullification && e.Action.Type == CardActionType.Response) == 1 &&
            s.Events<ExtraCardEffectStartedEvent>().Count(e => e.CardUseFrameId == useId && e.ActionId == actionId) == 1 &&
            s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 2 }] &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == useId && f.Action?.ActionId == actionId),
            "The owner's later Nullification occupies slot two and cancels the original effect; its paid extra round keeps the same first-use identity and still receives Miaoji I.");
        s.Cold(); s.ContinueDamage(); s.Play();
        var originalHits = s.Events<DamageAppliedEvent>();
        s.Need(originalHits.Length == 3 && originalHits.Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]) &&
            originalHits.All(e => e.SourceSeat == 0 && e.Amount == 2) &&
            s.Events<ExtraCardEffectIssuedEvent>().Count(e => e.CardUseFrameId == useId && e.ActionId == actionId && e.PaidCardId == cost) == 1 &&
            s.Events<ExtraCardEffectStartedEvent>().Count(e => e.CardUseFrameId == useId && e.ActionId == actionId) == 1 &&
            s.Events<CardUseDeclaredEvent>().Count(e => e.ResolutionId == useId) == 1 &&
            s.Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1,
            "All three landed AOE occurrences in the real repeated effect retain the frozen first-use bonus, without inventing another use or paying the repeat again.");
        s.PlayCardId(later);
        s.Need(s.Events<DamageAppliedEvent>().Skip(3).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]) &&
            s.Events<DamageAppliedEvent>().Skip(3).All(e => e.SourceSeat == 0 && e.Amount == 1) &&
            pair.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1) &&
            s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == counter && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == counter && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The later physical AOE is the third trick and gets no bonus; each conversion material, paid repeat cost and actual counterspell remains paid exactly once.");
        s.Cold();
    }

    private sealed class Scenario(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected actual trick-ordinal command."); }
        public void Answer(Func<PromptChoice, bool> pick)
        { var prompt = P!; Accept(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(pick).Id, G.Revision)); }
        public bool NullificationFor(params int[] seats) => P is { PlayerSeat: 0, Kind: DecisionKind.Nullification } &&
            G.ResolutionStack.OfType<NullificationWindowFrame>().Last().TargetSeats.SequenceEqual(seats);
        public bool IsDamagePause(int target) => P?.SkillPrompt?.SkillId == Watch &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") &&
            Events<DamageAppliedEvent>().LastOrDefault()?.TargetSeat == target;
        public void ContinueDamage() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void PlayCard(CardKind kind, int? target = null) => PlayCardId(V().Hand.First(c => c.Kind == kind).Id, target);
        public void PlayCardId(int id, int? target = null)
        {
            var action = G.GetHumanLegalActions().Single(a => a.CardId == id &&
                (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Accept(new PlayCardCommand(0, id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind,
                action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
            Play();
        }
        public void EndTurn() { Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId)); Play(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) ContinueDamage();
                    else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                    else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                        Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                    else throw new InvalidOperationException("Unexpected trick-ordinal boundary: " + JsonSerializer.Serialize(P));
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded trick-ordinal boundary: " + JsonSerializer.Serialize(new { Prompt = P, Hits = Events<DamageAppliedEvent>() }));
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
            Need(before == State(), "Cold replay preserves actual trick declarations, original AOE identity, paid materials, pending children and four filtered views.");
        }
        public static Scenario Start(string scene, int tier)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            ShanheStartingCard[] cards = scene switch
            {
                "physical" => [new(CardKind.DrawTwo), new(CardKind.Duel), new(CardKind.Duel), new(CardKind.Duel)],
                "virtual" => [new(CardKind.Nullification, Suit.Spade, 11)],
                _ => [new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Club, 5),
                    new(CardKind.ArrowBarrage, Suit.Heart, 1), new(CardKind.ArrowBarrage, Suit.Spade, 1), new(CardKind.Nullification, Suit.Club, 12)]
            };
            var setup = new ShanheBattleSetup(79, 1, "fixture:trick-" + scene, Owner, 12, 0, 0, 0, 0,
                scene == "repeat" ? [Luanji, Fengzi] : [], cards, [],
                [new(Enemy, MaxHpBonus: 20), new(Enemy, MaxHpBonus: 20), new(Enemy, MaxHpBonus: 20)], [], new string('e', 64))
            { TacticSkillIds = ["shanhe:miaoji-" + tier] };
            var g = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 8
            }, registry);
            var s = new Scenario(g, registry); s.Accept(new StartGameCommand());
            if (scene != "virtual") s.Play();
            return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-trick-ordinals", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (scene == "repeat") CharacterFixtureContent.Register(builder, ["classic:yuan-shao", "ol:wang-rong"]);
            string Read(string file)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + file)
                    ?? throw new InvalidOperationException("Missing production Shanhe passive bundle.");
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var actual = SkillProgramCatalog.Load(Read("shanhe-passives.rules.json"), Read("shanhe-passives.presentation.json"));
            foreach (var id in new[] { "shanhe:miaoji-1", "shanhe:miaoji-2" })
                builder.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                { Program = actual.Programs[id], ProgramPresentation = actual.Presentations[id], Tags = SkillTag.Locked });
            var preparation = scene == "virtual" ? """
                [{"id":"foreign-then-own-duel","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                  {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                  {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"owner"},"outputKind":"duel"},
                  {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"duel"}]}]
                """ : "[]";
            var fixture = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"retain-real-materials","query":"handLimit","operation":"add","value":40,"priority":0}],"triggers":{{preparation}}},
                  {"id":"{{Watch}}","revision":1,"triggers":[{"id":"pause-landed-own-damage","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
                ]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object>
                    {
                        [Driver] = new { name = "真实锦囊序号调度", description = "只调度原生用牌" },
                        [Watch] = new { name = "真实伤害子流程", description = "只暂停已经发生的伤害",
                            optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                    } }));
            foreach (var (id, program) in fixture.Programs)
                builder.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实锦囊使用者", "supporter", Driver, "qun", 12, [Watch]));
            builder.AddGeneral(new(Enemy, "固定守军", "supporter", "standard:none", "shu", 12));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实锦囊序号", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}

