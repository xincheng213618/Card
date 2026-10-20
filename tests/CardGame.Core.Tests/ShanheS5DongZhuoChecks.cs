using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5DongZhuoChecks
{
    private const string Boss = StandardShanheS5BossPackage.DongZhuoLuoyangId, Owner = "fixture:s5-dong-owner", Ally = "fixture:s5-dong-ally";
    private const string Jiuchi = "boundary:jiuchi-current", Roulin = "boundary:roulin-current", Benghuai = "boundary:benghuai-current";
    private const string Baonue = StandardShanheS5BossPackage.DongZhuoBaonueId, Reason = "skill.boundary.baonue-current", Jueqing = "classic:jueqing";
    private const string Driver = "fixture:s5-dong-driver", Quiet = "fixture:s5-dong-quiet", HpWatch = "fixture:s5-dong-recovery-child";
    private const string GainWatch = "fixture:s5-dong-claim-child", Suppress = "fixture:s5-dong-suppression";
    private const string ExtraLord = "fixture:s5-dong-extra-lord", NoProgramLord = "fixture:s5-dong-no-program-lord";
    private const string NoProgramGeneral = "fixture:s5-dong-no-program-general", Deck = "fixture:s5-dong-deck";

    public static void ExplicitPrintedLordRecipeFreezesValidatesHashesAndNeverChangesTeamRole()
    {
        var modern = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var actual = modern.Nodes.Single(node => node.Id == "luoyang").Encounters!
            .Single(encounter => encounter.Id == "luoyang:dongzhuo-s5").Enemies.Single();
        Need(actual.GeneralId == Boss && actual.QualifiedPrintedLordSkillIds?.SequenceEqual([Baonue]) == true &&
            StandardShanheCampaignCatalog.CreateDefinition(1).Nodes.SelectMany(node => node.Encounters ?? []).SelectMany(encounter => encounter.Enemies)
                .All(member => member.GeneralId != Boss),
            "The actual 2026 local Luoyang recipe uses the separate verified four-skill form with explicit printed Baonue qualification, without inserting it into the 2024 entry.");
        var registry = Registry(); var mutable = new List<string> { Baonue };
        var member = Member() with { QualifiedPrintedLordSkillIds = mutable }; mutable.Add(Jiuchi);
        Need(member.QualifiedPrintedLordSkillIds!.SequenceEqual([Baonue]) && member.QualifiedPrintedLordSkillIds is IList<string> { IsReadOnly: true },
            "The explicit recipe detaches its qualified printed Lord list immediately, before callers can mutate the input.");
        var setup = Recipe(member, Member());
        var s = Scene.Start(setup, registry);
        Need(new[] { 2, 3 }.All(seat => s.V(seat) is { Hp: 4, MaxHp: 8, TeamId: ShanheBattleSetup.EnemyTeamId } &&
                s.V(seat).Skills?.Any(skill => skill.ContentId == Baonue) == true && s.HasQualifiedBinding(seat)) &&
            Enumerable.Range(0, 4).All(seat => s.G.State.Players[seat].Role != Role.Lord),
            "Both actual scenario NPCs activate exactly their printed Baonue without becoming identity Lords or receiving a Lord HP bonus. " + s.BindingDiagnostic());
        s.Cold();
        var unqualified = Scene.Start(Recipe(Member(false), Member(false)), registry);
        Need(new[] { 2, 3 }.All(seat => !unqualified.HasQualifiedBinding(seat)),
            "An unconfigured scenario team role and the same printed skill definition do not implicitly qualify a Lord skill.");
        unqualified.Cold();
        Need(!JsonSerializer.Serialize(Member(false)).Contains(nameof(ShanheCombatantDefinition.QualifiedPrintedLordSkillIds), StringComparison.Ordinal) &&
            !ShanheBattleSetup.ValueEquals(setup, Recipe(Member(false), Member())) &&
            ShanheCampaignContent.GetContentHash(Definition(member)) != ShanheCampaignContent.GetContentHash(Definition(Member(false))),
            "Null keeps the old recipe shape, while an explicit qualification changes encounter equality and the actual campaign content fingerprint.");
        var frozen = ShanheCampaignContent.FreezeAndValidate(Definition(member));
        Need(frozen.Nodes.Single().Encounters!.Single().Enemies.Single().QualifiedPrintedLordSkillIds is IList<string> { IsReadOnly: true },
            "Campaign publication also freezes the nested qualification list.");
        foreach (var ids in new IReadOnlyList<string>[] { [Baonue, Baonue], [Jiuchi], ["fixture:unknown-lord"], [ExtraLord] })
            Reject(() => Scene.Start(Recipe(Member() with { QualifiedPrintedLordSkillIds = ids, ExtraSkillIds = [ExtraLord] }, Member(false)), registry));
        Reject(() => Scene.Start(Recipe(Member() with { GeneralId = Ally, ExtraSkillIds = [Baonue], QualifiedPrintedLordSkillIds = [Baonue] }, Member(false)), registry));
        Reject(() => Scene.Start(Recipe(Member() with { GeneralId = NoProgramGeneral, QualifiedPrintedLordSkillIds = [NoProgramLord] }, Member(false)), registry));
        VerifySameGrantGeneralReplacementInvalidatesOnlyConfiguredIndex(registry);
    }

    private static void VerifySameGrantGeneralReplacementInvalidatesOnlyConfiguredIndex(ContentRegistry registry)
    {
        // This is an isolated shared-index model, never a mutation of a running
        // game's state. Both templates deliberately retain the same grant ids.
        var printed = new GeneralSkillDefinition("暴虐", "原印刷主公技") { ContentId = Baonue, Tags = SkillTag.Lord };
        var original = new GeneralDefinition(Boss, "原模板", "", [printed], "qun", 8);
        var replacement = original with { Id = "fixture:s5-dong-replacement" };
        var p = new CharacterState { Seat = 2, Name = "独立模型", IsHuman = false, Role = Role.TeamB, RoleRevealed = true,
            TeamId = ShanheBattleSetup.EnemyTeamId, TeamRevealed = true, FactionRevealed = true,
            General = original, GeneralSelected = true, GeneralRevealed = true, Hp = 4, MaxHp = 8 };
        var index = new MatchSkillBindingIndex(registry.GetSkill, false, additionalPrintedLordQualification: (owner, grant) =>
            owner.Seat == 2 && owner.General.Id == Boss && grant.SkillId == Baonue && grant.SourceId == CharacterState.PrimarySkillSource &&
            grant.GrantId == $"{CharacterState.PrimarySkillSource}:{Baonue}" && grant.SkillInstanceId == grant.GrantId);
        var first = index.GetShard(p); var revision = p.SkillGrants.Revision;
        Need(first.HasSkill(Baonue), "The configured shared index admits the exact original printed grant.");
        p.General = replacement;
        Need(p.SkillGrants.Revision == revision && !index.GetShard(p).HasSkill(Baonue) && first.HasSkill(Baonue),
            "A real model-template assignment with unchanged skill-grant revision still invalidates the configured GeneralId stamp, without mutating a previously prepared shard.");
        p.General = original;
        Need(index.GetShard(p).HasSkill(Baonue), "The same exact original template can qualify its still-live printed grant again.");
        var grantId = $"{CharacterState.PrimarySkillSource}:{Baonue}"; p.SkillGrants.SetEnabled(grantId, false);
        Need(!index.GetShard(p).HasSkill(Baonue), "Explicit recipe qualification never bypasses actual grant disablement.");
        var ordinary = new MatchSkillBindingIndex(registry.GetSkill, false);
        p.SkillGrants.SetEnabled(grantId, true);
        Need(!ordinary.GetShard(p).HasSkill(Baonue), "An ordinary index without the scenario callback keeps the existing non-Lord boundary.");
    }

    public static void TwoQualifiedScenarioBaonueOwnersJudgeEachDamagePointThroughFullArmorCold()
    {
        var s = Scene.Start(); s.Use("armor-two", [1]); var victimHp = s.V(1).Hp;
        s.Cold(); s.Begin("hit-two", [1]); s.AtPaidDamageReturn();
        var judgments = s.Events<JudgmentRequestedEvent>().Where(e => e.Reason == Reason).ToArray();
        var claims = s.Events<ProgramJudgmentCardClaimedEvent>().Where(e => e.SkillId == Baonue).ToArray();
        var bindings = s.Events<ProgramBindingStartedEvent>().Where(e => e.SkillId == Baonue && e.Window == SkillProgramTriggerWindow.AfterDamageApplied).ToArray();
        Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 2, SourceLess: false,
                ArmorPayment: { ArmorBefore: 2, ArmorAbsorbed: 2, ArmorAfter: 0, HpLossAmount: 0 } }] &&
            s.V(1).Hp == victimHp && s.V(1).Armor == 0 &&
            new[] { 2, 3 }.All(owner => s.V(owner).Hp == 6 && s.V(owner).HandCount == 2) &&
            judgments.Length == 4 && judgments.Select(e => e.ResolutionId).Distinct().Count() == 4 && claims.Length == 4 &&
            bindings.Length == 4 && bindings.GroupBy(e => (e.OwnerSeat, e.SkillInstanceId)).Count() == 2 &&
            new[] { 2, 3 }.All(owner => judgments.Count(e => e.TargetSeat == owner) == 2 && claims.Count(e => e.OwnerSeat == owner) == 2 &&
                bindings.Count(e => e.OwnerSeat == owner && e.SkillInstanceId == $"{CharacterState.PrimarySkillSource}:{Baonue}") == 2) &&
            s.Events<RecoveryAppliedEvent>().Count(e => e.TargetSeat is 2 or 3 && e.Amount == 1) == 4 &&
            s.Events<AfterDamageEvent>() is [{ TargetSeat: 1, Amount: 2 }],
            "One actual two-point damage fully spent on Armor still supplies two Baonue points to each independent qualified observer; four judgments, recoveries and physical claims finish exactly once.");
        foreach (var claim in claims)
        {
            var judgment = judgments.Single(e => e.ResolutionId == claim.JudgmentFrameId);
            var resolved = s.Events<JudgmentResolvedEvent>().Single(e => e.ResolutionId == claim.JudgmentFrameId);
            Need(judgment.TargetSeat == claim.OwnerSeat && resolved.TargetSeat == claim.OwnerSeat && resolved.Suit == Suit.Spade &&
                resolved.CardId == claim.CardId && s.V(claim.OwnerSeat).Hand.Any(card => card.Id == claim.CardId) &&
                s.G.CardMovements.Count(m => m.CardId == claim.CardId && m.From == CardLocation.Judgment(claim.OwnerSeat) &&
                    m.To == CardLocation.Hand(claim.OwnerSeat) && m.Reason.Value == $"skill-program.{Baonue}.claimJudgmentCard") == 1,
                "Every real Spade judgment is paired to one original physical claim and its own independent Dong Zhuo.");
        }
        Need(new[] { HpWatch, GainWatch }.All(skill => s.Events<ProgramBindingStartedEvent>().Count(e => e.SkillId == skill) == 4 &&
                s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == skill && e.Completed) == 4) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver).InstructionIndex == 2,
            "All eight actual NPC-owned recovery/gain children have completed before the human damage producer's genuine paid-return choice; invisible NPC prompts are not fabricated.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.Events<JudgmentRequestedEvent>().Count(e => e.Reason == Reason) == 4 &&
            s.Events<ProgramJudgmentCardClaimedEvent>().Count(e => e.SkillId == Baonue) == 4 &&
            new[] { 2, 3 }.All(owner => s.V(owner).Hp == 6 && s.V(owner).HandCount == 2),
            "Resuming the exact paid damage producer after cold recovery repeats no point, recovery or original-card claim."); s.Cold();
    }

    public static void DisabledPrintedSourceOwnDamageSourceLessAndJueqingDoNotInventBaonue()
    {
        var inactive = Scene.Start(); inactive.Use("lose-three", [2]);
        Need(inactive.V(2).Hp == 1 && !inactive.HasQualifiedBinding(2),
            "A genuine HP payment activates the existing HP-one suppressor; explicit recipe qualification does not keep Baonue enabled. " + inactive.BindingDiagnostic());
        inactive.Cold(); inactive.Begin("hit-two", [1]); inactive.FinishBaonue();
        Need(inactive.Events<JudgmentRequestedEvent>().Count(e => e.Reason == Reason && e.TargetSeat == 2) == 0 &&
            inactive.Events<JudgmentRequestedEvent>().Count(e => e.Reason == Reason && e.TargetSeat == 3) == 2 &&
            inactive.Events<ProgramJudgmentCardClaimedEvent>().Count(e => e.SkillId == Baonue && e.OwnerSeat == 3) == 2,
            "Future real damage skips only the suppressed Dong Zhuo while the other explicitly qualified owner still receives both independent points."); inactive.Cold();

        var sourceLess = Scene.Start(); sourceLess.Use("armor-self"); sourceLess.Use("source-less");
        Need(sourceLess.Events<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 2, SourceLess: true,
                ArmorPayment: { ArmorAbsorbed: 2, HpLossAmount: 0 } }] && sourceLess.V().Hp == 8 &&
            !sourceLess.Events<ProgramBindingStartedEvent>().Any(e => e.SkillId == Baonue),
            "A genuine source-less damage paid entirely by Armor remains real damage but cannot name another Qun source for either Baonue owner."); sourceLess.Cold();

        var own = Scene.Start(Recipe(Member(), Member(false))); own.Use("target-self", [2]); own.Use("incoming", [2]);
        Need(own.Events<DamageAppliedEvent>().Select(e => (e.SourceSeat, e.TargetSeat)).SequenceEqual([(2, 2), (2, 0)]) &&
            !own.Events<ProgramBindingStartedEvent>().Any(e => e.SkillId == Baonue) &&
            own.HasQualifiedBinding(2),
            "An enabled Dong Zhuo's own source is excluded for both its actual self-damage and its damage to another character; a registered but unqualified second observer does not borrow eligibility.");
        own.Begin("hit-two", [1]); own.FinishBaonue();
        Need(own.Events<JudgmentRequestedEvent>().Count(e => e.Reason == Reason && e.TargetSeat == 2) == 2,
            "The same still-enabled qualified owner subsequently responds normally to another Qun source, proving the exclusion was about source ownership."); own.Cold();

        var jueqing = Scene.Start(Recipe(Member(), Member(), jueqing: true)); jueqing.Use("armor-two", [1]);
        jueqing.Use("lose-one", [1]); jueqing.PlaySlash(1);
        Need(jueqing.V(1) is { Hp: 6, Armor: 2 } && jueqing.Events<DamageReplacedWithHpLossEvent>() is [{ TargetSeat: 1, Amount: 1 }] &&
            jueqing.Events<DamageAppliedEvent>().Length == 0 && jueqing.Events<AfterDamageEvent>().Length == 0 &&
            jueqing.Events<JudgmentRequestedEvent>().All(e => e.Reason != Reason) &&
            !jueqing.Events<ProgramBindingStartedEvent>().Any(e => e.SkillId == Baonue),
            "Real loseHp and the original Jueqing physical Slash replacement bypass Armor and never manufacture damage-point Baonue opportunities."); jueqing.Cold();
    }

    private static ShanheCombatantDefinition Member(bool qualify = true) => new(Boss, MaxHpBonus: 4, Hp: 4, ExtraSkillIds: [HpWatch, GainWatch, Suppress])
        { StartingHand = [], QualifiedPrintedLordSkillIds = qualify ? [Baonue] : null };
    private static ShanheBattleSetup Recipe(ShanheCombatantDefinition a, ShanheCombatantDefinition b, bool jueqing = false) =>
        new(83, 1, "fixture-s5-dong-zhuo", Owner, 8, 0, 0, 0, 0, jueqing ? [Jueqing] : [],
            jueqing ? [new(CardKind.Slash, Suit.Spade, 7)] : [], [], [a, b], [new(Ally) { StartingHand = [] }], new string('d', 64));
    private static ShanheCampaignDefinition Definition(ShanheCombatantDefinition member) => new("fixture-dong-qualification", "实际主公资格",
        [Owner], ["gate"], [new("gate", "实际关卡", ShanheNodeKind.BossBattle, [], [new("battle", "明确配方", [member])])], []);
    private static ContentRegistry Registry() => ContentRegistry.Build(new StandardContentPackage(), new Fixture());
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        var rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { rejected = true; }
        Need(rejected, "The scenario rejects duplicate, unknown, non-Lord, extra-only and incomplete/unprogrammed Lord qualification inputs.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private SkillBindingShard Binding(int seat)
        {
            // Trusted read-only diagnostics, matching the retained binding-index
            // checks. A displayed printed skill is not an effective rule query.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", flags)!.GetValue(G)!;
            return (SkillBindingShard)typeof(GameEngine).GetMethod("GetSkillBindingShard", flags)!.Invoke(G, [players[seat]])!;
        }
        public bool HasQualifiedBinding(int seat) => Binding(seat).ActiveGrants.Any(grant => grant.SkillId == Baonue &&
            grant.SourceId == CharacterState.PrimarySkillSource && grant.GrantId == $"{CharacterState.PrimarySkillSource}:{Baonue}" && grant.SkillInstanceId == grant.GrantId);
        public string BindingDiagnostic() => JsonSerializer.Serialize(Enumerable.Range(0, 4).Select(seat => new
        {
            Seat = seat, V(seat).Role, V(seat).TeamId, V(seat).GeneralId, V(seat).Hp, V(seat).MaxHp,
            Shown = V(seat).Skills?.Select(skill => skill.ContentId),
            Active = Binding(seat).ActiveGrants.Select(grant => new { grant.SkillId, grant.SourceId, grant.GrantId, grant.SkillInstanceId }),
            HpPayments = Events<ProgramSkillHpLostEvent>().Where(e => e.TargetSeat == seat).Select(e => e.Amount)
        }));
        public void Accept(GameCommand command) { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "The real Dong Zhuo command was rejected."); }
        public void Step() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, G.Revision));
        public void Begin(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { Begin(id, targets); Play(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void AtPaidDamageReturn() => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Driver &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void PlaySlash(int target)
        {
            var action = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind)); Play();
        }
        public void FinishBaonue()
        {
            AtPaidDamageReturn(); Cold(); Continue(); Play();
        }
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Driver && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Continue(); continue; }
                Need(G.State.Winner == Winner.None && P?.PlayerSeat != 0,
                    "Unexpected bounded Dong Zhuo boundary: " + JsonSerializer.Serialize(new { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, P.Prompt, Skill = P.SkillPrompt?.SkillId },
                        Judgments = Events<JudgmentRequestedEvent>().Where(e => e.Reason == Reason), Claims = Events<ProgramJudgmentCardClaimedEvent>(), Damage = Events<DamageAppliedEvent>() }));
                Step();
            }
            throw new InvalidOperationException("The bounded actual Dong Zhuo continuation did not reach its native child.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the explicit original printed qualification, private hands, each damage point, exact judgment/recovery/claim child and paid material once.");
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every real card remains in one exact zone.");
        }
        public static Scene Start(ShanheBattleSetup? setup = null, ContentRegistry? content = null)
        {
            setup ??= Recipe(Member(), Member()); content ??= Registry();
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, content);
            var s = new Scene(game, content); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-dong-zhuo", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Resource(string suffix)
            {
                var assembly = typeof(StandardContentPackage).Assembly;
                using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var actual = SkillProgramCatalog.Load(Resource("boundary-dong-zhuo.rules.json"), Resource("boundary-dong-zhuo.presentation.json"));
            foreach (var (id, program) in actual.Programs) b.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                { Program = program, ProgramPresentation = actual.Presentations[id], Tags = id == Baonue ? SkillTag.Lord : id is Roulin or Benghuai ? SkillTag.Locked : SkillTag.None });
            var zhang = SkillProgramCatalog.Load(Resource("classic-zhang-chun-hua.rules.json"), Resource("classic-zhang-chun-hua.presentation.json"));
            b.AddSkill(new(Jueqing, zhang.Presentations[Jueqing].Name, zhang.Presentations[Jueqing].Description) { Program = zhang.Programs[Jueqing] });
            object Activation(string id, int count, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = count, maxTargets = count,
                targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            object Pause() => new { op = "chooseOption", target = "owner", resultBind = "paid-child", options = new[] { new { id = "continue" } } };
            var definitions = new object[] {
                new { id = Driver, revision = 1, activations = new[] {
                    Activation("armor-two", 1, new { op = "grantArmor", target = "selectedTarget", amount = 2 }),
                    Activation("armor-self", 0, new { op = "grantArmor", target = "owner", amount = 2 }),
                    Activation("hit-two", 1, new { op = "damage", target = "selectedTarget", amount = 2 }, Pause()),
                    Activation("lose-three", 1, new { op = "loseHp", target = "selectedTarget", amount = 3 }),
                    Activation("lose-one", 1, new { op = "loseHp", target = "selectedTarget", amount = 1 }),
                    Activation("source-less", 0, new { op = "receiveOwnerDamage", target = "owner", amount = 2 }),
                    Activation("incoming", 1, new { op = "damage", target = "owner", sourceRef = new { kind = "selectedTarget" }, amount = 2 }),
                    Activation("target-self", 1, new { op = "damage", target = "selectedTarget", sourceRef = new { kind = "selectedTarget" }, amount = 1 }) } },
                new { id = ExtraLord, revision = 1, activations = new[] { Activation("draw-one", 0, new { op = "draw", target = "owner", amount = 1 }) } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = HpWatch, revision = 1, triggers = new[] { new { id = "actual-recovery-child", window = "afterHpRecovered", subject = "owner", optional = false, effects = new[] { Pause() } } } },
                new { id = GainWatch, revision = 1, triggers = new[] { new { id = "actual-claim-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                    movementOccurrence = "perBatch", movementReasons = new[] { $"skill-program.{Baonue}.claimJudgmentCard" }, optional = false, effects = new[] { Pause() } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实董卓测试操作", description = "只调用现有护甲、伤害、失去体力与实体牌使用",
                    optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [ExtraLord] = new { name = "额外主公技能", description = "只有显式额外grant，不能冒充原印刷槽" }, [Quiet] = new { name = "安静NPC", description = "保留原生阶段" } };
            foreach (var id in new[] { HpWatch, GainWatch }) labels[id] = new { name = "真实支付子窗口", description = "只在实际回复或领取判定原牌后暂停",
                optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id], Tags = id == ExtraLord ? SkillTag.Lord : SkillTag.None });
            b.AddSkill(new(Suppress, "实际低体力失效", "HP1时使用既有共享抑制规则") { SuppressionRule = new(1) });
            b.AddSkill(new(NoProgramLord, "无程序主公技", "供严格输入拒绝") { Tags = SkillTag.Lord });
            b.AddGeneral(new(Owner, "实际群伤害来源", "", Driver, "qun", 8));
            b.AddGeneral(new(Ally, "实际伤害目标", "", Quiet, "shu", 8));
            b.AddGeneral(new(Boss, "当前董卓技能组夹具", "", Jiuchi, "qun", 4, [Roulin, Benghuai, Baonue]));
            b.AddGeneral(new(NoProgramGeneral, "缺程序印刷槽", "", NoProgramLord, "qun", 8));
            b.AddDeck(new(Deck, "固定真实黑桃判定牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "明确印刷主公技资格的山河对局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, Boss, NoProgramGeneral], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
