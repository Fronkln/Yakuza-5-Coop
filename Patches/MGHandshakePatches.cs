using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    internal static class MGHandshakePatches
    {
        public static void Init()
        {
            //Replace handshake guard with Ichiban
            IntPtr handshakeGuardModelName = CPP.ResolveRelativeAddress(CPP.PatternSearch("8D 4A 06 E8 ? ? ? ? E8 ? ? ? ? B9 50 17 00 00 E8") + 0x52, 7);
            CPP.PatchMemory(handshakeGuardModelName, System.Text.Encoding.ASCII.GetBytes("c_am_ichiban_tx_on"));
        }
    }
}
