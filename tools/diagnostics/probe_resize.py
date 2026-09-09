import ctypes as c
import time
import json
import argparse
from ctypes import wintypes as w

u = c.WinDLL('user32', use_last_error=True)
u.SetProcessDpiAwarenessContext.argtypes = [w.HANDLE]
u.SetProcessDpiAwarenessContext(w.HANDLE(-4))
u.GetWindowLongPtrW.argtypes = [w.HWND, c.c_int]
u.GetWindowLongPtrW.restype = c.c_ssize_t
u.SetWindowLongPtrW.argtypes = [w.HWND, c.c_int, c.c_ssize_t]
u.SetWindowLongPtrW.restype = c.c_ssize_t
u.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
u.GetWindowRect.argtypes = [w.HWND, c.POINTER(w.RECT)]
u.GetClientRect.argtypes = [w.HWND, c.POINTER(w.RECT)]
u.GetDpiForWindow.argtypes = [w.HWND]
u.SetWindowPos.argtypes = [w.HWND, w.HWND, c.c_int, c.c_int, c.c_int, c.c_int, w.UINT]
u.AdjustWindowRectExForDpi.argtypes = [c.POINTER(w.RECT), w.DWORD, w.BOOL, w.DWORD, w.UINT]
u.SendMessageTimeoutW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM, w.UINT, w.UINT, c.POINTER(c.c_size_t)]
u.SendMessageTimeoutW.restype = w.LPARAM

def send(h, msg, wp=0, lp=0):
    result = c.c_size_t()
    if not u.SendMessageTimeoutW(h, msg, wp, lp, 2, 1000, c.byref(result)):
        raise c.WinError(c.get_last_error())
    return result.value

def rect(h, client=False):
    r = w.RECT()
    if not (u.GetClientRect if client else u.GetWindowRect)(h, c.byref(r)):
        raise c.WinError(c.get_last_error())
    return [r.left, r.top, r.right-r.left, r.bottom-r.top]

found = []
@c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
def visit(h, _):
    s = c.create_unicode_buffer(512)
    u.GetWindowTextW(h, s, 512)
    if 'Embodiment of Scarlet Devil' in s.value:
        found.append((h, s.value))
    return True

u.EnumWindows(visit, 0)
parser = argparse.ArgumentParser()
parser.add_argument('--game', choices=['classic', 'nc'])
parser.add_argument('--hold', type=float, default=.5)
args = parser.parse_args()
for h, title in found:
    if args.game and (('New Classic' in title) != (args.game == 'nc')):
        continue
    original = rect(h)
    initial_client = rect(h, True)
    style = u.GetWindowLongPtrW(h, -16) & 0xffffffff
    exstyle = u.GetWindowLongPtrW(h, -20) & 0xffffffff
    desired = (1600, 900) if 'New Classic' in title else (1600, 1200)
    report = {'title': title, 'original': original, 'tests': []}
    try:
        for name, newstyle in [('sizing-message', style)]:
            c.set_last_error(0)
            previous = u.SetWindowLongPtrW(h, -16, newstyle)
            o = w.RECT(0, 0, *desired)
            u.AdjustWindowRectExForDpi(c.byref(o), newstyle, False, exstyle, u.GetDpiForWindow(h) or 96)
            send(h, 0x231)
            sizing = w.RECT(original[0], original[1], original[0]+o.right-o.left, original[1]+o.bottom-o.top)
            send(h, 0x214, 8, c.addressof(sizing))
            ok = u.SetWindowPos(h, None, original[0], original[1], o.right-o.left, o.bottom-o.top, 0x434)
            send(h, 0x232)
            send(h, 5, 0, desired[0] | (desired[1] << 16))
            if 'New Classic' in title:
                original_sizing = w.RECT(original[0], original[1], original[0]+original[2], original[1]+original[3])
                send(h, 0x214, 8, c.addressof(original_sizing))
            r = {'method': name, 'ok': bool(ok), 'error': c.get_last_error(), 'immediate': rect(h, True)}
            print(json.dumps({'phase':'holding', 'title':title, 'client':rect(h,True)}), flush=True)
            time.sleep(args.hold)
            r['after_hold'] = rect(h, True)
            report['tests'].append(r)
    finally:
        send(h, 0x231)
        sizing = w.RECT(original[0], original[1], original[0]+original[2], original[1]+original[3])
        send(h, 0x214, 8, c.addressof(sizing))
        u.SetWindowLongPtrW(h, -16, style)
        u.SetWindowPos(h, None, *original, 0x434)
        send(h, 0x232)
        restored_client = rect(h, True)
        send(h, 5, 0, restored_client[2] | (restored_client[3] << 16))
        time.sleep(.2)
        report['restored'] = rect(h)
        report['style_restored'] = (u.GetWindowLongPtrW(h, -16) & 0xffffffff) == style
    print(json.dumps(report, ensure_ascii=True), flush=True)
