using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// 提供基于 Win32 IFileOpenDialog 的文件选择对话框，
    /// 解决 WinUI 3 <see cref="Windows.Storage.Pickers.FileOpenPicker"/>
    /// 在管理员（elevated）进程下（如启用 TUN 模式时）抛出
    /// <see cref="System.Runtime.InteropServices.COMException"/> 0x80004005 的问题。
    /// </summary>
    public static class Win32FilePickerHelper
    {
        // ── COM interfaces ────────────────────────────────────────────────────

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig] int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid([In] ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);

            // IFileOpenDialog
            void GetResults(out IShellItemArray ppenum);
            void GetSelectedItems(out IShellItemArray ppsai);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            void GetPropertyStore(uint flags, [In] ref Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList(IntPtr keyType, [In] ref Guid riid, out IntPtr ppv);
            void GetAttributes(uint AttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems(out IntPtr ppenumShellItems);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
        }

        // SIGDN_FILESYSPATH = 0x80058000
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        // FOS_FILEMUSTEXIST = 0x1000, FOS_PATHMUSTEXIST = 0x800
        private const uint FOS_FILEMUSTEXIST = 0x1000;
        private const uint FOS_PATHMUSTEXIST = 0x0800;

        [DllImport("shell32.dll")]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            out IShellItem ppv);

        // CLSID_FileOpenDialog
        private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// 以 Win32 IFileOpenDialog 打开文件选择对话框，兼容 elevated 进程。
        /// </summary>
        /// <param name="hwnd">父窗口句柄，使用 <c>WindowNative.GetWindowHandle(MainWindow.Instance)</c>。</param>
        /// <param name="filters">
        /// 文件类型过滤器列表，每项为 (显示名, 通配符)，例如 ("可执行文件", "*.exe")。
        /// 传入空列表或 null 则不过滤（显示所有文件）。
        /// </param>
        /// <param name="title">对话框标题，null 则使用系统默认。</param>
        /// <returns>用户选择的文件完整路径；用户取消则返回 null。</returns>
        public static Task<string?> PickSingleFileAsync(
            IntPtr hwnd,
            IReadOnlyList<(string Name, string Spec)>? filters = null,
            string? title = null)
        {
            // 注意：必须在 STA 线程（即 UI 线程）上直接调用，不能用 Task.Run（线程池是 MTA）。
            // IFileOpenDialog 是 STA COM 对象，在 MTA 线程上调用会导致对话框无声失败。
            // 调用方（async void 事件处理器）已在 WinUI UI 线程（STA）上，直接同步运行即可。
            try
            {
                // Create the COM dialog object
                var dialog = (IFileOpenDialog)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(CLSID_FileOpenDialog)!)!;

                // Set options
                dialog.GetOptions(out uint opts);
                dialog.SetOptions(opts | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST);

                // Set title
                if (!string.IsNullOrWhiteSpace(title))
                    dialog.SetTitle(title);

                // Set filters
                if (filters != null && filters.Count > 0)
                {
                    var specs = new COMDLG_FILTERSPEC[filters.Count];
                    for (int i = 0; i < filters.Count; i++)
                        specs[i] = new COMDLG_FILTERSPEC
                        {
                            pszName = filters[i].Name,
                            pszSpec = filters[i].Spec
                        };
                    dialog.SetFileTypes((uint)specs.Length, specs);
                }

                // Show dialog — S_OK = 0, HRESULT_FROM_WIN32(ERROR_CANCELLED) = 0x800704C7
                int hr = dialog.Show(hwnd);
                if (hr != 0) return Task.FromResult<string?>(null); // user cancelled or error

                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return Task.FromResult<string?>(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Win32FilePickerHelper] PickSingleFileAsync 失败: {ex.Message}");
                return Task.FromResult<string?>(null);
            }
        }
    }
}
