using LabApi;
using LabApi.Events;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.Scp079Events;
using LabApi.Events.Arguments.Scp096Events;
using LabApi.Events.Arguments.Scp914Events;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Arguments.WarheadEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using UnityEngine;
using PlayerRoles;

namespace DiscordLogger
{
    public class DiscordLoggerPlugin : Plugin
    {
        // Метаданные плагина
        public override string Name { get; } = "Discord Logger";
        public override string Description { get; } = "Полное логирование всех действий сервера в Discord";
        public override string Author { get; } = "vityanvsk";
        public override Version Version { get; } = new Version(1, 2, 0, 0);
        public override Version RequiredApiVersion { get; } = new Version(LabApiProperties.CompiledVersion);
        public override LoadPriority Priority { get; } = LoadPriority.Low;

        // Настройки
        public static readonly string WebhookUrl = "https://discord.com/api/webhooks/1449102193423548576/1qeKAP3VE6jqmQa7z6SUp2GuvBt79zlF8rgOQN3zkpQScJqfIDqbrItdRrrqom7OxrGP";
        public static readonly string ServerName = "SCP:SL Server";
        public static readonly bool EnableDebugLogs = true;
        public static readonly int RateLimitDelay = 500;

        // Внутренние переменные
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly Queue<string> messageQueue = new Queue<string>();
        private static DateTime lastSendTime = DateTime.MinValue;
        private static readonly object queueLock = new object();
        private static readonly Dictionary<string, DateTime> playerJoinTimes = new Dictionary<string, DateTime>();
        private static readonly Dictionary<string, float> playerHealthTracker = new Dictionary<string, float>();
        private static readonly HashSet<string> processedEscapes = new HashSet<string>();
        private static readonly Dictionary<string, Vector3> lastPlayerPositions = new Dictionary<string, Vector3>();

        public override void Enable()
        {
            if (string.IsNullOrEmpty(WebhookUrl) || WebhookUrl == "YOUR_WEBHOOK_URL_HERE")
            {
                ServerConsole.AddLog("[DiscordLogger] Discord Webhook URL не настроен!", ConsoleColor.Red);
                return;
            }

            RegisterAllEvents();
            SendDiscordMessage("✅ **Логгер запущен**", $"Сервер **{ServerName}** начал логирование", 3066993);
            ServerConsole.AddLog("[DiscordLogger] Плагин успешно загружен!", ConsoleColor.Green);
        }

        public override void Disable()
        {
            UnregisterAllEvents();
            SendDiscordMessage("🔴 **Логгер остановлен**", $"Сервер **{ServerName}** прекратил логирование", 15158332);
            httpClient?.Dispose();
            ServerConsole.AddLog("[DiscordLogger] Плагин выгружен", ConsoleColor.Yellow);
        }

        private void RegisterAllEvents()
        {
            // События игрока (только существующие в API)
            PlayerEvents.Joined += OnPlayerJoined;
            PlayerEvents.Left += OnPlayerLeft;
            PlayerEvents.Dying += OnPlayerDying;
            PlayerEvents.ChangedRole += OnPlayerChangedRole;
            PlayerEvents.Spawned += OnPlayerSpawned;
            PlayerEvents.Hurt += OnPlayerHurt;
            PlayerEvents.UsedItem += OnPlayerUsedItem;
            PlayerEvents.PickedUpItem += OnPlayerPickedUpItem;
            PlayerEvents.DroppedItem += OnPlayerDroppedItem;
            PlayerEvents.Escaping += OnPlayerEscaping;
            PlayerEvents.Kicked += OnPlayerKicked;
            PlayerEvents.Banned += OnPlayerBanned;
            PlayerEvents.InteractingDoor += OnInteractingDoor;
            PlayerEvents.InteractingElevator += OnInteractingElevator;
            PlayerEvents.InteractingLocker += OnInteractingLocker;
            PlayerEvents.TriggeringTesla += OnTriggeringTesla;
            PlayerEvents.EnteringPocketDimension += OnEnteringPocketDimension;
            PlayerEvents.ReloadingWeapon += OnReloadingWeapon;
            PlayerEvents.InteractingGenerator += OnInteractingGenerator;
            PlayerEvents.ChangingItem += OnChangingItem;
            PlayerEvents.TogglingFlashlight += OnTogglingFlashlight;
            PlayerEvents.SearchingPickup += OnSearchingPickup;
            PlayerEvents.ThrowingItem += OnThrowingItem;
            PlayerEvents.ChangingNickname += OnChangingNickname;

            // События сервера
            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.RoundEnded += OnRoundEnded;
            ServerEvents.WaitingForPlayers += OnWaitingForPlayers;

            // События боеголовки
            WarheadEvents.Starting += OnWarheadStarting;
            WarheadEvents.Stopping += OnWarheadStopping;
            WarheadEvents.Detonating += OnWarheadDetonating;

            // События SCP-914
            Scp914Events.Activating += OnScp914Activating;
            Scp914Events.KnobChanged += OnScp914KnobChanged;
            Scp914Events.ProcessingPlayer += OnScp914ProcessingPlayer;

            // События SCP-079
            Scp079Events.GainingExperience += OnScp079GainingExperience;
            Scp079Events.ChangingCamera += OnScp079ChangingCamera;

            // События SCP-096
            Scp096Events.AddingTarget += OnScp096AddingTarget;
            Scp096Events.Enraging += OnScp096Enraging;
        }

        private void UnregisterAllEvents()
        {
            // События игрока
            PlayerEvents.Joined -= OnPlayerJoined;
            PlayerEvents.Left -= OnPlayerLeft;
            PlayerEvents.Dying -= OnPlayerDying;
            PlayerEvents.ChangedRole -= OnPlayerChangedRole;
            PlayerEvents.Spawned -= OnPlayerSpawned;
            PlayerEvents.Hurt -= OnPlayerHurt;
            PlayerEvents.UsedItem -= OnPlayerUsedItem;
            PlayerEvents.PickedUpItem -= OnPlayerPickedUpItem;
            PlayerEvents.DroppedItem -= OnPlayerDroppedItem;
            PlayerEvents.Escaping -= OnPlayerEscaping;
            PlayerEvents.Kicked -= OnPlayerKicked;
            PlayerEvents.Banned -= OnPlayerBanned;
            PlayerEvents.InteractingDoor -= OnInteractingDoor;
            PlayerEvents.InteractingElevator -= OnInteractingElevator;
            PlayerEvents.InteractingLocker -= OnInteractingLocker;
            PlayerEvents.TriggeringTesla -= OnTriggeringTesla;
            PlayerEvents.EnteringPocketDimension -= OnEnteringPocketDimension;
            PlayerEvents.ReloadingWeapon -= OnReloadingWeapon;
            PlayerEvents.InteractingGenerator -= OnInteractingGenerator;
            PlayerEvents.ChangingItem -= OnChangingItem;
            PlayerEvents.TogglingFlashlight -= OnTogglingFlashlight;
            PlayerEvents.SearchingPickup -= OnSearchingPickup;
            PlayerEvents.ThrowingItem -= OnThrowingItem;
            PlayerEvents.ChangingNickname -= OnChangingNickname;

            // События сервера
            ServerEvents.RoundStarted -= OnRoundStarted;
            ServerEvents.RoundEnded -= OnRoundEnded;
            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;

            // События боеголовки
            WarheadEvents.Starting -= OnWarheadStarting;
            WarheadEvents.Stopping -= OnWarheadStopping;
            WarheadEvents.Detonating -= OnWarheadDetonating;

            // События SCP-914
            Scp914Events.Activating -= OnScp914Activating;
            Scp914Events.KnobChanged -= OnScp914KnobChanged;
            Scp914Events.ProcessingPlayer -= OnScp914ProcessingPlayer;

            // События SCP
            Scp079Events.GainingExperience -= OnScp079GainingExperience;
            Scp079Events.ChangingCamera -= OnScp079ChangingCamera;
            Scp096Events.AddingTarget -= OnScp096AddingTarget;
            Scp096Events.Enraging -= OnScp096Enraging;
        }

        // Вспомогательный метод для получения уникального идентификатора игрока
        private string GetPlayerId(Player player)
        {
            if (player == null)
                return "Unknown";

            return player.UserId ?? player.Nickname ?? player.GetHashCode().ToString();
        }

        // Вспомогательный метод для получения читаемого названия роли
        private string GetRoleName(object role)
        {
            if (role == null) return "Неизвестно";

            string roleStr = role.ToString();

            // Если это PlayerRoleBase, извлекаем RoleTypeId
            if (roleStr.Contains("RoleTypeId"))
            {
                var start = roleStr.IndexOf("RoleTypeId = '") + "RoleTypeId = '".Length;
                var end = roleStr.IndexOf("'", start);
                if (start > 0 && end > start)
                {
                    roleStr = roleStr.Substring(start, end - start);
                }
            }

            // Используем обычный switch для C# 7.3
            switch (roleStr)
            {
                case "ClassD": return "D-Класс";
                case "Scientist": return "Учёный";
                case "FacilityGuard": return "Охранник";
                case "NtfPrivate": return "MTF Рядовой";
                case "NtfSergeant": return "MTF Сержант";
                case "NtfCaptain": return "MTF Капитан";
                case "NtfSpecialist": return "MTF Специалист";
                case "ChaosConscript": return "Повстанец Хаоса";
                case "ChaosRifleman": return "Стрелок Хаоса";
                case "ChaosRepressor": return "Подавитель Хаоса";
                case "ChaosMarauder": return "Мародёр Хаоса";
                case "Scp049": return "SCP-049";
                case "Scp0492": return "SCP-049-2 (Зомби)";
                case "Scp079": return "SCP-079";
                case "Scp096": return "SCP-096";
                case "Scp106": return "SCP-106";
                case "Scp173": return "SCP-173";
                case "Scp939": return "SCP-939";
                case "Scp3114": return "SCP-3114";
                case "Spectator": return "Наблюдатель";
                case "None": return "Нет роли";
                case "Tutorial": return "Обучение";
                default: return roleStr;
            }
        }

        // Вспомогательный метод для получения названия предмета
        private string GetItemName(ItemType itemType)
        {
            switch (itemType)
            {
                case ItemType.KeycardJanitor: return "Карта уборщика";
                case ItemType.KeycardScientist: return "Карта учёного";
                case ItemType.KeycardResearchCoordinator: return "Карта координатора";
                case ItemType.KeycardZoneManager: return "Карта менеджера зоны";
                case ItemType.KeycardGuard: return "Карта охранника";
                case ItemType.KeycardMTFPrivate: return "Карта MTF рядового";
                case ItemType.KeycardContainmentEngineer: return "Карта инженера";
                case ItemType.KeycardMTFOperative: return "Карта MTF оперативника";
                case ItemType.KeycardMTFCaptain: return "Карта MTF капитана";
                case ItemType.KeycardFacilityManager: return "Карта менеджера объекта";
                case ItemType.KeycardChaosInsurgency: return "Карта Повстанцев Хаоса";
                case ItemType.KeycardO5: return "Карта O5";
                case ItemType.Radio: return "Рация";
                case ItemType.GunCOM15: return "COM-15";
                case ItemType.Medkit: return "Аптечка";
                case ItemType.Flashlight: return "Фонарик";
                case ItemType.MicroHID: return "Micro-HID";
                case ItemType.SCP500: return "SCP-500";
                case ItemType.SCP207: return "SCP-207";
                case ItemType.Ammo12gauge: return "Патроны 12 калибра";
                case ItemType.GunE11SR: return "E11-SR";
                case ItemType.GunCrossvec: return "Crossvec";
                case ItemType.Ammo556x45: return "Патроны 5.56";
                case ItemType.GunFSP9: return "FSP-9";
                case ItemType.GunLogicer: return "Logicer";
                case ItemType.GrenadeHE: return "Осколочная граната";
                case ItemType.GrenadeFlash: return "Светошумовая граната";
                case ItemType.Ammo44cal: return "Патроны .44";
                case ItemType.Ammo762x39: return "Патроны 7.62";
                case ItemType.Ammo9x19: return "Патроны 9mm";
                case ItemType.GunCOM18: return "COM-18";
                case ItemType.SCP018: return "SCP-018";
                case ItemType.SCP268: return "SCP-268";
                case ItemType.Adrenaline: return "Адреналин";
                case ItemType.Painkillers: return "Обезболивающее";
                case ItemType.Coin: return "Монета";
                case ItemType.ArmorLight: return "Лёгкая броня";
                case ItemType.ArmorCombat: return "Боевая броня";
                case ItemType.ArmorHeavy: return "Тяжёлая броня";
                case ItemType.GunRevolver: return "Револьвер";
                case ItemType.GunAK: return "AK";
                case ItemType.GunShotgun: return "Дробовик";
                case ItemType.SCP330: return "SCP-330";
                case ItemType.SCP2176: return "SCP-2176";
                case ItemType.SCP244a: return "SCP-244-A";
                case ItemType.SCP244b: return "SCP-244-B";
                case ItemType.SCP1853: return "SCP-1853";
                case ItemType.ParticleDisruptor: return "Дезинтегратор частиц";
                case ItemType.GunCom45: return "COM-45";
                case ItemType.SCP1576: return "SCP-1576";
                case ItemType.Jailbird: return "Jailbird";
                case ItemType.AntiSCP207: return "Анти-SCP-207";
                case ItemType.GunFRMG0: return "FR-MG-0";
                case ItemType.GunA7: return "A7";
                case ItemType.Lantern: return "Фонарь";
                default: return itemType.ToString();
            }
        }

        // Вспомогательный метод для получения названия зоны
        private string GetZoneName(object zone)
        {
            if (zone == null) return "Неизвестно";

            string zoneStr = zone.ToString();
            switch (zoneStr)
            {
                case "LightContainment": return "Лёгкая зона";
                case "HeavyContainment": return "Тяжёлая зона";
                case "Entrance": return "Входная зона";
                case "Surface": return "Поверхность";
                case "Other": return "Другое";
                case "None": return "Неизвестно";
                default: return zoneStr;
            }
        }

        // События игроков с детальной информацией
        private void OnPlayerJoined(PlayerJoinedEventArgs ev)
        {
            playerJoinTimes[ev.Player.UserId] = DateTime.Now;
            lastPlayerPositions[ev.Player.UserId] = ev.Player.Position;
            LogAction($"➡️ **{ev.Player.Nickname}** присоединился к серверу");
        }

        private void OnPlayerLeft(PlayerLeftEventArgs ev)
        {
            var playTime = "Неизвестно";
            if (playerJoinTimes.ContainsKey(ev.Player.UserId))
            {
                playTime = Math.Round((DateTime.Now - playerJoinTimes[ev.Player.UserId]).TotalMinutes, 1) + " минут";
                playerJoinTimes.Remove(ev.Player.UserId);
            }

            processedEscapes.Remove(ev.Player.UserId);
            lastPlayerPositions.Remove(ev.Player.UserId);

            LogAction($"⬅️ **{ev.Player.Nickname}** покинул сервер | Играл: {playTime}");
        }

        private void OnPlayerDying(PlayerDyingEventArgs ev)
        {
            var attackerName = ev.Attacker?.Nickname ?? "Неизвестно";
            var attackerRole = GetRoleName(ev.Attacker?.Role);
            var victimRole = GetRoleName(ev.Player.Role);

            LogAction($"💀 **{ev.Player.Nickname}** ({victimRole}) убит игроком **{attackerName}** ({attackerRole})");
        }

        private void OnPlayerChangedRole(PlayerChangedRoleEventArgs ev)
        {
            // Пропускаем если это часть события побега
            string escapeKey = $"{ev.Player.UserId}_escape";
            if (processedEscapes.Contains(escapeKey))
            {
                processedEscapes.Remove(escapeKey);
                return;
            }

            var oldRole = GetRoleName(ev.OldRole);
            var newRole = GetRoleName(ev.NewRole);

            // Пропускаем технические смены ролей
            if (oldRole == newRole || newRole == "Нет роли")
                return;

            LogAction($"🔄 **{ev.Player.Nickname}** сменил роль с {oldRole} на {newRole}");
        }

        private void OnPlayerSpawned(PlayerSpawnedEventArgs ev)
        {
            var role = GetRoleName(ev.Role);
            var zone = GetZoneName(ev.Player.Zone);

            // Пропускаем спавн после побега
            string escapeKey = $"{ev.Player.UserId}_escape";
            if (processedEscapes.Contains(escapeKey))
            {
                return;
            }

            LogAction($"🎮 **{ev.Player.Nickname}** заспавнился как {role} | Зона: {zone}");

            // Сохраняем здоровье игрока для отслеживания урона
            string playerId = GetPlayerId(ev.Player);
            playerHealthTracker[playerId] = ev.Player.Health;
        }

        private void OnPlayerHurt(PlayerHurtEventArgs ev)
        {
            var attackerName = ev.Attacker?.Nickname ?? "Окружение";

            // Рассчитываем урон на основе изменения здоровья
            string playerId = GetPlayerId(ev.Player);
            float previousHealth = playerHealthTracker.ContainsKey(playerId) ? playerHealthTracker[playerId] : ev.Player.MaxHealth;
            float damage = previousHealth - ev.Player.Health;
            playerHealthTracker[playerId] = ev.Player.Health;

            if (damage > 0)
            {
                LogAction($"🩸 **{ev.Player.Nickname}** получил {damage:F0} урона от **{attackerName}** | HP: {ev.Player.Health:F0}/{ev.Player.MaxHealth:F0}");
            }
        }

        private void OnPlayerUsedItem(PlayerUsedItemEventArgs ev)
        {
            var itemName = GetItemName(ev.UsableItem?.Type ?? ItemType.None);
            LogAction($"💊 **{ev.Player.Nickname}** использовал {itemName}");
        }

        private void OnPlayerPickedUpItem(PlayerPickedUpItemEventArgs ev)
        {
            // Используем общее название, так как нет доступа к типу предмета
            var itemName = "Предмет";
            var zone = GetZoneName(ev.Player.Zone);
            LogAction($"🤏 **{ev.Player.Nickname}** подобрал {itemName} | Зона: {zone}");
        }

        private void OnPlayerDroppedItem(PlayerDroppedItemEventArgs ev)
        {
            // Получаем информацию о предмете из события
            var itemName = "Предмет";
            var zone = GetZoneName(ev.Player.Zone);
            LogAction($"👋 **{ev.Player.Nickname}** выбросил {itemName} | Зона: {zone}");
        }

        private void OnPlayerEscaping(PlayerEscapingEventArgs ev)
        {
            var oldRole = GetRoleName(ev.Player.Role);
            var newRole = GetRoleName(ev.NewRole);

            // Помечаем что обработали побег этого игрока
            string escapeKey = $"{ev.Player.UserId}_escape";
            processedEscapes.Add(escapeKey);

            // Пропускаем если роли не изменились
            if (oldRole == newRole || newRole == "Нет роли" || newRole == "Неизвестно")
                return;

            LogAction($"🏃 **{ev.Player.Nickname}** сбежал | Был: {oldRole}, Стал: {newRole}");
        }

        private void OnPlayerKicked(PlayerKickedEventArgs ev)
        {
            var issuer = ev.Issuer?.Nickname ?? "Сервер";
            LogAction($"🚫 **{issuer}** кикнул **{ev.Player.Nickname}** | Причина: {ev.Reason}");
        }

        private void OnPlayerBanned(PlayerBannedEventArgs ev)
        {
            var issuerName = ev.Issuer?.Nickname ?? "Сервер";
            var duration = ev.Duration > 0 ? $"{ev.Duration} минут" : "Перманентно";
            LogAction($"🔨 **{issuerName}** забанил **{ev.Player.Nickname}** на {duration} | Причина: {ev.Reason}");
        }

        private void OnInteractingDoor(PlayerInteractingDoorEventArgs ev)
        {
            var zone = GetZoneName(ev.Player.Zone);
            var doorState = ev.Door?.IsOpened == true ? "закрывает" : "открывает";
            LogAction($"🚪 **{ev.Player.Nickname}** {doorState} дверь | Зона: {zone}");
        }

        private void OnInteractingElevator(PlayerInteractingElevatorEventArgs ev)
        {
            LogAction($"🛗 **{ev.Player.Nickname}** использует лифт");
        }

        private void OnInteractingLocker(PlayerInteractingLockerEventArgs ev)
        {
            var zone = GetZoneName(ev.Player.Zone);
            LogAction($"🗄️ **{ev.Player.Nickname}** открывает шкафчик | Зона: {zone}");
        }

        private void OnTriggeringTesla(PlayerTriggeringTeslaEventArgs ev)
        {
            LogAction($"⚡ **{ev.Player.Nickname}** активировал теслу");
        }

        private void OnEnteringPocketDimension(PlayerEnteringPocketDimensionEventArgs ev)
        {
            LogAction($"🌀 **{ev.Player.Nickname}** попал в карманное измерение");
        }

        private void OnReloadingWeapon(PlayerReloadingWeaponEventArgs ev)
        {
            // Получаем название оружия из текущего предмета игрока
            var weaponName = "Оружие";
            if (ev.Player.CurrentItem != null)
            {
                weaponName = GetItemName(ev.Player.CurrentItem.Type);
            }
            LogAction($"🔫 **{ev.Player.Nickname}** перезаряжает {weaponName}");
        }

        private void OnInteractingGenerator(PlayerInteractingGeneratorEventArgs ev)
        {
            LogAction($"⚙️ **{ev.Player.Nickname}** взаимодействует с генератором");
        }

        private void OnChangingItem(PlayerChangingItemEventArgs ev)
        {
            // Пропускаем если меняем на пустоту или с пустоты
            if ((ev.OldItem == null || ev.OldItem.Type == ItemType.None) ||
                (ev.NewItem == null || ev.NewItem.Type == ItemType.None))
            {
                return;
            }

            var oldItem = GetItemName(ev.OldItem?.Type ?? ItemType.None);
            var newItem = GetItemName(ev.NewItem?.Type ?? ItemType.None);
            LogAction($"🎒 **{ev.Player.Nickname}** сменил {oldItem} на {newItem}");
        }

        private void OnTogglingFlashlight(PlayerTogglingFlashlightEventArgs ev)
        {
            var state = ev.NewState ? "включил" : "выключил";
            LogAction($"🔦 **{ev.Player.Nickname}** {state} фонарик");
        }

        private void OnSearchingPickup(PlayerSearchingPickupEventArgs ev)
        {
            var itemName = GetItemName(ev.Pickup?.Type ?? ItemType.None);
            var zone = GetZoneName(ev.Player.Zone);
            LogAction($"🔍 **{ev.Player.Nickname}** осматривает {itemName} | Зона: {zone}");
        }

        private void OnThrowingItem(PlayerThrowingItemEventArgs ev)
        {
            // Получаем информацию о бросаемом предмете
            var itemName = "Предмет";
            LogAction($"🏈 **{ev.Player.Nickname}** бросает {itemName}");
        }

        private void OnChangingNickname(PlayerChangingNicknameEventArgs ev)
        {
            // Получаем старое и новое имя из события
            LogAction($"✏️ **{ev.Player.Nickname}** изменил ник");
        }

        // События сервера
        private void OnRoundStarted()
        {
            playerHealthTracker.Clear();
            processedEscapes.Clear();

            var playerCount = Player.List.Count();
            var scpCount = Player.List.Count(p => p.Team == Team.SCPs);
            var mtfCount = Player.List.Count(p => p.Team == Team.FoundationForces);
            var ciCount = Player.List.Count(p => p.Team == Team.ChaosInsurgency);
            var dClassCount = Player.List.Count(p => p.Team == Team.ClassD);
            var scientistCount = Player.List.Count(p => p.Team == Team.Scientists);

            LogAction($"🎯 **РАУНД НАЧАЛСЯ** | Игроков: {playerCount} | SCP: {scpCount} | MTF: {mtfCount} | CI: {ciCount} | D-Class: {dClassCount} | Учёные: {scientistCount}");
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            string winner;
            switch (ev.LeadingTeam.ToString())
            {
                case "FacilityForces": winner = "Фонд"; break;
                case "ChaosInsurgency": winner = "Повстанцы Хаоса"; break;
                case "Anomalies": winner = "SCP"; break;
                case "Draw": winner = "Ничья"; break;
                default: winner = ev.LeadingTeam.ToString(); break;
            }
            LogAction($"🏁 **РАУНД ЗАВЕРШЕН** | Победитель: {winner}");
        }

        private void OnWaitingForPlayers()
        {
            var playerCount = Player.List.Count();
            LogAction($"⏳ **Ожидание игроков** | Текущее количество: {playerCount}");
        }

        // События боеголовки
        private void OnWarheadStarting(WarheadStartingEventArgs ev)
        {
            var playerName = ev.Player?.Nickname ?? "Автоматически";
            LogAction($"☢️ **БОЕГОЛОВКА ЗАПУЩЕНА** игроком **{playerName}**");
        }

        private void OnWarheadStopping(WarheadStoppingEventArgs ev)
        {
            var playerName = ev.Player?.Nickname ?? "Автоматически";
            LogAction($"🛑 **БОЕГОЛОВКА ОСТАНОВЛЕНА** игроком **{playerName}**");
        }

        private void OnWarheadDetonating(WarheadDetonatingEventArgs ev)
        {
            var survivorsCount = Player.List.Count(p => p.IsAlive);
            LogAction($"💥 **БОЕГОЛОВКА ВЗОРВАЛАСЬ** | Выживших: {survivorsCount}");
        }

        // События SCP-914
        private void OnScp914Activating(Scp914ActivatingEventArgs ev)
        {
            LogAction($"⚙️ **{ev.Player.Nickname}** активировал SCP-914");
        }

        private void OnScp914KnobChanged(Scp914KnobChangedEventArgs ev)
        {
            string setting;
            switch (ev.KnobSetting.ToString())
            {
                case "Rough": setting = "Грубо"; break;
                case "Coarse": setting = "Крупно"; break;
                case "OneToOne": setting = "1:1"; break;
                case "Fine": setting = "Точно"; break;
                case "VeryFine": setting = "Очень точно"; break;
                default: setting = ev.KnobSetting.ToString(); break;
            }
            LogAction($"🎛️ **{ev.Player.Nickname}** изменил режим SCP-914 на {setting}");
        }

        private void OnScp914ProcessingPlayer(Scp914ProcessingPlayerEventArgs ev)
        {
            string setting;
            switch (ev.KnobSetting.ToString())
            {
                case "Rough": setting = "Грубо"; break;
                case "Coarse": setting = "Крупно"; break;
                case "OneToOne": setting = "1:1"; break;
                case "Fine": setting = "Точно"; break;
                case "VeryFine": setting = "Очень точно"; break;
                default: setting = ev.KnobSetting.ToString(); break;
            }
            LogAction($"🔄 **{ev.Player.Nickname}** обработан в SCP-914 | Режим: {setting}");
        }

        // События SCP-079
        private void OnScp079GainingExperience(Scp079GainingExperienceEventArgs ev)
        {
            LogAction($"📈 **SCP-079** ({ev.Player.Nickname}) получил {ev.Amount} опыта");
        }

        private void OnScp079ChangingCamera(Scp079ChangingCameraEventArgs ev)
        {
            LogAction($"📹 **SCP-079** ({ev.Player.Nickname}) сменил камеру");
        }

        // События SCP-096  
        private void OnScp096AddingTarget(Scp096AddingTargetEventArgs ev)
        {
            var scp096 = ev.Player?.Nickname ?? "SCP-096";
            LogAction($"👁️ **{ev.Target.Nickname}** посмотрел на **SCP-096** ({scp096})");
        }

        private void OnScp096Enraging(Scp096EnragingEventArgs ev)
        {
            LogAction($"😡 **SCP-096** ({ev.Player.Nickname}) впал в ярость");
        }

        // Методы логирования
        private static void LogAction(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string fullMessage = $"[{timestamp}] {message}";

            lock (queueLock)
            {
                messageQueue.Enqueue(fullMessage);
            }

            ProcessMessageQueue();

            if (EnableDebugLogs)
                ServerConsole.AddLog($"[DiscordLogger] {fullMessage}", ConsoleColor.White);
        }

        private static async void ProcessMessageQueue()
        {
            try
            {
                if ((DateTime.Now - lastSendTime).TotalMilliseconds < RateLimitDelay)
                    return;

                List<string> messagesToSend = new List<string>();

                lock (queueLock)
                {
                    while (messageQueue.Count > 0 && messagesToSend.Count < 10)
                    {
                        messagesToSend.Add(messageQueue.Dequeue());
                    }
                }

                if (messagesToSend.Count > 0)
                {
                    string combinedMessage = string.Join("\\n", messagesToSend);
                    string title = "📝 Логи сервера";
                    int color = 3447003;

                    await SendDiscordMessageAsync(title, combinedMessage, color);
                    lastSendTime = DateTime.Now;

                    if (messageQueue.Count > 0)
                    {
                        await System.Threading.Tasks.Task.Delay(RateLimitDelay);
                        ProcessMessageQueue();
                    }
                }
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DiscordLogger] Ошибка обработки очереди: {ex.Message}", ConsoleColor.Red);
            }
        }

        private static void SendDiscordMessage(string title, string description, int color)
        {
            _ = SendDiscordMessageAsync(title, description, color);
        }

        private static async System.Threading.Tasks.Task SendDiscordMessageAsync(string title, string description, int color)
        {
            try
            {
                string webhook = WebhookUrl;

                // Экранируем специальные символы для JSON
                title = EscapeJson(title);
                description = EscapeJson(description);

                // Формируем JSON вручную без использования внешних библиотек
                string json = "{" +
                    "\"embeds\":[{" +
                        $"\"title\":\"{title}\"," +
                        $"\"description\":\"{description}\"," +
                        $"\"color\":{color}," +
                        $"\"timestamp\":\"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"," +
                        "\"footer\":{" +
                            $"\"text\":\"{ServerName}\"" +
                        "}" +
                    "}]" +
                "}";

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(webhook, content);

                if (!response.IsSuccessStatusCode)
                {
                    ServerConsole.AddLog($"[DiscordLogger] Ошибка отправки в Discord: {response.StatusCode}", ConsoleColor.Red);
                }
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DiscordLogger] Ошибка при отправке сообщения в Discord: {ex.Message}", ConsoleColor.Red);
            }
        }

        // Вспомогательный метод для экранирования специальных символов в JSON
        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str))
                return "";

            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("\b", "\\b")
                .Replace("\f", "\\f");
        }
    }
}