using System;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.Utility;

namespace SingingBlade
{
    internal static partial class ReachForStars
    {
        // ----------------------------------------------------------------
        // Досягаемость под дальность заклинания
        // ----------------------------------------------------------------
        //
        // Постоянный бонус баффа (ReachBonusFeet) задаёт ОБЫЧНЫЙ дальний удар клинком.
        // Но замысел фичи — "удар заклинателя, только на расстоянии": заклинание должно
        // доставляться всюду, куда достаёт само заклинание, а не до произвольной отметки
        // в футах. Дальность лучей растёт с уровнем (Близкая = 25 + 5 за два уровня), так
        // что никакое фиксированное число не подошло бы надолго.
        //
        // Поэтому в момент каста мы ДОТЯГИВАЕМ досягаемость ровно настолько, чтобы клинок
        // достал до конкретной цели, и снимаем добавку, как только заклинание ушло с
        // клинка. Стат Reach выбран той же точкой, что и у постоянного бонуса: +N к стату
        // = +N футов к дальности оружия (ReachRange = max(Reach - 5, 0)).
        //
        // Добавка живёт МИНИМУМ времени: ставится непосредственно перед созданием команды
        // атаки и снимается в Clear() — то есть сразу, как заклинание доставлено или
        // протухло. Если удар промахнулся, заклинание остаётся на клинке, и добавка вместе
        // с ним: следующая атака в этом же раунде тоже должна дотягиваться.
        //
        // РЕАЛИЗОВАНА БАФФОМ, А НЕ МОДИФИКАТОРОМ СТАТА — и это не стилистика.
        // Первая версия вешала модификатор прямо на стат Reach
        // (Stats.GetStat(Reach).AddModifier(...)), и это была мина: ModifiableValue
        // сериализует ВСЕ свои модификаторы в сейв (свойство PersistentModifierList), а у
        // модификатора без факта-источника некому снять его при загрузке. Сохранение в
        // окне между кастом и ударом — и досягаемость осталась бы удлинённой навсегда,
        // без единого способа это заметить и откатить.
        // Бафф сохраняется и восстанавливается штатно, истекает по своей длительности, и
        // худший случай теперь — лишний раунд досягаемости.
        //
        // Величина добавки переменная, поэтому она задаётся РАНГОМ баффа (Buff.SetRank):
        // у AddStatBonus бонус считается как Value * Fact.GetRank(), а Value в блюпринте
        // равен 1 (см. BuildReachStretchBuff).
        private static BlueprintBuff _reachStretchBuff;

        // true — цель достижима ударом (сразу или после растяжки).
        // false — не наш случай, причина уже записана в лог.
        private static bool TryReachTarget(UnitEntityData caster, UnitEntityData target,
                                           ItemEntityWeapon weapon, AbilityData spell)
        {
            // Страховка: добавка от прошлого каста не должна пережить новый.
            DropExtendedReach();

            var distance = caster.DistanceTo(target);
            var radius = UnitAttack.GetApproachRadius(weapon, caster, target);
            if (distance <= radius) return true;

            // Дальше обычного удара — но, возможно, в пределах самого заклинания.
            // GetApproachDistance возвращает ровно ту дистанцию, с которой движок
            // разрешает сотворить это заклинание по этой цели (с учётом объёма тел),
            // то есть сравнима с DistanceTo напрямую.
            var spellReach = spell.GetApproachDistance(target);
            if (distance > spellReach)
            {
                return Decline(string.Format(
                    "цель дальше самого заклинания: до неё {0:0.#} футов, заклинание достаёт на {1:0.#}",
                    distance / Feet.FeetToMetersRatio, spellReach / Feet.FeetToMetersRatio));
            }

            var blueprint = ReachStretchBuff();
            if (blueprint == null) return Decline("блюпринт растяжки досягаемости не найден в кэше");

            // +1 фут запаса: дистанция считается во float-метрах, и упереться в равенство
            // на границе — ровно тот случай, который мы уже поймали в логе (27,2 против 26,6).
            var extraFeet = (int)Math.Ceiling((distance - radius) / Feet.FeetToMetersRatio) + 1;
            if (extraFeet > SingingBladeBlueprints.MaxReachStretchFeet)
            {
                return Decline(string.Format(
                    "нужна растяжка на {0} футов — больше потолка в {1}",
                    extraFeet, SingingBladeBlueprints.MaxReachStretchFeet));
            }

            // Длительность — один раунд: это последняя страховка на случай, если ни одна
            // из явных точек снятия не сработает.
            var buff = caster.Descriptor.AddBuff(blueprint, caster,
                                                 TimeSpan.FromSeconds(ModeDurationSeconds));
            if (buff == null) return Decline("бафф растяжки не наложился");
            buff.SetRank(extraFeet);

            Main.Log(string.Format(
                "ReachForStars: досягаемость дотянута на +{0} футов под дальность заклинания " +
                "(до цели {1:0.#} футов, обычный удар достаёт на {2:0.#}, заклинание — на {3:0.#})",
                extraFeet, distance / Feet.FeetToMetersRatio, radius / Feet.FeetToMetersRatio,
                spellReach / Feet.FeetToMetersRatio));

            return true;
        }

        // Снимает временную добавку к досягаемости. Вызывать можно сколько угодно раз.
        //
        // Вызывается из Clear() (заклинание ушло с клинка или протухло), перед каждым новым
        // перехватом и по окончании любой команды носителя, если на клинке уже пусто, — то
        // есть из всех мест, где добавка перестаёт быть нужной. Оставить её висеть нельзя:
        // это был бы бессрочный бонус к досягаемости из ниоткуда.
        public static void DropExtendedReach()
        {
            try
            {
                var blueprint = ReachStretchBuff();
                var player = Game.Instance?.Player;
                if (blueprint == null || player == null) return;

                // Снимаем у всех в партии, а не у запомненного носителя: так у метода нет
                // собственного состояния, которое могло бы разойтись с реальностью после
                // загрузки сейва или смены персонажа.
                foreach (var unit in player.PartyAndPets)
                {
                    if (unit?.Descriptor != null && unit.Descriptor.HasFact(blueprint))
                    {
                        unit.Descriptor.RemoveFact(blueprint);
                    }
                }
            }
            catch (Exception e)
            {
                Main.LogError("ReachForStars.DropExtendedReach", e);
            }
        }

        private static BlueprintBuff ReachStretchBuff()
        {
            if (_reachStretchBuff == null)
                _reachStretchBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(Guids.ReachStretchBuffGuid);
            return _reachStretchBuff;
        }

        // Возвращает зону угрозы к ванильной, вычитая наш бонус к досягаемости.
        //
        // Зачем: движок считает зону угрозы (внеочередные атаки, сцепка в ближнем бою) от
        // той же дальности оружия, что и сам удар — UnitHelper.GetThreatRange возвращает
        // hand.Weapon.AttackRange.Meters. Без этого магус с активной способностью «Дотянуться до звёзд» начал бы
        // угрожать и бить внеочередными атаками на всю дистанцию удара, а враги считались
        // бы с ним в ближнем бою через полполя. Пользователь просил зону не раздувать,
        // поэтому дальность УДАРА растёт, а зона УГРОЗЫ остаётся ванильной.
        //
        // Вычитаем ровно свой вклад, а не обнуляем: бонусы от Увеличения, оружия с
        // досягаемостью и прочего должны продолжать работать как обычно.
        public static void TrimThreatRange(UnitEntityData unit, ref float? threatRange)
        {
            if (threatRange == null || unit == null) return;
            if (!IsModeActive(unit)) return;

            // Вычитаем и постоянный бонус баффа, и временную добавку под дальность
            // заклинания, если она сейчас висит на этом же юните: зона угрозы не должна
            // расти ни от того, ни от другого. Размер добавки берём из РАНГА её баффа —
            // ровно из того места, откуда его берёт и сам AddStatBonus.
            var ours = ReachBonusFeet;
            var stretch = ReachStretchBuff();
            if (stretch != null) ours += unit.Descriptor?.Buffs?.GetBuff(stretch)?.Rank ?? 0;

            threatRange = Math.Max(0f, threatRange.Value - ours.Feet().Meters);
        }

    }
}
