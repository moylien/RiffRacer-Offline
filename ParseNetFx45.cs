// Reemplazo local de Parse.NetFx45.dll 1.7.0.0: ParsePush y ParseAnalytics sin red.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

[assembly: AssemblyTitle("Parse.NetFx45 (reemplazo local offline)")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace Parse
{
    public class ParsePush
    {
        public IEnumerable<string> Channels { get; set; }
        public IDictionary<string, object> Data { get; set; }
        public string Alert { get; set; }

        public ParsePush() { }

        // Sin servidor no hay a quien enviar: se completa con exito sin hacer nada.
        public Task SendAsync() { return Task.FromResult(0); }
    }

    public static class ParseAnalytics
    {
        public static Task TrackAppOpenedAsync() { return Task.FromResult(0); }
        public static Task TrackEventAsync(string name) { return Task.FromResult(0); }
        public static Task TrackEventAsync(string name, IDictionary<string, string> dimensions) { return Task.FromResult(0); }
    }
}
