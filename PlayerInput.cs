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
        public delegate long Fighter_PreDestroy(IntPtr a1, long idk1, long idk2, long idk3);
        private delegate bool Fighter_SomeMysteriousInputUpdate(void* a1, ulong arg1, ulong arg2);

        private delegate long CInputDeviceSlot_UpdateData(IntPtr a1, long idk2, long idk3, long idk4);

        static Fighter_SomeMysteriousInputUpdate m_origUnknownFighterInputUpdate;
        static CInputDeviceSlot_UpdateData m_updateDataOrig;
        static Fighter_PreDestroy m_origDestructor;

         public static InputDeviceType Player1InputType = InputDeviceType.Keyboard;
        public static InputDeviceType Player2InputType = InputDeviceType.Controller;

        //If we do not route this to player1, game freezes when interacting with CCC
        //Bizzare bug that exists since Kenzan/Y3
        static bool FighterUnknownInputUpdate(void* fighter, ulong arg1, ulong arg2)
        {
            return m_origUnknownFighterInputUpdate.Invoke((void*)ActionFighterManager.GetFighter(0).Pointer, arg1, arg2);
        }

        unsafe static long FighterDestructor(IntPtr addr, long idk1, long idk2, long idk3)
        {
            if(Mod.CoopPlayer != null && addr == Mod.CoopPlayer.Pointer)
            {
                //if we dont restore input slot to original before we get destroyed
                //its like a 70% chance of crashing! Unacceptable
                ResetPlayer2Input();
            }

           return m_origDestructor(addr, idk1, idk2, idk3);
        }

        public static void ResetPlayer2Input()
        {
            if(Mod.CoopPlayer != null)
                Mod.CoopPlayer.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));
        }

        unsafe static long UpdateData(IntPtr addr, long idk2, long idk3, long idk4)
        {
            bool playerExists = Mod.m_coopPlayerIdx > -1 && ActionFighterManager.IsFighterPresent(Mod.m_coopPlayerIdx);

            if (!playerExists || ActionManager.IsPaused())
                return m_updateDataOrig(addr, idk2, idk3, idk4);

            uint* deviceTypePtr = (uint*)(addr + 0x12a4);
            uint deviceType = *deviceTypePtr;

            long result = 0;

            //0 device type in input means take input from everything, keyboard and controller
            //We don't want this. Otherwise player1 will be moved simultaneously by kbd and controller
            //So we force it to be updated in keyboard mode (device type 9) if the coop player exists
            if (deviceType == 0 && !Mod.IsDance())
            {
                    *deviceTypePtr = 9;
                    result = m_updateDataOrig(addr, idk2, idk3, idk4);
                    *deviceTypePtr = 0;
            }
            else
                result = m_updateDataOrig(addr, idk2, idk3, idk4);

            return result;
        }

        public static void Init()
        {
            m_updateDataOrig = Mod.engine.CreateHook<CInputDeviceSlot_UpdateData>((IntPtr)0x140F4D070, UpdateData);
            m_origDestructor = Mod.engine.CreateHook<Fighter_PreDestroy>(Y5Lib.Unsafe.CPP.PatternSearch("40 53 48 83 EC ? 48 8B 01 BA ? ? ? ? 48 8B D9 FF 90 F8 01 00 00"), FighterDestructor);

            IntPtr fighterInputUpdateAddr = Y5Lib.Unsafe.CPP.PatternSearch("40 56 41 57 48 81 EC ? ? ? ? C5 78 29 44 24 60");
            if (fighterInputUpdateAddr == IntPtr.Zero)
            {
                OE.LogError("Y5Coop - Couldn't find fighter input update function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find fighter input update function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            IntPtr fighterInputUpdate2Addr = Y5Lib.Unsafe.CPP.PatternSearch("48 83 EC ? 8B 91 30 34 00 00 0F BA E2 ? 72 ?");

            if (fighterInputUpdate2Addr == IntPtr.Zero)
            {
                OE.LogError("Y5Coop - Couldn't find fighter input update 2 function.");
                Mod.MessageBox(IntPtr.Zero, "Y5Coop - Couldn't find fighter input update 2 function.", "Fatal Y5 Coop Error", 0);
                Environment.Exit(0);
            }

            m_controllerInputUpdate = Mod.engine.CreateHook<FighterController_InputUpdate>(fighterInputUpdateAddr, FighterController__InputUpdate);
            m_origUnknownFighterInputUpdate = Mod.engine.CreateHook<Fighter_SomeMysteriousInputUpdate>(fighterInputUpdate2Addr, FighterUnknownInputUpdate);
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

            //If we do not do this player 2 will not be able to perform heat actions.
            //Heat action checking is only done on player 1!
            //So what we do is make coop player the main player very briefly
            //When we recieve triangle (heat action) input
            if (Mod.CoopPlayer.Pointer == fighter.Pointer)
            {
                if ((Mod.CoopPlayer.InputFlags & 131072) != 0)
                {
                    ActionFighterManager.SetPlayer(Mod.m_coopPlayerIdx);
                }
                else
                {
                    ActionFighterManager.SetPlayer(0);
                }
            }
        }
    }
}
