using System;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Items.Slots;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

namespace SingingBlade
{
    // Подписчик на правило атаки оружием. Отдельным классом, а не патчем на
    // MagusController: движок сам рассылает событие всем подписчикам, патчить
    // чужой метод незачем — меньше шансов подраться с другими модами.
    internal class ReachForStarsDelivery : IGlobalRulebookHandler<RuleAttackWithWeapon>
    {
        public void OnEventAboutToTrigger(RuleAttackWithWeapon evt)
        {
        }

        // Событие приходит на КАЖДУЮ атаку оружием любого юнита в игре, поэтому первым
        // делом — самая дешёвая проверка: лежит ли вообще что-то "на клинке".
        public void OnEventDidTrigger(RuleAttackWithWeapon evt)
        {
            if (!Main.Enabled || !ReachForStars.HasSpellPending) return;
            ReachForStars.OnWeaponAttackResolved(evt);
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
    internal class ReachForStarsGrant : IUnitEquipmentHandler, IAreaHandler
    {
        public void HandleEquipmentSlotUpdated(ItemSlot slot, ItemEntity previousItem)
        {
            if (!Main.Enabled) return;
            ReachForStars.RefreshToggle(slot?.Owner?.Unit);
        }

        // Смена экипировки при загрузке сейва события может не прислать, поэтому
        // дополнительно проходим по партии при загрузке области.
        public void OnAreaDidLoad()
        {
            if (!Main.Enabled) return;

            // Состояние сбрасываем ПЕРЕД обновлением фактов: в статиках лежит ссылка на
            // юнита из прошлой сессии (заклинание "на клинке" у персонажа, которого в этой
            // загрузке может не быть вовсе). Держать её незачем, а спутать — можно.
            ReachForStars.Clear();
            ReachForStars.RefreshParty();
        }

        public void OnAreaBeginUnloading()
        {
        }
    }

    // Начало хода в пошаговом бою: всё, что мод обещает "до конца этого раунда", гасится
    // здесь явно, а не таймером (см. ReachForStars.OnTurnStarted — почему таймера мало).
    internal class ReachForStarsTurns : ITurnBasedModeHandler
    {
        public void HandleTurnStarted(UnitEntityData unit)
        {
            if (!Main.Enabled) return;
            ReachForStars.OnTurnStarted(unit);
        }

        public void HandleSurpriseRoundStarted()
        {
        }

        public void HandleRoundStarted(int round)
        {
        }

        public void HandleUnitControlChanged(UnitEntityData unit)
        {
        }

        public void HandleUnitNotSurprised(UnitEntityData unit, RuleSkillCheck perceptionCheck)
        {
        }
    }

    // Выдаёт бесплатную атаку, пока заклинание лежит "на клинке", и пишет в лог судьбу
    // команды атаки.
    //
    // Зачем: в пошаговом бою атака после потраченного основного действия отбивается
    // движком в UnitCommandController.TickCommandTurnBased —
    //     flag3 = command.IsIgnoreCooldown || (CanActInCombat && !HasCooldownForCommand(command));
    //     if (!num || !flag3) { command.ForceFinishForTurnBased(Success); Queue.Clear(); }
    // то есть команда молча "завершается успехом", не ударив. Именно так выглядел баг
    // "подходит к врагу и не атакует".
    //
    // Ванильный магус обходит это ровно здесь же: MagusController.HandleUnitRunCommand
    // на старте команды зовёт unitAttack.IgnoreCooldown(...), если движок знает, что у
    // магуса подготовлен Заклинательный удар (PreparedSpellStrike / PreparedSpellCombat).
    // Наш "карман" движку не виден, поэтому ту же услугу оказываем себе сами — тем же
    // способом и в той же точке жизненного цикла.
    internal class ReachForStarsCommands : IUnitRunCommandHandler, IUnitCommandStartHandler, IUnitCommandEndHandler
    {
        public void HandleUnitRunCommand(UnitCommand cmd)
        {
            try
            {
                if (!(cmd is UnitAttack attack)) return;
                if (!ReachForStars.HasSpellOnBlade(cmd.Executor)) return;

                // Ровно одна атака оружием в дополнение к заклинанию — как у
                // Заклинательного удара (и как у нашей собственной команды).
                attack.IsSingleAttack = true;
                attack.IgnoreCooldown();
                Main.LogVerbose("ReachForStars: атаке разрешён бесплатный удар (заклинание на клинке)");
            }
            catch (Exception e)
            {
                Main.LogError("ReachForStars.HandleUnitRunCommand", e);
            }
        }

        // Диагностика: без неё судьбу команды в пошаговом режиме не отследить — движок
        // гасит её через ForceFinishForTurnBased(Success), то есть без ошибки в логе и
        // без внешних признаков. Пишем по носителю клинка, а не по "есть ли заклинание
        // в кармане": иначе не увидеть как раз те случаи, где карман уже опустел.
        public void HandleUnitCommandDidStart(UnitCommand cmd)
        {
            Trace(cmd, "стартовала");
        }

        public void HandleUnitCommandDidEnd(UnitCommand cmd)
        {
            Trace(cmd, "завершилась");

            // Страховка от залипшей досягаемости. Штатно добавку снимает Clear(), когда
            // заклинание уходит с клинка, но команда может закончиться и без удара —
            // прервали, цель умерла, игрок отменил. Если на клинке уже пусто, добавке
            // тем более неоткуда взяться, значит её пора убрать.
            try
            {
                if (cmd?.Executor != null
                    && ReachForStars.HoldsSingingBlade(cmd.Executor)
                    && !ReachForStars.HasSpellOnBlade(cmd.Executor))
                {
                    ReachForStars.DropExtendedReach();
                }
            }
            catch (Exception e)
            {
                Main.LogError("ReachForStars.HandleUnitCommandDidEnd", e);
            }
        }

        private static void Trace(UnitCommand cmd, string what)
        {
            try
            {
                // Галку проверяем ПЕРВОЙ: без неё не надо даже трогать экипировку —
                // событие приходит на каждую команду каждого юнита.
                if (cmd == null || !Main.Verbose) return;
                if (!ReachForStars.HoldsSingingBlade(cmd.Executor)) return;

                var turn = Game.Instance.TurnBasedCombatController?.CurrentTurn;
                Main.LogVerbose($"ReachForStars: команда {cmd.GetType().Name} ({cmd.Type}) {what}: " +
                         $"Result={cmd.Result}, стартовала={cmd.IsStarted}, " +
                         $"безКулдауна={cmd.IsIgnoreCooldown}, нуженПодход={cmd.ShouldUnitApproach}, " +
                         $"ходДействует={(turn != null ? turn.IsActing.ToString() : "нет хода")}, " +
                         $"заклинаниеНаКлинке={ReachForStars.HasSpellOnBlade(cmd.Executor)}");
            }
            catch (Exception e)
            {
                Main.LogError("ReachForStars.Trace", e);
            }
        }
    }

}
