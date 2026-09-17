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

        private static string _grantStatus = "";

        // точка входа, указана в Info.json
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            HarmonyInstance = new Harmony(modEntry.Info.Id);
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            Enabled = true;

            return true;
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

            if (!string.IsNullOrEmpty(_grantStatus))
            {
                GUILayout.Label(_grantStatus);
            }
        }
    }
}