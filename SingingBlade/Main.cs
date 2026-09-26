using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace SingingBlade
{
    public static class Main
    {
        public static Harmony HarmonyInstance;
        public static bool Enabled;

        // Подробный лог. ВЫКЛЮЧЕН по умолчанию: трассировка команд пишет по две строки на
        // каждое действие носителя клинка, и в обычной игре это десятки строк за бой —
        // Player.log пухнет, а полезное в нём тонет. Ошибки и причины отказа перехвата
        // пишутся всегда, независимо от этой галки.
        public static bool Verbose;

        // Не удалось применить патчи Harmony (например, игра обновилась и метода больше
        // нет). Мод при этом не должен падать целиком — он просто ничего не делает, а в
        // окне настроек видно, что случилось.
        public static bool PatchesFailed;

        // Взводится, если сборка блюпринтов упала на старте игры (см.
        // StartGameLoader_LoadPackTOC_Patch). Игра при этом запускается, но мода в ней
        // фактически нет — сообщаем об этом прямо в окне настроек, а не только в логе.
        public static bool BlueprintsFailed;

        private static UnityModManager.ModEntry _modEntry;

        private static string _grantStatus = "";

        // точка входа, указана в Info.json
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            _modEntry = modEntry;
            HarmonyInstance = new Harmony(modEntry.Info.Id);

            // PatchAll бросает, если целевого метода нет (обновление игры, конфликт с
            // другим модом). Без обёртки такое исключение уходит в UMM и мод не грузится
            // вовсе — вместе с безобидными частями вроде локализации.
            try
            {
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                PatchesFailed = true;
                LogError("Harmony.PatchAll", e);
            }

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            Enabled = true;

            return true;
        }

        // Пишет в лог UMM (и в Player.log). Нужен коду, который вмешивается в боевой
        // конвейер: там любое необработанное исключение ломает игроку ход, поэтому
        // мы ловим его, откатываемся на ванильное поведение и оставляем след в логе.
        public static void LogError(string where, Exception e)
        {
            _modEntry?.Logger?.Error($"{where}: {e}");
        }

        // Обычная строка в лог UMM (и в Player.log). Нужна там, где поведение зависит от
        // состояния пошагового боя, которое снаружи не видно: без такой строки разбирать
        // "почему персонаж не дошёл" можно только гаданием.
        public static void Log(string message)
        {
            _modEntry?.Logger?.Log(message);
        }

        // Трассировка для разбора пошагового боя. Пишется только при включённой галке
        // "Подробный лог" — см. Verbose.
        public static void LogVerbose(string message)
        {
            if (!Verbose) return;
            _modEntry?.Logger?.Log(message);
        }

        // Включение/выключение мода в окне UMM. Важно не только поднять флаг: выданные
        // персонажу факты (способность, фича «Грозы Элизиума»), бафф режима и растяжка
        // досягаемости живут на юните и сами никуда не денутся. Выключенный мод должен
        // переставать влиять на игру целиком, иначе игрок выключает его, сохраняется — и
        // уносит в сейв то, чего уже некому обслуживать.
        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            ReachForStars.OnModToggled(value);
            return true;
        }

        // Окно настроек мода в UMM (Ctrl+F10 в игре) — единственная точка выдачи
        // предмета игроку, никакого автохука на загрузку сейва (см. SingingBladeGrant.cs).
        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Поющий клинок — уникальный скимитар для Магуса.");

            if (BlueprintsFailed)
            {
                GUILayout.Label("ОШИБКА: блюпринты мода не собрались при запуске игры. " +
                                "Мод не работает, подробности — в Player.log (строка [SingingBlade]).");
            }

            if (PatchesFailed)
            {
                GUILayout.Label("ОШИБКА: не применились патчи Harmony — механика клинка работать не будет. " +
                                "Подробности в Player.log (строка [SingingBlade]).");
            }

            Verbose = GUILayout.Toggle(Verbose, "Подробный лог (трассировка команд в Player.log)");

            if (GUILayout.Button("Выдать Поющий клинок в инвентарь партии", GUILayout.ExpandWidth(false)))
            {
                _grantStatus = SingingBladeGrant.GrantToPlayer();
            }

            // Пересоздание предмета: факты-зачарования живут на конкретном экземпляре и
            // создаются вместе с ним, поэтому компоненты, добавленные в блюпринт позже,
            // на старом экземпляре могут не активироваться (см. SingingBladeGrant).
            if (GUILayout.Button("Пересоздать клинок (убрать старый, выдать свежий)", GUILayout.ExpandWidth(false)))
            {
                _grantStatus = SingingBladeGrant.RegrantToPlayer();
            }

            if (GUILayout.Button("Диагностика: что доехало до персонажа", GUILayout.ExpandWidth(false)))
            {
                _grantStatus = SingingBladeDiagnostics.Report();
            }

            if (!string.IsNullOrEmpty(_grantStatus))
            {
                GUILayout.Label(_grantStatus);
            }
        }
    }
}