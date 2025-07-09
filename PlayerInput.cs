using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Y5Lib;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    public unsafe static class PlayerInput
    {
        private delegate void* DeviceListener_GetInput(void* a1);
        public delegate void FighterController_InputUpdate(void* a1);
        public delegate long Fighter_PreDestroy(IntPtr a1, long idk1, long idk2, long idk3);
        private delegate bool Fighter_SomeMysteriousInputUpdate(void* a1, ulong arg1, ulong arg2);
        private delegate void GameInputUpdate(IntPtr a1, IntPtr a2, int a3, IntPtr a4);

        private delegate long CInputDeviceSlot_UpdateData(IntPtr a1, long idk2, long idk3, long idk4);
        private delegate long CInputDeviceSlot_UpdateData2(IntPtr a1, long idk2, long idk3, long idk4);

        static Fighter_SomeMysteriousInputUpdate m_origUnknownFighterInputUpdate;
        static CInputDeviceSlot_UpdateData m_updateDataOrig;
        static CInputDeviceSlot_UpdateData m_updateDataOrig2;
        static Fighter_PreDestroy m_origDestructor;

        private static int* m_inputVals = (int*)0;
        private static IntPtr m_inputValAssignmentAddr = IntPtr.Zero;
        private static IntPtr m_activeDeviceAssignmentAddr = IntPtr.Zero;


        public static bool IsPlayer1InputCalibrated = false;
        public static bool IsInputCalibrated = false;

        public static InputDeviceType Player1InputType = InputDeviceType.All;
        public static InputDeviceType Player2InputType = InputDeviceType.All;

        public static bool Player1ForcedInput = false;
        public static bool Player2ForcedInput = false;

        public static bool TwoControllerMode = false;
        public static bool AllowPlayer1InputCalibration = false;
        public static bool AllowPlayer2InputCalibration = true;

        public static bool IsLegacyInput { get { return IsLegacyDualshock || IsLegacyGenericController; } }
        public static bool IsLegacyGenericController = false;
        public static bool IsLegacyDualshock = false;

        public static bool FuckedUpPlayer1WorkAround = false;

        public static void Init()
        {
            m_inputValAssignmentAddr = CPP.PatternSearch("89 07 FF C3 48 83 C7 ? 83 FB ? 7C ?");
            m_inputVals = (int*)CPP.ResolveRelativeAddress(CPP.PatternSearch("48 8D 0D ? ? ? ? 49 89 73 10"), 7);
            m_activeDeviceAssignmentAddr = CPP.PatternSearch("8D 42 FD 89 51 3C");

            m_updateDataOrig = Mod.engine.CreateHook<CInputDeviceSlot_UpdateData>(CPP.PatternSearch("8B 81 A4 12 00 00"), UpdateData);
            m_updateDataOrig2 = Mod.engine.CreateHook<CInputDeviceSlot_UpdateData>(CPP.PatternSearch("48 8B C4 48 89 58 08 57 48 81 EC ? ? ? ? C5 F8 29 70 E8 48 8B D9 48 8B 0D ? ? ? ?"), UpdateData2);
            m_origDestructor = Mod.engine.CreateHook<Fighter_PreDestroy>(CPP.PatternSearch("40 53 48 83 EC ? 48 8B 01 BA ? ? ? ? 48 8B D9 FF 90 F8 01 00 00"), FighterDestructor);
            m_origInputUpdate = Mod.engine.CreateHook<GameInputUpdate>(CPP.PatternSearch("48 85 D2 0F 84 ? ? ? ? 4C 8B DC 49 89 5B 18 57"), Game_Input_Update);


            IntPtr fighterInputUpdateAddr = CPP.PatternSearch("40 56 41 57 48 81 EC ? ? ? ? C5 78 29 44 24 60");
            if (fighterInputUpdateAddr == IntPtr.Zero)
            {
                OE.LogError("Y5Coop - Couldn't find fighter input update function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find fighter input update function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            IntPtr fighterInputUpdate2Addr = CPP.PatternSearch("48 83 EC ? 8B 91 30 34 00 00 0F BA E2 ? 72 ?");

            if (fighterInputUpdate2Addr == IntPtr.Zero)
            {
                OE.LogError("Y5Coop - Couldn't find fighter input update 2 function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find fighter input update 2 function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            m_controllerInputUpdate = Mod.engine.CreateHook<FighterController_InputUpdate>(fighterInputUpdateAddr, FighterController__InputUpdate);
            m_origUnknownFighterInputUpdate = Mod.engine.CreateHook<Fighter_SomeMysteriousInputUpdate>(fighterInputUpdate2Addr, FighterUnknownInputUpdate);
        }



        public static void Calibrate()
        {
            if (AllowPlayer1InputCalibration && !Player1ForcedInput && !IsPlayer1InputCalibrated)
            {
                    for (int i = 1; i < 10; i++)
                    {
                        InputDeviceType type = (InputDeviceType)i;

                        if (IsInputSlotFree(type) && CheckIsThereAnyInput(type))
                        {
                            if (type != Player1InputType)
                            {
                                OE.LogInfo("Detected player 1 device type: " + type);
                                OnInputCalibrated(type, true);
                                IsPlayer1InputCalibrated = true;
                            }
                        }
                    }           
            }

            if (AllowPlayer2InputCalibration && !Player2ForcedInput)
            {
                for (int i = 1; i < 10; i++)
                {
                    InputDeviceType type = (InputDeviceType)i;

                    if (IsInputSlotFree(type) && CheckIsThereAnyInput(type) && !IsInputCalibrated)
                    {
                        if (type != Player2InputType)
                        {
                            OE.LogInfo("Detected player 2 device type: " + type);
                            OnInputCalibrated(type, false);
                            IsInputCalibrated = true;
                        }
                    }
                }
            }
        }

        public static void OnInputCalibrated(InputDeviceType input, bool isPlayer1)
        {
            //basically keyboard
            if (input == InputDeviceType.Keyboard)
                input = InputDeviceType.All;

            DisableInputOverride();

            if (isPlayer1)
                Player1InputType = input;
            else
                Player2InputType = input;

            Fighter fighter = null;

            if (isPlayer1)
                fighter = ActionFighterManager.Player;
            else
                fighter = Mod.CoopPlayer;

            fighter.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(input));

            EnableInputOverride();
        }


        public static bool IsInputSlotFree(InputDeviceType type)
        {
            if (IsPlayer1InputCalibrated)
                if (Player1InputType == type || (IsKBD(type) && Player1InputType == InputDeviceType.All))

                    return false;

            if (IsInputCalibrated)
                if (Player2InputType == type || (IsKBD(type) && Player2InputType == InputDeviceType.All))
                    return false;

            return true;
        }

        public static bool CheckIsThereAnyInput(InputDeviceType type)
        {
            IntPtr controllerData = InputDeviceData.GetRawData(type);

            short val = Marshal.ReadInt16(controllerData);

            if (val != 0)
                return true;

            if (Marshal.ReadInt64(controllerData + 0x8) != 0)
                return true;

            if (Marshal.ReadInt64(controllerData + 0x18) != 0)
                return true;

            return false;
        }


        public static bool IsKBD(InputDeviceType type)
        {
            if (!Mod.CoopPlayerHandle.IsValid())
                return type == InputDeviceType.Keyboard;
            else
                return type == InputDeviceType.All || type == InputDeviceType.Keyboard;
        }

        private static bool m_overriden = false;
        public static void EnableInputOverride()
        {
            if (m_overriden)
                return;

            CPP.NopMemory(m_inputValAssignmentAddr, 2);

            if (!IsLegacyInput)
            {
                m_inputVals[4] = m_inputVals[0];
                m_inputVals[0] = 0;
            }

            if (IsLegacyDualshock)
            {
                m_inputVals[0] = 0;
            }

            m_overriden = true;
        }

        public static void DisableInputOverride()
        {
            if (!m_overriden)
                return;

            if (!IsLegacyInput)
            {
                m_inputVals[0] = m_inputVals[4];
                m_inputVals[4] = 0;
            }

            if (IsLegacyDualshock)
            {
                m_inputVals[0] = 1000;
            }

            m_overriden = false;
        }

        //Prevent the game from changing the "active" device. Preventing UI buttons from changing
        public static void AllowActiveDeviceAssignment() 
        {
           CPP.PatchMemory(m_activeDeviceAssignmentAddr, new byte[] { 0x8D, 0x42, 0xFD });
        }

        public static void PreventActiveDeviceAssignment()
        {
            CPP.PatchMemory(m_activeDeviceAssignmentAddr, new byte[] { 0xC3, 0x90, 0x90 });
        }

        private static GameInputUpdate m_origInputUpdate;
        public static void Game_Input_Update(IntPtr a1, IntPtr a2, int a3, IntPtr a4)
        {
            if(!Mod.CoopPlayerHandle.IsValid())
            {
                AllowActiveDeviceAssignment();
                m_origInputUpdate(a1, a2, a3, a4);
                return;
            }

            InputDeviceType type = (InputDeviceType)Marshal.ReadInt32(a1 + 224);


            InputDeviceType realType = Player1InputType;

            if (realType == InputDeviceType.All)
                realType = InputDeviceType.Keyboard;

            if(type != realType)
                PreventActiveDeviceAssignment();

            m_origInputUpdate(a1, a2, a3, a4);

            AllowActiveDeviceAssignment();
        }


        //If we do not route this to player1, game freezes when interacting with CCC
        //Bizzare bug that exists since Kenzan/Y3
        //Not in Yakuza 0 though! my goat!
        static bool FighterUnknownInputUpdate(void* fighter, ulong arg1, ulong arg2)
        {          
            return m_origUnknownFighterInputUpdate.Invoke((void*)ActionFighterManager.GetFighter(0).Pointer, arg1, arg2);
        }

        unsafe static long FighterDestructor(IntPtr addr, long idk1, long idk2, long idk3)
        {
            if (Mod.CoopPlayer != null && addr == Mod.CoopPlayer.Pointer)
            {
                //if we dont restore input slot to original before we get destroyed
                //its like a 70% chance of crashing! Unacceptable
                ResetPlayer1Input();
                ResetPlayer2Input();
                DisableInputPatches();
            }

            return m_origDestructor(addr, idk1, idk2, idk3);
        }

        public static void ResetPlayer1Input()
        {
            Fighter player = ActionFighterManager.Player;

            if (player.UID != Mod.CoopPlayer.UID)
                player.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));
        }

        public static void ResetPlayer2Input()
        {
            if (Mod.CoopPlayer != null)
                Mod.CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));
        }

        private static long UpdateData(IntPtr addr, long idk2, long idk3, long idk4)
        {
            bool playerExists = Mod.m_coopPlayerIdx > -1 && ActionFighterManager.IsFighterPresent(Mod.m_coopPlayerIdx);

            if (!playerExists || ActionManager.IsPaused())
                return m_updateDataOrig(addr, idk2, idk3, idk4);

            //Dont do this when CCC is active
            if(ActionCCCManager.isActive)
                return m_updateDataOrig(addr, idk2, idk3, idk4);

            uint* deviceTypePtr = (uint*)(addr + 0x12a4);
            uint deviceType = *deviceTypePtr;

            long result = 0;

            //0 device type in input means take input from everything, keyboard and controller
            //We don't want this. Otherwise player1 will be moved simultaneously by kbd and controller
            //So we force it to be updated in keyboard mode (device type 9) if the coop player exists
            if (deviceType == 0 && !Mod.IsDance())
            {
                if (IsLegacyInput)
                    *deviceTypePtr = 9;
                else
                {
                   // if (IsKBD(Player1InputType) || IsKBD(Player2InputType))
                        *deviceTypePtr = 9;
                }
                result = m_updateDataOrig(addr, idk2, idk3, idk4);
                *deviceTypePtr = 0;
            }
            else
                result = m_updateDataOrig(addr, idk2, idk3, idk4);

            return result;
        }

        private static long UpdateData2(IntPtr addr, long idk2, long idk3, long idk4)
        {
            long result = m_updateDataOrig2(addr, idk2, idk3, idk4);

            bool playerExists = Mod.m_coopPlayerIdx > -1 && ActionFighterManager.IsFighterPresent(Mod.m_coopPlayerIdx);

            if (!playerExists || ActionManager.IsPaused())
                return result;

            uint* deviceTypePtr = (uint*)(addr + 0xE0);

            PostInputUpdate(addr, *deviceTypePtr);
            return result;
        }

        //Can be used to overwrite input
        //Particularly used in dance battles for now.
        private static void PostInputUpdate(IntPtr device, uint deviceType)
        {
            if (Mod.IsDance())
                DanceBattleInputUpdate(device, deviceType);
        }

        private static void DanceBattleInputUpdate(IntPtr device, uint deviceType)
        {
            //Co-op dance battle: Player 1 cannot choose move direction
            //Likewise, Player 2 cannot press button inputs. They must work together!

            if (deviceType != 0)
            {
                if ((InputDeviceType)deviceType == Player1InputType || (InputDeviceType)deviceType == InputDeviceType.Keyboard)
                {
                    long* flags = (long*)device;
                    long newVal = *flags;
                    newVal &= ~285216768;
                    newVal &= ~1140867072;
                    newVal &= (int)(~2281734144);
                    newVal &= ~570433536;

                    *flags = newVal;
                }
                else if ((InputDeviceType)deviceType == Player2InputType)
                {
                    long* flags = (long*)device;
                    long newVal = *flags;
                    newVal &= ~8;
                    newVal &= ~1;
                    newVal &= ~4;
                    newVal &= ~2;

                    *flags = newVal;
                }
            }
        }

        private static float m_playerHeavyInputTime = 0;

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

            //If we do not do this player 2 will not be able to perform heat actions.
            //Heat action checking is only done on player 1!
            //So what we do is make coop player the main player very briefly
            //When we recieve triangle (heat action) input
            if (Mod.CoopPlayer.Pointer == fighter.Pointer)
            {

                if (Mod.IsBattle())
                {
                    if(Mod.CoopPlayer.ModeManager.Current.Name == "HActBattleReady")
                    {
                        ActionFighterManager.SetPlayer(Mod.m_coopPlayerIdx);
                    }
                    else
                    {
                        if ((Mod.CoopPlayer.InputFlags & 131072) != 0)
                        {
                            m_playerHeavyInputTime += ActionManager.DeltaTime;

                            if (m_playerHeavyInputTime <= 0.1f)
                                ActionFighterManager.SetPlayer(Mod.m_coopPlayerIdx);
                            else
                                ActionFighterManager.SetPlayer(0);
                        }
                        else
                        {
                            m_playerHeavyInputTime = 0;
                            ActionFighterManager.SetPlayer(0);
                        }
                    }
                }
                else
                {
                    ActionFighterManager.SetPlayer(0);
                }
            }
        }

        private static bool m_enabled = false;
        public static void EnableInputPatches()
        {
            if (m_enabled)
                return;

            Mod.engine.EnableHook(m_origUnknownFighterInputUpdate);
            Mod.engine.EnableHook(m_updateDataOrig);
            Mod.engine.EnableHook(m_updateDataOrig2);
            Mod.engine.EnableHook(m_controllerInputUpdate);
            Mod.engine.EnableHook(m_origInputUpdate);


            m_enabled = true;
        }

        public static void DisableInputPatches()
        {
            if (!m_enabled)
                return;

            Mod.engine.DisableHook(m_origUnknownFighterInputUpdate);
            Mod.engine.DisableHook(m_updateDataOrig);
            Mod.engine.DisableHook(m_updateDataOrig2);
            Mod.engine.DisableHook(m_controllerInputUpdate);
            Mod.engine.DisableHook(m_origInputUpdate);

            m_enabled = false;
        }
    }
}
