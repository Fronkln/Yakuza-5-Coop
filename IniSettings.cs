using System.IO;
using System.Linq;
using Y5Lib;

namespace Y5Coop
{
    internal static class IniSettings
    {
        public static string IniPath
        {
            get
            {
                return Path.Combine(Mod.Instance.ModPath, "settings.ini");
            }
        }

        public static void Read()
        {
            Ini ini = new Ini(IniPath);

            Mod.CoopPlayerCommandset = ini.GetValue("Player2Moveset", "Combat", "");
            Mod.AllyMode = byte.Parse(ini.GetValue("AIControlled", "General", "0")) == 1;
            Mod.DestroyP2OnCCC = byte.Parse(ini.GetValue("DestroyOnTalk", "General", "0")) == 1;

            Camera.UseClassicCamera = byte.Parse(ini.GetValue("UseClassicCamera", "Camera", "0")) == 1;
            Camera.UseClassicCameraBattle = byte.Parse(ini.GetValue("UseClassicCameraBattle", "Camera", "0")) == 1;

            Camera.Height = float.Parse(ini.GetValue("Height", "Camera", "1.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MinFOV = float.Parse(ini.GetValue("MinFOV", "Camera", "0.6"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFOV = float.Parse(ini.GetValue("MinFOV", "Camera", "1"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MinFOVDistance = float.Parse(ini.GetValue("MinFOVDistance", "Camera", "6.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFOVDistance = float.Parse(ini.GetValue("MaxFOVDistance", "Camera", "15"), System.Globalization.CultureInfo.InvariantCulture);

            Camera.MinFollowDistance = float.Parse(ini.GetValue("MinFollowDistance", "Camera", "6.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFollowDistance = float.Parse(ini.GetValue("MinFollowDistance", "Camera", "12"), System.Globalization.CultureInfo.InvariantCulture);

            Camera.MinFollowOffset = float.Parse(ini.GetValue("MinFollowOffset", "Camera", "1"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFollowOffset = float.Parse(ini.GetValue("MaxFollowOffset", "Camera", "12"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MinFollowOffsetBattle = float.Parse(ini.GetValue("MinFollowOffsetBattle", "Camera", "3.5"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxFollowOffsetBattle = float.Parse(ini.GetValue("MaxFollowOffsetBattle", "Camera", "12"), System.Globalization.CultureInfo.InvariantCulture);

            Camera.FollowSpeed = float.Parse(ini.GetValue("FollowSpeed", "Camera", "1"), System.Globalization.CultureInfo.InvariantCulture);

            Camera.MinCameraHeight = float.Parse(ini.GetValue("MinHeightOffset", "Camera", "1.4"), System.Globalization.CultureInfo.InvariantCulture);
            Camera.MaxCameraHeight = float.Parse(ini.GetValue("MaxHeightOffset", "Camera", "2"), System.Globalization.CultureInfo.InvariantCulture);

            Mod.TeleportDistance = float.Parse(ini.GetValue("TeleportDistance", "General", "25"), System.Globalization.CultureInfo.InvariantCulture);

            string autoCreatePlr = ini.GetValue("AutoCreatePlayer", "General", "1");
            Mod.AutomaticallyCreatePlayer = byte.Parse(autoCreatePlr) == 1;

            string missions = ini.GetValue("BlacklistedMissions", "General", "");

            if (!string.IsNullOrEmpty(missions))
                Mod.BlacklistedMissions = missions.Split(';').Select(x => int.Parse(x.Trim())).ToArray();

            string debugInput = ini.GetValue("DebugLogInput", "Bindings", "0").Trim();
            Mod.DebugInput = byte.Parse(debugInput) == 1;

            PlayerInput.Player1InputType = InputDeviceType.All;

            PlayerInput.Player1ForcedInput = false;
            PlayerInput.Player2ForcedInput = false;

            string player1Override = ini.GetValue("Player1ControlTypeOverride", "Bindings", null).Trim();
            string player2Override = ini.GetValue("Player2ControlTypeOverride", "Bindings", null).Trim();

            if (!string.IsNullOrEmpty(player1Override))
            {
                InputDeviceType type = (InputDeviceType)int.Parse(player1Override);

                PlayerInput.Player1InputType = type;
                PlayerInput.Player1ForcedInput = true;
                PlayerInput.IsPlayer1InputCalibrated = true;
            }

            if (!string.IsNullOrEmpty(player2Override))
            {
                InputDeviceType type = (InputDeviceType)int.Parse(player2Override);

                if (type == InputDeviceType.Keyboard)
                    type = InputDeviceType.All;

                PlayerInput.Player2InputType = type;
                PlayerInput.Player2ForcedInput = true;
                PlayerInput.IsInputCalibrated = true;
            }

            PlayerInput.IsLegacyGenericController = byte.Parse(ini.GetValue("LegacyInput", "Bindings", null).Trim()) == 1;
            PlayerInput.IsLegacyDualshock = byte.Parse(ini.GetValue("LegacyDualshock", "Bindings", null).Trim()) == 1;

            PlayerInput.AllowPlayer1InputCalibration = true;
            PlayerInput.AllowPlayer2InputCalibration = true;

            if (PlayerInput.IsLegacyInput)
            {
                PlayerInput.AllowPlayer1InputCalibration = false;

                PlayerInput.Player1ForcedInput = true;

                PlayerInput.Player1InputType = InputDeviceType.All;
                PlayerInput.Player2InputType = InputDeviceType.Controller;
            }
        }
    }
}
