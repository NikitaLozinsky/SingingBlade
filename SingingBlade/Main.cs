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

        private static UnityModManager.ModEntry _modEntry;

        private static string _grantStatus = "";

        // точка входа, указана в Info.json
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            _modEntry = modEntry;
            HarmonyInstance = new Harmony(modEntry.Info.Id);
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

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

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }

        // Окно настроек мода в UMM (Ctrl+F10 в игре) — единственная точка выдачи
        // предмета игроку, никакого автохука на загрузку сейва (см. SingingBladeGrant.cs).
        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Поющий клинок — уникальный скимитар для Магуса.");

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