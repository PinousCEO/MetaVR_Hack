using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using MoonSharp.Interpreter.Platforms;

namespace LivingPartner.Shared
{
    // SCRIPTED MODULES run here: a small Lua program (written by the partner's language model when the user asks for
    // something, bought and sold in the marketplace) inside a machine that can do nothing but compute and call the
    // functions its owner gives it. MoonSharp (a Lua 5.2 interpreter in plain C#) does the interpreting; this class
    // is everything around it that every kind of script needs:
    //   - the hardened machine: an ALLOW-LIST of the standard library (no io, os, load, require, debug, metatables,
    //     coroutines, pattern matching; print goes to Log; math.random is the sandbox's own seeded generator)
    //   - the budget: every call into the script may execute Budget instructions and no more ("took too long"), and
    //     may not allocate more than MaxAlloc bytes; a script can never hang or starve the app
    //   - errors: nothing ever throws out of Load / Call. The first error is kept (Error, ErrorLine, ErrorSource) and
    //     from then on the sandbox is Broken: nothing runs any more.
    //   - forgiving argument readers for the functions the owner registers (LuaArgs), the colour parser, the helpers
    //     every script gets (rnd, rndi, dist, clamp, lerp - CommonDoc is their description for the model's prompt).
    // AOT (Quest, IL2CPP): only MoonSharp's plain-value interop is used - DynValue, Table, CallbackFunction, one
    // coroutine. No UserData, no conversion of CLR objects or delegates, no dynamic expressions, no file access.
    // Used by GlassScript (games, toys and tools on the glass / the desk) and RoomScript (things in the room).

    /// <summary>A function the owner of a sandbox gives to its script. Return null for "nothing".</summary>
    public delegate DynValue LuaFn(LuaArgs a);

    public sealed class LuaSandbox
    {
        public const int DefaultBudget = 1000000;       // instructions one call into the script may execute (the busiest frame of the examples needs 50 000)
        public const int Slice = 1000;                  // the budget is counted in slices of this many
        public const int MaxSource = 24000;             // characters of source
        public const long MaxAlloc = 48L * 1024 * 1024; // bytes one call may allocate
        public const int MaxText = 60;                  // letters of a string that is shown (LuaArgs.Str cuts to this by default)
        public const string ChunkName = "script";

        /// <summary>Instructions one Load / Call may execute.</summary>
        public int Budget = DefaultBudget;
        /// <summary>The budget is counted in slices of this many instructions (smaller = the counts are more exact, a little slower: the checker uses 100).</summary>
        public int SliceSize = Slice;
        /// <summary>The first error ("" = none). Once set, nothing runs any more.</summary>
        public string Error { get; private set; }
        /// <summary>The line of the script's source the error is on (1-based; 0 = unknown), and that line's text.</summary>
        public int ErrorLine { get; private set; }
        public string ErrorSource { get; private set; }
        public bool Broken { get { return Error.Length > 0; } }
        /// <summary>Instructions the last call used / the most any call used / all of them (counted in whole slices: an upper bound).</summary>
        public int LastInstructions { get; private set; }
        public int PeakInstructions { get; private set; }
        public long TotalInstructions { get; private set; }
        public int Calls { get; private set; }
        /// <summary>Real seconds the last call took / the longest call. A call that is still running after MaxSeconds is stopped like one over the budget.</summary>
        public double LastSeconds { get; private set; }
        public double PeakSeconds { get; private set; }
        public double MaxSeconds = 0.5;
        /// <summary>What the last successful Call returned (Nil when nothing).</summary>
        public DynValue Result { get; private set; }
        /// <summary>Where print() goes (null = nowhere). At most MaxPrints lines are passed on.</summary>
        public Action<string> Log;
        public const int MaxPrints = 200;
        /// <summary>The script's random numbers: rnd, rndi and math.random all draw from here.</summary>
        public readonly GlassRng Rng = new GlassRng();
        /// <summary>Things that are not errors but that the writer of the script should know ("unknown colour 'purple'"). Each once, at most MaxNotes.</summary>
        public readonly List<string> Notes = new List<string>();
        public const int MaxNotes = 24;

        /// <summary>The machine itself, for owners that need more than Def / Set / Call (stay with DynValue and Table: no UserData).</summary>
        public Script Lua { get { return script; } }
        public Table Globals { get { return script.Globals; } }
        /// <summary>The source that was loaded ("" before Load).</summary>
        public string Source { get; private set; }

        readonly Script script;
        Coroutine driver;
        DynValue driverFn;
        readonly DynValue[] callArgs = new DynValue[1 + MaxCallArgs];
        public const int MaxCallArgs = 6;
        int prints;
        static readonly DynValue[] NoArgs = new DynValue[0];

        static LuaSandbox()
        {
            // nothing of MoonSharp may touch the file system, the console or the environment, on any platform
            try { Script.GlobalOptions.Platform = new LimitedPlatformAccessor(); } catch (Exception) { }
            try { Script.DefaultOptions.ScriptLoader = new NoLoader(); } catch (Exception) { }
        }

        sealed class NoLoader : IScriptLoader
        {
            public object LoadFile(string file, Table globalContext) { throw new ScriptRuntimeException("files cannot be loaded here"); }
            public string ResolveFileName(string filename, Table globalContext) { return null; }
            public string ResolveModuleName(string modname, Table globalContext) { return null; }
        }

        public LuaSandbox(int seed = 1)
        {
            Error = ""; ErrorSource = ""; Source = ""; Result = DynValue.Nil;
            Rng.Seed(seed);
            script = new Script(CoreModules.Preset_HardSandbox);
            script.Options.ScriptLoader = new NoLoader();
            script.Options.UseLuaErrorLocations = true;                 // "script:12: message"
            script.Options.DebugPrint = Print;
            script.Options.DebugInput = null;
            Harden();
            DefCommon();
        }

        // ------------------------------------------------------------------ the allow-list

        /// <summary>The globals a script finds besides what its owner defines (functions and the three library tables).</summary>
        public static readonly string[] AllowedGlobals =
        {
            "_G", "_VERSION", "assert", "error", "ipairs", "next", "pairs", "select", "tonumber", "tostring", "type", "unpack",
            "print", "collectgarbage", "math", "string", "table",
        };
        public static readonly string[] AllowedString = { "byte", "char", "find", "format", "len", "lower", "upper", "rep", "reverse", "sub" };
        public static readonly string[] AllowedTable = { "concat", "insert", "remove", "sort", "unpack", "pack" };
        // (math is pure arithmetic: all of it stays; random / randomseed are replaced)

        // Sorting is done by the script machine itself (a comparison function called from C# could not be stopped by the budget).
        const string LibSource = @"
local function sort(t, lt)
  local n = #t
  if n < 2 then return end
  if lt == nil then lt = function(a, b) return a < b end end
  local a, b, width = t, {}, 1
  while width < n do
    local i = 1
    while i <= n do
      local mid, hi = i + width, i + 2 * width
      if mid > n + 1 then mid = n + 1 end
      if hi > n + 1 then hi = n + 1 end
      local l, r, k = i, mid, i
      while l < mid and r < hi do
        if lt(a[r], a[l]) then b[k] = a[r]; r = r + 1 else b[k] = a[l]; l = l + 1 end
        k = k + 1
      end
      while l < mid do b[k] = a[l]; l = l + 1; k = k + 1 end
      while r < hi do b[k] = a[r]; r = r + 1; k = k + 1 end
      i = hi
    end
    a, b = b, a
    width = width * 2
  end
  if a ~= t then for i = 1, n do t[i] = a[i] end end
end
return sort
";

        // One coroutine lives as long as the sandbox and runs every call (a new coroutine for every call would cost
        // megabytes of stack each time). It is resumed with the function and its arguments and yields what that returned.
        const string DriverSource = @"
local y = ...
return function(f, a, b, c, d, e, g)
  while true do
    f, a, b, c, d, e, g = y(f(a, b, c, d, e, g))
  end
end
";

        void Harden()
        {
            Table g = script.Globals;
            Keep(g, AllowedGlobals);
            Table s = TableOf(g, "string"), t = TableOf(g, "table"), m = TableOf(g, "math");
            if (s != null)
            {
                Keep(s, AllowedString);
                DynValue rep = s.Get("rep"), find = s.Get("find"), format = s.Get("format");
                s.Set("rep", DynValue.NewCallback((ctx, a) =>
                {
                    string str = a.Count > 0 && a[0].Type == DataType.String ? a[0].String : a.Count > 0 && a[0].Type == DataType.Number ? Num(a[0].Number) : "";
                    double n = a.Count > 1 && a[1].Type == DataType.Number ? a[1].Number : 0;
                    if (!(n >= 1) || str.Length == 0) return DynValue.NewString("");
                    if (n * str.Length > MaxString) throw new ScriptRuntimeException("string.rep: the result would be longer than " + MaxString + " letters");
                    var sb = new StringBuilder(str.Length * (int)n);
                    for (int i = 0; i < (int)n; i++) sb.Append(str);
                    return DynValue.NewString(sb.ToString());
                }, "rep"));
                // find: plain text only (patterns can take for ever)
                s.Set("find", DynValue.NewCallback((ctx, a) =>
                {
                    string str = a.Count > 0 ? Text(a[0]) : "", what = a.Count > 1 ? Text(a[1]) : "";
                    int from = a.Count > 2 && a[2].Type == DataType.Number ? SafeInt(a[2].Number, 1) : 1;
                    if (from < 0) from = str.Length + from + 1;
                    if (from < 1) from = 1;
                    if (from > str.Length + 1) return DynValue.Nil;
                    int at = str.IndexOf(what, from - 1, StringComparison.Ordinal);
                    if (at < 0) return DynValue.Nil;
                    return DynValue.NewTuple(DynValue.NewNumber(at + 1), DynValue.NewNumber(at + what.Length));
                }, "find"));
                if (format.Type == DataType.ClrFunction)
                {
                    CallbackFunction inner = format.Callback;
                    s.Set("format", DynValue.NewCallback((ctx, a) =>
                    {
                        string f = a.Count > 0 && a[0].Type == DataType.String ? a[0].String : null;
                        if (f == null) return DynValue.NewString(a.Count > 0 ? Text(a[0]) : "");
                        if (f.Length > 200) throw new ScriptRuntimeException("string.format: the format is too long");
                        int run = 0;
                        for (int i = 0; i < f.Length; i++)
                        {
                            run = f[i] >= '0' && f[i] <= '9' ? run + 1 : 0;
                            if (run > 2) throw new ScriptRuntimeException("string.format: widths over 99 are not allowed");
                        }
                        for (int i = 1; i < a.Count; i++)
                            if (a[i].Type == DataType.String && a[i].String.Length > MaxString) throw new ScriptRuntimeException("string.format: a string is too long");
                        return inner.ClrCallback(ctx, a);
                    }, "format"));
                }
            }
            if (t != null) Keep(t, AllowedTable);
            if (m != null)
            {
                m.Set("random", DynValue.NewCallback((ctx, a) =>
                {
                    if (a.Count == 0 || a[0].Type != DataType.Number) return DynValue.NewNumber(Rng.Next01());
                    int lo = 1, hi = SafeInt(a[0].Number, 1);
                    if (a.Count > 1 && a[1].Type == DataType.Number) { lo = hi; hi = SafeInt(a[1].Number, lo); }
                    return DynValue.NewNumber(RandomInt(lo, hi));
                }, "random"));
                m.Set("randomseed", DynValue.NewCallback((ctx, a) => DynValue.Nil, "randomseed"));
            }
            g.Set("collectgarbage", DynValue.NewCallback((ctx, a) => DynValue.NewNumber(0), "collectgarbage"));
            g.Set("print", DynValue.NewCallback((ctx, a) =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < a.Count && sb.Length < 400; i++) { if (i > 0) sb.Append(' '); sb.Append(Text(a[i])); }
                Print(sb.ToString());
                return DynValue.Nil;
            }, "print"));
            // the library written in Lua, and the coroutine that runs every call
            try
            {
                DynValue sort = script.Call(script.LoadString(LibSource, null, "lib"));
                if (t != null) t.Set("sort", sort);
                DynValue yield = DynValue.NewCallback((ctx, a) => DynValue.NewYieldReq(a.GetArray()), "yield");
                driverFn = script.Call(script.LoadString(DriverSource, null, "driver"), yield);
            }
            catch (Exception e) { Fail("the script machine could not be set up: " + e.Message); }
        }

        public const int MaxString = 20000;             // letters string.rep / string.format may produce or take

        static void Keep(Table t, string[] names)
        {
            var drop = new List<DynValue>();
            foreach (DynValue k in t.Keys)
            {
                bool keep = false;
                if (k.Type == DataType.String) for (int i = 0; i < names.Length; i++) if (names[i] == k.String) { keep = true; break; }
                if (!keep) drop.Add(k);
            }
            for (int i = 0; i < drop.Count; i++) t.Set(drop[i], DynValue.Nil);
        }

        static Table TableOf(Table g, string name)
        {
            DynValue v = g.Get(name);
            return v != null && v.Type == DataType.Table ? v.Table : null;
        }

        void Print(string s)
        {
            if (Log == null || prints >= MaxPrints) return;
            prints++;
            if (s == null) s = "";
            if (s.Length > 400) s = s.Substring(0, 400);
            try { Log(s); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ what the owner defines

        /// <summary>A global function for the script.</summary>
        public void Def(string name, LuaFn fn)
        {
            script.Globals.Set(name, Callback(name, fn));
        }

        /// <summary>A function value (for a table of methods).</summary>
        public DynValue Callback(string name, LuaFn fn)
        {
            LuaSandbox box = this;
            return DynValue.NewCallback((ctx, a) =>
            {
                DynValue r = fn(new LuaArgs(box, name, a));
                return r ?? DynValue.Nil;
            }, name);
        }

        public void Set(string name, double value) { script.Globals.Set(name, DynValue.NewNumber(value)); }
        public void Set(string name, string value) { script.Globals.Set(name, DynValue.NewString(value ?? "")); }
        public void Set(string name, bool value) { script.Globals.Set(name, DynValue.NewBoolean(value)); }
        public void Set(string name, DynValue value) { script.Globals.Set(name, value ?? DynValue.Nil); }
        public DynValue Get(string name) { return script.Globals.Get(name) ?? DynValue.Nil; }
        /// <summary>The script has a global function of this name.</summary>
        public bool Has(string name) { DynValue v = script.Globals.Get(name); return v != null && v.Type == DataType.Function; }
        public Table NewTable() { return new Table(script); }

        /// <summary>The names of every global function there is right now (the owner's and the library's "name" / "table.name"), sorted.</summary>
        public List<string> FunctionNames()
        {
            var list = new List<string>();
            foreach (TablePair p in script.Globals.Pairs)
            {
                if (p.Key.Type != DataType.String) continue;
                if (p.Value.Type == DataType.Function || p.Value.Type == DataType.ClrFunction) list.Add(p.Key.String);
                else if (p.Value.Type == DataType.Table && p.Key.String != "_G")
                    foreach (TablePair q in p.Value.Table.Pairs)
                        if (q.Key.Type == DataType.String && (q.Value.Type == DataType.Function || q.Value.Type == DataType.ClrFunction)) list.Add(p.Key.String + "." + q.Key.String);
            }
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>Something the writer of the script should know; kept once.</summary>
        public void Note(string text)
        {
            if (string.IsNullOrEmpty(text) || Notes.Count >= MaxNotes || Notes.Contains(text)) return;
            Notes.Add(text);
        }

        /// <summary>To throw from inside a LuaFn: the script stops with this message at the line that called.</summary>
        public static Exception Bad(string message) { return new ScriptRuntimeException(message); }

        // ------------------------------------------------------------------ running

        /// <summary>Compile the source and run its top level. False (and Error) when it does not compile or breaks.</summary>
        public bool Load(string source)
        {
            if (Broken) return false;
            Source = source ?? "";
            if (Source.Trim().Length == 0) return Fail("the script is empty");
            if (Source.Length > MaxSource) return Fail("the script is too long (" + Source.Length + " characters, at most " + MaxSource + ")");
            string deep = LuaGuard.TooDeep(Source);
            if (deep != null) return Fail(deep);
            DynValue chunk;
            try { chunk = script.LoadString(Source, null, ChunkName); scriptSource = script.SourceCodeCount - 1; }
            catch (InterpreterException e) { return Fail(e.DecoratedMessage ?? e.Message); }
            catch (Exception e) { return Fail("it could not be read: " + e.Message); }
            return Call(chunk);
        }

        /// <summary>Call the script's global function of this name. False when there is none (not an error) or it broke.</summary>
        public bool Call(string function, params DynValue[] args)
        {
            if (Broken) return false;
            DynValue f = script.Globals.Get(function);
            if (f == null || f.Type != DataType.Function) { Result = DynValue.Nil; return false; }
            return Call(f, args);
        }

        public bool Call(string function, double a) { return Call(function, DynValue.NewNumber(a)); }

        /// <summary>Call a function of the script (at most MaxCallArgs arguments) under the budget.</summary>
        public bool Call(DynValue function, params DynValue[] args) { return CallWithin(Budget, function, args); }

        /// <summary>The same with a budget of its own (a small one for something that is called very often).</summary>
        public bool CallWithin(int budget, DynValue function, params DynValue[] args)
        {
            Result = DynValue.Nil;
            if (Broken) return false;
            if (function == null || (function.Type != DataType.Function && function.Type != DataType.ClrFunction)) return false;
            if (args == null) args = NoArgs;
            int used = 0;
            try
            {
                if (driver == null) driver = script.CreateCoroutine(driverFn).Coroutine;
                callArgs[0] = function;
                for (int i = 0; i < MaxCallArgs; i++) callArgs[i + 1] = i < args.Length && args[i] != null ? args[i] : DynValue.Nil;
                long mem0 = Allocated(), t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int slice = SliceSize < 10 ? 10 : SliceSize;
                driver.AutoYieldCounter = slice;
                DynValue r = driver.Resume(callArgs);
                used = slice;
                while (driver.State == CoroutineState.ForceSuspended)
                {
                    if (used >= budget) { Count(used); return Stop(TooLong); }
                    if (Allocated() - mem0 > MaxAlloc) { Count(used); return Stop(TooMuch); }
                    // (one instruction can be slow - the length of a huge table - so the clock is watched as well)
                    if (MaxSeconds > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - t0) > MaxSeconds * System.Diagnostics.Stopwatch.Frequency) { Count(used); return Stop(TooLong); }
                    r = driver.Resume();
                    used += slice;
                }
                Count(used);
                LastSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (LastSeconds > PeakSeconds) PeakSeconds = LastSeconds;
                Result = r ?? DynValue.Nil;
                return true;
            }
            catch (InterpreterException e) { Count(used); driver = null; return Fail(e.DecoratedMessage ?? e.Message, LineIn(e.CallStack)); }
            catch (IndexOutOfRangeException) { Count(used); driver = null; return Fail("stack overflow (a function calls itself without end?)"); }
            catch (OutOfMemoryException) { Count(used); driver = null; return Fail(TooMuch); }
            catch (Exception e) { Count(used); driver = null; return Fail("it broke inside: " + e.GetType().Name + " " + e.Message); }
        }

        public const string TooLong = "took too long (a loop that never ends, or far too much work in one call)";
        public const string TooMuch = "used too much memory";

        void Count(int used)
        {
            LastInstructions = used;
            if (used > PeakInstructions) PeakInstructions = used;
            TotalInstructions += used;
            Calls++;
        }

        int scriptSource = -1;

        // The script was stopped in the middle of a call: where was it? (the line it was on is the best hint there is for a loop that never ends)
        bool Stop(string message)
        {
            int line = 0;
            try { if (driver != null) line = LineIn(driver.GetStackTrace(0)); } catch (Exception) { }
            driver = null;
            return Fail(message, line);
        }

        // the innermost line of the script's own source in a call stack (0 = none)
        int LineIn(IList<MoonSharp.Interpreter.Debugging.WatchItem> stack)
        {
            if (stack == null || scriptSource < 0) return 0;
            for (int i = 0; i < stack.Count; i++)
            {
                MoonSharp.Interpreter.Debugging.SourceRef at = stack[i] != null ? stack[i].Location : null;
                if (at != null && !at.IsClrLocation && at.SourceIdx == scriptSource && at.FromLine > 0) return at.FromLine;
            }
            return 0;
        }

        /// <summary>Stop the script for good with this error (also for the owner: "update is missing").</summary>
        public bool Fail(string message) { return Fail(message, 0); }

        bool Fail(string message, int lineOtherwise)
        {
            if (Broken) return false;
            string m = string.IsNullOrEmpty(message) ? "it broke" : message;
            int line = 0;
            // "script:12: message" (the innermost position in the script's own source)
            string tag = ChunkName + ":";
            int at = m.IndexOf(tag, StringComparison.Ordinal);
            if (at >= 0)
            {
                int p = at + tag.Length, q = p;
                while (q < m.Length && m[q] >= '0' && m[q] <= '9') q++;
                if (q > p && q < m.Length && m[q] == ':' && int.TryParse(m.Substring(p, q - p), NumberStyles.None, CultureInfo.InvariantCulture, out line))
                    m = (m.Substring(0, at) + m.Substring(q + 1)).Trim();
                else line = 0;
            }
            // (positions in the sandbox's own two chunks say nothing to the writer)
            m = StripChunk(StripChunk(m, "lib:"), "driver:");
            if (line == 0) line = lineOtherwise;
            ErrorLine = line;
            ErrorSource = LineOf(Source, line);
            string hint = Hint(m, ErrorSource);
            if (hint.Length > 0) m += " (" + hint + ")";
            if (m.Length > 300) m = m.Substring(0, 300);
            Error = m;
            return false;
        }

        static string StripChunk(string m, string tag)
        {
            int at = m.IndexOf(tag, StringComparison.Ordinal);
            if (at < 0) return m;
            int q = at + tag.Length;
            while (q < m.Length && m[q] >= '0' && m[q] <= '9') q++;
            if (q < m.Length && m[q] == ':') q++;
            return (m.Substring(0, at) + m.Substring(q)).Trim();
        }

        /// <summary>The error as one line for a person or a repair prompt: "line 12: attempt to call a nil value  |  foo(1)".</summary>
        public string ErrorText()
        {
            if (!Broken) return "";
            string s = ErrorLine > 0 ? "line " + ErrorLine + ": " + Error : Error;
            if (ErrorSource.Length > 0) s += "  |  " + ErrorSource;
            return s;
        }

        static string LineOf(string source, int line)
        {
            if (line < 1 || string.IsNullOrEmpty(source)) return "";
            int start = 0;
            for (int i = 1; i < line; i++)
            {
                int nl = source.IndexOf('\n', start);
                if (nl < 0) return "";
                start = nl + 1;
            }
            int end = source.IndexOf('\n', start);
            if (end < 0) end = source.Length;
            string s = source.Substring(start, end - start).Trim();
            return s.Length > 140 ? s.Substring(0, 140) : s;
        }

        static readonly string[] Gone =
        {
            "gsub", "gmatch", "match", "setmetatable", "getmetatable", "require", "pcall", "xpcall", "coroutine", "loadstring", "load", "dofile",
            "os.", "io.", "debug.", "bit32",
        };

        static string Hint(string message, string line)
        {
            if (line.Length == 0 || message.IndexOf("nil", StringComparison.Ordinal) < 0) return "";
            for (int i = 0; i < Gone.Length; i++)
                if (line.IndexOf(Gone[i], StringComparison.Ordinal) >= 0)
                    return "`" + Gone[i].TrimEnd('.') + "` does not exist here";
            return "";
        }

        /// <summary>
        /// Does the script machine work on THIS platform? Runs a small program that uses what scripts use (tables,
        /// closures, strings, sorting, the owner's functions, the budget) and says "ok ..." or what failed. Nothing
        /// throws. For a line in the log when an app starts (the Quest build cannot be tried any other way).
        /// </summary>
        public static string SelfTest()
        {
            try
            {
                var box = new LuaSandbox(7);
                double got = 0;
                box.Def("give", a => { got = a.Num(0, -1f); return DynValue.NewNumber(got * 2); });
                bool ok = box.Load(
                    "local t = {} for i = 1, 20 do t[i] = (i * 7) % 11 end table.sort(t, function(a, b) return a > b end)\n" +
                    "local function make(n) return function(x) return x + n end end\n" +
                    "s = string.format('%d %s %.1f', t[1], ('ab'):upper(), make(2)(0.5)) .. '|' .. table.concat({1, 2, 3}, ',')\n" +
                    "function update(dt) local sum = 0 for i = 1, 100 do sum = sum + give(i) * dt end return sum end\n" +
                    "function hang() while true do end end");
                if (!ok) return "FAILED to load: " + box.ErrorText();
                string s = box.Get("s").Type == DataType.String ? box.Get("s").String : "?";
                if (s != "10 AB 2.5|1,2,3") return "FAILED: wrong result '" + s + "'";
                if (!box.Call("update", 0.5)) return "FAILED to call: " + box.ErrorText();
                if (box.Result.Type != DataType.Number || Math.Abs(box.Result.Number - 5050.0) > 0.001 || got != 100) return "FAILED: update gave " + Text(box.Result);
                int used = box.LastInstructions;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < 50; i++) box.Call("update", 0.5);
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (box.Call("hang") || box.Error != TooLong) return "FAILED: an endless loop was not stopped (" + box.ErrorText() + ")";
                if (box.Call("update", 0.5)) return "FAILED: it ran after it broke";
                return "ok (" + used + " steps a call, " + (50.0 * used / Math.Max(ms, 0.001) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " million steps a second)";
            }
            catch (Exception e) { return "FAILED: " + e.GetType().Name + " " + e.Message; }
        }

        // Bytes this thread has allocated so far (0 where the platform cannot tell: the guard is then off).
        static bool allocBroken;
        static long Allocated()
        {
            if (allocBroken) return 0;
            try { return GC.GetAllocatedBytesForCurrentThread(); }
            catch (Exception) { allocBroken = true; return 0; }
        }

        // ------------------------------------------------------------------ helpers every script gets

        /// <summary>The description of the helpers for the model that writes scripts (part of every API text).</summary>
        public const string CommonDoc =
@"rnd() -> 0..1; rnd(a, b) -> a..b; rndi(a, b) -> a whole number a..b (both included). Use these, not os.time.
dist(x1, y1, x2, y2) -> distance; clamp(v, lo, hi); lerp(a, b, t) -> a + (b - a) * t.
Lua 5.2 without the dangerous parts. There is: math.*, string.format / sub / len / rep / upper / lower / byte / char / find (plain text only),
table.insert / remove / sort / concat / unpack, pairs, ipairs, tostring, tonumber, type, print (goes to a log).
There is NOT: io, os, require, load, pcall, coroutine, setmetatable, string patterns (gsub, match, gmatch).";

        public static readonly string[] CommonNames = { "rnd", "rndi", "dist", "clamp", "lerp" };

        void DefCommon()
        {
            Def("rnd", a =>
            {
                if (!a.Has(0)) return DynValue.NewNumber(Rng.Next01());
                float lo = a.Has(1) ? a.Num(0, 0f) : 0f, hi = a.Has(1) ? a.Num(1, 1f) : a.Num(0, 1f);
                return DynValue.NewNumber(lo + (hi - lo) * Rng.Next01());
            });
            Def("rndi", a =>
            {
                int lo = a.Has(1) ? a.Int(0, 1) : 1, hi = a.Has(1) ? a.Int(1, 1) : a.Int(0, 1);
                return DynValue.NewNumber(RandomInt(lo, hi));
            });
            Def("dist", a =>
            {
                double dx = a.Num(2, 0f) - a.Num(0, 0f), dy = a.Num(3, 0f) - a.Num(1, 0f);
                return DynValue.NewNumber(Math.Sqrt(dx * dx + dy * dy));
            });
            Def("clamp", a =>
            {
                float v = a.Num(0, 0f), lo = a.Num(1, 0f), hi = a.Num(2, 1f);
                if (lo > hi) { float t = lo; lo = hi; hi = t; }
                return DynValue.NewNumber(v < lo ? lo : v > hi ? hi : v);
            });
            Def("lerp", a =>
            {
                double x = a.Num(0, 0f), y = a.Num(1, 0f), t = a.Num(2, 0f);
                return DynValue.NewNumber(x + (y - x) * t);
            });
        }

        int RandomInt(int lo, int hi)
        {
            if (hi < lo) { int t = lo; lo = hi; hi = t; }
            long span = (long)hi - lo + 1;
            if (span <= 1) return lo;
            if (span > int.MaxValue) span = int.MaxValue;
            return lo + Rng.Int((int)span);
        }

        // ------------------------------------------------------------------ values

        /// <summary>What tostring would say, without calling into the script.</summary>
        public static string Text(DynValue v)
        {
            if (v == null) return "nil";
            switch (v.Type)
            {
                case DataType.String: return v.String ?? "";
                case DataType.Number: return Num(v.Number);
                case DataType.Boolean: return v.Boolean ? "true" : "false";
                case DataType.Nil: case DataType.Void: return "nil";
                case DataType.Table: return "table";
                case DataType.Function: case DataType.ClrFunction: return "function";
                default: return v.Type.ToString().ToLowerInvariant();
            }
        }

        static string Num(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        internal static int SafeInt(double d, int otherwise)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return otherwise;
            if (d > 2000000000.0) return 2000000000;
            if (d < -2000000000.0) return -2000000000;
            return (int)Math.Floor(d + 0.5);
        }

        // ---- colours: a number 0xRRGGBB, a name of the storybook palette, "#RRGGBB"
        public static readonly string[] ColorNames =
        {
            "cream", "ink", "brown", "pink", "honey", "mint", "sky", "lilac", "white", "black", "red", "orange", "yellow", "green", "blue", "grey",
        };
        static readonly int[] ColorValues =
        {
            0xFFF8EA, 0x4A2F27, 0x9A6B4F, 0xF6A5B8, 0xF2B84B, 0x9FD8B4, 0x9CCBEB, 0xC4B2E6, 0xFFFFFF, 0x2A1A15, 0xE2574C, 0xF1924B, 0xF8D663, 0x6FBF73, 0x5B9BD5, 0xB9AFA6,
        };
        public const int Ink = 0x4A2F27, Cream = 0xFFF8EA;

        /// <summary>The colour a script value means. False when it means none (rgb is then left alone).</summary>
        public static bool TryColor(DynValue v, ref int rgb)
        {
            if (v == null) return false;
            if (v.Type == DataType.Number)
            {
                double d = v.Number;
                if (double.IsNaN(d) || d < 0 || d > 0xFFFFFF) return false;
                rgb = (int)d;
                return true;
            }
            if (v.Type == DataType.String)
            {
                string s = (v.String ?? "").Trim().ToLowerInvariant();
                if (s == "gray") s = "grey";
                else if (s == "purple" || s == "violet") s = "lilac";
                else if (s == "gold") s = "honey";
                else if (s == "beige") s = "cream";
                for (int i = 0; i < ColorNames.Length; i++) if (ColorNames[i] == s) { rgb = ColorValues[i]; return true; }
                if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
                else if (s.StartsWith("0x", StringComparison.Ordinal)) s = s.Substring(2);
                int n;
                if (s.Length == 6 && int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out n)) { rgb = n; return true; }
                if (s.Length == 3 && int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out n))
                {
                    int r = (n >> 8) & 15, g = (n >> 4) & 15, b = n & 15;
                    rgb = (r * 17 << 16) | (g * 17 << 8) | (b * 17);
                    return true;
                }
                return false;
            }
            if (v.Type == DataType.Table)          // {r, g, b}: 0..1 or 0..255
            {
                DynValue r = v.Table.Get(1), g = v.Table.Get(2), b = v.Table.Get(3);
                if (r.Type != DataType.Number || g.Type != DataType.Number || b.Type != DataType.Number) return false;
                double k = r.Number <= 1.0 && g.Number <= 1.0 && b.Number <= 1.0 ? 255.0 : 1.0;
                rgb = (Byte(r.Number * k) << 16) | (Byte(g.Number * k) << 8) | Byte(b.Number * k);
                return true;
            }
            return false;
        }

        static int Byte(double d) { return double.IsNaN(d) || d < 0 ? 0 : d > 255 ? 255 : (int)d; }
    }

    /// <summary>
    /// The arguments of a call from the script, read forgivingly: a number may come as a string, a missing or broken
    /// one (nil, NaN, infinity, a table) becomes the default, a string is cut. Indices are 0-based.
    /// </summary>
    public struct LuaArgs
    {
        public readonly LuaSandbox Box;
        public readonly string Function;
        readonly CallbackArguments args;
        readonly int shift;

        public LuaArgs(LuaSandbox box, string function, CallbackArguments args) { Box = box; Function = function; this.args = args; shift = 0; }
        LuaArgs(LuaSandbox box, string function, CallbackArguments args, int shift) { Box = box; Function = function; this.args = args; this.shift = shift; }

        public int Count { get { int n = args.Count - shift; return n < 0 ? 0 : n; } }

        /// <summary>The same arguments without the first n (a method called with a colon: skip the object).</summary>
        public LuaArgs Skip(int n) { return new LuaArgs(Box, Function, args, shift + n); }

        public DynValue Raw(int i)
        {
            i += shift;
            if (i < 0 || i >= args.Count) return DynValue.Nil;
            DynValue v = args[i];
            return v == null || v.Type == DataType.Void ? DynValue.Nil : v;
        }

        /// <summary>There is something (not nil) at i.</summary>
        public bool Has(int i) { return Raw(i).Type != DataType.Nil; }
        public bool IsTable(int i) { return Raw(i).Type == DataType.Table; }
        public bool IsNumber(int i) { float f; return TryNum(i, out f); }
        public bool IsString(int i) { return Raw(i).Type == DataType.String; }

        public bool TryNum(int i, out float value)
        {
            DynValue v = Raw(i);
            double d;
            if (v.Type == DataType.Number) d = v.Number;
            else if (v.Type == DataType.String && double.TryParse(v.String, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { }
            else { value = 0f; return false; }
            if (double.IsNaN(d) || double.IsInfinity(d) || d > 1e9 || d < -1e9) { value = 0f; return false; }
            value = (float)d;
            return true;
        }

        /// <summary>A finite number, or the default.</summary>
        public float Num(int i, float otherwise = 0f) { float f; return TryNum(i, out f) ? f : otherwise; }
        public float Num(int i, float otherwise, float lo, float hi) { float f = Num(i, otherwise); return f < lo ? lo : f > hi ? hi : f; }
        public int Int(int i, int otherwise = 0) { float f; return TryNum(i, out f) ? LuaSandbox.SafeInt(f, otherwise) : otherwise; }

        /// <summary>A number that has to be there: the script stops with a clear message when it is not.</summary>
        public float Need(int i, string what)
        {
            float f;
            if (TryNum(i, out f)) return f;
            throw LuaSandbox.Bad(Function + ": " + what + " (argument " + (i + 1) + ") must be a number, it is " + Describe(Raw(i)));
        }

        static string Describe(DynValue v)
        {
            if (v.Type == DataType.Nil) return "nil";
            if (v.Type == DataType.Number) return "not a real number (" + LuaSandbox.Text(v) + ")";
            if (v.Type == DataType.String) return "the string \"" + (v.String.Length > 20 ? v.String.Substring(0, 20) : v.String) + "\"";
            return "a " + v.Type.ToString().ToLowerInvariant();
        }

        /// <summary>Text (numbers and booleans are written out), cut to max letters.</summary>
        public string Str(int i, string otherwise = "", int max = LuaSandbox.MaxText)
        {
            DynValue v = Raw(i);
            string s;
            if (v.Type == DataType.String) s = v.String ?? "";
            else if (v.Type == DataType.Number || v.Type == DataType.Boolean) s = LuaSandbox.Text(v);
            else return otherwise;
            if (max > 0 && s.Length > max) s = s.Substring(0, max);
            return s;
        }

        public bool Bool(int i, bool otherwise = false)
        {
            DynValue v = Raw(i);
            if (v.Type == DataType.Boolean) return v.Boolean;
            if (v.Type == DataType.Nil) return otherwise;
            if (v.Type == DataType.Number) return v.Number != 0;
            if (v.Type == DataType.String) return v.String != "false" && v.String != "0" && v.String.Length > 0;
            return true;
        }

        /// <summary>A colour (see LuaSandbox.TryColor); a value that means none becomes the default and is noted.</summary>
        public int Color(int i, int otherwise = LuaSandbox.Ink)
        {
            DynValue v = Raw(i);
            if (v.Type == DataType.Nil) return otherwise;
            int rgb = otherwise;
            if (!LuaSandbox.TryColor(v, ref rgb)) Box.Note("unknown colour " + (v.Type == DataType.String ? "'" + (v.String.Length > 20 ? v.String.Substring(0, 20) : v.String) + "'" : LuaSandbox.Text(v)) + " (use 0xRRGGBB or: " + string.Join(" ", LuaSandbox.ColorNames) + ")");
            return rgb;
        }

        /// <summary>The table at i, or null.</summary>
        public Table Tab(int i) { DynValue v = Raw(i); return v.Type == DataType.Table ? v.Table : null; }
        /// <summary>The function at i, or null.</summary>
        public DynValue Fn(int i) { DynValue v = Raw(i); return v.Type == DataType.Function ? v : null; }

        /// <summary>A number field of a table (by name, or by position when the name is missing), or the default.</summary>
        public static float Field(Table t, string name, int index, float otherwise)
        {
            if (t == null) return otherwise;
            DynValue v = name != null ? t.Get(name) : DynValue.Nil;
            if ((v == null || v.Type != DataType.Number) && index > 0) v = t.Get(index);
            if (v == null || v.Type != DataType.Number) return otherwise;
            double d = v.Number;
            if (double.IsNaN(d) || double.IsInfinity(d) || d > 1e9 || d < -1e9) return otherwise;
            return (float)d;
        }
    }

    /// <summary>
    /// A look at the source BEFORE it is given to the parser: the parser and the compiler walk nested things by
    /// calling themselves, and a source nested thousands deep would overflow the real stack (which cannot be caught).
    /// </summary>
    public static class LuaGuard
    {
        public const int MaxNesting = 60;       // brackets, and blocks (function / do / if / repeat ... end)
        public const int MaxChain = 240;        // operators in one statement

        /// <summary>Null when the source is fine, otherwise what is wrong with it.</summary>
        public static string TooDeep(string s)
        {
            int brackets = 0, blocks = 0, chain = 0, unary = 0;
            int i = 0, n = s.Length, line = 1;
            while (i < n)
            {
                char c = s[i];
                if (c == '\n') { line++; i++; continue; }
                if (c == '-' && i + 1 < n && s[i + 1] == '-')          // a comment
                {
                    i += 2;
                    int lvl = LongOpen(s, i);
                    if (lvl >= 0) i = LongClose(s, i + lvl + 2, lvl, ref line);
                    else while (i < n && s[i] != '\n') i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    i++;
                    while (i < n && s[i] != c && s[i] != '\n') { if (s[i] == '\\') i++; i++; }
                    i++;
                    continue;
                }
                if (c == '[')
                {
                    int lvl = LongOpen(s, i);
                    if (lvl >= 0) { i = LongClose(s, i + lvl + 2, lvl, ref line); continue; }
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    string w = s.Substring(i, j - i);
                    i = j;
                    switch (w)
                    {
                        case "function": case "do": case "if": case "repeat":
                            blocks++; chain = 0; unary = 0;
                            if (blocks > MaxNesting) return "line " + line + ": blocks are nested too deeply";
                            break;
                        case "end": case "until":
                            if (blocks > 0) blocks--;
                            chain = 0; unary = 0;
                            break;
                        case "not":
                            if (++unary > 12) return "line " + line + ": too many `not` in a row";
                            break;
                        case "and": case "or":
                            unary = 0;
                            if (++chain > MaxChain) return "line " + line + ": one expression is too long";
                            break;
                        case "local": case "return": case "then": case "else": case "elseif": case "while": case "for": case "in": case "break":
                            chain = 0; unary = 0;
                            break;
                        default: unary = 0; break;
                    }
                    continue;
                }
                if (char.IsDigit(c))
                {
                    while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '.')) i++;
                    unary = 0;
                    continue;
                }
                switch (c)
                {
                    case '(': case '[': case '{':
                        if (++brackets > MaxNesting) return "line " + line + ": brackets are nested too deeply";
                        unary = 0;
                        break;
                    case ')': case ']': case '}':
                        if (brackets > 0) brackets--;
                        unary = 0;
                        break;
                    case ';': case ',': chain = 0; unary = 0; break;
                    case '=':
                        if (i + 1 < n && s[i + 1] == '=') { i++; if (++chain > MaxChain) return "line " + line + ": one expression is too long"; }
                        else chain = 0;
                        unary = 0;
                        break;
                    case '-': case '#':
                        if (++unary > 12) return "line " + line + ": too many signs in a row";
                        if (++chain > MaxChain) return "line " + line + ": one expression is too long";
                        break;
                    case '+': case '*': case '/': case '%': case '^': case '<': case '>': case '~': case '.':
                        if (c == '.' && !(i + 1 < n && s[i + 1] == '.')) { unary = 0; break; }      // a field, not ..
                        if (c == '.') { i++; if (i + 1 < n && s[i + 1] == '.') i++; }
                        unary = 0;
                        if (++chain > MaxChain) return "line " + line + ": one expression is too long";
                        break;
                    case ' ': case '\t': case '\r': break;
                    default: unary = 0; break;
                }
                i++;
            }
            return null;
        }

        // "[[" or "[==[" at i: the number of = signs, otherwise -1
        static int LongOpen(string s, int i)
        {
            if (i >= s.Length || s[i] != '[') return -1;
            int j = i + 1, lvl = 0;
            while (j < s.Length && s[j] == '=') { j++; lvl++; }
            return j < s.Length && s[j] == '[' ? lvl : -1;
        }

        static int LongClose(string s, int i, int lvl, ref int line)
        {
            while (i < s.Length)
            {
                if (s[i] == '\n') line++;
                if (s[i] == ']')
                {
                    int j = i + 1, k = 0;
                    while (j < s.Length && s[j] == '=') { j++; k++; }
                    if (k == lvl && j < s.Length && s[j] == ']') return j + 1;
                }
                i++;
            }
            return i;
        }
    }
}
