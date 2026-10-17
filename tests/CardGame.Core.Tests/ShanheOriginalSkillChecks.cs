using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheOriginalSkillChecks
{
    private const string Qiangxi = "boundary:qiangxi", Shizhan = "boundary:shizhan-current";
    private const string Shenxing = "classic:shenxing", Lianying = "boundary:lianying", Fanci = "shanhe:fanci";
    private const string Owner = "fixture:shanhe-original-owner", Watch = "fixture:shanhe-original-damage-child";
    private const string Deck = "fixture:shanhe-original-deck";

    public static void QiangxiDamageCostRunsCounterBeforeTargetAndDoesNotRepeatCold()
    {
        var s = Scenario.Start("qiangxi");
        s.Use(Qiangxi, "receive-damage", targets: [1]);
        s.AtDamageChild();
        s.Need(s.Events<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 1, SourceLess: true }] &&
            s.V().Hp == 7 && s.V(1).Hp == 32 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Qiangxi) &&
            !s.Events<ProgramSkillHpLostEvent>().Any(e => e.SkillId == Qiangxi),
            "The acquired 2024 variant pays real source-less damage and suspends its target instruction while the damage child is pending; HP loss is not an equivalent payment.");
        s.Cold(); s.Continue(); s.Play();
        var first = s.Events<DamageAppliedEvent>();
        s.Need(first.Length == 5 && first.Select(e => e.TargetSeat).SequenceEqual([0, 1, 2, 3, 1]) &&
            first.All(e => e.Amount == 1) && first.Skip(1).All(e => e.SourceSeat == 0 && !e.SourceLess) &&
            s.V(1).Hp == 30 && s.V(2).Hp == 31 && s.V(3).Hp == 31,
            "One genuine self-damage cost resolves the full three-enemy counter before the original one-point target damage, with no repeated payment.");
        s.Use(Qiangxi, "receive-damage", targets: [2]);
        s.AtDamageChild(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.Events<DamageAppliedEvent>().Length == 7 && s.V().Hp == 6 &&
            s.V(1).Hp == 30 && s.V(2).Hp == 30 && s.V(3).Hp == 31 && s.CounterUses() == 1 &&
            s.Events<ProgramSkillResolvedEvent>().Count(e => e.SkillId == Qiangxi && e.Completed) == 2,
            "A second actual cost in the same turn still pays damage and completes its own target, while the already spent first-damage tactic does not fire again.");
        s.Cold();
    }

    public static void QiangxiSourceLessCostSurvivesJueqingWhileSourcedCounterAndHitBecomeHpLossCold()
    {
        var s = Scenario.Start("qiangxi-jueqing");
        s.Use(Qiangxi, "receive-damage", targets: [1]);
        s.AtDamageChild();
        s.Need(s.Events<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 1, SourceLess: true }] &&
            s.V().Hp == 7 && s.Events<DamageReplacedWithHpLossEvent>().Length == 0 &&
            s.CounterUses() == 0 && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Qiangxi),
            "An acquired Jueqing cannot claim the source-less received-damage cost as its owner's caused damage; the real cost must suspend before its counter and target hit.");
        s.Cold(); s.Continue(); s.Play();
        var losses = s.Events<DamageReplacedWithHpLossEvent>();
        s.Need(s.Events<DamageAppliedEvent>().Length == 1 && s.CounterUses() == 1 &&
            losses.Length == 4 && losses.All(e => e.PolicyOwnerSeat == 0 && e.Amount == 1) &&
            losses.Select(e => e.TargetSeat).SequenceEqual([1, 2, 3, 1]) &&
            s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Fanci && e.Activated && e.Completed) == 1 &&
            s.V(1).Hp == 30 && s.V(2).Hp == 31 && s.V(3).Hp == 31,
            "The genuine cost triggers one full Fanci first; all three sourced counter hits and the original Qiangxi hit then become actual HP loss through native Jueqing.");
        s.Cold();
        s.Use(Qiangxi, "receive-damage", targets: [2]);
        s.AtDamageChild(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.Events<DamageAppliedEvent>() is
                [{ TargetSeat: 0, Amount: 1, SourceLess: true }, { TargetSeat: 0, Amount: 1, SourceLess: true }] &&
            s.V().Hp == 6 && s.CounterUses() == 1 &&
            s.Events<DamageReplacedWithHpLossEvent>().Select(e => e.TargetSeat).SequenceEqual([1, 2, 3, 1, 2]) &&
            s.Events<DamageReplacedWithHpLossEvent>().All(e => e.PolicyOwnerSeat == 0 && e.Amount == 1) &&
            s.Events<ProgramSkillResolvedEvent>().Count(e => e.SkillId == Qiangxi && e.Completed) == 2 &&
            s.V(1).Hp == 30 && s.V(2).Hp == 30 && s.V(3).Hp == 31,
            "A second distinct-target use pays one more source-less cost and one sourced HP-loss hit, without paying an old cost again or refunding the actual-turn Fanci quota.");
        s.Cold();
    }

    public static void ShizhanUsesRealOtherActorDuelAndOnlyDamageTriggersCounterCold()
    {
        var s = Scenario.Start("shizhan");
        var slash = s.V().Hand.Single().Id;
        s.Use(Shizhan, "selected-actor-duel", targets: [1]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Need(s.Events<CardUseDeclaredEvent>().Single() is { SourceSeat: 1, CardKind: CardKind.Duel, CardId: 0 } &&
            s.P!.Choices.Any(c => c.Cards.SequenceEqual([slash])) && s.CounterUses() == 0,
            "The selected enemy really declares a virtual-entity Duel and publishes a genuine physical Slash response to the owner.");
        s.Cold();
        s.Answer(c => c.Cards.SequenceEqual([slash])); s.Play();
        s.Need(s.V().Hp == 8 && s.V(1).Hp == 31 && s.CounterUses() == 0 &&
            s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 1 }] &&
            s.Events<CardRespondedEvent>().Any(e => e.CardId == slash && e.ResponderSeat == 0) &&
            s.G.CardMovements.Count(m => m.CardId == slash && m.To == CardLocation.DiscardPile) == 1 &&
            s.V().HandCount == 0,
            "Paying the real Slash makes the empty-handed actor lose the Duel; damage to the opponent cannot consume the owner's first-received-damage counter.");
        s.Use(Shizhan, "selected-actor-duel", targets: [2]);
        s.AtDamageChild();
        s.Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 2, TargetSeat: 0, Amount: 1 } &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Any() && s.CounterUses() == 0,
            "The second selected enemy remains the genuine Duel damage source when the now empty owner cannot respond; its card still owns the suspended damage child.");
        s.Cold(); s.Continue(); s.Play();
        var recent = s.Events<DamageAppliedEvent>().Skip(1).ToArray();
        s.Need(recent.Length == 4 && recent.Select(e => e.TargetSeat).SequenceEqual([0, 1, 2, 3]) &&
            recent.Skip(1).All(e => e.SourceSeat == 0 && e.Amount == 1) && s.CounterUses() == 1 &&
            s.Events<CardUseFinishedEvent>().Count(e => e.CardKind == CardKind.Duel) == 2 &&
            s.Events<ProgramSkillResolvedEvent>().Count(e => e.SkillId == Shizhan && e.Completed) == 2,
            "The real damage triggers one full counter chain, then both native Duel uses and their original skill parents finish exactly once.");
        s.Cold();
    }

    public static void ShenxingLastHandBatchCompletesLianyingBeforeNativeDrawCold()
    {
        var s = Scenario.Start("shenxing");
        var materials = s.V().Hand.Select(c => c.Id).ToArray();
        var weapon = s.V().Equipment.Single().Id;
        s.Need(materials.Length == 2 && s.V(1).TeamId == s.V().TeamId && s.V(1).HandCount == 0,
            "The fixed campaign opening supplies two real hand cards, one equipped weapon and an actual friendly seat.");
        var before = s.G.CardMovements.Last().Sequence;
        s.Use(Shenxing, "discard-two-draw-one", materials);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Lianying &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Shenxing);
        var batch = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
        s.Need(s.V().HandCount == 0 && s.V(1).HandCount == 0 && parent.InstructionIndex == 1 &&
            batch.ParentFrameId == parent.Id && batch.Movements.Count == 2 &&
            batch.Movements.Select(m => m.CardId).Order().SequenceEqual(materials.Order()) &&
            batch.Movements.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) &&
            !s.G.CardMovements.Any(m => m.Sequence > before && m.From == CardLocation.DrawPile),
            "The original two-card Hand payment is one real empty-hand batch, and its native Lianying child suspends Shenxing before any benefit draw.");
        s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Lianying && s.P.Choices.Any(c => c.Targets.Count > 0));
        s.Need(s.P!.Choices.All(c => c.Targets.Count is >= 1 and <= 2) &&
            s.P.Choices.Any(c => c.Targets.SequenceEqual([0, 1])),
            "The paid Hand batch freezes X=2 and genuinely permits supporting both the owner and one teammate.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([0, 1])); s.Play();
        var draws = s.G.CardMovements.Where(m => m.Sequence > before && m.From == CardLocation.DrawPile && m.To.Zone == CardZoneKind.Hand).ToArray();
        s.Need(draws.Length == 3 && draws.Select(m => m.To).SequenceEqual([CardLocation.Hand(0), CardLocation.Hand(1), CardLocation.Hand(0)]) &&
            s.V().HandCount == 2 && s.V(1).HandCount == 1 &&
            materials.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile) == 1) &&
            s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Lianying && e.Activated && e.Completed) == 1 &&
            s.Events<ProgramSkillResolvedEvent>().Count(e => e.SkillId == Shenxing && e.Completed) == 1,
            "Both selected recipients draw through the Lianying child before exactly one original Shenxing Draw1 returns; both discarded entities pay once.");
        s.Cold();
        var retained = s.V().Hand.Last().Id;
        var handCost = s.V().Hand.First().Id;
        s.Use(Shenxing, "discard-two-draw-one", [handCost, weapon]); s.Play();
        s.Need(s.V().HandCount == 2 && s.V().Hand.Any(c => c.Id == retained) && s.V(1).HandCount == 1 &&
            !s.V().Equipment.Any() &&
            s.G.CardMovements.Count(m => m.CardId == weapon && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Lianying && e.Activated && e.Completed) == 1 &&
            s.Events<ProgramSkillResolvedEvent>().Count(e => e.SkillId == Shenxing && e.Completed) == 2,
            "A later genuine mixed HE payment leaves one Hand card, so it cannot fabricate an empty-Hand Lianying trigger or give the teammate another card.");
        s.Cold();
    }

    private sealed class Scenario(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int CounterUses() => Events<SkillUsageConsumedEvent>().Count(e => e.SkillId == Fanci && e.SkillOwnerSeat == 0);
        public void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
        public void Accept(GameCommand command)
        {
            var result = G.Submit(command);
            Need(result.Accepted, result.Error?.Message ?? "The original-skill fixture command was rejected.");
        }
        public void Use(string skill, string activation, int[]? cards = null, int[]? targets = null) => Accept(
            new UseProgramSkillCommand(0, skill, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void AtDamageChild() => Reach(() => P?.SkillPrompt?.SkillId == Watch &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue();
                    else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                    else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                        Answer(c => c.Cards.Count == 0);
                    else throw new InvalidOperationException("Unexpected original-skill boundary: " + JsonSerializer.Serialize(P));
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded original-skill boundary: " + JsonSerializer.Serialize(new { Prompt = P, Facts = Events<DamageAppliedEvent>() }));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves exact original-skill payments, native card ownership, child frames, facts and four filtered views.");
        }
        public static Scenario Start(string scene)
        {
            string[] skills = scene switch
            { "qiangxi" => [Qiangxi], "qiangxi-jueqing" => [Qiangxi, "classic:jueqing"], "shizhan" => [Shizhan], _ => [Shenxing, Lianying] };
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var setup = new ShanheBattleSetup(73, 1, "fixture:original-" + scene, Owner, 8, 0, 0, 0, 0, skills,
                scene switch { "shizhan" => [new(CardKind.Slash, Suit.Spade, 6)], "shenxing" => [new(CardKind.Dodge, Suit.Heart, 4), new(CardKind.Dodge, Suit.Club, 5)], _ => [] },
                scene == "shenxing" ? [new(CardKind.QinggangSword, Suit.Spade, 6)] : [],
                Enumerable.Range(scene == "shenxing" ? 2 : 1, scene == "shenxing" ? 2 : 3)
                    .Select(i => new ShanheCombatantDefinition(Peer(i), MaxHpBonus: 20)).ToArray(),
                scene == "shenxing" ? [new(Peer(1))] : [], "fixture-original-skills")
            { TacticSkillIds = scene == "shenxing" ? [] : [Fanci] };
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var s = new Scenario(game, registry); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private static string Peer(int seat) => "fixture:shanhe-original-peer-" + seat;

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-original-skills", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, scene switch
            { "qiangxi" => ["boundary:dian-wei"], "qiangxi-jueqing" => ["boundary:dian-wei", "classic:zhang-chun-hua"], "shizhan" => ["boundary:hua-xiong"], _ => ["classic:gu-yong", "boundary:lu-xun"] });
            string Read(string name)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + name) ??
                    throw new InvalidOperationException("Missing native Shanhe reaction bundle.");
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var reactions = SkillProgramCatalog.Load(Read("shanhe-reactions.rules.json"), Read("shanhe-reactions.presentation.json"));
            builder.AddSkill(new(Fanci, reactions.Presentations[Fanci].Name, reactions.Presentations[Fanci].Description)
            { Program = reactions.Programs[Fanci], Tags = SkillTag.Locked });
            var pause = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Watch}}","revision":1,"triggers":[{"id":"pause-real-received-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"priority":100,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"continued","options":[{"id":"continue"}]}]}]}
                ]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Watch] = new { name = "原生受伤子流程", description = "只暂停既有真实伤害，不产生伤害或收益", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            builder.AddSkill(new(Watch, "原生受伤子流程", "只暂停既有真实伤害")
            { Program = pause.Programs[Watch], ProgramPresentation = pause.Presentations[Watch] });
            builder.AddGeneral(new(Owner, "原版技能使用者", "supporter", "standard:none", "qun", 12,
                scene == "shenxing" ? [] : [Watch]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new(Peer(seat), "固定真实对手", "supporter", "standard:none", "shu", 12));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "原版技能战法联动", 4, 4,
                new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Peer(1), Peer(2), Peer(3)],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = scene == "shenxing" ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = scene == "shenxing" ? 2 : 3 }));
        }
    }
}
