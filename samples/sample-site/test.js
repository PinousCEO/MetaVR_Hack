// Dependency-free test runner for the sample project: `node test.js`
// Exit code 0 = all checks passed. Used by the Companion Host to verify a change before it asks for approval.
const fs = require("fs");
const path = require("path");
const assert = require("assert");
const { daysUntil, formatDue, summarize } = require("./app.js");

let failed = 0;
function test(name, fn) {
  try { fn(); console.log("ok   - " + name); }
  catch (e) { failed++; console.log("FAIL - " + name + "\n       " + e.message); }
}

const html = fs.readFileSync(path.join(__dirname, "index.html"), "utf8");
const css = fs.readFileSync(path.join(__dirname, "styles.css"), "utf8");

function cssVar(name) {
  const m = css.match(new RegExp("--" + name + "\\s*:\\s*([^;]+);"));
  return m ? m[1].trim() : null;
}
function hexToRgb(hex) {
  let h = hex.replace("#", "");
  if (h.length === 3) h = h.split("").map((c) => c + c).join("");
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
}
function luminance([r, g, b]) {
  const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
}
function contrast(a, b) {
  const la = luminance(hexToRgb(a)), lb = luminance(hexToRgb(b));
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

test("date helpers", () => {
  assert.strictEqual(daysUntil("2026-11-18", "2026-11-16"), 2);
  assert.strictEqual(formatDue("2026-11-16", "2026-11-16"), "today");
  assert.strictEqual(formatDue("2026-11-17", "2026-11-16"), "tomorrow");
  assert.strictEqual(formatDue("2026-11-20", "2026-11-16"), "in 4 days");
  assert.strictEqual(formatDue("2026-11-15", "2026-11-16"), "1 day overdue");
  assert.strictEqual(formatDue("2026-11-10", "2026-11-16"), "6 days overdue");
});

test("summary line", () => {
  assert.strictEqual(summarize(["2026-11-15", "2026-11-17", "2026-11-30"], "2026-11-16"), "3 tasks, 1 due soon, 1 overdue");
});

test("page structure is intact", () => {
  assert.ok(/<title>[^<]*Momo Notes[^<]*<\/title>/.test(html), "title must mention Momo Notes");
  assert.ok(html.includes('id="summary-line"'), "summary line element is required");
  const cards = html.match(/<article class="card[^"]*"/g) || [];
  assert.ok(cards.length >= 4, "at least four task cards expected, found " + cards.length);
  const headings = html.match(/<article class="card[^"]*">\s*<h2>/g) || [];
  assert.strictEqual(headings.length, cards.length, "every card starts with a heading");
  assert.ok(html.includes('href="styles.css"') && html.includes('src="app.js"'), "stylesheet and script are linked");
});

test("stylesheet is well formed and themed through variables", () => {
  assert.strictEqual((css.match(/{/g) || []).length, (css.match(/}/g) || []).length, "unbalanced braces");
  for (const v of ["bg", "text", "accent"]) assert.ok(/^#[0-9a-fA-F]{3,6}$/.test(cssVar(v) || ""), "--" + v + " must be a hex colour");
  assert.ok(/\.card\s*{/.test(css), ".card rule is required");
});

test("body text is readable (WCAG AA contrast)", () => {
  const ratio = contrast(cssVar("text"), cssVar("bg"));
  assert.ok(ratio >= 4.5, "contrast of --text on --bg is " + ratio.toFixed(2) + ", needs 4.5");
});

console.log(failed === 0 ? "\nall checks passed" : "\n" + failed + " check(s) failed");
process.exit(failed === 0 ? 0 : 1);
