using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiXiahouShangChecks
{
    private const string Hero = "mobile:shi-xiahou-shang", Skill = "mobile:shi-tanfeng",
        Peer = "fixture:shi-xiahou-shang-peer", Quiet = "fixture:shi-xiahou-shang-quiet",
        Watch = "fixture:shi-xiahou-shang-damage-watch", Deck = "fixture:shi-xiahou-shang-deck";

    public static void PreselectedBothIndependentTargetsNativeDamageAndPaidColdRecovery()
    {
        var s = Scene.Start(targetHand: 5, targetHp: 5);
        s.Reach(s.ModePrompt);
        s.Need(new[] { "discard", "fire", "both", "cancel" }.All(id => s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id)),
            "One preparation prompt preselects either item, both in order, or cancellation before either effect happens.");
        s.Mode("both"); s.Target(1); s.Count("discard-one"); s.Pay();
        s.Reach(() => s.TargetPrompt("fire-target"));
        s.Need(s.V(1).HandCount == 4 && s.V(1).Hp == 5 && s.P!.Choices.Any(c => c.Targets.SequenceEqual([1])),
            "The chosen discard settles before the independent second target; the first target remains legal for the second item.");
        var payments = s.PaidCards(); s.Cold();
        s.Target(1); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1 && e.Nature == DamageNature.Fire),
            "The second item starts a real one-point fire damage child on the independently selected same target.");
        s.Cold(); s.Continue(); s.Reach(() => s.G.State.CurrentSeat != 0);
        s.Need(s.PaidCards() == payments && payments == 1 && s.V(1).Hp == 4 &&
            s.Skipped(SkillProgramTurnPhase.Draw) && s.Skipped(SkillProgramTurnPhase.Play),
            "Both paid branches return once, then current equal hand and HP comparisons skip only this actual turn's draw and play.");
        s.Replay();

        var different = Scene.Start(targetHand: 5, targetHp: 6);
        different.Mode("both"); different.Target(1); different.Count("discard-one"); different.Pay();
        different.Target(2); different.Finish();
        different.Need(different.V(1).Hp == 6 && different.V(2).Hp == 7 && different.V(1).HandCount == 4,
            "The second item can choose a different participant without retaining the discard target for damage.");
    }

    public static void CancellationZeroOneTwoSelfAndLiveHandComparison()
    {
        var cancel = Scene.Start(); cancel.Mode("cancel"); cancel.Finish();
        cancel.Need(cancel.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && cancel.V(0).HandCount == 6 && cancel.PaidCards() == 0 &&
            !cancel.Skipped(SkillProgramTurnPhase.Draw) && !cancel.Skipped(SkillProgramTurnPhase.Play),
            "Cancellation asks for no target, pays no cards and leaves both normal phases available.");

        foreach (var item in new[]
        {
            (Count: "discard-zero", Target: 0, TargetHand: 4, Paid: 0, DrawSkipped: true, OwnHand: 4),
            (Count: "discard-one", Target: 1, TargetHand: 5, Paid: 1, DrawSkipped: true, OwnHand: 4),
            (Count: "discard-two", Target: 1, TargetHand: 7, Paid: 2, DrawSkipped: false, OwnHand: 6),
            (Count: "discard-two", Target: 1, TargetHand: 0, Paid: 0, DrawSkipped: true, OwnHand: 4),
            (Count: "discard-two", Target: 0, TargetHand: 4, Paid: 2, DrawSkipped: true, OwnHand: 2)
        })
        {
            var s = Scene.Start(targetHand: item.TargetHand); s.Mode("discard"); s.Target(item.Target); s.Count(item.Count);
            for (var i = 0; i < item.Paid; i++) s.Pay();
            s.Finish();
            s.Need(s.PaidCards() == item.Paid && s.V(0).HandCount == item.OwnHand &&
                s.Skipped(SkillProgramTurnPhase.Draw) == item.DrawSkipped && !s.Skipped(SkillProgramTurnPhase.Play),
                "Zero/one/two and empty/self targets discard real available cards and compare current hands after all payments: " + item);
            if (item.Target == 0 && item.Paid == 2)
            {
                s.End(); s.Reach(s.ModePrompt); s.Mode("cancel"); s.Finish();
                s.Need(s.V(0).HandCount == 4 && s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard },
                    "The previous turn's draw debt expires; cancelling next preparation restores the normal draw and play phases.");
            }
        }
    }

    public static void PostDamageHpComparisonSelfTargetAndNestedOwnerHealthChange()
    {
        foreach (var item in new[] { (Target: 1, Hp: 6, Skip: false), (Target: 1, Hp: 5, Skip: true), (Target: 0, Hp: 6, Skip: true) })
        {
            var s = Scene.Start(targetHp: item.Hp); s.Mode("fire"); s.Target(item.Target); s.Finish();
            s.Need(s.V(item.Target).Hp == (item.Target == 0 ? 3 : item.Hp - 1) &&
                s.Skipped(SkillProgramTurnPhase.Play) == item.Skip && !s.Skipped(SkillProgramTurnPhase.Draw) && s.PaidCards() == 0,
                "Only the preselected fire branch runs, including self damage, and its post-damage current HP decides the play debt.");
        }
        var changed = Scene.Start(targetHp: 5, changeOwnerHp: true);
        changed.Mode("fire"); changed.Target(1); changed.Reach(() => changed.P?.SkillPrompt?.SkillId == Watch);
        changed.Need(changed.V(0).Hp == 3 && changed.V(1).Hp == 4,
            "A real after-damage child changed the owner's HP before the parent comparison.");
        changed.Cold(); changed.Continue(); changed.Finish();
        changed.Need(!changed.Skipped(SkillProgramTurnPhase.Play) && changed.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard },
            "The parent compares current 4 versus 3 after the returned damage child, rather than frozen preparation HP 4 versus 4.");
        changed.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(player => player.Seat == seat);
        public T[] E<T>() => G.Events.Select(envelope => envelope.Payload).OfType<T>().ToArray();
        public bool ModePrompt() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "both");
        public bool TargetPrompt(string? role = null) => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "shi-tanfeng-target" && (role is null || c.Parameters.GetValueOrDefault("target-role") == role));
        public void Mode(string id) { Reach(ModePrompt); Answer(c => c.Parameters.GetValueOrDefault("option-id") == id); }
        public void Target(int seat) { Reach(() => TargetPrompt()); Answer(c => c.Targets.SequenceEqual([seat])); }
        public void Count(string id) { Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true); Answer(c => c.Parameters.GetValueOrDefault("option-id") == id); }
        public void Pay()
        {
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
            Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Finish() => Reach(() => G.State.CurrentSeat != 0 || P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public int PaidCards() => G.CardMovements.Count(movement => movement.To == CardLocation.DiscardPile &&
            movement.Reason.Value == "skill-program.mobile:shi-tanfeng.SelectAndMoveOwnedCard");
        public bool Skipped(SkillProgramTurnPhase phase)
        {
            // ProgramTurnPhaseSubstitutedEvent is an invoice for the separate
            // card-or-marker alternative-cost mechanism, not a universal skip
            // event. These fixtures have a nonempty deck and no other draws:
            // observe the actual normal draw and actual Play entry instead.
            var actualTurn = G.Events.Select(envelope => envelope.Payload)
                .SkipWhile(payload => payload is not TurnStartedEvent { TurnNumber: 1, ActorSeat: 0 })
                .TakeWhile(payload => payload is not TurnEndedEvent { TurnNumber: 1, ActorSeat: 0 }).ToArray();
            return phase switch
            {
                SkillProgramTurnPhase.Draw => !actualTurn.OfType<CardsDrawnEvent>().Any(e => e.PlayerSeat == 0 && e.Count > 0),
                SkillProgramTurnPhase.Play => !actualTurn.OfType<PhaseChangedEvent>().Any(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play),
                _ => throw new InvalidOperationException("The preparation fixture checks only its normal draw and Play branches.")
            };
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 180; i++)
            {
                if (ready()) return;
                if (P is { } prompt && prompt.PlayerSeat == 0)
                {
                    if (prompt.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (prompt.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    throw new InvalidOperationException("Unexpected Tanfeng prompt. " + State());
                }
                Need(G.State.Winner == Winner.None, "The bounded scene must retain all participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded scene did not reach its expected preparation or native child.");
        }
        public void Answer(Func<PromptChoice, bool> match)
        {
            var prompt = P!; var choice = prompt.Choices.FirstOrDefault(match);
            Need(choice is not null, "The expected original issued choice must exist.");
            Accept(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice!.Id, G.Revision));
        }
        private void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "Tanfeng command rejected.");
        }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + State()); }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))).ToArray(),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold restore preserves exact views, frame cursor, prepaid movement, pending target and journal.");
        }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The actual command journal reproduces independent targets, paid children, damage and current-turn debts.");
        }
        public static Scene Start(int targetHand = 5, int targetHp = 5, bool changeOwnerHp = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(changeOwnerHp));
            ShanheStartingCard[] Hand(int count) => Enumerable.Range(0, count).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i % 13 + 1)).ToArray();
            var setup = new ShanheBattleSetup(97, 1, "shi-xiahou-shang", Hero, 4, 0, 0, 0, 0, [Watch], Hand(4), [],
                [new(Peer, Hp: targetHp) { StartingHand = Hand(targetHand) }, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [], new string('f', 64));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
            }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture(bool changeOwnerHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-xiahou-shang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var lose = changeOwnerHp ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : string.Empty;
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"damage-child","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,"isSkillInvocation":false,
                "effects":[{{lose}}{"op":"chooseOption","target":"owner","resultBind":"damage-return","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "保留真实回合并跳过AI出牌。" },
                [Watch] = new { name = Watch, description = "暂停原生伤害后的实际子窗口。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "固定同桌", "", Quiet, "wei", 8));
            builder.AddDeck(new(Deck, "固定闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "探锋真实准备阶段", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
