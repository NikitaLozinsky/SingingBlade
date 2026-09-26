using System;
using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

namespace SingingBlade
{
    // StartGameLoader.LoadPackTOC грузит оглавление основного пак-файла блюпринтов
    // (ResourcesLibrary.BlueprintsCache) — это самый ранний момент, когда кэш уже
    // готов принимать новые записи через AddCachedBlueprint. Раньше здесь был патч
    // на несуществующий метод ResourcesLibrary.LoadLibrary — в декомпиле такого
    // метода нет вовсе, поэтому Harmony просто не находил цель.
    [HarmonyPatch(typeof(StartGameLoader), nameof(StartGameLoader.LoadPackTOC))]
    public static class StartGameLoader_LoadPackTOC_Patch
    {
        private static bool _initialized;

        public static void Postfix()
        {
            if (_initialized) return;
            _initialized = true;

            // Постфикс выполняется ВНУТРИ загрузочной корутины игры
            // (GameStarter.StartGameCoroutine), поэтому любое исключение, выпущенное
            // отсюда наружу, убивает корутину целиком: игра навсегда зависает на 70%
            // загрузки, без окна с ошибкой и без каких-либо симптомов, кроме трейса в
            // Player.log. Ровно так и случилось из-за одной строки с неверным типом
            // enum'а в BuildSongArea. Мод не должен уметь блокировать запуск игры:
            // ловим, пишем в лог и в окно настроек UMM, даём игре стартовать без мода.
            try
            {
                SingingBladeBlueprints.Create();
            }
            catch (Exception e)
            {
                Main.BlueprintsFailed = true;
                Main.LogError("SingingBladeBlueprints.Create", e);
                return;
            }

            // Подписки боевого конвейера включаем только если блюпринты собрались:
            // без них способности «Дотянуться до звёзд» всё равно не на что ссылаться.
            ReachForStars.Subscribe();
        }
    }

    // «Дотянуться до звёзд»: перехват каста лучевого заклинания, чтобы вместо дальнобойной
    // атаки касанием персонаж ударил клинком, а заклинание ушло вместе с ударом.
    // Точка та же, что использует ванильный Эльдричский лучник (там этот код стоит
    // прямо в теле OnAction и требует firstWeapon.Blueprint.IsRanged).
    //
    // Префикс сознательно НЕ подменяет ветки отказа: если каст по какой-то причине
    // невозможен, TryInterceptCast возвращает false, и дальше работает ванильный
    // OnAction со всеми своими проверками и FX прерывания.
    [HarmonyPatch(typeof(UnitUseAbility), "OnAction")]
    public static class UnitUseAbility_OnAction_Patch
    {
        public static bool Prefix(UnitUseAbility __instance, ref UnitCommand.ResultType __result)
        {
            if (!Main.Enabled) return true;
            return !ReachForStars.TryInterceptCast(__instance, ref __result);
        }
    }

    // Зона угрозы (внеочередные атаки и сцепка в ближнем бою) считается от той же
    // дальности оружия, что и сам удар: UnitHelper.GetThreatRange возвращает
    // hand.Weapon.AttackRange.Meters, а наша способность «Дотянуться до звёзд» эту дальность как раз и
    // удлиняет бонусом к стату Reach. Без этого патча магус угрожал бы и бил
    // внеочередными атаками на всю дистанцию дистанционного удара — пользователь
    // просил зону не раздувать, поэтому здесь мы вычитаем ровно свой вклад обратно.
    //
    // Точка выбрана одна и самая узкая: GetThreatRange — единственное место, откуда
    // зону угрозы берёт система сцепки (UnitCombatEngagementController). Остальные
    // потребители дальности оружия (UnitEngagementExtension.IsReach: предпросмотр
    // угрозы при движении в пошаговом режиме, Рубящий удар, парирование дуэлянта)
    // намеренно НЕ трогаем — там честная дальность оружия уместна, а лезть в них
    // значило бы размазывать патчи по чужому боевому конвейеру.
    [HarmonyPatch(typeof(UnitHelper), nameof(UnitHelper.GetThreatRange))]
    public static class UnitHelper_GetThreatRange_Patch
    {
        // try/catch обязателен: это постфикс на ГОРЯЧЕМ методе боевого конвейера
        // (зону угрозы движок спрашивает постоянно, для каждого юнита). Любое исключение
        // отсюда — это не одна ошибка, а поток ошибок и сломанная сцепка в ближнем бою
        // у всех участников боя.
        public static void Postfix(UnitEntityData unit, ref float? __result)
        {
            if (!Main.Enabled) return;

            try
            {
                ReachForStars.TrimThreatRange(unit, ref __result);
            }
            catch (Exception e)
            {
                Main.LogError("UnitHelper.GetThreatRange postfix", e);
            }
        }
    }

    // LoadPack(string, Locale) — единая точка, через которую загружается и первый
    // языковой пак при старте игры, и любой пак при смене языка в настройках
    // (приватный LoadPack(Locale) внутри LocalizationManager сам вызывает эту
    // перегрузку). Патчим один раз здесь, а не только при старте, чтобы наши
    // строки не терялись при переключении языка.
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.LoadPack), typeof(string), typeof(Locale))]
    public static class LocalizationManager_LoadPack_Patch
    {
        public static void Postfix(LocalizationPack __result, Locale locale)
        {
            if (__result == null) return;

            SingingBladeLocalization.Apply(__result, locale);
        }
    }
}
