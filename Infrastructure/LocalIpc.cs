using System.Security.AccessControl;
using System.Security.Principal;

namespace WallpaperProfiles.Infrastructure;

/// <summary>
/// Creates the named <c>Local\Belie.*</c> events and mutexes Belie's processes signal each other with.
/// Each one gets an explicit DACL that lets only the current Windows account open it, instead of the
/// token's default DACL (which also admits the logon-session SID and SYSTEM). Other processes running as
/// the same user can still signal them; that is the same privilege Belie itself has.
/// </summary>
internal static class LocalIpc
{
    public static EventWaitHandle Event(EventResetMode mode, string name) => Event(mode, name, out _);

    public static EventWaitHandle Event(EventResetMode mode, string name, out bool createdNew) =>
        EventWaitHandleAcl.Create(false, mode, name, out createdNew, EventSecurity());

    public static Mutex Mutex(bool initiallyOwned, string name, out bool createdNew) =>
        MutexAcl.Create(initiallyOwned, name, out createdNew, MutexSecurity());

    internal static SecurityIdentifier CurrentUser
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!;
        }
    }

    internal static EventWaitHandleSecurity EventSecurity()
    {
        var security = new EventWaitHandleSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return security;
    }

    internal static MutexSecurity MutexSecurity()
    {
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new MutexAccessRule(CurrentUser, MutexRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
