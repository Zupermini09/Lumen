using System.Runtime.InteropServices;
using System.Text;
using Lumen.Core;

namespace Lumen.Platform.Secrets;

/// <summary>Generic passwords in the user's login keychain (service "Lumen", account = key).</summary>
internal sealed class MacKeychainSecretStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecItemNotFound = -25300;
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("Lumen");

    public string? Get(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out var length, out var data, out var item);
        if (status == ErrSecItemNotFound)
            return null;
        Check(status, "read");

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    public void Set(string key, string secret)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var password = Encoding.UTF8.GetBytes(secret);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, IntPtr.Zero, out var item);

        if (status == ErrSecItemNotFound)
        {
            Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                (uint)password.Length, password, IntPtr.Zero), "save");
            return;
        }

        Check(status, "read");
        try
        {
            Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password), "update");
        }
        finally
        {
            CFRelease(item);
        }
    }

    public void Remove(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, IntPtr.Zero, out var item);
        if (status == ErrSecItemNotFound)
            return;
        Check(status, "read");
        try
        {
            Check(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string action)
    {
        if (status != 0)
            throw new InvalidOperationException($"Keychain {action} failed (OSStatus {status}).");
    }

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    // Overload used when only the item reference is needed (passwordLength/passwordData = NULL).
    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength, IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
