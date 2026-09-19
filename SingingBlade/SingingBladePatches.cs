using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
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

            SingingBladeBlueprints.Create();
            SustainedNote.Subscribe();
        }
    }

    // "Долгая нота": перехват каста лучевого заклинания, чтобы вместо дальнобойной
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
            return !SustainedNote.TryInterceptCast(__instance, ref __result);
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
