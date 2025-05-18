using Microsoft.SqlServer.Server;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

        private delegate IntPtr InvokeHAct(IntPtr a1, float unknown);
        private delegate IntPtr CActionHActChpManagerProcessRegisters(IntPtr a1);

        private delegate ulong CActionHActManagerPrepareHAct(IntPtr hactMan, string hactName, ulong idk3, ulong idk4, ulong idk5, ulong idk6);
        private static CActionHActManagerPrepareHAct PrepareHAct;

        private delegate long PreloadHAct(string name, string path, int flags);

        public static DirectoryInfo HActDir;

        public static bool NextHActIsByCoopPlayer = false;

        //Never set the co-op player as ZA_HUPLAYER
        private static HashSet<string> m_hactCoopBlacklist = new HashSet<string>();
        private static HashSet<string> m_coopHacts = new HashSet<string>();

        public static void Init()
        {
            string blacklistFile = Path.Combine(Mod.ModPath, "coop_hact_blacklist.txt");

            if(File.Exists(blacklistFile))
            {
                string[] blacklist = File.ReadAllLines(blacklistFile);

                var cleanedLines = blacklist
                    .Select(line => {
                        int commentIndex = line.IndexOf("//");
                        return commentIndex >= 0 ? line.Substring(0, commentIndex).Trim() : line.Trim();
                    })
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                foreach(string str in cleanedLines)
                    m_hactCoopBlacklist.Add(str.ToLowerInvariant());
            }

            HActDir = new DirectoryInfo(Path.Combine(Mod.ModPath, "hact"));

            IntPtr hactRegisterFunc = CPP.PatternSearch("48 89 5C 24 08 48 89 74 24 10 57 48 83 EC ? 41 8B F1 41 8B F8 48 8B DA");

            ProcessHActCharacters = Mod.engine.CreateHook<CActionHActManager_ProcessHActCharacters>(CPP.PatternSearch("48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 41 54 41 56 41 57 48 83 EC ? 8B 15 ? ? ? ?"),HActManager_ProcessHActCharacters);
            RegisterFighterOnHAct = Marshal.GetDelegateForFunctionPointer<RegisterFighterHAct>(hactRegisterFunc);
            RegisterFighterOnHActDetour = Mod.engine.CreateHook<RegisterFighterHAct>(hactRegisterFunc, HAct_RegisterFighter);
            PrepareHAct = Mod.engine.CreateHook<CActionHActManagerPrepareHAct>(CPP.PatternSearch("48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC ? 48 8B D9 C5 F8 29 74 24 40"), HActManager_PrepareHAct);


            m_invokeHactOrig = Mod.engine.CreateHook<InvokeHAct>((IntPtr)0x140B797F0, Invoke_Hact);
          //  m_origProcRegisters = Mod.engine.CreateHook<CActionHActChpManagerProcessRegisters>((IntPtr)0x140DA0360, CActionHActChpManager_ProcessRegisters);
            m_origPreloadHAct = Mod.engine.CreateHook<PreloadHAct>((IntPtr)0x140EBA020, Preload_HAct);

            Mod.engine.EnableHook(m_invokeHactOrig);
            //Mod.engine.EnableHook(m_origProcRegisters);
            Mod.engine.EnableHook(m_origPreloadHAct);

            Mod.engine.EnableHook(ProcessHActCharacters);
            Mod.engine.EnableHook(RegisterFighterOnHActDetour);
            Mod.engine.EnableHook(PrepareHAct);

        }

        private static IntPtr m_hactNameBuff = Marshal.AllocHGlobal(256);
        public static bool CoopHActExists(string hactName)
        {
            string path = "data/hact/" + hactName + "_coop.par";

            Marshal.Copy(new byte[256], 0, m_hactNameBuff, 256);

            byte[] strBuf = Encoding.ASCII.GetBytes(path);
            Marshal.Copy(strBuf, 0, m_hactNameBuff, strBuf.Length);
            string result = Parless.GetFilePath(m_hactNameBuff);
            OE.LogInfo(Directory.GetCurrentDirectory());

            bool exists = false;

            if (File.Exists(result) && result.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
                exists = true;

            return exists;
        }


        static InvokeHAct m_invokeHactOrig;
        static IntPtr Invoke_Hact(IntPtr a1, float unknown)
        {
            NextHActIsByCoopPlayer = false;

            if(Mod.CoopPlayer == null || !ActionFighterManager.IsFighterPresent(Mod.m_coopPlayerIdx))
                return m_invokeHactOrig(a1, unknown);

            if (Mod.CoopPlayer.Index > 0 && ActionFighterManager.Player.Pointer == Mod.CoopPlayer.Pointer)
                NextHActIsByCoopPlayer = true;

            ActionFighterManager.SetPlayer(0);

            //OE.LogInfo("WE JUST HAVE TO PUSH ON. WE DONT GET TO GIVE UP THIS LIFE");

            return m_invokeHactOrig(a1, unknown);
        }

        unsafe static ulong HActManager_PrepareHAct(IntPtr hactMan, string hactName, ulong idk3, ulong idk4, ulong idk5, ulong idk6)
        {
            if (m_hactCoopBlacklist.Contains(hactName.ToLowerInvariant()) && NextHActIsByCoopPlayer)
                NextHActIsByCoopPlayer = false;

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
            //if (replaceName == "ZA_HUPLAYER")
                //fighterID = Mod.m_coopPlayerIdx;

            RegisterFighterOnHActDetour(idk, replaceName, fighterID, idk3);
        }

        unsafe static ulong HActManager_ProcessHActCharacters(IntPtr hactMan, ulong idk1, ulong idk2, ulong idk3)
        {
            if (Mod.m_coopPlayerIdx < 0)
                return ProcessHActCharacters(hactMan, idk1, idk2, idk3);

            uint hactID = *(uint*)(hactMan + 0x830);

            EntityUID test = ActionFighterManager.Player.UID;
            IntPtr registersStart = hactMan + 0x8E0;

            EntityUID* chara1RegisterUID = (EntityUID*)(registersStart + 0x24);

            if (NextHActIsByCoopPlayer)
            {
                //if (chara1RegisterUID->Serial == ActionFighterManager.GetFighter(0).UID.Serial)
                chara1RegisterUID->Serial = Mod.CoopPlayer.UID.Serial;
            }
            else
            {
                if(Mod.CoopPlayer.Index > 0)
                    chara1RegisterUID->Serial = ActionFighterManager.GetFighter(0).UID.Serial;

                RegisterFighterOnHAct(hactID, "ZA_HUCOOP0", Mod.m_coopPlayerIdx, 1);
            }

            ulong result = ProcessHActCharacters(hactMan, idk1, idk2, idk3);

            //Register the coop player onto hacts.
            //Any hact that includes ZA_HUCOOP0 character will now have the coop player present


            return result;
        }



        static PreloadHAct m_origPreloadHAct;
        unsafe static long Preload_HAct(string hactName, string path, int flags)
        {
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

            return m_origPreloadHAct(hactName, path, flags);
        }
    }
}
