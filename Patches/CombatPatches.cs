using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    internal static class CombatPatches
    {
        private static IntPtr m_evilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero;
        private static byte[] m_origBytesevilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero = new byte[5];

        public static void Init()
        {
            m_evilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero = CPP.PatternSearch("E8 ? ? ? ? C5 F8 10 BB ? ? ? ? C5 F8 10 B3");
            Marshal.Copy(m_evilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero, m_origBytesevilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero, 0, 5);
        }

        public static void OnStartBestGirl()
        {
            //Prevent Haruka from crashing the game when entering battles
            CPP.NopMemory(m_evilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero, 5);
        }

        public static void OnEndBestGirl()
        {
            CPP.PatchMemory(m_evilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero, m_origBytesevilFunctionCallThatCrashesBattlesIfYouAreHarukaAndTheGameHasntLoadedABattleThisSessionByDividingByZero);
        }
    }
}
