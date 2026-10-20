using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheShihunChecks
{
    private const string Skill = StandardShanheS5CaoxingHighPackage.ShihunId;
    private const string Jiaozi = "classic:jiaozi", Shidu = StandardShanheS5ShiduPackage.ShiduId, Liushi = "ol:liu-shi";
    private const string Owner = "fixture:shihun-owner", Victim = "fixture:shihun-victim", Peer = "fixture:shihun-peer", Deck = "fixture:shihun-deck";
    private const string Driver = "fixture:shihun-driver", DamageWatch = "fixture:shihun-damage", Changed = "fixture:shihun-changed", Gain = "fixture:shihun-gain";
    private const string Own = "fixture:shihun-owned", Limited = "fixture:shihun-limited", Active = "fixture:shihun-active", Old = "fixture:shihun-old";
    private const string Locked = "fixture:shihun-locked", TargetGate = "fixture:shihun-target-gate", SourceGate = "fixture:shihun-source-gate", Quiet = "fixture:shihun-quiet", Prevent = "fixture:shihun-prevent";
    private const string Jueqing = "classic:jueqing", Xiaoming = "shanhe:xiaoming";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:shihun-production", StandardShanheS5CaoxingHighPackage.CaoxingShihunId, 4, 0, 0, 0, 0,
            [], [], [], [new("classic:cao-cao")], [], new string('d', 64))));

    public static void DirectSlashAcquiresCurrentEligibleSkillOncePerDamageAndReplaysCold()
    {
        var s = Scene.Start("dynamic"); s.Use("armor-one", [2]); s.Card(CardKind.Alcohol, []); s.Play();
        var first = s.Card(CardKind.Slash, [2]); s.Until(s.AtChanged);
        var paid = s.Paid().Single(); s.AssertPaid(paid, first, [Limited, Active]);
        Need(s.E<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 2, RemainingHp: 19,
                ArmorPayment: { ArmorBefore: 1, ArmorAbsorbed: 1, HpLossAmount: 1 } }] &&
            s.E<HpConditionalSkillGrantChangedEvent>().Any(f => f.OwnerSeat == 2 && f.SkillId == Old && !f.Added) &&
            s.E<HpConditionalSkillGrantChangedEvent>().Count(f => f.OwnerSeat == 2 && f.SkillId is (Limited or Active) && f.Added) == 2 &&
            s.Registry.GetSkill(Limited).Tags.HasFlag(SkillTag.Limited) && !s.Registry.GetSkill(Limited).Tags.HasFlag(SkillTag.Locked),
            "One actual two-point wine Slash pays Armor and HP once; the post-damage pool includes both newly effective skills, including a non-locked Limited skill, and excludes the expired skill, locked plumbing and the already-owned skill.");
        s.Privacy(); s.Cold(); s.Continue(); s.Finish(first); s.UseCopied(paid.SelectedSkillId);
        var second = s.Card(CardKind.Slash, [2]); s.Until(s.AtChanged);
        var next = s.Paid().Last(); var remaining = new[] { Limited, Active }.Single(id => id != paid.SelectedSkillId);
        s.AssertPaid(next, second, [remaining]);
        Need(s.Paid().Length == 2 && next.Source.SkillInstanceId == paid.Source.SkillInstanceId && next.ProgramFrameId != paid.ProgramFrameId &&
            next.GrantedGrant.SkillInstanceId != paid.GrantedGrant.SkillInstanceId && s.Has(paid.SelectedSkillId) && s.Has(remaining) &&
            s.Has(paid.SelectedSkillId, 2) && s.Has(remaining, 2),
            "A second independent real Slash copies the remaining eligible identity once; neither the previous grant nor either victim skill is removed or disabled.");
        s.Cold(); s.Continue(); s.Finish(second); s.UseCopied(remaining);
        var third = s.Card(CardKind.Slash, [2]); s.Finish(third);
        Need(s.Paid().Length == 2 && s.E<SkillsAcquiredEvent>().Count(f => f.SourceSkillId == Skill) == 2 &&
            s.E<ProgramRandomDamageTargetSkillCompletedEvent>().Select(f => f.ProgramFrameId).ToHashSet().SetEquals(s.Paid().Select(f => f.ProgramFrameId)),
            "After both identities are genuinely owned, a third paid Slash has an empty pool and cannot sample or issue another grant."); s.Cold();

        var armor = Scene.Start("armor"); armor.Use("armor-two", [2]); armor.Card(CardKind.Alcohol, []); armor.Play();
        var full = armor.Card(CardKind.Slash, [2]); armor.Until(armor.AtChanged);
        armor.AssertPaid(armor.Paid().Single(), full, [Limited, Active]);
        Need(armor.E<DamageAppliedEvent>() is [{ Amount: 2, RemainingHp: 19,
                ArmorPayment: { ArmorAbsorbed: 2, HpLossAmount: 0, ActualHpLost: 0 } }] && armor.Paid().Length == 1,
            "A real damage occurrence entirely paid by two Armor still copies exactly one skill; HP loss is not substituted for the native damage amount.");
        armor.Cold(); armor.Continue(); armor.Finish(full);
        VerifyLoaderBoundaries();
    }

    public static void DodgedChainedAndReplacedDamageDoNotAcquireSkillsAndPaidSourceLossKeepsGrant()
    {
        foreach (var name in new[] { "dodged", "prevent", "jueqing", "maximum", "program", "source-less", "unowned" })
        {
            var s = Scene.Start(name);
            if (name is "program" or "source-less") s.Use(name == "program" ? "damage" : "receive", name == "program" ? [2] : []);
            else { var card = s.Card(CardKind.Slash, [2]); s.Finish(card); }
            Need(s.Paid().Length == 0 && s.E<SkillsAcquiredEvent>().All(f => f.SourceSkillId != Skill) &&
                (name == "dodged" ? s.E<CardRespondedEvent>().Any(f => f.ResponderSeat == 2 && f.EffectiveCardKind == CardKind.Dodge) && s.E<DamageAppliedEvent>().Length == 0 :
                 name == "prevent" ? s.E<ProgramDamagePreventedEvent>().Length == 1 && s.E<DamageAppliedEvent>().Length == 0 :
                 name == "jueqing" ? s.V(2).Hp == 18 && s.E<DamageAppliedEvent>().Length == 0 :
                 name == "maximum" ? s.V(2).MaxHp == 19 && s.E<CardDamageReplacedWithMaximumHpLossEvent>().Length == 1 && s.E<DamageAppliedEvent>().Length == 0 :
                 name == "source-less" ? s.E<DamageAppliedEvent>() is [{ SourceLess: true, TargetSeat: 0, Amount: 1 }] : s.E<DamageAppliedEvent>().Length == 1),
                "Only a genuine direct Slash damage occurrence can acquire a skill; dodge, prevention, early replacement, program damage, source-less damage and catalog-only presence confer no authority. " + s.Diagnostic()); s.Cold();
        }
        var chain = Scene.Start("chain"); chain.Use("armor-two", [2]); chain.Use("armor-two", [3]);
        chain.Card(CardKind.IronChain, [2, 3]); chain.Play(); chain.Card(CardKind.Alcohol, []); chain.Play();
        var fire = chain.Card(CardKind.FireSlash, [2]); chain.Until(chain.AtChanged);
        chain.AssertPaid(chain.Paid().Single(), fire, [Limited]); chain.Cold(); chain.Continue(); chain.Finish(fire);
        Need(chain.E<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 2, ArmorPayment.HpLossAmount: 0 },
                { TargetSeat: 3, Amount: 2, ArmorPayment.HpLossAmount: 0 }] && chain.E<ChainedDamagePropagatedEvent>() is [{ TargetSeat: 3, Amount: 2 }] &&
            chain.Paid().Length == 1 && chain.Has(Limited) && !chain.Has(Active),
            "The original Fire Slash copies from its actual direct target; the equally real Armor-paid chained damage cannot copy the unrelated recipient's available skill."); chain.Cold();

        var virtualUse = Scene.Start("liushi");
        var cost = virtualUse.V().Hand.Single(c => c.Suit == Suit.Heart).Id;
        virtualUse.Accept(new UseProgramSkillCommand(0, Liushi, "topdeck-heart-slash", [cost], [2], virtualUse.G.Revision, virtualUse.P!.PromptId));
        virtualUse.Until(virtualUse.AtChanged); var virtualReceipt = virtualUse.Paid().Single(); virtualUse.AssertPaid(virtualReceipt, null, [Limited]);
        virtualUse.Cold(); virtualUse.Continue(); virtualUse.Play();
        Need(virtualUse.E<ProgramLiushiEffectAppliedEvent>() is [{ OwnerSeat: 0, TargetSeat: 2, MarkerCount: 1 } marker] &&
            marker.CardUseFrameId == virtualReceipt.CardUseFrameId && marker.ActionId == virtualReceipt.CardActionId &&
            virtualUse.E<CardUseDeclaredEvent>() is [{ SourceSeat: 0, CardKind: CardKind.Slash }] &&
            virtualUse.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile) == 1,
            "The real Heart-card Liushi activation pays its original topdeck entity and creates one native virtual Slash whose same paid damage owns both one persistent Liushi layer and one Shihun grant."); virtualUse.Cold();

        foreach (var name in new[] { "paid-loss", "paid-death" })
        {
            var s = Scene.Start(name); var slash = s.Card(CardKind.Slash, [2]); s.Until(s.AtChanged);
            var paid = s.Paid().Single(); s.AssertPaid(paid, slash, [Limited]); s.Cold(); s.Continue();
            if (name == "paid-loss")
            {
                s.Until(() => s.At(Changed) && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "lost"));
                Need(s.V().Hp == 19 && !s.Has(Skill) && s.Has(Limited), "A real skill-change child withdraws the HP-derived Shihun source while its independently paid copied skill remains enabled.");
                s.Cold(); s.Continue(); s.Finish(slash); s.UseCopied(Limited);
            }
            else s.Until(() => !s.V().IsAlive && s.E<ProgramRandomDamageTargetSkillCompletedEvent>().Any(f => f.ProgramFrameId == paid.ProgramFrameId) &&
                s.E<CardUseFinishedEvent>().Any(f => f.CardId == slash));
            Need(s.Paid().Length == 1 && s.E<ProgramRandomDamageTargetSkillCompletedEvent>().Count(f => f.ProgramFrameId == paid.ProgramFrameId) == 1 &&
                s.G.ResolutionStack.All(f => f.Id != paid.ProgramFrameId) && s.E<CardUseFinishedEvent>().Count(f => f.CardId == slash) == 1 &&
                (name == "paid-loss" ? !s.Has(Skill) && s.Has(Limited) : s.G.State.Winner == Winner.None && s.V(1).IsAlive && s.E<PlayerDiedEvent>().Count(f => f.VictimSeat == 0) == 1),
                "Loss or genuine death after the atomic random grant drains its actual SkillsChanged child and original use once, without re-sampling or rolling back the independent grant. " + s.Diagnostic()); s.Cold();
        }
    }

    public static void ShiduHandLeadershipChangesJiaoziDamageBeforeAndAfterHalfReturnCold()
    {
        var s = Scene.Start("jiaozi"); s.Use("armor-two", [3]); s.Use("armor-two", [0]);
        var king = s.V().Hand.Single(c => c.Rank == 13).Id;
        s.Accept(new UseProgramSkillCommand(0, Shidu, "contest-take-return", [king], [2], s.G.Revision, s.P!.PromptId));
        s.Until(() => s.P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card"));
        s.Answer(c => c.Cards.SequenceEqual([king])); s.Until(() => s.At(Gain));
        Need(s.E<PindianResultDeterminedEvent>().Single(f => f.SkillId == Shidu).Result is { SourceWon: true, SourceRank: 13 } &&
            s.V().HandCount == 7 && s.V(2).HandCount == 0 && s.V(3).HandCount == 4 && s.E<ProgramPindianHalfReturnCountEvent>().Length == 0,
            "The actual hand Pindian consumes one source card and obtains the victim's remaining four, making seven genuinely held cards uniquely greatest before half-hand capture.");
        s.Cold(); s.Continue();
        s.Until(() => s.At(Gain) && s.P!.Choices.Any(c => c.Targets.SequenceEqual([3]))); s.Answer(c => c.Targets.SequenceEqual([3])); s.Until(s.AtDamage);
        Need(s.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2, ArmorPayment.HpLossAmount: 0 }] &&
            s.E<ProgramCardDamageModifiedEvent>().Where(f => f.Source.SkillId == Jiaozi).ToArray() is [{ TargetSeat: 3, BaseAmount: 1, ModifiedAmount: 2 }],
            "Current uniquely greatest ownership immediately increases the acquisition child's genuine program damage, even when two Armor absorb all of it.");
        s.Cold(); s.Continue(); s.Until(() => s.At(Gain)); s.Continue();
        s.Until(() => s.P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "returned"));
        Need(s.E<ProgramPindianHalfReturnCountEvent>() is [{ HandCount: 7, ReturnCount: 3 }], "The half return captures the actual seven-card hand after the damage child, without an invented extra draw.");
        s.Cold();
        foreach (var card in s.V().Hand.Take(3).Select(c => c.Id).ToArray()) s.Answer(c => c.Cards.SequenceEqual([card]));
        s.Play(); Need(s.V().HandCount == 4 && s.V(2).HandCount == 3 && s.V(3).HandCount == 4, "Returning three original Hand entities leaves a real tie with the unchanged peer's four cards.");
        s.Use("damage", [3]);
        Need(s.E<DamageAppliedEvent>().Last() is { TargetSeat: 3, Amount: 1, RemainingHp: 19 } && s.E<ProgramCardDamageModifiedEvent>().Count(f => f.Source.SkillId == Jiaozi) == 1,
            "The same source immediately loses Jiaozi's bonus when the actual half return creates a tie; the former unique maximum is not cached."); s.Cold();
        s.Use("draw", []); Need(s.V().HandCount == 5, "One genuine draw makes the owner uniquely greatest again.");
        s.Begin("duel", [2, 0]); s.Until(s.AtDamage);
        Need(s.E<DamageAppliedEvent>().Last() is { SourceSeat: 2, TargetSeat: 0, Amount: 2, RemainingHp: 20,
                ArmorPayment: { ArmorAbsorbed: 2, HpLossAmount: 0 } } &&
            s.E<ProgramCardDamageModifiedEvent>().Where(f => f.Source.SkillId == Jiaozi).Select(f => f.TargetSeat).SequenceEqual([3, 0]),
            "A real ordered native Duel then damages the uniquely greatest owner from a different source, proving the recipient-side Jiaozi bonus and exact Armor payment rather than only outgoing Slash behavior."); s.Cold(); s.Continue(); s.Play();
        VerifyActualRecipes();
    }

    private static void VerifyActualRecipes()
    {
        var ids = new[] { StandardShanheS5CaoxingHighPackage.CaoxingJiaoziId, StandardShanheS5CaoxingHighPackage.CaoxingShihunId };
        var old = StandardShanheCampaignCatalog.CreateDefinition(3, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(n => n.Encounters ?? []).All(e => e.Enemies.All(m => !ids.Contains(m.GeneralId))), "The high local S5 combinations are absent from the 2024 edition.");
        for (var tier = 1; tier <= 3; tier++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounters = definition.Nodes.Single(n => n.Id == "guangcheng").Encounters!;
            foreach (var id in ids)
            {
                var encounter = encounters.SingleOrDefault(e => e.Enemies.Any(m => m.GeneralId == id));
                Need((encounter is not null) == (tier == 3), "The two explicit local high combinations appear only in the selected local third tier.");
                if (encounter is null) continue;
                var setup = new ShanheBattleSetup(83, 1, "guangcheng", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, encounter.Allies ?? [], ShanheCampaignContent.GetContentHash(definition));
                var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var game = StandardShanheCampaignCatalog.CreateBattle(setup);
                Need(game.Submit(new StartGameCommand()).Accepted, "The actual high-combination recipe must start.");
                var enemy = game.CreateSnapshot(0).Players.Single(p => p.GeneralId == id);
                string[] printed = id == ids[0] ? [Liushi, "ol:zhan-wan", Shidu, "classic:tianbian", Jiaozi] : [Liushi, "ol:zhan-wan", Shidu, "classic:tianbian", Jiaozi, Skill];
                Need(enemy is { Hp: 4, MaxHp: 4, HandCount: 4, Armor: 0 } && enemy.Equipment.Count == 0 &&
                    enemy.Skills?.Select(s => s.ContentId).ToHashSet().SetEquals(printed) == true && encounter.Enemies is [{ MaxHpBonus: 0, Hp: null, InitialHandBonus: 0, DrawBonus: 0, StartingHand: null }] &&
                    (encounter.Allies?.Count ?? 0) == 0, "The actual local encounter starts raw four HP/four hand cards with its exact production skill combination and no invented fixed equipment or growth.");
                var before = State(game); var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                Need(State(cold) == before, "The actual conditional high package and native startup replay identically.");
                var plain = setup with { EnemyRoster = [new(StandardShanheS5CaoxingPackage.CaoxingId)] };
                Need(!StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Skill), "Warming a high alias cannot import Shihun into the unowned base recipe.");
                var extra = plain with { ExtraSkillIds = [Skill] };
                Need(StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Skill) &&
                    !StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Skill) && StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Skill),
                    "Extra-skill and plain recipes with the same general IDs remain separate through both conditional cache directions.");
            }
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        string Read(string name) { var a = typeof(StandardContentPackage).Assembly; using var stream = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith('.' + name, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("shanhe-s5-shihun.rules.json"); var presentation = Read("shanhe-s5-shihun.presentation.json"); _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var edit in new Action<JsonObject>[] { t => t["damageOccurrence"] = "perDamagePoint", t => t["subject"] = "owner", t => t["optional"] = true,
            t => t["effects"]![0]!["target"] = "selectedTarget", t => t["effects"]![0]!["condition"] = new JsonObject { ["kind"] = "wounded" } })
        {
            var copy = JsonNode.Parse(rules)!; edit(copy["skills"]![0]!["triggers"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The native random-damage acquisition contract rejects per-point, recipient-owner, optional, foreign-target and conditional instruction variants.");
        }
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint()) });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string name)
    {
        public GameEngine G { get; private set; } = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public bool Has(string id, int seat = 0) => V(seat).Skills?.Any(s => s.ContentId == id) == true;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramRandomDamageTargetSkillReceipt[] Paid() => E<ProgramRandomDamageTargetSkillAcquiredEvent>().Select(f => f.Receipt).ToArray();
        public bool At(string id) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == id;
        public bool AtChanged() => At(Changed);
        public bool AtDamage() => At(DamageWatch);
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, (result.Error?.Message ?? "Rejected real Shihun command.") + " " + Diagnostic()); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Begin(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void Use(string id, int[] targets) { Begin(id, targets); Play(); }
        public int Card(CardKind kind, int[] targets)
        {
            var legal = G.GetHumanLegalActions();
            var implicitSelf = targets.Length == 0 && kind is CardKind.Alcohol or CardKind.Peach or CardKind.DrawTwo;
            var a = legal.FirstOrDefault(a => a.Kind != LegalActionKind.Recast && a.ConversionSource is null && a.CardId is { } id &&
                V().Hand.Any(c => c.Id == id && c.Kind == kind) &&
                (a.TargetSeats.SequenceEqual(targets) || implicitSelf && a.TargetSeats.SequenceEqual([0])));
            if (a is null) throw new InvalidOperationException("Missing actual physical card action: " + JsonSerializer.Serialize(new
            {
                Scene = name, RequestedKind = kind, Targets = targets, Prompt = P?.Kind,
                Hand = V().Hand.Select(c => new { c.Id, c.Kind, c.Suit, c.Rank }),
                Legal = legal.Select(c => new { c.Kind, c.CardId, c.PlayedCardKind, c.TargetSeats, c.ProgramSkillId, Conversion = c.ConversionSource?.SkillId })
            }));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)); return a.CardId.Value;
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Finish(int card) { Play(); Need(E<CardUseFinishedEvent>().Count(f => f.CardId == card) == 1, "The original real card use completes exactly once."); }
        public void UseCopied(string id)
        {
            var before = V().HandCount; Need(G.GetHumanLegalActions().Any(a => a.ProgramSkillId == id), "The randomly acquired real program is immediately available through native legal actions.");
            Accept(new UseProgramSkillCommand(0, id, "draw", [], [], G.Revision, P!.PromptId)); Play();
            Need(V().HandCount == before + 1 && G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == new CardMoveReason("skill-program." + id + ".Draw")) == 1,
                "The copied skill executes its genuine native Draw1 once, rather than merely appearing in skill metadata."); Cold();
        }
        public void Until(Func<bool> stop)
        {
            for (var i = 0; i < 260; i++)
            {
                if (stop()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (p.Kind == DecisionKind.RescueDying) { Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    if (p.Kind == DecisionKind.Nullification) { Answer(c => c.Parameters.GetValueOrDefault("response") == "pass"); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge) { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected actual Shihun boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded real Shihun scene did not reach its named boundary: " + Diagnostic());
        }
        public void AssertPaid(ProgramRandomDamageTargetSkillReceipt r, int? card, string[] candidates)
        {
            var root = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == r.ProgramFrameId);
            var use = G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == r.CardUseFrameId);
            var random = new DeterministicRandom(unchecked((int)r.RandomBefore)); var selected = r.Candidates[random.Next(r.Candidates.Count)];
            Need(r.InstructionIndex == 1 && r.Source.SkillId == Skill && r.Source.OwnerSeat == 0 && r.Source.SkillInstanceId == root.SkillInstanceId && r.GameplayHash == root.GameplayHash &&
                root.RandomDamageTargetSkill is not null && use.Action is { Type: CardActionType.Use, ActorSeat: 0 } action && action.ActionId == r.CardActionId &&
                (card is { } physical ? use.CardId == physical : use.CardId == 0 && use.PhysicalCardIds is { Count: 0 }) &&
                use.TargetSeats.Contains(2) && r.TargetSeat == 2 && r.DamageAmount == E<DamageAppliedEvent>().Last().Amount && r.DamageFrameId > 0 && r.DamageWindowId > 0 &&
                r.Candidates.SequenceEqual(candidates.Order(StringComparer.Ordinal)) && r.Candidates is IList<string> { IsReadOnly: true } && r.SelectedSkillId == selected && random.State == r.RandomAfter &&
                r.GrantedGrant is { TurnExpiry: null, PhaseExpiry: null, NextActualTurnStartExpiry: null } && r.GrantedGrant.SkillId == r.SelectedSkillId && r.GrantedGrant.SkillInstanceId != r.Source.SkillInstanceId &&
                E<SkillsAcquiredEvent>().Count(f => f.PlayerSeat == 0 && f.SourceSkillId == Skill && f.SkillIds.SequenceEqual([r.SelectedSkillId])) == 1 && Has(r.SelectedSkillId),
                "The owning receipt freezes one sorted distinct eligible pool, exact direct Use/damage identity, deterministic RNG transition and independent permanent grant before its genuine SkillsChanged child. " + Diagnostic());
        }
        public void Privacy() => Need(Enumerable.Range(0, 4).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)) &&
            !SnapshotJson.Serialize(G.CreateSnapshot(0)).Contains("RandomDamageTargetSkill", StringComparison.Ordinal), "Trusted random candidates stay off player views and every foreign hand remains private.");
        public void Cold() { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold native replay preserves random candidates, RNG choice, independent grant, actual materials and every paid child without resampling or repaying."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { name, G.State.TurnNumber, G.State.Winner,
            Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.Armor, p.HandCount, p.IsAlive }),
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(c => c.Id) }, Paid = Paid(), Damage = E<DamageAppliedEvent>(), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        public static Scene Start(string name)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(name));
            ShanheStartingCard[] hand = name == "jiaozi" ? [new(CardKind.Nullification, Suit.Club, 13), new(CardKind.Nullification, Suit.Club, 3), new(CardKind.Nullification, Suit.Club, 4), new(CardKind.Nullification, Suit.Club, 5)] :
                name == "chain" ? [new(CardKind.IronChain, Suit.Club, 3), new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.FireSlash, Suit.Diamond, 7)] :
                name == "liushi" ? [new(CardKind.Dodge, Suit.Heart, 7)] :
                [new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.Slash, Suit.Club, 6), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Club, 8)];
            var victim = new ShanheCombatantDefinition(Victim, Hp: name == "dynamic" || name == "jiaozi" ? 20 : 19)
            { StartingHand = name == "jiaozi" ? Enumerable.Range(2, 5).Select(n => new ShanheStartingCard(CardKind.Nullification, Suit.Club, n)).ToArray() : name == "dodged" ? [new(CardKind.Dodge, Suit.Heart, 2)] : [] };
            var setup = new ShanheBattleSetup(83, 1, "fixture:shihun-" + name, Owner, 20, 0, 0, 0, 3, [], hand, [],
                [victim, new(Peer) { StartingHand = name == "jiaozi" ? Enumerable.Range(2, 4).Select(n => new ShanheStartingCard(CardKind.Nullification, Suit.Club, n)).ToArray() : [] }],
                [new(Peer) { StartingHand = [] }], new string('e', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var scene = new Scene(g, r, name); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }

    private sealed class Fixture(string name) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-shihun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cao-xing");
            foreach (var id in new[] { Skill, Shidu, Jiaozi, Jueqing, Xiaoming, "classic:tianbian" }) b.AddSkill(Production.Value.GetSkill(id));
            var defs = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string id) => new { op = "chooseOption", target = "owner", resultBind = id, options = new[] { new { id } } };
            void Add(string id, string key, object value, params string[] options) { defs.Add(new() { ["id"] = id, ["revision"] = 1, [key] = value }); labels[id] = new { name = id, description = "真实杀伤害、独立随机授技与实时手牌比较", optionLabels = options.ToDictionary(x => x, x => x) }; }
            object Act(string id, int targets, object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            Add(Driver, "activations", new[] { Act("armor-one", 1, [new { op = "grantArmor", target = "selectedTarget", amount = 1 }]), Act("armor-two", 1, [new { op = "grantArmor", target = "selectedTarget", amount = 2 }]),
                Act("damage", 1, [new { op = "damage", target = "selectedTarget", amount = 1 }]), Act("receive", 0, [new { op = "receiveOwnerDamage", target = "owner", amount = 1 }]),
                Act("draw", 0, [new { op = "draw", target = "owner", amount = 1 }]), Act("duel", 2, [new { op = "startVirtualDuel", target = "owner" }]) });
            defs.Single(d => Equals(d["id"], Driver))["modifiers"] = new[] { new { id = "physical-slash-range", query = "attackRange", operation = "add", value = 2, priority = 0 } };
            foreach (var id in new[] { Own, Limited, Active, Old, Locked }) Add(id, "activations", new[] { new { id = "draw", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                targetKind = "anyLiving", usesPerTurn = (int?)null, usesPerGame = id == Limited ? 1 : (int?)null, effects = new[] { new { op = "draw", target = "owner", amount = 1 } } } });
            Add(TargetGate, "hpSkillGrants", new object[] { new { id = "wounded-limited", skillId = Limited, maximumHp = 19 }, new { id = "wounded-active", skillId = Active, maximumHp = 19 }, new { id = "unwounded-only", skillId = Old, minimumHp = 20 } });
            Add(SourceGate, "hpSkillGrants", new[] { new { id = "live-copy-source", skillId = Skill, minimumHp = 20 } });
            Add(DamageWatch, "triggers", new[] { new { id = "actual-damage", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage", optional = false, priority = -100, effects = new[] { Pause("continue") } } }, "continue");
            var changedEffects = new List<object> { Pause("paid") };
            if (name is "paid-loss" or "paid-death") changedEffects.Add(new { op = "loseHp", target = "owner", amount = name == "paid-death" ? 20 : 1 });
            if (name == "paid-loss") changedEffects.Add(Pause("lost"));
            var changedTrigger = new Dictionary<string, object> { ["id"] = "real-acquired-skill-child", ["window"] = "skillsChanged", ["subject"] = "owner", ["optional"] = false,
                ["condition"] = new { kind = "phaseIsPlay" }, ["effects"] = changedEffects.ToArray() };
            if (name is "paid-loss" or "paid-death") { changedTrigger["usageScope"] = "game"; changedTrigger["usageLimit"] = 1; }
            Add(Changed, "triggers", new[] { changedTrigger }, name == "paid-loss" ? ["paid", "lost"] : ["paid"]);
            Add(Gain, "triggers", new[] { new { id = "actual-whole-hand-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                movementReasons = new[] { "skill-program." + Shidu + ".MoveBoundCards" }, usageScope = "game", usageLimit = 1, optional = false,
                effects = new object[] { Pause("obtained"), new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "damage", target = "selectedTarget", amount = 1 }, Pause("after-damage") } } }, "obtained", "after-damage");
            Add(Quiet, "triggers", new[] { new { id = "quiet-real-ai", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } });
            Add(Prevent, "triggers", new[] { new { id = "prevent-real-slash", window = "beforeDamageApplied", subject = "damageTarget", optional = false, effects = new[] { new { op = "preventCurrentDamage", target = "owner" } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = p, ProgramPresentation = catalog.Presentations[id], Tags = id == Limited ? SkillTag.Limited : id is Own or Active or Old ? SkillTag.None : SkillTag.Locked });
            var source = new List<string> { Own, DamageWatch, Changed };
            if (name == "jiaozi") source.AddRange([Shidu, Jiaozi, "classic:tianbian", Gain]);
            else if (name is "paid-loss" or "paid-death") source.Add(SourceGate); else if (name != "unowned") source.Add(Skill);
            if (name == "liushi") source.Add(Liushi); if (name == "jueqing") source.Add(Jueqing); if (name == "maximum") source.Add(Xiaoming);
            b.AddGeneral(new(Owner, "真实噬魂与骄恣来源", "", Driver, "qun", 20, source));
            var target = name is "dynamic" or "armor" or "dodged" or "prevent" or "jueqing" or "maximum" or "program" or "source-less" or "unowned"
                ? new[] { Quiet, Locked, TargetGate } : new[] { Quiet, Locked, Limited };
            if (name == "prevent") target = target.Append(Prevent).ToArray();
            b.AddGeneral(new(Victim, "真实当前技能池受害者", "", Own, "wei", 20, target));
            b.AddGeneral(new(Peer, "真实同行与间接受害者", "", name == "chain" ? Active : Quiet, "wei", 20, name == "chain" ? [Quiet] : []));
            b.AddDeck(new(Deck, "无自然摸牌的固定原牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:nullification", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实动态授技与半手比较", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Victim, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
