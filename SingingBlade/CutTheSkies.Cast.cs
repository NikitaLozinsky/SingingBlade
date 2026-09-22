using System;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Kingmaker.Visual.Particles.FxSpawnSystem;

namespace SingingBlade
{
    internal static partial class CutTheSkies
    {
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
                if (caster == null || spell == null) return false;

                // Гейт режима идёт ПЕРВЫМ, и это важно для логирования: всё, что ниже,
                // при отказе пишет причину в Player.log, а писать её имеет смысл только
                // для того, кто реально включил режим. Иначе строка вылезала бы на каждый
                // каст каждого сопартийца.
                if (!IsModeActive(caster))
                {
                    // Носитель клинка кастует, а режим выключен — тоже стоит строки в логе:
                    // отличает "префикс не сработал вовсе" (строки нет совсем) от
                    // "бафф не висит" (строка есть). Кроме одного случая: сама способность
                    // «Разрезать небеса» — на её касте баффа ещё нет по определению, он
                    // только будет наложен, и строка в логе сбивала бы с толку.
                    if (HoldsSingingBlade(caster) && !IsOwnAbility(spell))
                        Main.Log("CutTheSkies: каст не перехвачен — режим «Разрезать небеса» " +
                                 "не активен, баффа на кастере нет");
                    return false;
                }

                if (target == null) return Decline("цель не юнит (каст по точке или по земле)");

                // Тип команды. Ванильный Лучник смотрит только Standard/Swift, но у него
                // это про анимацию; нам же важно не пропустить каст, который мифика
                // превращает в действие движения (AbilityData.RuntimeActionType: при
                // MythicAbilitiesAsMoveAction обычное заклинание становится CommandType.Move).
                if (command.Type != UnitCommand.CommandType.Standard
                    && command.Type != UnitCommand.CommandType.Swift
                    && command.Type != UnitCommand.CommandType.Move)
                    return Decline("тип команды " + command.Type);

                if (!spell.IsRay)
                    return Decline("у заклинания " + spell.Blueprint.name +
                                   " нет дистанционной атаки касанием (AbilityData.IsRay = false)");

                if (!IsDeliverable(spell))
                    return Decline("заклинание " + spell.Blueprint.name +
                                   " не из списка магуса и не в списке разрешённых способностей");

                // Только по врагу. Клинок в этой схеме реально БЬЁТ цель оружием, поэтому
                // провести через него что-то дружественное нельзя в принципе — получилась
                // бы атака по своему. Ваниль в аналогичном месте проверяет ровно так же
                // (UnitUseAbility.CreateCastCommand: "unit.IsEnemy(target.Unit)").
                // Для заклинаний магуса это почти теория, а вот «Орудия свободы» умеют
                // целиться и в союзника — там проверка обязательна.
                if (!caster.IsEnemy(target)) return Decline("цель не враг");

                var weapon = MeleeWeapon(caster);
                if (weapon == null) return Decline("в руках нет оружия ближнего боя");

                // Не подменяем ванильные ветки отказа: если каст и так невозможен,
                // отдаём управление обратно, пусть движок сам откажет и покажет FX.
                if (!spell.IsAvailable) return Decline("заклинание недоступно (IsAvailable = false)");
                if (!target.IsInGame || target.Descriptor.State.IsDead)
                    return Decline("цель мертва или не в игре");

                // Дотягиваем клинок до цели, если она дальше обычного удара. ДО создания
                // UnitAttack — команда считает себе радиус подхода от дальности оружия,
                // а та берётся из стата в момент расчёта.
                if (!TryReachTarget(caster, target, weapon, spell)) return false;

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

                // Контрольная проверка ПОСЛЕ растяжки: команда атаки посчитает себе радиус
                // подхода сама, уже с новой досягаемостью. Если цель всё равно вне радиуса —
                // подбегания в этой схеме нет, атака просто умерла бы на месте, а игрок
                // остался бы и без удара, и без луча. Лучше отдать каст ванили.
                if (caster.DistanceTo(target) > UnitAttack.GetApproachRadius(weapon, caster, target))
                {
                    DropExtendedReach();
                    return Decline("после растяжки цель всё ещё вне радиуса удара");
                }

                Store(caster, spell);

                // FX подготовки заклинания в руках надо погасить вместе с атакой,
                // иначе он останется висеть (ровно тот класс багов, на который мы
                // уже дважды напарывались с незакрытыми партиклами).
                attack.ClearFxOnAttack = TakeHandFx(command);
                caster.Commands.AddToQueueFirst(attack);
                Main.LogVerbose($"CutTheSkies: атака поставлена в очередь " +
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
                Main.LogError("CutTheSkies.TryInterceptCast", e);
                return false;
            }
        }

        // Это наша собственная способность включения режима, а не заклинание игрока.
        //
        // Сравниваем ССЫЛКИ на блюпринты, а не строки GUID: AssetGuid.ToString() создаёт
        // строку на каждый вызов, и это на пути каста; плюс опечатка в строковой константе
        // здесь не ловится вовсе, а несуществующий блюпринт виден сразу.
        private static bool IsOwnAbility(AbilityData spell)
        {
            var blueprint = spell?.Blueprint;
            if (blueprint == null) return false;

            foreach (var own in ModeAbilities())
            {
                if (blueprint == own) return true;
            }

            return false;
        }

        private static BlueprintAbility _modeAbilityMagus;
        private static BlueprintAbility _modeAbilityEldritch;

        private static IEnumerable<BlueprintAbility> ModeAbilities()
        {
            if (_modeAbilityMagus == null)
                _modeAbilityMagus = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(Guids.CutTheSkiesAbilityGuid);
            if (_modeAbilityEldritch == null)
                _modeAbilityEldritch = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(Guids.CutTheSkiesAbilityEldritchGuid);

            yield return _modeAbilityMagus;
            yield return _modeAbilityEldritch;
        }

        // Пишет в лог, почему перехват не состоялся, и возвращает false.
        //
        // Нужен потому, что отказ перехвата выглядит для игрока ровно как "мод не работает":
        // заклинание просто улетает обычным лучом, никаких других признаков нет. Причин
        // отказа с десяток (не тот тип заклинания, не из списка магуса, цель далеко, нет
        // оружия в руках...), и различить их снаружи невозможно. Вызывается только после
        // того, как пройден гейт IsModeActive, — то есть строка появляется лишь когда игрок
        // действительно включил режим и чего-то ждал.
        private static bool Decline(string reason)
        {
            Main.Log("CutTheSkies: заклинание НЕ проведено через клинок — " + reason);
            return false;
        }

        // Заклинание подходит, если это дистанционная атака касанием и оно из книги
        // Магуса/Чародейского наследника.
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

        // Способности, которые проводятся через клинок ПОМИМО заклинаний магуса.
        //
        // Это не "заклинания" в смысле книги: у мифических способностей нет ни спеллбука,
        // ни места в списке магуса, они живут в окне способностей. Но движку всё равно —
        // доставка через клинок работает для всего, у чего есть дистанционная атака
        // касанием (AbilityDeliverProjectile с оружием RayType), а бросок атаки оружием
        // подменяет собственный бросок луча. Поэтому достаточно разрешить конкретный
        // блюпринт здесь; проверка IsRay выше всё равно отсечёт то, что лучом не является.
        //
        // Пополнять список — одна строка. Пользователь собирался со временем расширить
        // отбор до всех способностей с дистанционной атакой касанием; если дойдёт до этого,
        // список заменится на сам критерий, а не будет расти бесконечно.
        private static readonly string[] ExtraDeliverableAbilities =
        {
            Guids.AzataFirstAscensionAbility
        };

        // Годится ли способность для проведения через клинок: либо это заклинание из
        // списка/книги магуса, либо она явно разрешена белым списком выше.
        //
        // Белый список резолвится один раз и сравнивается по ссылке — заодно это проверка
        // самих GUID: несуществующий блюпринт сразу виден в логе, а не тихо не совпадает.
        private static bool IsDeliverable(AbilityData spell)
        {
            var blueprint = spell?.Blueprint;
            if (blueprint == null) return false;

            if (IsMagusBookSpell(spell)) return true;

            if (_extraDeliverable == null)
            {
                _extraDeliverable = new List<BlueprintAbility>();
                foreach (var guid in ExtraDeliverableAbilities)
                {
                    var bp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(guid);
                    if (bp != null) _extraDeliverable.Add(bp);
                    else Main.Log("CutTheSkies: в списке разрешённых способностей не найден блюпринт " + guid);
                }
            }

            foreach (var allowed in _extraDeliverable)
            {
                if (blueprint == allowed) return true;
            }

            return false;
        }

        private static List<BlueprintAbility> _extraDeliverable;

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

        private static FieldInfo _handFxField;

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
