using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class ShenTaishiCiChecks
{
    private const string Hero = "mobile:shen-taishi-ci", Driver = "fixture:shen-tsc-driver", Hold = "fixture:shen-tsc-hold", Quiet = "fixture:shen-tsc-quiet", CleanupHold = "fixture:shen-tsc-judgment-hold",
        Ally = "fixture:shen-tsc-ally", Enemy = "fixture:shen-tsc-enemy", Far = "fixture:shen-tsc-far", Deck = "fixture:shen-tsc-deck";

    public static void PaidDiscardAndOpaqueTakeResumeOriginalTurnAndRotate()
    {
        var s = Scene.Start(pause: true); s.Play();
        s.Need(s.Wei().SequenceEqual([0, 1, 1, 1]), "The official GameStarting program marks each actual other character.");
        s.End(); s.Reach(() => s.Powei(1));
        s.Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShenTaishiCiPowei is not null).ShenTaishiCiPowei is
            { TargetSeat: 1, Stage: ShenTaishiCiPoweiStage.Choice }, "The actual marked foreign turn owns its choice on the Powei frame.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "damage");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        var paid = s.E<ShenTaishiCiPoweiPaidEvent>().Single();
        s.Need(paid.Branch == "damage" && paid.From == CardLocation.Hand(0) && paid.To == CardLocation.DiscardPile &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShenTaishiCiPowei is not null).ShenTaishiCiPowei is
            { Stage: ShenTaishiCiPoweiStage.MovementChildren } && s.E<ShenTaishiCiPoweiRangeEvent>().Length == 0,
            "The real logical hand discard pays once before its actual CardsMoved child; damage and range wait for that child.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.Powei(2));
        s.Need(s.V(1).Hp == 3 && s.Wei()[1] == 0 && s.E<ShenTaishiCiPoweiRangeEvent>() is [{ AttackerSeat: 1, OwnerSeat: 0 }] &&
            s.G.CardMovements.Count(m => m.CardId == paid.CardId && m.Sequence == paid.Sequence) == 1,
            "Paid discard resumes its original one-point native damage once, clears that target's Wei, and grants that turn's directed range.");
        var take = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == "take");
        s.Need(take.Cards.Count == 0 && s.G.CreateSnapshot(3).PendingDecision?.Choices.All(c => c.Cards.Count == 0) != false,
            "The foreign Hand choice exposes only an opaque slot, including to another player view.");
        var before = s.V().HandCount; s.Answer(c => c.Id == take.Id); s.Reach(() => s.Powei(3));
        var obtained = s.E<ShenTaishiCiPoweiPaidEvent>().Single(e => e.Branch == "take");
        s.Need(s.V().HandCount == before + 1 && obtained.From == CardLocation.Hand(2) && obtained.To == CardLocation.Hand(0) &&
            s.E<ShenTaishiCiPoweiRangeEvent>().Any(e => e.AttackerSeat == 2 && e.OwnerSeat == 0) && s.G.GetCombatDistance(2, 0) == 2 && s.G.GetAttackRange(2) == 1,
            "Obtaining pays one actual hidden hand entity and grants only the intended directed range relation without changing distance or weapon range.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel"); s.Play();
        s.Need(s.Wei().SequenceEqual([0, 1, 0, 1]), "At the next true own-turn prelude, all remaining Wei move simultaneously once and skip the owner.");
        s.Cold();
    }

    public static void MissionSuccessPhysicalSlashChoiceAndFailedDyingRecovery()
    {
        var s = Scene.Start(); s.Play();
        for (var target = 1; target <= 3; target++) { s.Use("tap", [target]); s.Play(); }
        s.Need(s.Wei().All(n => n == 0) && !s.V().Skills!.Any(k => k.ContentId == "mobile-god:shenzhu"),
            "Actual damage clears every Wei, while success still waits for the original owner's actual next turn.");
        s.End(); s.Play();
        s.Need(s.E<ShenTaishiCiPoweiMissionEvent>() is [{ Success: true }] && s.V().Skills!.Any(k => k.ContentId == "mobile-god:shenzhu"),
            "The own-turn prelude completes the official mission and grants the actual derived Shenzhu skill.");
        s.Slash(3); s.Reach(() => s.P?.SkillPrompt?.SkillId == "mobile-god:shenzhu");
        var hand = s.V().HandCount; s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "draw-one"); s.Play();
        s.Need(s.V().HandCount == hand + 1 && s.CanSlash(3), "An actual physical Slash's first Shenzhu choice draws one real card and permits one further Slash.");
        s.Slash(3); s.Reach(() => s.P?.SkillPrompt?.SkillId == "mobile-god:shenzhu");
        hand = s.V().HandCount; s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "draw-three"); s.Play();
        s.Need(s.V().HandCount == hand + 3 && !s.CanSlash(3), "The alternative draws three actual cards and forbids further Slash uses in the current actual turn.");
        s.Cold();

        var failure = Scene.Start(); failure.Play(); failure.Use("collapse"); failure.Play();
        failure.Need(failure.V().Hp == 1 && failure.Wei().All(n => n == 0) && failure.V().Equipment.Count == 0 &&
            failure.E<ShenTaishiCiPoweiMissionEvent>() is [{ Success: false }] &&
            failure.G.CardMovements.Any(m => m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.CardKind == CardKind.QinggangSword),
            "A genuine signed-HP dying entry fails the mission once, recovers to one, clears every Wei and discards the real equipment.");
        failure.Cold(); failure.End(); failure.Play();
        failure.Need(!failure.V().Skills!.Any(k => k.ContentId == "mobile-god:shenzhu") && failure.E<ShenTaishiCiPoweiMissionEvent>().Length == 1,
            "Failure permanently ends the mission; its empty field cannot later awaken Shenzhu.");
    }

    public static void HigherHpSlashDulieUsesFinalHeartJudgment()
    {
        var s = Scene.Start(attack: true); s.Play(); s.End();
        s.Need(s.V().Hp == 4 && s.V(3).Hp == 7, "The campaign setup uses the actual four-HP official general and genuinely higher seven-HP attacker.");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == CleanupHold, cancelPowei: true);
        s.Need(s.G.ResolutionStack.OfType<JudgmentFrame>().Any(j => j.Reason == "skill.mobile-god.dulie") && s.E<ShenTaishiCiDulieCancelledEvent>().Length == 0,
            "The real finalized-judgment child pauses before Dulie consumes its public result and mutates the original Slash targets.");
        s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Play();
        var cancelled = s.E<ShenTaishiCiDulieCancelledEvent>().Single();
        s.Need(cancelled.RemainingTargetCount == 0 && s.V().Hp == 4 && s.E<ProgramJudgmentOutcomeRecordedEvent>().Any(e => e.ProgramFrameId == cancelled.FrameId &&
            e.Outcome is { Suit: Suit.Heart, Reason: "skill.mobile-god.dulie" }) &&
            s.E<CardUseDeclaredEvent>().Any(e => e.ResolutionId == cancelled.UseId && e.SourceSeat == 3) &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == cancelled.UseId) == 1,
            "The actual finalized Heart removes the only Slash target and completes that original real use once after cold restore.");

        var multi = Scene.StartMulti(); multi.Play();
        multi.Need(multi.V().HandCount == 1 && multi.V().Equipment.Any(c => c.Kind == CardKind.FangtianHalberd) && multi.V(1).GeneralId == Hero &&
            multi.V().Hp == 7 && multi.V(1).Hp == 4 && multi.V(2).Hp == 4,
            "The multi-target cancellation uses a real last-hand Slash, genuinely equipped Fangtian and the actual official Taishi Ci at seat one.");
        var action = multi.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1, 2]));
        multi.Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, multi.G.Revision, multi.P!.PromptId, action.PlayedCardKind));
        multi.Reach(() => multi.P?.SkillPrompt?.SkillId == CleanupHold);
        var use = multi.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.SourceSeat == 0 && f.CardKind == CardKind.Slash);
        multi.Need(use.TargetIndex == 0 && use.TargetSeats.SequenceEqual([1, 2]) && use.PreparedTargetAttacks is [{ TargetSeat: 1 }, { TargetSeat: 2 }] &&
            use.Continuations.FangtianHalberd is { TargetSeats: [1, 2] },
            "The real judgment pauses on a fully prepared two-target Fangtian use before first-target cancellation.");
        multi.Cold(); multi.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); multi.Play();
        var removed = multi.E<ShenTaishiCiDulieCancelledEvent>().Single();
        multi.Need(multi.V(1).Hp == 4 && multi.V(2).Hp == 3 && multi.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == removed.UseId) == 1 &&
            removed.RemainingTargetCount == 1 && multi.E<ShenTaishiCiDulieRetainedTargetEvent>() is [{ TargetSeat: 2, Index: 0 }] &&
            multi.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 2 && e.SourceSeat == 0) == 1,
            "The retained second target takes the same real Slash's one-point damage once after cold restore; cancelled Taishi Ci takes none.");
    }

    private sealed class Fixture(bool attack) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-taishi-ci", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"tap","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":1}]},
                    {"id":"collapse","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:shen-tsc-no-draw","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
                  {"id":"{{CleanupHold}}","revision":1,"triggers":[{"id":"real-finalized-judgment","window":"judgmentFinalized","subject":"any","judgmentReasons":["skill.mobile-god.dulie"],"minimumRank":1,"maximumRank":13,"suits":["spade","heart","club","diamond"],"excludedReasons":[],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cleanup","options":[{"id":"continue"}]}]}]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"paid-discard-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.shen-taishi-ci.powei.discard"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}]}
                """;
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "原生伤害体力驱动", description = "固定真实小局" },
                [Quiet] = new { name = "安静同行", description = "跳过无关出牌" },
                ["fixture:shen-tsc-no-draw"] = new { name = "保留真实末张手牌", description = "正常摸牌数减二" },
                [CleanupHold] = new { name = "真实判定结果子窗", description = "实际判定最终结果完成后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续原杀" } },
                [Hold] = new { name = "实际弃牌子窗", description = "真实支付后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续原破围" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:shen-tsc-idle", "普通角色", "普通角色"));
            b.AddCard(new("fixture:shen-tsc-halberd", "方天画戟", "装备牌", "原生多目标武器", CardKind.FangtianHalberd));
            b.AddGeneral(new(Ally, "真实盟友", "", Quiet, "wu", 4)); b.AddGeneral(new(Enemy, "真实敌方", "", Quiet, "wei", 4));
            b.AddGeneral(new(Far, "高体力攻击者", "", attack ? "fixture:shen-tsc-idle" : Quiet, "wei", attack ? 7 : 4));
            b.AddDeck(new(Deck, "固定红桃闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "神太史慈原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int[] Wei() => Enumerable.Range(0, 4).Select(s => V(s).Markers?.FirstOrDefault(m => m.Kind == PlayerMarkerKind.ShenTaishiCiWei)?.Count ?? 0).ToArray();
        public bool Powei(int target) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == "mobile-god:powei" && p.TargetSeat == target && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cancel");
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public bool CanSlash(int seat) => G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([seat]));
        public void Slash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done, bool cancelPowei = false)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal game.");
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (cancelPowei && p.SkillPrompt?.SkillId == "mobile-god:powei") Answer(c => c.Parameters.GetValueOrDefault("branch") == "cancel");
                else if (p.Kind == DecisionKind.DiscardCards) Accept(new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected human boundary: " + JsonSerializer.Serialize(p));
            }
            throw new InvalidOperationException("Bounded actual Shen Taishi Ci flow not reached: " + JsonSerializer.Serialize(P));
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.First(predicate); Accept(new AnswerPromptCommand(0, p.PromptId, c.Id, G.Revision)); }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements), "Cold restore replays the accepted journal and preserves the original owning receipt, private decision, real movement and once-only outcome.");
        }
        public void Accept(GameCommand c)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command."); }
        public void Need(bool yes, string message)
        { if (!yes) throw new InvalidOperationException(message + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(15).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool pause = false, bool attack = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(attack));
            var setup = new ShanheBattleSetup(31, 1, "shen-tsc-native", Hero, 4, 0, 0, 0, 0, pause ? [Driver, Hold] : attack ? [Driver, CleanupHold] : [Driver],
                Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Spade, 7)).ToArray(), [new(CardKind.QinggangSword, Suit.Club, 1)],
                [new(Enemy) { StartingHand = [new(CardKind.Dodge, Suit.Club, 8)] }, new(Far) { StartingHand = attack ? [new(CardKind.Slash, Suit.Spade, 9)] : [new(CardKind.Dodge, Suit.Spade, 9)] }],
                [new(Ally) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 3)] }], new string('c', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
        public static Scene StartMulti()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(true));
            var setup = new ShanheBattleSetup(31, 1, "shen-tsc-fangtian", Far, 7, 0, 0, 0, 0, ["fixture:shen-tsc-no-draw", CleanupHold],
                [new(CardKind.Slash, Suit.Spade, 7)], [new(CardKind.FangtianHalberd, Suit.Spade, 12)],
                [new(Ally) { StartingHand = [] }, new(Enemy) { StartingHand = [] }], [new(Hero) { StartingHand = [] }], new string('c', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
