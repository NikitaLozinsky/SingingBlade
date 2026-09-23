using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace SingingBlade
{
    // Диагностика состояния мода по кнопке в окне настроек UMM.
    //
    // Появилась потому, что переключатель «Разрезать небеса» не доехал до панели
    // способностей, при этом в логах не было НИ ОДНОГО исключения нашего мода —
    // то есть цепочка выдачи рвалась молча. Именно этот отчёт и показал, что
    // блюпринты зарегистрированы и оба компонента на зачаровании есть, а до
    // персонажа не доезжает ничего: после этого штатная цепочка блюпринтов была
    // заменена на прямую выдачу из кода (CutTheSkiesGrant).
    internal static class SingingBladeDiagnostics
    {
        public static string Report()
        {
            if (Game.Instance?.Player == null)
            {
                return "Нет активной игровой сессии — загрузите сохранение.";
            }

            // Заодно приводим факт-переключатель в соответствие с экипировкой: если
            // DLL обновили посреди сессии, событие смены экипировки уже не придёт,
            // а перезагружать игру ради этого незачем.
            CutTheSkies.RefreshParty();

            var sb = new StringBuilder();
            sb.AppendLine("(выданные факты синхронизированы с экипировкой)");
            sb.AppendLine();

            // 1. Зарегистрированы ли блюпринты вообще.
            //
            // Строка со СПОСОБНОСТЬЮ тут появилась не сразу — и зря: ровно её блюпринт
            // однажды забыли зарегистрировать в SingingBladeBlueprints.Create(), выдача
            // молча не состоялась, а отчёт об этом не говорил ничего.
            sb.AppendLine("Блюпринты:");
            sb.AppendLine("  предмет: " + Found<BlueprintItemWeapon>(Guids.ItemGuid));
            sb.AppendLine("  способность (резерв Магуса): " + Found<BlueprintAbility>(Guids.CutTheSkiesAbilityGuid));
            sb.AppendLine("  способность (резерв Наследника): " + Found<BlueprintAbility>(Guids.CutTheSkiesAbilityEldritchGuid));
            sb.AppendLine("  бафф режима: " + Found<BlueprintBuff>(Guids.CutTheSkiesBuffGuid));
            sb.AppendLine("  бафф ауреоли: " + Found<BlueprintBuff>(Guids.SongAureoleGuid));
            sb.AppendLine("  бафф растяжки досягаемости: " + Found<BlueprintBuff>(Guids.ReachStretchBuffGuid));

            // Самопроверка ровно на ту поломку, из-за которой игра не сохранялась:
            // безымянный компонент блюпринта роняет сериализацию факта
            // (EntityFact.ComponentsDictionary строит словарь по component.name).
            // Снаружи это выглядит как окно SAVINGERROR без объяснений, поэтому пусть
            // будет видно кнопкой.
            sb.AppendLine("  " + CheckComponentNames());
            sb.AppendLine("  зачарование «Гроза Элизиума»: " + Found<BlueprintWeaponEnchantment>(Guids.StormEnchantmentGuid));
            sb.AppendLine("  фича грозы (носитель): " + Found<BlueprintFeature>(Guids.StormFeatureGuid));
            sb.AppendLine("  фича грозы (дракон): " + Found<BlueprintFeature>(Guids.StormPetFeatureGuid));

            // 2. Кто держит клинок. Ищем по всей партии, а не только у ГГ —
            // предмет мог перекочевать к компаньону.
            var itemBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(Guids.ItemGuid);
            UnitEntityData wielder = null;
            foreach (var unit in Game.Instance.Player.Party)
            {
                var hands = unit.Body?.PrimaryHand?.MaybeWeapon;
                var offHand = unit.Body?.SecondaryHand?.MaybeWeapon;
                if ((hands != null && hands.Blueprint == itemBlueprint) ||
                    (offHand != null && offHand.Blueprint == itemBlueprint))
                {
                    wielder = unit;
                    break;
                }
            }

            sb.AppendLine();
            if (wielder == null)
            {
                var inInventory = Game.Instance.Player.Party.Any(
                    u => u.Inventory.Contains(Reflect.Ref<BlueprintItemReference>(Guids.ItemGuid)));
                sb.AppendLine(inInventory
                    ? "Клинок есть в инвентаре, но НИ У КОГО НЕ В РУКАХ — экипируйте его."
                    : "Клинка нет ни у кого в партии — выдайте его кнопкой выше.");
                return sb.ToString();
            }

            sb.AppendLine("Клинок в руках у: " + wielder.CharacterName);

            // 3. Доехало ли до носителя то, что должно — в ТЕКУЩЕМ режиме.
            //
            // Печатаем ОБА варианта способности: так видно и то, что выдалось, и то, что
            // осталось от прошлой сборки, если резерв персонажа определился иначе.
            var abilityMagus = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(Guids.CutTheSkiesAbilityGuid);
            var abilityEldritch = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(Guids.CutTheSkiesAbilityEldritchGuid);
            sb.AppendLine("  способность как факт (вариант Магуса): " +
                          YesNo(abilityMagus != null && wielder.Descriptor.HasFact(abilityMagus)));
            sb.AppendLine("  способность как факт (вариант Наследника): " +
                          YesNo(abilityEldritch != null && wielder.Descriptor.HasFact(abilityEldritch)));

            var inAbilityList = wielder.Descriptor.Abilities?.Enumerable
                ?.Any(a => a.Blueprint == abilityMagus || a.Blueprint == abilityEldritch) == true;
            sb.AppendLine("  способность в списке способностей: " + YesNo(inAbilityList));

            // «Гроза Элизиума»: фича носителя выдаётся из кода, а фича дракона — уже
            // компонентом AddFeatureToPet из неё. Печатаем обе, иначе разорванную цепочку
            // "фича есть, а у Айву нет" снаружи не увидеть никак.
            var stormFeature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(Guids.StormFeatureGuid);
            sb.AppendLine("  фича «Грозы Элизиума»: " +
                          YesNo(stormFeature != null && wielder.Descriptor.HasFact(stormFeature)));

            var stormPetFeature = ResourcesLibrary.TryGetBlueprint<BlueprintFeature>(Guids.StormPetFeatureGuid);
            foreach (var pet in wielder.Pets)
            {
                var petUnit = pet.Entity;
                if (petUnit == null) continue;
                sb.AppendLine("    питомец " + petUnit.CharacterName + ": фича грозы — " +
                              YesNo(stormPetFeature != null && petUnit.Descriptor.HasFact(stormPetFeature)));
            }

            // 4. Зачарования на самом клинке — если нашего тут нет, вопрос к предмету.
            var weapon = wielder.Body?.PrimaryHand?.MaybeWeapon;
            if (weapon != null && weapon.Blueprint != itemBlueprint)
            {
                weapon = wielder.Body?.SecondaryHand?.MaybeWeapon;
            }
            sb.AppendLine();
            sb.AppendLine("Зачарования на клинке:");
            if (weapon?.Enchantments != null)
            {
                foreach (var ench in weapon.Enchantments)
                {
                    sb.AppendLine("  " + ench.Blueprint.name + " (компонентов: " + ench.Blueprint.ComponentsArray.Length + ")");
                }
            }

            // 5. Состояние песни — чтобы отличить "режим не включается" от
            // "песнь не звучит".
            sb.AppendLine();
            sb.AppendLine("Песнь сейчас звучит на носителе: " + YesNo(HasBuff(wielder, Guids.SongAureoleGuid)));
            sb.AppendLine("Режим «Разрезать небеса» активен: " + YesNo(CutTheSkies.IsModeActive(wielder)));

            // 6. Наши баффы с остатком времени. Нужно для разбора случая "крит был,
            // а песня не продлилась": единственное, что может не дать песне зазвучать
            // при успешной проверке навыка, — маркер "уже спели в этом раунде"
            // (SungThisRoundFlag). Если после неудавшегося продления он всё ещё висит,
            // значит гейт держится дольше, чем должен, и чинить надо именно его.
            sb.AppendLine();
            sb.AppendLine("Наши баффы на носителе (осталось, сек):");
            AppendBuff(sb, wielder, Guids.SungThisRoundFlagGuid, "маркер 'уже спели в этом раунде'");
            AppendBuff(sb, wielder, Guids.SongBuffGuid, "Песнь клинка");
            AppendBuff(sb, wielder, Guids.SongBuffEmpoweredGuid, "Песнь клинка (усиленная)");
            AppendBuff(sb, wielder, Guids.SongAureoleGuid, "ауреоль");
            AppendBuff(sb, wielder, Guids.CutTheSkiesBuffGuid, "режим «Разрезать небеса»");
            AppendBuff(sb, wielder, Guids.ReachStretchBuffGuid, "растяжка досягаемости");

            // Досягаемость клинка — ГЛАВНЫЙ признак того, работает ли дистанционный удар.
            // Весь режим держится на одном бонусе к стату Reach, и когда компонент этого
            // бонуса однажды забыли положить в бафф, снаружи это выглядело как "способность
            // тратится, а поведение обычное" — по этим двум числам было бы видно сразу.
            // Движок считает так: AttackRange оружия = базовая дальность + max(Reach - 5, 0).
            sb.AppendLine();
            var reach = wielder.Stats?.GetStat(StatType.Reach)?.ModifiedValue ?? 0;
            sb.AppendLine("Досягаемость: стат Reach = " + reach +
                          " (у среднего существа без бонусов 5), дальность удара клинком = " +
                          (weapon != null ? weapon.AttackRange.Value + " футов" : "клинок не найден"));

            return sb.ToString();
        }

        // Проверяет, что у всех компонентов всех блюпринтов мода задано имя.
        //
        // Имя компонента — это ключ, по которому игра сохраняет данные факта
        // (EntityFact.ComponentsDictionary). Пустое имя = исключение при сохранении и
        // окно SAVINGERROR. Имена раздаёт SingingBladeBlueprints.NameElements, а эта
        // строка подтверждает, что раздача действительно случилась.
        private static string CheckComponentNames()
        {
            var guids = new[]
            {
                Guids.ItemGuid, Guids.EnchantmentGuid, Guids.StormEnchantmentGuid,
                Guids.AbilityGuid, Guids.CutTheSkiesAbilityGuid, Guids.CutTheSkiesAbilityEldritchGuid,
                Guids.SongBuffGuid, Guids.SongBuffEmpoweredGuid, Guids.SongAureoleGuid,
                Guids.SungThisRoundFlagGuid, Guids.CutTheSkiesBuffGuid, Guids.ReachStretchBuffGuid,
                Guids.SongAreaGuid, Guids.StormFeatureGuid, Guids.StormPetFeatureGuid
            };

            var checkedComponents = 0;
            var unnamed = 0;
            var missing = 0;

            foreach (var guid in guids)
            {
                var blueprint = ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>(guid);
                if (blueprint == null) { missing++; continue; }

                foreach (var component in blueprint.ComponentsArray ?? new BlueprintComponent[0])
                {
                    if (component == null) continue;
                    checkedComponents++;
                    if (string.IsNullOrEmpty(component.name)) unnamed++;
                }
            }

            if (missing > 0)
                return "имена компонентов: не найдено блюпринтов — " + missing + " (мод собрался не полностью)";

            return unnamed == 0
                ? "имена компонентов: в порядке (проверено " + checkedComponents + ")"
                : "имена компонентов: БЕЗ ИМЕНИ " + unnamed + " из " + checkedComponents +
                  " — игра НЕ СОХРАНИТСЯ, см. NameElements";
        }

        // Есть ли у персонажа такой резерв и сколько в нём очков.
        //
        // Отличать "резерва нет вовсе" от "резерв пуст" важно: в первом случае способность
        // просто не тот вариант (у Наследника свой блюпринт резерва), во втором — всё верно,
        // надо отдохнуть. Снаружи оба случая выглядят одинаково: счётчик 0 и "нет ресурсов".
        private static void AppendResource(StringBuilder sb, UnitEntityData unit, string guid, string label)
        {
            var resource = ResourcesLibrary.TryGetBlueprint<BlueprintAbilityResource>(guid);
            if (resource == null)
            {
                sb.AppendLine("  " + label + ": блюпринт ресурса не найден");
                return;
            }

            var has = false;
            foreach (var owned in unit.Descriptor.Resources)
            {
                if (owned == resource) { has = true; break; }
            }

            sb.AppendLine("  " + label + ": " +
                          (has ? "есть, очков " + unit.Descriptor.Resources.GetResourceAmount(resource) : "нет"));
        }

        private static void AppendBuff(StringBuilder sb, UnitEntityData unit, string guid, string label)
        {
            var blueprint = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(guid);
            var buff = blueprint == null ? null : unit.Descriptor.Buffs.GetBuff(blueprint);
            if (buff == null)
            {
                sb.AppendLine("  " + label + ": нет");
                return;
            }

            var left = buff.IsPermanent ? "ПОСТОЯННЫЙ" : buff.TimeLeft.TotalSeconds.ToString("0.0");
            sb.AppendLine("  " + label + ": есть, осталось " + left);
        }

        private static bool HasBuff(UnitEntityData unit, string guid)
        {
            var buff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(guid);
            return buff != null && unit.Descriptor.Buffs.GetBuff(buff) != null;
        }

        private static string Found<T>(string guid) where T : BlueprintScriptableObject
        {
            return ResourcesLibrary.TryGetBlueprint<T>(guid) != null ? "есть" : "НЕТ";
        }

        private static string YesNo(bool value)
        {
            return value ? "да" : "НЕТ";
        }
    }
}
