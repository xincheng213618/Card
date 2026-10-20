using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ArmorAiChecks
{
    private const string Driver = "fixture:armor-ai-driver", Owner = "fixture:armor-ai-owner";
    private const string Ally = "fixture:armor-ai-ally", FirstEnemy = "fixture:armor-ai-first-enemy";
    private const string SecondEnemy = "fixture:armor-ai-second-enemy", Deck = "fixture:armor-ai-deck";
    private const string Jueqing = "classic:jueqing", Xiaoming = "shanhe:xiaoming", Gangzhi = "ol:gangzhi";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-armor-ai-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new("standard:cao-cao")], [], new string('b', 64))));

    public static void PublishedProgramAiChoosesRealKillInsteadOfArmoredLowHpAndQueriesStayPure()
    {
        var s = Scene.Start("targets");
        s.Use("armor-other", [2]);
        Need(s.V(2) is { Hp: 1, Armor: 3 } && s.V(3) is { Hp: 2, Armor: 0 },
            "The two genuine enemies must present a protected one-HP decoy and an unprotected two-HP lethal target.");
        var unchanged = s.Boundary();
        var revision = s.G.Revision;
        var commandCount = s.G.AcceptedCommands.Count;
        var eventCount = s.G.Events.Count;
        var view = s.G.CreateSnapshot(0);
        var action = s.Action("hit-two");
        var end = s.G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.EndPlay);
        var brain = new SimpleAiBrain(0, 83, AiDecisionPolicy.CurrentVersion);
        var (chosen, thought) = brain.ChoosePlay(view, [action, end], 1);
        var targets = brain.ChooseActiveSkillTargets(view, chosen, []);
        var armoredScore = brain.ScoreProgramTarget(view, 2, action.ProgramAiHint!);
        var bareScore = brain.ScoreProgramTarget(view, 3, action.ProgramAiHint!);
        var legacyBareScore = brain.ScoreProgramTarget(view, 3, new(0, 0, 0, 0, 0, 2, false, false));
        Need(chosen == action && targets.SequenceEqual([3]) && armoredScore > 0 && bareScore > armoredScore,
            $"Published Damage2 must retain positive damage/Armor pressure while rewarding the real kill; protected={armoredScore}, bare={bareScore}, targets={string.Join(',', targets)}.");
        Need(bareScore == legacyBareScore && thought.Candidates.All(c => double.IsFinite(c.Score)) &&
            view.Players.Where(p => p.Seat != 0).All(p => p.Hand.Count == 0),
            "Zero-Armor program damage retains the old two-HP harm value, and scoring reads no foreign physical hand faces.");
        Need(s.G.Revision == revision && s.G.AcceptedCommands.Count == commandCount && s.G.Events.Count == eventCount &&
            s.Boundary() == unchanged,
            "Legal-action publication, public target scoring and standalone AI choices cannot alter revision, commands, events, zones or checkpoint state.");
        s.Use(chosen.ProgramActivationId!, targets.ToArray());
        Need(s.V(2) is { IsAlive: true, Hp: 1, Armor: 3 } && !s.V(3).IsAlive &&
            s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2, ArmorPayment: null }] &&
            s.G.AcceptedCommands.OfType<UseProgramSkillCommand>().Last().TargetSeats.SequenceEqual([3]),
            "The actual AI-selected native program command must kill only its unarmored target and leave the armored decoy untouched.");
        s.Cold();
        PolicyProjectionDoesNotReadUnpublishedRecipientSkills();
    }

    private static void PolicyProjectionDoesNotReadUnpublishedRecipientSkills()
    {
        var scene = Scene.Start("visibility");
        scene.Use("armor-other", [2]);
        CheckProjection(); scene.Cold(); CheckProjection();
        scene.Use("hit-two", [2]);
        Need(scene.V(2) is { Hp: 4, Armor: 3 } && scene.Events<DamageAppliedEvent>().Length == 0 &&
            scene.Events<DamageReplacedWithHpLossEvent>() is [{ TargetSeat: 2, Amount: 2 }],
            "Limiting an AI projection must not disable the recipient's real acquired replacement policy or spend Armor in the actual command.");
        scene.Cold();

        void CheckProjection()
        {
            var before = scene.Boundary();
            var actors = ((IEnumerable<CharacterState>)typeof(GameEngine)
                .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.G)!).ToArray();
            Need(new[] { Jueqing, Gangzhi }.All(id => actors[2].SkillGrants.Grants.Any(grant =>
                    grant.IsEnabled && grant.SourceId == "campaign:shanhe" && grant.SkillId == id)),
                "Both policies must be genuine enabled encounter grants, rather than imaginary skill flags.");
            var complete = scene.G.CreateSnapshot(0);
            // Exercise the projection boundary with reduced visibility; this is
            // not an assertion that the visible team fixture is an identity-concealment mode.
            var limited = complete with { Players = Array.AsReadOnly(complete.Players.Select(player =>
                player.Seat == 2 ? player with { Skills = [], SecondarySkills = [] } : player).ToArray()) };
            var method = typeof(GameEngine).GetMethod("CreateAiSkillContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var visible = (PlayerSkillContext)method.Invoke(scene.G, [actors[2], 0, complete])!;
            var unavailable = (PlayerSkillContext)method.Invoke(scene.G, [actors[2], 0, limited])!;
            var own = (PlayerSkillContext)method.Invoke(scene.G, [actors[2], 2, limited])!;
            Need(visible.DamageBecomesHpLoss && visible.OtherParticipantDamageBecomesHpLoss &&
                !unavailable.DamageBecomesHpLoss && !unavailable.OtherParticipantDamageBecomesHpLoss &&
                own.DamageBecomesHpLoss && own.OtherParticipantDamageBecomesHpLoss,
                "Complete foreign projection and actual owner know both policies; a foreign projection omitting both skill columns cannot reveal either policy.");
            var owner = scene.V();
            var effects = scene.Registry.GetSkill(Driver).Program!.Activations.Single(a => a.Id == "hit-two").Effects;
            var context = new PlayerSkillContext(0, owner.Hp, owner.MaxHp, owner.HandCount, TurnPhase.Play, IsOwnTurn: true);
            var known = ProgramCompositionAi.Estimate(effects, context, publicContext: new(1, SelectedTarget: visible)).Hint;
            var unknown = ProgramCompositionAi.Estimate(effects, context, publicContext: new(1, SelectedTarget: unavailable)).Hint;
            Need(known is { TargetDamage: 0, TargetDirectHpLoss: 2 } &&
                known.PredictTarget(6, 8, 3) is { HpAfter: 4, ArmorAfter: 3 } &&
                unknown is { TargetDamage: 2, TargetDirectHpLoss: 0 } &&
                unknown.PredictTarget(6, 8, 3) is { HpAfter: 6, ArmorAfter: 1 } && scene.Boundary() == before,
                "Real registered program estimates must consume only the supplied public policy context, leave the actual game unchanged and remain stable after cold restoration.");
        }
    }

    public static void ProgramHealthEstimatesRespectRemainingArmorAndInstructionOrder()
    {
        var s = Scene.Start("sequences");
        var unchanged = s.Boundary();
        var grantFirst = s.Action("grant-first").ProgramAiHint!;
        var damageFirst = s.Action("damage-first").ProgramAiHint!;
        Need(grantFirst is { OwnerDamage: 4, OwnerArmorLoss: 3, OwnerHpLoss: 1, OwnerWouldEnterDying: false } &&
            !Estimate(s, "grant-first").IsSelfLethal,
            "GrantArmor3 followed by Damage2 twice must spend only the remaining Armor, predict one lost HP and preserve a two-HP owner.");
        Need(damageFirst.OwnerWouldEnterDying == true && damageFirst.OwnerHpLoss >= 2 &&
            Estimate(s, "damage-first").IsSelfLethal,
            "A later Armor grant cannot retroactively prevent the first two-point hit from putting its two-HP owner into dying.");
        Need(s.Boundary() == unchanged, "Ordered program estimates and published hints must remain pure native queries.");
        s.Use("grant-first");
        Need(s.V() is { Hp: 1, Armor: 0 } && s.Events<ArmorGrantedEvent>() is [{ PlayerSeat: 0, Amount: 3 }] &&
            s.Events<DamageAppliedEvent>() is
            [{ TargetSeat: 0, Amount: 2, ArmorPayment: { ArmorBefore: 3, ArmorAbsorbed: 2, ArmorAfter: 1, HpLossAmount: 0 } },
             { TargetSeat: 0, Amount: 2, ArmorPayment: { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpLossAmount: 1 } }],
            "The actual ordered native program must agree with the estimate: the same Armor cannot absorb its original amount twice.");
        s.Cold();

        var existing = Scene.Start("sequences");
        existing.Use("armor-self");
        unchanged = existing.Boundary();
        var repeated = existing.Action("self-double").ProgramAiHint!;
        var loss = existing.Action("self-loss");
        Need(repeated is { OwnerDamage: 4, OwnerArmorLoss: 3, OwnerHpLoss: 1, OwnerWouldEnterDying: false } &&
            !Estimate(existing, "self-double").IsSelfLethal,
            "Two damage instructions must consume three existing Armor once, rather than estimating each instruction against the same original Armor.");
        Need(loss.ProgramAiHint is { OwnerDamage: 0, OwnerArmorLoss: 0, OwnerDirectHpLoss: 2,
                OwnerHpLoss: 2, OwnerWouldEnterDying: true } && Estimate(existing, "self-loss").IsSelfLethal,
            "An actual loseHp instruction remains lethal at two HP despite three Armor, without being relabeled as absorbed damage.");
        var end = existing.G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.EndPlay);
        var (chosen, _) = new SimpleAiBrain(0, 83, AiDecisionPolicy.CurrentVersion)
            .ChoosePlay(existing.G.CreateSnapshot(0), [loss, end], 1);
        Need(chosen.Kind == LegalActionKind.EndPlay && existing.Boundary() == unchanged,
            "The public AI must refuse the genuinely lethal direct-loss action, and its query must leave native state untouched.");
    }

    public static void PurePaymentsSeparateDamageHpLossAndMaximumLossWithNativeWitnesses()
    {
        var s = Scene.Start("payments");
        s.Use("armor-other", [2]);
        var unchanged = s.Boundary();
        var full = PublicDamagePrediction.Calculate(1, 3, 2, maxHp: 8);
        var partial = PublicDamagePrediction.Calculate(3, 1, 2, maxHp: 8);
        var direct = PublicDamagePrediction.Calculate(2, 3, 2, PublicDamagePaymentKind.HpLoss, 8);
        var maximum = PublicDamagePrediction.Calculate(6, 3, 2, PublicDamagePaymentKind.MaximumHpLoss, 6);
        var zeroMaximum = PublicDamagePrediction.Calculate(1, 3, 1, PublicDamagePaymentKind.MaximumHpLoss, 1);
        var bareDamage = PublicDamagePrediction.Calculate(2, 0, 2, maxHp: 8);
        var bareLoss = PublicDamagePrediction.Calculate(2, 0, 2, PublicDamagePaymentKind.HpLoss, 8);
        Need(PublicDamagePrediction.Calculate(1, 1, 4, maxHp: 8) is
                { HpBefore: 1, HpAfter: -2, HpLossAmount: 3, ActualHpLost: 3, ArmorAbsorbed: 1, ArmorAfter: 0 } &&
            new SkillProgramTriggerValue(SkillProgramTriggerValueKind.OwnerLostHp, 0)
                .Resolve(new(0, -2, false, CurrentMaxHp: 8)) == 10,
            "Armor subtracts only the absorbed damage; both public payment and frozen lost-HP operands retain the full negative-HP debt.");
        Need(full is { DamageAmount: 2, ArmorAbsorbed: 2, HpLossAmount: 0, HpAfter: 1, ArmorAfter: 1, IsLethal: false } &&
            partial is { DamageAmount: 2, ArmorAbsorbed: 1, HpLossAmount: 1, HpAfter: 2, ArmorAfter: 0 },
            "Public prediction preserves full damage and actual Armor pressure even when no HP is lost; partial payment separates the two resources.");
        Need(direct is { DamageAmount: 0, ArmorAbsorbed: 0, HpLossAmount: 2, HpAfter: 0, ArmorAfter: 3, IsLethal: true } &&
            maximum is { DamageAmount: 0, ArmorAbsorbed: 0, HpLossAmount: 2, HpAfter: 4, ArmorAfter: 3, MaxHpAfter: 4 } &&
            zeroMaximum is { ArmorAbsorbed: 0, ArmorAfter: 3, MaxHpAfter: 0, IsLethal: true },
            "Direct HP loss and maximum-HP clamping bypass Armor, including death from zero maximum HP.");
        Need(bareDamage.HpLossAmount == bareLoss.HpLossAmount && bareDamage.HpAfter == bareLoss.HpAfter &&
            bareDamage.IsLethal == bareLoss.IsLethal && s.Boundary() == unchanged,
            "Zero-Armor predictions retain ordinary lethality while every pure payment query leaves revision, history and physical zones unchanged.");
        var lossHint = s.Action("lose-two").ProgramAiHint!;
        var predictedLoss = lossHint.PredictTarget(6, 8, 3);
        Need(lossHint is { TargetDamage: 0, TargetDirectHpLoss: 2, TargetHpLoss: 2 } &&
            predictedLoss is { HpLossAmount: 2, ArmorAbsorbed: 0, ArmorAfter: 3, HpAfter: 4 },
            "The actual registry-generated target hint must preserve loseHp semantics in its frozen public instruction sequence.");
        s.Use("lose-two", [2]);
        Need(s.V(2) is { Hp: 4, Armor: 3 } && s.Events<DamageAppliedEvent>().Length == 0 &&
            s.Events<ProgramSkillHpLostEvent>() is [{ SkillId: Driver, TargetSeat: 2, Amount: 2 }],
            "Executing that genuine loseHp instruction must match its public prediction without consuming Armor or creating damage.");
        s.Cold();

        var j = Scene.Start("jueqing");
        j.Use("armor-other", [2]);
        var jueqingHint = j.Action("hit-two").ProgramAiHint!;
        var predictedJueqing = jueqingHint.PredictTarget(6, 8, 3);
        Need(predictedJueqing is { DamageAmount: 0, HpLossAmount: 2, ArmorAbsorbed: 0, ArmorAfter: 3, HpAfter: 4 },
            "The real source's native Jueqing policy must convert the program damage prediction to HP loss before Armor payment.");
        j.Use("hit-two", [2]);
        Need(j.V(2) is { Hp: 4, Armor: 3 } && j.Events<DamageAppliedEvent>().Length == 0 &&
            j.Events<DamageReplacedWithHpLossEvent>() is [{ PolicyOwnerSeat: 0, TargetSeat: 2, Amount: 2 }],
            "The native Jueqing replacement must agree with its public prediction and preserve all recipient Armor.");
        j.Cold();

        var x = Scene.Start("maximum");
        x.Use("armor-other", [3]);
        x.PlayCard(CardKind.Slash, 3);
        Need(x.V(3) is { Hp: 6, MaxHp: 7, Armor: 3 } && x.Events<DamageAppliedEvent>().Length == 0 &&
            x.Events<CardDamageReplacedWithMaximumHpLossEvent>() is [{ Receipt.TargetSeat: 3, Receipt.Amount: 1 }],
            "The formal first-Slash Xiaoming witness must reduce maximum HP before Armor payment, rather than spend Armor on a fictional damage segment.");
        x.Cold();

        var nativeLoss = Scene.Start("targets");
        var predictedNativeLoss = nativeLoss.Action("lose-two").ProgramAiHint!.PredictTarget(1, 8, 0);
        nativeLoss.UseUntilDying("lose-two", 2);
        Need(predictedNativeLoss is { HpAfter: -1, HpLossAmount: 2, DirectHpLossAmount: 2 } &&
            nativeLoss.V(2) is { IsAlive: true, Hp: -1 } &&
            nativeLoss.Events<ProgramSkillHpLostEvent>() is [{ TargetSeat: 2, Amount: 2, RemainingHp: -1 }],
            "The real generic loseHp operation preserves the full rescue debt, and its public hint must match the signed payment.");
        nativeLoss.Cold();
        var dying = Scene.Start("negative");
        dying.Use("armor-other", [2]);
        var negativeLoss = dying.Action("hit-two").ProgramAiHint!.PredictTarget(1, 8, 3);
        dying.UseUntilDying("hit-two", 2);
        Need(negativeLoss is { HpAfter: -1, HpLossAmount: 2, ArmorAfter: 3 } &&
            dying.V(2) is { IsAlive: true, Hp: -1, Armor: 3 } &&
            dying.Events<DamageReplacedWithHpLossEvent>() is [{ TargetSeat: 2, Amount: 2, RemainingHp: -1 }],
            "A genuine Jueqing replacement must preserve its signed rescue debt and bypass Armor, consistently with native HP loss.");
        unchanged = dying.Boundary();
        var support = new SkillProgramAiHint(0, 0, 0, 0, 1, 0, false, false);
        Need(double.IsFinite(new SimpleAiBrain(0, 83, AiDecisionPolicy.CurrentVersion)
            .ScoreProgramTarget(dying.G.CreateSnapshot(0), 2, support)),
            "Public support scoring must accept the genuine negative-HP dying recipient without throwing.");
        var selfRecovery = dying.Registry.GetSkill(Driver).Program!.Activations.Single(a => a.Id == "self-recover").Effects;
        var rescueEstimate = ProgramCompositionAi.Estimate(selfRecovery,
            new PlayerSkillContext(0, -1, 8, 0, TurnPhase.Play, IsOwnTurn: true));
        Need(rescueEstimate.Hint.OwnerRecovery == 3 && !rescueEstimate.IsSelfLethal &&
            rescueEstimate.Hint.OwnerWouldEnterDying == false &&
            PublicDamageSequencePrediction.Calculate(-1, 8, 3, [new(PublicHealthEffectKind.Recover, 1)]) is
                { HpAfter: 0, ArmorAfter: 3, IsLethal: false } &&
            PublicDamageSequencePrediction.Calculate(-1, 8, 3, [new(PublicHealthEffectKind.Recover, 2)]) is
                { HpAfter: 1, ArmorAfter: 3, IsLethal: false } && dying.Boundary() == unchanged,
            "Recovery must pay the signed rescue debt exactly, remain a pure query and not mislabel an existing dying state as self-inflicted lethality.");
        dying.Cold();
    }

    private static ProgramAiEstimate Estimate(Scene scene, string activationId)
    {
        var owner = scene.V();
        var effects = scene.Registry.GetSkill(Driver).Program!.Activations.Single(a => a.Id == activationId).Effects;
        return ProgramCompositionAi.Estimate(effects,
            new PlayerSkillContext(0, owner.Hp, owner.MaxHp, owner.HandCount, TurnPhase.Play, IsOwnTurn: true)
            { Armor = owner.Armor });
    }

    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public ContentRegistry Registry { get; } = registry;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction Action(string id) => G.GetHumanLegalActions().Single(a =>
            a.ProgramSkillId == Driver && a.ProgramActivationId == id);
        public void Use(string id, int[]? targets = null)
        {
            Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, G.PendingDecision!.PromptId));
            Play();
        }
        public void UseUntilDying(string id, int target)
        {
            Accept(new UseProgramSkillCommand(0, Driver, id, [], [target], G.Revision, G.PendingDecision!.PromptId));
            for (var step = 0; step < 120; step++)
            {
                if (V(target).Hp <= 0 && G.ResolutionStack.OfType<DyingFrame>().Any(frame => frame.VictimSeat == target)) return;
                Need(V(target).IsAlive, "The direct-loss witness must pause before the native dying response resolves.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded native dying child.");
        }
        public void PlayCard(CardKind kind, int target)
        {
            var action = G.GetHumanLegalActions().Single(a => a.CardId is { } id &&
                V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null &&
                a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision,
                G.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
            Play();
        }
        private void Accept(GameCommand command)
        {
            var result = G.Submit(command);
            Need(result.Accepted, result.Error?.Message ?? "The Armor AI fixture rejected a genuine command.");
        }
        private void Play()
        {
            for (var step = 0; step < 180; step++)
            {
                if (G.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
                Need(G.State.Winner == Winner.None, "The small Armor AI scenario must not finish a match.");
                Need(G.PendingDecision?.PlayerSeat != 0,
                    "Unexpected Armor AI fixture prompt: " + JsonSerializer.Serialize(G.PendingDecision));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Armor AI fixture did not return to Play.");
        }
        public string Boundary() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(),
            Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = Boundary();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), Registry);
            Need(Boundary() == before, "Cold replay must preserve the actual AI command, Armor payments and every public/private view exactly.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var extras = scene is "jueqing" or "negative" ? new[] { Jueqing } : Array.Empty<string>();
            ShanheStartingCard[] cards = scene is "jueqing" or "maximum"
                ? [new(CardKind.Slash, Suit.Spade, 6)] : [];
            var setup = new ShanheBattleSetup(83, 1, "fixture-armor-ai-" + scene, Owner,
                scene == "sequences" ? 2 : 8, 0, 0, 0, 0, extras, cards, [],
                [new(FirstEnemy, Hp: scene is "targets" or "negative" ? 1 : 6,
                    ExtraSkillIds: scene == "visibility" ? [Jueqing, Gangzhi] : []), new(SecondEnemy, Hp: scene == "targets" ? 2 : 6)],
                [new(Ally)], new string('e', 64))
            { TacticSkillIds = scene == "maximum" ? [Xiaoming] : [] };
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 4, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var result = new Scene(game, registry);
            result.Accept(new StartGameCommand()); result.Play(); return result;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-armor-ai", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Jueqing, Xiaoming, Gangzhi }) builder.AddSkill(Production.Value.GetSkill(id));
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"armor-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"owner","amount":3}]},
                  {"id":"armor-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":3}]},
                  {"id":"hit-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                  {"id":"lose-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"self-double","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2},{"op":"damage","target":"owner","amount":2}]},
                  {"id":"grant-first","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"owner","amount":3},{"op":"damage","target":"owner","amount":2},{"op":"damage","target":"owner","amount":2}]},
                  {"id":"damage-first","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2},{"op":"grantArmor","target":"owner","amount":3},{"op":"damage","target":"owner","amount":2}]},
                  {"id":"self-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":2}]},
                  {"id":"self-recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":3}]}]}]}
                """;
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实护甲AI操作", description = "通过原生伤害、扣血和赋甲指令检查公开AI估计" } }
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new(Driver, catalog.Presentations[Driver].Name, catalog.Presentations[Driver].Description)
            { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            builder.AddGeneral(new(Owner, "护甲AI使用者", "", Driver, "shu", 8));
            foreach (var id in new[] { Ally, FirstEnemy, SecondEnemy }) builder.AddGeneral(new(id, "真实候选", "", "standard:none", "qun", 8));
            builder.AddDeck(new(Deck, "零自然摸牌真实牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "护甲AI原生战斗", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, FirstEnemy, SecondEnemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
