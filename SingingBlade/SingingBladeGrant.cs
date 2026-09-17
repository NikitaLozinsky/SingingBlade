using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Designers;

namespace SingingBlade
{
    // Ручная выдача предмета в инвентарь игрока — по кнопке в настройках мода
    // (см. Main.OnGUI), а НЕ хуком на каждую загрузку сейва. Хук на загрузку
    // тянул бы за собой побочный эффект на каждом старте игры без явного
    // действия игрока — кнопка надёжнее и предсказуемее.
    internal static class SingingBladeGrant
    {
        public static string GrantToPlayer()
        {
            if (Game.Instance?.Player == null)
            {
                return "Нет активной игровой сессии — загрузите сохранение.";
            }

            var itemRef = Reflect.Ref<BlueprintItemReference>(Guids.ItemGuid);

            // Идемпотентность: смотрим по факту содержимого инвентаря ВСЕЙ партии
            // (сравнение по AssetGuid предмета, см. ItemsCollection.Contains),
            // а не по отдельному флагу/факту, добавленному в сейв — так состояние
            // "выдавали или нет" не размазывается по сейву отдельно от самого
            // предмета: единственный источник истины — сам инвентарь.
            var alreadyHas = Game.Instance.Player.Party.Any(unit => unit.Inventory.Contains(itemRef));
            if (alreadyHas)
            {
                return "Поющий клинок уже есть у партии — повторно не выдаю.";
            }

            var blueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(Guids.ItemGuid);
            if (blueprint == null)
            {
                return "Блюпринт предмета не найден (мод не успел зарегистрировать блюпринты).";
            }

            var mainCharacter = GameHelper.GetPlayerCharacter();
            if (mainCharacter == null)
            {
                return "Не найден главный персонаж партии.";
            }

            var entity = mainCharacter.Inventory.Add(blueprint);
            entity.Identify();

            return "Поющий клинок выдан в инвентарь главного персонажа партии.";
        }
    }
}
