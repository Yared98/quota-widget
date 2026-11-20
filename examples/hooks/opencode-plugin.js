// OpenCode plugin: ask QuotaWidget for a fresh read when a session goes idle.
// Install: copy to ~/.config/opencode/plugin/ (global) or .opencode/plugin/ (one project).
// `qw refresh` returns at once (the widget, or a detached qw, does the read), so this never slows OpenCode.
import { spawn } from "node:child_process";

const PROVIDER_IDS = ["opencode"]; // provider ids from your QuotaWidget config.json

export const QuotaWidgetRefresh = async () => ({
  event: async ({ event }) => {
    if (event.type !== "session.idle") return;
    try {
      const child = spawn("qw", ["refresh", ...PROVIDER_IDS], {
        detached: true,
        stdio: "ignore",
        windowsHide: true,
      });
      child.on("error", () => {}); // qw not on PATH: ignore
      child.unref();
    } catch {
      // never break the session because of the widget
    }
  },
});
