using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TargetedTrickHandHpChecks
{
    private const string Bing = "ol:bingzheng", She = "ol:sheyan", Driver = "fixture:target-hand-hp-driver";
    private const string Watch = "fixture:target-hand-hp-watch", Owner = "fixture:target-hand-hp-owner";
    private const string Loss = "fixture:target-hand-hp-loss";
    private const string Mode = "identity:classic-target-hand-hp";

    public static void ExactEntryContractsAndFrozenNestedEvents()
    {
        var original = Embedded("rules.json"); var labels = Embedded("presentation.json");
        _ = SkillProgramCatalog.Load(original, labels);
        foreach (var mutate in new Action<JsonObject>[]
        {
            t => t["optional"] = false,
            t => t["usageScope"] = "game",
            t => t["window"] = "turnEnding",
            t => t["effects"]![0]!["target"] = "selectedTarget",
            t => t["effects"]![0]!["amount"] = 1,
            t => t["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""))
        })
            foreach (var skill in new[] { Bing, She })
            {
                var invalid = JsonNode.Parse(original)!; var t = invalid["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == skill)!["triggers"]![0]!.AsObject();
                mutate(t); var rejected = false;
                try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), labels); }
                catch (InvalidOperationException) { rejected = true; }
                Need(rejected, "The finite ending/target operation rejects unsupported contexts, extra costs and instructions before runtime.");
            }
        var targets = new List<int> { 0, 1 }; var cards = new List<int> { 8 }; var parameters = new Dictionary<string, string> { ["branch"] = "add" };
        var receipt = new TargetedOrdinaryTrickReceipt { OriginalTargets = targets, Choices = [new(new("freeze"), "freeze", cards, targets, parameters)] };
        var fact = (TargetedOrdinaryTrickOfferedEvent)CommittedEventProjection.Freeze(new TargetedOrdinaryTrickOfferedEvent(4, receipt));
        targets.Add(2); cards.Add(9); parameters["branch"] = "changed";
        Frozen(fact.Receipt.OriginalTargets); Frozen(fact.Receipt.Choices); Frozen(fact.Receipt.Choices[0].Cards); Frozen(fact.Receipt.Choices[0].Targets);
        Need(fact.Receipt.OriginalTargets.SequenceEqual([0, 1]) && fact.Receipt.Choices[0].Cards.SequenceEqual([8]) &&
            fact.Receipt.Choices[0].Parameters["branch"] == "add" && fact.Receipt.Choices[0].Parameters is IDictionary<string, string> { IsReadOnly: true },
            "Committed target events detach every nested source list and parameter dictionary.");
        var resolved = (TargetedOrdinaryTrickResolvedEvent)CommittedEventProjection.Freeze(new TargetedOrdinaryTrickResolvedEvent(4, 5, 6,
            new(She, "targeted-trick-adjustment", 0, "source"), "hash", targets, targets, null, targets));
        Frozen(resolved.BeforeTargets); Frozen(resolved.AddedTargets); Frozen(resolved.ResultTargets);
    }

    public static void ActualEndingDrawDiscardSelfAndOptionalGiftColdReturn()
    {
        foreach (var discard in new[] { false, true })
        {
            var (g, registry) = Start("standard:dodge", discard ? 3 : 5);
            var ownerBefore = V(g, 0).HandCount;
            End(g); Reach(g, p => IsOptional(p, Bing)); g = Cold(g, registry); Activate(g);
            ReachStage(g, UnequalHandHpStage.Target); Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])) == (ownerBefore != V(g, 0).Hp),
                "Only an actually unequal own hand/HP is a legal ending participant; the initial four-card four-HP owner is excluded.");
            Answer(g, c => c.Targets.SequenceEqual([1])); ReachStage(g, UnequalHandHpStage.Action);
            Answer(g, c => c.Parameters["branch"] == (discard ? "discard" : "draw"));
            if (discard)
            {
                ReachStage(g, UnequalHandHpStage.Discard); var p = P(g)!;
                Need(p.PlayerSeat == 1 && p.IsPrivate && g.CreateSnapshot(0).PendingDecision is null && p.Choices.All(c => c.Cards.Count == 1),
                    "The target chooses its own real hand payment privately; the owner cannot see or choose another player's hand.");
                g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1);
            }
            ReachStage(g, UnequalHandHpStage.Gift);
            var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.UnequalHandHp is not null).UnequalHandHp!;
            Need(receipt.RewardQualified == true && V(g, 1).HandCount == V(g, 1).Hp && V(g, 0).HandCount == ownerBefore + 1 &&
                receipt.Payments.Count == 2 && E<UnequalHandHpRewardQualifiedEvent>(g).Single().Qualified,
                "One real adjustment reaches equality, then rewards the skill owner once before optional gifting.");
            Private(g, 0); RejectWrongActor(g); g = Cold(g, registry);
            var gifted = P(g)!.Choices.First(c => c.Parameters["branch"] == "give-card").Cards.Single();
            Answer(g, c => c.Cards.SequenceEqual([gifted])); DrainEnding(g);
            Need(E<UnequalHandHpCompletedEvent>(g).Count() == 1 && g.CardMovements.Count(m => m.CardId == gifted && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.Hand(1) && m.Reason.Value == Reason("gift")) == 1 && E<UnequalHandHpMovementIssuedEvent>(g).Count(e => e.Payment.Step == "reward") == 1,
                "The gift, adjustment and reward keep one paid return through cold replay.");
            _ = Cold(g, registry);
        }
        var (self, selfRegistry) = Start("standard:dodge", 5);
        var trim = V(self, 0).Hand.Take(V(self, 0).HandCount - V(self, 0).Hp + 1).Select(c => c.Id).ToArray();
        Use(self, "trim", trim); Play(self); Need(V(self, 0).HandCount == V(self, 0).Hp - 1, "An actual discard prepares the self adjustment.");
        End(self); Reach(self, p => IsOptional(p, Bing)); Activate(self); ReachStage(self, UnequalHandHpStage.Target);
        Answer(self, c => c.Targets.SequenceEqual([0])); ReachStage(self, UnequalHandHpStage.Action); Answer(self, c => c.Parameters["branch"] == "draw");
        DrainEnding(self);
        Need(E<UnequalHandHpRewardQualifiedEvent>(self).Single() is { TargetSeat: 0, Qualified: true } &&
            E<UnequalHandHpMovementIssuedEvent>(self).Count(e => e.Payment.Step == "adjust-draw") == 1 &&
            E<UnequalHandHpMovementIssuedEvent>(self).Count(e => e.Payment.Step == "reward") == 1 &&
            E<UnequalHandHpMovementIssuedEvent>(self).All(e => e.Payment.Step != "gift"),
            "Self adjustment gives the owner both actual draws without a meaningless self-transfer.");
        _ = Cold(self, selfRegistry);
    }

    public static void ActualAdjustmentChildrenRecheckHpAndPreservePaidSourceLoss()
    {
        foreach (var child in new[] { "change-hand", "change-hp", "lose-source" })
        {
            var (g, registry) = Start("standard:dodge", 5, child);
            if (child == "lose-source") { Use(g, "trim", V(g, 0).Hand.Take(V(g, 0).HandCount - V(g, 0).Hp + 1).Select(c => c.Id).ToArray()); Play(g); }
            End(g); Reach(g, p => IsOptional(p, Bing)); Activate(g); ReachStage(g, UnequalHandHpStage.Target);
            Answer(g, c => c.Targets.SequenceEqual([child == "lose-source" ? 0 : 1])); ReachStage(g, UnequalHandHpStage.Action); Answer(g, c => c.Parameters["branch"] == "draw");
            Reach(g, p => p.SkillPrompt?.SkillId == Watch); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.UnequalHandHp is not null);
            Need(root.UnequalHandHp!.Stage == UnequalHandHpStage.AdjustmentChildren && root.PendingMovementContinuation is not null &&
                !E<UnequalHandHpRewardQualifiedEvent>(g).Any(), "An actual gain child pauses before comparing current hand/HP or giving the reward.");
            g = Cold(g, registry); Continue(g);
            if (child == "lose-source")
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Loss);
                Need(V(g, 0).Hp == 4 && V(g, 0).MaxHp == 4 && V(g, 0).Skills!.Any(s => s.ContentId == Bing),
                    "The actual Dying entry recovers to the owner's four HP before its terminal source replacement.");
                g = Cold(g, registry); Continue(g);
                Need(!V(g, 0).Skills!.Any(s => s.ContentId == Bing), "The paid original skill source actually disappeared through a legal Dying-entry replacement.");
                g = Cold(g, registry);
            }
            DrainEnding(g);
            Need(E<UnequalHandHpRewardQualifiedEvent>(g).Single().Qualified == (child == "lose-source") &&
                E<UnequalHandHpMovementIssuedEvent>(g).Count(e => e.Payment.Step == "reward") == (child == "lose-source" ? 1 : 0) &&
                E<UnequalHandHpCompletedEvent>(g).Count() == 1, "Equality follows all real adjustment children; an issued paid source keeps its one completion.");
            _ = Cold(g, registry);
        }
    }

    public static void TargetedTrickAdditionIneffectivenessAndCompletePhysicalUse()
    {
        foreach (var kind in new[] { "standard:draw_two", "standard:iron_chain", "standard:duel", "standard:fire_attack", "standard:dismantlement", "standard:snatch" })
        {
            var (g, registry) = Start(kind, 5); var physical = V(g, 0).Hand.First().Id;
            var action = g.GetHumanLegalActions().First(a => a.CardId == physical && a.Kind != LegalActionKind.Recast &&
                (kind == "standard:draw_two" || a.TargetSeats.SequenceEqual([1])));
            Accept(g, new PlayCardCommand(0, physical, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind) { TargetCardId = action.TargetCardId });
            Reach(g, p => IsOptional(p, She)); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(); var beforeAction = use.Action!;
            Need(beforeAction.EffectiveSuit == Suit.Spade && beforeAction.EffectiveRank == 7 && beforeAction.EffectiveIsRed == false,
                "A catalog containing only the new target ability captures the real card appearance for added-target suit and color legality.");
            g = Cold(g, registry); Activate(g); Reach(g, p => IsSheChoice(p));
            Need(P(g)!.Choices.Any(c => c.Parameters["branch"] == "add") && P(g)!.Choices.All(c => c.Parameters["branch"] != "ineffective"),
                "A unique effective ordinary trick target can add a legal participant but cannot nullify the last effective target.");
            if (kind == "standard:snatch") Need(P(g)!.Choices.All(c => !c.Targets.Contains(2)), "Added Snatch targets retain the actual user's one-seat distance restriction.");
            var extra = P(g)!.Choices.First(c => c.Parameters["branch"] == "add").Targets;
            g = Cold(g, registry); Answer(g, c => c.Parameters["branch"] == "add" && c.Targets.SequenceEqual(extra));
            Need(g.ResolutionStack.OfType<CardUseFrame>().Single().Action is { } after && after.ActionId == beforeAction.ActionId &&
                after.ActorSeat == beforeAction.ActorSeat && after.ProviderSeat == beforeAction.ProviderSeat && after.PhysicalCards.SequenceEqual(beforeAction.PhysicalCards),
                "Target addition preserves the exact accepted use, provider and paid physical entity.");
            FinishUse(g, use.Id);
            Need(E<TargetedOrdinaryTrickResolvedEvent>(g).Count(e => e.CardUseFrameId == use.Id && e.AddedTargets.Count > 0) == 1 &&
                g.CardMovements.Count(m => m.CardId == physical && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1,
                "Every supported single-target trick resolves the added target and moves its actual material once at whole-use completion.");
            _ = Cold(g, registry);
        }
        var (group, groupRegistry) = Start("standard:arrow_barrage", 5); var card = V(group, 0).Hand.First().Id;
        Accept(group, new PlayCardCommand(0, card, [], group.Revision, P(group)!.PromptId)); Reach(group, p => IsOptional(p, She)); Activate(group); Reach(group, IsSheChoice);
        var groupUse = group.ResolutionStack.OfType<CardUseFrame>().Single(); var originalTargets = groupUse.TargetSeats.ToArray();
        Answer(group, c => c.Parameters["branch"] == "ineffective" && c.Targets.SequenceEqual([1]));
        Need(group.ResolutionStack.OfType<CardUseFrame>().Single().TargetSeats.SequenceEqual(originalTargets) &&
            group.ResolutionStack.OfType<CardUseFrame>().Single().IneffectiveTargetSeats!.Contains(1),
            "Ineffectiveness keeps the actual original target identity and all other group targets.");
        FinishUse(group, groupUse.Id);
        Need(E<CardEffectSkippedEvent>(group).Any(e => e.TargetSeat == 1) && E<CardUseFinishedEvent>(group).Count(e => e.ResolutionId == groupUse.Id) == 1,
            "The native group target loop skips precisely the selected effect and finishes the same whole use.");
        _ = Cold(group, groupRegistry);
    }

    public static void CompoundTargetsFreshCandidatesAndLastEffectiveDraft()
    {
        var (g, registry) = Start("fixture:target-hand-hp-borrowed", 5);
        Use(g, "equip", targets: [1]); Play(g); Use(g, "equip", targets: [2]); Play(g);
        var sword = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats[0] == 1);
        Accept(g, new PlayCardCommand(0, sword.CardId!.Value, sword.TargetSeats, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 1 && IsOptional(p, She)); Activate(g); Reach(g, IsSheChoice);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(); var actionId = use.Action!.ActionId;
        var pair = P(g)!.Choices.First(c => c.Parameters["branch"] == "add" && c.Targets[0] == 2).Targets.ToArray();
        Need(pair.Length == 2 && pair[0] != pair[1], "Borrowed Sword addition chooses both its new armed primary and legal Slash victim.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual(pair));
        Reach(g, p => p.PlayerSeat == 2 && IsOptional(p, She)); Activate(g); Reach(g, IsSheChoice);
        Need(P(g)!.Choices.Any(c => c.Parameters["branch"] == "ineffective" && c.Targets.SequenceEqual([1])) &&
            P(g)!.Choices.Where(c => c.Parameters["branch"] == "ineffective").All(c => c.Targets.Single() is 1 or 2),
            "A newly added actual primary receives its existing target-relation ability; the victim is not a second primary effect.");
        Answer(g, c => c.Parameters["branch"] == "ineffective" && c.Targets.SequenceEqual([1]));
        Need(E<TargetedOrdinaryTrickResolvedEvent>(g).Last(e => e.CardUseFrameId == use.Id) is { } resolved && resolved.ActionId == actionId &&
            resolved.ResultTargets.SequenceEqual(sword.TargetSeats.Concat(pair)),
            "Compound invalidation keeps both original and appended pairs on the same accepted action.");
        FinishUse(g, use.Id);
        Need(E<BorrowedSwordResolvedEvent>(g).Where(e => e.ResolutionId == use.Id).All(e => e.WeaponOwnerSeat != 1) &&
            E<BorrowedSwordResolvedEvent>(g).Any(e => e.ResolutionId == use.Id && e.WeaponOwnerSeat == 2 && e.SlashTargetSeat == 1 &&
                (e.UsedSlash || e.TransferredWeaponCardId is not null)),
            "Only the still-effective added primary performs its actual forced-use or weapon-transfer outcome."); _ = Cold(g, registry);

        var (draft, draftRegistry) = Start("standard:five_grains", 5); var card = V(draft, 0).Hand.First().Id;
        Accept(draft, new PlayCardCommand(0, card, [], draft.Revision, P(draft)!.PromptId));
        Reach(draft, p => IsOptional(p, She)); var draftUse = draft.ResolutionStack.OfType<CardUseFrame>().Single();
        foreach (var canceled in new[] { 0, 1, 2 })
        {
            Activate(draft); Reach(draft, IsSheChoice); Answer(draft, c => c.Parameters["branch"] == "ineffective" && c.Targets.SequenceEqual([canceled]));
            if (canceled < 2) Reach(draft, p => IsOptional(p, She));
        }
        Need(draft.ResolutionStack.OfType<CardUseFrame>().Single().TargetSeats.Count == 4 &&
            draft.ResolutionStack.OfType<CardUseFrame>().Single().IneffectiveTargetSeats!.Count == 3,
            "Repeated actual source decisions preserve original target identities and exactly one effective draft participant.");
        draft = Cold(draft, draftRegistry); FinishUse(draft, draftUse.Id);
        Need(E<TargetedOrdinaryTrickOfferedEvent>(draft).Count(e => e.Receipt.CardUseFrameId == draftUse.Id) == 3 &&
            E<CardsRevealedEvent>(draft).Single(e => e.ResolutionId == draftUse.Id).Cards.Count == 4 &&
            E<HarvestCardSelectedEvent>(draft).Where(e => e.ResolutionId == draftUse.Id).Select(e => e.PlayerSeat).SequenceEqual([3]),
            "The last candidate cannot invalidate the final effect; native Five Grains reveals the original four cards and only the effective target picks.");
        _ = Cold(draft, draftRegistry);
    }

    public static void PaidEquipmentRecoveryAndNestedDamageDyingChildren()
    {
        var (lion, registry) = Start("fixture:target-hand-hp-lion", 5, "lion-recovery");
        var armor = V(lion, 0).Hand.First().Id; Accept(lion, new PlayCardCommand(0, armor, [], lion.Revision, P(lion)!.PromptId)); Play(lion);
        Use(lion, "hurt"); Play(lion); var beforeHp = V(lion, 0).Hp;
        End(lion); Reach(lion, p => IsOptional(p, Bing)); Activate(lion); ReachStage(lion, UnequalHandHpStage.Target);
        Answer(lion, c => c.Targets.SequenceEqual([1])); ReachStage(lion, UnequalHandHpStage.Action); Answer(lion, c => c.Parameters["branch"] == "draw");
        ReachStage(lion, UnequalHandHpStage.Gift); Answer(lion, c => c.Cards.SequenceEqual([armor])); Reach(lion, p => p.SkillPrompt?.SkillId == Watch);
        Need(V(lion, 0).Hp == beforeHp + 1 && lion.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.UnequalHandHp is not null)
            .UnequalHandHp is { Stage: UnequalHandHpStage.GiftChildren, Payments.Count: 3 }, "An actual equipped Lion gift pays once and pauses in its exact recovery child.");
        lion = Cold(lion, registry); Continue(lion); DrainEnding(lion);
        Need(lion.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Hand(1)) == 1 &&
            E<UnequalHandHpCompletedEvent>(lion).Count() == 1, "Paid equipment loss recovery returns to the gift receipt without repeating either effect."); _ = Cold(lion, registry);

        foreach (var child in new[] { "damage", "fatal" })
        {
            var (g, r) = Start(child == "fatal" ? "standard:peach" : "standard:dodge", 5, child);
            if (child == "fatal") { Use(g, "trim", V(g, 0).Hand.Take(V(g, 0).HandCount - V(g, 0).Hp + 1).Select(c => c.Id).ToArray()); Play(g); }
            End(g); Reach(g, p => IsOptional(p, Bing)); Activate(g); ReachStage(g, UnequalHandHpStage.Target);
            Answer(g, c => c.Targets.SequenceEqual([child == "fatal" ? 0 : 1])); ReachStage(g, UnequalHandHpStage.Action); Answer(g, c => c.Parameters["branch"] == "draw");
            Reach(g, p => p.SkillPrompt?.SkillId == Watch); g = Cold(g, r); Continue(g);
            if (child == "fatal") { Reach(g, p => p.Kind == DecisionKind.RescueDying); g = Cold(g, r); }
            DrainEnding(g);
            Need(E<UnequalHandHpCompletedEvent>(g).Count() == 1 && !E<UnequalHandHpMovementIssuedEvent>(g).Any(e => e.Payment.Step == "reward") &&
                (child == "fatal" ? !V(g, 0).IsAlive : V(g, 1).Hp == 4), "Real damage or dying children finish before equality and never issue an unqualified reward."); _ = Cold(g, r);
        }
    }

    public static void NativeAiActivatesPublicEndingAndTargetInterventions()
    {
        var (g, registry) = Start("standard:arrow_barrage", 5, aiDong: true);
        var arrow = V(g, 0).Hand.First().Id; Accept(g, new PlayCardCommand(0, arrow, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 1 && IsOptional(p, She));
        var initial = E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == She);
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => p.PlayerSeat == 1 && IsSheChoice(p));
        Need(E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == She) == initial + 1,
            "Native AI prices the public harmful card effect and activates the target intervention.");
        Accept(g, new AdvanceOneStepCommand(g.Revision)); FinishUse(g, g.ResolutionStack.OfType<CardUseFrame>().Single().Id); _ = Cold(g, registry);
        var (ending, endingRegistry) = Start("standard:dodge", 5, aiDong: true); End(ending);
        Reach(ending, p => p.PlayerSeat == 1 && IsOptional(p, Bing));
        Accept(ending, new AdvanceOneStepCommand(ending.Revision));
        Need(E<ProgramBindingStartedEvent>(ending).Any(e => e.SkillId == Bing && e.OwnerSeat == 1) &&
            !ending.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.ActorSeat != 0),
            "Native AI activates a positive public hand/HP opportunity before choosing its actual participant.");
        _ = Cold(ending, endingRegistry);
    }

    private static string Embedded(string suffix)
    {
        using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-dong-yun.{suffix}")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static string Reason(string step) => $"skill-program.{Bing}.unequal-hand-hp.{step}";
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool IsOptional(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool IsSheChoice(PendingDecision p) => p.SkillPrompt?.SkillId == She && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "targeted-ordinary-trick");
    private static void ReachStage(GameEngine g, UnequalHandHpStage stage) => Reach(g, p => g.ResolutionStack.LastOrDefault() is ProgramSkillFrame { UnequalHandHp: { } r } && r.Stage == stage);
    private static void Accept(GameEngine g, GameCommand command)
    {
        var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Actual target/hand-HP command failed.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 140; step++)
        {
            var p = P(g); if (p is not null && done(p)) return;
            if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying })
            { Answer(g, c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die"); continue; }
            if (p is { PlayerSeat: 0 } && IsOptional(p, She))
            { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
            if (p is { PlayerSeat: 0 }) throw new InvalidOperationException($"Unexpected native target/hand-HP prompt {p.Kind}: {p.Prompt}");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed native target/hand-HP fixture did not reach its bounded boundary.");
    }
    private static void FinishUse(GameEngine g, long use)
    {
        for (var step = 0; step < 180 && g.ResolutionStack.Any(f => f.Id == use); step++)
        {
            if (P(g) is { } optional && IsOptional(optional, She))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (P(g) is { PlayerSeat: 0 } p)
            {
                if (p.Kind is DecisionKind.RescueDying or DecisionKind.Nullification) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass");
                else if (p.SkillPrompt?.SkillId == She && IsOptional(p, She)) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else Answer(g, _ => true);
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Need(!g.ResolutionStack.Any(f => f.Id == use), "All actual trick target children return once before whole-use completion."); Play(g);
    }
    private static void DrainEnding(GameEngine g)
    {
        for (var step = 0; step < 100 && g.ResolutionStack.Any(f => f is ProgramSkillFrame { UnequalHandHp: not null }); step++)
        {
            if (P(g) is { } p) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "decline" || c.Parameters.GetValueOrDefault("option-id") == "continue" ||
                c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Need(!g.ResolutionStack.Any(f => f is ProgramSkillFrame { UnequalHandHp: not null }), "The finite ending operation returns to its actual phase exactly once.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves exact receipts, paid entities, private choices and typed parent returns."); return copy; }
    private static void Private(GameEngine g, int viewer) => Need(P(g) is { IsPrivate: true } && Enumerable.Range(0, 4).Where(s => s != viewer)
        .All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[viewer].Hand.Count == 0), "Private hand selection is only exposed to its legitimate chooser.");
    private static void RejectWrongActor(GameEngine g)
    { var p = P(g)!; var before = State(g); Need(!g.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices.First().Id, g.Revision)).Accepted && State(g) == before, "Wrong-actor input cannot change the exact paid prefix."); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    { Need(values is ICollection<T> { IsReadOnly: true }, "A projected nested collection is immutable."); }
    private static void Need(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(string card, int peerHp, string child = "none", bool aiDong = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(card, peerHp, child, aiDong));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string card, int peerHp, string child, bool aiDong) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("targeted-trick-hand-hp-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var production = SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json"));
            foreach (var (id, program) in production.Programs) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = program, ProgramPresentation = production.Presentations[id] });
            var watchEffects = new JsonArray(JsonNode.Parse("""{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}"""));
            if (child == "change-hand") watchEffects.Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""));
            if (child == "change-hp") watchEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":1}"""));
            if (child == "damage") watchEffects.Add(JsonNode.Parse("""{"op":"damage","target":"owner","amount":1}"""));
            if (child == "fatal") watchEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":20}"""));
            if (child == "lose-source") watchEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":4}"""));
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"trim","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"adjustment","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{Reason("adjust-draw")}}}"],"optional":false,"effects":[]}]}
            ]}
            """)!;
            rules["skills"]![1]!["triggers"]![0]!["effects"] = watchEffects;
            if (child == "lion-recovery") rules["skills"]![1]!["triggers"] = JsonNode.Parse("""[{"id":"lion-return","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]""");
            if (child == "lose-source")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,
                  "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true},
                    {"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Bing}}"],"sourceBind":"standard:none"}]}]}
                """));
            }
            var presentations = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实准备", description = "真实弃置实体" },
                [Watch] = new { name = "真实获得子窗", description = "先结清获得后判断手牌与HP", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            if (child == "lose-source")
            {
                presentations[Loss] = new { name = "真实濒死源失去", description = "合法来源替换", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            var fixture = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = fixture.Presentations[id] });
            var ownerSkills = aiDong ? new List<string>() : new List<string> { Driver };
            ownerSkills.Add(She);
            if (child is "lose-source" or "lion-recovery" or "fatal") ownerSkills.Add(Watch);
            if (child == "lose-source") ownerSkills.Add(Loss);
            b.AddGeneral(new(Owner, "实际董允技能拥有者", "supporter", aiDong ? Driver : Bing, "shu", aiDong ? 10 : 3, ownerSkills, GeneralGender.Male));
            const string peerSkill = "fixture:target-hand-hp-selection";
            b.AddSkill(new(peerSkill, "唯一首个AI候选", "固定角色选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:target-hand-hp-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实其他角色", "supporter", peerSkill, "wei", peerHp,
                [She, .. (aiDong ? new[] { Bing } : []), .. (child != "none" ? new[] { Watch } : [])]));
            if (card == "fixture:target-hand-hp-borrowed") b.AddCard(new(card, "借刀杀人", "普通锦囊", "真实pair", CardKind.BorrowedSword));
            if (card == "fixture:target-hand-hp-lion") b.AddCard(new(card, "白银狮子", "装备牌", "真实装备失去回复", CardKind.SilverLion));
            b.AddDeck(new("fixture:target-hand-hp-deck", "固定真实实体牌堆", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(card == "fixture:target-hand-hp-borrowed" && i % 6 == 0 ? "standard:crossbow" : card, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实目标与手牌动态结算", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:target-hand-hp-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
