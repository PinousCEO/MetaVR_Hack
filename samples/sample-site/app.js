// Small pure helpers so the project has real behaviour to test.
function daysUntil(dueIso, todayIso) {
  const due = new Date(dueIso + "T00:00:00Z");
  const today = new Date(todayIso + "T00:00:00Z");
  return Math.round((due - today) / 86400000);
}

function formatDue(dueIso, todayIso) {
  const d = daysUntil(dueIso, todayIso);
  if (d < 0) return Math.abs(d) + " day" + (Math.abs(d) === 1 ? "" : "s") + " overdue";
  if (d === 0) return "today";
  if (d === 1) return "tomorrow";
  return "in " + d + " days";
}

function summarize(dueDates, todayIso) {
  const overdue = dueDates.filter((d) => daysUntil(d, todayIso) < 0).length;
  const soon = dueDates.filter((d) => { const n = daysUntil(d, todayIso); return n >= 0 && n <= 2; }).length;
  return dueDates.length + " tasks, " + soon + " due soon, " + overdue + " overdue";
}

if (typeof document !== "undefined") {
  const today = new Date().toISOString().slice(0, 10);
  const spans = Array.from(document.querySelectorAll("[data-due]"));
  spans.forEach((s) => { s.textContent = formatDue(s.dataset.due, today); });
  const line = document.getElementById("summary-line");
  if (line) line.textContent = summarize(spans.map((s) => s.dataset.due), today);
}

if (typeof module !== "undefined") module.exports = { daysUntil, formatDue, summarize };
