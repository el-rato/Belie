using System;
using System.IO;
using System.Runtime.InteropServices;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.UI;

/// <summary>
/// Wraps the modern Vista+ common item dialog (IFileDialog with FOS_PICKFOLDERS)
/// so users get the same fluent folder picker as Explorer instead of the legacy
/// tree-view FolderBrowserDialog.
/// </summary>
internal static class ModernFolderPicker
{
    private static readonly Guid ClassIdFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid ShellItemIdList = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private const uint FosPickFolders = 0x20;
    private const uint FosForceFileSystem = 0x40;
    private const uint FosPathMustExist = 0x800;
    private const uint SigdnFileSysPath = 0x80058000;

    /// <summary>
    /// Shows the modern folder picker. Returns the selected file system path,
    /// or null when the user cancels or the dialog cannot be shown.
    /// </summary>
    public static string? Pick(System.Windows.Window? owner, string title, string? initialDirectory)
    {
        IFileDialog? dialog = null;
        try
        {
            dialog = (IFileDialog)new FileOpenDialog();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
            dialog.SetTitle(title);

            var start = initialDirectory;
            if (!string.IsNullOrWhiteSpace(start) && File.Exists(start))
            {
                start = Path.GetDirectoryName(start);
            }
            if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
            {
                var iid = ShellItemIdList;
                if (SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out var folder) == 0)
                {
                    try
                    {
                        dialog.SetFolder(folder);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(folder);
                    }
                }
            }

            var ownerHandle = owner != null
                ? new System.Windows.Interop.WindowInteropHelper(owner).Handle
                : IntPtr.Zero;
            if (dialog.Show(ownerHandle) != 0)
            {
                return null;
            }
            if (dialog.GetResult(out var result) != 0)
            {
                return null;
            }
            try
            {
                return result.GetDisplayName(SigdnFileSysPath, out var path) == 0 ? path : null;
            }
            finally
            {
                Marshal.ReleaseComObject(result);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Modern folder picker failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (dialog != null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr hwndOwner);

        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);

        void SetFileTypeIndex(uint iFileType);

        [PreserveSig]
        int GetFileTypeIndex(out uint piFileType);

        [PreserveSig]
        int Advise(IntPtr pfde, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int SetOptions(uint fos);

        [PreserveSig]
        int GetOptions(out uint pfos);

        void SetDefaultFolder(IShellItem psi);

        void SetFolder(IShellItem psi);

        [PreserveSig]
        int GetFolder(out IShellItem ppsi);

        [PreserveSig]
        int GetCurrentSelection(out IShellItem ppsi);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        [PreserveSig]
        int GetResult(out IShellItem ppsi);

        [PreserveSig]
        int AddPlace(IShellItem psi, uint fdap);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        [PreserveSig]
        int ClearClientData();

        void SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
