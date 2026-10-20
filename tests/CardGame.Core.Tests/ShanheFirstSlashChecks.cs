using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheFirstSlashChecks
{
    private const string Dangtou = "shanhe:current-dangtou-2036", Dangtou2 = "shanhe:current-dangtou-2037", CommonDangtou = "shanhe:current-dangtou-2038";
    private const string Leihuo = "shanhe:current-leihuo-2100", Leihuo2 = "shanhe:current-leihuo-2101";
    private const string Lianji = "shanhe:current-lianji-2051", Lianji2 = "shanhe:current-lianji-2052", Erlianji = "shanhe:current-lianji-2053";
    private const string Owner = "fixture:first-slash-owner", Peer = "fixture:first-slash-peer", Driver = "fixture:first-slash-driver";
    private const string Watch = "fixture:first-slash-paid", Quiet = "fixture:first-slash-quiet", Virtual = "fixture:first-slash-virtual";
    private const string Gate = "fixture:first-slash-hp-grants", Loss = "fixture:first-slash-before-response-loss", Deck = "fixture:first-slash-deck";
    private const string Counter = "fixture:first-slash-other-actor";
    private static readonly string[] Skills = [Dangtou, Dangtou2, CommonDangtou, Leihuo, Leihuo2, Lianji, Lianji2, Erlianji];
    private static readonly string[] Rewards = ["tactic-dangtou-current-2036", "tactic-dangtou-current-2037", "tactic-dangtou-current-2038",
        "tactic-leihuo-current-2100", "tactic-leihuo-current-2101", "tactic-lianji-current-2051", "tactic-lianji-current-2052", "tactic-lianji-current-2053"];
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-first-slash-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('a', 64))
        { TacticSkillIds = Skills }));

    public static void DeclarationsCountMissResponsesConversionsAndVirtualUsesCold()
    {
        VerifyLoaderBoundaries();
        var s = Scene.Start("response");
        var duel = s.BeginCard(CardKind.Duel, [1]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var response = s.V().Hand.First(card => card.Kind == CardKind.Slash).Id;
        s.Cold(); s.Answer(choice => choice.Cards.SequenceEqual([response])); s.Play();
        Need(s.Events<DuelResponseEvent>().Any(e => e.ResponderSeat == 0 && e.UsedSlash && e.SlashCardId == response) &&
            s.SlashUses().Length == 0 && s.Modifiers().Length == 0 && s.Events<DamageAppliedEvent>() is [{ Amount: 1, TargetSeat: 1 }],
            "A real paid Slash in a native Duel response is not a use, and the actual non-Slash Duel receives neither tactic.");
        s.AssertPaidOnce(duel); s.AssertPaidOnce(response); s.Cold();
        var beforeHits = s.Events<DamageAppliedEvent>().Length;
        var missed = s.BeginCard(CardKind.Slash, [1]); s.Play();
        Need(s.SlashUses() is [{ ActorSeat: 0, EffectiveCardKind: CardKind.Slash }] &&
            s.Events<DamageAppliedEvent>().Length == beforeHits && s.Modifiers().Length == 0 &&
            s.G.CardMovements.Any(move => move.From == CardLocation.Hand(1) && move.To == CardLocation.Processing &&
                s.G.CreateCardZoneDiagnostics().Single(card => card.CardId == move.CardId).CardKind == CardKind.Dodge),
            "The first real physical Slash is genuinely dodged, but its declaration already consumes the round's first-use position.");
        s.AssertPaidOnce(missed); s.Cold();
        s.BeginCard(CardKind.FireSlash, [2]); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { TargetSeat: 2, Amount: 2 } &&
            s.Modifiers() is [{ Source.SkillId: Leihuo, BaseAmount: 1, ModifiedAmount: 2 }] && s.SlashUses().Length == 2,
            "The following first elemental Slash gets only Lei Huo: the missed ordinary Slash consumed Dangtou but not the elemental-use position.");
        s.Cold(); s.Continue(); s.Play(); s.Use("hit", [2]);
        Need(s.Events<DamageAppliedEvent>().Last() is { Amount: 1, TargetSeat: 2 } && s.Modifiers().Length == 1,
            "A genuine program Damage instruction cannot manufacture an owner-used Slash identity."); s.Cold();

        var converted = Scene.Start("converted");
        var material = converted.BeginCard(CardKind.Dodge, [2], converted: true); converted.AtDamage();
        Need(converted.Events<DamageAppliedEvent>() is [{ Amount: 2, TargetSeat: 2 }] &&
            converted.SlashUses() is [{ EffectiveCardKind: CardKind.Slash, ActorSeat: 0 }] &&
            converted.Modifiers() is [{ Source.SkillId: Dangtou, BaseAmount: 1, ModifiedAmount: 2 }] &&
            converted.Events<CardActionAcceptedEvent>().Any(e => e.Action is { ActorSeat: 0, EffectiveKind: CardKind.Slash } action &&
                action.ConversionChain.Any(source => source.SkillId == "classic:wusheng")),
            "Actual Wusheng pays a red Dodge and declares its effective Slash, not its printed material kind, as the first round Slash.");
        converted.Cold(); converted.Continue(); converted.Play(); converted.PlayCard(CardKind.Slash, [2]);
        Need(converted.Events<DamageAppliedEvent>().Last().Amount == 1 && converted.Modifiers().Length == 1,
            "A later printed Slash cannot reuse the converted Slash's already-consumed first-use position."); converted.AssertPaidOnce(material); converted.Cold();

        var virtualUse = Scene.Start("virtual");
        virtualUse.Answer(choice => choice.Targets.SequenceEqual([2])); virtualUse.AtDamage();
        Need(virtualUse.SlashUses() is [{ ActorSeat: 0, EffectiveCardKind: CardKind.Slash }] &&
            virtualUse.Events<CardUseDeclaredEvent>() is [{ SourceSeat: 0, CardKind: CardKind.Slash, CardId: 0 }] &&
            virtualUse.Events<DamageAppliedEvent>() is [{ Amount: 2, TargetSeat: 2 }] && virtualUse.Modifiers().Single().Source.SkillId == Dangtou,
            "A genuinely issued zero-material virtual Slash owns its first-use declaration and receives the same production bonus.");
        virtualUse.Cold(); virtualUse.Continue(); virtualUse.Play(); virtualUse.PlayCard(CardKind.Slash, [2]);
        Need(virtualUse.Events<DamageAppliedEvent>().Last().Amount == 1 && virtualUse.SlashUses().Length == 2 &&
            virtualUse.Modifiers().Length == 1, "A virtual first Slash consumes the position before the following physical Slash."); virtualUse.Cold();
        VerifyNativeFireConversionDiffersFromDamageNature();
        VerifyCommittedLihuoChangesTheSameUseWithoutRewritingItsDeclaration();
    }

    public static void RoundAndActorTurnOrdinalsRemainStableAcrossNativeBoundariesCold()
    {
        var s = Scene.Start("turns"); var round = s.Round; var firstTurn = s.G.State.TurnNumber;
        s.PlayCard(CardKind.FireSlash, [2]); s.PlayCard(CardKind.ThunderSlash, [2]);
        Need(s.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual([3, 1]),
            "The first actual elemental Slash receives both independent +1 sources; a second elemental use in the same turn receives neither.");
        s.Use("extra-turn"); s.EndPlay(); s.Play();
        Need(s.G.State.TurnNumber > firstTurn && s.Round == round && s.Events<TurnStartedEvent>().Last().ActorSeat == 0,
            "The existing extra-turn instruction starts another real owner turn while preserving the current round.");
        s.Cold(); s.PlayCard(CardKind.FireSlash, [2]);
        Need(s.Events<DamageAppliedEvent>().Last().Amount == 2 && s.Modifiers().Last().Source.SkillId == Leihuo &&
            s.Modifiers().Count(e => e.Source.SkillId == Dangtou) == 1,
            "A new actual turn resets Lei Huo, while the earlier round's Dangtou remains consumed.");
        s.EndPlay(); s.Play();
        Need(s.Round > round && s.Events<TurnStartedEvent>().Last().ActorSeat == 0,
            "The bounded native quiet opponents finish their turns and reach the next genuine round, without simulating a match.");
        s.Cold(); s.PlayCard(CardKind.FireSlash, [2]);
        Need(s.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual([3, 1, 2, 3]) &&
            s.Modifiers().Count(e => e.Source.SkillId == Dangtou) == 2 && s.Modifiers().Count(e => e.Source.SkillId == Leihuo) == 3,
            "The real round and actual-turn boundaries reset their respective declarations independently."); s.Cold();

        var multi = Scene.Start("multi"); var card = multi.BeginCard(CardKind.FireSlash, [1, 2]);
        multi.AtDamage(); var first = multi.SlashUses().Single();
        Need(multi.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 5 }], "The first recipient of one real two-target Fire Slash receives both +2 versions.");
        multi.Cold(); multi.Continue(); multi.AtDamage();
        Need(multi.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 5), (2, 5)]) &&
            multi.SlashUses() is [var same] && same == first && multi.Modifiers().Length == 4 &&
            multi.Modifiers().All(e => e.ResolutionId == first.OriginFrameId),
            "Returning from a first-recipient child does not advance this same use's ordinal or steal its bonus from the second recipient.");
        multi.Cold(); multi.Continue(); multi.Play(); multi.AssertPaidOnce(card); multi.PlayCard(CardKind.ThunderSlash, [2]);
        Need(multi.Events<DamageAppliedEvent>().Last().Amount == 1 && multi.Modifiers().Length == 4,
            "The next distinct use is ineligible even though the preceding one resolved multiple targets."); multi.Cold();

        var loss = Scene.Start("loss"); var issued = loss.BeginCard(CardKind.FireSlash, [2]); loss.AtSkill(Loss);
        Need(loss.SlashUses() is [{ EffectiveCardKind: CardKind.FireSlash }] && loss.V().Hp == 19 &&
            loss.Events<HpConditionalSkillGrantChangedEvent>().Count(e => !e.Added && e.OwnerSeat == 0 && e.SkillId is Dangtou or Leihuo) == 2,
            "The paid first Slash already owns its declaration before a legitimate response child withdraws both HP-derived sources.");
        loss.Cold(); loss.Continue(); loss.AtDamage();
        Need(loss.Events<DamageAppliedEvent>() is [{ Amount: 1 }] && loss.Modifiers().Length == 0,
            "A source lost before damage finalization cannot contribute merely because it was active at declaration.");
        loss.Cold(); loss.Continue(); loss.Play(); loss.Use("recover-one"); loss.PlayCard(CardKind.FireSlash, [2]);
        Need(loss.V().Hp == 20 && loss.Events<DamageAppliedEvent>().Last().Amount == 1 && loss.Modifiers().Length == 0 &&
            loss.SlashUses().Length == 2, "Restoring the real sources does not erase either already-issued first-use position or refund the original cost.");
        loss.AssertPaidOnce(issued); loss.Cold();
        VerifyNestedQinglongRetainsTheParentsIssuedOrdinal();
        VerifyOtherActorsUseTheirOwnRoundPrefix();
    }

    private static void VerifyNativeFireConversionDiffersFromDamageNature()
    {
        var fan = Scene.Start("fan"); var weapon = fan.PlayCard(CardKind.ZhuqueFan);
        var action = fan.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]) &&
            a.PlayedCardKind == CardKind.FireSlash && a.CardId is { } id && fan.V().Hand.Any(card => card.Id == id && card.Kind == CardKind.Slash));
        var slash = fan.Begin(action); fan.AtDamage();
        Need(fan.SlashUses() is [{ EffectiveCardKind: CardKind.FireSlash }] && fan.Events<DamageAppliedEvent>() is [{ Nature: DamageNature.Fire, Amount: 3 }] &&
            fan.Modifiers().Select(e => e.Source.SkillId).ToHashSet().SetEquals([Dangtou, Leihuo]),
            "A real equipped Zhuque Fan changes an original ordinary Slash into effective FireSlash before declaration, so it genuinely qualifies for Lei Huo.");
        fan.Cold(); fan.Continue(); fan.Play(); fan.AssertPaidOnce(slash);
        Need(fan.G.CardMovements.Count(m => m.CardId == weapon && m.To == CardLocation.Equipment(0)) == 1,
            "The conversion is supplied by an actually paid and equipped Fan, not a changed test enum."); fan.Cold();

        var zither = Scene.Start("zither"); zither.PlayCard(CardKind.CrimsonSoulZither); zither.BeginCard(CardKind.Slash, [2]); zither.AtDamage();
        Need(zither.SlashUses() is [{ EffectiveCardKind: CardKind.Slash }] && zither.Events<DamageAppliedEvent>() is [{ Nature: DamageNature.Fire, Amount: 2 }] &&
            zither.Modifiers() is [{ Source.SkillId: Dangtou }],
            "The real Crimson Soul Zither changes damage nature while the declared card remains ordinary Slash, so it neither consumes nor receives Lei Huo.");
        zither.Cold(); zither.Continue(); zither.Play(); zither.PlayCard(CardKind.ThunderSlash, [2]);
        Need(zither.Events<DamageAppliedEvent>().Last().Amount == 2 && zither.Modifiers().Last().Source.SkillId == Leihuo,
            "The subsequent genuinely elemental card is still this actual turn's first elemental Slash after ordinary fire-nature damage."); zither.Cold();
    }

    private static void VerifyCommittedLihuoChangesTheSameUseWithoutRewritingItsDeclaration()
    {
        var s = Scene.Start("lihuo"); var card = s.BeginCard(CardKind.Slash, [2]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "boundary:lihuo" && s.P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var declared = s.SlashUses().Single();
        Need(declared.EffectiveCardKind == CardKind.Slash && s.Events<ProgramCurrentSlashFireChangedEvent>().Length == 0,
            "The real ordinary Slash is already declared before the production committed-use Li Huo offer.");
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "current-slash-fire") == true);
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("branch") == "convert" && choice.Targets.Count == 0); s.AtDamage();
        Need(s.Events<ProgramCurrentSlashFireChangedEvent>() is [{ OriginalKind: CardKind.Slash, FinalKind: CardKind.FireSlash, ExtraTargetSeat: null } changed] &&
            changed.CardUseFrameId == declared.OriginFrameId && changed.ActionId == declared.CardActionId &&
            s.SlashUses() is [var unchanged] && unchanged == declared &&
            s.Events<DamageAppliedEvent>() is [{ Amount: 3, TargetSeat: 2, Nature: DamageNature.Fire }] && s.Modifiers().Length == 2,
            "The same committed use follows its exact typed Slash-to-FireSlash evolution, receiving both first-use bonuses without manufacturing a second declaration or mutating the original fact.");
        s.Cold(); s.Continue();
        s.Reach(() => s.P?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "completed-use-payment") == true);
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("branch") == "lose-hp"); s.Play();
        Need(s.V().Hp == 19 && s.Events<ProgramCompletedUsePaymentEvent>() is [{ OwnerSeat: 0, HpLost: 1, DiscardedCardId: null }] &&
            s.Events<ProgramCurrentSlashFireChangedEvent>().Length == 1,
            "The genuine converted-use completion pays one real HP cost once after its damage children, rather than being silently skipped by the fixture.");
        s.AssertPaidOnce(card); s.Cold(); s.BeginCard(CardKind.ThunderSlash, [2]); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last().Amount == 1 && s.Modifiers().Length == 2,
            "The following natural elemental Slash counts the earlier typed Li Huo conversion in its actual-turn prefix and cannot receive Lei Huo again.");
        s.Cold(); s.Continue(); s.Play(); s.Cold();
    }

    private static void VerifyNestedQinglongRetainsTheParentsIssuedOrdinal()
    {
        var s = Scene.Start("nested"); s.PlayCard(CardKind.QinglongCrescentBlade);
        var parentCard = s.BeginCard(CardKind.FireSlash, [1, 2]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade });
        var parent = s.SlashUses().Single(); var childCard = s.V().Hand.First(card => card.Kind == CardKind.Slash).Id;
        Need(s.Events<DamageAppliedEvent>().Length == 0 && s.P!.IsPrivate && s.G.CreateSnapshot(1).PendingDecision is null,
            "The first recipient's actual Dodge opens the existing private Qinglong follow-up before the parent's later recipient.");
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("action") == "qinglong-slash" && choice.Cards.SequenceEqual([childCard])); s.AtDamage();
        Need(s.SlashUses().Select(e => e.EffectiveCardKind).SequenceEqual([CardKind.FireSlash, CardKind.Slash]) &&
            s.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 1 }] && s.Modifiers().Length == 0 &&
            s.Events<QinglongCrescentBladeResolvedEvent>().Count(e => e.Used && e.SlashCardIds.SequenceEqual([childCard])) == 1,
            "The nested real physical Slash is declared second and completes first without borrowing its parent's first-round bonus.");
        s.Cold(); s.Continue(); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 1), (2, 5)]) &&
            s.Modifiers().Length == 2 && s.Modifiers().All(e => e.ResolutionId == parent.OriginFrameId && e.TargetSeat == 2) &&
            s.SlashUses().First() == parent,
            "After the nested use expands current history, the resumed parent's second recipient still reads its issued first-Use prefix for both +2 bonuses.");
        s.Cold(); s.Continue(); s.Play(); s.AssertPaidOnce(parentCard); s.AssertPaidOnce(childCard);
        Need(s.Events<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0 && e.Debit.Query == SkillRuleQuery.SlashLimit) == 1,
            "Qinglong's genuine follow-up remains a separate declaration but does not pay the ordinary Play Slash quota twice."); s.Cold();
    }

    private static void VerifyOtherActorsUseTheirOwnRoundPrefix()
    {
        var s = Scene.Start("actor");
        s.Accept(new UseProgramSkillCommand(0, Driver, "hit", [], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("response") == "take-damage"); s.Play();
        Need(s.Events<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.ActorSeat == 1 && e.EffectiveCardKind == CardKind.Slash && e.RoundActionType == CardActionType.Use) &&
            s.Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 2) && s.SlashUses().Length == 0,
            "Another holder genuinely uses its own first virtual Slash during this actor's actual turn; its declaration belongs to that source, not to everyone in the round.");
        s.BeginCard(CardKind.FireSlash, [2]); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 2, Amount: 3 } &&
            s.Modifiers().Count(e => e.Source.OwnerSeat == 0) == 2 && s.Modifiers().Count(e => e.Source.OwnerSeat == 1) == 1,
            "The current player still has both of its own first-use bonuses after the other actor's native declaration."); s.Cold(); s.Continue(); s.Play(); s.Cold();
    }

    public static void CardLayerBonusesPrecedeReplacementsAndDoNotRepeatOnChainsCold()
    {
        var s = Scene.Start("chain"); s.PlayCard(CardKind.IronChain, [1, 2]); s.PlayCard(CardKind.IronChain, [3]);
        Need(new[] { 1, 2, 3 }.All(seat => s.V(seat).IsChained), "Two actual paid Iron Chains link exactly the three intended recipients.");
        s.Use("armor-two", [1]); s.PlayCard(CardKind.Alcohol); var action = s.Action(CardKind.FireSlash, [1]); var before = s.State();
        var prediction = s.G.PredictPublicActionDamage(0, action, 1); var unchanged = s.State() == before;
        Need(prediction is { DamageAmount: 6, ArmorAbsorbed: 2, HpLossAmount: 4 } && unchanged,
            "A pure real-action preview includes Alcohol plus the two production card-layer bonuses before Armor, without consuming first use: " +
            JsonSerializer.Serialize(new { Prediction = prediction, Unchanged = unchanged,
                Action = new { action.Kind, action.CardId, action.PlayedCardKind, action.TargetSeats, action.ConversionSource },
                Physical = s.V().Hand.Single(card => card.Id == action.CardId), s.V().HasAlcoholEffect, TargetArmor = s.V(1).Armor,
                Actual = s.Diagnostic() }));
        var card = s.Begin(action);
        foreach (var target in new[] { 1, 2, 3 })
        {
            s.AtDamage(); Need(s.Events<DamageAppliedEvent>().Last() is { Amount: 6, Nature: DamageNature.Fire } hit && hit.TargetSeat == target,
                "Each native propagated recipient retains the origin's complete six-point card layer: " + s.Diagnostic());
            if (target == 1) Need(s.Events<DamageAppliedEvent>().Last().ArmorPayment is { ArmorBefore: 2, ArmorAbsorbed: 2, HpLossAmount: 4 } && s.V(1).Hp == 16,
                "The original damage still equals six while actual Armor pays two and HP pays four.");
            Need(s.Modifiers().Length == 2 && s.Modifiers().All(e => e.TargetSeat == 1 && e.ModifiedAmount == e.BaseAmount + 2),
                "The two owner-used card-layer modifiers issue only for the origin, never again on an elemental propagation recipient.");
            s.Cold(); s.Continue();
        }
        s.Play(); Need(s.Events<AfterDamageEvent>().Select(e => e.Amount).SequenceEqual([6, 6, 6]) &&
            s.Events<ChainedDamagePropagatedEvent>().Length == 2 && s.SlashUses().Length == 1 && s.Events<RecipientScopedDamageBaseEvent>().Length == 0,
            "The native unscoped chain inherits the finalized first card layer without creating a per-recipient first-use counter or scope."); s.AssertPaidOnce(card); s.Cold();

        foreach (var scene in new[] { "jueqing", "maximum" })
        {
            var r = Scene.Start(scene); r.Use("armor-four", [2]); r.PlayCard(CardKind.Alcohol); var paid = r.PlayCard(CardKind.FireSlash, [2]);
            Need(r.V(2) is { Hp: 14, Armor: 4 } && r.Events<DamageAppliedEvent>().Length == 0 && r.Events<AfterDamageEvent>().Length == 0 &&
                r.Modifiers().Length == 0 &&
                (scene == "jueqing" ? r.V(2).MaxHp == 20 && r.Events<DamageReplacedWithHpLossEvent>() is [{ Amount: 6, TargetSeat: 2 }] :
                    r.V(2).MaxHp == 14 && r.Events<CardDamageReplacedWithMaximumHpLossEvent>() is [{ Receipt.Amount: 6, Receipt.TargetSeat: 2 }]),
                "The real card-layer bonuses are retained by Jueqing or the first-round maximum-HP replacement before Armor, with no invented real damage or after-damage trigger.");
            r.AssertPaidOnce(paid); r.Cold();
        }
    }

    public static void EightProductionTacticsGrantIndependentBonusesAndNativeSlashQuotaCold()
    {
        VerifyActualLoadoutsAndEditionIsolation();
        var stacked = Scene.Start("stacked"); stacked.BeginCard(CardKind.FireSlash, [2]); stacked.AtDamage();
        Need(stacked.Events<DamageAppliedEvent>() is [{ Amount: 8, TargetSeat: 2 }] && stacked.Modifiers().Length == 5 &&
            stacked.Modifiers().Select(e => e.Source.SkillId).ToHashSet().SetEquals(Skills.Take(5)) &&
            stacked.Modifiers().Select(e => e.Source.SkillInstanceId).Distinct().Count() == 5,
            "All five independently named production damage versions coexist: three Dangtou bonuses 1/2/1 and two Lei Huo bonuses 1/2 add seven to the first actual elemental Slash.");
        stacked.Cold(); stacked.Continue(); stacked.Play();
        var quota = Scene.Start("quota");
        for (var index = 0; index < 6; index++)
        {
            var paid = quota.PlayCard(CardKind.Slash, [2]); quota.AssertPaidOnce(paid); quota.Cold();
        }
        var debits = quota.Events<CardUseDebitRecordedEvent>().Where(e => e.Debit.ActorSeat == 0 && e.Debit.Query == SkillRuleQuery.SlashLimit).ToArray();
        Need(debits.Length == 6 && debits.Select(e => e.Debit.PhaseInstanceId).Distinct().Count() == 1 &&
            quota.Events<DamageAppliedEvent>().Length == 6 && quota.Modifiers().Length == 0 && !quota.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The three actual production Lianji skills grant 1+3+1 additional native uses in one real Play, rather than just describing those numbers in JSON.");
        var remaining = quota.V().Hand.First(c => c.Kind == CardKind.Slash).Id; var before = quota.State();
        var refused = quota.G.Submit(new PlayCardCommand(0, remaining, [2], quota.G.Revision, quota.P!.PromptId, CardKind.Slash));
        Need(!refused.Accepted && quota.State() == before && quota.Events<CardUseDebitRefundedEvent>().Length == 0,
            "The seventh actual material is still held and its over-quota command is atomically refused, without refunding or repeating the first six payments."); quota.Cold();
        VerifyActualGaoshunPindianDoesNotConsumeFirstSlash();
    }

    private static void VerifyActualGaoshunPindianDoesNotConsumeFirstSlash()
    {
        const string general = "shanhe:s5-gaoshun-xianzhen", contest = "boundary:xianzhen-current";
        ShanheStartingCard[] fixedCards = [new(CardKind.Alcohol, Suit.Spade, 1), new(CardKind.FireSlash, Suit.Heart, 9),
            new(CardKind.Slash, Suit.Club, 3), new(CardKind.ThunderSlash, Suit.Spade, 5)];
        var setup = new ShanheBattleSetup(83, 1, "fixture-real-gaoshun-first-slash", general, 4, 0, 0, 0, 0, [], fixedCards, [],
            [new("classic:gao-shun", MaxHpBonus: 20) { StartingHand = [new(CardKind.Slash, Suit.Club, 2), new(CardKind.Slash, Suit.Spade, 2)] }], [], new string('f', 64))
        { TacticSkillIds = [Dangtou2, Leihuo2, Lianji2] };
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var game = StandardShanheCampaignCatalog.CreateBattle(setup);
        T[] Facts<T>() => game.Events.Select(item => item.Payload).OfType<T>().ToArray();
        PlayerSnapshot View(int seat) => game.CreateSnapshot(seat).Players[seat];
        void Accept(GameCommand command)
        {
            var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "The actual Gaoshun encounter command was rejected.");
        }
        void Cold()
        {
            var before = FullState(game);
            game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Need(FullState(game) == before, "Cold production Gaoshun retains the original Pindian cards, independent tactic grants and native first-use declarations.");
        }
        void Play()
        {
            for (var step = 0; step < 180; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
                Need(prompt?.PlayerSeat != 0 && game.State.Winner == Winner.None,
                    "Unexpected actual Gaoshun boundary: " + JsonSerializer.Serialize(new { prompt?.Kind, prompt?.PlayerSeat, Skill = prompt?.SkillPrompt?.SkillId,
                        Frames = game.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) }));
                Accept(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("The bounded actual Gaoshun Pindian return was not reached.");
        }
        Accept(new StartGameCommand()); Play();
        Need(View(0) is { GeneralId: general, Hp: 4, MaxHp: 4 } && View(0).Skills?.Any(skill => skill.ContentId == contest) == true &&
            View(0).Skills?.Any(skill => skill.ContentId == "boundary:jinjiu-current") == true && View(0).HandCount >= 8 &&
            View(1) is { Hp: 24, MaxHp: 24, HandCount: 2 } && game.CreateSnapshot(1).Players[1].Hand.All(card => card.Kind == CardKind.Slash && card.Rank == 2),
            "The actual production alias enters with its real two printed programs, normal random starting hand plus four physical recipe cards, against a fixed low-rank healthy Pindian opponent.");
        var wine = View(0).Hand.Last(card => card.Kind == CardKind.Alcohol && card.Suit == Suit.Spade && card.Rank == 1).Id;
        var fire = View(0).Hand.Last(card => card.Kind == CardKind.FireSlash && card.Suit == Suit.Heart && card.Rank == 9).Id;
        Need(game.GetHumanLegalActions().Any(action => action.CardId == wine && action.Kind == LegalActionKind.Slash && action.PlayedCardKind == CardKind.Slash) &&
            game.CreateCardZoneDiagnostics().Single(card => card.CardId == wine).CardKind == CardKind.Alcohol,
            "The printed Spade-one Alcohol really has Jinjiu's playable Slash identity; it is not a preassigned physical King or a substituted test card.");
        Cold(); Accept(new UseProgramSkillCommand(0, contest, "contest", [wine], [1], game.Revision, game.PendingDecision!.PromptId));
        Need(game.ResolutionStack.Last() is PindianFrame && game.CreateSnapshot(1).PendingDecision is { IsPrivate: true } &&
            game.CreateSnapshot(0).PendingDecision is null && game.CreateSnapshot(0).PublicRevealedCards.Count == 0,
            "Only the true NPC opponent sees the still-private native Pindian selection; the test never answers for it.");
        Cold(); Play();
        Need(Facts<PindianResultDeterminedEvent>() is [{ SkillId: contest, Result: { SourceSeat: 0, OpponentSeat: 1,
                SourceRank: 13, OpponentRank: 2, SourceWon: true } result }] && result.SourceCardId == wine &&
            Facts<CurrentTurnCardUseKindsRecordedEvent>().All(use => !IsSlash(use.EffectiveCardKind)) &&
            Facts<CardUseDeclaredEvent>().Length == 0 && Facts<CardUseDebitRecordedEvent>().Length == 0 &&
            game.CardMovements.Count(move => move.CardId == wine && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.PindianReveal) == 1 &&
            game.CardMovements.Count(move => move.CardId == wine && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.PindianFinish) == 1,
            "Native Jinjiu makes the printed low-point Alcohol count as King in a real winning Pindian, whose unique reveal/discard is not a Slash use or a quota debit.");
        Cold();
        var fireActions = game.GetHumanLegalActions().Where(action => action.CardId == fire).ToArray();
        var slash = fireActions.FirstOrDefault(action => action.TargetSeats.SequenceEqual([1]) && action.Kind == LegalActionKind.Slash &&
            (action.PlayedCardKind ?? View(0).Hand.Single(card => card.Id == fire).Kind) == CardKind.FireSlash && action.ConversionSource is null);
        Need(slash is not null, "The original fixed FireSlash must have a genuine published original-opponent action after winning Pindian: " +
            JsonSerializer.Serialize(new { FireId = fire, Physical = View(0).Hand.SingleOrDefault(card => card.Id == fire),
                Actions = fireActions.Select(action => new { action.Kind, action.CardId, action.TargetSeats, action.PlayedCardKind, action.ConversionSource }),
                Prompt = game.PendingDecision?.Kind, Pindian = Facts<PindianResultDeterminedEvent>(),
                Granted = Facts<OriginalTargetAdditionGrantedEvent>(), Uses = Facts<CurrentTurnCardUseKindsRecordedEvent>() }));
        var published = slash!;
        Accept(new PlayCardCommand(0, published.CardId!.Value, published.TargetSeats, game.Revision, game.PendingDecision!.PromptId, published.PlayedCardKind)
            { ConversionSource = published.ConversionSource, AdditionalConversionSources = published.AdditionalConversionSources });
        Cold(); Play();
        var modifiers = Facts<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId is Dangtou2 or Leihuo2).ToArray();
        Need(Facts<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 5, Nature: DamageNature.Fire }] && View(1).Hp == 19 &&
            Facts<CurrentTurnCardUseKindsRecordedEvent>().Where(use => IsSlash(use.EffectiveCardKind)).ToArray() is [{ ActorSeat: 0, EffectiveCardKind: CardKind.FireSlash }] &&
            modifiers.Length == 2 && modifiers.Select(e => e.Source.SkillId).ToHashSet().SetEquals([Dangtou2, Leihuo2]) &&
            modifiers.All(e => e.ModifiedAmount == e.BaseAmount + 2 && e.Source.SkillInstanceId == "campaign:shanhe-tactic:" + e.Source.SkillId) &&
            game.CardMovements.Count(move => move.CardId == fire && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Count(move => move.CardId == fire && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
            "The subsequent original Fire Slash is still the first actual use: two real campaign-source +2 modifiers make five damage after the non-use Pindian, with one physical payment and cleanup.");
        Cold();
    }

    private static void VerifyActualLoadoutsAndEditionIsolation()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        var current = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        Need(Rewards.All(id => current.Rewards.Any(reward => reward.Id == id)) && old.Rewards.All(reward => !Rewards.Contains(reward.Id)),
            "All eight source versions enter only the current edition, with independent rewards rather than a same-name upgrade alias.");
        var defaults = StandardShanheCampaignCatalog.GetDefaultCodexUnlockedRewardIds(current);
        Need(Rewards.Where(id => id != Rewards[4] && id != Rewards[6]).All(defaults.Contains) &&
            !defaults.Contains(Rewards[4]) && !defaults.Contains(Rewards[6]), "Only the six non-tree versions are available by default; Lei Huo II and Lianji II retain their actual tree requirements.");
        foreach (var indexes in new[] { new[] { 0, 1, 2, 3, 4, 5 }, new[] { 6, 7 } })
        {
            var ids = indexes.Select(index => Rewards[index]).ToArray(); var skills = indexes.Select(index => Skills[index]).ToArray();
            var run = ShanheCampaignSession.Create(current, new(83, "classic:guan-yu") { EquippedTacticIds = ids, UnlockedRewardIds = ids },
                StandardShanheCampaignCatalog.CreateBattleRegistry);
            void ColdRun()
            {
                var before = JsonSerializer.Serialize(run.Snapshot);
                run = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(run.CreateCheckpoint())), current, StandardShanheCampaignCatalog.CreateBattleRegistry);
                Need(JsonSerializer.Serialize(run.Snapshot) == before, "The actual at-most-six loadout and issued route survive cold campaign replay.");
            }
            ColdRun();
            for (var step = 0; step < 6 && run.Snapshot.Phase != ShanheCampaignPhase.Battle; step++)
            {
                ShanheCampaignCommand command = run.Snapshot.Phase == ShanheCampaignPhase.Map
                    ? new MoveShanheNodeCommand(run.Snapshot.AvailableNodes[0].Id, run.Revision)
                    : new ChooseShanheNodeOptionCommand(run.Snapshot.NodeChoices.First(choice => choice.RewardId is { } id &&
                        current.Rewards.Single(reward => reward.Id == id).TacticSkillId is null && choice.GoldCost <= run.Snapshot.Gold && choice.HpCost < run.Snapshot.Hp).Id, null, run.Revision);
                var result = run.Submit(command); Need(result.Accepted, result.Error ?? "The actual current-edition map command failed."); ColdRun();
            }
            var setup = run.PendingBattleSetup!;
            Need(setup.TacticSkillIds.SequenceEqual(skills) && setup.ExtraSkillIds.Count == 0 && run.Snapshot.TacticIds.SequenceEqual(ids),
                "Real loadout, opening choice and map commands issue exactly the equipped tactics, independently of extra-skill slots and validation-probe contents.");
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            var started = game.Submit(new StartGameCommand()); Need(started.Accepted, started.Error?.Message ?? "The equipped encounter did not start.");
            Need(skills.All(skill => game.CreateSnapshot(0).Players[0].Skills?.Count(value => value.ContentId == skill) == 1) &&
                game.CreateCheckpoint().Options.ShanheBattle!.TacticSkillIds.SequenceEqual(skills),
                "Actual production encounter startup installs each equipped production skill once and keeps the ordered frozen recipe.");
            var before = FullState(game);
            game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Need(FullState(game) == before, "The actual production battle grants, startup cards and owning frames restore without another initial acquisition.");
        }
    }

    private static string Resource(string suffix)
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static void VerifyLoaderBoundaries()
    {
        var rules = Resource("shanhe-current-slash-tactics.rules.json"); var presentation = Resource("shanhe-current-slash-tactics.presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var id in new[] { Dangtou, Leihuo })
        {
            void Reject(Action<JsonObject> change)
            {
                var copy = JsonNode.Parse(rules)!; var modifier = copy["skills"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == id)!["damageModifiers"]![0]!.AsObject();
                change(modifier); var rejected = false;
                try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
                Need(rejected, "The shared first-Slash condition rejects an absent/invalid ordinal, wrong scope/layer/family or unrelated condition field.");
            }
            Reject(m => m.Remove("maximumUseOrdinal")); Reject(m => m["maximumUseOrdinal"] = 0); Reject(m => m["maximumUseOrdinal"] = 21);
            Reject(m => m["sourceScope"] = "damageSource"); Reject(m => m["damageLayer"] = "additionalDamage");
            Reject(m => m["cardKinds"] = new JsonArray("slash")); Reject(m => m["condition"] = "always");
        }
    }
    private static bool IsSlash(CardKind? kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string FullState(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Facts = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
        game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public int Round => Events<RoundStartedEvent>().Last().RoundNumber;
        public T[] Events<T>() => G.Events.Select(item => item.Payload).OfType<T>().ToArray();
        public ProgramCardDamageModifiedEvent[] Modifiers() => Events<ProgramCardDamageModifiedEvent>().Where(e => Skills.Contains(e.Source.SkillId)).ToArray();
        public CurrentTurnCardUseKindsRecordedEvent[] SlashUses() => Events<CurrentTurnCardUseKindsRecordedEvent>().Where(e => e.ActorSeat == 0 && e.RoundActionType == CardActionType.Use && IsSlash(e.EffectiveCardKind)).ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "A first-Slash command was rejected."); }
        public void Answer(Func<PromptChoice, bool> predicate) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(predicate).Id, G.Revision));
        public void Continue() => Answer(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Use(string id, int[]? targets = null) { Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId)); Play(); }
        public LegalAction Action(CardKind kind, int[] targets, bool converted = false) => G.GetHumanLegalActions().First(action => action.CardId is { } id &&
            V().Hand.Any(card => card.Id == id && card.Kind == kind) && action.TargetSeats.SequenceEqual(targets) && action.Kind != LegalActionKind.Recast &&
            (converted ? action.ConversionSource?.SkillId == "classic:wusheng" : action.ConversionSource is null));
        public int Begin(LegalAction action)
        {
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources }); return action.CardId.Value;
        }
        public int BeginCard(CardKind kind, int[]? targets = null, bool converted = false) => Begin(Action(kind, targets ?? [], converted));
        public int PlayCard(CardKind kind, int[]? targets = null) { var card = BeginCard(kind, targets); Play(); return card; }
        public void AtSkill(string skill) => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill);
        public void AtDamage() => AtSkill(Watch);
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == "boundary:lihuo" &&
                    P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                { Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                Need(P?.PlayerSeat != 0 && G.State.Winner == Winner.None, "Unexpected real first-Slash boundary: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded native first-Slash boundary was not reached: " + Diagnostic());
        }
        public void AssertPaidOnce(int card) => Need(G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The original real material has one Hand payment and one native cleanup through all children and cold returns.");
        public string Diagnostic() => JsonSerializer.Serialize(new { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId,
                Choices = P.Choices.Select(choice => choice.Id) }, Turn = G.State.TurnNumber, Round,
            Uses = SlashUses().Select(e => new { e.OriginFrameId, e.CardActionId, e.ActorSeat, e.TurnNumber, e.EffectiveCardKind }),
            Damage = Events<DamageAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount }), Modifiers = Modifiers(),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) });
        public string State() => FullState(G);
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold recovery preserves exact declaration order, effective kinds, frozen card damage, Armor and paid entities without repeated quota or source consumption.");
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every actual card retains exactly one zone.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            string[] tactics = scene == "quota" ? [Lianji, Lianji2, Erlianji] : scene == "stacked" ? Skills.Take(5).ToArray() :
                scene is "multi" or "nested" or "chain" or "jueqing" or "maximum" ? [Dangtou2, Leihuo2] : scene == "loss" ? [] : [Dangtou, Leihuo];
            var own = new List<string> { Watch, "classic:wusheng" };
            if (scene == "virtual") own.Add(Virtual);
            if (scene == "loss") own.AddRange([Gate, Loss]);
            if (scene == "jueqing") own.Add("classic:jueqing");
            if (scene == "maximum") own.Add("shanhe:xiaoming");
            if (scene == "lihuo") own.Add("boundary:lihuo");
            ShanheStartingCard[] opening = scene == "quota" ? Enumerable.Range(0, 7).Select(index => new ShanheStartingCard(CardKind.Slash, Suit.Club, index + 1)).ToArray() :
                [new(CardKind.Duel, Suit.Club, 1), new(CardKind.Slash, Suit.Spade, 2), new(CardKind.Slash, Suit.Club, 3),
                 new(CardKind.Dodge, Suit.Heart, 4), new(CardKind.FireSlash, Suit.Diamond, 5), new(CardKind.ThunderSlash, Suit.Spade, 6),
                 new(CardKind.FireSlash, Suit.Heart, 7), new(CardKind.FireSlash, Suit.Diamond, 8), new(CardKind.FireSlash, Suit.Heart, 9),
                 new(CardKind.Alcohol, Suit.Club, 10), new(CardKind.IronChain, Suit.Club, 11), new(CardKind.IronChain, Suit.Spade, 12)];
            if (scene is "fan" or "zither" or "nested") opening = opening.Concat(new[] { new ShanheStartingCard(
                scene == "fan" ? CardKind.ZhuqueFan : scene == "zither" ? CardKind.CrimsonSoulZither : CardKind.QinglongCrescentBlade, Suit.Diamond, 1) }).ToArray();
            var enemies = Enumerable.Range(1, 3).Select(seat => new ShanheCombatantDefinition(Peer, Hp: scene is "response" or "nested" && seat == 1 ? 18 : null,
                ExtraSkillIds: scene == "actor" && seat == 1 ? [Counter, Dangtou] : [])
            { StartingHand = scene == "response" && seat == 1 ? [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 8)] :
                scene == "nested" && seat == 1 ? [new(CardKind.Dodge, Suit.Heart, 8)] : [] }).ToArray();
            var setup = new ShanheBattleSetup(83, 1, "fixture-first-slash-" + scene, Owner, 20, 0, 0, 0, 0, own, opening, [], enemies, [], new string('a', 64))
            { TacticSkillIds = tactics };
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry); result.Accept(new StartGameCommand());
            if (scene == "virtual") result.Reach(() => result.P is { PlayerSeat: 0 } && result.P.SkillPrompt?.SkillId == Virtual);
            else result.Play(); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-first-slash", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:guan-yu");
            foreach (var id in Skills.Concat(new[] { "classic:jueqing", "shanhe:xiaoming", "boundary:lihuo" })) builder.AddSkill(Production.Value.GetSkill(id));
            object Pause(string name) => new { op = "chooseOption", target = "owner", resultBind = name, options = new[] { new { id = "continue" } } };
            object Activation(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets,
                targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            var modifiers = new List<object> { new { id = "range", query = "attackRange", operation = "add", value = 4, priority = 0 },
                new { id = "keep", query = "handLimit", operation = "add", value = 20, priority = 0 } };
            if (scene != "quota") modifiers.Add(new { id = "enough-native-uses", query = "slashLimit", operation = "add", value = 10, priority = 0 });
            if (scene is "multi" or "nested") modifiers.Add(new { id = "native-two-targets", query = "cardTargetCount", operation = "add", value = 1, priority = 0,
                cardKinds = new[] { "slash", "fireSlash", "thunderSlash" } });
            var definitions = new object[] {
                new { id = Driver, revision = 1, modifiers, activations = new[] {
                    Activation("hit", 1, new { op = "damage", target = "selectedTarget", amount = 1 }),
                    Activation("armor-two", 1, new { op = "grantArmor", target = "selectedTarget", amount = 2 }),
                    Activation("armor-four", 1, new { op = "grantArmor", target = "selectedTarget", amount = 4 }),
                    Activation("extra-turn", 0, new { op = "pendExtraTurn", target = "owner" }),
                    Activation("recover-one", 0, new { op = "recover", target = "owner", amount = 1 }) } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "actual-paid-damage", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage",
                    optional = false, effects = new[] { Pause("paid") } } } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "skip-unrelated-ai-play", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Virtual, revision = 1, triggers = new[] { new { id = "real-virtual-first", window = "playPhaseStarting", subject = "owner", optional = false,
                    usageScope = "game", usageLimit = 1, effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLiving" },
                        new { op = "useVirtualCard", target = "selectedTarget", outputKind = "slash", targetRestriction = "distanceUnlimitedAgainstTarget", useCardActionWindows = true } } } } },
                new { id = Gate, revision = 1, hpSkillGrants = new[] { new { id = "first-round-hp", skillId = Dangtou, minimumHp = 20 },
                    new { id = "first-elemental-hp", skillId = Leihuo, minimumHp = 20 } } },
                new { id = Loss, revision = 1, triggers = new[] { new { id = "actual-issued-source-loss", window = "slashBeforeResponse", ownerRelation = "actor",
                    cardKinds = new[] { "slash", "fireSlash", "thunderSlash" }, optional = false, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "loseHp", target = "owner", amount = 1 }, Pause("lost") } } } },
                new { id = Counter, revision = 1, triggers = new[] { new { id = "other-actors-real-use", window = "afterDamageApplied", subject = "owner", damageOccurrence = "perDamage",
                    optional = false, usageScope = "game", usageLimit = 1, condition = new { kind = "not", children = new[] { new { kind = "directCardUseDamage" } } },
                    effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "eventSource" }, new { op = "useVirtualSlash", target = "selectedTarget" } } } } }
            };
            var labels = new[] { Driver, Watch, Quiet, Virtual, Gate, Loss, Counter }.ToDictionary(id => id, id => new
            { name = id, description = "真实声明、原生伤害及独立来源检查", optionLabels = id is Watch or Loss ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) builder.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实首杀来源", "", Driver, "qun", 20));
            builder.AddGeneral(new(Peer, "真实响应及传播目标", "", Quiet, "wei", 20));
            if (scene is "fan" or "zither" or "nested")
            {
                var kind = scene == "fan" ? CardKind.ZhuqueFan : scene == "zither" ? CardKind.CrimsonSoulZither : CardKind.QinglongCrescentBlade; var card = CardCatalog.Get(kind);
                builder.AddCard(new("fixture:first-slash-equipment-" + kind, card.DisplayName, card.CategoryName, card.Description, kind));
            }
            builder.AddDeck(new(Deck, "没有自然摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "首杀轮与回合边界", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
