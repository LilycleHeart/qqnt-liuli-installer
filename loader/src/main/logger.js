// LiteLoader 主进程 tee 日志：零依赖、永不抛错。
// 背景：LiteLoader 的报错平时只打在 QQ 控制台，用户来报「每次启动弹报错」时没有任何落盘证据。
// 这个 logger 把所有关键事件（启动 banner、插件 load/loaded/加载失败、LiteLoader 自己弹的错误框、
// 主进程未捕获异常）写到 <profile>/liteLoader.log，安装器诊断包会自动带上。
//
// 约束（故意为之）：
//   * 单文件 ≤512KB，超出轮转为 liteLoader.1.log（只留 1 个历史）；
//   * 每行 `yyyy-MM-dd HH:mm:ss.SSS [level] [scope] msg`，单行 ≤4KB；
//   * 任何写入/轮转失败都静默（绝不影响 QQ/LiteLoader 启动）；
//   * 只监听 uncaughtExceptionMonitor（不改 Node 默认崩溃行为），不用 uncaughtException/unhandledRejection。
const fs = require("fs");
const path = require("path");

const MAX_BYTES = 512 * 1024;
const MAX_LINE = 4 * 1024;
const FILE_NAME = "liteLoader.log";

let cachedPath = null;
let cachedSize = -1;

function resolveProfile() {
    try {
        if (globalThis.LiteLoader && LiteLoader.path && LiteLoader.path.profile) {
            return String(LiteLoader.path.profile);
        }
    } catch { }
    try {
        if (process.env && process.env.LITELOADERQQNT_PROFILE) {
            return String(process.env.LITELOADERQQNT_PROFILE);
        }
    } catch { }
    try { return path.dirname(path.dirname(__dirname)); } catch { } // <root>/src/main -> <root>
    try { return process.cwd(); } catch { }
    return ".";
}

function logPath() {
    if (cachedPath) return cachedPath;
    try { cachedPath = path.join(resolveProfile(), FILE_NAME); } catch { cachedPath = null; }
    return cachedPath;
}

function pad(value, width) {
    let s = String(value);
    while (s.length < width) s = "0" + s;
    return s;
}

function stamp() {
    try {
        const d = new Date();
        return d.getFullYear() + "-" + pad(d.getMonth() + 1, 2) + "-" + pad(d.getDate(), 2)
            + " " + pad(d.getHours(), 2) + ":" + pad(d.getMinutes(), 2) + ":" + pad(d.getSeconds(), 2)
            + "." + pad(d.getMilliseconds(), 3);
    } catch { return ""; }
}

// 脱敏：账号/邮箱/token/壁纸 dataURL/长 base64 都不落盘
function sanitize(text) {
    try {
        let s = String(text == null ? "" : text);
        s = s.replace(/(uin\s*[=:]\s*)\d+/gi, "$1***");
        s = s.replace(/(uid\s*[=:]\s*)[A-Za-z0-9_\-]+/gi, "$1***");
        s = s.replace(/u_[A-Za-z0-9]{6,}/g, "u_***");
        s = s.replace(/[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}/g, "***@***");
        s = s.replace(/(sk-)[A-Za-z0-9]{8,}/g, "$1***");
        s = s.replace(/(Bearer\s+)[A-Za-z0-9._\-]{8,}/gi, "$1***");
        s = s.replace(/data:image\/[a-z0-9.+\-]+;base64,[A-Za-z0-9+/=]{100,}/gi, "data:image/...;base64,<省略>");
        s = s.replace(/[A-Za-z0-9+/=]{300,}/g, "<长 base64 已省略>");
        return s;
    } catch { return ""; }
}

function oneLine(text) {
    try {
        let s = String(text == null ? "" : text).replace(/\r/g, " ").replace(/\n/g, " ").replace(/\t/g, " ");
        if (s.length > MAX_LINE) s = s.slice(0, MAX_LINE) + "…";
        return s;
    } catch { return ""; }
}

// 消息开头的 [xxx] 当作 scope（例如 "[Main] [slug] 加载失败" / "[dialog] ..."）
function splitScope(message) {
    try {
        const s = String(message == null ? "" : message);
        const m = /^\[([^\]\r\n]{1,40})\]\s*([\s\S]*)$/.exec(s);
        if (m) return [m[1], m[2]];
        return ["LiteLoader", s];
    } catch { return ["LiteLoader", ""]; }
}

function rotateIfNeeded(target) {
    try {
        if (cachedSize < 0) {
            try { cachedSize = fs.statSync(target).size; } catch { cachedSize = 0; }
        }
        if (cachedSize <= MAX_BYTES) return;
        const backup = path.join(path.dirname(target), "liteLoader.1.log");
        try { fs.rmSync(backup, { force: true }); } catch { }
        try { fs.renameSync(target, backup); } catch { }
        cachedSize = 0;
    } catch { }
}

function write(level, message, extra) {
    try {
        const target = logPath();
        if (!target) return;
        let text = message;
        if (extra !== undefined && extra !== null && extra !== "") {
            let extraText;
            try { extraText = (extra && extra.stack) ? extra.stack : String(extra); } catch { extraText = ""; }
            text = String(message) + " " + extraText;
        }
        const parts = splitScope(text);
        const line = stamp() + " [" + level + "] [" + parts[0] + "] " + oneLine(sanitize(parts[1])) + "\n";
        try { fs.mkdirSync(path.dirname(target), { recursive: true }); } catch { }
        rotateIfNeeded(target);
        try { fs.appendFileSync(target, line, "utf8"); } catch { }
        try { cachedSize = (cachedSize < 0 ? 0 : cachedSize) + Buffer.byteLength(line, "utf8"); } catch { }
    } catch { }
}

function info(message, extra) { write("info", message, extra); }
function warn(message, extra) { write("warn", message, extra); }
function error(message, extra) { write("error", message, extra); }
function debug(message, extra) { write("debug", message, extra); }

// 只观察、不接管：uncaughtExceptionMonitor 不会改变 Node 的默认崩溃行为
try {
    process.on("uncaughtExceptionMonitor", (error, origin) => {
        try {
            write("fatal", "[process] uncaughtException (" + String(origin || "") + ")",
                error && error.stack ? error.stack : String(error));
        } catch { }
    });
} catch { }

module.exports = { info, warn, error, debug, path: () => logPath() };
