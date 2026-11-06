// =============================================================================
//  hook.c  ——  QQNT「resources\app\package.json 读取重定向」hook DLL
// -----------------------------------------------------------------------------
//  运行形态：
//      QQ.exe  --(静态导入)-->  winmm.dll (我们自己的代理)
//                                └─(DllMain 里 LoadLibraryW 同目录)--> dbghelp.dll
//                                                                        └─ 就是本 DLL
//  本 DLL 只被「加载」而不被「调用」，DllMain 里只 CreateThread，真正的活儿
//  （读配置 / 判角色 / 装 MinHook）全部在这个 worker 线程里做，绝不在 DllMain
//  里做重活、绝不 LoadLibrary。
//
//  设计要点（与上级设计一致）：
//   1) 进程角色判定：读 GetCommandLineW，只有「主浏览器进程」生效。
//      - 含 "--type="          → 渲染/GPU/utility 子进程，放行
//      - 含 "--exApp"          → 频道等 exApp 子进程，放行
//      - 含 "loadapp=exApp"    → 腾讯文档等 exApp 子进程，放行
//      其它进程一律「不装 hook」，于是 exApp 自然读到原版 package.json。
//   2) 只挂一个 CreateFileW（MinHook）。命中「...\resources\app\package.json」
//      时：
//        probe 模式  → 只写日志（序号、调用方模块+偏移、完整栈、参数、结果）
//        active 模式 → 按配置判定「这一次」是否要换成我们的 patched 文件，
//                      要换就调用真正的 CreateFileW 打开
//                      <同目录>\package.patched.json（或 LIULI_PKG_PATCHED），
//                      把那个句柄原样返回给调用方（不 hook ReadFile）。
//   3) 任何异常都必须「放行原版」：所有自身代码都包在 __try 或判空里，
//      日志失败、配置缺失、patched 文件不存在、打开失败……统统 fallback。
//   4) 日志：C:\liuli-b2\hook.log（UTF-8，追加，带时间戳/进程名/pid/tid/开机毫秒）。
//   5) 配置文件查找顺序（取第一个存在的，见 load_cfg_file）：
//        ① 环境变量 LIULI_CFG 指定的路径 —— 最高优先，调试时用来覆盖
//        ② 本 DLL（dbghelp.dll）同目录的 hook.ini —— 产品化正式位置（QQ 根目录）
//        ③ C:\liuli-b2\hook.ini —— 旧调试路径，兼容保留
//      三个都找不到 → 无配置 → 默认 probe（安全优先）。
//
//  编译：
//      build.cmd 生成两个 DLL（同一份源码）：
//        dbghelp.probe.dll  —— /DLIULI_PROBE=1，永远只记日志，绝对不改写
//        dbghelp.dll        —— 由上面找到的 hook.ini / 环境变量决定行为；
//                              没有配置时默认也是 probe（安全优先）
//
//  可选宏：
//      LIULI_PROBE        强制探针模式（忽略配置里的 mode）
//      LIULI_EXTRA_SENSORS 额外挂两个「只读传感器」：CreateFile2 和
//                         GetFileAttributesExW，只为探针补盲（不改写）
//      LIULI_NO_DBGHELP_FWD 去掉下面那批 dbghelp 转发导出
//
//  MinHook: https://github.com/TsudaKageyu/minhook （BSD-2-Clause）
//  本文件中的导出转发（/export:xxx=C:\Windows\System32\dbghelp.xxx）是为了
//  万一有代码把我们的 dbghelp.dll 当真 dbghelp 用（QQ 崩溃上报之类）时，
//  GetProcAddress 仍能拿到真正的系统实现，避免把 QQ 搞崩。写法与现有
//  winmm 代理完全一致。
// =============================================================================

#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0A00
#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif
#ifndef _CRT_NONSTDC_NO_DEPRECATE
#define _CRT_NONSTDC_NO_DEPRECATE
#endif

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <wchar.h>
#include <string.h>
#include <intrin.h>
#include <tlhelp32.h>   // 自保护模块按名字找 wrapper.node（CreateToolhelp32Snapshot）

#include "minhook/include/MinHook.h"

// -----------------------------------------------------------------------------
// 1. dbghelp 常用导出转发（懒解析：只有别人 GetProcAddress 到才会去加载系统那份）
// -----------------------------------------------------------------------------
#ifndef LIULI_NO_DBGHELP_FWD
#pragma comment(linker, "/export:SymInitialize=C:\\Windows\\System32\\dbghelp.SymInitialize")
#pragma comment(linker, "/export:SymInitializeW=C:\\Windows\\System32\\dbghelp.SymInitializeW")
#pragma comment(linker, "/export:SymCleanup=C:\\Windows\\System32\\dbghelp.SymCleanup")
#pragma comment(linker, "/export:SymFromAddr=C:\\Windows\\System32\\dbghelp.SymFromAddr")
#pragma comment(linker, "/export:SymFromName=C:\\Windows\\System32\\dbghelp.SymFromName")
#pragma comment(linker, "/export:SymFromToken=C:\\Windows\\System32\\dbghelp.SymFromToken")
#pragma comment(linker, "/export:SymFromIndex=C:\\Windows\\System32\\dbghelp.SymFromIndex")
#pragma comment(linker, "/export:SymFromInlineContext=C:\\Windows\\System32\\dbghelp.SymFromInlineContext")
#pragma comment(linker, "/export:SymGetModuleBase=C:\\Windows\\System32\\dbghelp.SymGetModuleBase")
#pragma comment(linker, "/export:SymGetModuleBase64=C:\\Windows\\System32\\dbghelp.SymGetModuleBase64")
#pragma comment(linker, "/export:SymGetOptions=C:\\Windows\\System32\\dbghelp.SymGetOptions")
#pragma comment(linker, "/export:SymSetOptions=C:\\Windows\\System32\\dbghelp.SymSetOptions")
#pragma comment(linker, "/export:SymLoadModuleEx=C:\\Windows\\System32\\dbghelp.SymLoadModuleEx")
#pragma comment(linker, "/export:SymLoadModuleExW=C:\\Windows\\System32\\dbghelp.SymLoadModuleExW")
#pragma comment(linker, "/export:SymUnloadModule=C:\\Windows\\System32\\dbghelp.SymUnloadModule")
#pragma comment(linker, "/export:SymUnloadModule64=C:\\Windows\\System32\\dbghelp.SymUnloadModule64")
#pragma comment(linker, "/export:SymRefreshModuleList=C:\\Windows\\System32\\dbghelp.SymRefreshModuleList")
#pragma comment(linker, "/export:SymFunctionTableAccess=C:\\Windows\\System32\\dbghelp.SymFunctionTableAccess")
#pragma comment(linker, "/export:SymFunctionTableAccess64=C:\\Windows\\System32\\dbghelp.SymFunctionTableAccess64")
#pragma comment(linker, "/export:SymGetLineFromAddr64=C:\\Windows\\System32\\dbghelp.SymGetLineFromAddr64")
#pragma comment(linker, "/export:SymGetLineFromAddrW64=C:\\Windows\\System32\\dbghelp.SymGetLineFromAddrW64")
#pragma comment(linker, "/export:SymGetLineNext64=C:\\Windows\\System32\\dbghelp.SymGetLineNext64")
#pragma comment(linker, "/export:SymGetLinePrev64=C:\\Windows\\System32\\dbghelp.SymGetLinePrev64")
#pragma comment(linker, "/export:SymGetSymFromAddr64=C:\\Windows\\System32\\dbghelp.SymGetSymFromAddr64")
#pragma comment(linker, "/export:SymGetSymFromName64=C:\\Windows\\System32\\dbghelp.SymGetSymFromName64")
#pragma comment(linker, "/export:StackWalk=C:\\Windows\\System32\\dbghelp.StackWalk")
#pragma comment(linker, "/export:StackWalk64=C:\\Windows\\System32\\dbghelp.StackWalk64")
#pragma comment(linker, "/export:StackWalkEx=C:\\Windows\\System32\\dbghelp.StackWalkEx")
#pragma comment(linker, "/export:UnDecorateSymbolName=C:\\Windows\\System32\\dbghelp.UnDecorateSymbolName")
#pragma comment(linker, "/export:UnDecorateSymbolNameW=C:\\Windows\\System32\\dbghelp.UnDecorateSymbolNameW")
#pragma comment(linker, "/export:SymGetSearchPath=C:\\Windows\\System32\\dbghelp.SymGetSearchPath")
#pragma comment(linker, "/export:SymGetSearchPathW=C:\\Windows\\System32\\dbghelp.SymGetSearchPathW")
#pragma comment(linker, "/export:SymSetSearchPath=C:\\Windows\\System32\\dbghelp.SymSetSearchPath")
#pragma comment(linker, "/export:SymSetSearchPathW=C:\\Windows\\System32\\dbghelp.SymSetSearchPathW")
#pragma comment(linker, "/export:SymEnumerateModules64=C:\\Windows\\System32\\dbghelp.SymEnumerateModules64")
#pragma comment(linker, "/export:SymEnumerateModulesW64=C:\\Windows\\System32\\dbghelp.SymEnumerateModulesW64")
#pragma comment(linker, "/export:SymGetTypeInfo=C:\\Windows\\System32\\dbghelp.SymGetTypeInfo")
#pragma comment(linker, "/export:SymGetTypeInfoEx=C:\\Windows\\System32\\dbghelp.SymGetTypeInfoEx")
#pragma comment(linker, "/export:SymGetModuleInfo64=C:\\Windows\\System32\\dbghelp.SymGetModuleInfo64")
#pragma comment(linker, "/export:SymGetModuleInfoW64=C:\\Windows\\System32\\dbghelp.SymGetModuleInfoW64")
#pragma comment(linker, "/export:SymSetContext=C:\\Windows\\System32\\dbghelp.SymSetContext")
#pragma comment(linker, "/export:SymFindFileInPath=C:\\Windows\\System32\\dbghelp.SymFindFileInPath")
#pragma comment(linker, "/export:SymFindFileInPathW=C:\\Windows\\System32\\dbghelp.SymFindFileInPathW")
#pragma comment(linker, "/export:SymGetHomeDirectoryW=C:\\Windows\\System32\\dbghelp.SymGetHomeDirectoryW")
#pragma comment(linker, "/export:SymSetHomeDirectoryW=C:\\Windows\\System32\\dbghelp.SymSetHomeDirectoryW")
#pragma comment(linker, "/export:SymGetSourceFileW=C:\\Windows\\System32\\dbghelp.SymGetSourceFileW")
#pragma comment(linker, "/export:SymEnumSymbols=C:\\Windows\\System32\\dbghelp.SymEnumSymbols")
#pragma comment(linker, "/export:SymEnumSymbolsW=C:\\Windows\\System32\\dbghelp.SymEnumSymbolsW")
#pragma comment(linker, "/export:SymEnumTypes=C:\\Windows\\System32\\dbghelp.SymEnumTypes")
#pragma comment(linker, "/export:SymEnumTypesW=C:\\Windows\\System32\\dbghelp.SymEnumTypesW")
#pragma comment(linker, "/export:SymMatchFileName=C:\\Windows\\System32\\dbghelp.SymMatchFileName")
#pragma comment(linker, "/export:SymMatchString=C:\\Windows\\System32\\dbghelp.SymMatchString")
#pragma comment(linker, "/export:SymRegisterCallback64=C:\\Windows\\System32\\dbghelp.SymRegisterCallback64")
#pragma comment(linker, "/export:SymRegisterCallbackW64=C:\\Windows\\System32\\dbghelp.SymRegisterCallbackW64")
#pragma comment(linker, "/export:ImagehlpApiVersion=C:\\Windows\\System32\\dbghelp.ImagehlpApiVersion")
#pragma comment(linker, "/export:ImagehlpApiVersionEx=C:\\Windows\\System32\\dbghelp.ImagehlpApiVersionEx")
#pragma comment(linker, "/export:MakeSureDirectoryPathExists=C:\\Windows\\System32\\dbghelp.MakeSureDirectoryPathExists")
#pragma comment(linker, "/export:SearchTreeForFile=C:\\Windows\\System32\\dbghelp.SearchTreeForFile")
#pragma comment(linker, "/export:SearchTreeForFileW=C:\\Windows\\System32\\dbghelp.SearchTreeForFileW")
#pragma comment(linker, "/export:SymGetSymbolFile=C:\\Windows\\System32\\dbghelp.SymGetSymbolFile")
#pragma comment(linker, "/export:SymGetTypeFromName=C:\\Windows\\System32\\dbghelp.SymGetTypeFromName")
#pragma comment(linker, "/export:SymSrvGetFileIndexInfo=C:\\Windows\\System32\\dbghelp.SymSrvGetFileIndexInfo")
#pragma comment(linker, "/export:SymSrvStoreFile=C:\\Windows\\System32\\dbghelp.SymSrvStoreFile")
#pragma comment(linker, "/export:SymSetParentWindow=C:\\Windows\\System32\\dbghelp.SymSetParentWindow")
#pragma comment(linker, "/export:SymAddSourceStream=C:\\Windows\\System32\\dbghelp.SymAddSourceStream")
#pragma comment(linker, "/export:SymEnumSourceFiles=C:\\Windows\\System32\\dbghelp.SymEnumSourceFiles")
#pragma comment(linker, "/export:SymEnumSourceLines=C:\\Windows\\System32\\dbghelp.SymEnumSourceLines")
#endif // LIULI_NO_DBGHELP_FWD

// -----------------------------------------------------------------------------
// 2. 常量与全局状态
// -----------------------------------------------------------------------------
#define LIULI_VER            L"1.0-b2"
#define LOG_PATH_DEFAULT     L"C:\\liuli-b2\\hook.log"
#define CFG_PATH_DEFAULT     L"C:\\liuli-b2\\hook.ini"
#define LOG_DIR_DEFAULT      L"C:\\liuli-b2"
#define PATCHED_NAME         L"package.patched.json"
#define MAX_CONDS            8
#define MAX_STACK_FRAMES     8
#define LOG_BUDGET_MAX       20000   // 单进程最多写多少条 hit 日志，防止刷爆磁盘

typedef HANDLE (WINAPI *PFN_CreateFileW)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES,
                                        DWORD, DWORD, HANDLE);

typedef enum {
    MODE_PROBE = 0,      // 只记日志
    MODE_ACTIVE = 1,     // 按条件改写
    MODE_OFF = 2         // 连 hook 都不装，只记一条启动日志
} LIULI_MODE;

typedef enum {
    COND_ALL = 0,
    COND_SEQ_EQ,
    COND_SEQ_GE,
    COND_TID,
    COND_MODULE,          // 直接调用方模块名包含子串
    COND_STACK_MODULE,    // 栈上任意一帧在指定模块
    COND_ADDR,            // 直接调用方 == 模块+偏移
    COND_SKIP_FULLREADS,  // 数「全文读」次数：前 N 次放行，第 N+1 次起匹配（不依赖偏移）【旧语义，保留作回滚】
    COND_VERIFIER,        // 识别「这是不是 QQ 的完整性校验读」：是 → 放行原版；不是 → 匹配（见第 9a 节）
    COND_NEVER            // 未知/写错的条件：恒假（宁可不生效也不能乱改）
} COND_TYPE;

typedef struct {
    COND_TYPE type;
    BOOL      negate;
    int       ival;
    ULONG_PTR offset;
    wchar_t   sval[64];
} COND;

static HMODULE        g_self          = NULL;
static wchar_t        g_self_path[MAX_PATH] = L"";  // 本 DLL 完整路径（worker 早期求出来）
static DWORD          g_pid           = 0;
static DWORD          g_init_tid      = 0;
static wchar_t        g_proc_name[MAX_PATH] = L"?";
static wchar_t        g_log_path[MAX_PATH]  = LOG_PATH_DEFAULT;
static wchar_t        g_cfg_path[MAX_PATH]  = CFG_PATH_DEFAULT;
static wchar_t        g_cfg_self[MAX_PATH]  = L"";  // 候选②：本 DLL 同目录\hook.ini
static const wchar_t *g_cfg_src        = L"none";   // 实际命中的规则：env/self/default/none
static int            g_cfg_env_miss   = 0;         // LIULI_CFG 给了路径但打不开（继续往下找）
static wchar_t        g_patched[MAX_PATH]   = L"";   // 空 = 用「被请求文件同目录\package.patched.json」
static int            g_mode          = MODE_PROBE;
static int            g_force         = 0;          // 1 = 跳过进程角色判定（仅调试）
static int            g_init_delay_ms = 0;
static int            g_dump_cmdline  = 1;
static COND           g_conds[MAX_CONDS];
static int            g_cond_count    = 0;
static volatile LONG  g_hit_seq       = 0;
static volatile LONG  g_fullread_seq  = 0;   // 只数「全文读」，属性读不自增（见 skipfullreads:N）
static volatile LONG  g_verifier_seq  = 0;   // 只数「校验读」（原始 acc == 0x80000000），见第 9a 节的 verifier:N
static volatile LONG  g_log_budget    = LOG_BUDGET_MAX;
static PFN_CreateFileW g_orig         = NULL;
static DWORD          g_tls_slot      = TLS_OUT_OF_INDEXES;
static ULONGLONG      g_proc_start    = 0;          // 进程创建时间（FILETIME）
static INIT_ONCE      g_log_once      = INIT_ONCE_STATIC_INIT;
static CRITICAL_SECTION g_log_cs;
static INIT_ONCE      g_dir_once      = INIT_ONCE_STATIC_INIT;
static volatile LONG  g_installed     = 0;
static ULONG_PTR      g_hook_target   = 0;

#if LIULI_EXTRA_SENSORS
typedef HANDLE (WINAPI *PFN_CreateFile2)(LPCWSTR, DWORD, DWORD, DWORD,
                                         LPCREATEFILE2_EXTENDED_PARAMETERS);
typedef BOOL   (WINAPI *PFN_GetFileAttributesExW)(LPCWSTR, GET_FILEEX_INFO_LEVELS, LPVOID);
static PFN_CreateFile2          g_orig_cf2 = NULL;
static PFN_GetFileAttributesExW g_orig_gfae = NULL;
#endif

// RtlCaptureStackBackTrace 走 GetProcAddress，避免额外链接 ntdll.lib
typedef USHORT (WINAPI *PFN_RtlCaptureStackBackTrace)(ULONG, ULONG, PVOID *, PULONG);
static PFN_RtlCaptureStackBackTrace g_capture_stack = NULL;

// TLS 重入守卫（实现在本文件后半段，detour 里要用，先声明）
static void tls_guard_on(void);
static void tls_guard_off(void);
static BOOL tls_guard_hit(void);

// -----------------------------------------------------------------------------
// 3. 小工具：大小写不敏感比较、路径判定、时间
// -----------------------------------------------------------------------------
static WCHAR lower_w(WCHAR c)
{
    return (c >= L'A' && c <= L'Z') ? (WCHAR)(c + 32) : c;
}

// '/' 与 '\' 视为等价，方便匹配 Node 可能给出的正斜杠路径
static WCHAR norm_sep(WCHAR c)
{
    return (c == L'/') ? L'\\' : c;
}

static BOOL ends_with_ci(const wchar_t *s, const wchar_t *suffix)
{
    size_t n, sl;
    if (!s || !suffix) return FALSE;
    n  = wcslen(s);
    sl = wcslen(suffix);
    if (n < sl) return FALSE;
    s += (n - sl);
    while (sl--) {
        if (lower_w(norm_sep(s[sl])) != lower_w(norm_sep(suffix[sl]))) return FALSE;
    }
    return TRUE;
}

// 命中判定：路径必须以 resources\app\package.json 结尾（大小写/分隔符不敏感）。
// 相对路径 "resources\app\package.json" 也算命中。
static BOOL is_target_pkg_path(const wchar_t *path)
{
    static const wchar_t kSuffix[] = L"resources\\app\\package.json";
    size_t n, sl, i;

    if (!path || !*path) return FALSE;
    if (!ends_with_ci(path, L"package.json")) return FALSE;   // 便宜的快速拒绝
    n  = wcslen(path);
    sl = wcslen(kSuffix);
    if (n < sl) return FALSE;
    path += (n - sl);
    for (i = 0; i < sl; ++i) {
        if (lower_w(norm_sep(path[i])) != lower_w(norm_sep(kSuffix[i]))) return FALSE;
    }
    return TRUE;
}

// 从「被请求的 package.json」推出同目录的 package.patched.json
static BOOL derive_patched_path(const wchar_t *requested, wchar_t *out, size_t cch)
{
    const wchar_t *p, *last_sep = NULL;
    size_t dirlen;

    if (!requested || !out || cch < 32) return FALSE;
    for (p = requested; *p; ++p) {
        if (*p == L'\\' || *p == L'/') last_sep = p;
    }
    dirlen = last_sep ? (size_t)(last_sep - requested + 1) : 0;
    if (dirlen + wcslen(PATCHED_NAME) + 1 > cch) return FALSE;
    if (dirlen) wmemcpy(out, requested, dirlen);
    out[dirlen] = 0;
    wcscat_s(out, cch, PATCHED_NAME);
    return TRUE;
}

static void cache_proc_start(void)
{
    FILETIME c, e, k, u;
    ULARGE_INTEGER li;
    if (GetProcessTimes(GetCurrentProcess(), &c, &e, &k, &u)) {
        li.LowPart  = c.dwLowDateTime;
        li.HighPart = (LONG)c.dwHighDateTime;
        g_proc_start = li.QuadPart;
    }
}

// 进程已运行毫秒数：日志里用来判断「hook 装好时进程跑到哪一步了」
static ULONGLONG uptime_ms(void)
{
    FILETIME now;
    ULARGE_INTEGER li;
    ULONGLONG d;

    if (!g_proc_start) return 0;
    GetSystemTimeAsFileTime(&now);
    li.LowPart  = now.dwLowDateTime;
    li.HighPart = (LONG)now.dwHighDateTime;
    d = (li.QuadPart > g_proc_start) ? (li.QuadPart - g_proc_start) : 0;
    return d / 10000ULL;
}

static void cache_proc_name(void)
{
    wchar_t full[MAX_PATH];
    DWORD n;
    const wchar_t *p, *base = full;

    g_pid = GetCurrentProcessId();
    n = GetModuleFileNameW(NULL, full, MAX_PATH);
    if (n == 0) { wcscpy_s(g_proc_name, MAX_PATH, L"?"); return; }
    full[n] = 0;
    for (p = full; *p; ++p) {
        if (*p == L'\\' || *p == L'/') base = p + 1;
    }
    wcsncpy_s(g_proc_name, MAX_PATH, base, _TRUNCATE);
}

// -----------------------------------------------------------------------------
// 4. 日志：UTF-8 追加写 C:\liuli-b2\hook.log
// -----------------------------------------------------------------------------
static BOOL CALLBACK log_cs_init(PINIT_ONCE once, PVOID param, PVOID *ctx)
{
    (void)once; (void)param; (void)ctx;
    InitializeCriticalSection(&g_log_cs);
    return TRUE;
}

static BOOL CALLBACK mkdir_once(PINIT_ONCE once, PVOID param, PVOID *ctx)
{
    wchar_t dir[MAX_PATH];
    const wchar_t *p, *last_sep = NULL;

    (void)once; (void)param; (void)ctx;
    // 默认日志路径才去建 C:\liuli-b2；日志被改到别处时不污染系统盘根目录
    if (lstrcmpiW(g_log_path, LOG_PATH_DEFAULT) == 0) {
        CreateDirectoryW(L"C:\\liuli-b2", NULL);
        CreateDirectoryW(LOG_DIR_DEFAULT, NULL);
    }

    // 日志被配置到别处时，也顺手把它所在的目录建出来
    if (g_log_path[0]) {
        wcsncpy_s(dir, _countof(dir), g_log_path, _TRUNCATE);
        for (p = dir; *p; ++p) {
            if (*p == L'\\' || *p == L'/') last_sep = p;
        }
        if (last_sep && last_sep > dir + 2) {
            dir[last_sep - dir] = 0;
            CreateDirectoryW(dir, NULL);
        }
    }
    return TRUE;
}

// 用「真正的」CreateFileW 打开日志，绝不再走 detour，避免递归
static HANDLE open_log_file(void)
{
    DWORD acc   = FILE_APPEND_DATA;
    DWORD share = FILE_SHARE_READ | FILE_SHARE_WRITE;
    if (g_orig) {
        return g_orig(g_log_path, acc, share, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    }
    return CreateFileW(g_log_path, acc, share, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
}

static void raw_log_w(const wchar_t *text)
{
    int blen;
    char *buf;
    HANDLE h;
    DWORD written = 0;

    if (!text) return;
    InitOnceExecuteOnce(&g_dir_once, mkdir_once, NULL, NULL);
    InitOnceExecuteOnce(&g_log_once, log_cs_init, NULL, NULL);

    blen = WideCharToMultiByte(CP_UTF8, 0, text, -1, NULL, 0, NULL, NULL);
    if (blen <= 0) return;
    buf = (char *)HeapAlloc(GetProcessHeap(), 0, (SIZE_T)blen + 3);
    if (!buf) return;
    WideCharToMultiByte(CP_UTF8, 0, text, -1, buf, blen, NULL, NULL);

    // 换行：CRLF，记事本 / Git Bash / PowerShell 都友好
    buf[blen - 1] = '\r';
    buf[blen]     = '\n';
    buf[blen + 1] = 0;

    EnterCriticalSection(&g_log_cs);
    h = open_log_file();
    if (h != INVALID_HANDLE_VALUE) {
        WriteFile(h, buf, (DWORD)strlen(buf), &written, NULL);
        CloseHandle(h);
    }
    LeaveCriticalSection(&g_log_cs);

    HeapFree(GetProcessHeap(), 0, buf);
}

// logf：统一前缀（本地时间戳 / pid / tid / 进程名 / 进程运行毫秒）
static void liuli_log(const wchar_t *fmt, ...)
{
    // 缓冲区别开太大：这个函数在任意线程（包括栈很深的 Node/libuv 线程）上跑
    wchar_t body[2048];
    wchar_t line[2300];
    SYSTEMTIME st;
    va_list ap;

    if (InterlockedDecrement(&g_log_budget) < 0) return;

    va_start(ap, fmt);
    _vsnwprintf_s(body, _countof(body), _TRUNCATE, fmt, ap);
    va_end(ap);

    GetLocalTime(&st);
    _snwprintf_s(line, _countof(line), _TRUNCATE,
                 L"%04u-%02u-%02u %02u:%02u:%02u.%03u pid=%lu tid=%lu proc=%s up=%llums %s",
                 (unsigned)st.wYear, (unsigned)st.wMonth, (unsigned)st.wDay,
                 (unsigned)st.wHour, (unsigned)st.wMinute, (unsigned)st.wSecond,
                 (unsigned)st.wMilliseconds,
                 (unsigned long)g_pid, (unsigned long)GetCurrentThreadId(),
                 g_proc_name, (unsigned long long)uptime_ms(),
                 body);
    raw_log_w(line);
}

// -----------------------------------------------------------------------------
// 5. 模块 / 地址描述
// -----------------------------------------------------------------------------
static HMODULE resolve_addr(ULONG_PTR addr, wchar_t *name, size_t cch,
                            ULONG_PTR *base, ULONG_PTR *off)
{
    HMODULE mod = NULL;
    wchar_t full[MAX_PATH];
    DWORD n;
    const wchar_t *p, *b = full;

    if (name && cch) name[0] = 0;
    if (base) *base = 0;
    if (off)  *off  = 0;
    if (!addr) return NULL;

    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            (LPCWSTR)addr, &mod) || !mod) {
        return NULL;
    }
    n = GetModuleFileNameW(mod, full, MAX_PATH);
    if (n == 0) return mod;
    full[n] = 0;
    for (p = full; *p; ++p) {
        if (*p == L'\\' || *p == L'/') b = p + 1;
    }
    if (name && cch) wcsncpy_s(name, cch, b, _TRUNCATE);
    if (base) *base = (ULONG_PTR)mod;
    if (off)  *off  = addr - (ULONG_PTR)mod;
    return mod;
}

// "模块名+0x偏移"；解不出来就打裸地址
static void describe_addr(ULONG_PTR addr, wchar_t *out, size_t cch)
{
    wchar_t name[MAX_PATH];
    ULONG_PTR base = 0, off = 0;
    HMODULE mod;

    if (!out || !cch) return;
    if (!addr) { wcsncpy_s(out, cch, L"-", _TRUNCATE); return; }

    mod = resolve_addr(addr, name, _countof(name), &base, &off);
    if (mod && name[0]) {
        _snwprintf_s(out, cch, _TRUNCATE, L"%s+0x%llX", name, (unsigned long long)off);
    } else {
        _snwprintf_s(out, cch, _TRUNCATE, L"?+0x%p", (void *)addr);
    }
}

// -----------------------------------------------------------------------------
// 6. 命中上下文（探针要记的东西全在这里）
// -----------------------------------------------------------------------------
typedef struct {
    int        seq;
    int        fullread_idx;      // 这是第几次「全文读」（1 起）；非全文读为 0
    int        verifier_idx;      // 这是第几次「校验读」（原始 acc==0x80000000，1 起）；不是校验读为 0
    DWORD      tid;
    DWORD      access;
    DWORD      share;
    DWORD      disp;
    DWORD      attr;
    ULONG_PTR  retaddr;
    wchar_t    caller[96];        // 直接调用方：「模块名+0x偏移」
    wchar_t    caller_mod[96];    // 直接调用方模块名
    ULONG_PTR  caller_off;
    wchar_t    stack[900];        // 栈帧拼接 "A.dll+0x1|B.dll+0x2|..."
    wchar_t    stack_mod[MAX_STACK_FRAMES][96];
    ULONG_PTR  stack_off[MAX_STACK_FRAMES];
    int        stack_n;
} HITCTX;

static void hit_add_frame(HITCTX *c, ULONG_PTR addr)
{
    wchar_t name[MAX_PATH];
    ULONG_PTR base = 0, off = 0;
    HMODULE mod;
    int i;
    wchar_t piece[160];

    if (!addr || c->stack_n >= MAX_STACK_FRAMES) return;
    mod = resolve_addr(addr, name, _countof(name), &base, &off);
    if (!name[0]) return;                 // 解不出模块的先不要（不干净的栈）
    if (mod && mod == g_self) return;     // 跳过本 DLL 自己的帧
    for (i = 0; i < c->stack_n; ++i) {
        if (c->stack_mod[i][0] && _wcsicmp(c->stack_mod[i], name) == 0 &&
            c->stack_off[i] == off) {
            return;                       // 去重
        }
    }
    wcsncpy_s(c->stack_mod[c->stack_n], 96, name, _TRUNCATE);
    c->stack_off[c->stack_n] = off;
    c->stack_n++;

    _snwprintf_s(piece, _countof(piece), _TRUNCATE, L"%s%s+0x%llX",
                 (c->stack[0] ? L"|" : L""), name, (unsigned long long)off);
    wcscat_s(c->stack, _countof(c->stack), piece);
}

static void hit_collect(HITCTX *c, ULONG_PTR retaddr, DWORD access, DWORD share,
                        DWORD disp, DWORD attr, int seq, int fullread_idx, int verifier_idx)
{
    int i;

    memset(c, 0, sizeof(*c));
    c->seq          = seq;
    c->fullread_idx = fullread_idx;
    c->verifier_idx = verifier_idx;
    c->tid      = GetCurrentThreadId();
    c->access   = access;
    c->share    = share;
    c->disp     = disp;
    c->attr     = attr;
    c->retaddr  = retaddr;

    // 第一帧：精确调用方（_ReturnAddress），后面是 RtlCaptureStackBackTrace
    hit_add_frame(c, retaddr);
    if (g_capture_stack) {
        PVOID frames[16];
        USHORT n = g_capture_stack(0, 16, frames, NULL);
        for (i = 0; i < (int)n && c->stack_n < MAX_STACK_FRAMES; ++i) {
            hit_add_frame(c, (ULONG_PTR)frames[i]);
        }
    }

    if (c->stack_n > 0) {
        wcsncpy_s(c->caller_mod, _countof(c->caller_mod), c->stack_mod[0], _TRUNCATE);
        c->caller_off = c->stack_off[0];
        describe_addr(retaddr, c->caller, _countof(c->caller));
    } else {
        describe_addr(retaddr, c->caller, _countof(c->caller));
    }
    if (!c->stack[0]) wcsncpy_s(c->stack, _countof(c->stack), L"-", _TRUNCATE);
}

// -----------------------------------------------------------------------------
// 6b. 自我保护（SP）模块：模式 / 目标位 / 状态
//     真正的实现（AOB 扫描、各 detour、init 入口）在本文件第 12b 节。
//     之所以把这些「状态」提前放在这里，是因为第 7 节的配置解析（apply_kv）要在
//     读 hook.ini 的当下就调用 sp_parse_mode / sp_parse_targets，保证这些值在
//     liuli_selfprotect_init() 被调用之前就已经就位。
// -----------------------------------------------------------------------------
#define SP_LOG_PREFIX        L"event=sp"

typedef enum {
    SP_MODE_OFF     = 0,   // 完全不介入
    SP_MODE_OBSERVE = 1,   // 只挂钩记录（默认）
    SP_MODE_APPLY   = 2    // 按 SP_TARGET 实际改写/拦截
} SP_MODE;

#define SP_T_SIGE      (1u << 0)   // wrapper.node [FGESDK] 文件扫描原语
#define SP_T_SIGD1     (1u << 1)   // wrapper.node [FGESDK] 模块/导出探测 #1
#define SP_T_SIGD2     (1u << 2)   // wrapper.node [FGESDK] 打点 #2
#define SP_T_SIGB      (1u << 3)   // QQNT.dll node 运行时函数（只观测！）
#define SP_T_MSGBOX    (1u << 4)   // MessageBoxW（识别「文件已损坏」）
#define SP_T_HANDLE    (1u << 5)   // 句柄溯源伪装（L1，常开）
#define SP_T_NTQUERY   (1u << 6)   // NtQueryInformationProcess（反调试探测）
#define SP_T_ENUM      (1u << 7)   // K32EnumProcessModules（隐藏自身模块）

// 历史上这里是"默认全观察"，但 sigE/sigD/sigB 三个 AOB 目标的**真实参数个数未知**：
// SP_DEFINE_DETOUR 用固定 4 参 orig(a1,a2,a3,a4) 透传，一旦真函数 >4 参，
// 第 5+ 个参数在栈上而 detour 转发时那格已经是新调用的栈帧，等于给原函数塞垃圾
// ——即使是 observe 模式也可能把 QQ 跑崩。所以默认**绝不挂**这三个 AOB。
// 需要时用配置显式打开（hook.ini 的 sp_targets= / 环境变量 LIULI_SP_TARGETS=）。
#define SP_T_ALL_OBSERVE  (SP_T_SIGE | SP_T_SIGD1 | SP_T_SIGD2 | SP_T_SIGB | \
                           SP_T_MSGBOX | SP_T_HANDLE)
#define SP_T_DEFAULT_APPLY 0u

// 期望 RVA：用来判断"是不是 9.9.36-53644 这个构建"，对不上就只 warn 不 apply
#define SP_EXP_SIGE_RVA    0x036FD870u
#define SP_EXP_SIGD1_RVA   0x02F2E430u
#define SP_EXP_SIGD2_RVA   0x03F55048u
#define SP_EXP_SIGB_RVA    0x00511150u

// 单个函数的命中记录（诊断用，不参与运行期逻辑）
typedef struct {
    const char *name;          // "sigE" / "sigD#1" ...
    const char *module;        // "wrapper.node" / "QQNT.dll"
    unsigned    flag;          // SP_T_xxx
    unsigned    expected_rva;  // 期望 RVA（0 = 不校验）
    PVOID       addr;          // 运行时找到的函数入口地址
    PVOID       orig;          // MinHook 原始入口（MH_CreateHook 回填）
    BOOL        hooked;
} SP_TARGET;

static SP_MODE   g_sp_mode        = SP_MODE_OBSERVE;
// 默认目标：只挂「提示框识别 + 反调试探测 + 句柄伪装」，不挂任何 AOB。
// 这三个都不碰未知参数个数的函数，零透传风险。
static unsigned  g_sp_targets     = SP_T_MSGBOX | SP_T_NTQUERY | SP_T_HANDLE;
static int       g_sp_hide_self   = 0;
static volatile LONG g_sp_seq     = 0;      // 全局事件序号
static int       g_sp_hard_patch  = 0;      // 1 = apply 时用"改字节"而不是挂钩
static int       g_sp_apply_value = 0;      // apply 模式下"干净"的返回值（VM 定夺，默认 0）

// 配置入口：hook.ini（apply_kv）与 init 里的环境变量覆盖都要用，先声明
static void sp_parse_mode(const wchar_t *v);
static void sp_parse_targets(const wchar_t *v);
void liuli_selfprotect_init(void);

// -----------------------------------------------------------------------------
// 7. 配置：hook.ini  +  环境变量覆盖
//    配置文件位置按 ①LIULI_CFG ②本 DLL 同目录 ③C:\liuli-b2 顺序找第一个存在的
// -----------------------------------------------------------------------------
static void trim_w(wchar_t *s)
{
    wchar_t *p, *q;
    if (!s) return;
    p = s;
    while (*p == L' ' || *p == L'\t' || *p == L'\r' || *p == L'\n') ++p;
    if (p != s) memmove(s, p, (wcslen(p) + 1) * sizeof(wchar_t));
    q = s + wcslen(s);
    while (q > s && (q[-1] == L' ' || q[-1] == L'\t' || q[-1] == L'\r' || q[-1] == L'\n')) --q;
    *q = 0;
}

static void parse_conds(const wchar_t *s)
{
    wchar_t buf[600];
    wchar_t *ctx = NULL;
    wchar_t *tok;

    g_cond_count = 0;
    if (!s || !*s) return;
    wcsncpy_s(buf, _countof(buf), s, _TRUNCATE);

    tok = wcstok_s(buf, L",", &ctx);
    while (tok && g_cond_count < MAX_CONDS) {
        COND *c = &g_conds[g_cond_count];
        memset(c, 0, sizeof(*c));
        trim_w(tok);
        if (*tok == L'!') { c->negate = TRUE; ++tok; trim_w(tok); }

        if (_wcsicmp(tok, L"all") == 0) {
            c->type = COND_ALL;
        } else if (_wcsicmp(tok, L"readonly") == 0) {
            c->type = COND_ALL;      // 只读打开本来就会被强制检查，这里等价于 all
        } else if (_wcsnicmp(tok, L"seq:", 4) == 0) {
            c->type = COND_SEQ_EQ;
            c->ival = _wtoi(tok + 4);
        } else if (_wcsnicmp(tok, L"seqmin:", 7) == 0) {
            c->type = COND_SEQ_GE;
            c->ival = _wtoi(tok + 7);
        } else if (_wcsnicmp(tok, L"tid:", 4) == 0) {
            c->type = COND_TID;
            c->ival = _wtoi(tok + 4);
        } else if (_wcsnicmp(tok, L"module:", 7) == 0) {
            c->type = COND_MODULE;
            wcsncpy_s(c->sval, _countof(c->sval), tok + 7, _TRUNCATE);
            trim_w(c->sval);
        } else if (_wcsnicmp(tok, L"stackmodule:", 12) == 0) {
            c->type = COND_STACK_MODULE;
            wcsncpy_s(c->sval, _countof(c->sval), tok + 12, _TRUNCATE);
            trim_w(c->sval);
        } else if (_wcsnicmp(tok, L"addr:", 5) == 0) {
            const wchar_t *body = tok + 5;
            const wchar_t *plus = wcschr(body, L'+');
            if (plus) {
                size_t mlen = (size_t)(plus - body);
                if (mlen >= _countof(c->sval)) mlen = _countof(c->sval) - 1;
                wmemcpy(c->sval, body, mlen);
                c->sval[mlen] = 0;
                trim_w(c->sval);
                c->type   = COND_ADDR;
                c->offset = (ULONG_PTR)wcstoull(plus + 1, NULL, 0);
            } else {
                c->type = COND_NEVER;
            }
        } else if (_wcsnicmp(tok, L"skipfullreads:", 14) == 0) {
            c->type = COND_SKIP_FULLREADS;
            c->ival = _wtoi(tok + 14);
            if (c->ival < 0) c->ival = 0;    // 负数不合法 → 当 0（从第 1 次全文读就改）
        } else if (_wcsicmp(tok, L"verifier") == 0) {
            c->type = COND_VERIFIER;         // 不带数字与 verifier:N 等价，N 只回显日志
            c->ival = 1;
        } else if (_wcsnicmp(tok, L"verifier:", 9) == 0) {
            c->type = COND_VERIFIER;
            c->ival = _wtoi(tok + 9);        // 只为日志/诊断保留：校验读恒放行，不按次数配额
            if (c->ival < 0) c->ival = 0;    // 负数不合法 → 当 0（不影响判定，只影响 why 里的回显）
        } else if (_wcsicmp(tok, L"auto") == 0) {
            c->type = COND_SKIP_FULLREADS;   // auto 是 skipfullreads:1 的别名（旧语义，保留兼容）
            c->ival = 1;
        } else {
            c->type = COND_NEVER;    // 写错 → 恒假，绝不误改
        }
        ++g_cond_count;
        tok = wcstok_s(NULL, L",", &ctx);
    }
}

static void apply_kv(const wchar_t *key, const wchar_t *val)
{
    if (!key || !val) return;

    if (_wcsicmp(key, L"mode") == 0) {
        if (_wcsicmp(val, L"active") == 0)      g_mode = MODE_ACTIVE;
        else if (_wcsicmp(val, L"off") == 0)    g_mode = MODE_OFF;
        else                                    g_mode = MODE_PROBE;
    } else if (_wcsicmp(key, L"match") == 0 || _wcsicmp(key, L"pkg_match") == 0) {
        parse_conds(val);
    } else if (_wcsicmp(key, L"patched") == 0 || _wcsicmp(key, L"pkg_patched") == 0) {
        if (*val) wcsncpy_s(g_patched, MAX_PATH, val, _TRUNCATE);
    } else if (_wcsicmp(key, L"log") == 0) {
        if (*val) wcsncpy_s(g_log_path, MAX_PATH, val, _TRUNCATE);
    } else if (_wcsicmp(key, L"force") == 0) {
        g_force = _wtoi(val);
    } else if (_wcsicmp(key, L"cmdline") == 0) {
        g_dump_cmdline = _wtoi(val);
    } else if (_wcsicmp(key, L"init_delay_ms") == 0 || _wcsicmp(key, L"delay_ms") == 0) {
        g_init_delay_ms = _wtoi(val);
        if (g_init_delay_ms < 0) g_init_delay_ms = 0;
    }
    // ---- 自我保护（第 12b 节）的 ini 键。键名大小写不敏感，值已由调用方 trim_w 去空白。
    //      这里只负责把值放进 g_sp_*；环境变量在 liuli_selfprotect_init() ->
    //      sp_load_cfg() 里再覆盖一次，所以 env 优先级仍然高于 ini（与本文其余键一致）。
    else if (_wcsicmp(key, L"sp") == 0 || _wcsicmp(key, L"selfprotect") == 0) {
        sp_parse_mode(val);
    } else if (_wcsicmp(key, L"sp_targets") == 0) {
        sp_parse_targets(val);
    } else if (_wcsicmp(key, L"sp_hide") == 0) {
        g_sp_hide_self = (_wtoi(val) != 0) ? 1 : 0;
    } else if (_wcsicmp(key, L"sp_hard") == 0) {
        g_sp_hard_patch = (_wtoi(val) != 0) ? 1 : 0;
    } else if (_wcsicmp(key, L"sp_ret") == 0) {
        g_sp_apply_value = _wtoi(val);
    }
}

// 取环境变量（值太长/不存在都算没有）
static BOOL env_get(const wchar_t *name, wchar_t *out, size_t cch)
{
    DWORD n = GetEnvironmentVariableW(name, out, (DWORD)cch);
    return (n > 0 && n < (DWORD)cch);
}

// 本 DLL 完整路径 → 「同目录\hook.ini」。只做字符串拼接，不判断文件是否存在。
static BOOL self_cfg_path(const wchar_t *dll_path, wchar_t *out, size_t cch)
{
    wchar_t dir[MAX_PATH];
    wchar_t *p, *last = NULL;

    if (!out || !cch) return FALSE;
    out[0] = 0;
    if (!dll_path || !*dll_path) return FALSE;

    wcsncpy_s(dir, _countof(dir), dll_path, _TRUNCATE);
    for (p = dir; *p; ++p) {
        if (*p == L'\\' || *p == L'/') last = p;
    }
    if (!last) return FALSE;          // 完整路径里一定有分隔符，没有就当求不出来
    *last = 0;                        // 砍掉文件名，留下目录（含结尾反斜杠的位置）

    // 目录 + "\hook.ini" + 结尾 0
    if (wcslen(dir) + 10 > cch) return FALSE;
    wcsncpy_s(out, cch, dir, _TRUNCATE);
    wcsncat_s(out, cch, L"\\hook.ini", _TRUNCATE);
    return TRUE;
}

// worker 早期调用：记下本 DLL 的完整路径，并算出「同目录\hook.ini」候选路径
static void cache_self_path(void)
{
    DWORD n;

    g_self_path[0] = 0;
    g_cfg_self[0]  = 0;
    if (!g_self) return;              // 没拿到 HMODULE 就放弃候选②，不影响 ①③

    n = GetModuleFileNameW(g_self, g_self_path, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) {
        g_self_path[0] = 0;
        return;
    }
    g_self_path[n] = 0;
    if (!self_cfg_path(g_self_path, g_cfg_self, _countof(g_cfg_self))) {
        g_cfg_self[0] = 0;
    }
}

// 读一个具体路径的配置文件；文件存在且读到内容返回 TRUE（解析失败也返回 TRUE，
// 因为「存在」这一点已经满足了，别再往下掉到低优先级的文件上）
static BOOL load_cfg_from(const wchar_t *path)
{
    static char  raw[16384];
    static wchar_t buf[16384];
    HANDLE h;
    DWORD got = 0, i;
    char *p = raw;
    wchar_t *line;
    int n;

    if (!path || !*path) return FALSE;

    h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return FALSE;
    if (!ReadFile(h, raw, sizeof(raw) - 1, &got, NULL)) { CloseHandle(h); return FALSE; }
    CloseHandle(h);
    raw[got] = 0;
    if (got >= 3 && (unsigned char)p[0] == 0xEF && (unsigned char)p[1] == 0xBB &&
        (unsigned char)p[2] == 0xBF) {
        p += 3;
    }
    n = MultiByteToWideChar(CP_UTF8, 0, p, -1, buf, (int)_countof(buf) - 1);
    if (n <= 0) {
        // 配置文件被记事本存成 GBK/ANSI 时也能读：退回系统 ACP 再试一次
        n = MultiByteToWideChar(CP_ACP, 0, p, -1, buf, (int)_countof(buf) - 1);
    }
    if (n <= 0) return TRUE;
    buf[n] = 0;

    line = buf;
    while (line && *line) {
        wchar_t *nl = wcschr(line, L'\n');
        wchar_t *eq;
        if (nl) *nl = 0;
        trim_w(line);
        if (*line && *line != L'#' && *line != L';' &&
            !(line[0] == L'/' && line[1] == L'/')) {
            eq = wcschr(line, L'=');
            if (eq) {
                wchar_t *k = line;
                wchar_t *v = eq + 1;
                wchar_t *hash;
                *eq = 0;
                trim_w(k);
                trim_w(v);
                // 行尾 "# 注释" 支持：'#' 前面必须是空白
                for (hash = v; *hash; ++hash) {
                    if (*hash == L'#' && hash > v && (hash[-1] == L' ' || hash[-1] == L'\t')) {
                        *hash = 0;
                        trim_w(v);
                        break;
                    }
                }
                apply_kv(k, v);
            }
        }
        if (!nl) break;
        line = nl + 1;
    }
    (void)i;
    return TRUE;
}

// 按优先级找第一个存在的配置文件并加载：
//   ① 环境变量 LIULI_CFG（最高优先，调试覆盖用）
//   ② 本 DLL 同目录的 hook.ini（产品化正式位置：QQ 根目录）
//   ③ C:\liuli-b2\hook.ini（旧调试路径，兼容保留）
// 都不存在则维持「无配置 = probe」，g_cfg_path 留在默认值供日志对照。
static BOOL load_cfg_file(void)
{
    wchar_t env_path[1024];

    // ① 环境变量 LIULI_CFG
    if (env_get(L"LIULI_CFG", env_path, _countof(env_path)) && *env_path) {
        if (load_cfg_from(env_path)) {
            wcsncpy_s(g_cfg_path, MAX_PATH, env_path, _TRUNCATE);
            g_cfg_src = L"env";
            return TRUE;
        }
        // 环境变量指了路径但读不到：记一笔，继续按顺序往下找（不把调试流程卡死）
        g_cfg_env_miss = 1;
    }

    // ② 本 DLL 同目录的 hook.ini
    if (g_cfg_self[0] && load_cfg_from(g_cfg_self)) {
        wcsncpy_s(g_cfg_path, MAX_PATH, g_cfg_self, _TRUNCATE);
        g_cfg_src = L"self";
        return TRUE;
    }

    // ③ 旧调试路径
    if (load_cfg_from(CFG_PATH_DEFAULT)) {
        wcsncpy_s(g_cfg_path, MAX_PATH, CFG_PATH_DEFAULT, _TRUNCATE);
        g_cfg_src = L"default";
        return TRUE;
    }

    g_cfg_src = L"none";
    return FALSE;
}

static void apply_env(void)
{
    wchar_t v[1024];

    // LIULI_CFG 已经在 load_cfg_file() 里作为①优先级处理过了，这里不再重复加载，
    // 否则同一个文件会被解析两遍、match 条件被重复累加。
    if (env_get(L"LIULI_MODE", v, _countof(v)))      apply_kv(L"mode", v);
    if (env_get(L"LIULI_PKG_MATCH", v, _countof(v))) apply_kv(L"match", v);
    if (env_get(L"LIULI_PKG_PATCHED", v, _countof(v))) apply_kv(L"patched", v);
    if (env_get(L"LIULI_LOG", v, _countof(v)))       apply_kv(L"log", v);
    if (env_get(L"LIULI_FORCE", v, _countof(v)))     apply_kv(L"force", v);
    if (env_get(L"LIULI_DELAY_MS", v, _countof(v)))  apply_kv(L"init_delay_ms", v);
}

// -----------------------------------------------------------------------------
// 8. 进程角色判定
// -----------------------------------------------------------------------------
typedef enum {
    ROLE_MAIN = 0,
    ROLE_SKIP_TYPE,     // --type=xxx 子进程
    ROLE_SKIP_EXAPP     // --exApp / --loadapp=exApp
} ROLE;

static BOOL ci_contains(const wchar_t *hay, const wchar_t *needle)
{
    size_t nl, i;
    const wchar_t *p;

    if (!hay || !needle) return FALSE;
    nl = wcslen(needle);
    if (!nl) return TRUE;
    for (p = hay; *p; ++p) {
        for (i = 0; i < nl; ++i) {
            if (!p[i] || lower_w(norm_sep(p[i])) != lower_w(norm_sep(needle[i]))) break;
        }
        if (i == nl) return TRUE;
    }
    return FALSE;
}

static ROLE classify_process(void)
{
    const wchar_t *cl = GetCommandLineW();

    if (!cl) return ROLE_MAIN;
    if (ci_contains(cl, L"--type="))       return ROLE_SKIP_TYPE;
    if (ci_contains(cl, L"--exapp"))       return ROLE_SKIP_EXAPP;
    if (ci_contains(cl, L"loadapp=exapp")) return ROLE_SKIP_EXAPP;
    return ROLE_MAIN;
}

// -----------------------------------------------------------------------------
// 9. 条件求值 + 只读判定
// -----------------------------------------------------------------------------
// 「全文读」判定：只有这些 dwDesiredAccess 才说明调用方会把文件内容读出来。
//   FILE_READ_DATA (0x0001)      —— 普通读；GENERIC_READ 展开后也含这一位
//   GENERIC_READ   (0x80000000)  —— 单独出现时按位与 0x0001 得 0，但它隐含读数据
// 只查属性（FILE_READ_ATTRIBUTES 0x80 等）的打开两条都不满足 → 不算全文读。
static BOOL is_fullread_open(DWORD access)
{
    if ((access & FILE_READ_DATA) != 0) return TRUE;
    if (access == (DWORD)GENERIC_READ) return TRUE;
    return FALSE;
}

// -----------------------------------------------------------------------------
// 9a. verifier 条件：「这一次全文读是不是 QQ 的完整性校验读？」
//     —— installer#3 / #34 #35 #39 #40 这一族「装好了但插件从没加载」的修复核心。
// -----------------------------------------------------------------------------
// 背景（四份真实诊断包逐条核对，见 issue-collection/parts/A-install.md §0.3）：
//   QQ 启动时会用两种**原始 dwDesiredAccess**去读 resources\app\package.json，完全可分：
//
//     校验读（必须拿到原版）  调用方 QQNT.dll+0x5314C1x（8 帧栈）
//                             acc = 0x80000000  ← 未展开的 GENERIC_READ
//     入口读（必须拿到 patched）调用方 QQNT.dll+0x2986007（5 帧栈）
//                             acc = 0x00120089  ← 展开后的 FILE_GENERIC_READ
//
//   1.1.0 的 `skipfullreads:1` 只数「第几次全文读」，而校验读**不保证先发生**：
//   失败形态（d34 5/5、d30 3/3、d35 7/8）里 fr=1 就是入口读，被无条件放行 →
//   入口按原版 main 解析 → liuli_loader.js 从未执行，全程没有任何报错。
//
// 本条件的语义：
//   verifier（= verifier:N；N 只用于日志/诊断，不参与判定）
//     「这次是校验读」（hook 看到的原始 acc == 0x80000000）  → **恒放行原版，不设次数上限**；
//     「这次不是校验读」（入口读 0x00120089，以及其它任何读数据内容的打开）→ 匹配 → 换 patched。
//
//   为什么校验读必须恒放行、不能照搬 skipfullreads 的「前 N 次」配额：同一进程里
//   校验读可能出现多次 —— d34 的 `seq=11 fr=11 acc=0x80000000 caller=wrapper.node+0x36FD8D6`
//   就是第 2 次（FGESDK 的另一种校验原语）。只要有一次校验读拿到 patched 内容，
//   哈希/清单就对不上，可能直接触发「文件已损坏」甚至 QQ 静默退出（#39 实测过
//   物理改 package.json 的后果；hook 换源对校验方而言效果类似）。
//   因此 `verifier:1` 与 `verifier:99` 的行为完全相同；数字只是原样回显到
//   `why=verifier:N(...)` 方便复盘配置 —— 不要据此以为「校验读只放行 N 次」。
//
//   属性读（FILE_READ_ATTRIBUTES 等）fullread_idx==0，永远不匹配，加 '!' 也一样 ——
//   与 skipfullreads 的既有保证保持一致（属性读绝不能被改写）。
//
// ★ 风险 1（方案 A 的固有风险，也是唯一误判方向）：如果某个 QQ 版本的**入口读也用
//   未展开的 0x80000000**，本条件会把它误判成校验读而放行 —— 回到「静默失效」
//   （不会更糟，但没修好）。反之不会误改校验读，因为所有 0x80000000 的读都放行。
//   9.9.35/9.9.36 的四份诊断包 100% 可分，但不保证未来版本。
//   兜底（方案 B，本文件**未实现**，仅 TODO）：不靠 acc，而靠调用点身份 ——
//   在 QQNT.dll 里用 AOB/特征码扫出「校验读」的那个 call 站点（实测 d35=+0x5314C18、
//   d38=+0x5314C58，每次 QQ 升级都会漂，所以必须扫特征码而不是写死 RVA），把结果与
//   expected_rva 比对并打 WARN-build-drift（第 12b 节的 sp_locate_ex / sp_hook_one
//   已有整套实现：sp_scan_aob、sp_module_by_name、sp_module_text_range）。命中该站点
//   的调用才放行原版；定位失败则退回「全部改写」并 WARN。备选 B2（不改字节、不改
//   acc 判据）：用栈形状区分（校验读 8 帧、入口读 5 帧，实测稳定），但帧数依赖编译
//   器优化，同样需要 VM 验证。
//
// ★ 风险 2（本条件能力之外）：hook 是 DllMain 起 worker 线程后异步装上的（第 13 节），
//   装好之前发生的读取完全看不见 —— 那种情况下任何判定（包括 seq/skipfullreads）都
//   可能漏掉入口读。这需要安装器侧「装完自检 liteLoader.log 真的出现了」兜底，
//   不是 hook 单点能修的。
// -----------------------------------------------------------------------------

// 「校验读」的唯一信号：hook 观察到的**原始** acc 就是未展开的 GENERIC_READ。
// 注意：这里**不能**复用 is_fullread_open —— 它对 0x80000000 和 0x120089 一视同仁
// （后者靠 FILE_READ_DATA 命中），掩码层面的差异正是本条件要利用的东西。
static BOOL is_verifier_read(DWORD access)
{
    return (access == (DWORD)GENERIC_READ);
}

// verifier 的求值：校验读恒放行（**不设次数上限**），其余全文读（入口读 0x00120089 等）
// 一律匹配；属性读恒假。
// 注意：这里刻意不照搬 skipfullreads 的「前 N 次」配额 —— 同一进程可能出现多次校验读
// （d34 的 fr=11 acc=0x80000000 就是第 2 次），只要有一次校验读被换成 patched，
// 完整性校验就可能失败、QQ 直接起不来。见第 9a 节的说明。
static BOOL verifier_read_match(const HITCTX *c)
{
    if (c->fullread_idx <= 0) return FALSE;   // 属性读等：恒不匹配（加 '!' 也不匹配）
    return !is_verifier_read(c->access);      // 校验读 → 放行；入口读（0x120089）等 → 改写
}

static BOOL is_readonly_open(DWORD access, DWORD disp)
{
    const DWORD write_mask = GENERIC_WRITE | GENERIC_ALL | FILE_WRITE_DATA |
                             FILE_APPEND_DATA | FILE_WRITE_EA | FILE_WRITE_ATTRIBUTES |
                             DELETE | WRITE_DAC | WRITE_OWNER;

    if (access & write_mask) return FALSE;
    if (disp != OPEN_EXISTING && disp != OPEN_ALWAYS) return FALSE;
    return TRUE;
}

static BOOL eval_conds(const HITCTX *c, wchar_t *why, size_t cch)
{
    int i, f;
    wchar_t frdesc[64] = L"";

    if (g_cond_count <= 0) {
        swprintf_s(why, cch, L"no-match-config");
        return FALSE;
    }
    for (i = 0; i < g_cond_count; ++i) {
        const COND *k = &g_conds[i];
        BOOL r = FALSE;

        switch (k->type) {
        case COND_ALL:   r = TRUE; break;
        case COND_SEQ_EQ: r = (c->seq == k->ival); break;
        case COND_SEQ_GE: r = (c->seq >= k->ival); break;
        case COND_TID:   r = ((int)c->tid == k->ival); break;
        case COND_MODULE: r = ci_contains(c->caller_mod, k->sval); break;
        case COND_STACK_MODULE:
            for (f = 0; f < c->stack_n; ++f) {
                if (ci_contains(c->stack_mod[f], k->sval)) { r = TRUE; break; }
            }
            break;
        case COND_ADDR:
            for (f = 0; f < c->stack_n; ++f) {
                if (_wcsicmp(c->stack_mod[f], k->sval) == 0 &&
                    c->stack_off[f] == k->offset) { r = TRUE; break; }
            }
            break;
        case COND_SKIP_FULLREADS:
            // 第 N+1 次及以后的「全文读」才算命中；fullread_idx<=0（属性读/没数到）
            // 恒为不匹配，且取反也不会把属性读放进来（属性读永远不该被改写）。
            r = (c->fullread_idx > 0 && c->fullread_idx > k->ival);
            if (k->negate && c->fullread_idx > 0) r = !r;
            if (!frdesc[0]) {
                _snwprintf_s(frdesc, _countof(frdesc), _TRUNCATE, L"%sskipfullreads:%d",
                             (k->negate ? L"!" : L""), k->ival);
            }
            break;
        case COND_VERIFIER:
            // 校验读识别（installer#3 修复）：不看「第几次」，只看这次是不是校验读；
            // 校验读恒放行、不设次数上限（数字只回显日志）。语义与风险见第 9a 节。
            r = verifier_read_match(c);
            if (k->negate && c->fullread_idx > 0) r = !r;
            if (!frdesc[0]) {
                _snwprintf_s(frdesc, _countof(frdesc), _TRUNCATE, L"%sverifier:%d",
                             (k->negate ? L"!" : L""), k->ival);
            }
            break;
        default: r = FALSE; break;
        }
        // COND_SKIP_FULLREADS / COND_VERIFIER 的取反已在上面处理过（要保证属性读恒假），这里跳过
        if (k->negate && k->type != COND_SKIP_FULLREADS && k->type != COND_VERIFIER) r = !r;
        if (!r) {
            swprintf_s(why, cch, L"cond%d-false", i);
            return FALSE;
        }
    }
    // 条件全成立：把「这是第几次全文读 / 第几次校验读」写进 why，现场一眼能判读。
    // vfr=0 表示本次启动到这一读为止还没见过校验读（0x80000000），是诊断 injection
    // 失效的关键线索：旧 skipfullreads 判据下 fr=1 会被放行，verifier 判据下会改写。
    if (frdesc[0]) {
        _snwprintf_s(why, cch, _TRUNCATE, L"%s(fr=%d,vfr=%d)", frdesc, c->fullread_idx, c->verifier_idx);
    } else if (c->fullread_idx > 0) {
        _snwprintf_s(why, cch, _TRUNCATE, L"ok(fr=%d,vfr=%d)", c->fullread_idx, c->verifier_idx);
    } else {
        wcsncpy_s(why, cch, L"ok", _TRUNCATE);
    }
    return TRUE;
}

// event=cond 回显用：条件类型的人话名字
static const wchar_t *cond_type_name(COND_TYPE t)
{
    switch (t) {
    case COND_ALL:            return L"all";
    case COND_SEQ_EQ:         return L"seq";
    case COND_SEQ_GE:         return L"seqmin";
    case COND_TID:            return L"tid";
    case COND_MODULE:         return L"module";
    case COND_STACK_MODULE:   return L"stackmodule";
    case COND_ADDR:           return L"addr";
    case COND_SKIP_FULLREADS: return L"skipfullreads";
    case COND_VERIFIER:       return L"verifier";
    default:                  return L"never";
    }
}

// -----------------------------------------------------------------------------
// 10. CreateFileW detour：探针 + 生效（换句柄）
// -----------------------------------------------------------------------------
static HANDLE WINAPI Detour_CreateFileW(LPCWSTR lpFileName, DWORD dwDesiredAccess,
                                        DWORD dwShareMode, LPSECURITY_ATTRIBUTES lpSecurityAttributes,
                                        DWORD dwCreationDisposition, DWORD dwFlagsAndAttributes,
                                        HANDLE hTemplateFile)
{
    PFN_CreateFileW orig = g_orig;
    DWORD saved_err, err;
    HANDLE h;
    HITCTX ctx;
    int seq;
    int fr_idx = 0;
    int vfr_idx = 0;      // 这是第几次「校验读」（原始 acc==0x80000000）；0 = 本次不是校验读
    BOOL want = FALSE;
    wchar_t why[160] = L"-";
    wchar_t patched[MAX_PATH] = L"";
    wchar_t decision[32] = L"pass";
    const wchar_t *note = L"-";

    // 理论上 g_orig 一定非空（hook 只在其后启用）；拿不到就必须保守失败
    if (!orig) return INVALID_HANDLE_VALUE;

    // 非目标路径：最快路径直接放行
    if (tls_guard_hit() || !lpFileName || !is_target_pkg_path(lpFileName)) {
        return orig(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes,
                    dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
    }

    saved_err = GetLastError();
    tls_guard_on();

    seq = (int)InterlockedIncrement(&g_hit_seq);
    // 「全文读」序号：只在这条已确认是目标 package.json 的路径上数，
    // 属性读（FILE_READ_ATTRIBUTES 之类）不自增，也就不会把门卫的位置算歪。
    // 必须在 eval_conds 之前算好（skipfullreads:N 就靠这个值判定）。
    if (is_fullread_open(dwDesiredAccess)) {
        fr_idx = (int)InterlockedIncrement(&g_fullread_seq);
        // 「校验读」序号（verifier:N 用）：与 fr_idx 分开计数，fr 保持历史诊断口径。
        // 判据是 hook 看到的**原始** acc（0x80000000 = 未展开的 GENERIC_READ），
        // 不是展开后的掩码 —— 见第 9a 节。
        if (is_verifier_read(dwDesiredAccess)) {
            vfr_idx = (int)InterlockedIncrement(&g_verifier_seq);
        }
    }
    hit_collect(&ctx, (ULONG_PTR)_ReturnAddress(), dwDesiredAccess, dwShareMode,
                dwCreationDisposition, dwFlagsAndAttributes, seq, fr_idx, vfr_idx);

    // 是否要换成 patched 文件
    if (g_mode == MODE_ACTIVE) {
        want = eval_conds(&ctx, why, _countof(why));
        if (want && !is_readonly_open(dwDesiredAccess, dwCreationDisposition)) {
            want = FALSE;
            wcsncpy_s(why, _countof(why), L"not-readonly-open", _TRUNCATE);
        }
        if (want) {
            if (g_patched[0]) {
                wcsncpy_s(patched, MAX_PATH, g_patched, _TRUNCATE);
            } else if (!derive_patched_path(lpFileName, patched, MAX_PATH)) {
                want = FALSE;
                wcsncpy_s(why, _countof(why), L"path-too-long", _TRUNCATE);
            }
            if (want && GetFileAttributesW(patched) == INVALID_FILE_ATTRIBUTES) {
                want = FALSE;
                wcsncpy_s(why, _countof(why), L"patched-missing", _TRUNCATE);
            }
        }
    } else {
        wcsncpy_s(why, _countof(why), L"mode-probe", _TRUNCATE);
    }

    if (want) {
        SetLastError(saved_err);
        __try {
            h = orig(patched, dwDesiredAccess, dwShareMode, lpSecurityAttributes,
                     dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            h = INVALID_HANDLE_VALUE;
        }
        err = GetLastError();
        if (h != INVALID_HANDLE_VALUE) {
            wcsncpy_s(decision, _countof(decision), L"rewrite", _TRUNCATE);
            note = L"-";
            liuli_log(L"event=hit seq=%d fr=%d vfr=%d tid=%lu path=\"%s\" acc=0x%08lX share=0x%08lX disp=%lu "
                 L"attr=0x%08lX caller=%s ret=0x%p stack=%s res=ok err=0 "
                 L"decision=%s patched=\"%s\" note=%s why=%s",
                 seq, fr_idx, vfr_idx, (unsigned long)ctx.tid, lpFileName,
                 (unsigned long)dwDesiredAccess, (unsigned long)dwShareMode,
                 (unsigned long)dwCreationDisposition, (unsigned long)dwFlagsAndAttributes,
                 ctx.caller, (void *)ctx.retaddr, ctx.stack, decision, patched, note, why);
            SetLastError(err);
            tls_guard_off();
            return h;
        }
        // patched 打不开 → 老实回退原版
        note = L"patched-open-failed";
        wcsncpy_s(decision, _countof(decision), L"rewrite-fallback", _TRUNCATE);
        SetLastError(saved_err);
    }

    __try {
        h = orig(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes,
                 dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        h = INVALID_HANDLE_VALUE;
        SetLastError(ERROR_INVALID_PARAMETER);
    }
    err = GetLastError();

    liuli_log(L"event=hit seq=%d fr=%d vfr=%d tid=%lu path=\"%s\" acc=0x%08lX share=0x%08lX disp=%lu "
         L"attr=0x%08lX caller=%s ret=0x%p stack=%s res=%s err=%lu "
         L"decision=%s patched=\"%s\" note=%s why=%s",
         seq, fr_idx, vfr_idx, (unsigned long)ctx.tid, lpFileName,
         (unsigned long)dwDesiredAccess, (unsigned long)dwShareMode,
         (unsigned long)dwCreationDisposition, (unsigned long)dwFlagsAndAttributes,
         ctx.caller, (void *)ctx.retaddr, ctx.stack,
         (h == INVALID_HANDLE_VALUE ? L"fail" : L"ok"), (unsigned long)err,
         decision, patched, note, why);

    SetLastError(err);
    tls_guard_off();
    return h;
}

// -----------------------------------------------------------------------------
// 10b. 可选传感器（只记日志、永不改写；默认不编译）
// -----------------------------------------------------------------------------
#if LIULI_EXTRA_SENSORS
static HANDLE WINAPI Detour_CreateFile2(LPCWSTR lpFileName, DWORD dwDesiredAccess,
                                        DWORD dwShareMode, DWORD dwCreationDisposition,
                                        LPCREATEFILE2_EXTENDED_PARAMETERS pEx)
{
    HANDLE h;

    if (!g_orig_cf2) return INVALID_HANDLE_VALUE;
    if (tls_guard_hit() || !lpFileName || !is_target_pkg_path(lpFileName)) {
        return g_orig_cf2(lpFileName, dwDesiredAccess, dwShareMode, dwCreationDisposition, pEx);
    }
    tls_guard_on();
    h = g_orig_cf2(lpFileName, dwDesiredAccess, dwShareMode, dwCreationDisposition, pEx);
    liuli_log(L"event=sensor api=CreateFile2 path=\"%s\" acc=0x%08lX disp=%lu res=%s",
         lpFileName, (unsigned long)dwDesiredAccess, (unsigned long)dwCreationDisposition,
         (h == INVALID_HANDLE_VALUE ? L"fail" : L"ok"));
    tls_guard_off();
    return h;
}

static BOOL WINAPI Detour_GetFileAttributesExW(LPCWSTR lpFileName, GET_FILEEX_INFO_LEVELS level,
                                               LPVOID lpInfo)
{
    BOOL ok;
    if (!g_orig_gfae) return FALSE;
    if (tls_guard_hit() || !lpFileName || !is_target_pkg_path(lpFileName)) {
        return g_orig_gfae(lpFileName, level, lpInfo);
    }
    tls_guard_on();
    ok = g_orig_gfae(lpFileName, level, lpInfo);
    liuli_log(L"event=sensor api=GetFileAttributesExW path=\"%s\" res=%s",
         lpFileName, (ok ? L"ok" : L"fail"));
    tls_guard_off();
    return ok;
}
#endif

// -----------------------------------------------------------------------------
// 11. TLS 重入守卫（用 TlsAlloc，避免 __declspec(thread) 在早期 LoadLibrary 的坑）
// -----------------------------------------------------------------------------
static void tls_guard_on(void)
{
    if (g_tls_slot != TLS_OUT_OF_INDEXES) TlsSetValue(g_tls_slot, (LPVOID)1);
}

static void tls_guard_off(void)
{
    if (g_tls_slot != TLS_OUT_OF_INDEXES) TlsSetValue(g_tls_slot, NULL);
}

static BOOL tls_guard_hit(void)
{
    return (g_tls_slot != TLS_OUT_OF_INDEXES) && (TlsGetValue(g_tls_slot) != NULL);
}

// -----------------------------------------------------------------------------
// 12. 找 CreateFileW 的真实实现并装 hook
// -----------------------------------------------------------------------------
static PVOID follow_jmps(PVOID p)
{
    int i;
    unsigned char *b;
    PVOID next;

    for (i = 0; i < 4 && p; ++i) {
        b = (unsigned char *)p;
        next = NULL;
        __try {
            if (b[0] == 0xE9) {                        // jmp rel32
                INT32 rel;
                memcpy(&rel, b + 1, 4);
                next = (PVOID)(b + 5 + rel);
            } else if (b[0] == 0xFF && b[1] == 0x25) { // jmp qword ptr [rip+disp32]
                INT32 disp;
                void **slot;
                memcpy(&disp, b + 2, 4);
                slot = (void **)(b + 6 + disp);
                next = *slot;
            }
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            next = NULL;
        }
        if (!next || next == p) break;
        p = next;
    }
    return p;
}

static void install_hook(void)
{
    HMODULE k32;
    FARPROC proc;
    PVOID target;
    wchar_t desc[128];
    MH_STATUS st;

    if (MH_Initialize() != MH_OK) {
        liuli_log(L"event=install status=fail stage=MH_Initialize");
        return;
    }

    k32  = GetModuleHandleW(L"kernel32.dll");
    proc = k32 ? GetProcAddress(k32, "CreateFileW") : NULL;
    if (!proc) {
        liuli_log(L"event=install status=fail stage=GetProcAddress CreateFileW err=%lu",
             (unsigned long)GetLastError());
        return;
    }
    // kernel32 里多半只是个 jmp 跳板，跟到底（kernelbase 的真实实现）再挂钩，
    // 这样「从 kernelbase 导入的调用方」也能被覆盖。
    target = follow_jmps((PVOID)proc);
    describe_addr((ULONG_PTR)target, desc, _countof(desc));

    st = MH_CreateHook(target, (LPVOID)Detour_CreateFileW, (LPVOID *)&g_orig);
    if (st != MH_OK) {
        liuli_log(L"event=install status=fail stage=MH_CreateHook err=%d target=%s",
             (int)st, desc);
        return;
    }

#if LIULI_EXTRA_SENSORS
    {
        HMODULE kb = GetModuleHandleW(L"kernelbase.dll");
        FARPROC p2 = kb ? GetProcAddress(kb, "CreateFile2") : NULL;
        if (p2) {
            st = MH_CreateHook(follow_jmps((PVOID)p2), (LPVOID)Detour_CreateFile2,
                               (LPVOID *)&g_orig_cf2);
            liuli_log(L"event=install sensor=CreateFile2 status=%d", (int)st);
        }
        p2 = GetProcAddress(k32, "GetFileAttributesExW");
        if (p2) {
            st = MH_CreateHook(follow_jmps((PVOID)p2), (LPVOID)Detour_GetFileAttributesExW,
                               (LPVOID *)&g_orig_gfae);
            liuli_log(L"event=install sensor=GetFileAttributesExW status=%d", (int)st);
        }
    }
#endif

    st = MH_EnableHook(MH_ALL_HOOKS);
    if (st != MH_OK) {
        liuli_log(L"event=install status=fail stage=MH_EnableHook err=%d target=%s", (int)st, desc);
        return;
    }

    g_hook_target = (ULONG_PTR)target;
    InterlockedExchange(&g_installed, 1);
    liuli_log(L"event=install status=ok target=%s hook=%p mode=%s conds=%d patched=\"%s\" delay_ms=%d",
         desc, (void *)target,
         (g_mode == MODE_ACTIVE ? L"active" : (g_mode == MODE_OFF ? L"off" : L"probe")),
         g_cond_count, g_patched, g_init_delay_ms);
}

// =============================================================================
// 12b. 自我保护 / 反检测 / 文件校验退出中和（SP.1 ~ SP.9）
// -----------------------------------------------------------------------------
//  从 selfprotect-proposal.c 并入。设计原则与 hook.c 一致：
//    - 任何一步失败只 liuli_log 一条 warn，**绝不**让 QQ 崩；
//    - 所有指针解引用/写内存都包在 __try 里；
//    - 默认 observe：只挂钩记录、一个字节都不改；apply 必须显式配置；
//    - 特征码只在指定模块里扫，扫不到就跳过（版本漂移不致命）。
//  复用 hook.c 已有的工具（不重复实现）：
//    liuli_log / describe_addr / ends_with_ci / ci_contains / trim_w /
//    env_get / follow_jmps / g_orig / g_self / MH_* 。
//  配置（优先级：环境变量 > hook.ini；两者都没有 = observe，targets 默认见 6b 节）：
//      hook.ini / 环境变量
//        sp         / LIULI_SP         = off | observe | apply
//        sp_targets / LIULI_SP_TARGETS = sigE,sigD,sigB,msgbox,handle,ntquery,enum
//        sp_hide    / LIULI_SP_HIDE    = 1|0   把本 DLL 从模块枚举里摘掉
//        sp_hard    / LIULI_SP_HARD    = 1|0   apply 时用"改字节"代替挂钩
//        sp_ret     / LIULI_SP_RET     = 0|1   apply 时给 AOB 目标的返回值
//
//  实测坐标（9.9.36-53644，见 selfprotect\sig-hits-9.9.36.txt）：
//      wrapper.node  sigE  → RVA 0x36FD870  函数 0x36FD870..0x36FE687
//      wrapper.node  sigD#1→ RVA 0x2F2E430
//      wrapper.node  sigD#2→ RVA 0x3F55048
//      QQNT.dll      sigB  → 命中 0x511151，函数入口 0x511150（sig 少吃首字节 0x41）
//      QQ.exe        损坏出口 RVA 0x1D8B0 —— 本模块**故意不 patch 它**
//
//  待 VM 定夺（不要凭猜开 apply）：
//      * wrapper.node 那两个 [FGESDK] 函数返回 0 还是 1 表示"没问题"；
//      * K32EnumProcessModules 过滤后 QQ 自身逻辑是否受影响。
// =============================================================================

// 各目标在 MinHook 里回填的原始入口 / 目标位（必须定义在 SP_DEFINE_DETOUR 之前）
static PVOID    g_sp_targets_orig[4]  = { NULL, NULL, NULL, NULL };
static unsigned g_sp_targets_flag[4]  = { 0, 0, 0, 0 };
static PVOID    g_sp_msgbox_orig = NULL;
static PVOID    g_sp_gfpnh_orig  = NULL;   // GetFinalPathNameByHandleW
static PVOID    g_sp_gfihb_orig  = NULL;   // GetFileInformationByHandle
static PVOID    g_sp_gfihbex_orig= NULL;   // GetFileInformationByHandleEx
static PVOID    g_sp_ntqip_orig  = NULL;   // NtQueryInformationProcess
static PVOID    g_sp_enum_orig   = NULL;   // K32EnumProcessModules

// -----------------------------------------------------------------------------
// SP.1  四条 AOB（来自 BetterUniverse dbghelp.dll，已在 9.9.36 上实测命中）
//       掩码 'x' = 必须相等，'?' = 通配（对应数组里的 0x00）
// -----------------------------------------------------------------------------
typedef struct {
    const char         *name;
    const unsigned char *bytes;
    const char         *mask;
    unsigned            len;
    unsigned            expected_rva;
    const char         *module;   // 只在哪个模块里扫
    unsigned            flag;
} SP_SIG;

/* sigB  len=45  wildcards=16 */
static const unsigned char sp_sigB[] = {
    0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0x00, 0x00,
    0x00, 0x00, 0x0f, 0x29, 0xb4, 0x24, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8b, 0x05, 0x00, 0x00, 0x00,
    0x00, 0x48, 0x31, 0xe0, 0x48, 0x89, 0x84, 0x24, 0x00, 0x00, 0x00, 0x00, 0xb9
};
/* sigD  len=17  wildcards=4 */
static const unsigned char sp_sigD[] = {
    0x41, 0x57, 0x41, 0x56, 0x56, 0x57, 0x53, 0x48, 0x81, 0xec, 0x00, 0x00, 0x00, 0x00, 0x44, 0x89,
    0xce
};
/* sigE  len=41  wildcards=16 */
static const unsigned char sp_sigE[] = {
    0x55, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x56, 0x57, 0x53, 0x48, 0x81, 0xec, 0x00, 0x00, 0x00,
    0x00, 0x48, 0x8d, 0xac, 0x24, 0x00, 0x00, 0x00, 0x00, 0x48, 0xc7, 0x85, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x49, 0x89, 0xce, 0xf6, 0x02
};

#define SP_SIGB_MASK "xxxxxxxxxxxxxx????xxxx????xxx????xxxxxxx????x"
#define SP_SIGD_MASK "xxxxxxxxxx????xxx"
#define SP_SIGE_MASK "xxxxxxxxxxxxx????xxxx????xxx????????xxxxx"

// sigD 专门用于 wrapper.node：它在 QQNT.dll 里有 21 处命中，**绝不能**在 QQNT.dll 里用。
static const SP_SIG sp_sigs[] = {
    { "sigE",  sp_sigE, SP_SIGE_MASK, 41, SP_EXP_SIGE_RVA,  "wrapper.node", SP_T_SIGE  },
    { "sigD",  sp_sigD, SP_SIGD_MASK, 17, SP_EXP_SIGD1_RVA, "wrapper.node", SP_T_SIGD1 }, // 第 1 处
    { "sigD",  sp_sigD, SP_SIGD_MASK, 17, SP_EXP_SIGD2_RVA, "wrapper.node", SP_T_SIGD2 }, // 第 2 处
    { "sigB",  sp_sigB, SP_SIGB_MASK, 45, SP_EXP_SIGB_RVA,  "QQNT.dll",     SP_T_SIGB  },
};

// -----------------------------------------------------------------------------
// SP.2  工具：按名字取模块基址 / 取 .text 区间 / AOB 扫描 / 写字节
// -----------------------------------------------------------------------------

// node 的 addon 是用完整路径 LoadLibrary 的，GetModuleHandleW(L"wrapper.node")
// 大多数情况能命中；保守起见再退一步用 Toolhelp 按后缀匹配（ends_with_ci 复用
// hook.c 第 3 节那份，'/' 与 '\' 等价）。
static HMODULE sp_module_by_name(const wchar_t *base_name)
{
    HMODULE h = NULL;
    HANDLE snap;
    MODULEENTRY32W me;

    if (!base_name || !*base_name) return NULL;
    h = GetModuleHandleW(base_name);
    if (h) return h;

    snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, GetCurrentProcessId());
    if (snap == INVALID_HANDLE_VALUE) return NULL;
    memset(&me, 0, sizeof(me));
    me.dwSize = sizeof(me);
    if (Module32FirstW(snap, &me)) {
        do {
            if (ends_with_ci(me.szModule, base_name) || ends_with_ci(me.szExePath, base_name)) {
                h = me.hModule;
                break;
            }
        } while (Module32NextW(snap, &me));
    }
    CloseHandle(snap);
    return h;
}

// 取模块 .text 的 [start, end) 区间
static int sp_module_text_range(HMODULE mod, BYTE **pstart, BYTE **pend)
{
    BYTE *base = (BYTE *)mod;
    IMAGE_DOS_HEADER *dos;
    IMAGE_NT_HEADERS *nt;
    IMAGE_SECTION_HEADER *sec;
    int i;

    *pstart = *pend = NULL;
    if (!base) return 0;
    __try {
        dos = (IMAGE_DOS_HEADER *)base;
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
        nt = (IMAGE_NT_HEADERS *)(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;
        sec = IMAGE_FIRST_SECTION(nt);
        for (i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
            if (memcmp(sec[i].Name, ".text", 5) == 0) {
                *pstart = base + sec[i].VirtualAddress;
                *pend   = *pstart + (sec[i].Misc.VirtualSize ? sec[i].Misc.VirtualSize
                                                             : sec[i].SizeOfRawData);
                return 1;
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *pstart = *pend = NULL;
    }
    return 0;
}

// 在 [start,end) 里扫一条 AOB，返回第 nHit（0 起）个命中；越界/异常返回 NULL
static BYTE *sp_scan_aob(BYTE *start, BYTE *end, const SP_SIG *sig, int nHit)
{
    const unsigned char *pat = sig->bytes;
    const char *mask = sig->mask;
    unsigned len = sig->len;
    unsigned first_len = 0;
    BYTE *p, *limit;
    int seen = 0;

    if (!start || !end || !pat || !mask || !len || (unsigned)(end - start) < len) return NULL;

    // 用「首个必须匹配的字节」做快速跳转，避免逐字节全比
    while (first_len < len && mask[first_len] != 'x') first_len++;

    limit = end - len;
    p = start + first_len;
    __try {
        while (p <= limit) {
            BYTE *q = (BYTE *)memchr(p, pat[first_len], (size_t)(limit - p + 1));
            unsigned k;
            if (!q) return NULL;
            {
                BYTE *base = q - first_len;
                for (k = 0; k < len; ++k) {
                    if (mask[k] == 'x' && base[k] != pat[k]) break;
                }
                if (k == len) {
                    if (seen == nHit) return base;
                    ++seen;
                }
            }
            p = q + 1;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return NULL;
    }
    return NULL;
}

// 改写内存里的字节（apply + hard 才用）。失败返回 FALSE，绝不抛异常出来。
static BOOL sp_write_bytes(PVOID addr, const void *src, SIZE_T n)
{
    DWORD old = 0, tmp = 0;
    BOOL ok = FALSE;

    if (!addr || !src || !n) return FALSE;
    __try {
        if (!VirtualProtect(addr, n, PAGE_EXECUTE_READWRITE, &old)) return FALSE;
        memcpy(addr, src, n);
        VirtualProtect(addr, n, old, &tmp);
        FlushInstructionCache(GetCurrentProcess(), addr, n);
        ok = TRUE;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        ok = FALSE;
    }
    return ok;
}

// 把函数开头改成 "xor eax,eax; ret"（返回 0）或 "mov eax,1; ret"（返回 1）
// 只覆盖 3/6 字节：因为 C3 之后不会再执行，后面残留的原字节是死代码。
static BOOL sp_patch_return(PVOID func, int value)
{
    static const unsigned char ret0[] = { 0x33, 0xC0, 0xC3 };                    // xor eax,eax; ret
    static const unsigned char ret1[] = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 };   // mov eax,1;   ret
    if (!func) return FALSE;
    return value ? sp_write_bytes(func, ret1, sizeof(ret1))
                 : sp_write_bytes(func, ret0, sizeof(ret0));
}

// -----------------------------------------------------------------------------
// SP.3  句柄溯源伪装（L1）—— 本方案最明确的指纹，优先做
// -----------------------------------------------------------------------------
// 背景：hook.c 把 package.json 的读重定向到 package.patched.json，返回的是
// patched 文件的句柄。QQNT.dll 导入了 GetFinalPathNameByHandleW /
// GetFileInformationByHandle(Ex)，只要它拿句柄反查一次，就能看见 patched 路径。
// 下面三个 detour 让它"反查不到"。
//
// 【常开】三个 detour 内部**不再看 g_sp_mode**：只要命中"我们自己换出去的那个
// patched 句柄"就一律按原版文件回答。它只影响我们自己换出来的句柄，别的句柄
// 原样透传，零副作用——所以 observe/apply 都常开，和
// SP_Detour_GetFinalPathNameByHandleW "永远改回原版路径" 保持一致。
// "要不要装这三个 hook"仍然由 g_sp_targets & SP_T_HANDLE 控制（默认含 handle）。

// 复用 hook.c 的 PATCHED_NAME / 名字长度；这里只做"把 patched 名换回原版名"的替换
#define SP_PATCHED_NAME L"package.patched.json"
#define SP_ORIG_NAME    L"package.json"

// 把 "...\package.patched.json" 改回 "...\package.json"（原地、长度只会变短）
static BOOL sp_rewrite_patched_path(wchar_t *s, size_t cch)
{
    size_t n, p, o;

    if (!s || !*s) return FALSE;
    n = wcslen(s);
    p = wcslen(SP_PATCHED_NAME);
    o = wcslen(SP_ORIG_NAME);
    if (n < p || n + 1 > cch) return FALSE;
    if (!ends_with_ci(s, SP_PATCHED_NAME)) return FALSE;
    memcpy(s + (n - p), SP_ORIG_NAME, (o + 1) * sizeof(wchar_t));
    return TRUE;
}

// 由 patched 路径推出原版路径（用于"按原版文件回答文件信息"）
static BOOL sp_orig_path_from_patched(const wchar_t *patched, wchar_t *out, size_t cch)
{
    size_t n, p, o;

    if (!patched || !out || !cch) return FALSE;
    n = wcslen(patched);
    p = wcslen(SP_PATCHED_NAME);
    o = wcslen(SP_ORIG_NAME);
    if (n < p || n - p + o + 1 > cch) return FALSE;
    if (!ends_with_ci(patched, SP_PATCHED_NAME)) return FALSE;
    memcpy(out, patched, (n - p) * sizeof(wchar_t));
    memcpy(out + (n - p), SP_ORIG_NAME, (o + 1) * sizeof(wchar_t));
    return TRUE;
}

typedef DWORD (WINAPI *PFN_GetFinalPathNameByHandleW)(HANDLE, LPWSTR, DWORD, DWORD);

static DWORD WINAPI SP_Detour_GetFinalPathNameByHandleW(HANDLE h, LPWSTR buf, DWORD cch, DWORD flags)
{
    PFN_GetFinalPathNameByHandleW orig = (PFN_GetFinalPathNameByHandleW)g_sp_gfpnh_orig;
    DWORD r;

    if (!orig) return 0;
    r = orig(h, buf, cch, flags);
    if (r == 0 || !buf) return r;

    // 只有"我们换出来的句柄"才会命中这个后缀；命中就把名字改回原版
    if (ends_with_ci(buf, SP_PATCHED_NAME)) {
        wchar_t tmp[MAX_PATH];
        wcsncpy_s(tmp, _countof(tmp), buf, _TRUNCATE);
        if (sp_rewrite_patched_path(tmp, _countof(tmp))) {
            size_t m = wcslen(tmp);
            if (m + 1 <= (size_t)cch) {
                memcpy(buf, tmp, (m + 1) * sizeof(wchar_t));
                liuli_log(L"%s api=GetFinalPathNameByHandleW 命中换源句柄, 已回报原版路径",
                          SP_LOG_PREFIX);
                return (DWORD)m;
            }
        }
    }
    return r;
}

typedef BOOL (WINAPI *PFN_GetFileInformationByHandle)(HANDLE, LPBY_HANDLE_FILE_INFORMATION);
typedef BOOL (WINAPI *PFN_GetFileInformationByHandleEx)(HANDLE, FILE_INFO_BY_HANDLE_CLASS, LPVOID, DWORD);

// 判断这个句柄是不是我们的 patched 文件；是则返回打开的原版句柄（调用方负责关）
static HANDLE sp_open_orig_for_handle(HANDLE h)
{
    PFN_GetFinalPathNameByHandleW gfp = (PFN_GetFinalPathNameByHandleW)g_sp_gfpnh_orig;
    wchar_t path[MAX_PATH], orig[MAX_PATH];
    DWORD n;
    HANDLE h2 = INVALID_HANDLE_VALUE;

    if (!gfp || !g_orig) return INVALID_HANDLE_VALUE;   // g_orig = hook.c 里真正的 CreateFileW
    n = gfp(h, path, (DWORD)_countof(path), FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
    if (n == 0 || n >= (DWORD)_countof(path)) return INVALID_HANDLE_VALUE;
    if (!sp_orig_path_from_patched(path, orig, _countof(orig))) return INVALID_HANDLE_VALUE;
    __try {
        h2 = g_orig(orig, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                    OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        h2 = INVALID_HANDLE_VALUE;
    }
    return h2;
}

static BOOL WINAPI SP_Detour_GetFileInformationByHandle(HANDLE h, LPBY_HANDLE_FILE_INFORMATION info)
{
    PFN_GetFileInformationByHandle orig = (PFN_GetFileInformationByHandle)g_sp_gfihb_orig;
    HANDLE h2;
    BOOL ok;

    if (!orig) return FALSE;
    // 不再有 mode==APPLY 的门控：命中换源句柄就一律按原版文件回答（常开、零副作用）
    h2 = sp_open_orig_for_handle(h);
    if (h2 != INVALID_HANDLE_VALUE) {
        ok = orig(h2, info);           // 用原版文件的信息回答
        CloseHandle(h2);
        if (ok) {
            liuli_log(L"%s api=GetFileInformationByHandle 换源句柄按原版文件回答", SP_LOG_PREFIX);
            return TRUE;
        }
    }
    return orig(h, info);              // 任何异常都回退真实结果
}

static BOOL WINAPI SP_Detour_GetFileInformationByHandleEx(HANDLE h, FILE_INFO_BY_HANDLE_CLASS cls,
                                                          LPVOID buf, DWORD sz)
{
    PFN_GetFileInformationByHandleEx orig = (PFN_GetFileInformationByHandleEx)g_sp_gfihbex_orig;
    HANDLE h2;
    BOOL ok;

    if (!orig) return FALSE;
    // 同上：mode==APPLY 门控已去掉，句柄伪装常开
    h2 = sp_open_orig_for_handle(h);
    if (h2 != INVALID_HANDLE_VALUE) {
        ok = orig(h2, cls, buf, sz);
        CloseHandle(h2);
        if (ok) {
            liuli_log(L"%s api=GetFileInformationByHandleEx cls=%d 换源句柄按原版文件回答",
                      SP_LOG_PREFIX, (int)cls);
            return TRUE;
        }
    }
    return orig(h, cls, buf, sz);
}

// -----------------------------------------------------------------------------
// SP.4  自保护目标探针（observe）/ 中和（apply）
// -----------------------------------------------------------------------------
typedef ULONG_PTR (WINAPI *PFN_SP_ANY)(ULONG_PTR, ULONG_PTR, ULONG_PTR, ULONG_PTR);

// sigE：wrapper.node 的 [FGESDK] 文件扫描原语（CreateFileW + 1MB 分块 ReadFile）
// sigD#1 / sigD#2：同 SDK 的模块探测/打点
// sigB：QQNT.dll 的 node/V8 内部函数（**只观测，apply 模式下也不改**）
//
// 注意（这也是 6b 节把这三个位从默认目标里摘掉的原因）：x64 下 4 个整型参数在
// rcx/rdx/r8/r9，原样透传对 4 参函数没问题；但这些函数真实参数个数未知，若 >4 参，
// 第 5+ 个参数在栈上，而 detour 转发 orig(a1..a4) 是一次**新的调用**，那一格已经是
// 新的栈帧，等于给原函数塞垃圾。所以默认不挂，只能由配置显式打开。
#define SP_DEFINE_DETOUR(fn, idx, label)                                            \
    static ULONG_PTR WINAPI fn(ULONG_PTR a1, ULONG_PTR a2, ULONG_PTR a3, ULONG_PTR a4) \
    {                                                                               \
        PFN_SP_ANY orig = (PFN_SP_ANY)g_sp_targets_orig[idx];                        \
        LONG n = InterlockedIncrement(&g_sp_seq);                                    \
        wchar_t caller[128];                                                         \
        describe_addr((ULONG_PTR)_ReturnAddress(), caller, _countof(caller));        \
        liuli_log(L"%s event=probe target=" label L" seq=%ld mode=%s caller=%s",     \
                  SP_LOG_PREFIX, n,                                                  \
                  (g_sp_mode == SP_MODE_APPLY ? L"apply" : L"observe"), caller);     \
        if (!orig) return 0;                                                         \
        if (g_sp_mode == SP_MODE_APPLY && (g_sp_targets & g_sp_targets_flag[idx])) {  \
            liuli_log(L"%s event=neutralize target=" label L" ret=%d", SP_LOG_PREFIX, \
                      g_sp_apply_value);                                             \
            return (ULONG_PTR)(LONG)g_sp_apply_value;                                \
        }                                                                            \
        return orig(a1, a2, a3, a4);                                                 \
    }

SP_DEFINE_DETOUR(SP_Detour_SigE,  0, L"sigE")
SP_DEFINE_DETOUR(SP_Detour_SigD1, 1, L"sigD1")
SP_DEFINE_DETOUR(SP_Detour_SigD2, 2, L"sigD2")
SP_DEFINE_DETOUR(SP_Detour_SigB,  3, L"sigB")

// -----------------------------------------------------------------------------
// SP.5  MessageBoxW —— 识别「文件已损坏」，apply 时吞掉
// -----------------------------------------------------------------------------
typedef int (WINAPI *PFN_MessageBoxW)(HWND, LPCWSTR, LPCWSTR, UINT);

static BOOL sp_is_damaged_text(LPCWSTR s)
{
    if (!s) return FALSE;
    return ci_contains(s, L"文件已损坏") || ci_contains(s, L"请重新安装QQ");
}

static int WINAPI SP_Detour_MessageBoxW(HWND hWnd, LPCWSTR text, LPCWSTR caption, UINT type)
{
    PFN_MessageBoxW orig = (PFN_MessageBoxW)g_sp_msgbox_orig;
    wchar_t caller[128];
    BOOL damaged = sp_is_damaged_text(text) || sp_is_damaged_text(caption);

    if (!orig) return 0;
    if (damaged) {
        describe_addr((ULONG_PTR)_ReturnAddress(), caller, _countof(caller));
        liuli_log(L"%s event=damaged seq=%ld caller=%s caption=\"%s\"",
                  SP_LOG_PREFIX, (long)InterlockedIncrement(&g_sp_seq),
                  caller, caption ? caption : L"");
        if (g_sp_mode == SP_MODE_APPLY && (g_sp_targets & SP_T_MSGBOX)) {
            liuli_log(L"%s event=damaged-suppressed", SP_LOG_PREFIX);
            return IDOK;    // 只吞提示，不改变进程后续（QQ 该退出还是会退出）
        }
    }
    return orig(hWnd, text, caption, type);
}

// -----------------------------------------------------------------------------
// SP.6  NtQueryInformationProcess —— 反调试探测（等价 BetterUniverse 那一条）
// -----------------------------------------------------------------------------
typedef LONG (WINAPI *PFN_NtQueryInformationProcess)(HANDLE, ULONG, PVOID, ULONG, PULONG);

#define SP_PROCESS_DEBUG_PORT          7
#define SP_PROCESS_DEBUG_OBJECT_HANDLE 30
#define SP_PROCESS_DEBUG_FLAGS         31
#define SP_STATUS_SUCCESS              0x00000000L
#define SP_STATUS_PORT_NOT_SET         0xC0000353L

static LONG WINAPI SP_Detour_NtQueryInformationProcess(HANDLE h, ULONG cls, PVOID buf,
                                                       ULONG len, PULONG ret)
{
    PFN_NtQueryInformationProcess orig = (PFN_NtQueryInformationProcess)g_sp_ntqip_orig;
    LONG st;
    BOOL apply = (g_sp_mode == SP_MODE_APPLY && (g_sp_targets & SP_T_NTQUERY));

    if (!orig) return SP_STATUS_SUCCESS;
    if (!apply) {
        st = orig(h, cls, buf, len, ret);
        if ((cls == SP_PROCESS_DEBUG_PORT || cls == SP_PROCESS_DEBUG_OBJECT_HANDLE ||
             cls == SP_PROCESS_DEBUG_FLAGS) && st == SP_STATUS_SUCCESS) {
            liuli_log(L"%s api=NtQueryInformationProcess cls=%lu（QQ 在查调试状态）",
                      SP_LOG_PREFIX, (unsigned long)cls);
        }
        return st;
    }

    switch (cls) {
    case SP_PROCESS_DEBUG_PORT:          // 无调试器 → Port 为 0
        if (buf && len >= sizeof(ULONG_PTR)) { *(ULONG_PTR *)buf = 0; }
        if (ret) *ret = (ULONG)sizeof(ULONG_PTR);
        return SP_STATUS_SUCCESS;
    case SP_PROCESS_DEBUG_OBJECT_HANDLE: // 无调试器 → STATUS_PORT_NOT_SET
        if (buf && len >= sizeof(ULONG_PTR)) { *(ULONG_PTR *)buf = 0; }
        if (ret) *ret = (ULONG)sizeof(ULONG_PTR);
        return SP_STATUS_PORT_NOT_SET;
    case SP_PROCESS_DEBUG_FLAGS:         // 1 = 未被调试（语义是反的）
        if (buf && len >= sizeof(ULONG)) { *(ULONG *)buf = 1; }
        if (ret) *ret = (ULONG)sizeof(ULONG);
        return SP_STATUS_SUCCESS;
    default:
        return orig(h, cls, buf, len, ret);
    }
}

// -----------------------------------------------------------------------------
// SP.7  K32EnumProcessModules —— 把本 DLL / 我们的代理从模块列表里摘掉
// -----------------------------------------------------------------------------
typedef BOOL (WINAPI *PFN_EnumProcessModules)(HANDLE, HMODULE *, DWORD, LPDWORD);

static BOOL WINAPI SP_Detour_EnumProcessModules(HANDLE proc, HMODULE *mods, DWORD cb, LPDWORD needed)
{
    PFN_EnumProcessModules orig = (PFN_EnumProcessModules)g_sp_enum_orig;
    BOOL ok;
    DWORD n, i, j;

    if (!orig) return FALSE;
    ok = orig(proc, mods, cb, needed);
    if (!g_sp_hide_self || !ok || !mods || !needed) return ok;

    n = (*needed >= sizeof(HMODULE)) ? (*needed / sizeof(HMODULE)) : 0;
    if (n > cb / sizeof(HMODULE)) n = cb / sizeof(HMODULE);

    __try {
        for (i = 0, j = 0; i < n; ++i) {
            HMODULE m = mods[i];
            if (m && (m == g_self || m == (HMODULE)GetModuleHandleW(L"winmm.dll"))) {
                continue;                 // 摘掉我们自己
            }
            mods[j++] = m;
        }
        if (j != n) {
            *needed = j * (DWORD)sizeof(HMODULE);
            liuli_log(L"%s api=K32EnumProcessModules 已隐藏 %lu 个模块（现在 %lu 个）",
                      SP_LOG_PREFIX, (unsigned long)(n - j), (unsigned long)j);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return ok;
    }
    return ok;
}

// -----------------------------------------------------------------------------
// SP.8  配置读取 / 目标定位 / 挂钩
// -----------------------------------------------------------------------------
static void sp_parse_mode(const wchar_t *v)
{
    wchar_t buf[32];

    if (!v) return;
    wcsncpy_s(buf, _countof(buf), v, _TRUNCATE);
    trim_w(buf);                        // 环境变量可能带尾随空白
    if (_wcsicmp(buf, L"off") == 0)          g_sp_mode = SP_MODE_OFF;
    else if (_wcsicmp(buf, L"apply") == 0)   g_sp_mode = SP_MODE_APPLY;
    else                                     g_sp_mode = SP_MODE_OBSERVE;
}

static void sp_parse_targets(const wchar_t *v)
{
    wchar_t buf[512];
    wchar_t *ctx = NULL, *tok;
    unsigned mask = 0;

    if (!v || !*v) return;
    wcsncpy_s(buf, _countof(buf), v, _TRUNCATE);
    for (tok = wcstok_s(buf, L",", &ctx); tok; tok = wcstok_s(NULL, L",", &ctx)) {
        trim_w(tok);
        if (_wcsicmp(tok, L"sige") == 0)        mask |= SP_T_SIGE;
        else if (_wcsicmp(tok, L"sigd") == 0)   mask |= (SP_T_SIGD1 | SP_T_SIGD2);
        else if (_wcsicmp(tok, L"sigd1") == 0)  mask |= SP_T_SIGD1;
        else if (_wcsicmp(tok, L"sigd2") == 0)  mask |= SP_T_SIGD2;
        else if (_wcsicmp(tok, L"sigb") == 0)   mask |= SP_T_SIGB;
        else if (_wcsicmp(tok, L"msgbox") == 0) mask |= SP_T_MSGBOX;
        else if (_wcsicmp(tok, L"handle") == 0) mask |= SP_T_HANDLE;
        else if (_wcsicmp(tok, L"ntquery") == 0)mask |= SP_T_NTQUERY;
        else if (_wcsicmp(tok, L"enum") == 0)   mask |= SP_T_ENUM;
        else if (_wcsicmp(tok, L"all") == 0)    mask |= 0xFFFFFFFFu;
    }
    if (mask) g_sp_targets = mask;
}

// 环境变量覆盖（ini 已在 apply_kv 里落过值，这里后跑 → env 优先级更高）
static void sp_load_cfg(void)
{
    wchar_t v[512];

    if (env_get(L"LIULI_SP", v, _countof(v)))         sp_parse_mode(v);
    if (env_get(L"LIULI_SP_TARGETS", v, _countof(v))) sp_parse_targets(v);
    if (env_get(L"LIULI_SP_HIDE", v, _countof(v)))    g_sp_hide_self = _wtoi(v) ? 1 : 0;
    if (env_get(L"LIULI_SP_HARD", v, _countof(v)))    g_sp_hard_patch = _wtoi(v) ? 1 : 0;
    if (env_get(L"LIULI_SP_RET", v, _countof(v)))     g_sp_apply_value = _wtoi(v);
}

// ASCII 特征码名 → 宽字符（只用于日志/模块名，不做 locale 依赖）
static void sp_widen(const char *s, wchar_t *out, size_t cch)
{
    if (!out || !cch) return;
    out[0] = 0;
    if (!s) return;
    MultiByteToWideChar(CP_ACP, 0, s, -1, out, (int)cch);
    out[cch - 1] = 0;
}

// 按模块作用域定位一个目标；返回函数入口（已按 sigB 的 +1 修正）
static PVOID sp_locate_ex(const SP_SIG *sig, int nHit, unsigned *out_rva)
{
    wchar_t wname[MAX_PATH];
    HMODULE mod;
    BYTE *s = NULL, *e = NULL, *hit;

    *out_rva = 0;
    if (!sig || !sig->module) return NULL;
    sp_widen(sig->module, wname, _countof(wname));
    mod = sp_module_by_name(wname);
    if (!mod) return NULL;
    if (!sp_module_text_range(mod, &s, &e)) return NULL;

    hit = sp_scan_aob(s, e, sig, nHit);
    if (!hit) return NULL;
    *out_rva = (unsigned)((ULONG_PTR)hit - (ULONG_PTR)mod);

    // sigB 少吃了函数首字节 0x41（push r15 的 REX）：命中处前一字节是 0x41 就回退 1 字节
    if ((sig->flag & SP_T_SIGB) && hit > s && hit[-1] == 0x41) {
        hit -= 1;
        *out_rva -= 1;
    }
    return (PVOID)hit;
}

static void sp_hook_one(const SP_SIG *sig, int nHit, PVOID detour, int slot, const char *label)
{
    unsigned rva = 0;
    PVOID fn = sp_locate_ex(sig, nHit, &rva);
    MH_STATUS st;

    if (!fn) {
        liuli_log(L"%s event=locate status=miss target=%S module=%S（版本漂移？跳过，不影响启动）",
                  SP_LOG_PREFIX, label, sig->module);
        return;
    }
    liuli_log(L"%s event=locate target=%S rva=0x%X expected=0x%X %s",
              SP_LOG_PREFIX, label, rva, sig->expected_rva,
              (sig->expected_rva && rva != sig->expected_rva) ? L"WARN-build-drift" : L"ok");

    if (g_sp_mode == SP_MODE_OFF) return;
    if (!(g_sp_targets & sig->flag)) {
        liuli_log(L"%s event=hook skip target=%S reason=target-disabled", SP_LOG_PREFIX, label);
        return;
    }

    // apply + hard_patch：不挂钩，直接把函数开头改成"返回"（等价 QQNTFileVerifyPatch 的
    // 思路，但在内存里做）。sigB 是 node/V8 内部函数，**永远不走这条路**。
    if (g_sp_mode == SP_MODE_APPLY && g_sp_hard_patch && !(sig->flag & SP_T_SIGB)) {
        BOOL ok = sp_patch_return(fn, g_sp_apply_value);
        liuli_log(L"%s event=patch status=%s target=%S rva=0x%X bytes=%s ret=%d",
                  SP_LOG_PREFIX, (ok ? L"ok" : L"fail"), label, rva,
                  (g_sp_apply_value ? L"B8 01 00 00 00 C3" : L"33 C0 C3"), g_sp_apply_value);
        return;
    }

    st = MH_CreateHook(fn, detour, &g_sp_targets_orig[slot]);
    if (st != MH_OK) {
        liuli_log(L"%s event=hook status=fail target=%S mh=%d", SP_LOG_PREFIX, label, (int)st);
        return;
    }
    st = MH_EnableHook(fn);
    if (st != MH_OK) {
        liuli_log(L"%s event=hook status=fail stage=enable target=%S mh=%d", SP_LOG_PREFIX, label, (int)st);
        return;
    }
    g_sp_targets_flag[slot] = sig->flag;
    liuli_log(L"%s event=hook status=ok target=%S fn=%p mode=%s", SP_LOG_PREFIX, label, fn,
              (g_sp_mode == SP_MODE_APPLY ? L"apply" : L"observe"));
}

// 挂钩一个按名字导出的 API（先跟 jmp 到真实实现，与第 12 节 CreateFileW 做法一致）
static void sp_hook_api(const wchar_t *dll, const char *name, PVOID detour, PVOID *porig,
                        const char *label)
{
    HMODULE mod;
    FARPROC p;
    PVOID target;
    MH_STATUS st;

    mod = GetModuleHandleW(dll);
    if (!mod) {
        liuli_log(L"%s event=api status=miss dll=%s api=%S", SP_LOG_PREFIX, dll, label);
        return;
    }
    p = GetProcAddress(mod, name);
    if (!p) {
        liuli_log(L"%s event=api status=miss api=%S", SP_LOG_PREFIX, label);
        return;
    }
    target = follow_jmps((PVOID)p);
    st = MH_CreateHook(target, detour, porig);
    if (st != MH_OK) {
        liuli_log(L"%s event=api status=fail api=%S mh=%d", SP_LOG_PREFIX, label, (int)st);
        return;
    }
    st = MH_EnableHook(target);
    liuli_log(L"%s event=api status=%s api=%S target=%p", SP_LOG_PREFIX,
              (st == MH_OK ? L"ok" : L"fail"), label, target);
}

// -----------------------------------------------------------------------------
// SP.9  入口：worker 线程里 CreateFileW hook 成功启用之后调用（见第 13 节）
// -----------------------------------------------------------------------------
void liuli_selfprotect_init(void)
{
    sp_load_cfg();

#if LIULI_PROBE
    // 探针版硬性纪律：本 DLL 只记日志、绝不改内存。就算 hook.ini / 环境变量写了
    // sp=apply 或 sp_hard=1，这里也压回 observe（AOB 目标位仍可留着做定位诊断）。
    if (g_sp_mode != SP_MODE_OBSERVE) {
        liuli_log(L"%s event=init build=probe 强制 observe（忽略配置里的 sp）", SP_LOG_PREFIX);
        g_sp_mode = SP_MODE_OBSERVE;
    }
    if (g_sp_hard_patch) {
        liuli_log(L"%s event=init build=probe 忽略 sp_hard=1", SP_LOG_PREFIX);
        g_sp_hard_patch = 0;
    }
#endif

    liuli_log(L"%s event=init mode=%s targets=0x%X hide=%d hard=%d（默认 observe，apply 需显式配置）",
              SP_LOG_PREFIX,
              (g_sp_mode == SP_MODE_APPLY ? L"apply" :
               (g_sp_mode == SP_MODE_OFF ? L"off" : L"observe")),
              g_sp_targets, g_sp_hide_self, g_sp_hard_patch);

    if (g_sp_mode == SP_MODE_OFF) {
        liuli_log(L"%s event=init skip reason=mode-off", SP_LOG_PREFIX);
        return;
    }

    // ---- A. AOB 目标（默认全关；只有 sp_targets 显式包含才会走到这里） ----
    if (g_sp_targets & SP_T_SIGE)  sp_hook_one(&sp_sigs[0], 0, (PVOID)SP_Detour_SigE,  0, "sigE");
    if (g_sp_targets & SP_T_SIGD1) sp_hook_one(&sp_sigs[1], 0, (PVOID)SP_Detour_SigD1, 1, "sigD#1");
    if (g_sp_targets & SP_T_SIGD2) sp_hook_one(&sp_sigs[2], 1, (PVOID)SP_Detour_SigD2, 2, "sigD#2");
    if (g_sp_targets & SP_T_SIGB)  sp_hook_one(&sp_sigs[3], 0, (PVOID)SP_Detour_SigB,  3, "sigB");

    // ---- B. 句柄溯源伪装（L1，常开，零副作用：只在命中换源句柄时才改写结果） ----
    if (g_sp_targets & SP_T_HANDLE) {
        sp_hook_api(L"kernel32.dll", "GetFinalPathNameByHandleW",
                    (PVOID)SP_Detour_GetFinalPathNameByHandleW, &g_sp_gfpnh_orig,
                    "GetFinalPathNameByHandleW");
        sp_hook_api(L"kernel32.dll", "GetFileInformationByHandle",
                    (PVOID)SP_Detour_GetFileInformationByHandle, &g_sp_gfihb_orig,
                    "GetFileInformationByHandle");
        sp_hook_api(L"kernel32.dll", "GetFileInformationByHandleEx",
                    (PVOID)SP_Detour_GetFileInformationByHandleEx, &g_sp_gfihbex_orig,
                    "GetFileInformationByHandleEx");
    }

    // ---- C. 提示框识别（apply 才吞） ----
    if (g_sp_targets & SP_T_MSGBOX) {
        sp_hook_api(L"user32.dll", "MessageBoxW", (PVOID)SP_Detour_MessageBoxW,
                    &g_sp_msgbox_orig, "MessageBoxW");
    }

    // ---- D. 反调试探测（apply 才拦截） ----
    if (g_sp_targets & SP_T_NTQUERY) {
        sp_hook_api(L"ntdll.dll", "NtQueryInformationProcess",
                    (PVOID)SP_Detour_NtQueryInformationProcess, &g_sp_ntqip_orig,
                    "NtQueryInformationProcess");
    }

    // ---- E. 隐藏自身模块（默认关；改了模块数组，务必 VM 验） ----
    if (g_sp_hide_self && (g_sp_targets & SP_T_ENUM)) {
        sp_hook_api(L"kernel32.dll", "K32EnumProcessModules",
                    (PVOID)SP_Detour_EnumProcessModules, &g_sp_enum_orig,
                    "K32EnumProcessModules");
    }

    liuli_log(L"%s event=init done", SP_LOG_PREFIX);
}

// -----------------------------------------------------------------------------
// 13. worker 线程：DllMain 只负责把它拉起来
// -----------------------------------------------------------------------------
static DWORD WINAPI worker(LPVOID param)
{
    ROLE role;
    const wchar_t *cl;
    wchar_t clbuf[1000];
    int i;

    (void)param;
    g_init_tid = GetCurrentThreadId();
    cache_proc_start();
    cache_proc_name();

    g_tls_slot = TlsAlloc();
    g_capture_stack = (PFN_RtlCaptureStackBackTrace)GetProcAddress(
                          GetModuleHandleW(L"kernel32.dll"), "RtlCaptureStackBackTrace");

    // ---- 配置：默认(probe) → ini(①LIULI_CFG ②同目录 ③C:\liuli-b2) → 环境变量覆盖 ----
    g_mode = MODE_PROBE;
    cache_self_path();                   // 先求出「本 DLL 同目录\hook.ini」候选路径
    load_cfg_file();
    apply_env();
#if LIULI_PROBE
    g_mode = MODE_PROBE;                 // 探针版：配置说了也不算
#endif

    liuli_log(L"event=boot ver=%s build=%s self=%p selfpath_ok=%d cfg=\"%s\" cfg_src=%s "
              L"cfg_env_miss=%d mode=%s log=\"%s\" selfdll=\"%s\"",
         LIULI_VER,
#if LIULI_PROBE
         L"probe",
#else
         L"full",
#endif
         (void *)g_self, (int)(g_self_path[0] != 0), g_cfg_path, g_cfg_src, g_cfg_env_miss,
         (g_mode == MODE_ACTIVE ? L"active" : (g_mode == MODE_OFF ? L"off" : L"probe")),
         g_log_path, g_self_path[0] ? g_self_path : L"-");

    // ---- 角色判定 ----
    cl = GetCommandLineW();
    role = classify_process();
    if (g_dump_cmdline) {
        wcsncpy_s(clbuf, _countof(clbuf), cl ? cl : L"", _TRUNCATE);
        liuli_log(L"event=cmdline role=%s text=\"%s\"",
             (role == ROLE_MAIN ? L"main" :
              (role == ROLE_SKIP_TYPE ? L"skip-type" : L"skip-exapp")), clbuf);
    }

    if (role != ROLE_MAIN && !g_force) {
        liuli_log(L"event=skip reason=%s (本进程不改 package.json，exApp 读原版)",
             (role == ROLE_SKIP_TYPE ? L"type-subprocess" : L"exapp-subprocess"));
        return 0;
    }
    if (role != ROLE_MAIN && g_force) {
        liuli_log(L"event=force role=%s (LIULI_FORCE=1，强行在主进程判定之外生效)",
             (role == ROLE_SKIP_TYPE ? L"type" : L"exapp"));
    }

    if (g_mode == MODE_OFF) {
        liuli_log(L"event=skip reason=mode-off");
        return 0;
    }

    if (g_mode == MODE_ACTIVE && g_cond_count <= 0) {
        liuli_log(L"event=warn mode=active 但 match 为空/写错 → 自动降级为 probe");
        g_mode = MODE_PROBE;
    }

    if (g_init_delay_ms > 0) Sleep((DWORD)g_init_delay_ms);

    // 拿一下条件摘要，方便对日志
    for (i = 0; i < g_cond_count; ++i) {
        const COND *k = &g_conds[i];
        liuli_log(L"event=cond idx=%d type=%d name=%s negate=%d ival=%d sval=\"%s\" off=0x%llX",
             i, (int)k->type, cond_type_name(k->type), (int)k->negate, k->ival, k->sval,
             (unsigned long long)k->offset);
    }

    install_hook();

    // 第 12b 节：自我保护 / 反检测 / 损坏提示中和。
    // 只有 CreateFileW 挂钩真的启用（MH_Initialize + MH_EnableHook 都成功）才进来：
    // 那时 MinHook 已就绪、g_orig 有效，本模块才能安全地再挂钩/改字节。
    // 注意角色判定在上面的 `role != ROLE_MAIN` 处就 return 了，所以 exApp / --type=
    // 子进程根本走不到这里——selfprotect 同样不会在子进程里跑（符合预期）。
    if (InterlockedCompareExchange(&g_installed, 0, 0)) {
        liuli_selfprotect_init();
    } else {
        liuli_log(L"event=sp-init skip reason=createfilew-hook-not-installed");
    }
    return 0;
}

// -----------------------------------------------------------------------------
// 14. DllMain：只拉起 worker，绝不在此做重活 / 绝不 LoadLibrary
// -----------------------------------------------------------------------------
BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved)
{
    (void)reserved;

    if (reason == DLL_PROCESS_ATTACH) {
        HANDLE th;
        g_self = hinst;
        DisableThreadLibraryCalls(hinst);

        th = CreateThread(NULL, 0, worker, NULL, 0, NULL);
        if (th) {
            SetThreadPriority(th, THREAD_PRIORITY_ABOVE_NORMAL);
            CloseHandle(th);
        } else {
            // 连线程都起不来就只能认了（用 OutputDebugString，不碰文件系统）
            OutputDebugStringW(L"[liuli] CreateThread failed\n");
        }
    }
    // DLL_PROCESS_DETACH：不做任何事。QQ 退出时让 MinHook 的 trampoline 自然失效。
    return TRUE;
}
