using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryWangYiChecks
{
    private const string Zhen = "boundary:zhenlie-current", Miji = "boundary:miji-current";
    private const string Driver = "fixture:wy-driver", Entry = "fixture:wy-entry", Hp = "fixture:wy-hp", Loss = "fixture:wy-loss";
    private const string SourceSuppression = "fixture:wy-source-suppression";
    private const string Mode = "identity:classic-wy-fixture";

    public static void PaidOtherUseNullificationOpaqueObtainAndActualSlash()
    {
        var (g, r) = Create(); Play(g); var before = g.State.Players[0].Hp;
        Use(g, "enemy-duel", [1]); Reach(g, p => Activation(p, Zhen)); Reject(g); g = Restore(g, r);
        Activate(g, Zhen); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Zhen);
        var paid = root.PaidOwnTarget!;
        Require(paid.Use is { ActorSeat: 1, ProviderSeat: 1, TargetSeat: 0, EffectiveKind: CardKind.Duel, ActionId: not null } &&
            paid.ActualLost == 1 && !paid.Applied && g.State.Players[0].Hp == before - 1 &&
            E<PaidOwnTargetAppliedEvent>(g).Count() == 0,
            "The actual selected actor's zero-entity ordinary Duel freezes actor/provider/target before paying one real HP; nullification waits for the HP child.");
        g = Restore(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Zhen && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "obtain"));
        Require(E<PaidOwnTargetAppliedEvent>(g).Single().CardUseFrameId == paid.Use.CardUseFrameId &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == root.Id) == 1,
            "The restored HP child applies to the same use once and never repays the cost.");
        Option(g, "obtain"); Reach(g, p => p.SkillPrompt?.SkillId == Zhen && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        var prompt = P(g)!;
        Require(prompt.IsPrivate && prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand").All(c => c.Cards.Count == 0) &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "Choosing the original actor's hidden HE hand uses opaque current slots and exposes no identity in other views.");
        Reject(g); g = Restore(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Play(g);
        var obtainReason = $"skill-program.{Zhen}.{SkillProgramEffectOp.SelectAndMoveOwnedCard}";
        var claim = g.CardMovements.Last(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == obtainReason);
        Require(g.CardMovements.Count(m => m.CardId == claim.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) == 1 &&
            E<PaidOwnTargetAppliedEvent>(g).Count() == 1 && g.State.Players[0].Hp == before - 1 &&
            E<EarnedActualEndingBenefitIssuedEvent>(g).Count() == 0,
            "The original actor's entity is obtained once; the actual Duel completes without damage and without issuing the unchosen Ending promise.");
        g = Restore(g, r);

        var (slash, sr) = Create("standard:slash", observers: false); Play(slash);
        Accept(slash, new EndPlayPhaseCommand(0, slash.Revision, P(slash)!.PromptId));
        Reach(slash, p => Activation(p, Zhen));
        var window = slash.ResolutionStack.OfType<ActualUseTargetWindowFrame>().Single();
        var actual = slash.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == window.ParentFrameId);
        Require(actual.Action is { Type: CardActionType.Use, PhysicalCards.Count: 1 } && actual.SourceSeat != 0 &&
            actual.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash,
            "A native opponent's true paid Slash reaches the exact target window before Dodge or damage.");
        var hp = slash.State.Players[0].Hp; slash = Restore(slash, sr); Activate(slash, Zhen); Reach(slash, p => p.SkillPrompt?.SkillId == Zhen);
        Option(slash, "skip"); Until(slash, () => E<ProgramBindingResolvedEvent>(slash).Any(e => e.SkillId == Zhen && e.Completed));
        Require(slash.State.Players[0].Hp == hp - 1 && actual.Action.PhysicalCards.All(c =>
            slash.CardMovements.Count(m => m.CardId == c.CardId && m.To == CardLocation.Processing) == 1) &&
            E<PaidOwnTargetAppliedEvent>(slash).Single().CardUseFrameId == actual.Id,
            "The same native physical Slash is paid once and suppressed only for the actual protected target.");
        slash = Restore(slash, sr);

        var (legacy, legacyRegistry) = CreateLegacySource();
        ReachLegacyHuman(legacy, p => Activation(p, "boundary:shensu"));
        Require(legacy.CreateSnapshot(0).Players[0] is { Role: Role.Rebel, GeneralId: "fixture:wy-owner" } &&
            legacy.CreateSnapshot(1).Players[0].Role is null &&
            legacy.CreateSnapshot(1).Players[1] is { Role: Role.Lord, GeneralId: "fixture:wy-other-1" } &&
            E<GeneralSelectedEvent>(legacy).Count(e => e.ActorSeat == 0 && e.GeneralId == "fixture:wy-owner") == 1 &&
            E<GeneralSelectedEvent>(legacy).Count(e => e.ActorSeat == 1 && e.GeneralId == "fixture:wy-other-1") == 1,
            "The Lord first selects the exact published Shensu producer, then the first Rebel AI selects the exact Zhenlie target through public role weights.");
        var legacyHp = legacy.State.Players[0].Hp;
        Activate(legacy, "boundary:shensu");
        ReachLegacyHuman(legacy, p => p.SkillPrompt?.SkillId == "boundary:shensu" && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
        Answer(legacy, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0]));
        for (var i = 0; i < 40 && !E<PaidOwnTargetAppliedEvent>(legacy).Any(); i++)
            Accept(legacy, new AdvanceOneStepCommand(legacy.Revision));
        var legacyPaid = E<PaidOwnTargetHpEvent>(legacy).Single();
        Require(legacyPaid.Receipt.Use is { ActionId: null, LegacyProducerProgramId: not null, ActorSeat: 1, ProviderSeat: 1, TargetSeat: 0, EffectiveKind: CardKind.Slash } &&
            legacy.State.Players[0].Hp == legacyHp - 1 && E<PaidOwnTargetAppliedEvent>(legacy).Single().ActionId is null,
            "The registered legacy Shensu's real zero-entity Slash is protected using its exact original typed producer; it is not given a new Action or physical payment.");
        legacy = Restore(legacy, legacyRegistry);
        for (var i = 0; i < 40 && !E<ProgramBindingResolvedEvent>(legacy).Any(e => e.SkillId == Zhen && e.Completed); i++)
            Accept(legacy, new AdvanceOneStepCommand(legacy.Revision));
        Require(E<ProgramBindingResolvedEvent>(legacy).Any(e => e.SkillId == Zhen && e.Completed) &&
            E<ProgramSkillHpLostEvent>(legacy).Count(e => e.FrameId == legacyPaid.ProgramFrameId) == 1 &&
            !legacy.CardMovements.Any(m => m.CardId == 0),
            "The restored native protected target finishes one paid cost and the original legacy program without inventing a zero-ID entity.");
    }

    public static void PaidDyingRescueSourceLossAndDeclineResumeOriginalUse()
    {
        var (g, r) = Create(); Play(g); Use(g, "hurt-three"); Play(g);
        Require(g.State.Players[0].Hp == 1, "The fixture reaches one HP through a genuine previous command, without editing runtime state.");
        Use(g, "enemy-duel", [1]); Reach(g, p => Activation(p, Zhen)); Activate(g, Zhen); Reach(g, p => p.SkillPrompt?.SkillId == Entry);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Zhen);
        Require(g.ResolutionStack.OfType<DyingFrame>().Single().ParentFrameId == root.Id && root.PaidOwnTarget is { Applied: false, HpAfter: 0 } &&
            E<PaidOwnTargetAppliedEvent>(g).Count() == 0, "The genuine zero-HP cost owns its Dying-entry child before applying ineffectiveness.");
        g = Restore(g, r); Continue(g); Reach(g, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0);
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach"); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var rescue = E<CardActionAcceptedEvent>(g).Last(e => e.Action.EffectiveKind == CardKind.Peach).Action;
        g = Restore(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Zhen); Option(g, "skip"); Play(g);
        Require(g.State.Players[0].Hp == 1 && E<ProgramSkillHpLostEvent>(g).Count(e => e.FrameId == root.Id) == 1 &&
            E<PaidOwnTargetAppliedEvent>(g).Count(e => e.ProgramFrameId == root.Id) == 1 && rescue.PhysicalCards.Count == 1 &&
            rescue.PhysicalCards.All(c => g.CardMovements.Count(m => m.CardId == c.CardId && m.To == CardLocation.Processing) == 1),
            "JSON-restored Dying, physical Peach and recovery children return to one paid cost and apply ineffectiveness once.");
        g = Restore(g, r);

        var (loss, lr) = Create(sourceLoss: true); Play(loss); var hp = loss.State.Players[0].Hp;
        Use(loss, "enemy-duel", [1]); Reach(loss, p => Activation(p, Zhen)); Activate(loss, Zhen); Play(loss);
        Require(!loss.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Zhen) && loss.State.Players[0].Hp == hp - 1 &&
            E<PaidOwnTargetAppliedEvent>(loss).Count() == 1 && E<EarnedActualEndingBenefitIssuedEvent>(loss).Count() == 0 &&
            E<ProgramBindingResolvedEvent>(loss).Any(e => e.SkillId == Zhen && !e.Completed),
            "Actual HP observers suppress the original skill's qualification after payment; the same card remains ineffective and unpaid benefit choices are canceled.");
        Require(loss.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == SourceSuppression) &&
            E<SkillsAcquiredEvent>(loss).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Loss && e.SkillIds.Contains(SourceSuppression)) == 1,
            "The real paid-HP observer acquires one independent suppression source; this is qualification suppression, not physical grant removal.");
        loss = Restore(loss, lr);

        var (decline, dr) = Create("standard:slash", observers: false); Play(decline); hp = decline.State.Players[0].Hp;
        Use(decline, "enemy-duel", [1]); Reach(decline, p => Activation(p, Zhen));
        Answer(decline, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(decline, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 0);
        Require(decline.State.Players[0].Hp == hp && E<PaidOwnTargetHpEvent>(decline).Count() == 0 && E<PaidOwnTargetAppliedEvent>(decline).Count() == 0,
            "Declining an optional opportunity preserves HP and leaves the original ordinary trick's real response available.");
        decline = Restore(decline, dr);
    }

    public static void IndependentActualEndingPromisesFrozenXAndOptionalHeGifts()
    {
        var (g, r) = Create(observers: false); Play(g);
        for (var i = 0; i < 2; i++)
        {
            Use(g, "enemy-duel", [1]); Reach(g, p => Activation(p, Zhen)); Activate(g, Zhen);
            Reach(g, p => p.SkillPrompt?.SkillId == Zhen); Option(g, "ending"); Play(g);
        }
        var promises = E<EarnedActualEndingBenefitIssuedEvent>(g).Select(e => e.Benefit).ToArray();
        Require(promises.Length == 2 && promises.Select(b => b.Id).Distinct().Count() == 2 && promises.All(b =>
            b.ActualTurnNumber == 1 && b.ActualTurnOwnerSeat == 0 && b.BenefitSource.SkillId == Miji),
            "Two independently paid activations issue two distinct exact-instance promises in the original actual turn.");
        g = Restore(g, r); Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, Gift); var first = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Miji);
        Require(first.LostHpOwnedGift is { MaximumGiftCount: 2, ActualDrawCount: 2, PaidCardIds.Count: 0 } &&
            E<EarnedActualEndingBenefitConsumedEvent>(g).Count(e => e.Applied) == 1,
            "The exact earned Ending item is consumed once; lost HP freezes X before its actual draw.");
        PrivateGift(g); Reject(g); g = Restore(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "limited-owned-gift" && c.Targets.SequenceEqual([1]));
        Reach(g, Gift); Require(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-limited-owned-gift"),
            "Giving one does not require filling X; stopping remains legal after the first real payment.");
        g = Restore(g, r); StopGift(g); Reach(g, Gift); StopGift(g); Reach(g, p => Activation(p, Miji)); Activate(g, Miji); Reach(g, Gift); StopGift(g);
        Until(g, () => E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Miji && e.Completed) == 3);
        var given = E<LostHpOwnedCardGivenEvent>(g).Single();
        Require(E<EarnedActualEndingBenefitConsumedEvent>(g).Count(e => e.Applied) == 2 &&
            E<LostHpDrawGiftFrozenEvent>(g).Count() == 3 && E<LostHpDrawGiftFrozenEvent>(g).All(e => e.MaximumGiftCount == 2) &&
            g.CardMovements.Count(m => m.CardId == given.CardId && m.From == given.From && m.To == CardLocation.Processing && m.Reason.Value == "program.lost-hp-owned-gift.give") == 1 &&
            g.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == given.CardId),
            "Two earned occurrences and natural optional Miji are independent; a restored gift moves the original owned entity once and never redraws.");
        g = Restore(g, r);

        var (empty, er) = Create(observers: false, shortDeck: true); Play(empty); Use(empty, "hurt-one"); Play(empty); Use(empty, "drain"); Play(empty);
        Accept(empty, new EndPlayPhaseCommand(0, empty.Revision, P(empty)!.PromptId)); Reach(empty, p => Activation(p, Miji)); Activate(empty, Miji); Reach(empty, Gift);
        var frozen = E<LostHpDrawGiftFrozenEvent>(empty).Single();
        Require(frozen.MaximumGiftCount == 1 && frozen.ActualDrawCount == 0 && P(empty)!.Choices.Any(c => c.Cards.Count == 1),
            "An empty real deck freezes zero actual draw but the independently frozen X still permits an existing owned entity under the stated engineering default.");
        empty = Restore(empty, er); Answer(empty, c => c.Parameters.GetValueOrDefault("program-action") == "limited-owned-gift");
        Until(empty, () => E<ProgramBindingResolvedEvent>(empty).Any(e => e.SkillId == Miji && e.Completed)); empty = Restore(empty, er);
    }

    public static void LostHpGiftMovementDyingNativeAndStrictContracts()
    {
        var (g, r) = Create(gainDying: true, observers: false); Play(g); Use(g, "hurt-one"); Play(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => Activation(p, Miji)); Activate(g, Miji); Reach(g, Gift);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "limited-owned-gift" && c.Targets.SequenceEqual([1]));
        Reach(g, p => p.SkillPrompt?.SkillId == Entry);
        var given = E<LostHpOwnedCardGivenEvent>(g).Single();
        Require(g.ResolutionStack.OfType<DyingFrame>().Single().VictimSeat == 1 && given.PaidCount == 1 && given.DeliveredCount == 1,
            "A real recipient gain observer owns a Dying child after one actual HE transfer; it does not move or pay the entity again.");
        g = Restore(g, r); Continue(g);
        Until(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Miji && e.Completed));
        Require(E<LostHpOwnedCardGivenEvent>(g).Count() == 1 && E<LostHpDrawGiftFrozenEvent>(g).Count() == 1 &&
            g.CardMovements.Count(m => m.CardId == given.CardId && m.From == given.From && m.To == CardLocation.Processing) == 1,
            "JSON-restored gain/Dying/rescue children return to the exact Ending program without duplicate draws or gift payments."); g = Restore(g, r);

        var (native, nr) = Create(native: true, observers: false);
        for (var i = 0; i < 100 && native.State.Status != EngineStatus.Completed &&
            !(E<PaidOwnTargetAppliedEvent>(native).Any() && E<LostHpDrawGiftFrozenEvent>(native).Any()); i++) Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(E<PaidOwnTargetAppliedEvent>(native).Any() && E<LostHpDrawGiftFrozenEvent>(native).Any() &&
            native.AcceptedCommands.All(c => c is not AnswerPromptCommand),
            "The fixed native source really pays/nullifies and draws via Start/Advance, using public target appearance and own gift cards only."); native = Restore(native, nr);
    }

    public static void SharedActualTargetLoaderContracts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Read(string suffix) { using var s = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("boundary-wang-yi." + suffix)))!; using var reader = new StreamReader(s); return reader.ReadToEnd(); }
        var rules = Read("rules.json"); var presentation = Read("presentation.json");
        const string sharedWindowSkill = "fixture:shared-actual-target-window";
        var sharedWindowRules = $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{sharedWindowSkill}}","revision":1,
          "triggers":[{"id":"shared-window","window":"otherActualUseTargeted","subject":"owner","optional":true,
            "effects":[{"op":"offerShortRangeSlashTarget","target":"owner"}]}]}]}
        """;
        var sharedWindowPresentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [sharedWindowSkill] = new { name = "共享实际目标窗口", description = "共享机制 loader 回归" } } });
        var sharedWindow = SkillProgramCatalog.Load(sharedWindowRules, sharedWindowPresentation);
        Require(sharedWindow.Programs[sharedWindowSkill].Triggers.Single().Effects is [{ Op: SkillProgramEffectOp.OfferShortRangeSlashTarget }],
            "An unrelated actual-target operation keeps its own contract when sharing the paid-target trigger window.");
        var incompletePaidRejected = false;
        try { SkillProgramCatalog.Load(sharedWindowRules.Replace("offerShortRangeSlashTarget", "payHpThenNullifyOwnActualUseTarget"), sharedWindowPresentation); }
        catch (InvalidOperationException) { incompletePaidRejected = true; }
        Require(incompletePaidRejected, "Restricting validation to paid-target operations still rejects a paid cost without its exact four-step successor contract.");
        foreach (var invalid in new[] { rules.Replace("\"otherActualUseTargeted\"", "\"cardUseBeforeTargetEffects\""),
            rules.Replace("\"zones\": [\"hand\", \"equipment\"]", "\"zones\": [\"hand\", \"judgment\"]"),
            rules.Replace("\"earnedActualEnding\"", "\"otherLiving\"") })
        { var rejected = false; try { SkillProgramCatalog.Load(invalid, presentation); } catch (InvalidOperationException) { rejected = true; }
            Require(invalid != rules && rejected, "New paid-target and earned Ending contracts reject wrong windows, HE regions and unbacked foreign endings without changing classic rules."); }
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Gift(PendingDecision p) => p.SkillPrompt?.SkillId == Miji && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "limited-owned-gift");
    private static void PrivateGift(GameEngine g) { Require(P(g)!.IsPrivate && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null), "Only the owner receives own HE identities and private gift choices."); }
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Activate(GameEngine g, string skill) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
    private static void Option(GameEngine g, string option) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void Continue(GameEngine g) => Option(g, "continue");
    private static void StopGift(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-limited-owned-gift");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; var c = p.Choices.First(predicate); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var i = 0; i < 240; i++) { if (P(g) is { } p && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed actual command fixture did not reach its expected boundary."); }
    private static void ReachLegacyHuman(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 160; i++)
        {
            if (P(g) is { PlayerSeat: 1 } p)
            {
                if (predicate(p)) return;
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass"); continue; }
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The real human legacy producer did not reach its expected boundary.");
    }
    private static void Until(GameEngine g, Func<bool> predicate) { for (var i = 0; i < 240; i++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed actual command fixture did not finish its typed return."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p?.SkillPrompt?.SkillId is Hp or Entry) Continue(g);
        else if (p is not null && (Activation(p, Zhen) || Activation(p, Miji))) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Restore(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(cold), "Four private views, exact original use, paid HP, gifts and scalar earned Ending promises survive real JSON restore."); return cold; }
    private static void Reject(GameEngine g) { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("not-published"), g.Revision)).Accepted && before == State(g), "Rejected choices do not advance cost, promise, gift or any private view."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(string card = "standard:peach", bool observers = true, bool sourceLoss = false, bool gainDying = false, bool native = false, bool shortDeck = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(card, observers, sourceLoss, gainDying, native, shortDeck));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); if (!native) { Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(g, new SelectGeneralCommand(0, "fixture:wy-owner", g.Revision, P(g)!.PromptId)); }
        if (sourceLoss)
        {
            Play(g);
            Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Zhen),
                "The source-loss fixture initially has no printed Zhenlie grant.");
            Use(g, "grant-zhenlie"); Play(g);
            var owner = g.CreateSnapshot(0).Players[0];
            Require(owner.Skills!.Any(s => s.Id == Zhen) && owner.SkillRuntimeStates!.Single(s => s.SkillId == Zhen).IsAcquired &&
                E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.Contains(Zhen)) == 1,
                "One actual activation independently grants Zhenlie before its irreversible HP payment and later qualification suppression.");
        }
        return (g, r);
    }
    private static (GameEngine, ContentRegistry) CreateLegacySource()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture("standard:slash", false, false, false, true, false, legacySource: true));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = 1, HumanRole = Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 1);
        Require(P(g)!.ValidContentIds.Contains("fixture:wy-other-1") && !E<GeneralSelectedEvent>(g).Any(),
            "The exact registered Shensu general is published to the human Lord before any AI consumes the shared candidate pool.");
        Accept(g, new SelectGeneralCommand(1, "fixture:wy-other-1", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture(string card, bool observers, bool sourceLoss, bool gainDying, bool native, bool shortDeck, bool legacySource = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-wang-yi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
            {"id":"enemy-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
            {"id":"hurt-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":3}]},
            {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
            {"id":"drain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]},
            {"id":"{{Hp}}","revision":1,"triggers":[{"id":"hp-child","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[{"id":"entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
            {"id":"{{Loss}}","revision":1,"triggers":[{"id":"loss","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{SourceSuppression}}"]}]}]},
            {"id":"fixture:wy-gain","revision":1,"triggers":[{"id":"gift-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.lost-hp-owned-gift.give"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":6}]}]}]}
            """;
            if (sourceLoss)
            {
                var sourceRules = JsonNode.Parse(rules)!;
                ((JsonArray)sourceRules["skills"]![0]!["activations"]!).Add(JsonNode.Parse("""{"id":"grant-zhenlie","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:zhenlie-current"]}]}"""));
                rules = sourceRules.ToJsonString();
            }
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实能力驱动", description = "实际对方决斗及体力状态" },
                [Hp] = new { name = "HP真实子窗", description = "确切体力变化", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Entry] = new { name = "真实濒死入口", description = "实付后的 typed Dying", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Loss] = new { name = "真实来源资格抑制", description = "已付后的技能资格失效" }, ["fixture:wy-gain"] = new { name = "真实收牌濒死", description = "一次真实收牌" } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in c.Programs.Keys) b.AddSkill(new(id, id, "正式通用能力") { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            b.AddSkill(new(SourceSuppression, "已付来源资格抑制", "真实Lord由4HP支付至3HP时抑制其他武将技能") { SuppressionRule = new(3), Tags = SkillTag.Locked });
            var ownerRole = legacySource ? Role.Rebel : Role.Lord;
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:wy-pick-owner" : "fixture:wy-pick-other", "固定公开选将", "正式角色权重")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == ownerRole) == owner ? 10000d : -10000d) });
            var extras = new List<string> { Miji }; if (!sourceLoss) extras.Add("fixture:wy-pick-owner"); if (!native) extras.Add(Driver); if (observers) extras.Add(Hp); if (observers || gainDying) extras.Add(Entry); if (sourceLoss) extras.Add(Loss);
            b.AddGeneral(new("fixture:wy-owner", "真实界王异来源", "supporter", sourceLoss ? "fixture:wy-pick-owner" : Zhen, "wei", 3, extras));
            for (var i = 1; i < 4; i++)
            {
                var otherSkills = new List<string>();
                if (i == 1 && gainDying) otherSkills.AddRange(["fixture:wy-gain", Entry]);
                if (i == 1 && legacySource) otherSkills.Add("boundary:shensu");
                b.AddGeneral(new($"fixture:wy-other-{i}", "其他角色", "supporter", "fixture:wy-pick-other", "shu", 6, otherSkills));
            }
            b.AddDeck(new("fixture:wy-deck", "固定真实实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, shortDeck ? 20 : 64)
                .Select(i => new ContentDeckPhysicalCard(native ? i % 4 == 0 ? "standard:peach" : "standard:slash" : card, Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实贞烈秘计", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:wy-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wy-owner", "fixture:wy-other-1", "fixture:wy-other-2", "fixture:wy-other-3"]));
        }
    }
}
