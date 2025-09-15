using System;
using Y5Lib;
using Y5Lib.Unsafe;
namespace Y5Coop
{
    public static class FighterPatches
    {
        private delegate ulong DancerDestructor(IntPtr dancer, byte idk, ulong idk2, ulong idk3, ulong idk4, ulong idk5);
        private delegate ulong DancerDestructor2(IntPtr dancer);
        private delegate ulong FighterPreDestroy(IntPtr dancer);
        private unsafe delegate void FighterModeEquipUpdate(IntPtr fighterMode);

        public static void Init()
        {
            dieDancer = Mod.engine.CreateHook<DancerDestructor>(CPP.PatternSearch("41 56 48 83 EC ? 48 C7 44 24 20 ? ? ? ? 48 89 5C 24 40 48 89 6C 24 48 48 89 74 24 50 48 89 7C 24 58 44 8B F2 48 8B F1 48 8D 05 ? ? ? ? 48 89 01 48 8D 99 30 17 00 00"), Dancer_Destructor);
            dieLiveDancer = Mod.engine.CreateHook<DancerDestructor2>(CPP.PatternSearch("40 53 48 83 EC ? 48 8B D9 48 8B 89 ? ? ? ? 48 8B 01 FF 50 ? F6 83"), LiveDancer_Destructor);
            m_fighterPreDestroyOrig = Mod.engine.CreateHook<FighterPreDestroy>(CPP.PatternSearch("40 53 48 83 EC ? 48 8B 01 BA ? ? ? ? 48 8B D9 FF 90 ? ? ? ? 48 8B CB E8 ? ? ? ? 48 8B CB"), Fighter_PreDestroy);
            m_fighterModeEquipUpdateOrig = Mod.engine.CreateHook<FighterModeEquipUpdate>(CPP.PatternSearch("48 89 5C 24 ? 57 48 83 EC ? 48 8B F9 48 89 74 24"), FighterMode_Equip_Update);

            Mod.engine.EnableHook(dieDancer);
            Mod.engine.EnableHook(dieLiveDancer);
            Mod.engine.EnableHook(m_fighterPreDestroyOrig);
            Mod.engine.EnableHook(m_fighterModeEquipUpdateOrig);
        }

        private static DancerDestructor dieDancer;
        private static ulong Dancer_Destructor(IntPtr dancer, byte idk, ulong idk2, ulong idk3, ulong idk4, ulong idk5)
        {
            Mod.DontRespawnPlayerThisMissionDoOnce = true;

            //We must send a destroy signal to ActionFighterManager precisely at this function
            //Otherwise, our game will get stuck!
            //How the fuck does this happen? How did i even fix this?
            //..i have no idea!
            if (Mod.CoopPlayerIdx > -1)
            {
                PlayerInput.ResetPlayer2Input();
                ActionFighterManager.DestroyFighter(Mod.CoopPlayerIdx);
                Mod.Reset();
            }
            return dieDancer(dancer, idk, idk2, idk3, idk4, idk5);
        }

        private static DancerDestructor2 dieLiveDancer;
        private static ulong LiveDancer_Destructor(IntPtr dancer)
        {
            Mod.DontRespawnPlayerThisMissionDoOnce = true;

            //We must send a destroy signal to ActionFighterManager precisely at this function
            //Otherwise, our game will get stuck!
            //How the fuck does this happen? How did i even fix this?
            //..i have no idea!
            if (Mod.CoopPlayerIdx > -1)
            {
                Mod.DestroyPlayer2();
            }
            return dieLiveDancer(dancer);
        }

        private static FighterPreDestroy m_fighterPreDestroyOrig;
        private static ulong Fighter_PreDestroy(IntPtr fighterPtr)
        {
            //Resetting input like this is necessary to prevent crashes.
            if (fighterPtr == ActionFighterManager.GetFighter(0).Pointer || (ActionFighterManager.IsFighterPresent(Mod.CoopPlayerIdx) && Mod.CoopPlayer.Pointer == fighterPtr))
            {
                Fighter fighter = new Fighter() { Pointer = fighterPtr };
                fighter.InputController.SetSlot(ActionInputManager.GetInputDeviceSlot(InputDeviceType.All));
            }

            return m_fighterPreDestroyOrig(fighterPtr);
        }

        private static FighterModeEquipUpdate m_fighterModeEquipUpdateOrig;
        private static void FighterMode_Equip_Update(IntPtr fighterModePtr)
        {
            FighterMode mode = new FighterMode() { Pointer = fighterModePtr };
            Fighter fighter = mode.Fighter;

            if (Mod.CoopPlayerHandle.IsValid() && fighter.Index == Mod.CoopPlayerIdx)
                ActionFighterManager.SetPlayer(Mod.CoopPlayerIdx);

            m_fighterModeEquipUpdateOrig.Invoke(fighterModePtr);
        }
    }
}
