using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

namespace DesktopIconToggle;

public class DesktopManager : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;
    private const int SM_CXDRAG = 68;
    private const int SM_CYDRAG = 69;

    private NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hookID = IntPtr.Zero;

    // 双击状态机：一次有效点击 = Down → 未拖动 → Up；双击在第二次 Up 后判定。
    // 判定语义与 Windows 一致：双击只看两次 Down 的时间差与双击矩形，
    // 拖拽看 Down 点与 Up 点的总位移（中途抖动后回到原位松开仍是有效点击）。
    private readonly object _stateLock = new();
    private bool _buttonDown;
    private bool _dragExceeded;
    private NativeMethods.POINT _downPos;
    private int _downTime;
    private ClickInfo? _lastClick;

    private sealed record ClickInfo(int DownTime, NativeMethods.POINT Pos);

    public bool IsPaused { get; set; } = false;

    public DesktopManager()
    {
        _proc = HookCallback;
        _hookID = SetHook(_proc);
    }

    private IntPtr SetHook(NativeMethods.LowLevelMouseProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            return NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, proc, NativeMethods.GetModuleHandle(curModule.ModuleName!), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (IsPaused)
            {
                ResetDoubleClickState();
            }
            else
            {
                int msg = wParam.ToInt32();
                if (msg is WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP)
                {
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    switch (msg)
                    {
                        case WM_LBUTTONDOWN:
                            OnLeftButtonDown(data.pt);
                            break;
                        case WM_MOUSEMOVE:
                            OnMouseMove(data.pt);
                            break;
                        case WM_LBUTTONUP:
                            OnLeftButtonUp(data.pt);
                            break;
                    }
                }
                else if (msg is WM_RBUTTONDOWN or WM_MBUTTONDOWN)
                {
                    // 其它按键按下会打断双击序列，避免污染后续双击状态
                    ResetDoubleClickState();
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private void OnLeftButtonDown(NativeMethods.POINT pt)
    {
        lock (_stateLock)
        {
            _buttonDown = true;
            _dragExceeded = false;
            _downPos = pt;
            _downTime = Environment.TickCount;
        }
    }

    private void OnMouseMove(NativeMethods.POINT pt)
    {
        lock (_stateLock)
        {
            if (!_buttonDown || _dragExceeded)
            {
                return;
            }

            int dragX = NativeMethods.GetSystemMetrics(SM_CXDRAG);
            int dragY = NativeMethods.GetSystemMetrics(SM_CYDRAG);
            if (Math.Abs(pt.X - _downPos.X) > dragX || Math.Abs(pt.Y - _downPos.Y) > dragY)
            {
                // 超过系统拖拽阈值，标记为疑似拖拽/框选。
                // 此处不清除双击候选：快速双击按压期间的手部抖动常超过
                // SM_CXDRAG（默认仅 4px），但只要松开时回到按下点附近，
                // Windows 语义下仍是有效点击，应允许配对成双击。
                _dragExceeded = true;
            }
        }
    }

    private void OnLeftButtonUp(NativeMethods.POINT pt)
    {
        ClickInfo? prev;
        ClickInfo current;
        lock (_stateLock)
        {
            if (!_buttonDown)
            {
                return;
            }
            _buttonDown = false;

            // 拖拽判定：Down 点与 Up 点的总位移超过 SM_CXDRAG/SM_CYDRAG
            // 才视为拖拽/框选（框选松开位置必然远离按下点）。
            // 抖动后回到原位松开不算拖拽。
            int dragX = NativeMethods.GetSystemMetrics(SM_CXDRAG);
            int dragY = NativeMethods.GetSystemMetrics(SM_CYDRAG);
            if (Math.Abs(pt.X - _downPos.X) > dragX || Math.Abs(pt.Y - _downPos.Y) > dragY)
            {
                // 框选/拖拽结束，清除双击候选，
                // 防止随后的快速单击被误判为双击
                _lastClick = null;
                return;
            }

            current = new ClickInfo(_downTime, _downPos);
            prev = _lastClick;

            int doubleClickTime = (int)NativeMethods.GetDoubleClickTime();
            int doubleClickW = NativeMethods.GetSystemMetrics(SM_CXDOUBLECLK);
            int doubleClickH = NativeMethods.GetSystemMetrics(SM_CYDOUBLECLK);

            // 双击判定：与 Windows 一致，两次 Down 的时间差 <= 双击时间，
            // 且两次 Down 位置落在双击矩形内
            bool isDoubleClickCandidate = prev != null
                && current.DownTime - prev.DownTime >= 0
                && current.DownTime - prev.DownTime <= doubleClickTime
                && Math.Abs(current.Pos.X - prev.Pos.X) <= doubleClickW
                && Math.Abs(current.Pos.Y - prev.Pos.Y) <= doubleClickH;

            if (isDoubleClickCandidate)
            {
                // 候选已被本次配对消费，无论结果如何都不再保留
                _lastClick = null;
                var first = prev!;
                Task.Run(() =>
                {
                    // 两次点击都必须发生在桌面空白区域
                    if (IsMouseOverDesktopBackground(first.Pos.X, first.Pos.Y)
                        && IsMouseOverDesktopBackground(current.Pos.X, current.Pos.Y))
                    {
                        ToggleDesktopIcons();
                    }
                });
            }
            else
            {
                _lastClick = current;
            }
        }
    }

    private void ResetDoubleClickState()
    {
        lock (_stateLock)
        {
            _buttonDown = false;
            _dragExceeded = false;
            _lastClick = null;
        }
    }

    private bool IsMouseOverDesktopBackground(double x, double y)
    {
        try
        {
            var pt = new System.Windows.Point(x, y);
            var element = AutomationElement.FromPoint(pt);

            if (element == null) return false;

            if (element.Current.ControlType == ControlType.ListItem)
                return false;

            string className = element.Current.ClassName;
            return className == "SysListView32" || className == "WorkerW" || className == "Progman";
        }
        catch
        {
            return false;
        }
    }

    private void ToggleDesktopIcons()
    {
        IntPtr defView = IntPtr.Zero;
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        
        defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                IntPtr p = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != IntPtr.Zero)
                {
                    defView = p;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (defView != IntPtr.Zero)
        {
            NativeMethods.SendMessage(defView, 0x0111 /*WM_COMMAND*/, (IntPtr)0x7402, IntPtr.Zero);

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                if (key != null)
                {
                    int state = (int)(key.GetValue("HideIcons", 1) ?? 1);
                    key.SetValue("HideIcons", state == 1 ? 0 : 1);
                }
            }
            catch {}
        }
    }

    public void Dispose()
    {
        NativeMethods.UnhookWindowsHookEx(_hookID);
    }
}