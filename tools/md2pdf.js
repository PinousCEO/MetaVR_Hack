// Minimal Markdown -> HTML converter (headings, tables, lists, code, quotes, bold/italic/code/links) so that the
// reports can be printed to PDF with the browser that is already installed. No dependencies.
//   node tools/md2pdf.js <input.md> <output.html>
const fs = require("fs");
const [, , input, output] = process.argv;
const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const inline = (s) =>
  esc(s)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\*\*([^*]+)\*\*/g, "<b>$1</b>")
    .replace(/(^|[\s(])\*([^*\s][^*]*)\*/g, "$1<i>$2</i>")
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2">$1</a>');
const lines = fs.readFileSync(input, "utf8").replace(/\r\n/g, "\n").split("\n");
let html = [], i = 0, inList = null;
const closeList = () => { if (inList) { html.push(`</${inList}>`); inList = null; } };
while (i < lines.length) {
  const l = lines[i];
  if (l.startsWith("```")) {
    closeList(); const buf = []; i++;
    while (i < lines.length && !lines[i].startsWith("```")) buf.push(esc(lines[i++]));
    html.push("<pre>" + buf.join("\n") + "</pre>"); i++; continue;
  }
  if (/^\|.*\|\s*$/.test(l) && i + 1 < lines.length && /^\|[\s:|-]+\|\s*$/.test(lines[i + 1])) {
    closeList();
    const row = (s) => s.trim().replace(/^\||\|$/g, "").split(/(?<!\\)\|/).map((c) => c.trim().replace(/\\\|/g, "|"));
    html.push("<table><thead><tr>" + row(l).map((c) => `<th>${inline(c)}</th>`).join("") + "</tr></thead><tbody>");
    i += 2;
    while (i < lines.length && /^\|.*\|\s*$/.test(lines[i])) { html.push("<tr>" + row(lines[i]).map((c) => `<td>${inline(c)}</td>`).join("") + "</tr>"); i++; }
    html.push("</tbody></table>"); continue;
  }
  let m;
  if ((m = l.match(/^(#{1,6})\s+(.*)$/))) { closeList(); html.push(`<h${m[1].length}>${inline(m[2])}</h${m[1].length}>`); i++; continue; }
  if ((m = l.match(/^\s*(?:[-*]|\d+\.)\s+(.*)$/))) {
    const type = /^\s*\d+\./.test(l) ? "ol" : "ul";
    if (inList !== type) { closeList(); html.push(`<${type}>`); inList = type; }
    html.push(`<li>${inline(m[1])}</li>`); i++; continue;
  }
  if (l.startsWith(">")) { closeList(); html.push(`<blockquote>${inline(l.replace(/^>\s?/, ""))}</blockquote>`); i++; continue; }
  if (l.trim() === "") { closeList(); i++; continue; }
  closeList(); html.push(`<p>${inline(l)}</p>`); i++;
}
closeList();
const css = `body{font-family:"Malgun Gothic","Segoe UI",sans-serif;font-size:10.5pt;line-height:1.5;margin:28px;color:#1c1c1c}
h1{font-size:19pt;border-bottom:2px solid #333;padding-bottom:4px}h2{font-size:14pt;margin-top:22px;border-bottom:1px solid #bbb}h3{font-size:12pt}
table{border-collapse:collapse;width:100%;margin:8px 0;font-size:9pt}th,td{border:1px solid #999;padding:4px 6px;vertical-align:top;text-align:left}th{background:#eee}
code,pre{font-family:Consolas,monospace;font-size:9pt;background:#f3f3f3}pre{padding:8px;white-space:pre-wrap}blockquote{border-left:3px solid #888;margin:8px 0;padding:2px 10px;color:#333}`;
fs.writeFileSync(output, `<!doctype html><html><head><meta charset="utf-8"><title>${esc(input)}</title><style>${css}</style></head><body>${html.join("\n")}</body></html>`);
console.log("wrote " + output);
