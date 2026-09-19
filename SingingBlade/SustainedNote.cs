using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
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
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using Kingmaker.Visual.Particles.FxSpawnSystem;

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

    internal static class SustainedNote
    {
        private static SustainedNoteDelivery _deliverySubscriber;
        private static SustainedNoteGrant _grantSubscriber;

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

                var toggle = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(Guids.SustainedNoteToggleGuid);
                if (toggle == null) return;

                var shouldHave = HoldsSingingBlade(unit);
                var hasIt = unit.Descriptor.HasFact(toggle);

                if (shouldHave && !hasIt)
                {
                    unit.Descriptor.AddFact(toggle);
                }
                else if (!shouldHave && hasIt)
                {
                    unit.Descriptor.RemoveFact(toggle);
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

                Store(caster, spell);

                // FX подготовки заклинания в руках надо погасить вместе с атакой,
                // иначе он останется висеть (ровно тот класс багов, на который мы
                // уже дважды напарывались с незакрытыми партиклами).
                var handFx = TakeHandFx(command);

                var attack = new UnitAttack(target)
                {
                    ClearFxOnAttack = handFx,
                    // ВСЕГДА одиночная атака. У ванильного Эльдричского лучника здесь
                    // стоит (Type == Swift), то есть на обычном касте выходит ПОЛНАЯ
                    // серия ударов — для лука это уместно, а у нас давало вторую атаку
                    // по уже убитой цели (видно в combatLog: первый удар добивает, второй
                    // бьёт труп и не наносит урона вовсе). Да и по механике Заклинательного
                    // удара полагается ровно одна атака оружием в дополнение к заклинанию.
                    IsSingleAttack = true
                };
                attack.IgnoreCooldown();
                caster.Commands.AddToQueueFirst(attack);

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
