// Hook bootstrap and desktop-thread subclass. Present wallpaper and widget together.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wingdi.h>
#include <commctrl.h>
#include <stdint.h>

namespace {
constexpr wchar_t kMapping[] = L"Local\\SchoolSchedulerDockFrame.v3";
constexpr wchar_t kMutex[] = L"Local\\SchoolSchedulerDockMutex.v1";
constexpr UINT_PTR kSubclass = 0x5343444B;
constexpr size_t kHeaderBytes = 128;
struct FrameHeader {
    uint32_t magic, version, sequence;
    int32_t x, y, destWidth, destHeight, width, height, stride, opacity;
};
HANDLE mapping = nullptr, mutexHandle = nullptr;
BYTE* mapped = nullptr;
HDC frameDc = nullptr, bufferDc = nullptr;
HBITMAP frameBitmap = nullptr, bufferBitmap = nullptr;
HGDIOBJ previousFrame = nullptr, previousBuffer = nullptr;
BYTE* framePixels = nullptr;
FrameHeader frame = {};
uint32_t loadedSequence = 0;
UINT refreshMessage = 0, stopMessage = 0, hitTestMessage = 0, inputMessage = 0;
HWND desktop = nullptr;
int bufferWidth = 0, bufferHeight = 0;
bool stopped = false, painting = false;
HMODULE moduleHold = nullptr;
UINT_PTR watchdog = 0;
bool pointerInside = false;
UINT capturedButtons = 0;

void Count(int offset) {
    if (mapped) InterlockedIncrement(reinterpret_cast<volatile LONG*>(mapped + offset));
}
void OpenFrame() {
    if (!refreshMessage) refreshMessage = RegisterWindowMessageW(L"SchoolSchedulerDockRefresh.v1");
    if (!stopMessage) stopMessage = RegisterWindowMessageW(L"SchoolSchedulerDockStop.v2");
    if (!hitTestMessage) hitTestMessage = RegisterWindowMessageW(L"SchoolSchedulerDockHitTest.v1");
    if (!inputMessage) inputMessage = RegisterWindowMessageW(L"SchoolSchedulerDockInput.v1");
    if (!mapping) mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, kMapping);
    if (mapping && !mapped) mapped = static_cast<BYTE*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 0));
    if (!mutexHandle) mutexHandle = OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, kMutex);
}
void DeleteSurface(HDC& dc, HBITMAP& bitmap, HGDIOBJ& previous) {
    if (bitmap) { SelectObject(dc, previous); DeleteObject(bitmap); bitmap = nullptr; }
    if (dc) { DeleteDC(dc); dc = nullptr; }
    previous = nullptr;
}
void Cleanup() {
    stopped = true;
    if (desktop && GetCapture() == desktop) ReleaseCapture();
    capturedButtons = 0; pointerInside = false;
    DeleteSurface(frameDc, frameBitmap, previousFrame);
    DeleteSurface(bufferDc, bufferBitmap, previousBuffer);
    if (mapped) { UnmapViewOfFile(mapped); mapped = nullptr; }
    if (mapping) { CloseHandle(mapping); mapping = nullptr; }
    if (mutexHandle) { CloseHandle(mutexHandle); mutexHandle = nullptr; }
}
RECT FrameRect(HWND h) {
    POINT p = { frame.x, frame.y };
    MapWindowPoints(nullptr, h, &p, 1);
    return { p.x, p.y, p.x + frame.destWidth, p.y + frame.destHeight };
}
void LoadFrame() {
    if (!mapped || !mutexHandle) return;
    DWORD wait = WaitForSingleObject(mutexHandle, 0);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) return;
    const FrameHeader incoming = *reinterpret_cast<const FrameHeader*>(mapped);
    if (incoming.magic == 0x4B434F44 && incoming.version == 1 && incoming.sequence != loadedSequence &&
        incoming.width > 0 && incoming.height > 0 && incoming.width <= 4096 && incoming.height <= 4096 &&
        static_cast<uint64_t>(incoming.width) * incoming.height * 4 <= 64 * 1024 * 1024 - kHeaderBytes &&
        incoming.stride == incoming.width * 4 && incoming.destWidth > 0 && incoming.destHeight > 0 &&
        incoming.opacity >= 0 && incoming.opacity <= 255) {
        if (!frameBitmap || frame.width != incoming.width || frame.height != incoming.height) {
            DeleteSurface(frameDc, frameBitmap, previousFrame);
            frameDc = CreateCompatibleDC(nullptr);
            BITMAPINFO info = {};
            info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = incoming.width;
            info.bmiHeader.biHeight = -incoming.height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = BI_RGB;
            if (frameDc) frameBitmap = CreateDIBSection(frameDc, &info, DIB_RGB_COLORS,
                reinterpret_cast<void**>(&framePixels), nullptr, 0);
            if (frameBitmap) previousFrame = SelectObject(frameDc, frameBitmap);
        }
        if (frameBitmap) {
            CopyMemory(framePixels, mapped + kHeaderBytes, static_cast<size_t>(incoming.stride) * incoming.height);
            frame = incoming;
            loadedSequence = incoming.sequence;
            *reinterpret_cast<LONG*>(mapped + 92) = 6;
        }
    }
    ReleaseMutex(mutexHandle);
}
bool IconAt(HWND h, POINT p) {
    LVHITTESTINFO info = {};
    info.pt = p;
    int index = static_cast<int>(SendMessageW(h, LVM_HITTEST, 0, reinterpret_cast<LPARAM>(&info)));
    return index >= 0 && (info.flags & LVHT_ONITEM);
}
bool WidgetAt(HWND h, POINT p) {
    if (!frameBitmap || !frame.opacity) return false;
    RECT area = FrameRect(h);
    if (!PtInRect(&area, p) || IconAt(h, p)) return false;
    if (mapped && !(*reinterpret_cast<LONG*>(mapped + 72) & 1) &&
        (p.x < area.left + 6 || p.x >= area.right - 6 || p.y < area.top + 6 || p.y >= area.bottom - 6)) return true;
    int x = (p.x - area.left) * frame.width / frame.destWidth;
    int y = (p.y - area.top) * frame.height / frame.destHeight;
    return framePixels[y * frame.stride + x * 4 + 3] * frame.opacity / 255 >= 32;
}
void ForwardMouse(HWND h, UINT msg, WPARAM wp, POINT client) {
    if (!mapped) return;
    HWND host = reinterpret_cast<HWND>(*reinterpret_cast<uint64_t*>(mapped + 104));
    if (!IsWindow(host)) return;
    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN) {
        DWORD pid = 0;
        GetWindowThreadProcessId(host, &pid);
        AllowSetForegroundWindow(pid);
    }
    MapWindowPoints(h, nullptr, &client, 1);
    uint64_t packet = msg | (static_cast<uint64_t>(LOWORD(wp)) << 16) | (static_cast<uint64_t>(HIWORD(wp)) << 32);
    PostMessageW(host, inputMessage, static_cast<WPARAM>(packet), MAKELPARAM(client.x, client.y));
}
HCURSOR WidgetCursor(HWND h, POINT p) {
    RECT area = FrameRect(h);
    if (mapped && !(*reinterpret_cast<LONG*>(mapped + 72) & 1)) {
        bool l=p.x<area.left+6, r=p.x>=area.right-6, t=p.y<area.top+6, b=p.y>=area.bottom-6;
        if ((l && t) || (r && b)) return LoadCursorW(nullptr, IDC_SIZENWSE);
        if ((r && t) || (l && b)) return LoadCursorW(nullptr, IDC_SIZENESW);
        if (l || r) return LoadCursorW(nullptr, IDC_SIZEWE);
        if (t || b) return LoadCursorW(nullptr, IDC_SIZENS);
    }
    UINT id = mapped ? *reinterpret_cast<UINT*>(mapped + 84) : 0;
    return LoadCursorW(nullptr, id ? MAKEINTRESOURCEW(id) : IDC_ARROW);
}
void CompositeFrame(HWND h, HDC dc) {
    if (!frameBitmap || !frame.opacity) return;
    RECT area = FrameRect(h);
    int saved = SaveDC(dc), covered = 0;
    IntersectClipRect(dc, area.left, area.top, area.right, area.bottom);
    int count = static_cast<int>(SendMessageW(h, LVM_GETITEMCOUNT, 0, 0));
    for (int i = 0; i < count && i < 4096; ++i) {
        RECT icon = { LVIR_SELECTBOUNDS, 0, 0, 0 }, overlap;
        if (SendMessageW(h, LVM_GETITEMRECT, i, reinterpret_cast<LPARAM>(&icon)) && IntersectRect(&overlap, &icon, &area)) {
            ++covered;
            ExcludeClipRect(dc, icon.left - 2, icon.top - 2, icon.right + 2, icon.bottom + 2);
        }
    }
    if (mapped) *reinterpret_cast<LONG*>(mapped + 88) = covered;
    BLENDFUNCTION blend = { AC_SRC_OVER, 0, static_cast<BYTE>(frame.opacity), AC_SRC_ALPHA };
    Count(52);
    if (AlphaBlend(dc, area.left, area.top, frame.destWidth, frame.destHeight,
        frameDc, 0, 0, frame.width, frame.height, blend)) Count(56);
    else if (mapped) *reinterpret_cast<LONG*>(mapped + 60) = GetLastError();
    if (saved) RestoreDC(dc, saved);
}
bool EnsureBuffer(HDC dc, int width, int height) {
    if (bufferBitmap && bufferWidth == width && bufferHeight == height) return true;
    DeleteSurface(bufferDc, bufferBitmap, previousBuffer);
    bufferWidth = width; bufferHeight = height;
    bufferDc = CreateCompatibleDC(dc);
    if (bufferDc) bufferBitmap = CreateCompatibleBitmap(dc, width, height);
    if (bufferBitmap) previousBuffer = SelectObject(bufferDc, bufferBitmap);
    return bufferBitmap != nullptr;
}
LRESULT CALLBACK DesktopProc(HWND h, UINT msg, WPARAM wp, LPARAM lp, UINT_PTR, DWORD_PTR) {
    if (msg == stopMessage) {
        RECT old = FrameRect(h);
        if (GetCapture() == h) ReleaseCapture();
        if (watchdog) { KillTimer(h, watchdog); watchdog = 0; }
        RemoveWindowSubclass(h, DesktopProc, kSubclass);
        desktop = nullptr;
        Cleanup();
        RedrawWindow(h, &old, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
        // The host still owns its hook reference during an orderly stop.
        if (moduleHold) { FreeLibrary(moduleHold); moduleHold = nullptr; }
        return 0;
    }
    if (msg == WM_TIMER && wp == watchdog && mapped) {
        HWND host = reinterpret_cast<HWND>(*reinterpret_cast<uint64_t*>(mapped + 104));
        if (!IsWindow(host)) {
            RECT old = FrameRect(h);
            if (GetCapture() == h) ReleaseCapture();
            KillTimer(h, watchdog); watchdog = 0;
            RemoveWindowSubclass(h, DesktopProc, kSubclass);
            desktop = nullptr;
            Cleanup();
            RedrawWindow(h, &old, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
            // Keep the module reference after a crash: this callback must return safely.
        }
        return 0;
    }
    if (msg == WM_NCDESTROY) {
        RemoveWindowSubclass(h, DesktopProc, kSubclass);
        desktop = nullptr;
        return DefSubclassProc(h, msg, wp, lp);
    }
    if (msg == refreshMessage) {
        RECT old = FrameRect(h);
        LoadFrame();
        RECT current = FrameRect(h), damage;
        UnionRect(&damage, &old, &current);
        Count(76);
        // Never expose an erased background before the composed frame.
        RedrawWindow(h, &damage, nullptr, RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW | RDW_NOCHILDREN);
        return 0;
    }
    if (msg == hitTestMessage && mapped) {
        POINT p = { *reinterpret_cast<LONG*>(mapped + 116), *reinterpret_cast<LONG*>(mapped + 120) };
        MapWindowPoints(nullptr, h, &p, 1);
        *reinterpret_cast<LONG*>(mapped + 124) = IconAt(h, p) ? 1 : 0;
        return 0;
    }
    if (msg == WM_CAPTURECHANGED && capturedButtons) {
        POINT p; GetCursorPos(&p); ScreenToClient(h, &p);
        if (capturedButtons & MK_LBUTTON) ForwardMouse(h, WM_LBUTTONUP, 0, p);
        if (capturedButtons & MK_RBUTTON) ForwardMouse(h, WM_RBUTTONUP, 0, p);
        if (capturedButtons & MK_MBUTTON) ForwardMouse(h, WM_MBUTTONUP, 0, p);
        capturedButtons = 0;
        ForwardMouse(h, WM_MOUSELEAVE, 0, p);
    }
    if (msg == WM_MOUSELEAVE && pointerInside) {
        pointerInside = false;
        // A captured drag continues outside the desktop; capture loss sends button-up separately.
        if (!capturedButtons) ForwardMouse(h, WM_MOUSELEAVE, 0, {});
    }
    if (msg == WM_SETCURSOR && LOWORD(lp) == HTCLIENT) {
        POINT p; GetCursorPos(&p); ScreenToClient(h, &p);
        if (WidgetAt(h, p)) { SetCursor(WidgetCursor(h,p)); return TRUE; }
    }
    if (msg >= WM_MOUSEFIRST && msg <= WM_MOUSELAST && !painting) {
        POINT p = { static_cast<short>(LOWORD(lp)), static_cast<short>(HIWORD(lp)) };
        if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL) MapWindowPoints(nullptr, h, &p, 1);
        bool over = WidgetAt(h, p);
        if (over || capturedButtons) {
            if (!pointerInside) {
                pointerInside = true;
                TRACKMOUSEEVENT track = { sizeof(track), TME_LEAVE, h, 0 };
                TrackMouseEvent(&track);
            }
            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONDBLCLK) capturedButtons |= MK_LBUTTON;
            if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONDBLCLK) capturedButtons |= MK_RBUTTON;
            if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONDBLCLK) capturedButtons |= MK_MBUTTON;
            if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONDBLCLK) capturedButtons |= HIWORD(wp) == XBUTTON1 ? MK_XBUTTON1 : MK_XBUTTON2;
            if (capturedButtons && GetCapture() != h) SetCapture(h);
            ForwardMouse(h, msg, wp, p);
            if (msg == WM_LBUTTONUP) capturedButtons &= ~MK_LBUTTON;
            if (msg == WM_RBUTTONUP) capturedButtons &= ~MK_RBUTTON;
            if (msg == WM_MBUTTONUP) capturedButtons &= ~MK_MBUTTON;
            if (msg == WM_XBUTTONUP) capturedButtons &= ~(HIWORD(wp) == XBUTTON1 ? MK_XBUTTON1 : MK_XBUTTON2);
            if (!capturedButtons && GetCapture() == h) ReleaseCapture();
            if (over) SetCursor(WidgetCursor(h,p));
            return 0;
        }
        if (pointerInside) {
            pointerInside = false;
            ForwardMouse(h, WM_MOUSELEAVE, 0, p);
        }
    }
    if (msg == WM_ERASEBKGND && frameBitmap && frame.opacity && !painting) { Count(68); return 1; }
    if (msg == WM_PAINT && frameBitmap && frame.opacity && !painting) {
        RECT client;
        GetClientRect(h, &client);
        HDC probe = GetDC(h);
        bool ready = probe && EnsureBuffer(probe, client.right, client.bottom);
        if (probe) ReleaseDC(h, probe);
        if (!ready) return DefSubclassProc(h, msg, wp, lp);
        painting = true;
        PAINTSTRUCT ps = {};
        HDC dc = BeginPaint(h, &ps);
        int saved = SaveDC(bufferDc);
        IntersectClipRect(bufferDc, ps.rcPaint.left, ps.rcPaint.top, ps.rcPaint.right, ps.rcPaint.bottom);
        HWND wallpaper = GetParent(GetParent(h));
        POINT origin = {};
        MapWindowPoints(h, wallpaper, &origin, 1);
        SetViewportOrgEx(bufferDc, -origin.x, -origin.y, nullptr);
        SendMessageW(wallpaper, WM_ERASEBKGND, reinterpret_cast<WPARAM>(bufferDc), 0);
        SetViewportOrgEx(bufferDc, 0, 0, nullptr);
        DefSubclassProc(h, WM_PRINTCLIENT, reinterpret_cast<WPARAM>(bufferDc), PRF_CLIENT | PRF_ERASEBKGND);
        CompositeFrame(h, bufferDc);
        if (BitBlt(dc, ps.rcPaint.left, ps.rcPaint.top,
            ps.rcPaint.right - ps.rcPaint.left, ps.rcPaint.bottom - ps.rcPaint.top,
            bufferDc, ps.rcPaint.left, ps.rcPaint.top, SRCCOPY)) Count(64);
        if (mapped) *reinterpret_cast<LONG*>(mapped + 96) = loadedSequence;
        if (saved) RestoreDC(bufferDc, saved);
        EndPaint(h, &ps);
        painting = false;
        Count(48);
        return 0;
    }
    return DefSubclassProc(h, msg, wp, lp);
}
bool DesktopListView(HWND h) {
    wchar_t cls[64] = {};
    if (!GetClassNameW(h, cls, 64) || lstrcmpW(cls, L"SysListView32")) return false;
    return GetClassNameW(GetParent(h), cls, 64) && !lstrcmpW(cls, L"SHELLDLL_DefView");
}
}
extern "C" __declspec(dllexport) LRESULT CALLBACK DockHook(int code, WPARAM wp, LPARAM lp) {
    if (code >= 0 && lp) {
        const auto* data = reinterpret_cast<const CWPSTRUCT*>(lp);
        if (stopped) {
            // A previous host crash leaves a module reference until the next attachment.
            if (data->message != refreshMessage || !DesktopListView(data->hwnd))
                return CallNextHookEx(nullptr, code, wp, lp);
            stopped = false; frame = {}; loadedSequence = 0;
        }
        OpenFrame();
        Count(44);
        if (!desktop && data->message == refreshMessage && DesktopListView(data->hwnd)) {
            if (SetWindowSubclass(data->hwnd, DesktopProc, kSubclass, 0)) {
                desktop = data->hwnd;
                if (!moduleHold) GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                    reinterpret_cast<LPCWSTR>(&DesktopProc), &moduleHold);
                watchdog = SetTimer(desktop, kSubclass, 1000, nullptr);
                if (mapped) *reinterpret_cast<LONG*>(mapped + 100) = 1;
                SendMessageW(desktop, refreshMessage, 0, 0);
            }
        }
        if (!desktop && data->message == stopMessage && !stopped) Cleanup();
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}
BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
