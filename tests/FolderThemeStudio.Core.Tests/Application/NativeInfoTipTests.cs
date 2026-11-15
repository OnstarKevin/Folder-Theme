using System.Runtime.InteropServices;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class NativeInfoTipTests
{
    [Fact]
    public void NativeAdapter_FiltersOwnerAndPopsAnActualWindowsTooltip()
    {
        WpfTestHost.Invoke(() =>
        {
            var platform = (IExplorerInfoTipPlatform)Activator.CreateInstance(typeof(ExplorerInfoTipSuppressor).Assembly
                .GetType("FolderThemeStudio.App.Services.WindowsExplorerInfoTipPlatform", throwOnError: true)!)!;
            var controls = new InitControls { Size = 8, Classes = 0xFF };
            Assert.True(InitCommonControlsEx(ref controls));
            var module = GetModuleHandle(null);
            WindowProc proc = DefWindowProc;
            var cls = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(proc), Instance = module, Name = "CabinetWClass" };
            var atom = RegisterClass(ref cls);
            Assert.NotEqual((ushort)0, atom);
            nint root = 0, other = 0, tip = 0, text = 0, toolPointer = 0;
            try
            {
                root = CreateWindowEx(0, cls.Name, "Test shell view", 0x80000000, -10000, -10000, 1, 1, 0, 0, module, 0);
                other = CreateWindowEx(0, "STATIC", "Other view", 0x80000000, -10000, -10000, 1, 1, 0, 0, module, 0);
                tip = CreateWindowEx(0x00080080, "tooltips_class32", null, 0x80000003, 0, 0, 0, 0, root, 0, module, 0);
                Assert.NotEqual(nint.Zero, root);
                Assert.NotEqual(nint.Zero, tip);
                Assert.True(SetLayeredWindowAttributes(tip, 0, 0, 2)); // invisible test fixture
                text = Marshal.StringToHGlobalUni("Folder information");
                // The fixture uses the unthemed v5 common-control class, whose V2 structure omits lpReserved.
                var tool = new ToolInfo { Size = (uint)(Marshal.SizeOf<ToolInfo>() - nint.Size), Flags = 0x20, Window = root, Id = 1, Text = text };
                toolPointer = Marshal.AllocHGlobal(Marshal.SizeOf<ToolInfo>());
                Marshal.StructureToPtr(tool, toolPointer, false);
                Assert.NotEqual(nint.Zero, SendMessage(tip, 0x0432, 0, toolPointer)); // TTM_ADDTOOLW
                SendMessage(tip, 0x0411, 1, toolPointer); // TTM_TRACKACTIVATE
                Assert.True(IsWindowVisible(tip));
                Assert.Contains(tip, platform.FindInfoTips(root));
                Assert.True(platform.BelongsToFolderView(tip, root));
                Assert.False(platform.BelongsToFolderView(tip, other));
                platform.Dismiss(tip);
                Assert.False(IsWindowVisible(tip));
            }
            finally
            {
                if (tip != 0) DestroyWindow(tip);
                if (other != 0) DestroyWindow(other);
                if (root != 0) DestroyWindow(root);
                if (toolPointer != 0) Marshal.FreeHGlobal(toolPointer);
                if (text != 0) Marshal.FreeHGlobal(text);
                UnregisterClass(cls.Name, module);
                GC.KeepAlive(proc);
            }
        });
    }

    private delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? Menu;
        internal string Name;
    }
    [StructLayout(LayoutKind.Sequential)] private struct InitControls { internal uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ToolInfo
    {
        internal uint Size, Flags;
        internal nint Window;
        internal nuint Id;
        internal NativeRect Rect;
        internal nint Instance, Text, Param, Reserved;
    }
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitCommonControlsEx(ref InitControls controls);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WindowClass cls);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint module);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint exStyle, string cls, string? title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
