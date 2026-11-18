using System.Runtime.InteropServices;
using System.Text;
using ExpoReturn.Application;
using Microsoft.Win32;

namespace ExpoReturn.Desktop;

/// <summary>
/// Name of the person as Windows shows it (start menu / account picture), e.g. "EXTERNAL Reyna Morisco Luis David
/// (PM, ME/QMM-L2-JuP1)" → "Reyna Morisco Luis David". Never the Windows user: when the name cannot be read, null.
/// </summary>
public static class WindowsName
{
    private const int NameDisplay = 3;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder userName, ref uint size);

    public static string? Current()
    {
        var account = Environment.UserName;
        return PersonName.FromDisplayName(FromDirectory(), account) ?? PersonName.FromDisplayName(FromLogonScreen(account), account);
    }

    /// <summary>Display name of the domain account (asks the domain; needs the company network or a cached logon).</summary>
    private static string? FromDirectory()
    {
        try
        {
            uint size = 256;
            var sb = new StringBuilder((int)size);
            return GetUserNameEx(NameDisplay, sb, ref size) ? sb.ToString() : null;
        }
        catch { return null; }
    }

    /// <summary>The name the Windows logon screen shows for the last user, only if that user is the current one.</summary>
    private static string? FromLogonScreen(string account)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            var sam = key?.GetValue("LastLoggedOnSAMUser") as string;
            if (sam is null || !sam.Split('\\').Last().Equals(account, StringComparison.OrdinalIgnoreCase)) return null;
            return key?.GetValue("LastLoggedOnDisplayName") as string;
        }
        catch { return null; }
    }
}
