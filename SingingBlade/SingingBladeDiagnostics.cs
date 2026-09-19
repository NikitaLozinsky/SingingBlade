using System.Linq;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace SingingBlade
{
    // Диагностика состояния мода по кнопке в окне настроек UMM.
    //
    // Появилась потому, что переключатель "Долгая нота" не доехал до панели
    // способностей, при этом в логах не было НИ ОДНОГО исключения нашего мода —
    // то есть цепочка выдачи рвалась молча. Именно этот отчёт и показал, что
    // блюпринты зарегистрированы и оба компонента на зачаровании есть, а до
    // персонажа не доезжает ничего: после этого штатная цепочка блюпринтов была
    // заменена на прямую выдачу из кода (SustainedNoteGrant).
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
            SustainedNote.RefreshParty();

            var sb = new StringBuilder();
            sb.AppendLine("(состояние переключателя синхронизировано с экипировкой)");
            sb.AppendLine();

            // 1. Зарегистрированы ли блюпринты вообще.
            sb.AppendLine("Блюпринты:");
            sb.AppendLine("  предмет: " + Found<BlueprintItemWeapon>(Guids.ItemGuid));
            sb.AppendLine("  переключатель: " + Found<BlueprintActivatableAbility>(Guids.SustainedNoteToggleGuid));
            sb.AppendLine("  бафф режима: " + Found<BlueprintBuff>(Guids.SustainedNoteBuffGuid));
            sb.AppendLine("  бафф ауреоли: " + Found<BlueprintBuff>(Guids.SongAureoleGuid));

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

            // 3. Доехал ли переключатель до носителя.
            var toggle = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(Guids.SustainedNoteToggleGuid);

            sb.AppendLine("  переключатель как факт: " + YesNo(toggle != null && wielder.Descriptor.HasFact(toggle)));

            var activatable = wielder.Descriptor.ActivatableAbilities?.Enumerable
                ?.FirstOrDefault(a => a.Blueprint == toggle);
            sb.AppendLine("  переключатель в списке активируемых: " + YesNo(activatable != null));
            if (activatable != null)
            {
                sb.AppendLine("    включён: " + YesNo(activatable.IsOn));
                sb.AppendLine("    доступен (IsAvailable): " + YesNo(activatable.IsAvailable));
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
            sb.AppendLine("Режим 'Долгая нота' активен: " + YesNo(SustainedNote.IsModeActive(wielder)));

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
            AppendBuff(sb, wielder, Guids.SustainedNoteBuffGuid, "режим Долгой ноты");

            return sb.ToString();
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
