using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Y5Coop
{
    internal static class IniSettings
    {
        public static string IniPath
        {
            get
            {           
                Assembly assmb = Assembly.GetExecutingAssembly();
                return Path.Combine(Mod.ModPath, "settings.ini");
            }
        }

        public static void Read()
        {
            Assembly assmb = Assembly.GetExecutingAssembly();
            Ini ini = new Ini(Path.Combine(Path.GetDirectoryName(assmb.Location), "settings.ini"));

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

            PlayerInput.GuardButtonIdx = int.Parse(ini.GetValue("GuardButton", "Bindings", "6"));
            PlayerInput.StanceButtonIdx = int.Parse(ini.GetValue("StanceButton", "Bindings", "7"));

            PlayerInput.LightAttackButton = (JoystickOffset)Enum.Parse(typeof(JoystickOffset), ini.GetValue("LightAttackButton", "Bindings", "Buttons3"));
            PlayerInput.HeavyAttackButton = (JoystickOffset)Enum.Parse(typeof(JoystickOffset), ini.GetValue("HeavyAttackButton", "Bindings", "Buttons0"));
            PlayerInput.QuickstepButton = (JoystickOffset)Enum.Parse(typeof(JoystickOffset), ini.GetValue("QuickstepButton", "Bindings", "Buttons2"));
            PlayerInput.GrabButton = (JoystickOffset)Enum.Parse(typeof(JoystickOffset), ini.GetValue("GrabButton", "Bindings", "Buttons1"));
            PlayerInput.DragonRageButton = (JoystickOffset)Enum.Parse(typeof(JoystickOffset), ini.GetValue("DragonRageButton", "Bindings", "Buttons1"));
        }
    }
}
