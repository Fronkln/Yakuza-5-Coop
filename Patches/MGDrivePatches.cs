using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Y5Lib.Unsafe;

namespace Y5Coop
{
    internal static class MGDrivePatches
    {
        private delegate ulong CDriveVehicleBaseConstructor(IntPtr a1, IntPtr a2, int a3, IntPtr a4, IntPtr a5);


        public static void Init()
        {
            m_origVehicConstructor = Mod.engine.CreateHook<CDriveVehicleBaseConstructor>(CPP.ReadCall(CPP.ReadCall(CPP.PatternSearch("E8 ? ? ? ? 90 48 8D 05 ? ? ? ? 48 89 03 48 8D 8B ? ? ? ? 48 8B D3 E8 ? ? ? ? 90 33 FF"))), CDriveVehicleBase_Constructor);

            Mod.engine.EnableHook(m_origVehicConstructor);
        }

        private static CDriveVehicleBaseConstructor m_origVehicConstructor;
        unsafe static ulong CDriveVehicleBase_Constructor(IntPtr a1, IntPtr a2, int a3, IntPtr a4, IntPtr a5)
        {
            IntPtr kiryuOffset = IntPtr.Zero;
            IntPtr driver1StartOffset = IntPtr.Zero;

            string driverName = Marshal.PtrToStringAnsi(a2 + 0x14C);
            string driverName2 = Marshal.PtrToStringAnsi(a2 + 0xFC);

            if (!driverName.Contains("kiryu") && !driverName2.Contains("kiryu"))
                return m_origVehicConstructor(a1, a2, a3, a4, a5);


            if (driverName.Contains("kiryu"))
            {
                kiryuOffset = a2 + 0x14c;
            }

            if (driverName2.Contains("kiryu"))
            {
                kiryuOffset = a2 + 0xFC;
            }

            if (kiryuOffset != IntPtr.Zero)
            {
                //Find empty passengers for the car. If the first seat is empty, put him there
                //Otherwise, try the second seat. And if that is full too Ichiban can get lost
                if (Marshal.ReadByte(kiryuOffset + 72) == 0)
                    driver1StartOffset = kiryuOffset + 72;
                else if (Marshal.ReadByte(kiryuOffset + 144) == 0)
                    driver1StartOffset = kiryuOffset + 144;
            }

            if (driver1StartOffset != IntPtr.Zero)
            {
                int* flags = (int*)(driver1StartOffset + 64);

                Marshal.Copy(new byte[32], 0, driver1StartOffset, 32);

                string model = Mod.GetModelForPlayer2();
                byte[] modelBytes = Encoding.ASCII.GetBytes(model);
                Marshal.Copy(modelBytes, 0, driver1StartOffset, model.Length);

                *flags = 5929;
            }

            return m_origVehicConstructor(a1, a2, a3, a4, a5);
        }
    }
}
