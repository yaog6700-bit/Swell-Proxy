using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// 基于 Win32 GetOpenFileNameW 的文件选择器。
    /// 不依赖 COM 激活，不受 PublishTrimmed 影响，在普通进程和管理员进程中均可正常弹出对话框。
    /// WinUI3 FileOpenPicker 在 elevated 进程（如 TUN 模式）下会抛出 COMException 0x80004005，
    /// 此帮助类彻底绕开该问题。
    /// </summary>
    public static class Win32FilePickerHelper
    {
        // ── Native struct ─────────────────────────────────────────────────────

        /// <summary>Maps to native OPENFILENAMEW (commdlg.h).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct OPENFILENAME
        {
            public int    lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;       // double-null-terminated wide string
            public IntPtr lpstrCustomFilter;
            public int    nMaxCustFilter;
            public int    nFilterIndex;
            public IntPtr lpstrFile;         // writable path buffer (wide chars)
            public int    nMaxFile;
            public IntPtr lpstrFileTitle;
            public int    nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;        // dialog title (wide string)
            public int    Flags;
            public short  nFileOffset;
            public short  nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public int    dwReserved;
            public int    FlagsEx;
        }

        // ── P/Invoke ──────────────────────────────────────────────────────────

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

        // ── Flags ─────────────────────────────────────────────────────────────

        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR   = 0x00000008;
        private const int OFN_EXPLORER      = 0x00080000;
        private const int MAX_PATH_CHARS    = 32768;   // extended-length path

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// 弹出文件选择对话框（在 UI/STA 线程上同步运行，兼容 elevated 进程）。
        /// </summary>
        /// <param name="hwnd">父窗口句柄。</param>
        /// <param name="filters">文件类型过滤器，每项为 (显示名, 通配符)，例如 ("可执行文件", "*.exe")。</param>
        /// <param name="title">对话框标题；null 使用系统默认。</param>
        /// <returns>用户选择的文件完整路径；取消或失败返回 null。</returns>
        public static Task<string?> PickSingleFileAsync(
            IntPtr hwnd,
            IReadOnlyList<(string Name, string Spec)>? filters = null,
            string? title = null)
        {
            IntPtr filterPtr  = IntPtr.Zero;
            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr titlePtr   = IntPtr.Zero;

            try
            {
                // ── Build double-null-terminated filter string ────────────────
                // Format: "Display1\0*.ext1\0Display2\0*.ext2\0\0"
                var sb = new StringBuilder();
                if (filters != null && filters.Count > 0)
                {
                    foreach (var (name, spec) in filters)
                        sb.Append(name).Append('\0').Append(spec).Append('\0');
                }
                else
                {
                    sb.Append("所有文件").Append('\0').Append("*.*").Append('\0');
                }
                sb.Append('\0'); // final double-null terminator

                char[] filterChars = sb.ToString().ToCharArray();
                int    filterBytes = filterChars.Length * 2; // UTF-16LE: 2 bytes per char
                filterPtr = Marshal.AllocHGlobal(filterBytes);
                Marshal.Copy(filterChars, 0, filterPtr, filterChars.Length);

                // ── File result buffer (zero-initialised) ─────────────────────
                int bufferBytes = MAX_PATH_CHARS * 2;
                fileBuffer = Marshal.AllocHGlobal(bufferBytes);
                // Zero the entire buffer so we detect an empty result reliably
                for (int i = 0; i < bufferBytes; i += 2)
                    Marshal.WriteInt16(fileBuffer, i, 0);

                // ── Optional dialog title ─────────────────────────────────────
                if (title != null)
                    titlePtr = Marshal.StringToHGlobalUni(title);

                // ── Fill OPENFILENAME ─────────────────────────────────────────
                var ofn = new OPENFILENAME
                {
                    lStructSize  = Marshal.SizeOf<OPENFILENAME>(),
                    hwndOwner    = hwnd,
                    lpstrFilter  = filterPtr,
                    nFilterIndex = 1,
                    lpstrFile    = fileBuffer,
                    nMaxFile     = MAX_PATH_CHARS,
                    lpstrTitle   = titlePtr,
                    Flags        = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
                };

                // ── Show dialog ───────────────────────────────────────────────
                bool ok = GetOpenFileNameW(ref ofn);
                if (!ok)
                {
                    // User cancelled (CommDlgExtendedError == 0) or a real error.
                    Debug.WriteLine($"[Win32FilePickerHelper] 取消或错误，CommDlgExtendedError={CommDlgExtendedError()}");
                    return Task.FromResult<string?>(null);
                }

                string path = Marshal.PtrToStringUni(fileBuffer) ?? string.Empty;
                return Task.FromResult<string?>(string.IsNullOrEmpty(path) ? null : path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Win32FilePickerHelper] 异常: {ex}");
                return Task.FromResult<string?>(null);
            }
            finally
            {
                if (filterPtr  != IntPtr.Zero) Marshal.FreeHGlobal(filterPtr);
                if (fileBuffer != IntPtr.Zero) Marshal.FreeHGlobal(fileBuffer);
                if (titlePtr   != IntPtr.Zero) Marshal.FreeHGlobal(titlePtr);
            }
        }

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();
    }
}
