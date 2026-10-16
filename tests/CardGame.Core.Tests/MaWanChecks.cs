using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaWanChecks
{
    private const string Hunjiang = "ol:hunjiang", Driver = "fixture:mw-driver", Watch = "fixture:mw-draw", Quiet = "fixture:mw-quiet";
    private const string Shield = "fixture:mw-shield", Redirect = "fixture:mw-redirect", Source = "fixture:mw-hp-source", LateShield = "fixture:mw-late-shield", Owner = "fixture:mw-owner", Mode = "identity:classic-ma-wan-fixture";

    public static void SimultaneousPrivateCommitmentsUnanimousSupplementActualDrawAndCold()
    {
        foreach (var scenario in new[] { "normal", "draw", "mixed", "loss" })
        {
            var (g, registry) = Start(scenario);
            if (scenario == "loss") Need(V(g, 0).Hp == 9 && E<HpConditionalSkillGrantChangedEvent>(g).Any(e => e.OwnerSeat == 0 && e.SkillId == Hunjiang && e.Added),
                "The loss fixture begins with a genuine HP-nine conditional Hunjiang source, rather than a printed source or a fake replacement.");
            if (scenario is "draw" or "mixed") { Use(g, "hurt", [1]); Play(g); }
            if (scenario == "draw") { Use(g, "hurt", [2]); Play(g); }
            var participants = scenario == "normal" ? new[] { 1, 2, 3 } : new[] { 1, 2 };
            var before = V(g, 0).HandCount;
            Accept(g, new UseProgramSkillCommand(0, Hunjiang, "simultaneous-range-choices", [], participants, g.Revision, P(g)!.PromptId));
            Reach(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MaWanHunjiang?.Stage == MaWanHunjiangStage.PrivateVotes));
            var id = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MaWanHunjiang is not null).Id;
            for (var vote = 0; vote < participants.Length; vote++)
            {
                var expected = vote;
                Reach(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == id && f.MaWanHunjiang?.Votes.Count == expected) && P(g)?.PlayerSeat == participants[expected]);
                var chooser = participants[vote];
                Need(P(g) is { IsPrivate: true } && P(g)!.Choices.Count == 2 &&
                    Enumerable.Range(0, 4).Where(s => s != chooser).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
                    !E<MaWanHunjiangVoteRevealedEvent>(g).Any() && !E<MaWanHunjiangEntitledEvent>(g).Any() && V(g, 0).HandCount == before,
                    "Every frozen participant sees only their own two choices; previous private votes produce no public result, grant or early draw.");
                g = Cold(g, registry); Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var expectedDraw = scenario == "mixed" ? 1 : participants.Length;
            var grants = E<MaWanHunjiangEntitledEvent>(g).Where(e => e.FrameId == id).Select(e => e.TargetSeat).ToArray();
            Need(V(g, 0).HandCount == before + expectedDraw && E<MaWanHunjiangDrawIssuedEvent>(g).Single().Count == expectedDraw &&
                E<MaWanHunjiangVoteRevealedEvent>(g).Count() == participants.Length &&
                E<MaWanHunjiangVoteRevealedEvent>(g).All(e => e.Unanimous == (scenario != "mixed")) &&
                grants.SequenceEqual(scenario == "mixed" ? new[] { 2 } : participants) &&
                g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.ma-wan.hunjiang.draw") == expectedDraw,
                "All real commitments reveal together: a mixed vote applies each selected option, while unanimous votes execute both options for every participant and draw actual entities.");
            g = Cold(g, registry); Continue(g); Play(g);
            Need(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Hunjiang), "The real accepted activation consumes exactly this Play phase's single use.");
            Need(V(g, 0).Skills!.Any(s => s.Id == Hunjiang) == (scenario != "loss"), "The paid draw child really loses Hunjiang in the loss fixture, then returns exactly once.");
            if (scenario == "loss")
            {
                var issuedGrant = E<HpConditionalSkillGrantChangedEvent>(g).Single(e => e.OwnerSeat == 0 && e.SkillId == Hunjiang && e.Added);
                Need(V(g, 0).Hp == 8 && E<HpConditionalSkillGrantChangedEvent>(g).Count(e => !e.Added && e.GrantId == issuedGrant.GrantId &&
                    e.SkillInstanceId == issuedGrant.SkillInstanceId && e.Source == issuedGrant.Source) == 1,
                    "The actual paid draw child's native HP loss really revokes its one conditional skill grant before the later use. " +
                    JsonSerializer.Serialize(new { Hp = V(g, 0).Hp, Grants = E<HpConditionalSkillGrantChangedEvent>(g), Loss = E<ProgramSkillHpLostEvent>(g), Paid = E<MaWanHunjiangDrawIssuedEvent>(g) }));
                Slash(g, 3); Reach(g, () => Branch(P(g), "append"));
                Need(P(g)!.Choices.Where(c => c.Targets.Count > 0).Select(c => c.Targets[0]).Order().SequenceEqual([1, 2]),
                    "The already-issued actual-phase entitlement publishes its exact native Slash candidate even after its source skill was lost before that use.");
                g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([1])); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Play(g);
                Need(E<MaWanHunjiangTargetAddedEvent>(g).Count() == 1, "A lost printed skill cannot cancel its issued target choice in the executor's pre-receipt guard.");
            }
            _ = Cold(g, registry);
        }
    }

    public static void NativeAndZeroEntitySlashMultipleTargetsResponsesProducerReturnAndPhaseExpiryCold()
    {
        var (g, registry) = Start("normal"); HunjiangOn(g, [1, 2]); var before = Enumerable.Range(0, 4).Select(s => V(g, s).Hp).ToArray();
        Slash(g, 3); Reach(g, () => Branch(P(g), "append"));
        Need(P(g)!.Choices.Where(c => c.Targets.Count > 0).Select(c => c.Targets[0]).SequenceEqual([1, 2]), "The native physical Slash offers every issued living legal extra participant.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([1]));
        Need(P(g)!.Choices.All(c => !c.Targets.Contains(1)), "An independently selected extra target cannot be chosen twice.");
        Answer(g, c => c.Targets.SequenceEqual([2])); g = Cold(g, registry);
        var physical = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.MaWanHunjiang is not null); var physicalUse = physical.Id;
        Need(physical is { PhysicalCardIds.Count: 1 } && physical.TargetSeats.SequenceEqual([3]) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MaWanHunjiang is not null).MaWanHunjiang!.SelectedTargets.SequenceEqual([1, 2]),
            "Both frozen extensions belong to the original physical native Slash and preserve its single material entity before final submission.");
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Need(E<MaWanHunjiangTargetAddedEvent>(g).Where(e => e.UseId == physicalUse).OrderBy(e => e.AddedIndex).Select(e => e.TargetSeat).SequenceEqual([1, 2]),
            "The actual native use publishes both appended targets exactly once even when an unanswered Slash completes synchronously.");
        g = Cold(g, registry); Play(g);
        Need(Enumerable.Range(1, 3).All(s => V(g, s).Hp == before[s] - 1) &&
            E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat is 1 or 2 or 3) == 3 &&
            g.CardMovements.Count(m => m.CardId == physical.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Native response and target cursors actually resolve each of the three physical Slash targets exactly once.");
        Use(g, "virtual", [3]); Reach(g, () => Branch(P(g), "append"));
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        var virtualFrame = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.MaWanHunjiang is not null); var virtualUse = virtualFrame.Id;
        Need(virtualFrame is { CardId: 0, PhysicalCardIds.Count: 0 } && virtualFrame.TargetSeats.SequenceEqual([3]) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MaWanHunjiang is not null).MaWanHunjiang!.SelectedTargets.SequenceEqual([2]),
            "A real zero-entity program Slash owns its frozen extension while retaining its original producer and primary target.");
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Need(E<MaWanHunjiangTargetAddedEvent>(g).Single(e => e.UseId == virtualUse).TargetSeat == 2,
            "The zero-entity native use really appends its selected target before returning to its producer.");
        g = Cold(g, registry); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Driver && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true);
        var producer = g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Need(producer.SkillId == Driver && producer.SelectedTargetSeats.SequenceEqual([3]) && V(g, 2).Hp == before[2] - 2 && V(g, 3).Hp == before[3] - 2,
            "The native zero-entity Slash resolves both responses and returns only its original primary to the paused producer, which really continues to its next instruction.");
        g = Cold(g, registry); Continue(g); Play(g);
        var added = E<MaWanHunjiangTargetAddedEvent>(g).Count(); var turn = g.State.TurnNumber; End(g);
        Reach(g, () => g.State.TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Hunjiang), "A later actual Play phase regains its own once-per-phase active use.");
        Slash(g, 3); Play(g);
        Need(E<MaWanHunjiangTargetAddedEvent>(g).Count() == added && !E<MaWanHunjiangUseIssuedEvent>(g).Any(e => e.ActualTurnNumber == g.State.TurnNumber),
            "The previous phase's entitlement cannot issue any later-use targets after its actual Play phase ended.");
        _ = Cold(g, registry);
    }

    public static void CurrentRangeDeathProhibitionAndAlreadyTargetAreRecheckedCold()
    {
        foreach (var scenario in new[] { "range", "shield", "dead" })
        {
            var (g, registry) = Start(scenario);
            if (scenario == "range") { Use(g, "equip", [0]); Play(g); Need(V(g, 0).Equipment.Any(c => c.Kind == CardKind.QinglongCrescentBlade), "The range fixture actually equips its native range-three weapon."); }
            HunjiangOn(g, [1, 2, 3]);
            if (scenario == "range") { Use(g, "strip"); Play(g); Need(!V(g, 0).Equipment.Any(), "Actual equipment movement removes the initial range source before the later Slash."); }
            if (scenario == "shield") Need(V(g, 2).Skills!.Any(s => s.Id == Shield), "The frozen participant really owns its native Slash target-prohibition policy.");
            if (scenario == "dead") { Use(g, "kill", [2]); Play(g); Need(!V(g, 2).IsAlive, "The originally selected participant really dies before later target selection."); }
            Slash(g, 1); Reach(g, () => Branch(P(g), "append"));
            Need(P(g)!.Choices.Where(c => c.Targets.Count > 0).Select(c => c.Targets[0]).SequenceEqual([3]),
                "The accepted original target is excluded; another frozen participant is filtered by current range after weapon loss, a real target-prohibition skill or actual death.");
            g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3])); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Play(g);
            Need(E<MaWanHunjiangTargetAddedEvent>(g).Single().TargetSeat == 3 && E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 3) == 1 &&
                !E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 2), "Only the current legal extra target enters the original native Slash resolution.");
            _ = Cold(g, registry);
        }
        var (redirectGame, redirectRegistry) = Start("redirect"); HunjiangOn(redirectGame, [1, 2, 3]);
        var redirectedHp = V(redirectGame, 2).Hp; var originalHp = V(redirectGame, 3).Hp;
        Slash(redirectGame, 1); Reach(redirectGame, () => Branch(P(redirectGame), "append"));
        Answer(redirectGame, c => c.Targets.SequenceEqual([3])); Answer(redirectGame, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Reach(redirectGame, () => E<MaWanHunjiangRedirectedEvent>(redirectGame).Any()); redirectGame = Cold(redirectGame, redirectRegistry); Play(redirectGame);
        Need(E<MaWanHunjiangRedirectedEvent>(redirectGame).Single() is { OriginalTargetSeat: 3, NewTargetSeat: 2 } &&
            V(redirectGame, 2).Hp == redirectedHp - 1 && V(redirectGame, 3).Hp == originalHp,
            "The actual added target can pay a native redirect: its new target resolves once while the issued extra-target tail and native cursor survive cold return.");
        _ = Cold(redirectGame, redirectRegistry);

        var (duplicateGame, duplicateRegistry) = Start("duplicate"); HunjiangOn(duplicateGame, [2, 3]);
        Need(!V(duplicateGame, 1).Skills!.Any(s => s.Id is Redirect or Shield) && V(duplicateGame, 1).Skills!.Any(s => s.Id == LateShield) &&
            V(duplicateGame, 2).Skills!.Any(s => s.Id == Redirect) && V(duplicateGame, 3).Skills!.Any(s => s.Id == Redirect) &&
            V(duplicateGame, 2).Hp == 1 && V(duplicateGame, 3).Hp == 2,
            "The original target has no early redirect or prohibition, while both later real targets own Redirect and the final victim starts at one HP. " + Diagnostic(duplicateGame));
        var aHp = V(duplicateGame, 1).Hp; var cHp = V(duplicateGame, 3).Hp;
        Slash(duplicateGame, 1); Reach(duplicateGame, () => Branch(P(duplicateGame), "append"));
        Answer(duplicateGame, c => c.Targets.SequenceEqual([2])); Answer(duplicateGame, c => c.Targets.SequenceEqual([3]));
        Answer(duplicateGame, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Reach(duplicateGame, () => E<MaWanHunjiangRedirectedEvent>(duplicateGame).Count() == 1 || P(duplicateGame) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(E<MaWanHunjiangRedirectedEvent>(duplicateGame).Count() == 1, "The second native target must actually issue its duplicate-producing redirect before the Slash finishes. " + Diagnostic(duplicateGame));
        var duplicateUse = duplicateGame.ResolutionStack.OfType<CardUseFrame>().Single(u => u.MaWanHunjiang is { AddedTargets.Count: 2 });
        Need(duplicateUse.TargetIndex == 1 && duplicateUse.TargetSeats.SequenceEqual([1, 3, 3]) && V(duplicateGame, 1).Hp == aHp - 1 &&
            V(duplicateGame, 1).Skills!.Any(s => s.Id == Shield) &&
            E<MaWanHunjiangRedirectedEvent>(duplicateGame).Single() is { TargetIndex: 1, OriginalTargetSeat: 2, NewTargetSeat: 3 },
            "After the original target really takes damage and gains its conditional prohibition, the second target's real redirect produces the same recipient in the second and third slots.");
        duplicateGame = Cold(duplicateGame, duplicateRegistry);
        Reach(duplicateGame, () => E<MaWanHunjiangRedirectedEvent>(duplicateGame).Count() == 2);
        duplicateUse = duplicateGame.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == duplicateUse.Id);
        Need(duplicateUse.TargetIndex == 2 && duplicateUse.TargetSeats.SequenceEqual([1, 3, 2]) &&
            E<MaWanHunjiangRedirectedEvent>(duplicateGame).Last() is { TargetIndex: 2, OriginalTargetSeat: 3, NewTargetSeat: 2 } &&
            V(duplicateGame, 3).Hp == cHp - 1 && V(duplicateGame, 2).Hp == 1,
            "The repeated recipient's third-cursor redirect changes only the unprocessed slot, preserving the already resolved second slot and its actual damage.");
        duplicateGame = Cold(duplicateGame, duplicateRegistry); Play(duplicateGame);
        Need(V(duplicateGame, 1).Hp == aHp - 1 && !V(duplicateGame, 2).IsAlive && V(duplicateGame, 3).Hp == cHp - 1 &&
            Enumerable.Range(1, 3).All(s => E<DamageAppliedEvent>(duplicateGame).Count(e => e.TargetSeat == s) == 1) &&
            E<CardUseFinishedEvent>(duplicateGame).Count(e => e.ResolutionId == duplicateUse.Id) == 1,
            "All three native slots resolve exactly once across repeated-recipient redirection, cold replay and the final one-HP recipient's real dying/death return.");
        _ = Cold(duplicateGame, duplicateRegistry);
    }

    public static void HumanRecipientPrivateAnswerAiSourceAndPaidOwnerDeathCold()
    {
        var (g, registry) = Start("human");
        Need(V(g, 1).Skills!.Any(s => s.Id == Hunjiang) && !V(g, 1).Skills!.Any(s => s.Id is Quiet or Driver), "The public fixture selection really placed the sole AI Hunjiang source at seat one without unrelated driver actions or a skipped Play.");
        Use(g, "hurt", [0]); Play(g); End(g); Reach(g, () => P(g) is { PlayerSeat: 0, IsPrivate: true } && Branch(P(g), "draw"));
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MaWanHunjiang is not null).MaWanHunjiang!;
        Need(receipt.Source.OwnerSeat == 1 && receipt.Participants.Contains(0) && g.CreateSnapshot(1).PendingDecision is null,
            "A naturally chosen real AI source asks the human participant privately; the skill owner cannot inspect that participant's pending answer.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw");
        Reach(g, () => E<MaWanHunjiangDrawIssuedEvent>(g).Any(e => e.OwnerSeat == 1));
        Need(E<MaWanHunjiangVoteRevealedEvent>(g).Any(e => e.Seat == 0 && !e.AllowExtraTarget), "The human's actual private AnswerPrompt command commits to the same simultaneous receipt and later public result.");
        _ = Cold(g, registry);

        (g, registry) = Start("death"); Accept(g, new UseProgramSkillCommand(0, Hunjiang, "simultaneous-range-choices", [], [1], g.Revision, P(g)!.PromptId));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); g = Cold(g, registry); Continue(g); Reach(g, () => !V(g, 0).IsAlive);
        Need(E<MaWanHunjiangDrawIssuedEvent>(g).Count() == 1 && E<MaWanHunjiangEntitledEvent>(g).Count() == 1 &&
            !E<MaWanHunjiangTargetAddedEvent>(g).Any(), "Death inside a real paid draw child does not repeat the settled draw or invent a dead owner's subsequent Slash choice.");
        _ = Cold(g, registry);
    }

    private static void HunjiangOn(GameEngine g, IReadOnlyList<int> targets)
    {
        Accept(g, new UseProgramSkillCommand(0, Hunjiang, "simultaneous-range-choices", [], targets, g.Revision, P(g)!.PromptId));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); Continue(g); Play(g);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 240; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Ma Wan completed before the required native boundary: " + Diagnostic(g));
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Ma Wan prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Ma Wan boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Pending = P(g),
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, Skills = V(g, s).Skills!.Select(x => x.Id) }),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Native = g.Events.Select(e => e.Payload).Where(e => e is ProgramBindingStartedEvent or ProgramBindingResolvedEvent or MaWanHunjiangTargetAddedEvent or MaWanHunjiangRedirectedEvent or DamageAppliedEvent).Select(e => e.ToString()),
        Tail = g.Events.TakeLast(8).Select(e => e.Payload.ToString()) });
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Ma Wan command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold Ma Wan preserves every viewer's private commitment boundary, issued actual-phase targets, native draw/payment/response children and original virtual producer return."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ma-wan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:ma-wan", "classic:ma-chao");
            var loss = scenario == "loss" ? ", {\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scenario == "death" ? ", {\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
            var redirectTail = scenario == "duplicate" ? ", {\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"redirect-after\",\"options\":[{\"id\":\"continue\"}]}" : "";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":5}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"strip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]},
                {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"virtual-return","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"paid-hunjiang-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ma-wan.hunjiang.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]} {{{loss}}} ]}]},
              {"id":"{{{Shield}}}","revision":1,"cardPolicies":[{"id":"native-prohibition","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash"]}]},
              {"id":"{{{Source}}}","revision":1,"hpSkillGrants":[{"id":"actual-nine","skillId":"{{{Hunjiang}}}","minimumHp":9}]},
              {"id":"{{{LateShield}}}","revision":1,"hpSkillGrants":[{"id":"after-real-damage","skillId":"{{{Shield}}}","maximumHp":7}]},
              {"id":"{{{Redirect}}}","revision":1,"triggers":[{"id":"real-extra-redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},{"op":"redirectCurrentAttack","target":"selectedTarget"} {{{redirectTail}}} ]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Shield, Redirect, Source, LateShield };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "马玩实际命令小夹具", optionLabels = id is Driver or Watch || id == Redirect && scenario == "duplicate" ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "马玩原生规则") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:mw-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddGeneral(new(Owner, "马玩真实本人", "ol-ma-wan", "fixture:mw-owner-pick", "qun", 8,
                scenario == "range" ? [Hunjiang, Driver, Watch] : scenario == "human" ? ["classic:mashu", Driver, Watch] : scenario == "loss" ? [Source, "classic:mashu", Driver, Watch] : [Hunjiang, "classic:mashu", Driver, Watch]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:mw-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:mw-pick-" + index;
                b.AddSkill(new(pick, "固定其他顺序", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000000d : 100000000d * (3 - index)) });
                b.AddGeneral(new(id, "真实其他", "supporter", pick, "qun", 8,
                    scenario == "human" && index == 0 ? [Hunjiang, "classic:mashu", Watch] : scenario == "redirect" && index == 2 || scenario == "duplicate" && index > 0 ? [Quiet, Redirect] : scenario == "duplicate" && index == 0 ? [Quiet, LateShield] : scenario == "shield" && index == 1 ? [Quiet, Shield] : [Quiet])
                { InitialHp = scenario == "duplicate" && index == 1 ? 1 : scenario == "duplicate" && index == 2 ? 2 : 8 });
            }
            b.AddCard(new("fixture:mw-blade", "青龙偃月刀", "装备牌", "真实攻击范围3武器", CardKind.QinglongCrescentBlade));
            b.AddDeck(new("fixture:mw-deck", "固定实体非种子搜索", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i =>
                new ContentDeckPhysicalCard(scenario == "range" && i % 9 == 8 ? "fixture:mw-blade" : "standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "马玩真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:mw-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
