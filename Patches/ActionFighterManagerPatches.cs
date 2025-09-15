using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Y5Lib;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    internal static class ActionFighterManagerPatches
    {
        private delegate IntPtr CActionFighterManagerGetFighterByUID(IntPtr fighterMan, uint serial);

        public static void Init()
        {
            m_origGetFighterByUID = Mod.engine.CreateHook<CActionFighterManagerGetFighterByUID>(CPP.ReadCall(CPP.ReadCall(CPP.PatternSearch("E8 ? ? ? ? 48 85 C0 0F 84 ? ? ? ? 48 8B 10 48 8B C8 8B 7B"))), ActionFighterManager_GetFighterByUID);

            Mod.engine.EnableHook(m_origGetFighterByUID);
        }

        //Bizzare bug that causes UIDs to not work by itself sometimes
        //Its up to us to tell the game to quit being fuckin stupid!
        private static CActionFighterManagerGetFighterByUID m_origGetFighterByUID;
        private static IntPtr ActionFighterManager_GetFighterByUID(IntPtr fighterManager, uint serial)
        {

            IntPtr result = m_origGetFighterByUID(fighterManager, serial);

            if (result == IntPtr.Zero)
            {
                //0XBEEF is our special UID that shall always refer to the co-op player.
                //Our hooked HActManager_ProcessHActCharacters will register the co-op player's serial as 0xBEEF
                //Giving Kasuga a consistent UID that can never fail.
                if (serial == 0XBEEF && ActionFighterManager.IsFighterPresent(Mod.CoopPlayerIdx) && Mod.CoopPlayer != null)
                    return Mod.CoopPlayer.Pointer;

                if (ActionFighterManager.IsFighterPresent(Mod.CoopPlayerIdx) && Mod.CoopPlayer != null && serial == Mod.CoopPlayer.UID.Serial)
                    return Mod.CoopPlayer.Pointer;
            }

            return m_origGetFighterByUID(fighterManager, serial);
        }
    }
}
