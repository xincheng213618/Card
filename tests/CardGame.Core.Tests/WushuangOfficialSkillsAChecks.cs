using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class WushuangOfficialSkillsAChecks
{
    private const string Driver = "fixture:wushuang-a-driver", Quiet = "fixture:wushuang-a-quiet", Hero = "fixture:wushuang-a-hero";
    public static void TiaoxinExpandsOnlyAfterItsRequestedSlashFailsToDamage()
    {
        var s = Scene.Start([3001], tauntSlash: true);
        s.Targeted(3001, "taunt", 3); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass"); s.Play();
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "wushuang:skill-3001") &&
            s.E<WushuangTiaoxinPhaseExpandedEvent>().Length == 0,
            "A requested Slash that actually damages its owner keeps official Tiaoxin at one activation this phase.");

        s = Scene.Start([3001], tauntSlash: true);
        s.Targeted(3001, "taunt", 3); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Cold();
        s.Answer(c => c.Cards.Count == 1 && s.V().Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Dodge)); s.Play();
        s.Need(s.E<WushuangTiaoxinPhaseExpandedEvent>().Length == 1 &&
            s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "wushuang:skill-3001"),
            "An actually dodged requested Slash unlocks the second activation even when its source has no discardable cards left.");
        s.Cold(); s.Targeted(3001, "taunt", 3); s.Play();
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "wushuang:skill-3001"),
            "The expanded phase still permits only two actual Tiaoxin activations.");
        s.End(); s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "wushuang:skill-3001"),
            "A later actual Play phase receives its fresh initial activation.");
    }
    public static void OfficialAuditKeepsSelfGiftsPublicEquipmentCostsAndPhaseLimits()
    {
        var s = Scene.Start([3044], activeEnemy: true); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "wushuang:skill-3044"); s.ActivateTrigger();
        s.Reach(() => s.P?.Choices.Any(c => c.Cards.Count == 1) == true); s.Cold();
        var shown = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); s.Answer(c => c.Cards.SequenceEqual([shown])); s.Play();
        s.Need(s.E<ProgramCardsRevealedEvent>().Any(e => e.SkillId == "wushuang:skill-3044" && e.Cards.Any(c => c.Id == shown)) &&
            !s.G.CardMovements.Any(m => m.CardId == shown && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(0)),
            "Kangkai publicly reveals its self-gift without forging a physical self-to-self move or an equipment-use offer.");

        s = Scene.Start([3140], activeEnemy: true, equipmentOnly: true); var weapon = s.V().Equipment.Single().Id; s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "wushuang:skill-3140"); s.ActivateTrigger();
        s.Reach(() => s.P?.Choices.Any(c => c.Cards.Contains(weapon)) == true); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([weapon]));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        s.Answer(c => c.Targets.SequenceEqual([0])); s.Play();
        var discard = s.G.CardMovements.Single(m => m.CardId == weapon && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile);
        s.Need(s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.InitiatorSeat == 0 && e.ExecutorSeat == 0 &&
            e.SubjectSeat == 0 && e.ActualCount == 1 && e.HandCount == 0 && e.SequenceBefore < discard.Sequence && e.SequenceAfter >= discard.Sequence),
            "Official Beige accepts its actual equipment discard when the owner has no hand cards.");

        s = Scene.Start([3049], extraPlay: true); var turn = s.G.State.TurnNumber; var hp = s.V().Hp;
        s.Qingnang(); s.Play(); s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == turn, "Dangxian has advanced to the next real Play phase within the same actual turn.");
        s.Cold(); s.Qingnang(); s.Play();
        s.Need(s.V().Hp == hp + 2, "Official Qingnang may activate once in each of two real Play phases of the same turn.");
    }
    public static void OriginalYijiAndGuanxuKeepPrivatePhysicalEntitiesAcrossColdReplay()
    {
        var s = Scene.Start([3081]);
        s.Use("damage", 0); s.Reach(() => s.P?.SkillPrompt?.SkillId == "wushuang:skill-3081"); s.ActivateTrigger();
        s.Reach(() => s.Stage == "yiji-give");
        var ids = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().WushuangOriginalSkill!.Cards.ToArray();
        s.Need(ids.Length == 2 && s.G.CreateSnapshot(0).PrivateRevealedCards!.Select(c => c.Id).Intersect(ids).Count() == 2 &&
            Enumerable.Range(1, 3).All(v => s.G.CreateSnapshot(v).PrivateRevealedCards is null or { Count: 0 }),
            "Original Yiji privately views two exact top entities without first drawing them into the owner's hand.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "yiji-give" && c.Cards.Single() == ids[0] && c.Targets.Single() == 0);
        s.Reach(() => s.Stage == "yiji-give"); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "yiji-give" && c.Targets.Single() == 2); s.Play();
        s.Need(s.V().Hand.Any(c => c.Id == ids[0]) && s.G.CreateSnapshot(2).Players[2].Hand.Any(c => c.Id == ids[1]) &&
            ids.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.Reason.Value == "skill-program.wushuang.yiji.give") == 1),
            "Each viewed physical card is assigned once to the chosen living recipient, including self.");

        s = Scene.Start([3282]); s.Activate(3282, "inspect-and-exchange"); s.Reach(() => s.Stage == "begin");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "guanxu-target" && c.Targets.Single() == 2);
        s.Reach(() => s.Stage == "guanxu-view"); s.Cold();
        s.Need(s.G.CreateSnapshot(0).PrivateRevealedCards!.Count >= 5 && s.G.CreateSnapshot(1).PrivateRevealedCards is null or { Count: 0 },
            "Guanxu exposes the target hand and original top five only to its actual owner.");
        var before = s.V(2).HandCount;
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "guanxu-exchange" &&
            s.G.CreateCardZoneDiagnostics().Single(z => z.CardId == c.Cards[0]).CardKind == CardKind.Duel);
        s.Reach(() => s.Stage == "guanxu-discard"); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "guanxu-discard"); s.Play();
        s.Need(s.V(2).HandCount == before - 3 && s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.wushuang:skill-3282.discard") == 3,
            "The optional exact exchange precedes the real mandatory same-suit triple discard.");
    }
    public static void ChoufaRewritesOnlyFrozenHandEntitiesWithoutInspectingTheirNames()
    {
        var s = Scene.Start([3283]); s.Activate(3283, "reveal-and-rewrite"); s.Reach(() => s.Stage == "begin");
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "choufa-target" && c.Targets.Single() == 2); s.Reach(() => s.Stage == "choufa-reveal");
        s.Need(s.G.CreateSnapshot(0).PrivateRevealedCards is null or { Count: 0 }, "Choosing a hidden Choufa hand position never grants an unintended whole-hand inspection.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "choufa-reveal"); s.Play();
        var rewritten = s.E<WushuangChoufaRewrittenEvent>().Single();
        s.Need(rewritten.CardIds.Count > 0 && rewritten.CardIds.All(id => s.G.CreateSnapshot(2).Players[2].Hand.Single(c => c.Id == id).IdentityKind == CardKind.Slash),
            "Only exact current entities of other categories become Slash in the target's own authoritative hand view.");
        s.Cold();
    }
    public static void TairanRepaysActualRecoveredHpAndOnlyItsStillHeldDrawnCards()
    {
        var s = Scene.Start([3265]); var hp = s.V().Hp;
        s.End(); s.Reach(() => s.E<WushuangTairanDrawnEvent>().Length == 1); s.Cold();
        var drawn = s.E<WushuangTairanDrawnEvent>().Single(); var healed = s.E<WushuangTairanHealedEvent>().Single();
        s.Need(healed.Recovered == s.V().MaxHp - hp && s.V().Hp == s.V().MaxHp,
            "Ending Tairan records actual recovered HP and actual native drawn entities as independent debts.");
        s.Play();
        s.Need(s.V().Hp == hp && drawn.CardIds.All(id => s.G.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location != CardLocation.Hand(0)) &&
            s.E<WushuangTairanSettledEvent>().Length == 1,
            "The next real Play start loses the paid recovery exactly once and discards only still-held Tairan cards."); s.Cold();

        s = Scene.Start([3265], recoveryLoss: true); hp = s.V().Hp;
        s.End(); s.Reach(() => s.E<WushuangTairanDrawnEvent>().Length == 1); s.Cold();
        s.Need(s.E<WushuangTairanHealedEvent>().Single().Recovered == s.V().MaxHp - hp &&
            s.V().Hp == s.V().MaxHp - 1,
            "Tairan records the actual recovery invoice before a nested recovery reaction loses HP.");
        s.Play(); s.Need(s.V().Hp == hp - 1,
            "Tairan repays the full actual recovery rather than the net HP change after nested reactions.");
    }
    public static void YashiSuppressesItsActualSelfSourceUntilTheNextStart()
    {
        var s = Scene.Start([3126]); s.Use("damage", 0);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "wushuang:skill-3126"); s.ActivateTrigger();
        s.Reach(() => s.Stage == "begin"); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "yashi-suppress" && c.Targets.Single() == 0); s.Play();
        s.Need(s.E<WushuangYashiSuppressedEvent>().Single().Target == 0 &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Driver),
            "Yashi can suppress the actual self-inflicted source's non-locked skills."); s.Cold();
        s.End(); s.Play();
        s.Need(s.E<WushuangYashiExpiredEvent>().Length == 1 &&
            s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Driver),
            "Self-source Yashi expires at that source's next actual turn start.");
    }
    public static void LingrenBindsItsDamageToTheGuessedUseAndCiweiCancelsOnlySecondUse()
    {
        var s = Scene.Start([3247]); var hp = s.V(2).Hp;
        s.UseCard(CardKind.Slash, 2); s.Reach(() => s.P?.SkillPrompt?.SkillId == "wushuang:skill-3247"); s.ActivateTrigger();
        s.Reach(() => s.Stage == "begin"); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "lingren-target" && c.Targets.Single() == 2);
        s.Reach(() => s.Stage == "lingren-guess"); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "lingren-guess-7"); s.Play();
        s.Need(s.E<WushuangLingrenGuessedEvent>().Single().Correct == 3 && s.V(2).Hp == hp - 2 &&
            s.V().Skills!.Any(x => x.ContentId == "boundary:jianxiong") && s.V().Skills!.Any(x => x.ContentId == "classic:xingshang"),
            "Official Lingren adds damage only to its guessed physical use and grants the correct temporary classic skills, without refunding use counts."); s.Cold();
        s = Scene.Start([3284], activeEnemy: true); s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "wushuang:skill-3284"); s.Cold(); s.ActivateTrigger();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        s.Reach(() => s.E<WushuangCiweiNullifiedEvent>().Length == 1); s.Cold();
        var proof = s.E<WushuangCiweiNullifiedEvent>().Single();
        s.Need(proof.Actor == 2 && s.E<CurrentTurnCardUseKindsRecordedEvent>().Single(e => e.CardActionId == proof.ActionId).ActualTurnActorUseOrdinal == 2 &&
            s.G.CardMovements.Count(m => m.Sequence == proof.PaidMovement && m.CardId == proof.PaidCard && m.To == CardLocation.DiscardPile) == 1,
            "Ciwei commits exactly one physical discard against the actual other turn owner's second basic or ordinary trick use.");
    }
    public static void JinzhiPaysIncreasingSameColorCountsAndDrawsBeforeNativeBasicEffects()
    {
        var s = Scene.Start([3252], activeEnemy: true); var hp = s.V().Hp;
        s.Jinzhi(CardKind.Peach); s.Play(); s.Cold();
        s.Jinzhi(CardKind.Peach); s.Play(); s.Cold();
        var paid = s.E<WushuangJinzhiPaidEvent>();
        s.Need(paid.Length == 2 && paid.Select(e => e.Ordinal).SequenceEqual([1, 2]) &&
            paid.Select(e => e.Action.PhysicalCards.Count).SequenceEqual([1, 2]) && s.V().Hp == hp + 2 &&
            s.E<WushuangOriginalSkillDrawEvent>().Count(e => e.Requested == 1) == 2 &&
            paid.SelectMany(e => e.Action.PhysicalCards).All(c => s.G.CardMovements.Any(m => m.CardId == c.CardId && m.Reason.Value == "skill-program.wushuang.jinzhi.discard")),
            "The first and second round activations discard exactly one then two same-color entities, each draw once, then resolve their real Peach recovery.");
        s.Need(paid.All(e => e.Action.EffectiveSuit == Suit.None && e.Action.EffectiveRank is null &&
            e.Action.EffectiveIsRed is null && e.Action.PhysicalCards.All(c => c.EffectiveIsRed is not null)),
            "Jinzhi discards colored costs while its separate virtual basic card has no suit, rank or color.");
        s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "extended-view-as" && c.Cards.Count == 3);
        s.Reach(() => s.E<WushuangJinzhiPaidEvent>().Length == 3); s.Cold(); s.Play();
        s.Need(s.E<WushuangJinzhiPaidEvent>().Last() is { Ordinal: 3, Action.Type: CardActionType.Response },
            "The same true-round counter pays three same-color cards for a real out-of-turn Dodge, retaining native response completion.");
        s.Need(s.E<WushuangJinzhiPaidEvent>().Last().Action is { EffectiveSuit: Suit.None, EffectiveRank: null, EffectiveIsRed: null },
            "A Jinzhi response also has a colorless, rankless virtual appearance while retaining exact paid materials.");

        s = Scene.Start([3252], activeEnemy: true, responseBan: true); s.End();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Cold();
        s.Need(s.E<HandCardColorRestrictionGrantedEvent>().Count(e => e.Restriction.AffectedSeat == 0) == 2,
            "The fixture has actually prohibited both colors of hand-card use and response for this incoming Slash.");
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "extended-view-as" && c.Cards.Count == 1);
        s.Reach(() => s.E<WushuangJinzhiPaidEvent>().Length == 1); s.Cold(); s.Play();
        s.Need(s.E<WushuangJinzhiPaidEvent>().Single().Action.Type == CardActionType.Response,
            "A use-only hand restriction permits Jinzhi's legal discard cost and its separate colorless virtual response.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public string? Stage => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.WushuangOriginalSkill?.Stage;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.Status, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase,
            Prompt = P, Stage, Hand = V().Hand, Paid = E<WushuangJinzhiPaidEvent>().Select(e => new { e.Ordinal, e.RoundNumber, e.Action.Type }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())) });
        public void Accept(GameCommand c) { try { var r = G.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Rejected command"); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var selected = p.Choices.FirstOrDefault(match); Need(selected is not null, "The expected published choice is absent."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, selected!.Id, G.Revision)); }
        public void ActivateTrigger() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Activate(int id, string activation) => Accept(new UseProgramSkillCommand(0, "wushuang:skill-" + id, activation, [], [], G.Revision, P!.PromptId));
        public void Targeted(int id, string activation, int target) => Accept(new UseProgramSkillCommand(0, "wushuang:skill-" + id, activation, [], [target], G.Revision, P!.PromptId));
        public void Qingnang() => Accept(new UseProgramSkillCommand(0, "wushuang:skill-3049", "discard-and-heal", [V().Hand.First().Id], [0], G.Revision, P!.PromptId));
        public void Use(string id, int target) => Accept(new UseProgramSkillCommand(0, Driver, id, [], [target], G.Revision, P!.PromptId));
        public void UseCard(CardKind kind, int target)
        { var action = G.GetHumanLegalActions().First(a => a.CardId is { } card && V().Hand.Single(c => c.Id == card).Kind == kind && a.TargetSeats.SequenceEqual([target])); Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource }); }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Jinzhi(CardKind kind)
        {
            var action = G.GetHumanLegalActions().First(a => a.ProgramSkillId == "wushuang:skill-3252" && a.PlayedCardKind == kind);
            Accept(new UseProgramSkillCommand(0, action.ProgramSkillId!, action.ProgramActivationId!, action.SelectableCardIds,
                kind == CardKind.Peach ? [0] : [], G.Revision, P!.PromptId));
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 240; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    var skip = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                    if (skip is not null) { Answer(c => c.Id == skip.Id); continue; }
                    throw new InvalidOperationException("Unexpected actual human prompt." + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded official skill flow did not arrive." + Diagnostic());
        }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0));
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "Cold replay retains the exact owning skill, physical payment and current private prompt.");
        }
        public static Scene Start(int[] skills, bool activeEnemy = false, bool recoveryLoss = false, bool responseBan = false,
            bool tauntSlash = false, bool equipmentOnly = false, bool extraPlay = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(skills, activeEnemy, recoveryLoss, responseBan, extraPlay));
            var hand = new[] { new ShanheStartingCard(CardKind.Slash, Suit.Heart, 7), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Duel, Suit.Club, 6), new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Heart, 7) };
            var enemy = activeEnemy ? new[] { new ShanheStartingCard(CardKind.DrawTwo, Suit.Heart, 3), new(CardKind.DrawTwo, Suit.Club, 4), new(CardKind.Slash, Suit.Diamond, 7) }
                : new[] { new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3), new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Duel, Suit.Club, 6), new(CardKind.BaguaFormation, Suit.Heart, 2) };
            var setup = new ShanheBattleSetup(7, 1, "wushuang-a", Hero, 8, 0, 0, 0, 0, [], equipmentOnly ? [] : hand,
                equipmentOnly ? [new ShanheStartingCard(CardKind.QinggangSword, Suit.Club, 2)] : [],
                [new("fixture:wushuang-a-enemy", Hp: 12) { StartingHand = enemy }, new("fixture:wushuang-a-other", Hp: 12) { StartingHand = tauntSlash ? [new ShanheStartingCard(CardKind.Slash, Suit.Spade, 7)] : [] }],
                [new("fixture:wushuang-a-ally", Hp: 12) { StartingHand = [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
    private sealed class Fixture(int[] skills, bool activeEnemy, bool recoveryLoss, bool responseBan, bool extraPlay) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wushuang-a", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            SkillProgramCatalog Bundle(string name)
            {
                using var rules = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{name}.rules.json")!);
                using var presentation = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{name}.presentation.json")!);
                return SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            }
            var official = Bundle("wushuang-skills-a"); var audit = Bundle("wushuang-audit-a");
            foreach (var number in skills)
            { var id = "wushuang:skill-" + number; var cat = official.Programs.ContainsKey(id) ? official : audit; b.AddSkill(new(id, cat.Presentations[id].Name, cat.Presentations[id].Description) { Program = cat.Programs[id], ProgramPresentation = cat.Presentations[id], Tags = number is 3246 or 3265 or 3106 ? SkillTag.Locked : SkillTag.None }); }
            if (skills.Contains(3140))
            {
                // An ungranted consumer opts into the shared native invoice so
                // this regression verifies a real logical equipment discard.
                const string invoiceConsumer = "wushuang:skill-3252";
                b.AddSkill(new(invoiceConsumer, official.Presentations[invoiceConsumer].Name, official.Presentations[invoiceConsumer].Description)
                { Program = official.Programs[invoiceConsumer], ProgramPresentation = official.Presentations[invoiceConsumer] });
            }
            foreach (var (bundle, id) in new[] { ("boundary-cao-cao", "boundary:jianxiong"), ("classic-cao-pi", "classic:xingshang"), ("classic-lifecycle-skills", "classic:dangxian") })
            { var cat = Bundle(bundle); b.AddSkill(new(id, cat.Presentations[id].Name, cat.Presentations[id].Description) { Program = cat.Programs[id], ProgramPresentation = cat.Presentations[id] }); }
            var json = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"fixture-range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
             {"id":"fixture:wushuang-a-range","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}]},
             {"id":"fixture:wushuang-a-shield","revision":1,"cardPolicies":[{"id":"shield","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash"]}]},
             {"id":"fixture:wushuang-a-recovery-loss","revision":1,"triggers":[{"id":"lose-after-recovery","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
             {"id":"fixture:wushuang-a-response-ban","revision":1,"triggers":[{"id":"ban-target-hand","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventTarget"},{"op":"grantTurnHandCardProhibition","target":"selectedTarget"}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var own = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, "fixture:wushuang-a-range", "fixture:wushuang-a-shield", "fixture:wushuang-a-recovery-loss", "fixture:wushuang-a-response-ban" }.ToDictionary(id => id, id => new { name = id, description = "固定原生单骑无双规则夹具" }) }));
            foreach (var (id, p) in own.Programs) b.AddSkill(new(id, id, id) { Program = p, ProgramPresentation = own.Presentations[id] });
            b.AddGeneral(new(Hero, Hero, "", Driver, "shu", 12, skills.Select(id => "wushuang:skill-" + id)
                .Concat(extraPlay ? ["classic:dangxian"] : Array.Empty<string>())
                .Concat(recoveryLoss ? ["fixture:wushuang-a-recovery-loss"] : Array.Empty<string>()).ToArray()));
            foreach (var id in new[] { "fixture:wushuang-a-enemy", "fixture:wushuang-a-other", "fixture:wushuang-a-ally" })
                b.AddGeneral(new(id, id, "", activeEnemy && id.EndsWith("enemy", StringComparison.Ordinal) ? "fixture:wushuang-a-range" : Quiet, "wei", 12,
                    id.EndsWith("enemy", StringComparison.Ordinal) ? responseBan ? ["fixture:wushuang-a-response-ban"] : [] : ["fixture:wushuang-a-shield"]));
            b.AddDeck(new("fixture:wushuang-a-deck", "固定红桃闪补牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "单骑无双官方技能规则小局", 4, 4, new Dictionary<string, int>(), "fixture:wushuang-a-deck",
                GeneralPoolIds: [Hero, "fixture:wushuang-a-enemy", "fixture:wushuang-a-other", "fixture:wushuang-a-ally"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}

