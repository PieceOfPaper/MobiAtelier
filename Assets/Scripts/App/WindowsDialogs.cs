// Windows 기본 대화상자 (폴더 선택, 파일 열기). P/Invoke 만 쓰고 별도 STA 스레드에서 연다.
// 취소하면 null 을 돌려준다. Windows 가 아니면 항상 null.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class WindowsDialogs
{
    public static bool Supported =>
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        true;
#else
        false;
#endif

    static Task<string> RunSta(Func<IntPtr, string> body)
    {
        var tcs = new TaskCompletionSource<string>();
        if (!Supported) { tcs.SetResult(null); return tcs.Task; }
        IntPtr owner = GetActiveWindow();
        var t = new Thread(() =>
        {
            int hr = OleInitialize(IntPtr.Zero);
            try { tcs.SetResult(body(owner)); }
            catch (Exception e) { tcs.SetException(e); }
            finally { if (hr >= 0) OleUninitialize(); }
        }) { IsBackground = true, Name = "WindowsDialog" };
        t.Start();
        return tcs.Task;
    }

    public static Task<string> PickFolder(string title, string initial) => RunSta(owner =>
    {
        BrowseCallback cb = (hwnd, msg, lp, data) =>
        {
            if (msg == BFFM_INITIALIZED && !string.IsNullOrEmpty(initial)) SendMessage(hwnd, BFFM_SETSELECTIONW, (IntPtr)1, initial);
            return 0;
        };
        var bi = new BROWSEINFO
        {
            hwndOwner = owner,
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
            lpfn = cb,
        };
        IntPtr pidl = SHBrowseForFolderW(ref bi);
        GC.KeepAlive(cb);
        if (pidl == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
        }
        finally { CoTaskMemFree(pidl); }
    });

    /// <summary>filter 예: "JSON (*.json)\0*.json\0"</summary>
    public static Task<string> OpenFile(string title, string filter, string initialDir) => RunSta(owner =>
    {
        var file = new StringBuilder(2048);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
            hwndOwner = owner,
            lpstrFilter = filter + "\0",
            lpstrFile = file,
            nMaxFile = file.Capacity,
            lpstrInitialDir = initialDir,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
            lpstrDefExt = "json",
        };
        return GetOpenFileNameW(ref ofn) ? file.ToString() : null;
    });

    const uint BIF_RETURNONLYFSDIRS = 0x1, BIF_NEWDIALOGSTYLE = 0x40;
    const uint BFFM_INITIALIZED = 1, BFFM_SETSELECTIONW = 0x467;
    const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_NOCHANGEDIR = 0x8, OFN_EXPLORER = 0x80000;

    delegate int BrowseCallback(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public BrowseCallback lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public StringBuilder lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHBrowseForFolderW(ref BROWSEINFO bi);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
    [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] static extern void OleUninitialize();
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr ptr);
}
