using System;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace SingingBlade
{
    // «Дотянуться до звёзд» — проведение заклинаний с дистанционной атакой касанием
    // (Scorching Ray, Snowball и т.п.) через удар клинком ближнего боя.
    //
    // Название взято из истории клинка: «Дотянуться до звёзд!» кричала Ирвен через палубу
    // перед тем, как поймать брошенное заклинание на клинок. Заодно оно честно описывает
    // механику: вся способность — это бонус к стату Reach, то есть клинок буквально
    // дотягивается дальше. Прежние названия той же способности: "Долгая нота"
    // (SustainedNote) и «Разрезать небеса» (CutTheSkies, до 2026-09-26) — если встретишь
    // их в старых коммитах или в логах прошлых сессий, это одно и то же.
    //
    // Механика НЕ изобретена с нуля: в игре уже есть ровно она — у архетипа
    // Эльдричский лучник, только через дальнобойное оружие. Ванильная цепочка:
    //   UnitUseAbility.OnAction() ловит каст, кладёт заклинание в "карман"
    //   (UnitPartMagus.SetEldritchArcherSpell) и ставит в очередь UnitAttack;
    //   -> атака оружием доставляет заклинание через RuleCastSpell,
    //      следом проставляя ruleCastSpell.Context.AttackRoll = AttackRoll;
    //   -> AbilityDeliverProjectile.Deliver() видит непустой context.AttackRoll и
    //      НЕ бросает собственную атаку касанием, а переиспользует бросок оружия
    //      (см. "attackRoll = context.AttackRoll; if (attackRoll == null) {...}").
    // Именно последний шаг и переносит попадание/крит оружия на заклинание.
    //
    // Мы повторяем эту цепочку для оружия БЛИЖНЕГО боя, но СВОИМ путём, а не
    // захватывая ванильный флаг EldritchArcher. Захват флага выглядит короче, но
    // ломает "Боевое заклинание": UnitPartMagus.CanUseSpellCombat при поднятом
    // EldritchArcher требует дальнобойное оружие. Плюс MagusController.Tick()
    // каждый тик вычищает EldritchArcherSpell, если оружие не дальнобойное —
    // то есть наш "карман" стирался бы сразу после каста.
    //
    // Перенос множителя крита отдельного патча НЕ требует: ContextActionDealDamage
    // для крита берёт либо множитель оружия, либо X2 для магусовских заклинаний —
    // у скимитара множитель и так ×2, ветки совпадают.
    internal static partial class ReachForStars
    {
        // Насколько «Дотянуться до звёзд» удлиняет досягаемость клинка, в футах.
        //
        // Движок считает дальность оружия как weapon.AttackRange + Stats.ReachRange, где
        // ReachRange = max(Reach - 5, 0). То есть бонус +20 к стату Reach даёт скимитару
        // (базовые 5 футов) дальность удара 25 футов.
        //
        // Число намеренно умеренное: дальность оружия — это не только про удар, от неё же
        // считается зона угрозы и сцепка в ближнем бою (UnitHelper.GetThreatRange). Зону
        // угрозы мы возвращаем к ванильной отдельным патчем (см. SingingBladePatches),
        // но чем скромнее бонус, тем меньше мест, где он может вылезти боком.
        // 23, а не 20, и это не произвол: движок УКОРАЧИВАЕТ базовую дальность оружия
        // ближнего боя. BlueprintWeaponType.AttackRange для всего, что короче 10 футов,
        // возвращает max(MinWeaponRange, m_AttackRange - 4), то есть у скимитара вместо
        // заявленных в блюпринте 5 футов остаётся 2. С бонусом +20 получалось 22 фута
        // дальности удара вместо обещанных в описании 25 — это поймалось в логе
        // ("до цели 27,2 футов, достаёт на 26,6"). +23 к стату даёт ровно 25 футов
        // дальности оружия, как и написано в описании способности.
        public const int ReachBonusFeet = 23;

        // Сколько держится режим с одного применения (в секундах, как и остальные
        // длительности мода). РОВНО ОДИН РАУНД — решение пользователя: каждый раунд,
        // в который магус хочет ударить издалека, оплачивается своим очком резерва.
        // (Раньше здесь была минута — по образцу ванильного Мистического оружия.)
        //
        // Почему именно 6 секунд, хотя для ПЕСНИ ровно 6с в своё время не годились:
        // цели у длительностей противоположные. Песню надо было ПРОДЛЕВАТЬ, и бафф
        // обязан был дожить до крита в следующем раунде, поэтому ей дали 7с. Здесь же
        // мы хотим обратного — чтобы режим честно гас к началу следующего хода.
        // В пошаговом бою движок считает конец баффа от начала хода, а не от момента
        // применения (BuffCollection.AddBuff: "timeSpan = TurnBasedCombatController
        // .TurnStartTime" при IsInTurnBasedCombat), и снимает его по тому же TurnStartTime.
        // Значит 6с = бафф жив весь текущий ход целиком (быстрое действие, каст, удар)
        // и снимается ровно в начале следующего. В реальном времени 6 секунд — это
        // тот же раунд.
        public const float ModeDurationSeconds = 6f;

        private static ReachForStarsDelivery _deliverySubscriber;
        private static ReachForStarsGrant _grantSubscriber;
        private static ReachForStarsCommands _commandSubscriber;

        // Вызывается один раз вместе с регистрацией блюпринтов.
        public static void Subscribe()
        {
            if (_deliverySubscriber == null)
            {
                _deliverySubscriber = new ReachForStarsDelivery();
                EventBus.Subscribe(_deliverySubscriber);
            }

            if (_grantSubscriber == null)
            {
                _grantSubscriber = new ReachForStarsGrant();
                EventBus.Subscribe(_grantSubscriber);
            }

            if (_commandSubscriber == null)
            {
                _commandSubscriber = new ReachForStarsCommands();
                EventBus.Subscribe(_commandSubscriber);
            }
        }

        // Мод включили или выключили в окне UMM.
        //
        // Выключенный мод обязан перестать влиять на игру ПОЛНОСТЬЮ, а не только перестать
        // выполнять свой код: выданные факты, бафф режима и растяжка досягаемости живут на
        // персонаже и сами не исчезнут. Если их не снять, игрок выключит мод, сохранится —
        // и унесёт в сейв факты, которые больше некому обслуживать.
        public static void OnModToggled(bool enabled)
        {
            try
            {
                Clear();

                if (enabled)
                {
                    RefreshParty();
                    return;
                }

                var player = Game.Instance?.Player;
                if (player == null) return;

                // PartyAndPets, а не Party: фича грозы уезжает и на дракона Азаты, а он в
                // Party не входит. Штатно её сняло бы AddFeatureToPet вместе с фичей
                // носителя, но полагаться на это при уборке не стоит.
                foreach (var unit in player.PartyAndPets)
                {
                    if (unit?.Descriptor == null) continue;

                    foreach (var fact in AllGrantable()) RemoveIfPresent(unit, fact);
                    RemoveIfPresent(unit, ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(Guids.StormFeatureGuid));
                    RemoveIfPresent(unit, ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(Guids.StormPetFeatureGuid));
                    RemoveIfPresent(unit, ResourcesLibrary.TryGetBlueprint<BlueprintUnitFact>(Guids.ReachForStarsBuffGuid));
                }

                Main.Log("ReachForStars: мод выключен — выданные факты сняты с партии");
            }
            catch (Exception e)
            {
                Main.LogError("ReachForStars.OnModToggled", e);
            }
        }

        private static void RemoveIfPresent(UnitEntityData unit, BlueprintUnitFact fact)
        {
            if (HasUsableFact(unit, fact)) unit.Descriptor.RemoveFact(fact);
        }

        // ----------------------------------------------------------------
        // Проверки
        // ----------------------------------------------------------------

        // Режим включён = просто висит бафф переключателя.
        //
        // Раньше здесь была ВТОРАЯ проверка — "и песнь сейчас звучит" (бафф SongAureole).
        // Убрана по просьбе пользователя: переключатель должен работать в любой момент,
        // независимо от песни, и включаться/выключаться нажатием на иконку. Так и
        // тестировать удобнее, и поведение честнее — тумблер либо включён, либо нет,
        // без скрытого третьего состояния "включён, но молчит".
        public static bool IsModeActive(UnitEntityData caster)
        {
            if (caster == null) return false;
            return HasFact(caster, ref _modeBuff, Guids.ReachForStarsBuffGuid);
        }

        // Блюпринты кэшируем: этот путь дёргается на КАЖДОЕ применение способности
        // любым юнитом в игре (префикс на UnitUseAbility.OnAction), а TryGetBlueprint —
        // это поиск по общему кэшу ресурсов, незачем ходить туда по два раза за каст.
        private static BlueprintBuff _modeBuff;

        private static bool HasFact(UnitEntityData unit, ref BlueprintBuff cached, string guid)
        {
            if (cached == null) cached = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(guid);
            return cached != null && unit.Descriptor.Buffs.GetBuff(cached) != null;
        }

    }
}
