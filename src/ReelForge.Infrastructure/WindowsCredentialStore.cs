using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private readonly string _targetPrefix;
    private readonly string? _legacyTargetPrefix;

    public WindowsCredentialStore(string targetPrefix = "ReelForge", string? legacyTargetPrefix = "SeedanceWizard")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPrefix);
        _targetPrefix = targetPrefix;
        _legacyTargetPrefix = legacyTargetPrefix;
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        var secretBytes = Encoding.Unicode.GetBytes(value);
        var secretPointer = Marshal.AllocCoTaskMem(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, secretPointer, secretBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = GetTargetName(key),
                CredentialBlobSize = secretBytes.Length,
                CredentialBlob = secretPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not store the secret.");
            }
        }
        finally
        {
            if (secretBytes.Length > 0)
            {
                Marshal.Copy(new byte[secretBytes.Length], 0, secretPointer, secretBytes.Length);
            }

            Array.Clear(secretBytes);
            Marshal.FreeCoTaskMem(secretPointer);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);

        var value = ReadCredential(GetTargetName(_targetPrefix, key));
        if (value.Found) return Task.FromResult(value.Value);

        if (!string.IsNullOrWhiteSpace(_legacyTargetPrefix))
        {
            value = ReadCredential(GetTargetName(_legacyTargetPrefix, key));
            if (value.Found) return Task.FromResult(value.Value);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);

        if (CredentialExists(GetTargetName(_targetPrefix, key))) return Task.FromResult(true);
        return Task.FromResult(
            !string.IsNullOrWhiteSpace(_legacyTargetPrefix) &&
            CredentialExists(GetTargetName(_legacyTargetPrefix, key)));
    }

    private static bool CredentialExists(string targetName)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return false;
            throw new Win32Exception(error, "Windows Credential Manager could not inspect the secret.");
        }

        CredFree(credentialPointer);
        return true;
    }

    private static (bool Found, string? Value) ReadCredential(string targetName)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return (false, null);
            throw new Win32Exception(error, "Windows Credential Manager could not read the secret.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return (true, string.Empty);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return (true, Encoding.Unicode.GetString(bytes));
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);

        if (!CredDelete(GetTargetName(key), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Windows Credential Manager could not delete the secret.");
            }
        }

        if (!string.IsNullOrWhiteSpace(_legacyTargetPrefix) &&
            !CredDelete(GetTargetName(_legacyTargetPrefix, key), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error, "Windows Credential Manager could not delete the legacy secret.");
        }

        return Task.CompletedTask;
    }

    private string GetTargetName(string key) => GetTargetName(_targetPrefix, key);

    private static string GetTargetName(string targetPrefix, string key) => $"{targetPrefix}:{key}";

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("Credential keys contain invalid characters.", nameof(key));
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree([In] IntPtr credentialPointer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
