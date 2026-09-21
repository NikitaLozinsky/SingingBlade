using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Items.Slots;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Kingmaker.Visual.Particles.FxSpawnSystem;
using Pathfinding;
using TurnBased.Controllers;

namespace SingingBlade
{
    // "Долгая нота" — проведение заклинаний с дистанционной атакой касанием
    // (Scorching Ray, Snowball и т.п.) через удар клинком ближнего боя.
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
    // Подписчик на правило атаки оружием. Отдельным классом, а не патчем на
    // MagusController: движок сам рассылает событие всем подписчикам, патчить
    // чужой метод незачем — меньше шансов подраться с другими модами.
    internal class SustainedNoteDelivery : IGlobalRulebookHandler<RuleAttackWithWeapon>
    {
        public void OnEventAboutToTrigger(RuleAttackWithWeapon evt)
        {
        }

        public void OnEventDidTrigger(RuleAttackWithWeapon evt)
        {
            if (!Main.Enabled) return;
            SustainedNote.OnWeaponAttackResolved(evt);
        }
    }

    // Выдаёт/забирает переключатель при смене экипировки и при загрузке области.
    //
    // Раньше это делала штатная цепочка блюпринтов:
    //   зачарование -> AddUnitFeatureEquipment -> скрытая фича -> AddFacts -> переключатель.
    // Схема правильная (ровно так устроены ванильные LordProtectorEnchant и
    // ProtectionFromEvil), но у нас она рвалась МОЛЧА: диагностика показывала, что
    // оба компонента на блюпринте зачарования есть, а до персонажа не доезжает ни
    // фича, ни переключатель, и при этом в логе нет ни одного исключения мода.
    // Отлаживать четырёхзвенную цепочку вслепую дороже, чем заменить её одним звеном,
    // которое целиком наше и полностью наблюдаемое.
    internal class SustainedNoteGrant : IUnitEquipmentHandler, IAreaHandler
    {
        public void HandleEquipmentSlotUpdated(ItemSlot slot, ItemEntity previousItem)
        {
            if (!Main.Enabled) return;
            SustainedNote.RefreshToggle(slot?.Owner?.Unit);
        }

        // Смена экипировки при загрузке сейва события может не прислать, поэтому
        // дополнительно проходим по партии при загрузке области.
        public void OnAreaDidLoad()
        {
            if (!Main.Enabled) return;
            SustainedNote.RefreshParty();
        }

        public void OnAreaBeginUnloading()
        {
        }
    }

    // Выдаёт бесплатную атаку, пока заклинание лежит "на клинке", и пишет в лог судьбу
    // команды атаки.
    //
    // Зачем: в пошаговом бою атака после потраченного основного действия отбивается
    // движком в UnitCommandController.TickCommandTurnBased —
    //     flag3 = command.IsIgnoreCooldown || (CanActInCombat && !HasCooldownForCommand(command));
    //     if (!num || !flag3) { command.ForceFinishForTurnBased(Success); Queue.Clear(); }
    // то есть команда молча "завершается успехом", не ударив. Именно так выглядел баг
    // "подходит к врагу и не атакует".
    //
    // Ванильный магус обходит это ровно здесь же: MagusController.HandleUnitRunCommand
    // на старте команды зовёт unitAttack.IgnoreCooldown(...), если движок знает, что у
    // магуса подготовлен Заклинательный удар (PreparedSpellStrike / PreparedSpellCombat).
    // Наш "карман" движку не виден, поэтому ту же услугу оказываем себе сами — тем же
    // способом и в той же точке жизненного цикла.
    internal class SustainedNoteCommands : IUnitRunCommandHandler, IUnitCommandStartHandler, IUnitCommandEndHandler
    {
        public void HandleUnitRunCommand(UnitCommand cmd)
        {
            try
            {
                if (!(cmd is UnitAttack attack)) return;
                if (!SustainedNote.HasSpellOnBlade(cmd.Executor)) return;

                // Ровно одна атака оружием в дополнение к заклинанию — как у
                // Заклинательного удара (и как у нашей собственной команды).
                attack.IsSingleAttack = true;
                attack.IgnoreCooldown();
                Main.Log("SustainedNote: атаке разрешён бесплатный удар (заклинание на клинке)");
            }
            catch (Exception e)
            {
                Main.LogError("SustainedNote.HandleUnitRunCommand", e);
            }
        }

        // Диагностика: без неё судьбу команды в пошаговом режиме не отследить — движок
        // гасит её через ForceFinishForTurnBased(Success), то есть без ошибки в логе и
        // без внешних признаков. Пишем по носителю клинка, а не по "есть ли заклинание
        // в кармане": иначе не увидеть как раз те случаи, где карман уже опустел.
        public void HandleUnitCommandDidStart(UnitCommand cmd)
        {
            Trace(cmd, "стартовала");
        }

        public void HandleUnitCommandDidEnd(UnitCommand cmd)
        {
            Trace(cmd, "завершилась");
        }

        private static void Trace(UnitCommand cmd, string what)
        {
            try
            {
                if (cmd == null || !SustainedNote.HoldsSingingBlade(cmd.Executor)) return;

                var turn = Game.Instance.TurnBasedCombatController?.CurrentTurn;
                Main.Log($"SustainedNote: команда {cmd.GetType().Name} ({cmd.Type}) {what}: " +
                         $"Result={cmd.Result}, стартовала={cmd.IsStarted}, " +
                         $"безКулдауна={cmd.IsIgnoreCooldown}, нуженПодход={cmd.ShouldUnitApproach}, " +
                         $"ходДействует={(turn != null ? turn.IsActing.ToString() : "нет хода")}, " +
                         $"заклинаниеНаКлинке={SustainedNote.HasSpellOnBlade(cmd.Executor)}");
            }
            catch (Exception e)
            {
                Main.LogError("SustainedNote.Trace", e);
            }
        }
    }

    internal static class SustainedNote
    {
        // ====================================================================
        // РЕЖИМ РАБОТЫ "ДОЛГОЙ НОТЫ" — переключается ровно этой одной строкой
        // ====================================================================
        //
        // true  — ДИСТАНЦИОННЫЙ УДАР (текущий режим, выбран пользователем 2026-09-21).
        //         "Долгая нота" — активируемая способность: быстрое действие, стоит очко
        //         Мистического резерва, на несколько раундов удлиняет досягаемость клинка
        //         (бафф с бонусом к стату Reach). Пока она действует, магус бьёт скимитаром
        //         на расстоянии, и заклинание с дистанционной атакой касанием уходит тем же
        //         ударом — с места, без подбегания.
        //
        // false — СТАРАЯ СХЕМА (подбежать и ударить). "Долгая нота" — липкий переключатель,
        //         досягаемость не меняется, а после каста мод сам ставит в очередь команду
        //         атаки и прокладывает ей путь до цели. Весь код этой схемы СОХРАНЁН и
        //         рабочий (TryPrepareApproach / TryAllowNormalMovement / PathLength и
        //         BuildSustainedNoteToggle в блюпринтах) — переключение флага возвращает её
        //         целиком, пересборка обязательна, т.к. блюпринты строятся при загрузке.
        //
        // Почему ушли от неё: подход к цели из кода — это три независимых слоя пошаговой
        // механики (путь в пуле A*, режим пятифутового шага, гейт бесплатной атаки), каждый
        // из которых ломался молча. Дистанционный удар снимает саму необходимость подхода.
        public static readonly bool RangedStrike = true;

        // Насколько "Долгая нота" удлиняет досягаемость клинка, в футах.
        //
        // Движок считает дальность оружия как weapon.AttackRange + Stats.ReachRange, где
        // ReachRange = max(Reach - 5, 0). То есть бонус +20 к стату Reach даёт скимитару
        // (базовые 5 футов) дальность удара 25 футов.
        //
        // Число намеренно умеренное: дальность оружия — это не только про удар, от неё же
        // считается зона угрозы и сцепка в ближнем бою (UnitHelper.GetThreatRange). Зону
        // угрозы мы возвращаем к ванильной отдельным патчем (см. SingingBladePatches),
        // но чем скромнее бонус, тем меньше мест, где он может вылезти боком.
        public const int ReachBonusFeet = 20;

        // Сколько держится режим с одного применения (в секундах, как и остальные
        // длительности мода). 60 секунд = 10 раундов — как у ванильного Мистического
        // оружия, которое тоже стоит очко резерва.
        public const float RangedStrikeDurationSeconds = 60f;

        private static SustainedNoteDelivery _deliverySubscriber;
        private static SustainedNoteGrant _grantSubscriber;
        private static SustainedNoteCommands _commandSubscriber;

        // Вызывается один раз вместе с регистрацией блюпринтов.
        public static void Subscribe()
        {
            if (_deliverySubscriber == null)
            {
                _deliverySubscriber = new SustainedNoteDelivery();
                EventBus.Subscribe(_deliverySubscriber);
            }

            if (_grantSubscriber == null)
            {
                _grantSubscriber = new SustainedNoteGrant();
                EventBus.Subscribe(_grantSubscriber);
            }

            if (_commandSubscriber == null)
            {
                _commandSubscriber = new SustainedNoteCommands();
                EventBus.Subscribe(_commandSubscriber);
            }
        }

        // ----------------------------------------------------------------
        // Выдача переключателя носителю клинка
        // ----------------------------------------------------------------

        public static void RefreshParty()
        {
            var player = Game.Instance?.Player;
            if (player == null) return;

            foreach (var unit in player.Party)
            {
                RefreshToggle(unit);
            }
        }

        // Держит факт-переключатель в соответствии с тем, в руках ли Поющий клинок.
        public static void RefreshToggle(UnitEntityData unit)
        {
            try
            {
                if (unit?.Descriptor == null) return;

                // Что именно выдаём, зависит от режима (см. RangedStrike): активируемую
                // способность с платой из резерва либо старый липкий переключатель.
                BlueprintUnitFact granted = RangedStrike
                    ? (BlueprintUnitFact)ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(Guids.SustainedNoteAbilityGuid)
                    : ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(Guids.SustainedNoteToggleGuid);
                if (granted == null) return;

                // Факт другого режима мог остаться на персонаже с прошлой сборки — снимаем,
                // иначе в панели висели бы обе кнопки сразу.
                var stale = RangedStrike
                    ? (BlueprintUnitFact)ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(Guids.SustainedNoteToggleGuid)
                    : ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(Guids.SustainedNoteAbilityGuid);
                if (stale != null && unit.Descriptor.HasFact(stale)) unit.Descriptor.RemoveFact(stale);

                var shouldHave = HoldsSingingBlade(unit);
                var hasIt = unit.Descriptor.HasFact(granted);

                if (shouldHave && !hasIt)
                {
                    unit.Descriptor.AddFact(granted);
                }
                else if (!shouldHave && hasIt)
                {
                    unit.Descriptor.RemoveFact(granted);
                }

                // Клинок убрали из рук — снимаем и сам режим. Иначе в дистанционном режиме
                // у персонажа осталась бы удлинённая досягаемость (бафф живёт минуту сам по
                // себе) уже без Поющего клинка, что выглядит как читерский бонус из ниоткуда.
                if (!shouldHave)
                {
                    var modeBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(Guids.SustainedNoteBuffGuid);
                    if (modeBuff != null && unit.Descriptor.HasFact(modeBuff))
                    {
                        unit.Descriptor.RemoveFact(modeBuff);
                    }
                }
            }
            catch (Exception e)
            {
                Main.LogError("SustainedNote.RefreshToggle", e);
            }
        }

        public static bool HoldsSingingBlade(UnitEntityData unit)
        {
            var item = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(Guids.ItemGuid);
            if (item == null || unit?.Body == null) return false;

            return unit.Body.PrimaryHand?.MaybeWeapon?.Blueprint == item
                || unit.Body.SecondaryHand?.MaybeWeapon?.Blueprint == item;
        }

        // Заклинание, ждущее удара клинком. Фактически всегда не больше одного на
        // всю игру (уникальное оружие у одного персонажа), поэтому одного слота
        // достаточно — и он не течёт ссылками между загрузками, т.к. чистится по
        // сроку годности и при каждой неудачной проверке.
        private static UnitEntityData _caster;
        private static AbilityData _spell;
        private static TimeSpan _storedAt;

        private static FieldInfo _handFxField;

        // ----------------------------------------------------------------
        // Перехват каста (вызывается из префикса на UnitUseAbility.OnAction)
        // ----------------------------------------------------------------

        // true  -> мы забрали каст себе, ванильный OnAction выполнять не надо;
        // false -> не наш случай, пусть всё идёт как обычно.
        public static bool TryInterceptCast(UnitUseAbility command, ref UnitCommand.ResultType result)
        {
            try
            {
                var caster = command?.Executor;
                var spell = command?.Ability;
                var target = command?.Target?.Unit;
                if (caster == null || spell == null || target == null) return false;

                // Только основное действие/быстрое — как и у ванильного Лучника.
                if (command.Type != UnitCommand.CommandType.Standard && command.Type != UnitCommand.CommandType.Swift)
                    return false;

                if (!IsModeActive(caster)) return false;
                if (!IsDeliverableSpell(caster, spell)) return false;

                var weapon = MeleeWeapon(caster);
                if (weapon == null) return false;

                // Не подменяем ванильные ветки отказа: если каст и так невозможен,
                // отдаём управление обратно, пусть движок сам откажет и покажет FX.
                if (!spell.IsAvailable) return false;
                if (!target.IsInGame || target.Descriptor.State.IsDead) return false;

                var attack = new UnitAttack(target)
                {
                    // ВСЕГДА одиночная атака. У ванильного Эльдричского лучника здесь
                    // стоит (Type == Swift), то есть на обычном касте выходит ПОЛНАЯ
                    // серия ударов — для лука это уместно, а у нас давало вторую атаку
                    // по уже убитой цели (видно в combatLog: первый удар добивает, второй
                    // бьёт труп и не наносит урона вовсе). Да и по механике Заклинательного
                    // удара полагается ровно одна атака оружием в дополнение к заклинанию.
                    IsSingleAttack = true
                };
                attack.IgnoreCooldown();

                // Путь до цели прокладываем ДО того, как что-либо испортим: если дойти
                // нельзя, перехват отменяется целиком и заклинание уходит обычным
                // способом, по воздуху. Иначе игрок остался бы и без луча, и без удара.
                if (!TryPrepareApproach(caster, target, weapon, attack)) return false;

                Store(caster, spell);

                // FX подготовки заклинания в руках надо погасить вместе с атакой,
                // иначе он останется висеть (ровно тот класс багов, на который мы
                // уже дважды напарывались с незакрытыми партиклами).
                attack.ClearFxOnAttack = TakeHandFx(command);
                caster.Commands.AddToQueueFirst(attack);
                Main.Log($"SustainedNote: атака поставлена в очередь " +
                         $"(в очереди {caster.Commands.Queue.Count}, " +
                         $"основное действие занято: {caster.Commands.Standard != null})");

                result = UnitCommand.ResultType.Success;
                return true;
            }
            catch (Exception e)
            {
                // Любая неожиданность не должна ломать каст игроку — откатываемся
                // на ванильное поведение.
                Clear();
                Main.LogError("SustainedNote.TryInterceptCast", e);
                return false;
            }
        }

        // Готовит команду атаки к подходу: в пошаговом бою движок НЕ умеет сам вести
        // юнита к цели по команде, созданной из кода.
        //
        // UnitCommand.TickApproaching() в пошаговом режиме берёт путь либо из
        // command.ForcedPath, либо из PathVisualizer (путь, который проложил курсор
        // игрока), и, не найдя ни того ни другого, НЕМЕДЛЕННО обрывает команду:
        //     "Interrupting command ... because forcedPath was null or empty" -> Interrupt().
        // Наш UnitAttack рождается в момент каста, клика по врагу с прокладкой пути не
        // было — поэтому в отдалении от цели атака умирала сразу, а ход на этом и
        // заканчивался. Вплотную всё работало лишь потому, что подход не нужен вовсе
        // (ShouldUnitApproach == false).
        //
        // Ванильного примера "скастовал -> подбежал -> ударил" в игре нет: Эльдричскому
        // лучнику подход не нужен (радиус подхода равен дальности выстрела), а у мили-
        // Заклинательного удара команду атаки создаёт сам клик игрока в
        // UnitUseAbility.CreateCastCommand, и путь приходит от курсора. Поэтому строим
        // путь сами — ровно тем же способом, что и ванильный UnitFearController для
        // своей UnitMoveTo: AgentASP.FindPath -> BlockUntilCalculated -> ForcedPath.
        //
        // Возвращает false, если дойти нельзя — тогда перехват отменяется целиком.
        private static bool TryPrepareApproach(UnitEntityData caster, UnitEntityData target,
                                               ItemEntityWeapon weapon, UnitAttack attack)
        {
            // Тот же радиус, что посчитает себе сама команда в UnitAttack.Init(): дальность
            // оружия + объём обоих тел. В режиме дистанционного удара дальность оружия уже
            // включает бонус "Долгой ноты" к Reach, поэтому обычно мы попадаем сюда же.
            var radius = UnitAttack.GetApproachRadius(weapon, caster, target);
            // Уже в досягаемости удара — подход не нужен, ни путь, ни возня с режимом хода.
            if (caster.DistanceTo(target) <= radius) return true;

            // Режим дистанционного удара подбегания НЕ предусматривает вовсе: клинок достаёт
            // ровно настолько, насколько его удлинила "Долгая нота". Цель дальше — значит
            // это не наш случай, отдаём каст ванили, и заклинание улетит обычным лучом.
            if (RangedStrike) return false;

            // Дальше — старая схема "подбежать и ударить" (RangedStrike = false).
            // В реальном времени путь прокладывает сам движок (ветка view.MoveTo в
            // TickApproaching), вмешиваться незачем и нечем.
            if (!CombatController.IsInTurnBasedCombat()) return true;

            var turn = Game.Instance.TurnBasedCombatController?.CurrentTurn;
            if (turn == null) return false;

            if (!TryAllowNormalMovement(turn, caster, target, radius)) return false;

            var agent = caster.View?.AgentASP;
            if (agent == null) return false;

            // callback обязателен по сигнатуре, но нам сообщать нечего: путь мы тут же
            // дожидаемся синхронно.
            Path path = agent.FindPath(target.Position, delegate { }, radius);
            // null = агент уже считает другой путь. Лучше отдать каст ванили, чем
            // поставить в очередь атаку, которая всё равно оборвётся.
            if (path == null) return false;

            // ПОРЯДОК ЭТИХ ДВУХ СТРОК ПРИНЦИПИАЛЕН, и ровно в нём была ошибка.
            // Путь отдаём команде СРАЗУ, до ожидания расчёта, — так делает и ванильный
            // UnitFearController (`ForcedPath = FindPath(...)`, и только потом
            // BlockUntilCalculated). Причина: в колбэк FindPath зашит
            // `m_Seeker.ReleaseClaimedPath()`, поэтому к моменту возврата из
            // BlockUntilCalculated у пути не остаётся ни одного держателя, A* забирает
            // его в пул и очищает vectorPath. Сеттер ForcedPath делает Claim(this) —
            // присвоив заранее, мы удерживаем путь живым. Присваивание ПОСЛЕ ожидания
            // давало команде уже переработанный пулом путь, и TickApproaching обрывал её
            // на первом же тике ("forcedPath was null or empty"): в логе это выглядело
            // как `UnitAttack ... Result=Interrupt, стартовала=False, нуженПодход=True`.
            attack.ForcedPath = path;

            AstarPath.BlockUntilCalculated(path);
            if (path.error || path.vectorPath == null || path.vectorPath.Count == 0)
            {
                attack.ForcedPath = null;
                return false;
            }

            // Дойти должно хватить ОСТАВШЕГОСЯ хода. FindPath обрезает путь по запасу на
            // целый раунд (maxLength = 6 * CurrentSpeedMps, то есть два действия движения),
            // а у нас основное действие уже потрачено на каст — остаётся одно. Если пути
            // не хватает, перехват отменяем: пусть заклинание улетит обычным лучом, чем
            // персонаж уйдёт в никуда и застрянет с недошедшей атакой.
            var available = turn.GetRemainingMovementRange(caster, total: false, singleActionMove: false);
            var needed = PathLength(path);
            Main.Log($"SustainedNote: подход {needed:F1} м из доступных {available:F1} м " +
                     $"(режим хода {turn.CurrentMovementLimit}, радиус удара {radius:F1} м)");
            if (needed > available)
            {
                attack.ForcedPath = null;
                return false;
            }

            return true;
        }

        // Переводит ход из режима ПЯТИФУТОВОГО ШАГА в обычное движение, если пятью футами
        // до цели не дотянуться.
        //
        // Это и было причиной бага "персонаж делает два шага и застывает". Списание
        // движения в пошаговом бою не смотрит на тип команды — TurnController.TickMovement
        // смотрит ТОЛЬКО на GetEnabledFiveFootStep(unit):
        //     if (GetEnabledFiveFootStep(unit)) { MetersMovedByFiveFootStep += ...; }
        //     else                              { cooldowns.MoveAction += deltaTime; }
        // А у магуса с активным Заклинательным ударом "умный курсор" почти всегда стоит
        // на варианте с MovementLimit.FiveFootStep (см. PrepareSmartCursorVariants: первые
        // пять вариантов из девяти — именно пятифутовые). Движок сам снимает этот режим,
        // только когда путь ПОД КУРСОРОМ длиннее пяти футов — а у нашей команды никакого
        // курсорного пути нет.
        //
        // Дальше срабатывала ловушка: пройдя 7.5 фута, юнит упирался в
        //     ShouldRestrictNormalMovement -> MetersMovedByFiveFootStep > 0 -> true
        // что запрещает обычное движение до конца хода СОВСЕМ. Команда атаки оставалась
        // висеть (юнит хочет идти, но не может — TickApproaching в этом случае даже не
        // обрывает её), и ход намертво вставал: ни шагу, ни каста.
        //
        // Если же пятифутового шага ХВАТАЕТ, режим не трогаем: он выгоднее — не тратит
        // действие движения и даёт иммунитет к внеочередным атакам при отходе.
        private static bool TryAllowNormalMovement(TurnController turn, UnitEntityData caster,
                                                   UnitEntityData target, float radius)
        {
            if (!turn.GetEnabledFiveFootStep(caster)) return true;

            var toGo = caster.DistanceTo(target) - radius;
            if (toGo <= turn.GetRemainingFiveFootStepRange(caster)) return true;

            // SetMovementLimit молча игнорируется, пока юнит движется, — здесь он стоит
            // (только что отработал каст), так что смена режима применится.
            turn.SetMovementLimit(TurnController.MovementLimit.TwoActions);

            // Если режим всё-таки не сменился, подход спишется пятифутовым шагом и
            // застопорит ход — лучше не перехватывать каст вовсе.
            return !turn.GetEnabledFiveFootStep(caster);
        }

        private static float PathLength(Path path)
        {
            var points = path.vectorPath;
            var length = 0f;
            for (var i = 1; i < points.Count; i++) length += UnityEngine.Vector3.Distance(points[i - 1], points[i]);
            return length;
        }

        // ----------------------------------------------------------------
        // Доставка заклинания попаданием клинка
        // ----------------------------------------------------------------

        public static void OnWeaponAttackResolved(RuleAttackWithWeapon evt)
        {
            try
            {
                // Промах — заклинание остаётся "на клинке" и может быть доставлено
                // следующей атакой в этом же раунде. Так же ведёт себя и обычный
                // Удар заклинателя (MagusController выходит по !AttackRoll.IsHit).
                if (evt?.AttackRoll == null || !evt.AttackRoll.IsHit) return;

                var caster = evt.Initiator;

                // Оружие проверяем ДО изъятия из кармана: если ударили не клинком
                // (например, второй рукой или пустой рукой), заклинание должно просто
                // остаться ждать — но именно остаться, а не быть положенным заново.
                // Повторный Store() сбрасывал бы отметку времени, и "карман" жил бы
                // бесконечно вместо одного раунда.
                var weapon = evt.AttackRoll.Weapon;
                if (weapon == null || !weapon.Blueprint.IsMelee || weapon == caster.Body.EmptyHandWeapon) return;

                // Цель уже мертва (её добил урон самого клинка в этой же атаке) —
                // заклинание в труп не отправляем: исполнение RuleCastSpell отложенное,
                // и лучи прилетали уже после смерти, впустую. Заклинание при этом НЕ
                // теряется: оставляем его на клинке, как при промахе, — следующая атака
                // в этом же раунде доставит его по живой цели.
                var target = evt.AttackRoll.Target;
                if (target == null || target.Descriptor == null || target.Descriptor.State.IsDead) return;

                var spell = Take(caster);
                if (spell == null) return;

                var rule = Rulebook.Trigger(new RuleCastSpell(spell, target));
                // Ключевая строка всей фичи: доставка луча увидит непустой AttackRoll
                // и переиспользует бросок оружия вместо собственной атаки касанием,
                // вместе с подтверждённым критом. Проставляется ПОСЛЕ Trigger —
                // именно так это сделано и в ванили (RuleAttackWithWeapon, а также
                // KineticistController), потому что исполнение заклинания отложенное.
                rule.Context.AttackRoll = evt.AttackRoll;

                spell.Spend();
            }
            catch (Exception e)
            {
                Clear();
                Main.LogError("SustainedNote.OnWeaponAttackResolved", e);
            }
        }

        // ----------------------------------------------------------------
        // Состояние
        // ----------------------------------------------------------------

        private static void Store(UnitEntityData caster, AbilityData spell)
        {
            _caster = caster;
            _spell = spell;
            _storedAt = Game.Instance.TimeController.GameTime;
        }

        // Возвращает зону угрозы к ванильной, вычитая наш бонус к досягаемости.
        //
        // Зачем: движок считает зону угрозы (внеочередные атаки, сцепка в ближнем бою) от
        // той же дальности оружия, что и сам удар — UnitHelper.GetThreatRange возвращает
        // hand.Weapon.AttackRange.Meters. Без этого магус с "Долгой нотой" начал бы
        // угрожать и бить внеочередными атаками на всю дистанцию удара, а враги считались
        // бы с ним в ближнем бою через полполя. Пользователь просил зону не раздувать,
        // поэтому дальность УДАРА растёт, а зона УГРОЗЫ остаётся ванильной.
        //
        // Вычитаем ровно свой вклад, а не обнуляем: бонусы от Увеличения, оружия с
        // досягаемостью и прочего должны продолжать работать как обычно.
        public static void TrimThreatRange(UnitEntityData unit, ref float? threatRange)
        {
            if (!RangedStrike || threatRange == null || unit == null) return;
            if (!IsModeActive(unit)) return;

            threatRange = Math.Max(0f, threatRange.Value - ReachBonusFeet.Feet().Meters);
        }

        // Лежит ли сейчас заклинание "на клинке" у этого юнита. В отличие от Take() не
        // трогает содержимое кармана — только смотрит.
        public static bool HasSpellOnBlade(UnitEntityData unit)
        {
            if (unit == null || _spell == null || _caster != unit) return false;
            if (Game.Instance.TimeController.GameTime - _storedAt > 1.Rounds().Seconds) return false;
            return IsModeActive(unit);
        }

        public static void Clear()
        {
            _caster = null;
            _spell = null;
            _storedAt = TimeSpan.Zero;
        }

        // Достаёт отложенное заклинание, если оно ещё действительно, и очищает слот.
        private static AbilityData Take(UnitEntityData caster)
        {
            if (_spell == null || _caster == null || _caster != caster) return null;

            var spell = _spell;
            var storedAt = _storedAt;
            Clear();

            // Протухло (прошёл раунд), режим выключили или песнь смолкла — заклинание
            // уже не проводится. Один в один с ванильным Лучником, у которого
            // MagusController.Tick() роняет "карман" по тем же условиям.
            if (Game.Instance.TimeController.GameTime - storedAt > 1.Rounds().Seconds) return null;
            if (!IsModeActive(caster)) return null;

            return spell;
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
            return HasFact(caster, ref _modeBuff, Guids.SustainedNoteBuffGuid);
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

        // Заклинание подходит, если это дистанционная атака касанием и оно из книги
        // Магуса/Чародейского наследника.
        private static bool IsDeliverableSpell(UnitEntityData caster, AbilityData spell)
        {
            // AbilityData.IsRay — это ровно "дистанционная атака касанием":
            // deliverProjectile.Weapon.AttackType == AttackType.RangedTouch.
            if (!spell.IsRay) return false;
            return IsMagusBookSpell(spell);
        }

        // Намеренно НЕ используем UnitPartMagus.IsSpellFromMagusSpellList: он резолвит
        // книгу через BlueprintRoot.SystemMechanics.MagusClass, а Чародейский наследник
        // в этой игре существует и как ОТДЕЛЬНЫЙ класс (EldritchScionClass) — у такого
        // персонажа уровней Магуса нет, GetSpellbook(книга Магуса) вернёт null, и
        // ванильный метод упал бы на Spellbook.Blueprint.
        //
        // Основная проверка — по СПИСКУ заклинаний, а не по книге: книга Магуса и книга
        // Наследника ссылаются на один и тот же MagusSpellList, и список не зависит от
        // того, из какой книги заклинание в итоге кастуется. Это переживает моды,
        // сливающие спеллбуки (у пользователя стоит MythicMagicMayhem), где проверка
        // по блюпринту книги перестала бы узнавать заклинание.
        // Сравнение книги оставлено запасным вариантом — на случай заклинания, попавшего
        // в книгу помимо списка (например, выученного через Greater Spell Access).
        private static BlueprintSpellList _magusSpellList;

        private static bool IsMagusBookSpell(AbilityData spell)
        {
            if (_magusSpellList == null)
                _magusSpellList = ResourcesLibrary.TryGetBlueprint<BlueprintSpellList>(Guids.MagusSpellList);

            if (_magusSpellList != null && spell.IsInSpellList(_magusSpellList)) return true;

            var book = spell.Spellbook?.Blueprint;
            if (book == null) return false;

            var assetGuid = book.AssetGuid.ToString();
            return assetGuid == Guids.MagusSpellbook || assetGuid == Guids.EldritchScionSpellbook;
        }

        private static ItemEntityWeapon MeleeWeapon(UnitEntityData caster)
        {
            var weapon = caster.GetFirstWeapon();
            if (weapon == null) return null;
            if (!weapon.Blueprint.IsMelee) return null;
            if (weapon == caster.Body.EmptyHandWeapon) return null;
            return weapon;
        }

        // m_HandFxObjects приватное — забираем рефлексией и обнуляем, чтобы ванильный
        // код потом не погасил тот же FX повторно.
        private static List<IFxHandle> TakeHandFx(UnitUseAbility command)
        {
            if (_handFxField == null)
            {
                _handFxField = typeof(UnitUseAbility).GetField("m_HandFxObjects",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_handFxField == null) return null;

            var value = _handFxField.GetValue(command) as List<IFxHandle>;
            _handFxField.SetValue(command, null);
            return value;
        }
    }
}
