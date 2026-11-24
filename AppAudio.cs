using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RdpPanel
{
    // mutes or unmutes everything this program plays in the Windows volume mixer:
    // the sound of the RDP sessions and the alert sounds
    static class AppAudio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out int processId);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // mute (true) or unmute (false) the audio sessions of this process
        public static void SetMuted(bool mute)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return;
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return;
                var manager = (IAudioSessionManager2)o;
                IAudioSessionEnumerator sessions;
                if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null) return;
                int count;
                if (sessions.GetCount(out count) != 0) return;
                int me = Process.GetCurrentProcess().Id;
                Guid context = Guid.Empty;
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 control;
                    if (sessions.GetSession(i, out control) != 0 || control == null) continue;
                    int pid;
                    if (control.GetProcessId(out pid) != 0 || pid != me) continue;
                    var volume = control as ISimpleAudioVolume;
                    if (volume != null) volume.SetMute(mute, ref context);
                }
            }
            catch { }
        }
    }
}
