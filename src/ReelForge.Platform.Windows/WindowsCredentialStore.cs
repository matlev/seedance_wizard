using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ReelForge.Application;

namespace ReelForge.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private readonly string _targetPrefix;

    public WindowsCredentialStore(string targetPrefix = "ReelForge")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPrefix);
        _targetPrefix = targetPrefix;
    }

    public string DisplayName => "Windows Credential Manager";

    public string GetDisplayKey(string key)
    {
        ValidateKey(key);
        return GetTargetName(key);
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
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"{DisplayName} could not store the secret.");
        }
        finally
        {
            if (secretBytes.Length > 0)
                Marshal.Copy(new byte[secretBytes.Length], 0, secretPointer, secretBytes.Length);
            Array.Clear(secretBytes);
            Marshal.FreeCoTaskMem(secretPointer);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        var value = ReadCredential(GetTargetName(key));
        return Task.FromResult(value.Found ? value.Value : null);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        return Task.FromResult(CredentialExists(GetTargetName(key)));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        if (!CredDelete(GetTargetName(key), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error, $"{DisplayName} could not delete the secret.");
        }
        return Task.CompletedTask;
    }

    private bool CredentialExists(string targetName)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return false;
            throw new Win32Exception(error, $"{DisplayName} could not inspect the secret.");
        }
        CredFree(credentialPointer);
        return true;
    }

    private (bool Found, string? Value) ReadCredential(string targetName)
    {
        if (!CredRead(targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return (false, null);
            throw new Win32Exception(error, $"{DisplayName} could not read the secret.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return (true, string.Empty);

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

    private string GetTargetName(string key) => $"{_targetPrefix}:{key}";

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Credential keys contain invalid characters.", nameof(key));
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
