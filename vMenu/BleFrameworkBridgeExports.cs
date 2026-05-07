using System;
using System.Collections.Generic;

using CitizenFX.Core;

using Newtonsoft.Json;

using static CitizenFX.Core.Native.API;

namespace vMenuClient
{
    /// <summary>
    /// Client exports so ble_framework can list/read vMenu MP character KVP (resource-scoped storage).
    /// Lua: exports['vMenu']:BleGetMpCharacterSaveNames() -> JSON string array of display names (without mp_ped_ prefix).
    /// Lua: exports['vMenu']:BleGetMpCharacterJson(displayName) -> raw JSON string from KVP or empty string.
    /// </summary>
    public class BleFrameworkBridgeExports : BaseScript
    {
        public BleFrameworkBridgeExports()
        {
            Exports.Add("BleGetMpCharacterSaveNames", new Func<string>(BleGetMpCharacterSaveNames));
            Exports.Add("BleGetMpCharacterJson", new Func<string, string>(BleGetMpCharacterJson));
        }

        private static string BleGetMpCharacterSaveNames()
        {
            try
            {
                var names = new List<string>();
                var handle = StartFindKvp("mp_ped_");
                while (true)
                {
                    var k = FindKvp(handle);
                    if (string.IsNullOrEmpty(k))
                    {
                        break;
                    }

                    if (k.StartsWith("mp_ped_", StringComparison.Ordinal) && k.Length > 7)
                    {
                        names.Add(k.Substring(7));
                    }
                }

                EndFindKvp(handle);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return JsonConvert.SerializeObject(names);
            }
            catch (Exception)
            {
                return "[]";
            }
        }

        private static string BleGetMpCharacterJson(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return string.Empty;
            }

            var key = displayName.StartsWith("mp_ped_", StringComparison.Ordinal) ? displayName : "mp_ped_" + displayName;
            return GetResourceKvpString(key) ?? string.Empty;
        }
    }
}
