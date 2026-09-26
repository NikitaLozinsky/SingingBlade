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

        // Удаляет все имеющиеся экземпляры клинка и выдаёт свежий.
        //
        // Нужно потому, что факты-зачарования живут НА КОНКРЕТНОМ экземпляре предмета
        // и создаются в момент его создания. Если в блюпринт зачарования добавлен новый
        // компонент (как AddUnitFeatureEquipment для «Дотянуться до звёзд») уже ПОСЛЕ того, как
        // предмет был создан и сохранён, у старого экземпляра этого компонента в фактах
        // может просто не оказаться — он не активируется, и молча, без единой ошибки
        // в логе. Пересоздание предмета даёт заведомо свежие факты.
        public static string RegrantToPlayer()
        {
            if (Game.Instance?.Player == null)
            {
                return "Нет активной игровой сессии — загрузите сохранение.";
            }

            var blueprint = ResourcesLibrary.TryGetBlueprint<BlueprintItemWeapon>(Guids.ItemGuid);
            if (blueprint == null)
            {
                return "Блюпринт предмета не найден (мод не успел зарегистрировать блюпринты).";
            }

            var removed = 0;
            foreach (var unit in Game.Instance.Player.Party)
            {
                // ToList(): удаляем из той же коллекции, по которой идём.
                foreach (var item in unit.Inventory.Where(i => i.Blueprint == blueprint).ToList())
                {
                    unit.Inventory.Remove(item);
                    removed++;
                }
            }

            var mainCharacter = GameHelper.GetPlayerCharacter();
            if (mainCharacter == null)
            {
                return "Не найден главный персонаж партии.";
            }

            var entity = mainCharacter.Inventory.Add(blueprint);
            entity.Identify();

            return $"Убрано старых экземпляров: {removed}. Выдан новый Поющий клинок — не забудьте взять его в руки.";
        }
    }
}
