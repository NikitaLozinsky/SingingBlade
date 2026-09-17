using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;

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
