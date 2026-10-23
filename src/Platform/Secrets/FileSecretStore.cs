using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Lumen.Core;

namespace Lumen.Platform.Secrets;

/// <summary>
/// One file per secret. On Windows the content is encrypted with DPAPI (only this Windows user can decrypt it).
/// Elsewhere it is the fallback store: the folder and files are readable by the current user only (0700/0600).
/// </summary>
internal sealed class FileSecretStore(string directory, bool protect) : ISecretStore
{
    public string? Get(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (protect && OperatingSystem.IsWindows())
            bytes = Dpapi.Unprotect(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Set(string key, string secret)
    {
        EnsureDirectory();
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (protect && OperatingSystem.IsWindows())
            bytes = Dpapi.Protect(bytes);

        var path = PathFor(key);
        var temp = path + ".tmp";
        using (var stream = CreatePrivateFile(temp))
            stream.Write(bytes);
        File.Move(temp, path, overwrite: true);
    }

    public void Remove(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    // Hash the key so it is always a valid, fixed-length file name.
    private string PathFor(string key) =>
        Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".secret");

    private void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static class Dpapi
    {
        public static byte[] Protect(byte[] data) => Transform(data, protect: true);
        public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new DataBlob();
            var output = new DataBlob();
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                input.cbData = data.Length;
                input.pbData = handle.AddrOfPinnedObject();
                const int CryptProtectUiForbidden = 0x1;
                var ok = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
                if (!ok)
                    throw new CryptographicException(Marshal.GetLastWin32Error());

                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                handle.Free();
                if (output.pbData != IntPtr.Zero)
                    LocalFree(output.pbData);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
