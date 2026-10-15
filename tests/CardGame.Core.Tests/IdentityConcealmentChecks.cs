using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class IdentityConcealmentChecks
{
    private const string Owner = "fixture:identity-concealment-owner", Driver = "fixture:identity-concealment-driver", Quiet = "fixture:identity-concealment-quiet", Watch = "fixture:identity-concealment-watch", Mode = "identity:classic-concealment";
    public static void NativeGroupRecoveryAndSilverLionRemovalRejectUndefinedHp()
    {
        var (g, registry) = Start(peerInitialHp: 1, recoveryDeck: true); Play(g);
        Use(g, "lose-target", 2); SkipGaoling(g, 2); Play(g);
        Need(V(g, 2).Hp == 1 && !V(g, 2).HpUndefined, "Real appearance provides a visible wounded recovery control.");
        Use(g, "equip-target", 1); Play(g);
        Need(V(g, 1).Equipment.Any(c => c.Kind == CardKind.SilverLion) && V(g, 1).IdentityConcealed, "Native equipment use installs a physical Lion without appearance."); g = Cold(g, registry);
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.PeachGarden));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)); Play(g);
        var actors = (IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        Need(actors.Single(p => p.Seat == 1).Hp == 1 && V(g, 1).HpUndefined && V(g, 1).IdentityConcealed && !E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 1), "Actual Peach Garden cannot recover the hidden raw wound or reveal its target.");
        Need(V(g, 2).Hp == 2 && E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 2) == 1, "The same native group use really recovers the visible wounded peer once.");
        Use(g, "remove-equipment", 1); Reach(g, () => P(g)?.Choices.Any(c => c.Cards.Count == 1) == true); Answer(g, c => c.Cards.Count == 1); Play(g); g = Cold(g, registry);
        actors = (IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        Need(V(g, 1).Equipment.Count == 0 && actors.Single(p => p.Seat == 1).Hp == 1 && V(g, 1).HpUndefined && V(g, 1).IdentityConcealed && !E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 1) && !E<SilverLionRemovedRecoveryEvent>(g).Any(), "Real Lion removal moves the entity without recovery or appearance, including cold replay.");
    }
    public static void GameStartingHpLossAppearanceDoesNotOfferOffTurnGaoling()
    {
        var (g, registry) = Start(gameStartingLoss: true);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Driver && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")); Answer(g, c => c.Targets.SequenceEqual([1]));
        Need(P(g)?.SkillPrompt?.SkillId != "ol:gaoling", "GameStarting has no actual other character's turn and must not even publish an off-turn Gaoling offer."); Play(g);
        var appeared = E<IdentityGeneralAppearedEvent>(g).Single(e => e.Receipt.OwnerSeat == 1);
        Need(appeared.Receipt is { Entry: IdentityAppearanceEntry.ProgramHpLoss, ActualTurnNumber: 0, RestoredHp: 3, RequestedDeduction: 2 } && V(g, 1).Hp == 3 && !E<GaolingRecoverySelectedEvent>(g).Any() && !E<ProgramSkillHpLostEvent>(g).Any(e => e.TargetSeat == 1), "Real GameStarting HP loss reveals and prevents its deduction once without inventing a foreign actual turn or recovery."); _ = Cold(g, registry);
    }
    public static void UndefinedHpRejectsNativeRecoveryAndMaxHpNumberCalculation()
    {
        var (g, registry) = Start(peerInitialHp: 1); Play(g); var actors = (IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        Need(actors.Single(p => p.Seat == 1) is { Hp: 1, MaxHp: 3, HasHpValue: false }, "A real concealed template can retain a wounded raw initial HP without exposing a comparable rule value.");
        Use(g, "recover-self", 1); Play(g); Need(actors.Single(p => p.Seat == 1).Hp == 1 && V(g, 1).HpUndefined && !E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 1) && !E<IdentityGeneralAppearedEvent>(g).Any(e => e.Receipt.OwnerSeat == 1), "A native recovery aimed at the concealed undefined-HP target neither uses its stored wound nor reveals it.");
        Use(g, "clear-hand", 1); Play(g); Need(V(g, 1).Hand.Count == 0 && V(g, 1).HpUndefined, "Real card movement clears the concealed target's hand while leaving HP undefined.");
        var sequence = g.CardMovements.Count; Use(g, "draw-max-target", 1); Play(g);
        Need(V(g, 1).Hand.Count == 0 && g.CardMovements.Count == sequence && V(g, 1).HpUndefined, "TargetMaxHpMinusHandCount has no calculable operand and cannot turn the hidden raw maximum into three drawn cards."); _ = Cold(g, registry);
    }
    public static void SetupEveryViewerHasUndefinedHpAndForeignTemplatePrivacy()
    {
        var (g, registry) = Start(); Play(g);
        foreach (var viewer in Enumerable.Range(0, 4))
        foreach (var seat in Enumerable.Range(1, 3))
        {
            var p = g.CreateSnapshot(viewer).Players[seat]; Need(p.IdentityConcealed && p.HpUndefined && p.Hp == 0 && p.MaxHp == 0 && !p.IsGeneralPublic, "Concealed identity has no HP value even in its owner's normal view.");
            Need(viewer == seat ? p.GeneralId.StartsWith("fixture:identity-concealment-peer-", StringComparison.Ordinal) && p.Skills!.Any(s => s.ContentId == "ol:gaoling") : p.GeneralId == "concealment:ol" && (p.Skills?.Count ?? 0) == 0, "Only the real owner sees its primary template; every foreign view has the OL concealed facet and no skills.");
        }
        var actors = (IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        Need(actors.Where(p => p.Seat != 0).All(p => p.General.Gender == GeneralGender.Female && p.Gender == GeneralGender.Male && !p.HasHpValue && p.Hp == 3), "Trusted raw initial HP remains stored, while rule gender is male and no HP value exists before appearance.");
        Need(!g.GetHumanLegalActions().Any(a => a.ProgramActivationId == "hp-greater") && g.GetHumanLegalActions().Any(a => a.ProgramActivationId == "male-target" && a.SelectableTargetSeats.Contains(1)), "Hidden raw HP cannot qualify an HP comparison, while the existing male-only target filter uses concealed male rule gender rather than the real female template.");
        var before = State(g); Need(!g.Submit(new UseProgramSkillCommand(0, Driver, "hp-greater", [], [1], g.Revision, P(g)!.PromptId)).Accepted && before == State(g), "A command naming an undefined-HP target cannot bypass legal target discovery or mutate state.");
        Use(g, "male-target", 1); Play(g);
        Use(g, "recover-self", 0); Play(g); Need(V(g, 0).Hp == 3 && g.GetCombatDistance(0, 2) == 2, "A true HP-ordered distance policy does not compare the concealed target's stored three HP against the now-recovered visible actor.");
        _ = Cold(g, registry);
    }
    public static void ActualOwnTurnAppearanceRestoresLordInitialBonusWithoutOffTurnGaoling()
    {
        var (g, registry) = Start(hiddenOwner: true); Play(g); var p = V(g, 0);
        var appeared = E<IdentityGeneralAppearedEvent>(g).Single(e => e.Receipt.OwnerSeat == 0);
        Need(appeared.Receipt.Entry == IdentityAppearanceEntry.ActualTurnStart && appeared.Receipt.RestoredHp == 4 && appeared.Receipt.MaximumHp == 4 && appeared.Receipt.RequestedDeduction == 0 &&
            p.Hp == 4 && p.MaxHp == 4 && !p.IdentityConcealed && !p.HpUndefined && p.GeneralId == Owner && p.IsGeneralPublic, "Native actual own-turn start restores the selected three-HP template plus its real Lord bonus.");
        Need(!E<GaolingRecoverySelectedEvent>(g).Any() && !E<IdentityHpDeductionPreventedEvent>(g).Any(e => e.OwnerSeat == 0) && E<IdentityAppearanceCompletedEvent>(g).Count(e => e.OwnerSeat == 0) == 1, "Own-turn appearance does not trigger the off-turn Gaoling recovery or fabricate HP deduction.");
        _ = Cold(g, registry);
    }
    public static void NativeSlashAppearanceGaolingRecoveryChildColdAndLaterDamage()
    {
        var (g, registry) = Start(watch: true); Play(g); var hp = V(g, 0).Hp; NativeSlash(g, 1);
        BeginGaoling(g, 1, 0); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        var appearance = g.ResolutionStack.OfType<IdentityAppearanceFrame>().Single(); var parent = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == appearance.Receipt.ParentFrameId);
        Need(appearance.Receipt.Entry == IdentityAppearanceEntry.DamageDeduction && appearance.Receipt.RequestedDeduction == 1 && appearance.Receipt.DamageSourceSeat == 0 && appearance.Receipt.CardKind == CardKind.Slash && V(g, 1).Hp == 3 && V(g, 0).Hp == hp + 1 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any() && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.GaolingRecovery is not null), "Actual Slash suspends its exact use under appearance, restores HP and pays a real off-turn Gaoling recovery before deduction.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
        Need(E<IdentityGeneralAppearedEvent>(g).Count(e => e.Receipt.OwnerSeat == 1) == 1 && E<IdentityHpDeductionPreventedEvent>(g).Count(e => e.OwnerSeat == 1 && e.RequestedDeduction == 1) == 1 &&
            E<GaolingRecoverySelectedEvent>(g).Count(e => e.AppearanceFrameId == appearance.Id) == 1 && E<IdentityAppearanceCompletedEvent>(g).Count(e => e.AppearanceFrameId == appearance.Id) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == parent.Id) == 1 && !E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 1), "Cold recovery resumes one appearance, one recovery and one native use, with no invented first damage application.");
        NativeSlash(g, 1); Play(g); Need(V(g, 1).Hp == 2 && E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1 && E<IdentityGeneralAppearedEvent>(g).Count(e => e.Receipt.OwnerSeat == 1) == 1, "Later native Slash applies normal damage to the now-visible general without repeating appearance."); _ = Cold(g, registry);
    }
    public static void NativeProgramHpLossAndDamageReplacementRespectUndefinedHp()
    {
        var (g, registry) = Start(); Play(g); var lord = g.CreateSnapshot(0).Players.First(p => p.Seat != 0 && p.IdentityConcealed).Seat;
        Need(lord != 0 && V(g, lord).IdentityConcealed, "The current visible Lord's first actual Play can reach a concealed non-Lord before that character's actual turn begins.");
        Use(g, "lose-target", lord); SkipGaoling(g, lord); Play(g);
        var first = E<IdentityGeneralAppearedEvent>(g).Single(e => e.Receipt.OwnerSeat == lord);
        Need(first.Receipt is { Entry: IdentityAppearanceEntry.ProgramHpLoss, RestoredHp: 3, MaximumHp: 3, RequestedDeduction: 2 } && V(g, lord).Hp == 3 && !E<ProgramSkillHpLostEvent>(g).Any(e => e.TargetSeat == lord), "Source-less real program HP loss reveals the concealed non-Lord at true initial three HP and prevents this deduction once; the separate actual-own-turn check covers the Lord bonus.");
        g = Cold(g, registry); Use(g, "lose-target", lord); Play(g); Need(V(g, lord).Hp == 1 && E<ProgramSkillHpLostEvent>(g).Count(e => e.TargetSeat == lord && e.Amount == 2) == 1, "A later identical accepted HP-loss instruction pays normally once.");
        var (replacement, replacementRegistry) = Start(replaceDamage: true); Play(replacement); NativeSlash(replacement, 1); SkipGaoling(replacement, 1); Play(replacement);
        Need(E<IdentityGeneralAppearedEvent>(replacement).Single(e => e.Receipt.OwnerSeat == 1).Receipt.Entry == IdentityAppearanceEntry.DamageDeduction && V(replacement, 1).Hp == 3 && !E<DamageReplacedWithHpLossEvent>(replacement).Any(e => e.TargetSeat == 1), "DamageBecomesHpLoss native early-return route still honors undefined HP and the exact appearance invoice.");
        replacement = Cold(replacement, replacementRegistry); NativeSlash(replacement, 1); Play(replacement); Need(V(replacement, 1).Hp == 2 && E<DamageReplacedWithHpLossEvent>(replacement).Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1 && !E<DamageAppliedEvent>(replacement).Any(e => e.TargetSeat == 1), "After appearance the same true replacement policy applies HP loss instead of normal damage."); _ = Cold(replacement, replacementRegistry);
    }
    public static void PaidGaolingRecoveryChildCanKillAppearingOwnerAndReturnOnce()
    {
        var (g, registry) = Start(watch: true, killGaoling: true); Play(g); NativeSlash(g, 1); BeginGaoling(g, 1, 0); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        var appearance = g.ResolutionStack.OfType<IdentityAppearanceFrame>().Single(); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, () => P(g)?.PlayerSeat == 0 && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")); Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, () => !g.ResolutionStack.Any(f => f.Id == appearance.Id));
        Need(g.State.Winner == Winner.None && !V(g, 1).IsAlive && E<GaolingRecoverySelectedEvent>(g).Count(e => e.AppearanceFrameId == appearance.Id) == 1 && E<IdentityAppearanceCompletedEvent>(g).Count(e => e.AppearanceFrameId == appearance.Id) == 1 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == appearance.Receipt.ParentFrameId) == 1, "A real recovery observer kills the appearing skill owner, while paid recovery and the original native use each return exactly once."); _ = Cold(g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Need(bool b, string reason) { if (!b) throw new InvalidOperationException(reason); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected identity concealment command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Use(GameEngine g, string activation, int target) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], [target], g.Revision, P(g)!.PromptId));
    private static void NativeSlash(GameEngine g, int target) { var a = g.GetHumanLegalActions().First(a => a.PlayedCardKind is null or CardKind.Slash && a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([target])); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
    private static bool OptionalGaoling(GameEngine g, int owner) => P(g)?.PlayerSeat == owner && P(g)?.SkillPrompt?.SkillId == "ol:gaoling" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void BeginGaoling(GameEngine g, int owner, int recipient) { Reach(g, () => OptionalGaoling(g, owner)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, () => P(g)?.PlayerSeat == owner && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")); Answer(g, c => c.Targets.SequenceEqual([recipient])); }
    private static void SkipGaoling(GameEngine g, int owner) { Reach(g, () => OptionalGaoling(g, owner)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 220; i++) { if (done()) return; var p = P(g); Need(g.State.Winner == Winner.None, "Small concealment fixture ended before expected boundary.");
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        throw new InvalidOperationException("Small native concealment fixture exceeded its command bound.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var r = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(r), "Cold command replay preserves every viewer, exact appearance/HP deduction invoice and paid recovery cursor."); return r; }
    private static (GameEngine, ContentRegistry) Start(bool hiddenOwner = false, bool watch = false, bool replaceDamage = false, bool killGaoling = false, Role humanRole = Role.Lord, int peerInitialHp = 3, bool gameStartingLoss = false, bool recoveryDeck = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(hiddenOwner, watch, replaceDamage, killGaoling, peerInitialHp, gameStartingLoss, recoveryDeck)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = humanRole, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry); Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); return (g, registry);
    }
    private sealed class Fixture(bool hiddenOwner, bool watch, bool replaceDamage, bool killGaoling, int peerInitialHp, bool gameStartingLoss, bool recoveryDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("identity-concealment-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog, Func<string, bool>? filter = null) { foreach (var (id, program) in catalog.Programs.Where(p => filter?.Invoke(p.Key) ?? true)) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) }); }
            string Embedded(string suffix) { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-xuan-gong-zhu." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); } Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")), id => id == "ol:gaoling");
            var defs = new List<string> { $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"clear-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true}]},{"id":"draw-max-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","numberExpression":"targetMaxHpMinusHandCount"}]},{"id":"recover-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]},{"id":"lose-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},{"id":"hp-greater","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHandHpGreaterThanOwner","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},{"id":"male-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingMale","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]}],"triggers":[{"id":"quota","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":8}]}]{{(replaceDamage ? ",\"cardPolicies\":[{\"id\":\"real-hp-replacement\",\"kind\":\"damageBecomesHpLoss\"},{\"id\":\"hp-ordered-distance\",\"kind\":\"distanceOneToNotHigherHp\"}]" : ",\"cardPolicies\":[{\"id\":\"hp-ordered-distance\",\"kind\":\"distanceOneToNotHigherHp\"}]")}}} """, $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""" };
            if (recoveryDeck)
            {
                var driver = JsonNode.Parse(defs[0])!;
                driver["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]}"""));
                driver["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"remove-equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","zones":["equipment"],"amount":1,"resultBind":"lion"},{"op":"moveBoundCards","target":"owner","sourceBind":"lion","destination":"discardPile","awaitMovementTriggers":true}]}"""));
                defs[0] = driver.ToJsonString();
                b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().GetCard("classic:silver-lion"));
            }
            if (watch) defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-gaoling-recovery-child","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}{{(killGaoling ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}" : "")}}]}]}""");
            if (gameStartingLoss) { var driver = JsonNode.Parse(defs[0])!; driver["triggers"]!.AsArray().Add(JsonNode.Parse("""{"id":"actual-game-start-loss","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":2}]}""")); defs[0] = driver.ToJsonString(); }
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实驱动", description = "HP筛选及原生扣减" }, [Quiet] = new { name = "静默", description = "跳过其他出牌" } }; if (watch) labels[Watch] = new { name = "高陵恢复子窗口", description = "真实恢复及续接", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }; Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var ownerSkills = new List<string>(); if (hiddenOwner) ownerSkills.Add("ol:gaoling"); if (watch) ownerSkills.Add(Watch); b.AddGeneral(new(Owner, "真实隐匿驱动拥有者", "supporter", Driver, "jin", hiddenOwner ? 3 : 12, ownerSkills, GeneralGender.Female) { InitialHp = hiddenOwner ? 3 : 1 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:identity-concealment-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "真实隐匿女角色", "supporter", Quiet, "jin", 3, ["ol:gaoling"], GeneralGender.Female) { InitialHp = peerInitialHp }); b.AddDeck(new("fixture:identity-concealment-deck", "固定实际杀", 6, 0, []) { PhysicalCards = recoveryDeck ? Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:peach_garden", Suit.Heart, 7)).Concat(Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Club, 1))).ToArray() : Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Diamond, 7)).ToArray() }); b.AddMode(new(Mode, "真实身份隐匿", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:identity-concealment-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
