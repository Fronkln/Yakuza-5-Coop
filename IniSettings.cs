using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Y5Coop
{
    internal static class IniSettings
    {
        public static string IniPath
        {
            get
            {           
                return Path.Combine(Mod.ModPath, "settings.ini");
            }
        }

        public static void Read()
        {
            Ini ini = new Ini(IniPath);

            Camera.Height = float.Parse(ini.GetValue("Height", "Camera", "1.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MinFOV = float.Parse(ini.GetValue("MinFOV", "Camera", "0.6"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFOV = float.Parse(ini.GetValue("MinFOV", "Camera", "1"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MinFOVDistance = float.Parse(ini.GetValue("MinFOVDistance", "Camera", "6.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFOVDistance = float.Parse(ini.GetValue("MaxFOVDistance", "Camera", "15"), System.Globalization.CultureInfo.InvariantCulture);

            Camera.MinFollowDistance = float.Parse(ini.GetValue("MinFollowDistance", "Camera", "6.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFollowDistance = float.Parse(ini.GetValue("MinFollowDistance", "Camera", "12"), System.Globalization.CultureInfo.InvariantCulture);

            Camera.MinFollowOffset = float.Parse(ini.GetValue("MinFollowOffset", "Camera", "3.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFollowOffset = float.Parse(ini.GetValue("MaxFollowOffset", "Camera", "13"), System.Globalization.CultureInfo.InvariantCulture);

            Mod.TeleportDistance = float.Parse(ini.GetValue("TeleportDistance", "General", "25"), System.Globalization.CultureInfo.InvariantCulture);

            string autoCreatePlr = ini.GetValue("AutoCreatePlayer", "General", "1");
            Mod.AutomaticallyCreatePlayer = byte.Parse(autoCreatePlr) == 1;

            string missions = ini.GetValue("BlacklistedMissions", "General", "");

            if(!string.IsNullOrEmpty(missions))
                Mod.BlacklistedMissions = missions.Split(',').Select(x => int.Parse(x.Trim())).ToArray();
        }
    }
}
