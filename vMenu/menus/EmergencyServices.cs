using System;
using System.Collections.Generic;
using System.Linq;

using CitizenFX.Core;

using MenuAPI;

using Newtonsoft.Json;

using static CitizenFX.Core.Native.API;
using static vMenuClient.CommonFunctions;
using static vMenuShared.PermissionsManager;

namespace vMenuClient.menus
{
    public class EmergencyServices
    {
        private Menu menu;

        /// <summary>
        /// Config loaded from emergency_services.json: department name -> (spawn code -> custom display name).
        /// Set by EventManager when config is loaded.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> EmergencyVehiclesByDepartment { get; set; } =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Server-provided EUP outfits (admin-saved, visible to all players).</summary>
        public static Dictionary<string, List<EupOutfitData>> ServerEupOutfits { get; set; } = new Dictionary<string, List<EupOutfitData>>();

        /// <summary>Which department submenu is currently open (for refreshing when server data arrives).</summary>
        public static string CurrentOpenEupDepartmentKey { get; set; }

        /// <summary>Department submenus by key, so we can refresh when server sends outfit list.</summary>
        public static Dictionary<string, Menu> EupDeptMenusByKey { get; set; }

        /// <summary>Default extras per spawn code for Emergency Services vehicles (enabled when they spawn).</summary>
        public static Dictionary<string, List<int>> DefaultExtrasBySpawnCode { get; set; } = new Dictionary<string, List<int>>();

        private static readonly string[] DepartmentNames =
        {
            "Los Santos Police Department",
            "Blaine County Sheriff's Office",
            "San Andreas State Troopers",
            "San Andreas Fire & Rescue",
            "San Andreas Emergency Medical Service",
            "San Andreas Department of Corrections"
        };

        /// <summary>EUP department keys for storage and submenus.</summary>
        private static readonly string[] EupDepartmentKeys = { "LSPD", "SAST", "BCSO", "SAFR", "SAEMS", "SADOC" };

        private static readonly string[] EupDepartmentLabels =
        {
            "Los Santos Police Department",
            "San Andreas State Troopers",
            "Blaine County Sheriff's Office",
            "San Andreas Fire & Rescue",
            "San Andreas Emergency Medical Service",
            "San Andreas Department of Corrections"
        };

        public EmergencyServices() { }

        private void CreateMenu()
        {
            menu = new Menu("Emergency Services", "Emergency Services Menu");

            if (!IsAllowed(Permission.ESMenu) && !IsAllowed(Permission.ESAll))
            {
                return;
            }

            // ---- EUP at top ----
            var eupMenu = new Menu("Emergency Services", "EUP – Outfits & Accessories");
            var eupBtn = new MenuItem("EUP", "EUP Menu: department outfits and accessories") { Label = "→→→" };
            menu.AddMenuItem(eupBtn);
            MenuController.AddSubmenu(menu, eupMenu);
            MenuController.BindMenuItem(menu, eupMenu, eupBtn);

            // Accessories submenu (Vest, Hat, Glasses, Gloves – apply to current outfit)
            var accessoriesMenu = new Menu("EUP", "Accessories");
            var accessoriesBtn = new MenuItem("Accessories", "Add or change vest, hat, glasses, or gloves on your current outfit.") { Label = "→→→" };
            eupMenu.AddMenuItem(accessoriesBtn);
            MenuController.AddSubmenu(eupMenu, accessoriesMenu);
            MenuController.BindMenuItem(eupMenu, accessoriesMenu, accessoriesBtn);

            accessoriesMenu.OnMenuOpen += RefreshAccessoriesMenu;

            // Create Outfit: admin only – save to server so ALL players see it in department menus
            var createOutfitMenu = new Menu("EUP", "Create Outfit");
            var createOutfitBtn = new MenuItem("Create Outfit", "~o~Admin only.~s~ Save your current appearance to the shared list so all players can use it in department menus.") { Label = "→→→" };
            // Only ESSaveOutfits controls this; ESAll still controls general Emergency Services access but not saving outfits.
            var canSaveOutfits = IsAllowed(Permission.ESSaveOutfits);
            if (canSaveOutfits)
            {
                eupMenu.AddMenuItem(createOutfitBtn);
                MenuController.AddSubmenu(eupMenu, createOutfitMenu);
                MenuController.BindMenuItem(eupMenu, createOutfitMenu, createOutfitBtn);
            }

            for (var i = 0; i < EupDepartmentKeys.Length; i++)
            {
                var key = EupDepartmentKeys[i];
                var label = EupDepartmentLabels[i];
                var btn = new MenuItem(label, $"Save current outfit to {label}.") { ItemData = key };
                if (canSaveOutfits)
                    createOutfitMenu.AddMenuItem(btn);
            }

            if (canSaveOutfits)
            {
                createOutfitMenu.OnItemSelect += async (sender, item, index) =>
                {
                    if (item?.ItemData is string department)
                    {
                        var name = await GetUserInput("Enter outfit name", "", 40);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            Notify.Error("Invalid name.");
                            return;
                        }
                        var pedInfo = GetCurrentPedInfo();
                        var outfitData = new EupOutfitData { DisplayName = name.Trim(), PedInfo = pedInfo };
                        TriggerServerEvent("vMenu:SaveEupOutfit", department, JsonConvert.SerializeObject(outfitData));
                    }
                };
            }

            // Request server outfit list when opening EUP (so department menus show shared outfits)
            eupMenu.OnMenuOpen += (sender) => TriggerServerEvent("vMenu:RequestEupOutfits");

            // Department submenus for EUP outfits (LSPD, SAST, etc.) – show server list for all players
            var deptMenus = new Dictionary<string, Menu>();
            for (var i = 0; i < EupDepartmentKeys.Length; i++)
            {
                var key = EupDepartmentKeys[i];
                var label = EupDepartmentLabels[i];
                var deptMenu = new Menu("EUP", label);
                var deptBtn = new MenuItem(label, $"Saved outfits for {label}.") { Label = "→→→" };
                eupMenu.AddMenuItem(deptBtn);
                MenuController.AddSubmenu(eupMenu, deptMenu);
                MenuController.BindMenuItem(eupMenu, deptMenu, deptBtn);
                deptMenus[key] = deptMenu;
            }

            EupDeptMenusByKey = deptMenus;

            foreach (var key in EupDepartmentKeys)
            {
                var deptMenu = deptMenus[key];
                var keyCapture = key;
                deptMenu.OnMenuOpen += (sender) =>
                {
                    CurrentOpenEupDepartmentKey = keyCapture;
                    RefreshEupDepartmentMenu(deptMenu, keyCapture);
                };
                deptMenu.OnMenuClose += (sender) => CurrentOpenEupDepartmentKey = null;
                deptMenu.OnItemSelect += (sender, item, index) =>
                {
                    if (item?.ItemData is KeyValuePair<string, EupOutfitData> pair && pair.Value != null)
                    {
                        var data = pair.Value;
                        ApplyPedClothing(data.PedInfo);
                    }
                };
            }

            // ---- Vehicle Spawner (parent for the six department submenus) ----
            var vehicleSpawnerMenu = new Menu("Emergency Services", "Vehicle Spawner");
            var vehicleSpawnerBtn = new MenuItem("Vehicle Spawner", "Spawn emergency service vehicles by department.") { Label = "→→→" };
            menu.AddMenuItem(vehicleSpawnerBtn);
            MenuController.AddSubmenu(menu, vehicleSpawnerMenu);
            MenuController.BindMenuItem(menu, vehicleSpawnerMenu, vehicleSpawnerBtn);

            foreach (var departmentName in DepartmentNames)
            {
                var subMenu = new Menu("Emergency Services", departmentName);
                var subBtn = new MenuItem(departmentName, $"Spawn a vehicle from {departmentName}.") { Label = "→→→" };

                vehicleSpawnerMenu.AddMenuItem(subBtn);

                var vehicles = EmergencyVehiclesByDepartment != null && EmergencyVehiclesByDepartment.ContainsKey(departmentName)
                    ? EmergencyVehiclesByDepartment[departmentName]
                    : null;

                if (vehicles != null && vehicles.Count > 0)
                {
                    MenuController.AddSubmenu(vehicleSpawnerMenu, subMenu);
                    MenuController.BindMenuItem(vehicleSpawnerMenu, subMenu, subBtn);

                    foreach (var kvp in vehicles)
                    {
                        var spawnCode = kvp.Key;
                        var customName = kvp.Value ?? spawnCode;

                        var item = new MenuItem(customName, $"Spawn ~y~{customName}~s~ (spawn code: {spawnCode}).")
                        {
                            ItemData = spawnCode
                        };

                        subMenu.AddMenuItem(item);
                    }

                    subMenu.OnItemSelect += async (sender, item, index) =>
                    {
                        if (item?.ItemData is string code)
                        {
                            var spawnInside = MainMenu.VehicleSpawnerMenu != null && MainMenu.VehicleSpawnerMenu.SpawnInVehicle;
                            var replacePrev = MainMenu.VehicleSpawnerMenu != null && MainMenu.VehicleSpawnerMenu.ReplaceVehicle;
                            var vehHandle = await SpawnVehicle(code, spawnInside, replacePrev);

                            if (vehHandle != 0 && DefaultExtrasBySpawnCode != null && DefaultExtrasBySpawnCode.ContainsKey(code))
                            {
                                var extras = DefaultExtrasBySpawnCode[code];
                                if (extras != null)
                                {
                                    foreach (var extraId in extras)
                                    {
                                        if (extraId >= 0 && DoesExtraExist(vehHandle, extraId))
                                        {
                                            // false = enable extra
                                            SetVehicleExtra(vehHandle, extraId, false);
                                        }
                                    }
                                }
                            }
                        }
                    };
                }
                else
                {
                    subBtn.Description = "No vehicles configured for this department. Edit config/emergency_services.json on the server.";
                    subBtn.Enabled = false;
                    subBtn.LeftIcon = MenuItem.Icon.LOCK;
                }
            }
        }

        private struct AccessorySlot
        {
            public int Id;
            public bool IsProp;
        }

        private readonly Dictionary<MenuListItem, AccessorySlot> _accessoriesSlotMap = new Dictionary<MenuListItem, AccessorySlot>();

        private void RefreshAccessoriesMenu(Menu accessoriesMenu)
        {
            accessoriesMenu.ClearMenuItems();
            _accessoriesSlotMap.Clear();
            var ped = Game.PlayerPed.Handle;

            // Vest = component 9 (Body Armor)
            var vestMax = GetNumberOfPedDrawableVariations(ped, 9);
            if (vestMax > 0)
            {
                var vestList = new List<string> { "None" };
                for (var i = 0; i < vestMax; i++) vestList.Add($"Vest #{i + 1}");
                var current = GetPedDrawableVariation(ped, 9);
                var vestItem = new MenuListItem("Vest", vestList, Math.Min(current, vestMax), "Change vest (body armor) on current outfit.");
                accessoriesMenu.AddMenuItem(vestItem);
                _accessoriesSlotMap[vestItem] = new AccessorySlot { Id = 9, IsProp = false };
            }

            // Hat = prop 0
            var hatMax = GetNumberOfPedPropDrawableVariations(ped, 0);
            if (hatMax > 0)
            {
                var hatList = new List<string> { "None" };
                for (var i = 0; i < hatMax; i++) hatList.Add($"Hat #{i + 1}");
                var current = GetPedPropIndex(ped, 0);
                var hatItem = new MenuListItem("Hat", hatList, current + 1, "Add or remove hat on current outfit.");
                accessoriesMenu.AddMenuItem(hatItem);
                _accessoriesSlotMap[hatItem] = new AccessorySlot { Id = 0, IsProp = true };
            }

            // Glasses = prop 1
            var glassesMax = GetNumberOfPedPropDrawableVariations(ped, 1);
            if (glassesMax > 0)
            {
                var glassesList = new List<string> { "None" };
                for (var i = 0; i < glassesMax; i++) glassesList.Add($"Glasses #{i + 1}");
                var current = GetPedPropIndex(ped, 1);
                var glassesItem = new MenuListItem("Glasses", glassesList, current + 1, "Add or remove glasses on current outfit.");
                accessoriesMenu.AddMenuItem(glassesItem);
                _accessoriesSlotMap[glassesItem] = new AccessorySlot { Id = 1, IsProp = true };
            }

            // Gloves / arms = component 3 (Torso/arms)
            var glovesMax = GetNumberOfPedDrawableVariations(ped, 3);
            if (glovesMax > 0)
            {
                var glovesList = new List<string>();
                for (var i = 0; i < glovesMax; i++) glovesList.Add($"Option #{i + 1}");
                var current = GetPedDrawableVariation(ped, 3);
                var glovesItem = new MenuListItem("Arms / Gloves", glovesList, Math.Min(current, glovesMax - 1), "Change arms/torso (e.g. gloves) on current outfit.");
                accessoriesMenu.AddMenuItem(glovesItem);
                _accessoriesSlotMap[glovesItem] = new AccessorySlot { Id = 3, IsProp = false };
            }

            accessoriesMenu.OnListIndexChange += (sender, item, oldIdx, newIdx, itemIndex) =>
            {
                var listItem = item as MenuListItem;
                if (listItem == null || !_accessoriesSlotMap.TryGetValue(listItem, out var slot))
                    return;
                var p = Game.PlayerPed.Handle;
                if (slot.IsProp)
                {
                    if (newIdx == 0)
                        ClearPedProp(p, slot.Id);
                    else
                        SetPedPropIndex(p, slot.Id, newIdx - 1, 0, true);
                }
                else
                {
                    if (slot.Id == 9 && listItem.ListItems[0] == "None")
                    {
                        if (newIdx == 0)
                            SetPedComponentVariation(p, 9, 0, 0, 0);
                        else
                            SetPedComponentVariation(p, 9, newIdx - 1, 0, 0);
                    }
                    else if (slot.Id == 3)
                    {
                        var tex = GetPedTextureVariation(p, 3);
                        SetPedComponentVariation(p, 3, newIdx, tex, 0);
                    }
                    else
                    {
                        SetPedComponentVariation(p, slot.Id, newIdx, 0, 0);
                    }
                }
            };

            accessoriesMenu.RefreshIndex();
        }

        private static void RefreshEupDepartmentMenu(Menu deptMenu, string departmentKey)
        {
            deptMenu.ClearMenuItems();
            var list = ServerEupOutfits != null && ServerEupOutfits.ContainsKey(departmentKey) ? ServerEupOutfits[departmentKey] : null;
            if (list != null && list.Count > 0)
            {
                foreach (var data in list.OrderBy(x => x?.DisplayName ?? ""))
                {
                    if (data == null) continue;
                    var name = data.DisplayName ?? "Outfit";
                    var item = new MenuItem(name, "Apply this outfit.") { ItemData = new KeyValuePair<string, EupOutfitData>(null, data) };
                    deptMenu.AddMenuItem(item);
                }
            }
            else
            {
                var empty = new MenuItem("No outfits", "Admins can add outfits via ~y~Create Outfit~s~ in EUP. They appear here for everyone.");
                empty.Enabled = false;
                deptMenu.AddMenuItem(empty);
            }
            deptMenu.RefreshIndex();
        }

        /// <summary>
        /// Called when server sends the shared EUP outfit list. Updates cache and refreshes open department menu.
        /// </summary>
        public static void ReceiveServerEupOutfits(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;
            try
            {
                var raw = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
                if (raw == null)
                    return;
                ServerEupOutfits = new Dictionary<string, List<EupOutfitData>>();
                foreach (var kvp in raw)
                {
                    var list = new List<EupOutfitData>();
                    if (kvp.Value != null)
                    {
                        foreach (var s in kvp.Value)
                        {
                            if (string.IsNullOrEmpty(s)) continue;
                            try
                            {
                                var data = JsonConvert.DeserializeObject<EupOutfitData>(s);
                                if (data != null)
                                    list.Add(data);
                            }
                            catch { /* skip invalid entry */ }
                        }
                    }
                    ServerEupOutfits[kvp.Key] = list;
                }
                if (CurrentOpenEupDepartmentKey != null && EupDeptMenusByKey != null && EupDeptMenusByKey.TryGetValue(CurrentOpenEupDepartmentKey, out var menu))
                    RefreshEupDepartmentMenu(menu, CurrentOpenEupDepartmentKey);
            }
            catch { /* ignore */ }
        }

        public Menu GetMenu()
        {
            if (menu == null)
            {
                CreateMenu();
            }
            return menu;
        }
    }
}
