using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal sealed class SandboxedPluginProcess : IDisposable {
    private readonly SafeFileHandle _jobHandle;
    private readonly string _sessionDirectory;

    public SandboxedPluginProcess(
        Process process,
        StreamWriter standardInput,
        Stream standardOutput,
        Stream standardError,
        SafeFileHandle jobHandle,
        string sessionDirectory) {
        Process = process;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        StandardError = standardError;
        PackageDirectory = sessionDirectory;
        _jobHandle = jobHandle;
        _sessionDirectory = sessionDirectory;
    }

    public Process Process { get; }
    public StreamWriter StandardInput { get; }
    public Stream StandardOutput { get; }
    public Stream StandardError { get; }
    public string PackageDirectory { get; }

    public void Dispose() {
        StandardInput.Dispose();
        StandardOutput.Dispose();
        StandardError.Dispose();
        Process.Dispose();
        _jobHandle.Dispose();
        try {
            if (Directory.Exists(_sessionDirectory)) {
                Directory.Delete(_sessionDirectory, recursive: true);
            }
        } catch (Exception ex) {
            Services.Logger.Error($"Failed to remove plugin sandbox session '{_sessionDirectory}'", ex);
        }
    }
}

internal static partial class WindowsPluginSandbox {
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);
    private const int StartfUseStdHandles = 0x00000100;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private const uint HandleFlagInherit = 0x00000001;
    private static readonly IntPtr ProcThreadAttributeHandleList = (IntPtr)0x00020002;
    private static readonly IntPtr ProcThreadAttributeSecurityCapabilities = (IntPtr)0x00020009;

    public static SandboxedPluginProcess Start(
        string entryPath,
        IReadOnlyList<string> arguments,
        string packageDirectory,
        string pluginId,
        int memoryLimitMegabytes,
        string? interpreterPath = null) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("Process plugins require Windows AppContainer isolation.");
        }

        using var profile = AppContainerProfile.OpenOrCreate(pluginId);
        var sessionDirectory = CreateSessionPackage(profile.FolderPath, packageDirectory);
        try {
            var relativeEntry = Path.GetRelativePath(Path.GetFullPath(packageDirectory), Path.GetFullPath(entryPath));
            var sandboxEntryPath = Path.Combine(sessionDirectory, relativeEntry);
            var executablePath = interpreterPath is null
                ? sandboxEntryPath
                : PythonInterpreterLocator.StageRuntime(interpreterPath, sessionDirectory);
            IReadOnlyList<string> processArguments = interpreterPath is null
                ? arguments
                : ["-E", "-s", "-u", "-X", "utf8", sandboxEntryPath, .. arguments];
            return StartProcess(
                profile.Sid,
                executablePath,
                processArguments,
                sessionDirectory,
                memoryLimitMegabytes);
        } catch {
            if (Directory.Exists(sessionDirectory)) {
                Directory.Delete(sessionDirectory, recursive: true);
            }
            throw;
        }
    }

    private static string CreateSessionPackage(string profileFolder, string packageDirectory) {
        var sessionsDirectory = Path.Combine(profileFolder, "LocalState", "WosmPluginSessions");
        var sessionDirectory = Path.Combine(sessionsDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);
        foreach (var file in PluginPackageFiles.Enumerate(packageDirectory)) {
            var destination = Path.Combine(sessionDirectory, Path.GetRelativePath(packageDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
        return sessionDirectory;
    }

    private static SandboxedPluginProcess StartProcess(
        IntPtr appContainerSid,
        string entryPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int memoryLimitMegabytes) {
        IntPtr childStandardInput = IntPtr.Zero;
        IntPtr parentStandardInput = IntPtr.Zero;
        IntPtr parentStandardOutput = IntPtr.Zero;
        IntPtr childStandardOutput = IntPtr.Zero;
        IntPtr parentStandardError = IntPtr.Zero;
        IntPtr childStandardError = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr securityCapabilitiesPointer = IntPtr.Zero;
        IntPtr handleListPointer = IntPtr.Zero;
        IntPtr environmentPointer = IntPtr.Zero;
        var processInformation = new ProcessInformation();
        SafeFileHandle? jobHandle = null;

        try {
            CreateRedirectedPipe(out parentStandardInput, out childStandardInput, parentReads: false);
            CreateRedirectedPipe(out parentStandardOutput, out childStandardOutput, parentReads: true);
            CreateRedirectedPipe(out parentStandardError, out childStandardError, parentReads: true);

            nuint attributeListSize = 0;
            _ = NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref attributeListSize);
            attributeList = Marshal.AllocHGlobal(checked((nint)attributeListSize));
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 2, 0, ref attributeListSize)) {
                throw LastWin32Error("InitializeProcThreadAttributeList");
            }

            var securityCapabilities = new SecurityCapabilities {
                AppContainerSid = appContainerSid
            };
            securityCapabilitiesPointer = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
            Marshal.StructureToPtr(securityCapabilities, securityCapabilitiesPointer, false);
            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeSecurityCapabilities,
                    securityCapabilitiesPointer,
                    (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                    IntPtr.Zero,
                    IntPtr.Zero)) {
                throw LastWin32Error("UpdateProcThreadAttribute(SecurityCapabilities)");
            }

            handleListPointer = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handleListPointer, 0, childStandardInput);
            Marshal.WriteIntPtr(handleListPointer, IntPtr.Size, childStandardOutput);
            Marshal.WriteIntPtr(handleListPointer, IntPtr.Size * 2, childStandardError);
            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeHandleList,
                    handleListPointer,
                    (nuint)(IntPtr.Size * 3),
                    IntPtr.Zero,
                    IntPtr.Zero)) {
                throw LastWin32Error("UpdateProcThreadAttribute(HandleList)");
            }

            environmentPointer = CreateEnvironmentBlock(workingDirectory);
            var startupInfo = new StartupInfoEx {
                StartupInfo = new StartupInfo {
                    Size = Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles,
                    StandardInput = childStandardInput,
                    StandardOutput = childStandardOutput,
                    StandardError = childStandardError
                },
                AttributeList = attributeList
            };
            var commandLine = new StringBuilder(BuildCommandLine(entryPath, arguments));
            var creationFlags = CreateSuspended | CreateUnicodeEnvironment |
                ExtendedStartupInfoPresent | CreateNoWindow;
            if (!NativeMethods.CreateProcess(
                    entryPath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    creationFlags,
                    environmentPointer,
                    workingDirectory,
                    ref startupInfo,
                    out processInformation)) {
                throw LastWin32Error("CreateProcess(AppContainer)");
            }

            CloseHandle(ref childStandardInput);
            CloseHandle(ref childStandardOutput);
            CloseHandle(ref childStandardError);

            jobHandle = CreateJob(memoryLimitMegabytes);
            if (!NativeMethods.AssignProcessToJobObject(jobHandle, processInformation.Process)) {
                throw LastWin32Error("AssignProcessToJobObject");
            }

            var process = Process.GetProcessById(checked((int)processInformation.ProcessId));
            if (NativeMethods.ResumeThread(processInformation.Thread) == uint.MaxValue) {
                process.Dispose();
                throw LastWin32Error("ResumeThread");
            }

            var inputStream = new FileStream(
                new SafeFileHandle(parentStandardInput, ownsHandle: true),
                FileAccess.Write,
                4096,
                isAsync: false);
            parentStandardInput = IntPtr.Zero;
            var outputStream = new FileStream(
                new SafeFileHandle(parentStandardOutput, ownsHandle: true),
                FileAccess.Read,
                4096,
                isAsync: false);
            parentStandardOutput = IntPtr.Zero;
            var errorStream = new FileStream(
                new SafeFileHandle(parentStandardError, ownsHandle: true),
                FileAccess.Read,
                4096,
                isAsync: false);
            parentStandardError = IntPtr.Zero;
            var inputWriter = new StreamWriter(inputStream, new UTF8Encoding(false)) {
                AutoFlush = true
            };
            var result = new SandboxedPluginProcess(
                process,
                inputWriter,
                outputStream,
                errorStream,
                jobHandle,
                workingDirectory);
            jobHandle = null;
            return result;
        } catch {
            if (processInformation.Process != IntPtr.Zero) {
                _ = NativeMethods.TerminateProcess(processInformation.Process, 1);
            }
            throw;
        } finally {
            jobHandle?.Dispose();
            CloseHandle(ref childStandardInput);
            CloseHandle(ref parentStandardInput);
            CloseHandle(ref parentStandardOutput);
            CloseHandle(ref childStandardOutput);
            CloseHandle(ref parentStandardError);
            CloseHandle(ref childStandardError);
            CloseHandle(ref processInformation.Thread);
            CloseHandle(ref processInformation.Process);
            if (attributeList != IntPtr.Zero) {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (securityCapabilitiesPointer != IntPtr.Zero) {
                Marshal.FreeHGlobal(securityCapabilitiesPointer);
            }
            if (handleListPointer != IntPtr.Zero) {
                Marshal.FreeHGlobal(handleListPointer);
            }
            if (environmentPointer != IntPtr.Zero) {
                Marshal.FreeHGlobal(environmentPointer);
            }
        }
    }

    private static void CreateRedirectedPipe(
        out IntPtr parentHandle,
        out IntPtr childHandle,
        bool parentReads) {
        var securityAttributes = new SecurityAttributes {
            Size = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };
        if (!NativeMethods.CreatePipe(out var readHandle, out var writeHandle, ref securityAttributes, 0)) {
            throw LastWin32Error("CreatePipe");
        }

        parentHandle = parentReads ? readHandle : writeHandle;
        childHandle = parentReads ? writeHandle : readHandle;
        if (!NativeMethods.SetHandleInformation(parentHandle, HandleFlagInherit, 0)) {
            CloseHandle(ref parentHandle);
            CloseHandle(ref childHandle);
            throw LastWin32Error("SetHandleInformation");
        }
    }

    private static SafeFileHandle CreateJob(int memoryLimitMegabytes) {
        var jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (jobHandle.IsInvalid) {
            throw LastWin32Error("CreateJobObject");
        }

        try {
            var limits = new JobObjectExtendedLimitInformation {
                BasicLimitInformation = new JobObjectBasicLimitInformation {
                    LimitFlags = JobObjectLimitFlags.ActiveProcess |
                        JobObjectLimitFlags.ProcessMemory |
                        JobObjectLimitFlags.KillOnJobClose,
                    ActiveProcessLimit = 1
                },
                ProcessMemoryLimit = (nuint)checked((long)memoryLimitMegabytes * 1024 * 1024)
            };
            SetJobInformation(jobHandle, JobObjectInformationClass.ExtendedLimitInformation, limits);

            var uiRestrictions = new JobObjectBasicUiRestrictions {
                Restrictions = JobObjectUiLimitFlags.Handles |
                    JobObjectUiLimitFlags.ReadClipboard |
                    JobObjectUiLimitFlags.WriteClipboard |
                    JobObjectUiLimitFlags.SystemParameters |
                    JobObjectUiLimitFlags.DisplaySettings |
                    JobObjectUiLimitFlags.GlobalAtoms |
                    JobObjectUiLimitFlags.Desktop |
                    JobObjectUiLimitFlags.ExitWindows
            };
            SetJobInformation(
                jobHandle,
                JobObjectInformationClass.BasicUiRestrictions,
                uiRestrictions);
            return jobHandle;
        } catch {
            jobHandle.Dispose();
            throw;
        }
    }

    private static void SetJobInformation<T>(
        SafeFileHandle jobHandle,
        JobObjectInformationClass informationClass,
        T information) where T : struct {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try {
            Marshal.StructureToPtr(information, pointer, false);
            if (!NativeMethods.SetInformationJobObject(
                    jobHandle,
                    informationClass,
                    pointer,
                    (uint)Marshal.SizeOf<T>())) {
                throw LastWin32Error($"SetInformationJobObject({informationClass})");
            }
        } finally {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IntPtr CreateEnvironmentBlock(string workingDirectory) {
        var systemDirectory = Environment.SystemDirectory;
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var tempDirectory = Path.Combine(workingDirectory, "Temp");
        Directory.CreateDirectory(tempDirectory);
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["APPDATA"] = Path.Combine(workingDirectory, "AppData", "Roaming"),
            ["COMSPEC"] = Path.Combine(systemDirectory, "cmd.exe"),
            ["LOCALAPPDATA"] = Path.Combine(workingDirectory, "AppData", "Local"),
            ["PATH"] = $"{systemDirectory};{windowsDirectory}",
            ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
            ["PROCESSOR_ARCHITECTURE"] = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant(),
            ["SYSTEMROOT"] = windowsDirectory,
            ["TEMP"] = tempDirectory,
            ["TMP"] = tempDirectory,
            ["USERPROFILE"] = workingDirectory,
            ["WINDIR"] = windowsDirectory
        };
        var block = string.Join('\0', variables.Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
        var characters = block.ToCharArray();
        var pointer = Marshal.AllocHGlobal(characters.Length * sizeof(char));
        Marshal.Copy(characters, 0, pointer, characters.Length);
        return pointer;
    }

    private static string BuildCommandLine(string entryPath, IReadOnlyList<string> arguments) {
        return string.Join(' ', new[] { entryPath }.Concat(arguments).Select(QuoteCommandLineArgument));
    }

    private static string QuoteCommandLineArgument(string value) {
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"')) {
            return value;
        }

        var result = new StringBuilder(value.Length + 2).Append('"');
        var backslashCount = 0;
        foreach (var character in value) {
            if (character == '\\') {
                backslashCount++;
                continue;
            }
            if (character == '"') {
                result.Append('\\', backslashCount * 2 + 1).Append('"');
                backslashCount = 0;
                continue;
            }
            result.Append('\\', backslashCount).Append(character);
            backslashCount = 0;
        }
        result.Append('\\', backslashCount * 2).Append('"');
        return result.ToString();
    }

    private static void CloseHandle(ref IntPtr handle) {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
        _ = NativeMethods.CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    private static Win32Exception LastWin32Error(string operation) {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"{operation} failed: {new Win32Exception(error).Message}");
    }

    private sealed class AppContainerProfile : IDisposable {
        private AppContainerProfile(IntPtr sid, string folderPath) {
            Sid = sid;
            FolderPath = folderPath;
        }

        public IntPtr Sid { get; }
        public string FolderPath { get; }

        public static AppContainerProfile OpenOrCreate(string pluginId) {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pluginId))).ToLowerInvariant();
            var profileName = $"WosmPlugin-{hash[..32]}";
            var result = NativeMethods.CreateAppContainerProfile(
                profileName,
                $"WOSM plugin {pluginId}",
                "Isolated WPF OpenStreetmap Editor plugin",
                IntPtr.Zero,
                0,
                out var sid);
            if (result == ErrorAlreadyExistsHResult) {
                result = NativeMethods.DeriveAppContainerSidFromAppContainerName(profileName, out sid);
            }
            if (result < 0 || sid == IntPtr.Zero) {
                Marshal.ThrowExceptionForHR(result);
            }

            try {
                if (!NativeMethods.ConvertSidToStringSid(sid, out var sidStringPointer)) {
                    throw LastWin32Error("ConvertSidToStringSid");
                }
                string sidString;
                try {
                    sidString = Marshal.PtrToStringUni(sidStringPointer) ??
                        throw new InvalidOperationException("AppContainer SID conversion returned no value.");
                } finally {
                    _ = NativeMethods.LocalFree(sidStringPointer);
                }

                result = NativeMethods.GetAppContainerFolderPath(sidString, out var folderPathPointer);
                if (result < 0 || folderPathPointer == IntPtr.Zero) {
                    Marshal.ThrowExceptionForHR(result);
                }
                try {
                    var folderPath = Marshal.PtrToStringUni(folderPathPointer) ??
                        throw new InvalidOperationException("AppContainer folder lookup returned no value.");
                    return new AppContainerProfile(sid, folderPath);
                } finally {
                    Marshal.FreeCoTaskMem(folderPathPointer);
                }
            } catch {
                _ = NativeMethods.FreeSid(sid);
                throw;
            }
        }

        public void Dispose() {
            _ = NativeMethods.FreeSid(Sid);
        }
    }

    [Flags]
    private enum JobObjectLimitFlags : uint {
        ActiveProcess = 0x00000008,
        ProcessMemory = 0x00000100,
        KillOnJobClose = 0x00002000
    }

    [Flags]
    private enum JobObjectUiLimitFlags : uint {
        Handles = 0x00000001,
        ReadClipboard = 0x00000002,
        WriteClipboard = 0x00000004,
        SystemParameters = 0x00000008,
        DisplaySettings = 0x00000010,
        GlobalAtoms = 0x00000020,
        Desktop = 0x00000040,
        ExitWindows = 0x00000080
    }

    private enum JobObjectInformationClass {
        BasicUiRestrictions = 4,
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes {
        public int Size;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicUiRestrictions {
        public JobObjectUiLimitFlags Restrictions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JobObjectLimitFlags LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(
            out IntPtr readPipe,
            out IntPtr writePipe,
            ref SecurityAttributes pipeAttributes,
            uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(
            IntPtr attributeList,
            int attributeCount,
            int flags,
            ref nuint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            nuint size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            SafeFileHandle job,
            JobObjectInformationClass informationClass,
            IntPtr information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

        [DllImport("userenv.dll", EntryPoint = "CreateAppContainerProfile",
            CharSet = CharSet.Unicode)]
        public static extern int CreateAppContainerProfile(
            string appContainerName,
            string displayName,
            string description,
            IntPtr capabilities,
            uint capabilityCount,
            out IntPtr appContainerSid);

        [DllImport("userenv.dll", EntryPoint = "DeriveAppContainerSidFromAppContainerName",
            CharSet = CharSet.Unicode)]
        public static extern int DeriveAppContainerSidFromAppContainerName(
            string appContainerName,
            out IntPtr appContainerSid);

        [DllImport("userenv.dll", EntryPoint = "GetAppContainerFolderPath",
            CharSet = CharSet.Unicode)]
        public static extern int GetAppContainerFolderPath(string appContainerSid, out IntPtr path);

        [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

        [DllImport("advapi32.dll")]
        public static extern IntPtr FreeSid(IntPtr sid);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr memory);
    }
}
