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
            string blacklistFile = Path.Combine(Mod.Instance.ModPath, "coop_hact_blacklist.txt");

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

                m_hactCoopBlacklist.Clear();

                foreach(string str in cleanedLines)
                    m_hactCoopBlacklist.Add(str.ToLowerInvariant());
            }

            HActDir = new DirectoryInfo(Path.Combine(Mod.Instance.ModPath, "hact"));

            IntPtr hactRegisterFunc = CPP.PatternSearch("48 89 5C 24 08 48 89 74 24 10 57 48 83 EC ? 41 8B F1 41 8B F8 48 8B DA");

            ProcessHActCharacters = Mod.engine.CreateHook<CActionHActManager_ProcessHActCharacters>(CPP.PatternSearch("48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 41 54 41 56 41 57 48 83 EC ? 8B 15 ? ? ? ?"),HActManager_ProcessHActCharacters);
            RegisterFighterOnHAct = Marshal.GetDelegateForFunctionPointer<RegisterFighterHAct>(hactRegisterFunc);
            PrepareHAct = Mod.engine.CreateHook<CActionHActManagerPrepareHAct>(CPP.PatternSearch("48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC ? 48 8B D9 C5 F8 29 74 24 40"), HActManager_PrepareHAct);

            m_invokeHactOrig = Mod.engine.CreateHook<InvokeHAct>(CPP.PatternSearch("48 8B C4 57 48 83 EC ? 48 C7 40 ? ? ? ? ? 48 89 58 ? 48 89 70 ? C5 F8 29 70 ? 48 8B F1 E8 ? ? ? ? 45 33 C0 BA"), Invoke_Hact);       
            m_origPreloadHAct = Mod.engine.CreateHook<PreloadHAct>(CPP.PatternSearch("48 8B C4 57 41 54 41 55 41 56 41 57 48 83 EC ? 48 C7 40 ? ? ? ? ? 48 89 58 ? 48 89 68 ? 48 89 70 ? 45 8B E0"), Preload_HAct);

            //Remove useless checks in hact.chp for targets that dont exist (greater than 6)
            //No target in hact chp is ever greater than 6 in vanilla game so we can do this funny party trick for Y5 Co-op
            CPP.PatchMemory(CPP.PatternSearch("FF 90 ? ? ? ? 85 C0 75 ? 48 8B 03 48 8B CB FF 90 ? ? ? ? 85 C0 0F 85"), new byte[] { 0xB8, 0x01, 0x0, 0x0, 0x0, 0x90 });

            Mod.engine.EnableHook(m_invokeHactOrig);
            Mod.engine.EnableHook(m_origPreloadHAct);

            Mod.engine.EnableHook(ProcessHActCharacters);
            Mod.engine.EnableHook(RegisterFighterOnHActDetour);
            Mod.engine.EnableHook(PrepareHAct);

        }

        private static IntPtr m_hactNameBuff = Marshal.AllocHGlobal(256);
        public static bool CoopHActExists(string hactName)
        {
            string path = "data/hact/" + hactName + "_coop.par";
            string result = Parless.GetFilePath(path);
 
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

            return m_invokeHactOrig(a1, unknown);
        }

        unsafe static ulong HActManager_PrepareHAct(IntPtr hactMan, string hactName, ulong idk3, ulong idk4, ulong idk5, ulong idk6)
        {
            if (NextHActIsByCoopPlayer && m_hactCoopBlacklist.Contains(hactName.ToLowerInvariant()))
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
                {
                    NextHActIsByCoopPlayer = false;
                    hactName = coopVariant;
                }
               
            }

            return PrepareHAct(hactMan, hactName, idk3, idk4, idk5, idk6);
        }

        unsafe static ulong HActManager_ProcessHActCharacters(IntPtr hactMan, ulong idk1, ulong idk2, ulong idk3)
        {

            IntPtr registersStart = hactMan + 0x8E0;
            uint hactID = *(uint*)(hactMan + 0x830);

            EntityUID* chara1RegisterUID = (EntityUID*)(registersStart + 0x24);

            if (Mod.m_coopPlayerIdx < 0)
            {
                return ProcessHActCharacters(hactMan, idk1, idk2, idk3);
            }

            string hactName = ActionHActCHPManager.CurrentName;

            //Extra check here because invokehact is not always called
            if(!string.IsNullOrEmpty(hactName))
            {
                if (NextHActIsByCoopPlayer && m_hactCoopBlacklist.Contains(hactName.ToLowerInvariant()))
                    NextHActIsByCoopPlayer = false;

                if (hactName.Contains("coop"))
                    NextHActIsByCoopPlayer = false;
            }

            if (NextHActIsByCoopPlayer)
            {
                //Override original player 1 values with player 2 data
                Vector4* chara1RegisterPos = (Vector4*)(registersStart + 0x10);
                ushort* chara1RegisterRotY = (ushort*)(registersStart + 0x38);
                *chara1RegisterPos = Mod.CoopPlayer.Position;
                *chara1RegisterRotY = Mod.CoopPlayer.RotationY;
                chara1RegisterUID->Serial = Mod.CoopPlayer.UID.Serial;
            }
            else
            {
                if (Mod.CoopPlayer.Index > 0)
                    chara1RegisterUID->Serial = ActionFighterManager.GetFighter(0).UID.Serial;


                //Register the coop player onto hacts.
                //Any hact that includes ZA_HUCOOP0 character will now have the coop player present
                RegisterFighterOnHAct(hactID, "ZA_HUCOOP0", Mod.m_coopPlayerIdx, 1);
            }

            ulong result = ProcessHActCharacters(hactMan, idk1, idk2, idk3);
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
