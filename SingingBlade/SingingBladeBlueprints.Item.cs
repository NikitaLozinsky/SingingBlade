using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.AreaEffects;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.ActivatableAbilities.Restrictions;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;
using Kingmaker.Utility;
using UnityEngine;

namespace SingingBlade
{
    public static partial class SingingBladeBlueprints
    {
        // ----------------------------------------------------------------
        // Предмет
        // ----------------------------------------------------------------

        private static BlueprintItemWeapon BuildItem()
        {
            var item = new BlueprintItemWeapon
            {
                CR = 13,
                Charges = 1,
                SpendCharges = false,
                RestoreChargesOnRest = false,
                CasterLevel = 1,
                SpellLevel = 1,
                DC = 11,
                IsNonRemovable = false,
                KeepInPolymorph = false,
                Double = false,
                CountAsDouble = false
            };
            item.AssetGuid = BlueprintGuid.Parse(Guids.ItemGuid);
            item.name = "SingingBladeItem";

            Reflect.Set(item, "m_DisplayNameText", SingingBladeLocalization.CreateString(L.ItemName));
            Reflect.Set(item, "m_DescriptionText", SingingBladeLocalization.CreateString(L.ItemDescription));
            // Пустые (не null!) LocalizedString — как у ванильных предметов. Если оставить
            // поле C#-null, implicit-конвертация LocalizedString -> string в игре возвращает
            // буквально строку "<null>" (см. Kingmaker.Localization.LocalizedString, операторы
            // implicit operator string), и она показывается в тултипе как есть.
            // Большой поэтичный текст истории клинка — показывается отдельно от описания,
            // по кнопке "Сведения" в инвентаре (как у "Жертвы Роннека" и других легендарок).
            Reflect.Set(item, "m_FlavorText", SingingBladeLocalization.CreateString(L.ItemFlavorText));
            Reflect.Set(item, "m_NonIdentifiedNameText", new LocalizedString());
            Reflect.Set(item, "m_NonIdentifiedDescriptionText", new LocalizedString());
            // Иконка не задавалась вовсе -> движок молча подставлял дефолтную иконку
            // скимитара. Не резолвим Unity Sprite вручную по guid+fileid (для m_Icon
            // это прямая ссылка на объект Sprite, а не строковый BlueprintReference) —
            // проще и надёжнее одолжить уже загруженный Icon у Faith Bearer.
            // Своя картинка здесь НЕ используется сознательно: предмет переиспользует
            // визуал "Несущего веру" целиком — и модель, и иконку, — чтобы они не
            // разъезжались между собой. Свои иконки только у баффов и способностей.
            Reflect.Set(item, "m_Icon", ItemIcon(Guids.FaithBearerItem));
            Reflect.Set(item, "m_Cost", 100000);
            Reflect.Set(item, "m_Weight", 4.0f);
            Reflect.Set(item, "m_Type", Reflect.Ref<BlueprintWeaponTypeReference>(Guids.ScimitarWeaponType));
            Reflect.Set(item, "m_Size", Size.Medium);
            // Пустая (но НЕ null!) ссылка на "предмет одежды" персонажа. Мы его не
            // используем — визуал клинка целиком берётся из m_VisualParameters, — но
            // BlueprintItemEquipment.EquipmentEntity дереференсит поле БЕЗ проверки
            // (m_EquipmentEntity.Get(), не ?.Get()), а у блюпринтов из JSON ссылка
            // всегда создана. В рантайме поле оставалось C#-null, и открытие панели
            // настройки внешности в инвентаре роняло UI: ItemEntity.CanChangeColor()
            // -> BlueprintItemEquipment.get_EquipmentEntity -> NullReferenceException
            // (видно в Player.log, CharacterVisualSettingsVM.CreateItemsColorSelectors).
            // Четвёртый случай одной и той же категории — см. правило в CLAUDE.md.
            Reflect.Set(item, "m_EquipmentEntity", Reflect.Empty<KingmakerEquipmentEntityReference>());
            Reflect.Set(item, "m_OverrideDamageDice", false);
            Reflect.Set(item, "m_OverrideDamageType", false);
            Reflect.Set(item, "m_Enchantments", new[]
            {
                Reflect.Ref<BlueprintWeaponEnchantmentReference>(Guids.Enhancement4Enchantment),
                Reflect.Ref<BlueprintWeaponEnchantmentReference>(Guids.EnchantmentGuid),
                // Наша «Гроза Элизиума»: молния в того, кого раскритовали.
                Reflect.Ref<BlueprintWeaponEnchantmentReference>(Guids.StormEnchantmentGuid),
                // Ванильный Грохочущий взрыв — как есть, своего аналога не нужно:
                // внутри один компонент WeaponEnergyBurst со звуковым уроном d8.
                Reflect.Ref<BlueprintWeaponEnchantmentReference>(Guids.ThunderingBurstEnchantment)
            });

            // КРИТИЧНО: BlueprintItemWeapon.OnEnableWithLibrary() сам подставляет
            // m_VisualParameters = new WeaponVisualParameters() если поле null, но внутри
            // этого пустого объекта m_Projectiles остаётся C#-null (не пустой массив).
            // RuleAttackWithWeapon.LaunchProjectiles() безусловно читает
            // Weapon.WeaponVisualParameters.Projectiles.Length на КАЖДОЙ атаке этим оружием —
            // и падает с NullReferenceException ДО того, как успевает создать RuleDealDamage.
            // Именно поэтому базовый удар не наносил урон и наш крит-триггер не срабатывал:
            // RuleAttackWithWeapon.OnTrigger падал раньше, чем очередь доходила до damage
            // и до OnEventDidTrigger-подписчиков (см. GameLogFull.txt: "Object reference not
            // set to an instance of an object at WeaponVisualParameters.get_Projectiles()").
            // Задаём m_Projectiles явно пустым массивом, заодно переиспользуем модель
            // уникального скимитара "Несущий веру" вместо дефолтной модели типа Scimitar.
            var visualParameters = new WeaponVisualParameters();
            Reflect.Set(visualParameters, "m_Projectiles", new BlueprintProjectileReference[0]);
            Reflect.Set(visualParameters, "m_WeaponModel", new PrefabLink { AssetId = Guids.FaithBearerWeaponModel });
            Reflect.Set(visualParameters, "m_WeaponSheathModelOverride", new PrefabLink { AssetId = Guids.FaithBearerWeaponSheathModel });
            Reflect.Set(item, "m_VisualParameters", visualParameters);

            return item;
        }

        // ----------------------------------------------------------------
        // Зачарование оружия: крит-триггер (без skill-check гейта)
        // ----------------------------------------------------------------

        private static BlueprintWeaponEnchantment BuildEnchantment()
        {
            var castAbility = new ContextActionCastSpell();
            Reflect.Set(castAbility, "m_Spell", Reflect.Ref<BlueprintAbilityReference>(Guids.AbilityGuid));

            // Отмечаем "спели в этом раунде" ТОЛЬКО при реальном успехе песни (не на
            // каждой попытке) — неудачная проверка Подвижности не тратит "лимит раунда",
            // так что следующий крит в этой же серии ударов ещё может спеть успешно.
            // Короче раунда — иначе маркер сам себе мешает (см. ApplyBuff).
            var markSungThisRound = ApplyBuff(Guids.SungThisRoundFlagGuid, toCaster: true, seconds: SungThisRoundFlagSeconds);

            // Кольцо-ауреоль — на самого исполнителя, ровно один раз за успешную песню.
            // Именно здесь, а не в BuildAbility: AbilityEffectRunAction выполняется ОТДЕЛЬНО
            // для каждой цели AoE, и наложение "на кастера" оттуда сработало бы по разу на
            // каждого союзника в радиусе. Success-ветка проверки навыка выполняется один раз.
            var spawnAureole = ApplyBuff(Guids.SongAureoleGuid, toCaster: true);

            // Кольцо/ауреоль выступления раньше спавнилось здесь отдельным разовым
            // ContextActionSpawnFx(InspireCourageAreaFx) — но это Fx самой
            // InspireCourageArea, рассчитанный на ПОСТОЯННО включённую area-effect зону
            // с собственным контроллером жизненного цикла (спавн на активации/уничтожение
            // на ForceEnd()). Спавн "в лоб", без владеющего баффа, никем не уничтожается —
            // эффект оставался навсегда (см. историю в CLAUDE.md). Теперь этот же Fx
            // назначен прямо в FxOnStart у SongBuff/SongBuffEmpowered (см. BuildSongBuff) —
            // у PrefabLink там уже есть подтверждённо рабочая очистка вместе со снятием баффа
            // (Buff.TrySpawnParticleEffect/ClearParticleEffect), отдельный экшен тут не нужен.

            // Проверка Подвижности (DC 40) — песнь звучит не на каждом крите, а только при
            // успехе. Навык именно Подвижность, а не Убеждение: песнь рождается не из голоса,
            // а из танца с клинком и поющего рассечённого воздуха (см. описания в
            // Localization.json). CheckForCaster=true: триггер уже выполняется в контексте
            // атакующего (см. ActionsOnInitiator ниже), проверяем его навык, а не цели.
            var singChance = new ContextActionSkillCheck
            {
                Stat = StatType.SkillMobility,
                CheckForCaster = true,
                UseCustomDC = true,
                CustomDC = 40,
                Success = new ActionList { Actions = new GameAction[] { castAbility, markSungThisRound, spawnAureole } },
                Failure = new ActionList()
            };

            // Не больше одной СПЕТОЙ песни за раунд, даже если в серии ударов несколько
            // критов подряд — иначе полноценная атака могла бы наложить бафф/эхо-урон
            // по несколько раз за один раунд.
            var onceThisRoundGuard = new Conditional
            {
                Comment = "Не больше одной песни за раунд",
                ConditionsChecker = new ConditionsChecker
                {
                    Operation = Operation.And,
                    Conditions = new Condition[] { CasterHasFact(Guids.SungThisRoundFlagGuid, not: true) }
                },
                IfTrue = new ActionList { Actions = new GameAction[] { singChance } },
                IfFalse = new ActionList()
            };

            var trigger = new AddInitiatorAttackWithWeaponTrigger
            {
                CriticalHit = true,
                OnlyHit = true,
                // По умолчанию действия триггера выполняются в контексте того, КОГО ударили —
                // тогда область эффекта центрировалась бы на противнике, а не на Магусе. Нам нужно
                // ровно наоборот: песнь звучит от самого атакующего.
                ActionsOnInitiator = true,
                Action = new ActionList { Actions = new GameAction[] { onceThisRoundGuard } }
            };

            // Переключатель «Разрезать небеса» здесь НЕ выдаётся, хотя штатный способ был
            // бы именно таким: AddUnitFeatureEquipment -> скрытая фича -> AddFacts.
            // Схема верная (так устроены ванильные LordProtectorEnchant и
            // ProtectionFromEvil), но у нас рвалась молча: на блюпринте зачарования
            // оба компонента присутствовали, а до персонажа не доезжала ни фича, ни
            // переключатель — и ни одного исключения мода в логе. Заменено на прямую
            // выдачу из кода по событию смены экипировки (CutTheSkiesGrant):
            // одно звено вместо четырёх, и его состояние видно в диагностике.

            var enchantment = new BlueprintWeaponEnchantment();
            enchantment.AssetGuid = BlueprintGuid.Parse(Guids.EnchantmentGuid);
            enchantment.name = "SingingBladeEnchantment";
            Reflect.Set(enchantment, "m_EnchantmentCost", 1);
            Reflect.Set(enchantment, "m_IdentifyDC", 5);
            // m_EnchantName/m_Description непустые -> зачарование само становится записью
            // в списке "Свойства" тултипа предмета (как "Святое оружие" у Faith Bearer) —
            // с собственным именем и попап-описанием по наведению.
            Reflect.Set(enchantment, "m_EnchantName", SingingBladeLocalization.CreateString(L.EnchantmentName));
            Reflect.Set(enchantment, "m_Description", SingingBladeLocalization.CreateString(L.EnchantmentDescription));
            Reflect.Set(enchantment, "m_Prefix", new LocalizedString());
            Reflect.Set(enchantment, "m_Suffix", new LocalizedString());
            enchantment.ComponentsArray = new BlueprintComponent[] { trigger };

            return enchantment;
        }

    }
}
