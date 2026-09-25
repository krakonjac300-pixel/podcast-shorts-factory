#!/usr/bin/env node
// PreToolUse guard for the clip-browser MCP server.
// Any browser action that could commit something on Vyro / Clipping.net
// (submitting a clip, joining a campaign, requesting a payout, deleting...)
// is escalated to an explicit user confirmation, whatever permission mode
// the session runs in. Everything else passes through untouched.

const RISKY = /\b(submit|send|confirm|withdraw|payout|pay ?out|cash ?out|claim|join|apply|accept|enroll|delete|remove|leave|publish|pay|save payment|connect wallet)\b/i;

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input = {};
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }
  const tool = String(input.tool_name || "");
  const args = input.tool_input || {};
  const element = String(args.element || "");

  let reason = null;
  if (tool.endsWith("browser_click") && RISKY.test(element)) {
    reason = `Clicking "${element}" may commit an action on a clipping platform.`;
  } else if (tool.endsWith("browser_type") && args.submit === true) {
    reason = `Typing into "${element}" and pressing Enter may submit a form.`;
  } else if (tool.endsWith("browser_press_key") && /^enter$/i.test(String(args.key || ""))) {
    reason = "Pressing Enter may submit a form on a clipping platform.";
  }

  if (reason) {
    process.stdout.write(
      JSON.stringify({
        hookSpecificOutput: {
          hookEventName: "PreToolUse",
          permissionDecision: "ask",
          permissionDecisionReason: `${reason} Confirm before it runs.`,
        },
      })
    );
  }
  process.exit(0);
});
