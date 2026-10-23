using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using System.Text.Json;
using static Program;

internal static class FeedbackChecks
{

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Images(child)) yield return nested;
    }

    private static IEnumerable<(ImageDrawing Image, Rect Bounds)> ImageBounds(Drawing? drawing, Matrix transform)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect));
        if (drawing is DrawingGroup group)
        {
            var nestedTransform = group.Transform?.Value ?? Matrix.Identity;
            nestedTransform.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in ImageBounds(child, nestedTransform)) yield return nested;
        }
    }

    public static void PublicProjection()
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var view = Engine(vm).CreateSnapshot(0);
        var sequence = 0L;
        EventEnvelope Envelope(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 1, "feedback-check", payload);
        var privateEvents = new[]
        {
            Envelope(new GeneralSelectionRequestedEvent(1, ["private-general"])),
            Envelope(new GeneralSelectedEvent(1, "private-general")),
            Envelope(new CardMovedEvent(9876, CardKind.Peach, CardLocation.DrawPile,
                new CardLocation(CardZoneKind.Hand, 1), CardMoveReasons.Draw)),
            Envelope(new CardsDrawnEvent(1, 1))
        };
        var privateQuantities = BattleCueProjector.Project(privateEvents, view);
        Assert(privateQuantities is [{ Kind: BattleCueKind.Draw, SourceSeat: 1, TargetSeats: [1], DrawCount: 1,
                Label: "摸牌 +1", PublicCardId: null, PublicCard: null, CardKind: null }] &&
               BattleCueProjector.Project(privateEvents.Take(3), view).Count == 0 &&
               BattleCueProjector.ProjectCardPlays(privateEvents, view).Count == 0,
            "Draw feedback may disclose only the public quantity; hidden generals and private physical faces must remain absent.");
        var drawState = SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
        var extraDraw = new CardMoveReason("program.extra-draw-phase.draw");
        var drawEvents = new[]
        {
            Envelope(new CardMovedEvent(8901, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8902, CardKind.Peach, CardLocation.DrawPile, CardLocation.Hand(1), extraDraw)),
            Envelope(new CardMovedEvent(8903, CardKind.Dodge, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8904, CardKind.Nullification, CardLocation.DrawPile, CardLocation.Hand(1), CardMoveReasons.Draw)),
            Envelope(new CardsDrawnEvent(0, 1)),
            Envelope(new CardsDrawnEvent(0, 1)),
            Envelope(new CardsDrawnEvent(1, 2)),
            Envelope(new ActualDrawPhaseCompletedEvent(100, 0, 1, false, false, 99)),
            Envelope(new CardMovedEvent(8905, CardKind.IronChain, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.Draw)) with { Revision = 2 },
            Envelope(new CardsDrawnEvent(0, 1)) with { Revision = 2 }
        };
        var drawCues = BattleCueProjector.Project(drawEvents, view);
        Assert(drawCues is
               [{ Kind: BattleCueKind.Draw, SourceSeat: 0, TargetSeats: [0], DrawCount: 2, Label: "摸牌 +2", Revision: 1 },
                { Kind: BattleCueKind.Draw, SourceSeat: 1, TargetSeats: [1], DrawCount: 2, Label: "摸牌 +2", Revision: 1 },
                { Kind: BattleCueKind.Draw, SourceSeat: 0, TargetSeats: [0], DrawCount: 1, Label: "摸牌 +1", Revision: 2 }] &&
               drawCues.Select(cue => cue.Sequence).SequenceEqual(new[] { drawEvents[5].Sequence, drawEvents[6].Sequence, drawEvents[9].Sequence }) &&
               drawCues.All(cue => cue.ActorName == view.Players.Single(player => player.Seat == cue.SourceSeat).GeneralName),
            "Actual draw facts must aggregate once per committed revision and owner at the last fact, not use a requested phase quantity or another player's name.");
        Assert(drawCues.All(cue => cue.HasDrawCards && cue is
                { IsInquiry: false, CardKind: null, PublicCardId: null, PublicCard: null, JudgmentCard: null,
                    WeaponKind: null, IsUseCompletion: false, HpDelta: 0, RemainingHp: null }) &&
               BattleCueProjector.ProjectCardPlays(drawEvents, view).Count == 0 &&
               !(drawCues[0] with { DrawCount = 0 }).HasDrawCards &&
               !(drawCues[0] with { DrawCount = -1 }).HasDrawCards &&
               !(drawCues[0] with { SourceSeat = -1 }).HasDrawCards &&
               !(drawCues[0] with { IsInquiry = true }).HasDrawCards &&
               !(drawCues[0] with { Kind = BattleCueKind.Card }).HasDrawCards,
            "A draw quantity must not become a private face, fake played card, HP result or valid inquiry/empty/invalid-source animation.");
        var excludedDraws = new[]
        {
            Envelope(new CardMovedEvent(8910, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.InitialDeal)),
            Envelope(new CardMovedEvent(8911, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.RecastDraw)),
            Envelope(new CardMovedEvent(8912, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), new("skill.private.draw"))),
            Envelope(new CardMovedEvent(8913, CardKind.Slash, CardLocation.DrawPile, new(CardZoneKind.PrivateReserve, 0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8914, CardKind.Slash, CardLocation.Hand(1), CardLocation.Hand(0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8915, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(999), CardMoveReasons.Draw)),
            Envelope(new CardsDrawnEvent(0, 0)),
            Envelope(new CardsDrawnEvent(0, -1)),
            Envelope(new CardsDrawnEvent(-1, 2)),
            Envelope(new CardsDrawnEvent(999, 2)),
            Envelope(new ActualDrawPhaseCompletedEvent(101, 0, 1, true, true, 2)),
            Envelope(new ActualDrawPhaseCompletedEvent(102, 0, 1, false, false, 0))
        };
        Assert(BattleCueProjector.Project(excludedDraws, view).Count == 0 &&
               BattleCueProjector.Project([], view).Count == 0,
            "Private movements, skipped/requested phases and invalid quantities must not invent an actual public draw result.");
        var changedPrivateFaces = drawEvents.Select(envelope => envelope.Payload is CardMovedEvent moved
            ? envelope with { Payload = moved with { CardId = -777, CardKind = (CardKind)int.MaxValue } } : envelope).ToArray();
        Assert(JsonSerializer.Serialize(drawCues) == JsonSerializer.Serialize(BattleCueProjector.Project(changedPrivateFaces, view)) &&
               JsonSerializer.Serialize(drawCues) == JsonSerializer.Serialize(BattleCueProjector.Project(
                   drawEvents.Where(envelope => envelope.Payload is not CardMovedEvent), view)) &&
               JsonSerializer.Serialize(drawCues) == JsonSerializer.Serialize(BattleCueProjector.Project(drawEvents, Engine(vm).CreateSnapshot(1))) &&
               drawState == SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true)),
            "Changing hidden draw identities/kinds or the safe viewer must not change public quantity feedback or mutate the engine.");
        var aidResponses = BattleCueProjector.Project(
        [Envelope(new ProgramAttackRangeAidChoiceResolvedEvent(110, "classic:jiefan", "aid", 0, 1, 2, null, [8920])),
         Envelope(new ProgramAttackRangeAidChoiceResolvedEvent(110, "classic:jiefan", "aid", 0, 3, 2, null, [])),
         Envelope(new ProgramAttackRangeAidChoiceResolvedEvent(110, "classic:jiefan", "aid", 0, 1, 2, 8921, []))], view);
        Assert(aidResponses is
               [{ Kind: BattleCueKind.Response, SourceSeat: 1, TargetSeats: [2], Detail: "令目标摸一张牌" },
                { Kind: BattleCueKind.Response, SourceSeat: 3, TargetSeats: [2], Detail: "令目标摸一张牌" },
                { Kind: BattleCueKind.Response, SourceSeat: 1, TargetSeats: [2], Detail: "弃置一张武器" }] &&
               aidResponses.All(cue => cue is
               { IsInquiry: false, HpDelta: 0, RemainingHp: null, MaximumHp: null, DrawCount: 0,
                   HasDrawCards: false, CardKind: null, PublicCardId: null, PublicCard: null }),
            "An attack-range aid response must keep its responder and beneficiary without fabricating recovery, a draw animation or private card faces from the selected response.");
        ProgramActivationFeedback(view);
        PublicDraftRevealFeedback(view);
        foreach (var (paid, empty, owner, target, label) in new (IGameEvent, IGameEvent, int, int, string)[]
        {
            (new SilverLionRemovedRecoveryEvent(801, 1, CardMoveReasons.EquipmentReplace, 1, 3),
                new SilverLionRemovedRecoveryEvent(802, 1, CardMoveReasons.EquipmentReplace, 0, 3), 1, 1, "白银狮子 · 移除"),
            (new ChunlaoRescueEvent(803, 1, 2, 8901, 1, 1),
                new ChunlaoRescueEvent(804, 1, 2, 8902, 0, 0), 1, 2, "醇醪 · 酒救援"),
            (new ProgramDyingRescueEvent(805, "classic:chunlao", 1, 2, 8903, 1, 1),
                new ProgramDyingRescueEvent(806, "classic:chunlao", 1, 2, 8904, 0, 0), 1, 2, "酒救援")
        })
        {
            var actual = Envelope(new RecoveryAppliedEvent(target, target, 1, 3));
            var paidEnvelope = Envelope(paid);
            var emptyEnvelope = Envelope(empty);
            var projected = BattleCueProjector.Project([actual, paidEnvelope, emptyEnvelope], view);
            Assert(projected.Where(cue => cue.Kind == BattleCueKind.Recovery).ToArray() is
                    [{ HpDelta: 1, RemainingHp: 3, Sequence: var appliedSequence }] && appliedSequence == actual.Sequence,
                $"{paid.GetType().Name} must explain its public result without duplicating the actual recovery or inventing healing for zero recovery.");
            var explanations = projected.Where(cue => cue.Sequence != actual.Sequence).ToArray();
            Assert(explanations.Length == 2 && explanations.All(cue => cue is
                    { Kind: BattleCueKind.ResponseWindow, IsInquiry: false, HpDelta: 0, RemainingHp: null,
                        MaximumHp: null, CardKind: null, PublicCardId: null, PublicCard: null } &&
                    cue.SourceSeat == owner && cue.TargetSeats.SequenceEqual([target]) && cue.Label.Contains(label, StringComparison.Ordinal)) &&
                explanations[0].Detail == "回复体力已结算" && explanations[1].Detail == "此次未回复体力",
                $"{paid.GetType().Name} must retain the equipment/rescue identity and actual zero/success explanation without a numeric heal or false played face.");
            Assert(!GameSoundRules.FromPublicCues([explanations[1]]).Contains(GameSound.Recover),
                "A completed rescue/equipment result with zero actual recovery must not play a healing sound.");
        }
        var hpLossEvent = Envelope(new ProgramSkillHpLostEvent(810, "private:undisclosed-hp-loss", 1, 2, -1));
        var hpLossCues = BattleCueProjector.Project([hpLossEvent,
            Envelope(new ProgramSkillHpLostEvent(811, "private:undisclosed-hp-loss", 1, 0, -1))], view);
        Assert(hpLossCues is [{ Kind: BattleCueKind.HpLoss, SourceSeat: -1, TargetSeats: [1], Label: "−2",
                Detail: "失去体力", HpDelta: -2, RemainingHp: -1, SkillId: null, CardKind: null,
                PublicCardId: null, PublicCard: null, IsInquiry: false, CompletedCardUseResolutionId: null }] &&
            hpLossCues[0].Sequence == hpLossEvent.Sequence && hpLossCues[0].Revision == hpLossEvent.Revision &&
            hpLossCues[0].MaximumHp == view.Players.Single(player => player.Seat == 1).MaxHp &&
            hpLossCues[0].ActorName == view.Players.Single(player => player.Seat == 1).GeneralName &&
            GameSoundRules.FromPublicCues(hpLossCues).Count == 0 &&
            BattleCueProjector.ProjectCardPlays([hpLossEvent], view).Count == 0,
            "Actual positive HP loss must retain its exact public amount/remaining HP without inventing an attacker, private skill identity, played card or damage/healing sound; zero loss has no effect.");
        var afterUseLoss = hpLossEvent with
        {
            Revision = 2,
            Payload = ((ProgramSkillHpLostEvent)hpLossEvent.Payload) with { CompletedCardUseResolutionId = 812 }
        };
        var afterUseCue = BattleCueProjector.Project([afterUseLoss], view).Single();
        Assert(afterUseCue is { Kind: BattleCueKind.HpLoss, SourceSeat: -1, TargetSeats: [1], Revision: 2,
                CompletedCardUseResolutionId: 812, SourceCardUseResolutionId: null, CardKind: null, SkillId: null } &&
            GameSoundRules.FromPublicCues([afterUseCue]).Count == 0,
            "An after-use loss must preserve only its explicit public completed-use identity across revisions, without inventing an attacker or damage sound.");
        int[] targets = [1, 1, -1, 99];
        var publicEvents = new[]
        {
            Envelope(new CardUseDeclaredEvent(7, 2468, CardKind.Slash, 0)),
            Envelope(new TargetsConfirmedEvent(7, targets)),
            Envelope(new CardRespondedEvent(1357, 1, 0, CardKind.Slash)),
            Envelope(new DuelResponseEvent(7, 1, true, 1357, CardKind.Slash)),
            Envelope(new DamageAppliedEvent(0, 1, 2, 1, DamageNature.Fire)),
            Envelope(new RecoveryAppliedEvent(2, 1, 1, 2)),
            Envelope(new DamageAppliedEvent(0, 1, 0, 2))
        };
        var cues = BattleCueProjector.Project(publicEvents, view);
        targets[0] = 4;
        Assert(cues.Count == 4 && cues.Select(cue => cue.Sequence).Distinct().Count() == 4, "Public response duplicated or zero damage rendered.");
        Assert(cues.All(cue => cue.Revision == 1), "Public feedback lost its committed command revision.");
        Assert(cues[0].Label == "杀" && cues[0].TargetSeats.SequenceEqual([1]), "Declared card or detached public targets differ.");
        Assert(cues[0].CardKind == CardKind.Slash && cues[1].CardKind == CardKind.Slash && cues.Skip(2).All(cue => cue.CardKind is null),
            "Use and response artwork must come from their public effective kinds, never from private card IDs.");
        var secretSlash = Envelope(new CardUseDeclaredEvent(138, 2468, CardKind.Slash, 0)) with { Revision = 2 };
        var publicPlayed = Envelope(new CardRespondedEvent(1357, 1, 0, CardKind.Peach)) with { Revision = 2 };
        var beforeDisclosure = BattleCueProjector.Project([secretSlash, publicPlayed], view);
        Assert(beforeDisclosure.Single(cue => cue.Sequence == secretSlash.Sequence) is { Kind: BattleCueKind.Card, TargetSeats: [] } &&
               beforeDisclosure.Single(cue => cue.Sequence == publicPlayed.Sequence) is { Kind: BattleCueKind.Response, SourceSeat: 1 },
            "A secret original Slash exposes no early arrow while unrelated genuine Played feedback remains public.");
        var targetDisclosure = Envelope(new MouSmTargetsDisclosedEvent(138, 1380, 0, 2468, CardKind.Slash) { Targets = [1], HeldEventSequences = [secretSlash.Sequence + 100], HeldLogSequences = [100] }) with { Revision = 3 };
        var disclosedCue = BattleCueProjector.Project([targetDisclosure], Engine(vm).CreateSnapshot(1)).Single();
        Assert(disclosedCue is { Kind: BattleCueKind.Card, SourceSeat: 0, TargetSeats: [1], CardKind: CardKind.Slash, Detail: "指定目标" } &&
               targetDisclosure.Revision == 3 && disclosedCue.Sequence == targetDisclosure.Sequence && disclosedCue.Sequence > publicPlayed.Sequence &&
               BattleCueProjector.ProjectCardPlays([targetDisclosure], view).Count == 0,
            "The current committed disclosure supplies the arrow at its own increasing sequence without inventing a second played card or replaying an old envelope.");
        var recastCue = BattleCueProjector.Project([Envelope(new CardRecastEvent(0, 300, CardKind.IronChain, 1))], view).Single();
        Assert(recastCue is { CardKind: CardKind.IronChain, Detail: "重铸" }, "Recast lost its public card kind or action label.");
        Assert(cues[1].Label == "打出杀" && cues[2].Label == "−2" && cues[2].Nature == DamageNature.Fire && cues[3].Label == "+1", "Response, damage, or recovery feedback differs from committed events.");
        var lifeEvents = new[]
        {
            Envelope(new PlayerDyingEvent(41, 1, 0)),
            Envelope(new RecoveryAppliedEvent(2, 1, 2, 1)),
            Envelope(new DyingResolvedEvent(41, 1, true)),
            Envelope(new PlayerDyingEvent(42, 1, null)),
            Envelope(new PlayerDiedEvent(1, null)),
            Envelope(new DyingResolvedEvent(42, 1, false))
        };
        var lifeCues = BattleCueProjector.Project(lifeEvents, view);
        Assert(lifeCues.Select(cue => cue.Kind).SequenceEqual(new[]
            { BattleCueKind.Dying, BattleCueKind.Recovery, BattleCueKind.Rescued, BattleCueKind.Dying, BattleCueKind.Death }) &&
            lifeCues.Select(cue => cue.Sequence).SequenceEqual(lifeEvents.Take(5).Select(envelope => envelope.Sequence)),
            "Public dying, recovery, survival and death feedback must preserve event order without inventing a failed-rescue result.");
        Assert(lifeCues[0] is { LifeResolutionId: 41, SourceSeat: 1, TargetSeats: [1] } &&
            lifeCues[2] is { LifeResolutionId: 41, SourceSeat: 1, TargetSeats: [1], Label: "脱离濒死" } &&
            lifeCues[3] is { LifeResolutionId: 42, SourceSeat: 1, TargetSeats: [1] } &&
            lifeCues[4] is { LifeResolutionId: null, SourceSeat: 1, TargetSeats: [1] },
            "Life feedback must use the published victim and exact dying-window ID; death must not guess a frame ID or attribute its effect to the killer.");
        Assert(lifeCues[1] is { HpDelta: 2, RemainingHp: 1, SourceSeat: 2, TargetSeats: [1] } &&
            lifeCues.Where(cue => cue.Kind != BattleCueKind.Recovery).All(cue =>
                cue is { HpDelta: 0, RemainingHp: null, MaximumHp: null, IsInquiry: false }),
            "Only the public recovery fact may supply HP values; a survival event cannot infer an earlier HP result from the final viewer snapshot.");
        Assert(lifeCues.All(cue => cue is
            { CardKind: null, PublicCardId: null, PublicCard: null, IsUseCompletion: false, Revision: 1 }) &&
            BattleCueProjector.Project([Envelope(new DyingResolvedEvent(43, 2, false))], view).Count == 0,
            "Life outcomes must contain no private physical card face, fabricated card use, or rescue cue for a non-survived completion.");
        var sourceFreeEvents = new[] { Envelope(new DamageAppliedEvent(1, 1, 1, 0) { SourceLess = true }) };
        Assert(BattleCueProjector.Project(sourceFreeEvents, view).Single() is
            { Kind: BattleCueKind.Damage, SourceSeat: -1, TargetSeats: [1] },
            "Source-free damage feedback falsely attributes its internal continuation seat.");
        var sourceFreeReport = MatchSummary.Create(view with { Status = EngineStatus.Completed }, sourceFreeEvents)!;
        Assert(sourceFreeReport.Players.Single(player => player.Seat == 1) is { DamageDealt: 0, DamageTaken: 1 },
            "Source-free debt damage must count as received damage without crediting a damage dealer.");
        var virtualResponses = BattleCueProjector.Project(
            [Envelope(new CardRespondedEvent(-1, 1, 0, CardKind.Dodge)),
             Envelope(new CardRespondedEvent(-1, 2, 0, CardKind.Dodge))], view);
        Assert(virtualResponses.Count == 2 && virtualResponses.All(cue => cue.Label == "打出闪"),
            "Distinct virtual responses must not be collapsed by their shared nonphysical card sentinel.");
        var zeroResponses = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardRespondedEvent(0, 1, 0, CardKind.Dodge)),
             Envelope(new GroupResponseEvent(30, CardKind.ArrowBarrage, CardKind.Dodge, 1, true, 0)),
             Envelope(new CardRespondedEvent(0, 2, 0, CardKind.Dodge)),
             Envelope(new GroupResponseEvent(30, CardKind.ArrowBarrage, CardKind.Dodge, 2, true, 0))], view);
        Assert(zeroResponses.Count == 2 && zeroResponses.Select(cue => cue.SourceSeat).SequenceEqual([1, 2]),
            "The zero virtual-card sentinel collapsed distinct players or duplicated their group results.");
        var unknownResponse = BattleCueProjector.ProjectCardPlays([Envelope(new CardRespondedEvent(9876, 1, 0))], view).Single();
        Assert(unknownResponse.CardKind is null && unknownResponse.Label == "打出响应牌",
            "A legacy response without a public kind inferred a hidden physical face.");
        var counterspellCards = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardRespondedEvent(46, 1, 0, CardKind.Nullification)),
             Envelope(new NullificationRespondedEvent(21, 45, CardKind.Duel, 1, 46, true, 1))], view);
        Assert(counterspellCards.Count == 1 && counterspellCards[0].CardKind == CardKind.Nullification,
            "The committed counterspell and its chain result produced two public cards.");
        var rescueCards = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardUseDeclaredEvent(31, 99, CardKind.Peach, 1)),
             Envelope(new DyingResponseEvent(32, 1, true, 99) { UsedPeachPhysicalCardKind = CardKind.Dodge })], view);
        Assert(rescueCards.Count == 1 && rescueCards[0].CardKind == CardKind.Peach && rescueCards[0].CardActionLabel == "使用",
            "Converted rescue duplicated its declared use or displayed its physical material as the effective card.");
        var armorCues = BattleCueProjector.Project(
        [
            Envelope(new ArmorEffectAppliedEvent(8, CardKind.RenwangShield, 0, 1, CardKind.Slash)),
            Envelope(new JudgmentResolvedEvent(
                9,
                8,
                1,
                JudgmentReasons.BaguaDefense,
                42,
                CardKind.Dodge,
                Suit.Heart,
                Rank: 7,
                Succeeded: true))
        ], view);
        Assert(
            armorCues.Count == 2 &&
            armorCues[0].Label.Contains("仁王盾") &&
            armorCues[0].Label.Contains("无效") &&
            armorCues[1].Kind == BattleCueKind.Judgment &&
            armorCues[1].Label == "八卦阵 · ♥7" &&
            armorCues[1].Detail == "红色 · 视为打出闪",
            "Formal armor outcomes were not projected as public battle feedback.");
        // A public zone can show the printed Spade while the judgment event
        // publishes an effective Heart (for example, Hongyan). Neither is
        // evidence for exposing a new ordinary played-card face.
        var judgmentView = view with
        {
            Players = Array.AsReadOnly(view.Players.Select(player => player.Seat == 1 ? player with
            {
                Judgment = Array.AsReadOnly(new[] { new CardSnapshot(41, CardKind.Peach, Suit.Spade, 12, "桃", "Q") })
            } : player).ToArray())
        };
        var judgmentEvents = new[]
        {
            Envelope(new JudgmentRequestedEvent(10, 7, 1, JudgmentReasons.Indulgence, CardKind.Indulgence)),
            Envelope(new JudgmentReplacementResolvedEvent(11, 10, 1, 0, JudgmentReasons.Indulgence, true,
                40, 41, CardKind.Peach, Suit.Heart, 12)),
            Envelope(new JudgmentResolvedEvent(10, 7, 1, JudgmentReasons.Indulgence, 41, CardKind.Peach,
                Suit.Heart, 12, true)),
            Envelope(new JudgmentResolvedEvent(12, 8, 1, JudgmentReasons.Lightning, 43, CardKind.Slash,
                Suit.Spade, 5, true))
        };
        var judgmentCues = BattleCueProjector.Project(judgmentEvents, judgmentView);
        Assert(judgmentCues.Count == 4 &&
               judgmentCues[0] is { Kind: BattleCueKind.Judgment, JudgmentStage: JudgmentCueStage.Requested,
                   JudgmentResolutionId: 10, JudgmentTitle: "乐不思蜀", IsInquiry: true, Label: "乐不思蜀 · 判定中" } &&
               judgmentCues[1] is { SourceSeat: 0, TargetSeats: [1], JudgmentStage: JudgmentCueStage.Replaced,
                   JudgmentResolutionId: 10, JudgmentTitle: "乐不思蜀", IsInquiry: false, Label: "鬼才改判 · ♥Q", Detail: "最终判定牌已替换" } &&
               judgmentCues[2] is { JudgmentStage: JudgmentCueStage.Resolved, JudgmentResolutionId: 10, IsInquiry: false,
                   JudgmentTitle: "乐不思蜀", Label: "乐不思蜀 · ♥Q", Detail: "红桃 · 不跳过出牌阶段" } &&
               judgmentCues[3] is { JudgmentStage: JudgmentCueStage.Resolved, JudgmentResolutionId: 12, IsInquiry: false,
                   JudgmentTitle: "闪电", Label: "闪电 · ♠5", Detail: "黑桃 2–9 · 命中" } &&
               judgmentCues.Select(cue => cue.Sequence).SequenceEqual(judgmentEvents.Select(envelope => envelope.Sequence)),
            "Judgment lifecycle, replacement, card face, or rule outcome was not projected exactly.");
        Assert(judgmentCues.Take(2).All(cue => cue.JudgmentCard is null && !cue.HasJudgmentCard) &&
            judgmentCues[2] is { JudgmentCard: { Id: 41, Kind: CardKind.Peach, Suit: Suit.Heart, Rank: 12, RankText: "Q" } } &&
            judgmentCues[3] is { JudgmentCard: { Id: 43, Kind: CardKind.Slash, Suit: Suit.Spade, Rank: 5, RankText: "5" } } &&
            judgmentView.Players.Single(player => player.Seat == 1).Judgment.Single().Suit == Suit.Spade &&
            judgmentCues.All(cue => cue is { CardKind: null, PublicCardId: null, PublicCard: null }) &&
            judgmentCues.Skip(2).All(cue => cue.HasJudgmentCard && cue.JudgmentCard is { } card &&
                card.DisplayName == CardCatalog.Get(card.Kind).DisplayName) &&
            BattleCueProjector.ProjectCardPlays(judgmentEvents, judgmentView).Count == 0,
            "Only a resolved judgment may publish effective suit/rank; those values cannot become an ordinary physical face or borrow a printed suit from the final zone.");
        var waitingJudgment = BattleCueProjector.Project([judgmentEvents[0]], judgmentView).Single();
        Assert(waitingJudgment is { IsInquiry: true, CardKind: null, PublicCardId: null, PublicCard: null, JudgmentCard: null } &&
            BattleCueProjector.Project(
            [Envelope(new JudgmentReplacementRequestedEvent(13, 10, 1, 0, JudgmentReasons.Indulgence, 40, CardKind.Slash, Suit.Spade)),
             Envelope(new JudgmentReplacementResolvedEvent(13, 10, 1, 0, JudgmentReasons.Indulgence, false,
                 40, null, null, null, null))], judgmentView).Count == 0,
            "A pending judgment or unaccepted private replacement opportunity inferred an initial or hidden hand face.");
        var exhaustedJudgment = BattleCueProjector.Project(
            [Envelope(new JudgmentResolvedEvent(14, 8, 2, JudgmentReasons.BaguaDefense, null, null, null, null, false))], judgmentView).Single();
        Assert(exhaustedJudgment is { Kind: BattleCueKind.Judgment, JudgmentStage: JudgmentCueStage.Resolved,
            JudgmentResolutionId: 14, IsInquiry: false, CardKind: null, PublicCardId: null, PublicCard: null,
            JudgmentTitle: "八卦阵", JudgmentCard: null, HasJudgmentCard: false, Detail: "没有可用的判定牌" } &&
            cues.All(cue => cue.JudgmentStage is null && cue.JudgmentResolutionId is null && cue.JudgmentCard is null && !cue.HasJudgmentCard),
            "An exhausted judgment invented a card face or an unrelated public action acquired a judgment stage.");
        var luoshenCues = BattleCueProjector.Project(
        [Envelope(new JudgmentResolvedEvent(20, 7, 1, JudgmentReasons.Luoshen, 44, CardKind.Slash, Suit.Spade, 3, false)),
         Envelope(new JudgmentResolvedEvent(21, 7, 1, JudgmentReasons.Luoshen, 45, CardKind.Slash, Suit.Club, 4, false)),
         Envelope(new JudgmentResolvedEvent(22, 7, 1, JudgmentReasons.Luoshen, 46, CardKind.Slash, Suit.Heart, 5, true)),
         Envelope(new JudgmentResolvedEvent(23, 7, 1, JudgmentReasons.Luoshen, 47, CardKind.Slash, Suit.Diamond, 6, true))], view);
        Assert(luoshenCues.All(cue => cue is { JudgmentTitle: "洛神", HasJudgmentCard: true }) &&
            luoshenCues.Select(cue => cue.Detail).SequenceEqual(new[]
                { "黑色 · 可继续判定", "黑色 · 可继续判定", "红色 · 结束", "红色 · 结束" }),
            "Luoshen must follow its public black-suit repeat condition, not invert the engine's generic red-suit Succeeded flag or promise another judgment was accepted.");
        var ganglieCues = BattleCueProjector.Project(
        [Envelope(new JudgmentResolvedEvent(24, 7, 1, JudgmentReasons.Ganglie, 48, CardKind.Slash, Suit.Club, 7, false)),
         Envelope(new JudgmentResolvedEvent(25, 7, 1, JudgmentReasons.Ganglie, 49, CardKind.Slash, Suit.Heart, 8, true))], view);
        Assert(ganglieCues.All(cue => cue is { JudgmentTitle: "刚烈", HasJudgmentCard: true, Detail: "判定花色已确定" }),
            "A shared Ganglie reason cannot call the skill successful/failed when its registered variants use different suit conditions.");
        var leijiCues = BattleCueProjector.Project(new[]
            { JudgmentReasons.Leiji, "skill.classic.leiji", "skill.national.leiji", "skill.boundary.leiji" }
            .Select((reason, index) => Envelope(new JudgmentResolvedEvent(26 + index, 7, 1, reason,
                50 + index, CardKind.Slash, Suit.Spade, 9, false))), view);
        Assert(leijiCues.Count == 4 && leijiCues.All(cue => cue is
            { Kind: BattleCueKind.Judgment, JudgmentTitle: "雷击", HasJudgmentCard: true,
                Detail: "判定花色已确定", HpDelta: 0, RemainingHp: null }),
            "A public black Leiji judgment must not claim generic failure or damage/recovery before the subsequent decisions and typed HP facts.");
        var tieqiCues = BattleCueProjector.Project(
        [Envelope(new JudgmentResolvedEvent(30, 7, 1, "skill.slash-response-judgment", 54, CardKind.Slash, Suit.Heart, 10, true)),
         Envelope(new JudgmentResolvedEvent(31, 7, 1, "skill.slash-response-judgment", 55, CardKind.Slash, Suit.Spade, 11, false)),
         Envelope(new JudgmentResolvedEvent(32, 7, 1, "boundary:tieqi", 56, CardKind.Slash, Suit.Heart, 12, true))], view);
        Assert(tieqiCues[0] is { JudgmentTitle: "铁骑", Detail: "红色 · 目标不能使用闪" } &&
            tieqiCues[1] is { JudgmentTitle: "铁骑", Detail: "黑色 · 可正常响应" } &&
            tieqiCues[2] is { Detail: "判定花色已确定" },
            "Only the verified classic Tieqi reason may interpret red as prohibiting Dodge; a later suit-payment decision cannot borrow that result.");
        var responseCues = BattleCueProjector.Project(
        [
            Envelope(new ResponseRequestedEvent(0, 1, CardKind.Slash, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(20, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2)),
            Envelope(new NullificationRequestedEvent(21, 45, CardKind.Duel, 0, 1, false, 0)),
            Envelope(new NullificationRespondedEvent(21, 45, CardKind.Duel, 1, 46, true, 1)),
            Envelope(new NullificationRequestedEvent(21, 45, CardKind.Duel, 0, 2, true, 1)),
            Envelope(new NullificationResolvedEvent(21, 45, CardKind.Duel, true, 1))
        ], view);
        Assert(responseCues.Count == 6 &&
               responseCues[0] is { Kind: BattleCueKind.ResponseWindow, SourceSeat: 1, TargetSeats: [0], Label: "等待闪响应", Detail: "响应【杀】" } &&
               responseCues[1] is { Label: "连续响应 1 / 2", Detail: "仍需 1 张【闪】", ResponseCount: 1,
                   RequiredResponseCount: 2, RequiredResponseCardKind: CardKind.Dodge, ResponseResolutionId: 20, HasResponseProgress: true } &&
               responseCues[2] is { Label: "无懈可击询问中 · 第 1 层", Detail: "当前锦囊生效中 · 可令其失效" } &&
               responseCues[3] is { Kind: BattleCueKind.Response, Label: "打出无懈可击 · 第 1 层", Detail: "锦囊暂时失效" } &&
               responseCues[4] is { Label: "无懈可击询问中 · 第 2 层", Detail: "当前锦囊已失效 · 可反制恢复" } &&
               responseCues[5] is { SourceSeat: -1, Label: "决斗 · 已失效", Detail: "无懈链共 1 次响应" },
            "Response request, locked progress, or layered Nullification state was not projected exactly.");
        Assert(responseCues.Select(cue => cue.IsInquiry).SequenceEqual(new[] { true, false, true, false, true, false }),
            "Response progress, a played response or the final outcome was treated as an unanswered inquiry.");
        foreach (var groupKind in new[] { CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FiveGrains })
        {
            var groupCues = BattleCueProjector.Project(
            [Envelope(new NullificationRequestedEvent(22, 50, groupKind, 0, 1, false, 0) { WindowFrameId = 23, TargetSeat = 2 }),
             Envelope(new NullificationRespondedEvent(22, 50, groupKind, 1, 51, true, 1) { WindowFrameId = 23, TargetSeat = 2 }),
             Envelope(new NullificationResolvedEvent(22, 50, groupKind, true, 1) { WindowFrameId = 23, TargetSeat = 2 })], view);
            Assert(groupCues.Count == 3 && groupCues.All(cue => cue.TargetSeats.SequenceEqual([2]) &&
                cue.Detail!.Contains($"对{view.Players[2].GeneralName}的效果")),
                "Group counterspell inquiries, responses and outcomes must identify only the current recipient.");
        }
        Assert(responseCues.Where(cue => cue.Sequence != responseCues[1].Sequence).All(cue =>
                !cue.HasResponseProgress && cue.ResponseCount == 0 && cue.RequiredResponseCount == 0 &&
                cue.RequiredResponseCardKind is null && cue.ResponseResolutionId is null),
            "An inquiry, played counterspell or final result acquired progress from another public event.");

        // Public event projection only; native paid-response timing is covered by
        // the response fixture. Neither the viewer's prompt nor an incoming attack
        // can turn counted cards into a final immunity or evasion result.
        var projectionState = SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
        var countedResponses = new[]
        {
            Envelope(new CardRespondedEvent(5001, 1, 0, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(60, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2)),
            Envelope(new CardRespondedEvent(5002, 1, 0, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(60, 0, 1, CardKind.Slash, CardKind.Dodge, 2, 2))
        };
        var countedCues = BattleCueProjector.Project(countedResponses, view);
        var dodgeProgress = countedCues.Where(cue => cue.HasResponseProgress).ToArray();
        Assert(countedCues.Count == 4 && dodgeProgress.Length == 2 &&
               dodgeProgress[0] is { Kind: BattleCueKind.ResponseWindow, SourceSeat: 1, TargetSeats: [0],
                   Label: "连续响应 1 / 2", Detail: "仍需 1 张【闪】", ResponseCount: 1, RequiredResponseCount: 2,
                   RequiredResponseCardKind: CardKind.Dodge, ResponseResolutionId: 60, IsInquiry: false } &&
               dodgeProgress[1] is { Label: "连续响应 2 / 2", Detail: "所需【闪】已打齐", ResponseCount: 2,
                   RequiredResponseCount: 2, RequiredResponseCardKind: CardKind.Dodge, ResponseResolutionId: 60,
                   HpDelta: 0, CardKind: null, PublicCardId: null } &&
               BattleCueProjector.ProjectCardPlays(countedResponses, view).Count == 2 &&
               countedCues.Select(cue => cue.Sequence).Distinct().Count() == countedCues.Count,
            "Paid Dodge faces or exact public progress were duplicated, associated with a different group, or promoted to a final evasion result.");
        var slashProgress = BattleCueProjector.Project(
        [
            Envelope(new RequiredResponseProgressEvent(61, 2, 1, CardKind.Duel, CardKind.Slash, 1, 2)),
            Envelope(new RequiredResponseProgressEvent(61, 2, 1, CardKind.Duel, CardKind.Slash, 2, 2))
        ], view);
        Assert(slashProgress.Count == 2 && slashProgress[0] is
                { Detail: "仍需 1 张【杀】", RequiredResponseCardKind: CardKind.Slash, ResponseResolutionId: 61,
                    SourceSeat: 1, TargetSeats: [2], HasResponseProgress: true } &&
               slashProgress[1] is { Detail: "所需【杀】已打齐", ResponseCount: 2, RequiredResponseCount: 2,
                   RequiredResponseCardKind: CardKind.Slash, ResponseResolutionId: 61, HasResponseProgress: true },
            "Consecutive Slash progress used the incoming Duel or a previous Dodge rather than its exact counted card kind.");
        foreach (var (count, required) in new[] { (0, 2), (-1, 2), (3, 2), (2, 0), (2, -1), (int.MaxValue, 2) })
        {
            var invalid = BattleCueProjector.Project(
                [Envelope(new RequiredResponseProgressEvent(62, 0, 1, CardKind.Slash, CardKind.Dodge, count, required))], view).Single();
            Assert(!invalid.HasResponseProgress && invalid.ResponseCount == count && invalid.RequiredResponseCount == required &&
                   invalid.ResponseResolutionId == 62 && invalid.Detail == "已打出【闪】",
                "Invalid public counts invented remaining responses or a completed response result.");
        }
        var progress = dodgeProgress[0];
        Assert(!(progress with { IsInquiry = true }).HasResponseProgress &&
               !(progress with { Kind = BattleCueKind.Response }).HasResponseProgress &&
               !(progress with { RequiredResponseCardKind = null }).HasResponseProgress &&
               !(progress with { RequiredResponseCount = 1 }).HasResponseProgress,
            "Finite response progress escaped its exact non-inquiry multiple-response contract.");

        // These public terminal facts declare an equipment effect, rather than
        // a weapon card play, a private payment preview or a future HP result.
        var weaponEvents = new[]
        {
            Envelope(new StoneAxeResolvedEvent(70, 1, 2, true, [4001, 4002])),
            Envelope(new StoneAxeResolvedEvent(71, 1, 2, false, [])),
            Envelope(new QinglongCrescentBladeResolvedEvent(72, 1, 2, true, [3001])),
            Envelope(new QinglongCrescentBladeResolvedEvent(73, 1, 2, true, [3002, 3003], CardKind.FireSlash)),
            Envelope(new QinglongCrescentBladeResolvedEvent(74, 1, 2, true, [], CardKind.ThunderSlash)),
            Envelope(new QinglongCrescentBladeResolvedEvent(75, 1, 2, false, []))
        };
        var weaponCues = BattleCueProjector.Project(weaponEvents, view);
        Assert(weaponCues.Count == 6 && weaponCues.All(cue => cue.Kind == BattleCueKind.Weapon &&
                cue.SourceSeat == 1 && cue.TargetSeats.SequenceEqual([2]) && !cue.IsInquiry && !cue.HasResponseProgress &&
                cue.CardKind is null && cue.PublicCardId is null && cue.PublicCard is null &&
                cue.HpDelta == 0 && cue.RemainingHp is null && cue.MaximumHp is null) &&
               weaponCues[0] is { WeaponKind: CardKind.StoneAxe, WeaponResolutionId: 70, IsWeaponActivated: true,
                   Label: "贯石斧 · 破闪", Detail: "弃置 2 张牌 · 此杀继续结算" } &&
               weaponCues[1] is { WeaponKind: CardKind.StoneAxe, WeaponResolutionId: 71, IsWeaponActivated: false,
                   Label: "贯石斧 · 未发动", Detail: "此次未发动" } &&
               weaponCues[2] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 72, IsWeaponActivated: true,
                   Label: "青龙偃月刀 · 追击", Detail: "已追加【杀】使用" } &&
               weaponCues[3] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 73, IsWeaponActivated: true,
                   Detail: "已追加【火杀】使用" } &&
               weaponCues[4] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 74, IsWeaponActivated: true,
                   Detail: "已追加【雷杀】使用" } &&
               weaponCues[5] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 75, IsWeaponActivated: false,
                   Label: "青龙偃月刀 · 未发动", Detail: "此次放弃追击" } &&
               BattleCueProjector.ProjectCardPlays(weaponEvents, view).Count == 0,
            "A public weapon result lost its own activation/effective kind, rejected zero-material pursuit, fabricated a weapon play or promised future damage/immunity.");
        Assert(BattleCueProjector.Project(privateEvents.Concat(weaponEvents), view).Where(cue => cue.Kind != BattleCueKind.Draw).Select(cue => cue.Sequence)
                .SequenceEqual(weaponCues.Select(cue => cue.Sequence)) &&
               cues.Concat(responseCues).All(cue => cue.WeaponKind is null && cue.WeaponResolutionId is null && !cue.IsWeaponActivated),
            "Hidden setup/hand movement or another public cue was promoted to an equipment effect.");
        var publicFacts = countedResponses.Concat(weaponEvents).ToArray();
        var factCues = BattleCueProjector.Project(publicFacts, view);
        var foreignCues = BattleCueProjector.Project(publicFacts, Engine(vm).CreateSnapshot(1));
        Assert(factCues.Zip(foreignCues).All(pair => pair.First.Sequence == pair.Second.Sequence &&
                pair.First.Kind == pair.Second.Kind && pair.First.SourceSeat == pair.Second.SourceSeat &&
                pair.First.TargetSeats.SequenceEqual(pair.Second.TargetSeats) && pair.First.Label == pair.Second.Label &&
                pair.First.Detail == pair.Second.Detail && pair.First.ResponseCount == pair.Second.ResponseCount &&
                pair.First.RequiredResponseCount == pair.Second.RequiredResponseCount &&
                pair.First.RequiredResponseCardKind == pair.Second.RequiredResponseCardKind &&
                pair.First.ResponseResolutionId == pair.Second.ResponseResolutionId && pair.First.HasResponseProgress == pair.Second.HasResponseProgress &&
                pair.First.WeaponKind == pair.Second.WeaponKind && pair.First.WeaponResolutionId == pair.Second.WeaponResolutionId &&
                pair.First.IsWeaponActivated == pair.Second.IsWeaponActivated) &&
               factCues.Count == foreignCues.Count && projectionState == SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true)),
            "A foreign safe viewer changed public counted-response/equipment facts or projection mutated the engine.");
        var bulkProgress = Enumerable.Range(0, 40).Select(index =>
            Envelope(new RequiredResponseProgressEvent(100 + index, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2))).ToArray();
        var boundedProgress = BattleCueProjector.Project(bulkProgress, view);
        Assert(boundedProgress.Count == 12 && boundedProgress.Select(cue => cue.Sequence).SequenceEqual(
                bulkProgress.TakeLast(12).Select(envelope => envelope.Sequence)) &&
               boundedProgress.All(cue => cue.HasResponseProgress),
            "Typed progress changed ordinary bulk retention or duplicated a public event.");
        var bulkWeapons = Enumerable.Range(0, 40).Select(index =>
            Envelope(new StoneAxeResolvedEvent(200 + index, 1, 2, true, [4001, 4002]))).ToArray();
        var boundedWeapons = BattleCueProjector.Project(bulkWeapons, view);
        Assert(boundedWeapons.Count == 12 && boundedWeapons.Select(cue => cue.Sequence).SequenceEqual(
                bulkWeapons.TakeLast(12).Select(envelope => envelope.Sequence)) &&
               boundedWeapons.All(cue => cue.Kind == BattleCueKind.Weapon && cue.IsWeaponActivated),
            "Public weapon feedback changed ordinary retention or duplicated a terminal fact.");
        var iFieldView = view with
        {
            Players = view.Players.Select(player => player.Seat == 0
                ? player with
                {
                    Skills = [new GeneralSkillDefinition("I力场", "")
                    { ContentId = "classic:i-field" }]
                }
                : player).ToArray()
        };
        var iFieldCue = BattleCueProjector.Project(
            [Envelope(new WuyanDamagePreventedEvent(22, 1, 0, CardKind.Duel, 1, 0,
                "classic:i-field"))], iFieldView).Single();
        Assert(iFieldCue.Label == "I力场 · 锦囊伤害已防止",
            "Incoming trick prevention must display the skill that actually prevented damage.");
    }

    private static void PublicDraftRevealFeedback(GameSnapshot view)
    {
        var state = new BattleCueProjectionState();
        var sequence = 20000L;
        EventEnvelope E(IGameEvent payload, long revision) =>
            new(new EventId(++sequence), null, sequence, revision, $"public-draft-{revision}", payload);
        var firstFace = new CardSnapshot(9101, CardKind.Slash, Suit.Spade, 7, "杀", "7");
        var extraFace = new CardSnapshot(9102, CardKind.Peach, Suit.Heart, 3, "桃", "3");
        var begin = E(new GroupCardUsedEvent(900, 9100, CardKind.FiveGrains, 0, [0, 1, 2, 3]), 11);
        var first = E(new CardsRevealedEvent(900, [firstFace]), 11);
        var firstCues = BattleCueProjector.Project([begin, first], view, state)
            .Where(cue => cue.Kind == BattleCueKind.PublicReveal).ToArray();
        var extra = E(new CardsRevealedEvent(900, [extraFace]), 14);
        var extraCues = BattleCueProjector.Project([extra], view, state);
        Assert(firstCues is [{ Sequence: var firstSequence, RevealedCardIds: [9101], Revision: 11 }] &&
               firstSequence == first.Sequence &&
               extraCues is [{ Kind: BattleCueKind.PublicReveal, Sequence: var extraSequence,
                   RevealedCardIds: [9102], Revision: 14 }] && extraSequence == extra.Sequence,
            "An extra Five Grains effect round must reveal its new public pool across revisions without repeating GroupCardUsed or the first pool identity.");

        Assert(!BattleCueProjector.Project(
            [E(new GroupCardUsedEvent(901, 9200, CardKind.ArrowBarrage, 0, [1, 2, 3]), 15),
             E(new CardsRevealedEvent(901, [firstFace]), 15),
             E(new CardsRevealedEvent(902, [extraFace]), 15)], view, state)
            .Any(cue => cue.Kind == BattleCueKind.PublicReveal),
            "A reveal from another use or a non-Five-Grains group card must not inherit the previous public draft animation.");
        BattleCueProjector.Project([E(new CardUseFinishedEvent(900, 9100, CardKind.FiveGrains), 16)], view, state);
        Assert(!BattleCueProjector.Project([E(new CardsRevealedEvent(900, [firstFace]), 17)], view, state)
            .Any(cue => cue.Kind == BattleCueKind.PublicReveal),
            "Finishing the owning use must retire its public draft identity.");
        BattleCueProjector.Project([E(new GroupCardUsedEvent(903, 9300, CardKind.FiveGrains, 0, [0, 1]), 18)], view, state);
        state.Clear();
        state.ObservePublicView(view with { PublicRevealedCards = [extraFace] });
        Assert(!BattleCueProjector.Project([E(new CardsRevealedEvent(903, [extraFace]), 19)], view, state)
            .Any(cue => cue.Kind == BattleCueKind.PublicReveal),
            "Cold public faces may seed face lookup, but clearing presentation must not restore an earlier use's reveal animation identity.");
    }

    private static void ProgramActivationFeedback(GameSnapshot view)
    {
        const string skill = "classic:jiefan";
        const string activation = "aid-by-attack-range";
        view = view with
        {
            Players = Array.AsReadOnly(view.Players.Select(player => player.Seat == 0 ? player with
            {
                IsGeneralPublic = true, IsSecondaryGeneralPublic = false,
                Skills = [new GeneralSkillDefinition("解烦", "公开的技能发动检查") { ContentId = skill }]
            } : player).ToArray())
        };
        var hidden = view with
        {
            Players = Array.AsReadOnly(view.Players.Select(player => player.Seat == 0 ? player with
                { IsGeneralPublic = false, IsSecondaryGeneralPublic = false } : player).ToArray())
        };
        var sequence = 10000L;
        var state = new BattleCueProjectionState();
        EventEnvelope E(IGameEvent payload, long revision) => new(new EventId(++sequence), null, sequence, revision, $"revision-{revision}", payload);
        EventEnvelope[] Begin(long frame, long revision, string usage = activation) =>
        [E(new SkillUsageConsumedEvent(0, skill, usage, SkillUsageScope.Game, 1), revision),
         E(new ProgramSkillStartedEvent(frame, 0, skill, activation), revision)];
        EventEnvelope End(long frame, long revision, bool completed = true, string activationId = activation) =>
            E(new ProgramSkillResolvedEvent(frame, 0, skill, activationId, completed), revision);
        IReadOnlyList<BattleCue> Project(IEnumerable<EventEnvelope> events, GameSnapshot? snapshot = null) =>
            BattleCueProjector.Project(events, snapshot ?? view, state);

        Assert(Project(Begin(100, 11)) is [{ Kind: BattleCueKind.Skill, SkillId: skill, Revision: 11 }] &&
               Project([End(100, 14)]).Count == 0,
            "A real Jiefan usage/start must announce once, then suppress only its matching frame completion across committed revisions.");
        var twice = Project(Begin(101, 15).Concat(Begin(102, 15)));
        Assert(twice.Count == 2 && twice.All(cue => cue.Kind == BattleCueKind.Skill && cue.SkillId == skill) &&
               Project([End(101, 16), End(102, 17)]).Count == 0,
            "Distinct program frame identities must keep both genuine activations, including two under the same committed revision.");

        Assert(Project([E(new ProgramSkillStartedEvent(200, 0, skill, activation), 20)]).Count == 0 &&
               Project([End(200, 21)]) is [{ Kind: BattleCueKind.Skill, SkillId: skill }] &&
               Project([End(201, 22), End(202, 22)]).Count == 2,
            "Programs without a published usage payment must retain their actual completion banner and distinct frame identities.");
        Assert(Project(Begin(300, 30), hidden).Count == 0 &&
               Project([End(300, 31)]) is [{ Kind: BattleCueKind.Skill, SkillId: skill }],
            "A usage/start hidden by the public snapshot must not suppress the later first public completion banner.");

        Assert(Project(Begin(400, 40)).Count == 1 &&
               Project([End(400, 41, activationId: "another-activation")]).Count == 1 &&
               Project([End(400, 42, completed: false)]).Count == 0 &&
               Project([End(400, 43)]).Count == 1,
            "Only the full activation identity may consume an announced frame; failed completion must retire it too.");
        Assert(Project(Begin(500, 50)).Count == 1, "The reset fixture must first retain a public activation.");
        state.Clear();
        Assert(Project([End(500, 51)]).Count == 1,
            "Resetting presentation state must not retain an announcement from the previous table or restored session.");

        var crowded = Project(Begin(600, 60).Concat(Enumerable.Range(1, 13)
            .Select(turn => E(new TurnStartedEvent(turn, 0), 60))));
        Assert(crowded.All(cue => cue.Kind != BattleCueKind.Skill) &&
               Project([End(600, 61)]) is [{ Kind: BattleCueKind.Skill, SkillId: skill }],
            "A banner discarded by the bounded output must not be recorded as shown and swallow its later completion.");
        Assert(Project(Begin(700, 70, "shared-usage-group")).Count == 1 &&
               Project([End(700, 71)]).Count == 1,
            "A named usage group without the activation identity must remain unlinked instead of guessing by seat and skill.");
        var sameCommit = Begin(800, 80).Append(End(800, 80)).ToArray();
        Assert(Project(sameCommit) is [{ Kind: BattleCueKind.Skill, SkillId: skill }],
            "A synchronous usage/start/completion must remain one banner after bounded public projection.");
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
