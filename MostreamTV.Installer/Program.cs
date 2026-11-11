using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace MostreamTV.Installer;

internal static class Program
{
    private const string AppDisplayName = "Mostream TV";
    private const string AppExecutableName = "MostreamTV.Host.exe";
    private const string UninstallerFileName = "Uninstall Mostream TV.exe";
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MostreamTV";
    private const string PayloadResourceName = "MostreamTV.Installer.payload.zip";
    private const string EofCabinetMagic = "MSTRCAB1";
    private const string EofZipMagic = "MSTRZIP1";
    private const int EofFooterSize = 16;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var silent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase));
        var uninstall = args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));
        var noLaunch = args.Any(a => string.Equals(a, "--no-launch", StringComparison.OrdinalIgnoreCase));
        var noDesktop = args.Any(a => string.Equals(a, "--no-desktop", StringComparison.OrdinalIgnoreCase));
        var customDir = ParseFlagValue(args, "--install-dir");

        if (uninstall)
            return RunUninstall(silent);

        var defaultDir = string.IsNullOrWhiteSpace(customDir) ? DefaultInstallDirectory() : Path.GetFullPath(customDir);
        if (silent)
            return RunSilentInstall(defaultDir, createDesktopShortcut: !noDesktop, launchApp: !noLaunch);

        using var form = new SetupForm(defaultDir, !noDesktop, !noLaunch);
        Application.Run(form);
        return form.InstallSucceeded || !form.InstallAttempted ? 0 : 1;
    }

    internal static string DefaultInstallDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        AppDisplayName);

    internal static string AppVersionText
    {
        get
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (info is not null && !string.IsNullOrWhiteSpace(info))
                return info.Split('+')[0];
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";
        }
    }

    internal static bool HasPayload()
    {
        if (Assembly.GetExecutingAssembly().GetManifestResourceInfo(PayloadResourceName) is not null)
            return true;

        var selfPath = GetSelfExecutablePath();
        return !string.IsNullOrEmpty(selfPath) && TryGetEofPayload(selfPath!, out _, out _, out _);
    }

    internal static async Task InstallAsync(
        string targetDirectory,
        bool createDesktopShortcut,
        IProgress<(int Percent, string Status)> progress,
        CancellationToken cancellationToken = default)
    {
        if (!HasPayload())
            throw new InvalidOperationException("Installer payload was not embedded in this build.");

        targetDirectory = Path.GetFullPath(targetDirectory.Trim());
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new ArgumentException("Choose a valid installation folder.", nameof(targetDirectory));

        await Task.Run(() =>
        {
            progress.Report((5, "Preparing installation folder..."));
            Directory.CreateDirectory(targetDirectory);

            EnsureHostProcessClosed(targetDirectory);

            progress.Report((15, "Extracting Mostream TV files..."));
            ExtractPayloadToDirectory(targetDirectory, progress, cancellationToken);

            var mainExe = Path.Combine(targetDirectory, AppExecutableName);
            if (!File.Exists(mainExe))
                throw new FileNotFoundException("Installation package did not produce MostreamTV.Host.exe.", mainExe);

            progress.Report((84, "Preparing watch-party runtime..."));
            PreExtractSyncplayRuntime(targetDirectory);

            progress.Report((90, "Installing shortcuts and uninstaller..."));
            var uninstallerPath = Path.Combine(targetDirectory, UninstallerFileName);
            var selfPath = GetSelfExecutablePath();
            if (!string.IsNullOrEmpty(selfPath) && File.Exists(selfPath)
                && !string.Equals(Path.GetFullPath(selfPath), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
            {
                WriteUninstallerStub(selfPath!, uninstallerPath);
            }

            CreateShortcut(StartMenuShortcutPath(), mainExe, targetDirectory, "Host or join private watch parties");
            if (createDesktopShortcut)
                CreateShortcut(DesktopShortcutPath(), mainExe, targetDirectory, "Host or join private watch parties");

            RegisterUninstaller(targetDirectory, mainExe, uninstallerPath);
            progress.Report((100, "Mostream TV is ready!"));
        }, cancellationToken);
    }

    private static void ExtractPayloadToDirectory(
        string targetDirectory,
        IProgress<(int Percent, string Status)> progress,
        CancellationToken cancellationToken)
    {
        var selfPath = GetSelfExecutablePath();
        if (!string.IsNullOrEmpty(selfPath) && TryGetEofPayload(selfPath!, out var offset, out var length, out var magic))
        {
            if (string.Equals(magic, EofCabinetMagic, StringComparison.Ordinal))
            {
                var tempZipPath = Path.Combine(Path.GetTempPath(), $"mostream-payload-{Guid.NewGuid():N}.zip");
                try
                {
                    ExtractCabinetPayload(selfPath!, offset, length, tempZipPath);
                    using var archive = ZipFile.OpenRead(tempZipPath);
                    ExtractZipWithProgress(archive, targetDirectory, progress, cancellationToken);
                }
                finally
                {
                    TryDeleteFile(tempZipPath);
                }
                return;
            }

            using var exeStream = File.OpenRead(selfPath!);
            using var boundedStream = new SubStream(exeStream, offset, length);
            using var zipArchive = new ZipArchive(boundedStream, ZipArchiveMode.Read);
            ExtractZipWithProgress(zipArchive, targetDirectory, progress, cancellationToken);
            return;
        }

        using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Could not read embedded installer payload.");
        using var resourceArchive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
        ExtractZipWithProgress(resourceArchive, targetDirectory, progress, cancellationToken);
    }

    private static void ExtractZipWithProgress(
        ZipArchive archive,
        string targetDirectory,
        IProgress<(int Percent, string Status)>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var total = Math.Max(1, archive.Entries.Count);
        var completed = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destination, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Blocked unsafe archive entry: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
            }
            else
            {
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                entry.ExtractToFile(destination, overwrite: true);
            }

            completed++;
            if (progress is not null && (completed == total || completed % 15 == 0))
            {
                var pct = 15 + (int)(completed * 65.0 / total);
                progress.Report((pct, "Extracting Mostream TV files..."));
            }
        }
    }

    private static string? GetSelfExecutablePath()
    {
        var location = typeof(Program).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            return location;

        try
        {
            using var current = Process.GetCurrentProcess();
            return current.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetEofPayload(string exePath, out long payloadOffset, out long payloadLength, out string magic)
    {
        payloadOffset = 0;
        payloadLength = 0;
        magic = string.Empty;

        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            using var fs = File.OpenRead(exePath);
            if (fs.Length < EofFooterSize)
                return false;

            fs.Seek(-EofFooterSize, SeekOrigin.End);
            var footer = new byte[EofFooterSize];
            if (fs.Read(footer, 0, EofFooterSize) != EofFooterSize)
                return false;

            var candidateMagic = Encoding.ASCII.GetString(footer, 8, 8);
            if (!string.Equals(candidateMagic, EofCabinetMagic, StringComparison.Ordinal)
                && !string.Equals(candidateMagic, EofZipMagic, StringComparison.Ordinal))
            {
                return false;
            }

            var candidateLength = BitConverter.ToInt64(footer, 0);
            if (candidateLength <= 0 || candidateLength > fs.Length - EofFooterSize)
                return false;

            payloadLength = candidateLength;
            payloadOffset = fs.Length - EofFooterSize - candidateLength;
            magic = candidateMagic;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteUninstallerStub(string selfPath, string uninstallerPath)
    {
        if (TryGetEofPayload(selfPath, out var payloadOffset, out _, out _))
        {
            using var source = File.OpenRead(selfPath);
            using var target = File.Create(uninstallerPath);
            CopyExactBytes(source, target, payloadOffset);
            return;
        }

        File.Copy(selfPath, uninstallerPath, overwrite: true);
    }

    private static void ExtractCabinetPayload(string selfPath, long payloadOffset, long payloadLength, string destinationZipPath)
    {
        var tempCabPath = Path.Combine(Path.GetTempPath(), $"mostream-cab-{Guid.NewGuid():N}.cab");
        try
        {
            using (var source = File.OpenRead(selfPath))
            using (var cabOut = File.Create(tempCabPath))
            {
                source.Seek(payloadOffset, SeekOrigin.Begin);
                CopyExactBytes(source, cabOut, payloadLength);
            }

            var extracted = false;
            PspFileCallbackW callback = (_, notification, param1, _) =>
            {
                if (notification == SpFileNotifyFileInCabinet)
                {
                    var info = Marshal.PtrToStructure<FileInCabinetInfoW>(param1);
                    var entryName = Marshal.PtrToStringUni(info.NameInCabinet) ?? string.Empty;
                    if (string.Equals(entryName, "payload", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(entryName, "payload.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        info.FullTargetName = destinationZipPath;
                        Marshal.StructureToPtr(info, param1, false);
                        return FileOpDoIt;
                    }
                    return FileOpSkip;
                }

                if (notification == SpFileNotifyFileExtracted)
                    extracted = true;

                return 0;
            };

            var ok = SetupIterateCabinetW(tempCabPath, 0, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            if (!ok || !extracted || !File.Exists(destinationZipPath))
                throw new IOException($"Could not decompress installer payload (Win32 error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            TryDeleteFile(tempCabPath);
        }
    }

    private static void CopyExactBytes(Stream source, Stream destination, long byteCount)
    {
        var buffer = new byte[81920];
        var remaining = byteCount;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, toRead);
            if (read <= 0)
                throw new EndOfStreamException("Unexpected end of installer payload.");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    internal static void LaunchInstalledApp(string targetDirectory)
    {
        var mainExe = Path.Combine(targetDirectory, AppExecutableName);
        if (!File.Exists(mainExe))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = mainExe,
            WorkingDirectory = targetDirectory,
            UseShellExecute = true,
        });
    }

    private static int RunSilentInstall(string targetDirectory, bool createDesktopShortcut, bool launchApp)
    {
        try
        {
            var progress = new Progress<(int Percent, string Status)>();
            InstallAsync(targetDirectory, createDesktopShortcut, progress).GetAwaiter().GetResult();
            if (launchApp)
                LaunchInstalledApp(targetDirectory);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Setup failed: " + ex.Message);
            return 1;
        }
    }

    private static int RunUninstall(bool silent)
    {
        string? installDir = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey);
            installDir = key?.GetValue("InstallLocation") as string;
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(installDir))
        {
            var selfDir = Path.GetDirectoryName(GetSelfExecutablePath());
            if (!string.IsNullOrWhiteSpace(selfDir) && File.Exists(Path.Combine(selfDir, AppExecutableName)))
                installDir = selfDir;
        }

        if (!silent)
        {
            var confirm = MessageBox.Show(
                "Remove Mostream TV and its shortcuts from this computer?",
                "Uninstall Mostream TV",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return 0;
        }

        try
        {
            if (installDir is not null && !string.IsNullOrWhiteSpace(installDir))
                EnsureHostProcessClosed(installDir);

            TryDeleteFile(StartMenuShortcutPath());
            TryDeleteFile(DesktopShortcutPath());
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, throwOnMissingSubKey: false);

            if (installDir is not null && !string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
            {
                var selfPath = GetSelfExecutablePath() ?? string.Empty;
                var insideInstallDir = !string.IsNullOrEmpty(selfPath)
                    && Path.GetFullPath(selfPath).StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase);

                if (insideInstallDir)
                {
                    ScheduleDirectoryRemoval(installDir);
                }
                else
                {
                    Directory.Delete(installDir, recursive: true);
                }
            }

            if (!silent)
            {
                MessageBox.Show(
                    "Mostream TV has been uninstalled.",
                    "Uninstall Mostream TV",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(ex.Message, "Uninstall Mostream TV", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void PreExtractSyncplayRuntime(string installDir)
    {
        try
        {
            var archivePath = Path.Combine(installDir, "runtime", "Syncplay_1.7.6_Portable.zip");
            if (!File.Exists(archivePath))
                return;

            var runtimeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MoStreamTV",
                "Runtime",
                "Syncplay-1.7.6");
            var serverExe = Path.Combine(runtimeDir, "syncplayServer.exe");
            var clientExe = Path.Combine(runtimeDir, "SyncplayConsole.exe");
            if (File.Exists(serverExe) && File.Exists(clientExe))
                return;

            Directory.CreateDirectory(runtimeDir);
            using var archive = ZipFile.OpenRead(archivePath);
            ExtractZipWithProgress(archive, runtimeDir, null, CancellationToken.None);
        }
        catch
        {
            // Non-fatal: MostreamTV.Host verifies and extracts runtime on first session if needed.
        }
    }

    private static void EnsureHostProcessClosed(string installDir)
    {
        var fullTarget = Path.GetFullPath(installDir);
        foreach (var procName in new[] { "MostreamTV.Host", "MoStreamTV.Host" })
        {
            foreach (var proc in Process.GetProcessesByName(procName))
            {
                try
                {
                    var procPath = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(procPath)
                        && Path.GetFullPath(procPath).StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Mostream TV is currently running from the destination folder. Close it first and try again.");
                    }
                }
                catch (IOException)
                {
                    throw;
                }
                catch
                {
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
    }

    private static void RegisterUninstaller(string installDir, string mainExe, string uninstallerPath)
    {
        long totalBytes = 0;
        foreach (var file in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories))
        {
            try { totalBytes += new FileInfo(file).Length; } catch { }
        }

        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey);
        key.SetValue("DisplayName", AppDisplayName);
        key.SetValue("DisplayVersion", AppVersionText);
        key.SetValue("Publisher", AppDisplayName);
        key.SetValue("DisplayIcon", mainExe);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
        key.SetValue("EstimatedSize", Math.Max(1, (int)(totalBytes / 1024)), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string StartMenuShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        $"{AppDisplayName}.lnk");

    private static string DesktopShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"{AppDisplayName}.lnk");

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))
            ?? throw new InvalidOperationException("Windows ShellLink COM class is unavailable.");
        var link = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("Could not create Windows ShellLink instance."));
        link.SetPath(targetPath);
        link.SetWorkingDirectory(workingDir);
        link.SetDescription(description);
        link.SetIconLocation(targetPath, 0);
        ((IPersistFile)link).Save(shortcutPath, false);
    }

    private static void ScheduleDirectoryRemoval(string directory)
    {
        var escaped = directory.Replace("\"", "\"\"");
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{escaped}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string? ParseFlagValue(string[] args, string flagName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flagName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private const uint SpFileNotifyFileInCabinet = 0x00000011;
    private const uint SpFileNotifyFileExtracted = 0x00000013;
    private const uint FileOpDoIt = 1;
    private const uint FileOpSkip = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInCabinetInfoW
    {
        public IntPtr NameInCabinet;
        public uint FileSize;
        public uint Win32Error;
        public ushort DosDate;
        public ushort DosTime;
        public ushort DosAttribs;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FullTargetName;
    }

    private delegate uint PspFileCallbackW(IntPtr context, uint notification, IntPtr param1, IntPtr param2);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupIterateCabinetW(string cabinetFile, uint reserved, PspFileCallbackW msgHandler, IntPtr context);

    private sealed class SubStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _offset;
        private readonly long _length;
        private long _position;

        public SubStream(Stream inner, long offset, long length)
        {
            _inner = inner;
            _offset = offset;
            _length = length;
            _inner.Seek(offset, SeekOrigin.Begin);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _length - _position;
            if (remaining <= 0)
                return 0;

            _inner.Seek(_offset + _position, SeekOrigin.Begin);
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var next = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (next < 0 || next > _length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _position = next;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox _installDirBox;
    private readonly Button _browseButton;
    private readonly CheckBox _desktopShortcutCheck;
    private readonly CheckBox _launchAfterCheck;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly Button _cancelButton;
    private readonly bool _isDark;

    public bool InstallAttempted { get; private set; }
    public bool InstallSucceeded { get; private set; }

    public SetupForm(string defaultInstallDir, bool createDesktop, bool launchAfter)
    {
        _isDark = IsDarkPreferred();

        Text = $"Mostream TV {Program.AppVersionText} Setup";
        Icon = LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 10F);

        var scale = DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
        var scaledW = Math.Max(600, (int)Math.Round(600 * scale));
        var scaledH = Math.Max(345, (int)Math.Round(345 * scale));
        ClientSize = new Size(scaledW, scaledH);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(scaledW, scaledH);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var r = 0; r < 6; r++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var logo = new PictureBox
        {
            Image = LoadLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(Math.Max(36, (int)Math.Round(36 * scale)), Math.Max(36, (int)Math.Round(36 * scale))),
            Margin = new Padding(0, 2, 12, 0),
        };
        header.Controls.Add(logo, 0, 0);
        header.SetRowSpan(logo, 2);

        header.Controls.Add(new Label
        {
            Text = $"Install Mostream TV ({Program.AppVersionText})",
            UseMnemonic = false,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F),
        }, 1, 0);
        header.Controls.Add(new Label
        {
            Text = "Ready to use immediately — bundles the watch-party runtime and .NET runtime.",
            UseMnemonic = false,
            AutoSize = true,
            Tag = "muted",
            Margin = new Padding(0, 2, 0, 0),
        }, 1, 1);

        var dirRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 8) };
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dirRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dirRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        dirRow.Controls.Add(new Label { Text = "Install folder", UseMnemonic = false, AutoSize = true, Margin = new Padding(0, 0, 0, 4) }, 0, 0);
        dirRow.SetColumnSpan(dirRow.GetControlFromPosition(0, 0)!, 2);

        _installDirBox = new TextBox { Text = defaultInstallDir, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 2, 8, 2) };
        var buttonH = Math.Max(32, TextRenderer.MeasureText("Ag", Font).Height + 12);
        var browseW = Math.Max(96, TextRenderer.MeasureText("Browse...", Font).Width + 28);
        _browseButton = new Button { Text = "Browse...", Size = new Size(browseW, buttonH), Margin = new Padding(0, 2, 0, 2) };
        _browseButton.Click += (_, _) => BrowseFolder();
        dirRow.Controls.Add(_installDirBox, 0, 1);
        dirRow.Controls.Add(_browseButton, 1, 1);

        _desktopShortcutCheck = new CheckBox { Text = "Create Desktop shortcut", UseMnemonic = false, AutoSize = true, Checked = createDesktop, Margin = new Padding(0, 4, 0, 2) };
        _launchAfterCheck = new CheckBox { Text = "Launch Mostream TV after installation", UseMnemonic = false, AutoSize = true, Checked = launchAfter, Margin = new Padding(0, 2, 0, 8) };

        var progressPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 8, 0, 14),
        };
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _statusLabel = new Label { Text = "Click Install to set up Mostream TV.", UseMnemonic = false, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        _progressBar = new ProgressBar { Dock = DockStyle.Top, Height = Math.Max(18, (int)Math.Round(18 * scale)), Minimum = 0, Maximum = 100 };
        progressPanel.Controls.Add(_statusLabel, 0, 0);
        progressPanel.Controls.Add(_progressBar, 0, 1);

        var actionW = Math.Max(104, TextRenderer.MeasureText("Install", Font).Width + 32);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
        _cancelButton = new Button { Text = "Cancel", Size = new Size(actionW, buttonH) };
        _installButton = new Button { Text = "Install", Size = new Size(actionW, buttonH), Margin = new Padding(0, 0, 8, 0) };
        _cancelButton.Click += (_, _) => Close();
        _installButton.Click += async (_, _) => await StartInstallAsync();
        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_installButton);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(dirRow, 0, 1);
        root.Controls.Add(_desktopShortcutCheck, 0, 2);
        root.Controls.Add(_launchAfterCheck, 0, 3);
        root.Controls.Add(progressPanel, 0, 4);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);

        AcceptButton = _installButton;
        CancelButton = _cancelButton;
        Shown += (_, _) => _installButton.Focus();
        ApplyColors();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_isDark)
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where to install Mostream TV",
            SelectedPath = _installDirBox.Text,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            _installDirBox.Text = dialog.SelectedPath;
    }

    private async Task StartInstallAsync()
    {
        InstallAttempted = true;
        _installButton.Enabled = false;
        _browseButton.Enabled = false;
        _installDirBox.ReadOnly = true;
        _desktopShortcutCheck.Enabled = false;
        _cancelButton.Enabled = false;

        var targetDir = _installDirBox.Text.Trim();
        var progress = new Progress<(int Percent, string Status)>(update =>
        {
            _progressBar.Value = Math.Max(0, Math.Min(100, update.Percent));
            _statusLabel.Text = update.Status;
        });

        try
        {
            await Program.InstallAsync(targetDir, _desktopShortcutCheck.Checked, progress);
            InstallSucceeded = true;
            if (_launchAfterCheck.Checked)
                Program.LaunchInstalledApp(targetDir);
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Installation failed.";
            MessageBox.Show(this, ex.Message, "Mostream TV Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _installButton.Enabled = true;
            _browseButton.Enabled = true;
            _installDirBox.ReadOnly = false;
            _desktopShortcutCheck.Enabled = true;
            _cancelButton.Enabled = true;
        }
    }

    private void ApplyColors()
    {
        if (!_isDark)
            return;

        var win = Color.FromArgb(24, 26, 30);
        var input = Color.FromArgb(18, 20, 24);
        var btnBack = Color.FromArgb(46, 51, 60);
        var btnHover = Color.FromArgb(62, 68, 80);
        var border = Color.FromArgb(64, 70, 82);
        var text = Color.FromArgb(236, 240, 245);
        var muted = Color.FromArgb(152, 160, 174);

        BackColor = win;
        ForeColor = text;
        ApplyTreeColors(this, input, btnBack, btnHover, border, text, muted);
    }

    private static void ApplyTreeColors(Control parent, Color input, Color btnBack, Color btnHover, Color border, Color text, Color muted)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case TextBox tb:
                    tb.BackColor = input;
                    tb.ForeColor = text;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    b.BackColor = btnBack;
                    b.ForeColor = text;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.BorderColor = border;
                    b.FlatAppearance.MouseOverBackColor = btnHover;
                    break;
                case CheckBox cb:
                    cb.BackColor = Color.Transparent;
                    cb.ForeColor = text;
                    break;
                case Label lbl:
                    lbl.ForeColor = Equals(lbl.Tag, "muted") ? muted : text;
                    break;
            }
            if (c.HasChildren)
                ApplyTreeColors(c, input, btnBack, btnHover, border, text, muted);
        }
    }

    private static bool IsDarkPreferred()
    {
        try
        {
            var themePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MoStreamTV",
                "theme.txt");
            if (File.Exists(themePath))
                return string.Equals(File.ReadAllText(themePath).Trim(), "dark", StringComparison.OrdinalIgnoreCase);

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int light)
                return light == 0;
        }
        catch
        {
        }
        return false;
    }

    private static Icon? LoadIcon()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MostreamTV.Installer.mostream.ico");
            return s is null ? null : new Icon(s);
        }
        catch
        {
            return null;
        }
    }

    private static Image? LoadLogo()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MostreamTV.Installer.mostream-32.png");
            if (s is null)
                return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
