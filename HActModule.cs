using System;
using System.IO;
using System.Runtime.InteropServices;
using Y5Lib;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    internal static class HActModule
    {

        private delegate ulong CActionHActManager_ProcessHActCharacters(IntPtr hactMan, ulong idk1, ulong idk2, ulong idk3);
        static CActionHActManager_ProcessHActCharacters ProcessHActCharacters;

        private delegate void RegisterFighterHAct(uint idk, string replaceName, int fighterID, int idk2);
        static RegisterFighterHAct RegisterFighterOnHAct;
        static RegisterFighterHAct RegisterFighterOnHActDetour;

        private delegate ulong CActionHActManagerPrepareHAct(IntPtr hactMan, string hactName, ulong idk3, ulong idk4, ulong idk5, ulong idk6);
        private static CActionHActManagerPrepareHAct PrepareHAct;

        public static DirectoryInfo HActDir;

        public static void Init()
        {
            HActDir = new DirectoryInfo(Path.Combine(Mod.ModPath, "hact"));

            IntPtr hactRegisterFunc = CPP.PatternSearch("48 89 5C 24 08 48 89 74 24 10 57 48 83 EC ? 41 8B F1 41 8B F8 48 8B DA");

            ProcessHActCharacters = Mod.engine.CreateHook<CActionHActManager_ProcessHActCharacters>(CPP.PatternSearch("48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 41 54 41 56 41 57 48 83 EC ? 8B 15 ? ? ? ?"),HActManager_ProcessHActCharacters);
            RegisterFighterOnHAct = Marshal.GetDelegateForFunctionPointer<RegisterFighterHAct>(hactRegisterFunc);
            RegisterFighterOnHActDetour = Mod.engine.CreateHook<RegisterFighterHAct>(hactRegisterFunc, HAct_RegisterFighter);
            PrepareHAct = Mod.engine.CreateHook<CActionHActManagerPrepareHAct>(CPP.PatternSearch("48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC ? 48 8B D9 C5 F8 29 74 24 40"), HActManager_PrepareHAct);


            Mod.engine.EnableHook(ProcessHActCharacters);
            Mod.engine.EnableHook(RegisterFighterOnHActDetour);
            Mod.engine.EnableHook(PrepareHAct);
        }
        unsafe static ulong HActManager_PrepareHAct(IntPtr hactMan, string hactName, ulong idk3, ulong idk4, ulong idk5, ulong idk6)
        {
            //if (ReplacePlayerWithPlayer2Once && Mod.m_coopPlayerIdx < -1)
            // ActionFighterManager.SetPlayer(Mod.m_coopPlayerIdx);

            if (Mod.m_coopPlayerIdx < 0)
                return PrepareHAct(hactMan, hactName, idk3, idk4, idk5, idk6);

            if(!ActionFighterManager.IsFighterPresent(Mod.m_coopPlayerIdx))
                return PrepareHAct(hactMan, hactName, idk3, idk4, idk5, idk6);

            //override into coop version of the hact if it exists
            //eg: a31140_chase_dropkick _> a31140_chase_dropkick_coop
            if (HActDir.Exists)
            {
                string coopVariant = hactName + "_coop";

                //coop version of the hact exists and so does the coop player
                //lets override to this version!
                if (Directory.Exists(Path.Combine(HActDir.FullName, coopVariant)))
                    hactName = coopVariant;
            }

            return PrepareHAct(hactMan, hactName, idk3, idk4, idk5, idk6);
        }

        unsafe static void HAct_RegisterFighter(uint idk, string replaceName, int fighterID, int idk3)
        {

            RegisterFighterOnHActDetour(idk, replaceName, fighterID, idk3);
        }

        unsafe static ulong HActManager_ProcessHActCharacters(IntPtr hactMan, ulong idk1, ulong idk2, ulong idk3)
        {
            if (Mod.m_coopPlayerIdx < 0)
                return ProcessHActCharacters(hactMan, idk1, idk2, idk3);

            uint hactID = *(uint*)(hactMan + 0x830);
            ulong result = ProcessHActCharacters(hactMan, idk1, idk2, idk3);

            //Register the coop player onto hacts.
            //Any hact that includes ZA_HUCOOP0 character will now have the coop player present
            RegisterFighterOnHAct(hactID, "ZA_HUCOOP0", Mod.m_coopPlayerIdx, 1);

            return result;
        }
    }
}
