using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TrueStorageCheck_GUI
{
    public static class DiskTest
    {
        // Define an event for reporting the state and progress
        public delegate void ProgressDelegate(IntPtr instance, int state, int progress, int mbWritten);

        private const string DLL_STR = MainWindow.DLL_STR;

        // Note: Seems to be non-blittable type and can't be used as a return value 

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr DiskTest_Create(char driveLetter, ulong capacityToTest, bool stopOnFirstError, bool deleteTempFiles, bool writeLogFile, ProgressDelegate callback);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte DiskTest_PerformTest(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern int DiskTest_GetTestState(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern int DiskTest_GetTestProgress(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static public extern byte DiskTest_ForceStopTest(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte DiskTest_Destroy(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong DiskTest_GetLastSuccessfulVerifyPosition(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern double DiskTest_GetAverageWriteSpeed(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern double DiskTest_GetAverageReadSpeed(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern long DiskTest_GetTimeRemaining(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte DiskTest_IsDiskEmpty(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte DiskTest_DeleteTestFiles(IntPtr diskTestInstance);
    }
}
