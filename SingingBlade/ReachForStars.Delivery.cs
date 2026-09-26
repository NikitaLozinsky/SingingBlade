using System;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;

namespace SingingBlade
{
    internal static partial class ReachForStars
    {
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

                // Тратим ровно по тому же признаку, что и ваниль в UnitUseAbility.OnAction
                // ("if (ruleCastSpell.ShouldSpendResource) Ability.Spend()"): если каст
                // сорвался — скажем, его контрзаклинанием сбили, — ресурс не списывается.
                // Для заклинаний это тонкость, а для мифических способностей вроде
                // «Орудий свободы» это прямая плата из их собственного резерва.
                if (rule.ShouldSpendResource) spell.Spend();
            }
            catch (Exception e)
            {
                Clear();
                Main.LogError("ReachForStars.OnWeaponAttackResolved", e);
            }
        }

        // Заклинание, ждущее удара клинком. Фактически всегда не больше одного на
        // всю игру (уникальное оружие у одного персонажа), поэтому одного слота
        // достаточно — и он не течёт ссылками между загрузками, т.к. чистится по
        // сроку годности и при каждой неудачной проверке.
        private static UnitEntityData _caster;
        private static AbilityData _spell;
        private static TimeSpan _storedAt;

        // Самая дешёвая проверка "есть ли вообще что доставлять" — без обращений к
        // блюпринтам и баффам. Нужна подписчику на правило атаки: он срабатывает на каждую
        // атаку оружием в игре, и в 99.9% случаев карман пуст.
        public static bool HasSpellPending => _spell != null;

        // ----------------------------------------------------------------
        // Состояние
        // ----------------------------------------------------------------

        private static void Store(UnitEntityData caster, AbilityData spell)
        {
            _caster = caster;
            _spell = spell;
            _storedAt = Game.Instance.TimeController.GameTime;
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
            // Заклинание ушло с клинка — временная добавка к досягаемости больше не нужна.
            DropExtendedReach();
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

    }
}
