/* touchshim.dll - make mouse clicks arrive at FGO Arcade as touch input.
 *
 * The game registers for touch (RegisterTouchWindow) and drives its title and
 * menus from WM_TOUCH. Wine implements RegisterTouchWindow/CloseTouchInputHandle
 * as stubs and there is no touch device, so no touch ever arrives and the game
 * sits on "Please touch the screen" forever. The platform's own mouse-to-touch
 * remap only engages once a session is running, which is too late for the title.
 *
 * This DLL is injected into the game process. It:
 *   - replaces the game's imports of GetTouchInputInfo, CloseTouchInputHandle
 *     and RegisterTouchWindow with its own
 *   - waits for the game's window, then subclasses it
 *   - turns left-button down/move/up into WM_TOUCH messages carrying a fake
 *     touch handle, which GetTouchInputInfo answers with screen coordinates
 *
 * Build:  x86_64-w64-mingw32-gcc -shared -o touchshim.dll touchshim.c -luser32
 * Load:   inject.exe -d -k touchshim.dll -k fgohook.dll ago.exe ...
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#ifndef GET_X_LPARAM
#define GET_X_LPARAM(lp) ((int)(short)LOWORD(lp))
#define GET_Y_LPARAM(lp) ((int)(short)HIWORD(lp))
#endif

typedef BOOL (WINAPI *PFN_GetTouchInputInfo)(HANDLE, UINT, LPVOID, int);
typedef BOOL (WINAPI *PFN_CloseTouchInputHandle)(HANDLE);
typedef BOOL (WINAPI *PFN_RegisterTouchWindow)(HWND, ULONG);

static PFN_GetTouchInputInfo    real_GetTouchInputInfo;
static PFN_CloseTouchInputHandle real_CloseTouchInputHandle;
static PFN_RegisterTouchWindow  real_RegisterTouchWindow;

/* Any non-NULL value works; it only has to be recognisable to the hooks below. */
#define FAKE_TOUCH_HANDLE ((HANDLE)(ULONG_PTR)0x544F554348ULL)

typedef struct { int x; int y; DWORD flags; } TouchRec;
#define QUEUE_LEN 256
static TouchRec g_queue[QUEUE_LEN];
static volatile LONG g_head;
static volatile LONG g_tail;

static void enqueue(int x, int y, DWORD flags)
{
    LONG tail = g_tail;
    g_queue[tail % QUEUE_LEN].x = x;
    g_queue[tail % QUEUE_LEN].y = y;
    g_queue[tail % QUEUE_LEN].flags = flags;
    g_tail = tail + 1;
}

static int dequeue(TouchRec *out)
{
    LONG head = g_head;
    if (head >= g_tail)
        return 0;
    *out = g_queue[head % QUEUE_LEN];
    g_head = head + 1;
    return 1;
}

static BOOL WINAPI shim_GetTouchInputInfo(HANDLE handle, UINT count, LPVOID out, int size)
{
    TOUCHINPUT *ti;
    TouchRec rec;
    if (handle != FAKE_TOUCH_HANDLE)
        return real_GetTouchInputInfo ? real_GetTouchInputInfo(handle, count, out, size) : FALSE;
    if (!out || count < 1 || size < (int)sizeof(TOUCHINPUT))
        return FALSE;
    if (!dequeue(&rec)) {
        rec.x = 0;
        rec.y = 0;
        rec.flags = TOUCHEVENTF_UP;
    }
    ti = (TOUCHINPUT *)out;
    ZeroMemory(ti, sizeof(TOUCHINPUT));
    /* TOUCHINPUT coordinates are hundredths of a pixel, in screen space. */
    ti->x = rec.x * 100;
    ti->y = rec.y * 100;
    ti->dwID = 1;
    ti->dwFlags = rec.flags | TOUCHEVENTF_PRIMARY;
    ti->dwMask = TOUCHINPUTMASKF_CONTACTAREA;
    ti->dwTime = GetTickCount();
    ti->cxContact = 800;
    ti->cyContact = 800;
    return TRUE;
}

static BOOL WINAPI shim_CloseTouchInputHandle(HANDLE handle)
{
    if (handle == FAKE_TOUCH_HANDLE)
        return TRUE;
    return real_CloseTouchInputHandle ? real_CloseTouchInputHandle(handle) : TRUE;
}

static BOOL WINAPI shim_RegisterTouchWindow(HWND window, ULONG flags)
{
    return TRUE;
}

static WNDPROC g_original_proc;

static void send_touch(HWND window, LPARAM lparam, DWORD flags)
{
    POINT point;
    point.x = GET_X_LPARAM(lparam);
    point.y = GET_Y_LPARAM(lparam);
    ClientToScreen(window, &point);
    enqueue(point.x, point.y, flags);
    PostMessageW(window, WM_TOUCH, 1, (LPARAM)FAKE_TOUCH_HANDLE);
}

static LRESULT CALLBACK shim_window_proc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    switch (message) {
    case WM_LBUTTONDOWN:
        send_touch(window, lparam, TOUCHEVENTF_DOWN);
        break;
    case WM_MOUSEMOVE:
        if (wparam & MK_LBUTTON)
            send_touch(window, lparam, TOUCHEVENTF_MOVE);
        break;
    case WM_LBUTTONUP:
        send_touch(window, lparam, TOUCHEVENTF_UP);
        break;
    default:
        break;
    }
    return CallWindowProcW(g_original_proc, window, message, wparam, lparam);
}

static DWORD g_pid;
static HWND g_found;

static BOOL CALLBACK find_window_proc(HWND window, LPARAM lparam)
{
    DWORD pid = 0;
    RECT rect;
    GetWindowThreadProcessId(window, &pid);
    if (pid != g_pid || !IsWindowVisible(window))
        return TRUE;
    if (GetWindow(window, GW_OWNER) != NULL)
        return TRUE;
    if (!GetClientRect(window, &rect))
        return TRUE;
    if ((rect.right - rect.left) < 200 || (rect.bottom - rect.top) < 150)
        return TRUE;
    g_found = window;
    return FALSE;
}

static DWORD WINAPI attach_thread(LPVOID param)
{
    int attempt;
    (void)param;
    for (attempt = 0; attempt < 900; attempt++) {
        g_found = NULL;
        EnumWindows(find_window_proc, 0);
        if (g_found) {
            g_original_proc = (WNDPROC)SetWindowLongPtrW(g_found, GWLP_WNDPROC, (LONG_PTR)shim_window_proc);
            return 0;
        }
        Sleep(100);
    }
    return 0;
}

static void patch_imports(HMODULE module)
{
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)module;
    IMAGE_NT_HEADERS *nt;
    IMAGE_IMPORT_DESCRIPTOR *desc;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        return;
    nt = (IMAGE_NT_HEADERS *)((BYTE *)module + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE)
        return;
    desc = (IMAGE_IMPORT_DESCRIPTOR *)((BYTE *)module +
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
    for (; desc->Name; desc++) {
        IMAGE_THUNK_DATA *thunk = (IMAGE_THUNK_DATA *)((BYTE *)module + desc->FirstThunk);
        for (; thunk->u1.Function; thunk++) {
            FARPROC *slot = (FARPROC *)&thunk->u1.Function;
            FARPROC replacement = NULL;
            DWORD old_protect;
            if (*slot == (FARPROC)real_GetTouchInputInfo)
                replacement = (FARPROC)shim_GetTouchInputInfo;
            else if (*slot == (FARPROC)real_CloseTouchInputHandle)
                replacement = (FARPROC)shim_CloseTouchInputHandle;
            else if (*slot == (FARPROC)real_RegisterTouchWindow)
                replacement = (FARPROC)shim_RegisterTouchWindow;
            if (!replacement)
                continue;
            if (VirtualProtect(slot, sizeof(slot), PAGE_READWRITE, &old_protect)) {
                *slot = replacement;
                VirtualProtect(slot, sizeof(slot), old_protect, &old_protect);
            }
        }
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    HANDLE thread;
    HMODULE user32;
    (void)reserved;
    if (reason != DLL_PROCESS_ATTACH)
        return TRUE;
    DisableThreadLibraryCalls(instance);

    user32 = GetModuleHandleW(L"user32.dll");
    if (!user32)
        return TRUE;
    real_GetTouchInputInfo     = (PFN_GetTouchInputInfo)GetProcAddress(user32, "GetTouchInputInfo");
    real_CloseTouchInputHandle = (PFN_CloseTouchInputHandle)GetProcAddress(user32, "CloseTouchInputHandle");
    real_RegisterTouchWindow   = (PFN_RegisterTouchWindow)GetProcAddress(user32, "RegisterTouchWindow");

    g_pid = GetCurrentProcessId();
    patch_imports(GetModuleHandleW(NULL));

    thread = CreateThread(NULL, 0, attach_thread, NULL, 0, NULL);
    if (thread)
        CloseHandle(thread);
    return TRUE;
}
