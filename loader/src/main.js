const store = require("./main/store.js");
const { installHook } = require("./main/hook.js");
const { loadAllPlugins } = require("./main/loader.js");
const { Runtime } = require("./main/runtime.js");
const logger = require("./main/logger.js");

// 启动 banner：先落盘，后面出问题时才有上下文（版本/平台/pid/profile/禁用列表）
try {
    const cfg = LiteLoader.api.config.get("LiteLoader", require("./common/static/config.json"));
    const disabled = (cfg && Array.isArray(cfg.disabled_plugins)) ? cfg.disabled_plugins.join(",") : "";
    logger.info("[Main] LiteLoader 启动"
        + " liteloader=" + (LiteLoader.versions && LiteLoader.versions.liteloader)
        + " qqnt=" + (LiteLoader.versions && LiteLoader.versions.qqnt)
        + " electron=" + (LiteLoader.versions && LiteLoader.versions.electron)
        + " node=" + (LiteLoader.versions && LiteLoader.versions.node)
        + " platform=" + (LiteLoader.os && LiteLoader.os.platform)
        + " pid=" + process.pid
        + " qwqnt=" + (globalThis.qwqnt ? "是" : "否")
        + " profile=" + (LiteLoader.path && LiteLoader.path.profile)
        + " disabled_plugins=[" + disabled + "]");
} catch (error) {
    logger.error("[Main] 写启动 banner 失败", error && error.stack || error);
}

// 只记录、然后 rethrow：保持原来的崩溃/行为不变，但把失败原因落盘
try {
    installHook();
} catch (error) {
    logger.error("[Main] installHook 失败", error && error.stack || error);
    throw error;
}
try {
    loadAllPlugins();
} catch (error) {
    logger.error("[Main] loadAllPlugins 失败", error && error.stack || error);
    throw error;
}

const config = LiteLoader.api.config.get("LiteLoader", require("./common/static/config.json"));
for (const slug in config.deleting_plugins) store.deletePlugin(slug, [true, true], true);
for (const slug in config.installing_plugins) store.installPlugin(slug);
for (const plugin of Object.values(LiteLoader.plugins)) {
    const slug = (plugin.manifest && plugin.manifest.slug) || "(未知)";
    try {
        if (plugin.disabled) {
            logger.info(`[Main] [${slug}] 跳过：在 disabled_plugins 里`);
            continue;
        }
        if (plugin.incompatible) {
            logger.info(`[Main] [${slug}] 跳过：platform=${(plugin.manifest.platform || []).join(",")} 不含 ${LiteLoader.os.platform}`);
            continue;
        }
        if (!plugin.path.injects.main) {
            logger.info(`[Main] [${slug}] 跳过：manifest 没写 injects.main`);
            continue;
        }
        logger.info(`[Main] [${slug}] load ${plugin.path.injects.main}`);
        Runtime.registerPlugin(plugin, require(plugin.path.injects.main));
        logger.info(`[Main] [${slug}] loaded`);
    } catch (error) {
        console.log(`[Main] [${slug}]: `, error);
        logger.error(`[Main] [${slug}] 加载失败`, error && error.stack || error);
    }
}

if (!globalThis.qwqnt) {
    const main_path = "./application.asar/app_launcher/index.js";
    require(require("path").join(process.resourcesPath, "app", main_path));
    setImmediate(() => global.launcher.installPathPkgJson.main = main_path);
}
