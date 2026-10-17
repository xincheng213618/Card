using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OlMoSunQuanMeteorChecks
{
    private const string MeteorDriver = "fixture:meteor-driver", MeteorWatch = "fixture:meteor-watch";
    private const string MeteorOwner = "fixture:meteor-owner", MeteorMode = "identity:classic-meteor-spear";
    private const string MeteorContest = "boundary:qiaoshui-current";

    public static void MeteorZhangbaRequiresCompleteInitialSingletonAfterOrdinaryQuotaCold()
    {
        foreach (var exhausted in new[] { false, true })
        {
            var s = MeteorScenario.Start(exhausted);
            s.Use("equip", targets: [0]); s.Play();
            s.Need(s.V().Equipment.Count(c => c.Kind == CardKind.ZhangbaSerpentSpear) == 1,
                "The real registered Spear is actually equipped before the quota/target test.");
            s.Use("stock"); s.Play();
            var gift = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id;
            s.Use("give", [gift], [1]); s.Play();
            s.Need(s.V(1).HandCount == 1 && s.G.CardMovements.Count(m => m.CardId == gift &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
                "The Pindian opponent receives one real known material through an accepted Hand gift.");
            if (exhausted)
            {
                var slash = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
                    a.CardId is > 0 && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
                s.Accept(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, s.G.Revision, s.P!.PromptId,
                    slash.PlayedCardKind, slash.TargetCardId));
                s.Hold(); var original = s.LiveSlash(); s.Cold(); s.Continue(); s.Play();
                s.Need(s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0 &&
                    e.Debit.CardActionId == original.Action!.ActionId) == 1 &&
                    s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.Id) == 1,
                    "One real ordinary Slash debits the actual Play quota before the next-use grant is issued.");
            }
            var contestCard = s.V().Hand.First(c => c.Kind == CardKind.Slash && c.Suit == Suit.Heart).Id;
            s.Accept(new UseProgramSkillCommand(0, MeteorContest, "contest", [contestCard], [1], s.G.Revision, s.P!.PromptId));
            s.Play();
            var grant = s.E<NextActualUseTargetAdjustmentGrantedEvent>().Single();
            s.Need(s.E<PindianResultDeterminedEvent>().Single().Result is { SourceWon: true, SourceRank: 13 } &&
                s.G.CardMovements.Count(m => m.CardId == contestCard && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.Processing) == 1 &&
                !s.E<NextActualUseTargetAdjustmentConsumedEvent>().Any(e => e.GrantProgramFrameId == grant.ProgramFrameId),
                "The real winning Heart/Tianbian Pindian pays its original material and issues an unconsumed current-next-use target adjustment.");
            var spear = s.G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseEquipmentEffect &&
                a.EquipmentKind == CardKind.ZhangbaSerpentSpear);
            var ids = spear.SelectableCardIds.Where(id => s.V().Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash)).Take(2).ToArray();
            s.Need(ids.Length == 2 && spear.MinCardCount == 2 && spear.MaxCardCount == 2 &&
                spear.SelectableTargetSeats.Contains(1) && spear.SelectableTargetSeats.Contains(2) &&
                spear.MaxTargetCount == (exhausted ? 1 : 2),
                "Only Meteor-only admission caps the actual adjusted Spear menu to one initial target; ordinary available quota preserves the native two-target action.");
            s.Cold();
            if (exhausted)
            {
                var before = s.State();
                var rejected = s.G.Submit(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear,
                    ids, [1, 2], s.G.Revision, s.P!.PromptId));
                s.Need(!rejected.Accepted && before == s.State() &&
                    !s.E<NextActualUseTargetAdjustmentConsumedEvent>().Any(e => e.GrantProgramFrameId == grant.ProgramFrameId),
                    "A forged complete two-target request cannot borrow Meteor separately for each target, mutate any private view, debit quota, consume the grant or pay either material.");
            }
            var targets = exhausted ? new[] { 1 } : new[] { 1, 2 };
            var debitBefore = s.E<CardUseDebitRecordedEvent>().Length;
            var damageBefore = s.E<DamageAppliedEvent>().Length;
            s.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, ids, targets, s.G.Revision, s.P!.PromptId));
            s.Hold(); var use = s.LiveSlash();
            s.Need(use.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0 } action &&
                action.EffectiveKind == CardKind.Slash && action.TargetSeats.SequenceEqual(targets) &&
                action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(ids.Order()) &&
                use.TargetSeats.SequenceEqual(targets) && use.PhysicalCardIds!.Order().SequenceEqual(ids.Order()) &&
                ids.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1) &&
                !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == use.Id) &&
                s.E<CardUseDebitRecordedEvent>().Length == debitBefore + 1 &&
                s.E<NextActualUseTargetAdjustmentConsumedEvent>().Count(e => e.GrantProgramFrameId == grant.ProgramFrameId &&
                    e.OriginFrameId == use.Id) == 1,
                "The admitted original native Spear use retains its complete target list and two actual payments, consumes the adjustment once, still debits the ordinary counter, and waits for its real committed observer.");
            if (exhausted) s.Need(use.MoQuanyuQuota is { OriginalTargetSeat: 1 } quota && quota.UseId == use.Id &&
                s.E<MoQuanyuQuotaAcceptedEvent>().Count(e => e.Receipt == quota) == 1,
                "The legal exhausted-quota fallback has one exact initial-target Meteor receipt, rather than an unchecked multi-target exemption.");
            else s.Need(use.MoQuanyuQuota is null,
                "The genuine two-target control uses ordinary quota and does not acquire a singleton Meteor receipt.");
            s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardKind == CardKind.Slash) == 1 &&
                s.E<DamageAppliedEvent>().Skip(damageBefore).Where(e => e.SourceSeat == 0 && !e.SourceLess)
                    .Select(e => e.TargetSeat).Order().SequenceEqual(targets.Order()) &&
                ids.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1),
                "Cold recovery finishes the same native full target cursor once, with one direct damage per actual target and no repeated payment.");
        }
        InheritedHeartAppearanceKeepsIndependentPhaseQuotaCold();
    }

    private static void InheritedHeartAppearanceKeepsIndependentPhaseQuotaCold()
    {
        var s = MeteorScenario.Start(false, appearance: true);
        s.Use("equip", targets: [0]); s.Play(); s.Use("stock"); s.Play();
        s.Need(s.E<MoQuanyuChoiceRevealedEvent>().Single(e => e.ParticipantSeat == 0).Effect == MoQuanyuEffect.Baihong,
            "The appearance control has a real non-Meteor choice, so no singleton Meteor fallback can hide a false ordinary-quota rejection.");
        var first = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId is > 0 &&
            a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]) && s.V().Hand.Any(c => c.Id == a.CardId && c.Suit == Suit.Heart));
        s.Accept(new PlayCardCommand(0, first.CardId!.Value, first.TargetSeats, s.G.Revision, s.P!.PromptId, first.PlayedCardKind, first.TargetCardId));
        s.Hold(); var firstUse = s.LiveSlash(); s.Continue(); s.Play();
        s.Need(s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == firstUse.Id) == 1,
            "An actual Heart Slash first exhausts ordinary quota and establishes the genuine previous Play appearance.");
        s.Accept(new UseProgramSkillCommand(0, "classic:chenglue", "alternate", [], [], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, IsPrivate: true } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "alternating-suit-top"));
        s.Need(Enumerable.Range(1, 3).All(viewer => s.G.CreateSnapshot(viewer).PendingDecision is null),
            "The real Chenglue payment remains private to the actual hand owner.");
        s.Cold();
        for (var index = 0; index < 2; index++)
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "alternating-suit-top" &&
                c.Cards is [var id] && s.V().Hand.Any(h => h.Id == id && h.Suit == Suit.Heart));
        s.Play();
        var allowance = s.E<PlayPhaseSuitAllowanceGrantedEvent>().Single();
        s.Need(allowance.OwnerSeat == 0 && allowance.Suits.SequenceEqual([Suit.Heart]) && allowance.DiscardedCardIds.Count == 2 &&
            allowance.DiscardedCardIds.All(id => s.G.CardMovements.Count(m => m.CardId == id &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.alternating-suit.discard") == 1),
            "A genuine two-Heart discard, rather than a numeric fixture modifier, grants the independent current-Play suit allowance.");
        var gift = s.V().Hand.First(c => c.Kind == CardKind.Slash && c.Suit == Suit.Heart).Id;
        s.Use("give", [gift], [1]); s.Play();
        var contest = s.V().Hand.First(c => c.Kind == CardKind.Slash && c.Suit == Suit.Heart).Id;
        s.Accept(new UseProgramSkillCommand(0, MeteorContest, "contest", [contest], [1], s.G.Revision, s.P!.PromptId)); s.Play();
        var grant = s.E<NextActualUseTargetAdjustmentGrantedEvent>().Single();
        s.Need(s.E<PindianResultDeterminedEvent>().Single().Result.SourceWon,
            "The multi-target control still obtains its target adjustment through real Pindian.");
        var material = s.V().Hand.First(c => c.Suit == Suit.Spade && c.Kind == CardKind.ZhangbaSerpentSpear);
        var action = s.G.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.CardId == material.Id &&
            a.ConversionSource?.SkillId == "boundary:jianying" && a.PlayedCardKind == CardKind.Slash &&
            a.ProgramActivationId == "next-actual-use-target-adjustment" && a.TargetSeats.SequenceEqual([1, 2]));
        s.Need(action is not null && material.Suit == Suit.Spade &&
            s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0) == 1,
            "The real Spade material's inherited Heart Slash remains published with both original targets under the independent suit allowance after ordinary quota is exhausted.");
        s.Cold(); var debitBefore = s.E<CardUseDebitRecordedEvent>().Length;
        s.Accept(new PlayCardCommand(0, material.Id, action!.TargetSeats, s.G.Revision, s.P!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        s.Hold(); var use = s.LiveSlash();
        s.Need(use.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Slash,
                EffectiveSuit: Suit.Heart, PhysicalCards: [var cost] } actual && cost.CardId == material.Id &&
            cost.CardKind == CardKind.ZhangbaSerpentSpear && cost.From == CardLocation.Hand(0) &&
            actual.ConversionChain.Any(source => source.SkillId == "boundary:jianying") &&
            use.TargetSeats.SequenceEqual([1, 2]) && use.MoQuanyuQuota is null &&
            s.E<CardUseDebitRecordedEvent>().Length == debitBefore &&
            s.E<NextActualUseTargetAdjustmentConsumedEvent>().Count(e => e.GrantProgramFrameId == grant.ProgramFrameId && e.OriginFrameId == use.Id) == 1 &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == use.Id),
            "The accepted native action records actual Heart appearance and both targets while its original physical invoice remains the Spade Spear; the suit allowance retains its own debit semantics without a Meteor receipt.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == material.Id && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            s.E<CardUseDebitRecordedEvent>().Length == debitBefore,
            "Cold replay completes the same transformed entity once without repaying or losing the independent Heart allowance.");
    }

    private sealed class MeteorScenario(bool exhausted, GameEngine game, ContentRegistry registry, bool appearance = false)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string id, int[]? cards = null, int[]? targets = null) =>
            Accept(new UseProgramSkillCommand(0, MeteorDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "A real human choice is published."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Hold() => Reach(() => P?.SkillPrompt?.SkillId == MeteorWatch && P.PlayerSeat == 0);
        public CardUseFrame LiveSlash() => G.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Slash);
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "No terminal game before boundary " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mo-quanyu-choice"))
                    Answer(c => c.Parameters.GetValueOrDefault("effect") == (appearance ? "1" : "6"));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public string State() => JsonSerializer.Serialize(new { G.Revision,
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains every private view, native Pindian grant, accepted Spear payment, whole target cursor and quota fact."); }
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, command.GetType().Name + " " + result.Error?.Code + "/" + result.Error?.Message); }
        public void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Exhausted = exhausted, Appearance = appearance, G.Revision, G.State.TurnNumber,
            G.State.Phase, G.State.CurrentSeat, G.State.Winner, Prompt = P is { } p ? new { p.Kind, p.PlayerSeat,
                Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(4).Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }) } : null,
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }),
            Debits = E<CardUseDebitRecordedEvent>().Length, Grants = E<NextActualUseTargetAdjustmentGrantedEvent>().Length });
        public static MeteorScenario Start(bool exhausted, bool appearance = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new MeteorFixture(appearance));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = MeteorMode, UseInteractiveSetup = true, ConfirmOpeningHand = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 4, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new MeteorScenario(exhausted, g, r, appearance); s.Accept(new StartGameCommand());
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, MeteorOwner, g.Revision, s.P!.PromptId)); s.Play();
            s.Need(s.E<MoQuanyuChoiceRevealedEvent>().Single(e => e.ParticipantSeat == 0).Effect ==
                (appearance ? MoQuanyuEffect.Baihong : MoQuanyuEffect.Liuxing),
                "The human's genuine first-round private choice establishes the intended native allowance, rather than injected history or a numeric modifier.");
            return s;
        }
    }

    private sealed class MeteorFixture(bool appearance) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mo-meteor-spear", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static void Production(IContentRegistryBuilder builder, string bundle, params string[] ids)
            {
                string Read(string part) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + part + ".json") ?? throw new InvalidOperationException("Missing real embedded " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var id in ids) builder.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description)
                { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            }
            Production(b, "ol-mo-sun-quan", "ol:mo-quanyu", "ol:mo-tianen", "ol:mo-qiangang", "ol:mo-rumo");
            Production(b, "boundary-jian-yong", MeteorContest); Production(b, "classic-qin-mi", "classic:tianbian");
            if (appearance)
            {
                Production(b, "classic-ju-shou", "boundary:jianying");
                Production(b, "classic-xu-you", "classic:chenglue");
            }
            // Import only the genuine existing equipment definition, with no
            // extra classic generals, skills, deck or unrelated mode.
            new StandardClassicGeneralPackage().Register(new MeteorSpearBuilder(b));
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:meteor-driver","revision":1,"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"spear"}]},
                {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]}],
               "modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":60,"priority":0}]},
              {"id":"fixture:meteor-watch","revision":1,"triggers":[{"id":"paid-native-slash","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observed","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var presentations = new Dictionary<string, object>
            {
                [MeteorDriver] = new { name = "真实流星丈八驱动", description = "真实装备、获得及转交原实体。" },
                [MeteorWatch] = new { name = "真实已付杀暂停", description = "保留真实材料及原生目标。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var local = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, p) in local.Programs) b.AddSkill(new(id, id, "真实丈八边界") { Program = p, ProgramPresentation = local.Presentations[id] });
            const string ownPick = "fixture:meteor-own-pick";
            b.AddSkill(new(ownPick, "本人", "真人选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -1000000d) });
            var ownSkills = new List<string> { "ol:mo-quanyu", MeteorContest, "classic:tianbian", MeteorDriver, MeteorWatch };
            if (appearance) ownSkills.AddRange(["boundary:jianying", "classic:chenglue"]);
            b.AddGeneral(new(MeteorOwner, "真实流星本人", "supporter", ownPick, "wu", 12, ownSkills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:meteor-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:meteor-peer-pick-" + i;
                b.AddSkill(new(pick, "其他角色", "公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => (double)(4000 - i * 100)) });
                b.AddGeneral(new(peers[i - 1], "真实目标", "supporter", pick, "shu", 12));
            }
            b.AddDeck(new("fixture:meteor-deck", "固定真实32实体", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 16).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 7))
                .Concat(Enumerable.Range(0, 16).Select(_ => new ContentDeckPhysicalCard("classic:zhangba-serpent-spear", Suit.Spade, 12))).ToArray() });
            b.AddMode(new(MeteorMode, "流星丈八真实4人边界", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:meteor-deck", GeneralCandidateCount: 4, GeneralPoolIds: [MeteorOwner, .. peers]));
        }
    }
    private sealed class MeteorSpearBuilder(IContentRegistryBuilder inner) : IContentRegistryBuilder
    {
        public void AddCard(ContentCardDefinition d) { if (d.LegacyKind == CardKind.ZhangbaSerpentSpear) inner.AddCard(d); }
        public void AddSkill(ContentSkillDefinition d) { }
        public void AddGeneral(ContentGeneralDefinition d) { }
        public void AddDeck(ContentDeckRecipe d) { }
        public void AddMode(ContentModeDefinition d) { }
    }
}
