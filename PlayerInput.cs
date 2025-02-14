using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Y5Lib;

namespace Y5Coop
{
    public unsafe static class PlayerInput
    {
        private delegate void* DeviceListener_GetInput(void* a1);
        public delegate void FighterController_InputUpdate(void* a1);

        public static PadInputInfo* m_customPadInf;
        private static byte* weirdPadThing;
        private static byte* weirdPadThing2;

        public static DirectInput DirectInput = new DirectInput();
        public static Guid JoystickGuid = Guid.Empty;
        public static Joystick Joystick = null;

        public static int Player1InputFlags;
        public static int Player2InputFlags;

        public static int StanceButtonIdx = 7;
        public static int GuardButtonIdx = 6;

        public static JoystickOffset LightAttackButton = JoystickOffset.Buttons3;
        public static JoystickOffset HeavyAttackButton = JoystickOffset.Buttons0;
        public static JoystickOffset QuickstepButton = JoystickOffset.Buttons2;
        public static JoystickOffset GrabButton = JoystickOffset.Buttons1;
        public static JoystickOffset DragonRageButton = JoystickOffset.Buttons1;

        public static void Init()
        {
            m_customPadInf = (PadInputInfo*)Marshal.AllocHGlobal(2048);
            weirdPadThing = (byte*)Marshal.AllocHGlobal(8);
            weirdPadThing2 = (byte*)Marshal.AllocHGlobal(8);

            Y5Lib.Unsafe.CPP.PatchMemory((IntPtr)weirdPadThing, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
            Y5Lib.Unsafe.CPP.PatchMemory((IntPtr)weirdPadThing2, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);

            long* ptr1 = (long*)((long)m_customPadInf + 0x1E0);
            long* ptr2 = (long*)((long)m_customPadInf + 0x1E8);

            *ptr1 = (long)weirdPadThing;
            *ptr2 = (long)weirdPadThing2;

            OE.LogInfo("Weird Pad Thing   " + ((long)weirdPadThing).ToString("X"));
            OE.LogInfo("Weird Pad Thing2  " + ((long)weirdPadThing).ToString("X"));


            IntPtr getInputAddr = Y5Lib.Unsafe.CPP.PatternSearch("48 8B 51 10 48 8D 41 18 48 89 82 C0 18 00 00 48");

            if (getInputAddr == IntPtr.Zero)
            {
                OE.LogError("Couldn't find get input function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find get input function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            m_getInputDeleg = Mod.engine.CreateHook<DeviceListener_GetInput>(getInputAddr, GetInputDetour);

            // Initialize DirectInput
            DirectInput = new DirectInput();

            // Find a Joystick Guid
            JoystickGuid = Guid.Empty;

            OE.LogInfo("Searching for gamepads");

            foreach (var deviceInstance in DirectInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices))
            {
                JoystickGuid = deviceInstance.InstanceGuid;
                Console.WriteLine(deviceInstance.ProductName + " PRODUCT");
            }


            // If Gamepad not found, look for a Joystick
            if (JoystickGuid == Guid.Empty)
            {
                OE.LogInfo("Searching for joysticks");

                DirectInput.GetDevices();

                foreach (var deviceInstance in DirectInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
                {
                    JoystickGuid = deviceInstance.InstanceGuid;
                    Console.WriteLine(deviceInstance.ProductName + " JOYSTICK");
                }
            }

            // If Joystick not found, throws an error
            if (JoystickGuid == Guid.Empty)
            {
                OE.LogError("No joystick/Gamepad found.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - No joystick/Gamepad found.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            // Instantiate the joystick
            Joystick = new Joystick(DirectInput, JoystickGuid);

            OE.LogInfo(string.Format("Found Joystick/Gamepad with GUID: {0}", JoystickGuid));

            // Set BufferSize in order to use buffered data.
            Joystick.Properties.BufferSize = 128;

            // Acquire the joystick
            Joystick.Acquire();
        }



        static DeviceListener_GetInput m_getInputDeleg;
        unsafe static void* GetInputDetour(void* a1)
        {
            void* result;

            if (Mod.CoopPlayer == null)
            {
                result = m_getInputDeleg(a1);
                return result;
            }

            void* fighterPtr = *(void**)((long)a1 + 0x20);

            if (Mod.CoopPlayer != null && Mod.CoopPlayer.Pointer.ToInt64() == (long)fighterPtr)
            {
                UpdatePlayer2();
                return m_customPadInf;
            }



            result = m_getInputDeleg(a1);
            return result;
        }

        public static void GetPlayer2AttackInput()
        {
            Joystick.Poll();

            int x = Joystick.GetCurrentState().X;
            int y = Joystick.GetCurrentState().Y;

            var datas = Joystick.GetBufferedData();

            //Guard
            if (Joystick.GetCurrentState().Buttons[GuardButtonIdx])
                Player2InputFlags |= 1048576;

            //Battle Stance
            if (Joystick.GetCurrentState().Buttons[StanceButtonIdx])
                Player2InputFlags |= 2097152;


            foreach (var state in datas)
            {
                if (state.Offset == LightAttackButton)
                {
                    if (state.Value > 0)
                    {
                        Player2InputFlags |= 1;
                    }
                }
                else if (state.Offset == HeavyAttackButton)
                {
                    if (state.Value > 0)
                    {
                        Player2InputFlags |= 2;
                        ActionFighterManager.SetPlayer(Mod.m_coopPlayerIdx);
                    }
                    else
                    {
                        ActionFighterManager.SetPlayer(0);
                    }
                }
                else if(state.Offset == GrabButton)
                {
                    if (state.Value > 0)
                        Player2InputFlags |= 4;
                }
                else if(state.Offset == QuickstepButton)
                {
                    if (state.Value > 0)
                        Player2InputFlags |= 8;
                }
                else if(state.Offset == DragonRageButton)
                {
                    Player2InputFlags |= 64;
                }

                /*
                switch (state.Offset)
                {
                    
                    //Light Attack
                    case JoystickOffset.Buttons3:
                        if (state.Value > 0)
                            Player2InputFlags |= 1;
                        break;

                    //Heavy Attack
                    case JoystickOffset.Buttons0:
                        if (state.Value > 0)
                            Player2InputFlags |= 2;
                        break;

                    //Grab
                    case JoystickOffset.Buttons1:
                        if (state.Value > 0)
                            Player2InputFlags |= 4;
                        break;

                    //Quickstep

                    case JoystickOffset.Buttons2:
                        if (state.Value > 0)
                            Player2InputFlags |= 8;
                        break;
                        

                }
            */
            }
        }


        public static FighterController_InputUpdate m_controllerInputUpdate;
        public static void FighterController__InputUpdate(void* a1)
        {
            if (Mod.CoopPlayer == null)
            {
                m_controllerInputUpdate(a1);
                return;
            }

            void* fighterPtr = *(void**)((long)a1 + 0x20);
            Fighter fighter = new Fighter() { Pointer = (IntPtr)fighterPtr };

            m_controllerInputUpdate(a1);

            if (Mod.CoopPlayer.Pointer == fighter.Pointer)
            {
                GetPlayer2AttackInput();
                fighter.InputFlags = Player2InputFlags;
                Player2InputFlags = 0;
            }
        }

        public static void UpdatePlayer2()
        {
            var state = Joystick.GetCurrentState();

            float x = (float)Math.Round(state.X / (float)ushort.MaxValue, 3);
            float y = (float)Math.Round(state.Y / (float)ushort.MaxValue, 3);

            if (x - y != 0)
            {
                //weirdPadThing[4] = 0xFF;
            }
            //else
            //weirdPadThing[4] = 0;

            if (!Mod.IsHunting() || (Mod.CoopPlayer.InputFlags & 0x100000) == 0)
            {
                Vector2 leftLever = new Vector2();

                if (x == 0.5f)
                    leftLever.x = 0;
                else if (x < 0.5f)
                    leftLever.x = -1;
                else if (x > 0.5f)
                    leftLever.x = 1;

                if (y == 0.5f)
                    leftLever.y = 0;
                else if (y < 0.5f)
                    leftLever.y = -1;
                else if (y > 0.5f)
                    leftLever.y = 1;

                m_customPadInf->leftLever = leftLever;
            }
            else
            {
                Vector2 rightLever = new Vector2();

                if (y == 0.5f)
                    rightLever.x = 0;
                else if (y < 0.5f)
                    rightLever.x = -1;
                else if (y > 0.5f)
                    rightLever.x = 1;

                if (x == 0.5f)
                    rightLever.y = 0;
                else if (x < 0.5f)
                    rightLever.y = -1;
                else if (x > 0.5f)
                    rightLever.y = 1;

                m_customPadInf->rightLever = rightLever;
            }

        }

        public static void RemapThread()
        {
            JoystickOffset TryGetPressedOffset()
            {
                var datas = PlayerInput.Joystick.GetBufferedData();

                foreach (var data in datas)
                {
                    if (data.Value > 0)
                        return data.Offset;
                }

                return (JoystickOffset)(-1);
            }

            int TryGetPressedButton()
            {
                var state = PlayerInput.Joystick.GetCurrentState();

                for (int i = 0; i < state.Buttons.Length; i++)
                    if (state.Buttons[i])
                        return i;

                return -1;
            }

            OE.LogInfo("Remapping...");
            PlayerInput.Joystick.Poll();
            Thread.Sleep(1000);

            OE.LogInfo("Press light attack button");

            while (true)
            {
                PlayerInput.Joystick.Poll();
                JoystickOffset pressedOffset = TryGetPressedOffset();

                if ((int)pressedOffset > -1)
                {
                    PlayerInput.LightAttackButton = pressedOffset;
                    break;
                }
            }

            Thread.Sleep(500);

            OE.LogInfo("Press Heavy attack button");

            while (true)
            {
                PlayerInput.Joystick.Poll();
                JoystickOffset pressedOffset = TryGetPressedOffset();

                if ((int)pressedOffset > -1)
                {
                    PlayerInput.HeavyAttackButton = pressedOffset;
                    break;
                }
            }


            Thread.Sleep(500);



            OE.LogInfo("Press Grab attack button");

            while (true)
            {
                PlayerInput.Joystick.Poll();
                JoystickOffset pressedOffset = TryGetPressedOffset();

                if ((int)pressedOffset > -1)
                {
                    PlayerInput.GrabButton = pressedOffset;
                    break;
                }
            }

            Thread.Sleep(500);


            OE.LogInfo("Press Quickstep button");

            while (true)
            {
                PlayerInput.Joystick.Poll();
                JoystickOffset pressedOffset = TryGetPressedOffset();

                if ((int)pressedOffset > -1)
                {
                    PlayerInput.QuickstepButton = pressedOffset;
                    break;
                }
            }

            Thread.Sleep(500);


            OE.LogInfo("Press Taunt/Dragon Rage button");

            while (true)
            {
                PlayerInput.Joystick.Poll();
                JoystickOffset pressedOffset = TryGetPressedOffset();

                if ((int)pressedOffset > -1)
                {
                    PlayerInput.DragonRageButton = pressedOffset;
                    break;
                }
            }


            Thread.Sleep(500);


            OE.LogInfo("Press Guard button");

            while (true)
            {
                PlayerInput.Joystick.Poll();
                int pressedButton = TryGetPressedButton();

                if (pressedButton > -1)
                {
                    PlayerInput.GuardButtonIdx = pressedButton;
                    break;
                }
            }

            Thread.Sleep(500);

            OE.LogInfo("Press battle stance button");


            while (true)
            {
                PlayerInput.Joystick.Poll();
                int pressedButton = TryGetPressedButton();

                if (pressedButton > -1)
                {
                    PlayerInput.StanceButtonIdx = pressedButton;
                    break;
                }
            }

            SaveToIni();

            OE.LogInfo("Remapping complete. Saved to Ini");
        }   
        
        private static void SaveToIni()
        {
            Ini ini = new Ini(IniSettings.IniPath);

            ini.WriteValue("LightAttackButton", "Bindings", LightAttackButton.ToString());
            ini.WriteValue("HeavyAttackButton", "Bindings", HeavyAttackButton.ToString());
            ini.WriteValue("QuickstepButton", "Bindings", QuickstepButton.ToString());
            ini.WriteValue("GrabButton", "Bindings", GrabButton.ToString());
            ini.WriteValue("GuardButton", "Bindings", GuardButtonIdx.ToString());
            ini.WriteValue("StanceButton", "Bindings", StanceButtonIdx.ToString());
            ini.WriteValue("DragonRageButton", "Bindings", DragonRageButton.ToString());

            ini.Save();
        }
    }
}
