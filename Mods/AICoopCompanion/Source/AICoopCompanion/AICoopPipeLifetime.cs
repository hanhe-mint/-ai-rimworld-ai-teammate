using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace AICoopCompanion
{
    internal static class AICoopPipeLifetime
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafePipeHandle handle, IntPtr overlapped);

        internal static void CloseWithoutWaiting(NamedPipeServerStream pipe)
        {
            if (pipe == null) return;
            // Closing a stream with pending I/O must never hold up Unity's main thread.
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    CancelIoEx(pipe.SafePipeHandle, IntPtr.Zero);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { pipe.Dispose(); }
                catch { }
            });
        }
    }
}
