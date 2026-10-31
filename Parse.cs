// Reemplazo local (offline) de Parse.dll 1.7.0.0 para Riff Racer.
// Misma API publica que consume ParseOctane.dll; todo se guarda en archivos locales
// (LocalData\store.json y LocalData\files\*). No hay servidor externo.
// Compatible con el compilador C# 5 de .NET Framework 4.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: AssemblyTitle("Parse (reemplazo local offline)")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace Parse
{
    // ------------------------------------------------------------------ errores
    public class ParseException : Exception
    {
        public enum ErrorCode
        {
            OtherCause = -1, InternalServerError = 1, ConnectionFailed = 100, ObjectNotFound = 101,
            InvalidQuery = 102, InvalidClassName = 103, MissingObjectId = 104, InvalidKeyName = 105,
            InvalidPointer = 106, InvalidJSON = 107, CommandUnavailable = 108, NotInitialized = 109,
            IncorrectType = 111, ObjectTooLarge = 116, UsernameMissing = 200, PasswordMissing = 201,
            UsernameTaken = 202, EmailTaken = 203
        }
        public ErrorCode Code { get; private set; }
        public ParseException(ErrorCode code, string message) : base(message) { Code = code; }
    }

    // ------------------------------------------------------------------ cliente
    public static class ParseClient
    {
        public struct Configuration
        {
            public string ApplicationId { get; set; }
            public string Server { get; set; }
            public string MasterKey { get; set; }
            public string WindowsKey { get; set; }
            public string DotNetKey { get; set; }
            public IDictionary<string, string> AdditionalHTTPHeaders { get; set; }
        }

        public static void Initialize(Configuration configuration) { Log.W("Initialize(Config) appId=" + configuration.ApplicationId); LocalStore.Init(); EnsureLocalUser(); }
        public static void Initialize(string applicationId, string dotnetKey) { Log.W("Initialize(string) appId=" + applicationId); LocalStore.Init(); EnsureLocalUser(); }

        // Si no hay sesion activa, crea y loguea automaticamente el usuario local predeterminado
        static void EnsureLocalUser()
        {
            if (ParseUser.CurrentUser != null) { Log.W("EnsureLocalUser: ya existe user=" + ParseUser.CurrentUser.Username); return; }
            const string name = "offline_player";
            const string pass = "offline";
            lock (LocalStore.Sync)
            {
                Rec existing = LocalStore.Class("_User").FirstOrDefault(
                    x => x.Data.ContainsKey("username") && (x.Data["username"] as string) == name);
                if (existing != null)
                {
                    Log.W("EnsureLocalUser: restaurando sesion id=" + existing.Id);
                    ParseUser u = new ParseUser();
                    u.Load(existing);
                    ParseUser.SetCurrent(u);
                    LocalStore.SetSession(u.ObjectId);
                }
                else
                {
                    Log.W("EnsureLocalUser: creando nuevo usuario offline_player");
                    ParseUser nu = new ParseUser();
                    nu.Username = name;
                    nu.Password = pass;
                    nu.SignUpAsync().Wait();
                    Log.W("EnsureLocalUser: usuario creado id=" + nu.ObjectId);
                }
            }
        }
    }

    // ------------------------------------------------------------------ log de diagnostico
    internal static class Log
    {
        static string path = null;
        static readonly object sync = new object();
        static Log()
        {
            // Intentar varias rutas para que el log aparezca en algún lado
            string[] candidates = new string[] {
                TryPath(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location ?? ""), "LocalData", "parse_debug.log")),
                TryPath(Path.Combine(Environment.CurrentDirectory, "LocalData", "parse_debug.log")),
                TryPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiffRacerParse", "parse_debug.log")),
                TryPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "parse_debug.log")),
                TryPath(@"C:\parse_debug.log"),
            };
            foreach (string c in candidates) { if (c != null) { path = c; break; } }
        }
        static string TryPath(string p)
        {
            try
            {
                string d = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                File.WriteAllText(p, "=== Parse debug === cwd=" + Environment.CurrentDirectory + "\r\n");
                return p;
            }
            catch { return null; }
        }
        public static void W(string msg)
        {
            if (path == null) return;
            try { lock (sync) { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n"); } }
            catch { }
        }
    }

    // ------------------------------------------------------------------ utilidades de tarea
    internal static class T
    {
        public static Task Done() { return Task.FromResult(0); }
        public static Task<R> Ok<R>(R v) { return Task.FromResult(v); }
        public static Task<R> Fail<R>(Exception e)
        {
            Log.W("FAIL<" + typeof(R).Name + "> " + e.GetType().Name + ": " + e.Message);
            TaskCompletionSource<R> s = new TaskCompletionSource<R>();
            s.SetException(e);
            return s.Task;
        }
        public static Task FailTask(Exception e)
        {
            Log.W("FAILTASK " + e.GetType().Name + ": " + e.Message);
            return Fail<int>(e);
        }
    }

    // ------------------------------------------------------------------ almacen
    internal sealed class Rec
    {
        public string Id;
        public DateTime Created;
        public DateTime Updated;
        public string Pw;
        public Dictionary<string, object> Data = new Dictionary<string, object>();
    }

    internal static class LocalStore
    {
        public static readonly object Sync = new object();
        static readonly Dictionary<string, List<Rec>> classes = new Dictionary<string, List<Rec>>();
        static string session;
        static bool loaded;
        static string dir;
        static string storePath;
        static readonly Random rng = new Random(Guid.NewGuid().GetHashCode());

        public static string Dir { get { Init(); return dir; } }

        public static void Init()
        {
            lock (Sync)
            {
                if (loaded) return;
                dir = ResolveDir();
                Directory.CreateDirectory(Path.Combine(dir, "files"));
                storePath = Path.Combine(dir, "store.json");
                bool existed = File.Exists(storePath);
                loaded = true; // antes de Seed(): Class() vuelve a llamar a Init()
                if (existed) Load();
                Seed();
                if (!existed) Save();
            }
        }

        static string ResolveDir()
        {
            string baseDir = null;
            try { baseDir = Path.GetDirectoryName(typeof(LocalStore).Assembly.Location); } catch { }
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "LocalData"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine("RiffRacer", "LocalData"))
            };
            foreach (string c in candidates)
            {
                try
                {
                    Directory.CreateDirectory(c);
                    string probe = Path.Combine(c, ".write_test");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    return c;
                }
                catch { }
            }
            return candidates[1];
        }

        public static List<Rec> Class(string name)
        {
            Init();
            List<Rec> l;
            if (!classes.TryGetValue(name, out l)) { l = new List<Rec>(); classes[name] = l; }
            return l;
        }

        public static string Session { get { Init(); return session; } }
        public static void SetSession(string userId) { lock (Sync) { Init(); session = userId; Save(); } }

        public static string NewId()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            while (true)
            {
                char[] b = new char[10];
                for (int i = 0; i < b.Length; i++) b[i] = chars[rng.Next(chars.Length)];
                string id = new string(b);
                bool dup = false;
                foreach (KeyValuePair<string, List<Rec>> kv in classes)
                    if (kv.Value.Any(r => r.Id == id)) { dup = true; break; }
                if (!dup) return id;
            }
        }

        static void Seed()
        {
            // Configuration
            List<Rec> cfg;
            if (!classes.TryGetValue("Configuration", out cfg) || cfg.Count == 0)
            {
                Rec r = new Rec();
                r.Id = "ITz5K79wcQ";
                r.Created = r.Updated = DateTime.UtcNow;
                r.Data["TrackConversionCost"] = 0L;
                r.Data["TrackCreatorCoinReward"] = 100L;
                r.Data["OnTrackCoinValue"] = 10L;
                r.Data["WatchAdCoinReward"] = 200L;
                r.Data["ShareScoreCoinReward"] = 200L;
                r.Data["InterstitialAdFrequency"] = 0L;
                r.Data["RegionGroup"] = "Default";
                Class("Configuration").Add(r);
            }

            // Version
            List<Rec> ver;
            if (!classes.TryGetValue("Version", out ver) || ver.Count == 0)
            {
                Rec rv = new Rec();
                rv.Id = "BoJMZ1TQJZ";
                rv.Created = rv.Updated = DateTime.UtcNow;
                rv.Data["Ver"] = 100L;
                Class("Version").Add(rv);
            }

            // Profile_Steam
            List<Rec> prof;
            if (!classes.TryGetValue("Profile_Steam", out prof) || prof.Count == 0)
            {
                Rec rp = new Rec();
                rp.Id = "LocalProfile";
                rp.Created = rp.Updated = DateTime.UtcNow;
                rp.Data["SystemID"] = "1";
                rp.Data["Username"] = "PLAYER";
                rp.Data["Platform"] = "PC";
                rp.Data["DriverLevel"] = 0L;
                rp.Data["FavouriteCar"] = 1L;
                rp.Data["FavouriteSong"] = "null";
                rp.Data["FavouriteGenre"] = "pop";
                Class("Profile_Steam").Add(rp);
            }

            // News
            List<Rec> news;
            if (!classes.TryGetValue("News", out news) || news.Count == 0)
            {
                Rec rn = new Rec();
                rn.Id = "RiffLocalNews";
                rn.Created = rn.Updated = DateTime.UtcNow;
                rn.Data["ID"] = 16L;
                rn.Data["Status"] = "Active";
                rn.Data["Pinned"] = true;
                string msg = "Servidor local offline activo.";
                string title = "Riff Racer Offline";
                rn.Data["en"] = msg; rn.Data["es"] = msg; rn.Data["fr"] = msg; rn.Data["de"] = msg; rn.Data["it"] = msg; rn.Data["ru"] = msg;
                rn.Data["enTitle"] = title; rn.Data["esTitle"] = title; rn.Data["frTitle"] = title; rn.Data["deTitle"] = title; rn.Data["itTitle"] = title; rn.Data["ruTitle"] = title;
                Class("News").Add(rn);
            }
        }

        static void Load()
        {
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(File.ReadAllText(storePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return;
                object s;
                if (root.TryGetValue("session", out s)) session = s as string;
                object cl;
                Dictionary<string, object> cd = root.TryGetValue("classes", out cl) ? cl as Dictionary<string, object> : null;
                if (cd == null) return;
                foreach (KeyValuePair<string, object> kv in cd)
                {
                    List<object> arr = kv.Value as List<object>;
                    if (arr == null) continue;
                    List<Rec> list = new List<Rec>();
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        Rec r = new Rec();
                        r.Id = d["id"] as string;
                        r.Created = MiniJson.ParseDate(d["created"] as string);
                        r.Updated = MiniJson.ParseDate(d["updated"] as string);
                        object pw; if (d.TryGetValue("pw", out pw)) r.Pw = pw as string;
                        Dictionary<string, object> data = d["data"] as Dictionary<string, object>;
                        if (data != null) r.Data = data;
                        list.Add(r);
                    }
                    classes[kv.Key] = list;
                }
            }
            catch
            {
                // Archivo danado: se conserva una copia y se empieza limpio.
                try { File.Copy(storePath, storePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); } catch { }
                classes.Clear();
                session = null;
            }
        }

        public static void Save()
        {
            lock (Sync)
            {
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["session"] = session;
                Dictionary<string, object> cd = new Dictionary<string, object>();
                foreach (KeyValuePair<string, List<Rec>> kv in classes)
                {
                    List<object> arr = new List<object>();
                    foreach (Rec r in kv.Value)
                    {
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["id"] = r.Id;
                        d["created"] = MiniJson.FormatDate(r.Created);
                        d["updated"] = MiniJson.FormatDate(r.Updated);
                        if (r.Pw != null) d["pw"] = r.Pw;
                        d["data"] = r.Data;
                        arr.Add(d);
                    }
                    cd[kv.Key] = arr;
                }
                root["classes"] = cd;
                string json = MiniJson.Write(root, true);
                string tmp = storePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(storePath)) File.Replace(tmp, storePath, null);
                else File.Move(tmp, storePath);
            }
        }
    }

    // ------------------------------------------------------------------ JSON minimo
    internal static class MiniJson
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FormatDate(DateTime d)
        {
            return d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);
        }
        public static DateTime ParseDate(string s)
        {
            DateTime d;
            if (s != null && DateTime.TryParse(s, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
            return DateTime.UtcNow;
        }

        public static string Write(object v, bool pretty)
        {
            StringBuilder sb = new StringBuilder();
            W(sb, v, pretty ? 0 : -1);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, int level)
        {
            if (level < 0) return;
            sb.Append('\n');
            sb.Append(' ', level * 2);
        }

        static void W(StringBuilder sb, object v, int lvl)
        {
            if (v == null) { sb.Append("null"); return; }
            string s = v as string;
            if (s != null) { WS(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is short || v is byte || v is sbyte || v is ushort || v is uint || v is ulong)
            { sb.Append(Convert.ToString(v, Inv)); return; }
            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, Inv);
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
                string t = d.ToString("R", Inv);
                if (t.IndexOf('.') < 0 && t.IndexOf('E') < 0 && t.IndexOf('e') < 0) t += ".0";
                sb.Append(t);
                return;
            }
            if (v is DateTime)
            { sb.Append("{\"__type\":\"Date\",\"iso\":\""); sb.Append(FormatDate((DateTime)v)); sb.Append("\"}"); return; }
            ParseFile f = v as ParseFile;
            if (f != null) { sb.Append("{\"__type\":\"File\",\"name\":"); WS(sb, f.Name); sb.Append('}'); return; }
            ParseObject po = v as ParseObject;
            if (po != null)
            {
                sb.Append("{\"__type\":\"Pointer\",\"className\":"); WS(sb, po.ClassName);
                sb.Append(",\"objectId\":"); WS(sb, po.ObjectId); sb.Append('}'); return;
            }
            byte[] bytes = v as byte[];
            if (bytes != null) { sb.Append("{\"__type\":\"Bytes\",\"base64\":\""); sb.Append(Convert.ToBase64String(bytes)); sb.Append("\"}"); return; }
            IDictionary dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Indent(sb, lvl < 0 ? -1 : lvl + 1);
                    WS(sb, Convert.ToString(e.Key, Inv));
                    sb.Append(lvl < 0 ? ":" : ": ");
                    W(sb, e.Value, lvl < 0 ? -1 : lvl + 1);
                }
                if (!first) Indent(sb, lvl);
                sb.Append('}');
                return;
            }
            IEnumerable en = v as IEnumerable;
            if (en != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Indent(sb, lvl < 0 ? -1 : lvl + 1);
                    W(sb, o, lvl < 0 ? -1 : lvl + 1);
                }
                if (!first) Indent(sb, lvl);
                sb.Append(']');
                return;
            }
            WS(sb, v.ToString());
        }

        static void WS(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- lectura
        sealed class R
        {
            public string S; public int P;
        }

        public static object Parse(string json)
        {
            R r = new R(); r.S = json; r.P = 0;
            object v = Val(r);
            return Revive(v);
        }

        static void Ws(R r) { while (r.P < r.S.Length && char.IsWhiteSpace(r.S[r.P])) r.P++; }

        static object Val(R r)
        {
            Ws(r);
            if (r.P >= r.S.Length) throw new FormatException("JSON incompleto");
            char c = r.S[r.P];
            if (c == '{')
            {
                r.P++;
                Dictionary<string, object> d = new Dictionary<string, object>();
                Ws(r);
                if (r.S[r.P] == '}') { r.P++; return d; }
                while (true)
                {
                    Ws(r);
                    string k = Str(r);
                    Ws(r);
                    if (r.S[r.P++] != ':') throw new FormatException("se esperaba ':'");
                    d[k] = Val(r);
                    Ws(r);
                    char n = r.S[r.P++];
                    if (n == ',') continue;
                    if (n == '}') break;
                    throw new FormatException("objeto JSON invalido");
                }
                return d;
            }
            if (c == '[')
            {
                r.P++;
                List<object> l = new List<object>();
                Ws(r);
                if (r.S[r.P] == ']') { r.P++; return l; }
                while (true)
                {
                    l.Add(Val(r));
                    Ws(r);
                    char n = r.S[r.P++];
                    if (n == ',') continue;
                    if (n == ']') break;
                    throw new FormatException("arreglo JSON invalido");
                }
                return l;
            }
            if (c == '"') return Str(r);
            if (string.CompareOrdinal(r.S, r.P, "true", 0, 4) == 0) { r.P += 4; return true; }
            if (string.CompareOrdinal(r.S, r.P, "false", 0, 5) == 0) { r.P += 5; return false; }
            if (string.CompareOrdinal(r.S, r.P, "null", 0, 4) == 0) { r.P += 4; return null; }
            int st = r.P;
            while (r.P < r.S.Length && "+-0123456789.eE".IndexOf(r.S[r.P]) >= 0) r.P++;
            string num = r.S.Substring(st, r.P - st);
            if (num.Length == 0) throw new FormatException("valor JSON invalido");
            if (num.IndexOfAny(new char[] { '.', 'e', 'E' }) >= 0) return double.Parse(num, Inv);
            long lv;
            if (long.TryParse(num, NumberStyles.Integer, Inv, out lv)) return lv;
            return double.Parse(num, Inv);
        }

        static string Str(R r)
        {
            if (r.S[r.P] != '"') throw new FormatException("se esperaba cadena");
            r.P++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = r.S[r.P++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char e = r.S[r.P++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(r.S.Substring(r.P, 4), 16));
                            r.P += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Convierte los objetos con "__type" a sus tipos reales.
        public static object Revive(object v)
        {
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d != null)
            {
                object t;
                if (d.TryGetValue("__type", out t))
                {
                    string ts = t as string;
                    if (ts == "Date") return ParseDate(d["iso"] as string);
                    if (ts == "File") return new ParseFile(d["name"] as string, true);
                    if (ts == "Bytes") return Convert.FromBase64String(d["base64"] as string);
                    if (ts == "Pointer") return ParseObject.Shell(d["className"] as string, d["objectId"] as string);
                }
                List<string> keys = new List<string>(d.Keys);
                foreach (string k in keys) d[k] = Revive(d[k]);
                return d;
            }
            List<object> l = v as List<object>;
            if (l != null)
            {
                for (int i = 0; i < l.Count; i++) l[i] = Revive(l[i]);
                return l;
            }
            return v;
        }
    }

    // ------------------------------------------------------------------ ParseObject
    public class ParseObject
    {
        internal sealed class Op
        {
            public int Kind; // 0 set, 1 increment, 2 add, 3 delete
            public string Key;
            public object Value;
        }

        internal string className;
        internal string objectId;
        internal DateTime? created;
        internal DateTime? updated;
        internal Dictionary<string, object> data = new Dictionary<string, object>();
        internal List<Op> ops = new List<Op>();

        public ParseObject(string className)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentException("className");
            this.className = className;
        }

        internal static ParseObject Shell(string className, string id)
        {
            ParseObject o = new ParseObject(className);
            o.objectId = id;
            return o;
        }

        public string ClassName { get { return className; } }
        public string ObjectId { get { return objectId; } }
        public DateTime? CreatedAt { get { return created; } }
        public DateTime? UpdatedAt { get { return updated; } }
        public bool IsDirty { get { return objectId == null || ops.Count > 0; } }
        public ICollection<string> Keys { get { return data.Keys; } }

        public bool ContainsKey(string key) { return data.ContainsKey(key); }

        public object this[string key]
        {
            get
            {
                object v;
                if (!data.TryGetValue(key, out v)) throw new KeyNotFoundException("The given key was not present in the dictionary.");
                return v;
            }
            set
            {
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("key");
                Do(new Op { Kind = 0, Key = key, Value = value });
            }
        }

        public T Get<T>(string key)
        {
            return (T)ConvertTo(this[key], typeof(T));
        }

        public bool TryGetValue<T>(string key, out T result)
        {
            object v;
            if (data.TryGetValue(key, out v)) { try { result = (T)ConvertTo(v, typeof(T)); return true; } catch (InvalidCastException) { } }
            result = default(T);
            return false;
        }

        static object ConvertTo(object v, Type t)
        {
            if (v == null)
            {
                if (t.IsValueType && Nullable.GetUnderlyingType(t) == null) throw new InvalidCastException("Valor nulo no convertible a " + t.Name);
                return null;
            }
            if (t.IsInstanceOfType(v)) return v;
            Type u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(string)) return Convert.ToString(v, CultureInfo.InvariantCulture);
            if (v is IConvertible && typeof(IConvertible).IsAssignableFrom(u))
                return Convert.ChangeType(v, u, CultureInfo.InvariantCulture);
            throw new InvalidCastException("No se puede convertir " + v.GetType().Name + " a " + t.Name);
        }

        public void Increment(string key) { Do(new Op { Kind = 1, Key = key, Value = 1L }); }
        public void Increment(string key, long amount) { Do(new Op { Kind = 1, Key = key, Value = amount }); }
        public void Increment(string key, double amount) { Do(new Op { Kind = 1, Key = key, Value = amount }); }
        public void Add(string key, object value) { Do(new Op { Kind = 2, Key = key, Value = value }); }
        public void Remove(string key) { Do(new Op { Kind = 3, Key = key }); }

        void Do(Op op)
        {
            Apply(data, op);
            ops.Add(op);
        }

        internal static void Apply(Dictionary<string, object> d, Op op)
        {
            object cur;
            switch (op.Kind)
            {
                case 0: d[op.Key] = Clone(op.Value); break;
                case 1:
                    if (d.TryGetValue(op.Key, out cur) && cur != null)
                    {
                        if (cur is double || op.Value is double) d[op.Key] = Convert.ToDouble(cur, CultureInfo.InvariantCulture) + Convert.ToDouble(op.Value, CultureInfo.InvariantCulture);
                        else d[op.Key] = Convert.ToInt64(cur, CultureInfo.InvariantCulture) + Convert.ToInt64(op.Value, CultureInfo.InvariantCulture);
                    }
                    else d[op.Key] = op.Value;
                    break;
                case 2:
                    List<object> l = (d.TryGetValue(op.Key, out cur) ? cur as List<object> : null);
                    if (l == null) { l = new List<object>(); d[op.Key] = l; }
                    l.Add(Clone(op.Value));
                    break;
                case 3: d.Remove(op.Key); break;
            }
        }

        internal static object Clone(object v)
        {
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d != null)
            {
                Dictionary<string, object> n = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> kv in d) n[kv.Key] = Clone(kv.Value);
                return n;
            }
            IDictionary idict = v as IDictionary;
            if (idict != null)
            {
                Dictionary<string, object> n = new Dictionary<string, object>();
                foreach (DictionaryEntry e in idict) n[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = Clone(e.Value);
                return n;
            }
            if (v is string || v is byte[] || v == null) return v;
            IEnumerable en = v as IEnumerable;
            if (en != null && !(v is ParseObject) && !(v is ParseFile))
            {
                List<object> l = new List<object>();
                foreach (object o in en) l.Add(Clone(o));
                return l;
            }
            return v;
        }

        internal virtual void BeforeSave(Rec r) { }

        internal void Load(Rec r)
        {
            objectId = r.Id;
            created = r.Created;
            updated = r.Updated;
            Dictionary<string, object> n = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in r.Data) n[kv.Key] = Clone(kv.Value);
            data = n;
            ops.Clear();
        }

        public Task SaveAsync()
        {
            try
            {
                lock (LocalStore.Sync)
                {
                    List<Rec> list = LocalStore.Class(className);
                    Rec r = null;
                    bool isNew = false;
                    if (objectId != null)
                    {
                        r = list.FirstOrDefault(x => x.Id == objectId);
                        if (r == null) throw new ParseException(ParseException.ErrorCode.ObjectNotFound, "Object not found.");
                    }
                    else
                    {
                        r = new Rec();
                        r.Id = LocalStore.NewId();
                        r.Created = DateTime.UtcNow;
                        isNew = true;
                    }
                    foreach (Op op in ops) Apply(r.Data, op);
                    r.Updated = DateTime.UtcNow;
                    BeforeSave(r);
                    if (isNew) list.Add(r);
                    LocalStore.Save();
                    Load(r);
                }
                Log.W("SaveAsync class=" + className + " id=" + (objectId ?? "NEW"));
                return T.Done();
            }
            catch (Exception e) { Log.W("SaveAsync EXCEPTION class=" + className + " " + e.Message); return T.FailTask(e); }
        }

        public Task DeleteAsync()
        {
            try
            {
                if (objectId != null)
                {
                    lock (LocalStore.Sync)
                    {
                        LocalStore.Class(className).RemoveAll(x => x.Id == objectId);
                        LocalStore.Save();
                    }
                }
                return T.Done();
            }
            catch (Exception e) { return T.FailTask(e); }
        }

        public static ParseQuery<ParseObject> GetQuery(string className)
        {
            return new ParseQuery<ParseObject>(className);
        }
    }

    // ------------------------------------------------------------------ ParseUser
    public class ParseUser : ParseObject
    {
        static ParseUser current;
        string pendingPassword;

        public ParseUser() : base("_User") { }

        internal static void SetCurrent(ParseUser u) { current = u; }

        public string Username
        {
            get { object v; return data.TryGetValue("username", out v) ? v as string : null; }
            set { this["username"] = value; }
        }
        public string Email
        {
            get { object v; return data.TryGetValue("email", out v) ? v as string : null; }
            set { this["email"] = value; }
        }
        public string Password { set { pendingPassword = value; } }

        static string Hash(string id, string pw)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(id + ":" + pw)));
        }

        internal override void BeforeSave(Rec r)
        {
            if (pendingPassword != null) { r.Pw = Hash(r.Id, pendingPassword); pendingPassword = null; }
        }

        public Task SignUpAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(Username)) throw new ParseException(ParseException.ErrorCode.UsernameMissing, "Cannot sign up user with an empty name.");
                if (objectId == null && string.IsNullOrEmpty(pendingPassword)) throw new ParseException(ParseException.ErrorCode.PasswordMissing, "Cannot sign up user with an empty password.");
                lock (LocalStore.Sync)
                {
                    string name = Username;
                    bool taken = LocalStore.Class("_User").Any(x => x.Id != objectId && x.Data.ContainsKey("username") && (x.Data["username"] as string) == name);
                    if (taken) throw new ParseException(ParseException.ErrorCode.UsernameTaken, "Account already exists for this username.");
                }
                Task t = SaveAsync();
                t.Wait();
                current = this;
                LocalStore.SetSession(objectId);
                return T.Done();
            }
            catch (AggregateException ae) { return T.FailTask(ae.GetBaseException()); }
            catch (Exception e) { return T.FailTask(e); }
        }

        public static Task<ParseUser> LogInAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(username)) throw new ParseException(ParseException.ErrorCode.UsernameMissing, "Must specify a username for the user to log in with.");
                if (string.IsNullOrEmpty(password)) throw new ParseException(ParseException.ErrorCode.PasswordMissing, "Must specify a password for the user to log in with.");
                lock (LocalStore.Sync)
                {
                    Rec r = LocalStore.Class("_User").FirstOrDefault(x => x.Data.ContainsKey("username") && (x.Data["username"] as string) == username);
                    if (r == null || r.Pw == null || r.Pw != Hash(r.Id, password))
                        throw new ParseException(ParseException.ErrorCode.ObjectNotFound, "Invalid username/password.");
                    ParseUser u = new ParseUser();
                    u.Load(r);
                    current = u;
                    LocalStore.SetSession(u.objectId);
                    return T.Ok<ParseUser>(u);
                }
            }
            catch (Exception e) { return T.Fail<ParseUser>(e); }
        }

        public static void LogOut()
        {
            current = null;
            LocalStore.SetSession(null);
        }

        public static ParseUser CurrentUser
        {
            get
            {
                lock (LocalStore.Sync)
                {
                    if (current == null)
                    {
                        string sid = LocalStore.Session;
                        if (sid != null)
                        {
                            Rec r = LocalStore.Class("_User").FirstOrDefault(x => x.Id == sid);
                            if (r != null) { ParseUser u = new ParseUser(); u.Load(r); current = u; }
                        }
                    }
                    return current;
                }
            }
        }

        public static ParseQuery<ParseUser> Query { get { return new ParseQuery<ParseUser>("_User"); } }
    }

    // ------------------------------------------------------------------ ParseInstallation
    public class ParseInstallation : ParseObject
    {
        static ParseInstallation current;

        public ParseInstallation() : base("_Installation") { }

        public Guid InstallationId
        {
            get { object v; Guid g; return (data.TryGetValue("installationId", out v) && Guid.TryParse(v as string, out g)) ? g : Guid.Empty; }
        }

        public static ParseInstallation CurrentInstallation
        {
            get
            {
                lock (LocalStore.Sync)
                {
                    if (current != null) return current;
                    ParseInstallation inst = new ParseInstallation();
                    List<Rec> list = LocalStore.Class("_Installation");
                    if (list.Count > 0) inst.Load(list[0]);
                    else
                    {
                        inst["installationId"] = Guid.NewGuid().ToString();
                        inst["deviceType"] = "winrt";
                        inst["appName"] = "Riff Racer";
                        inst.SaveAsync().Wait();
                    }
                    current = inst;
                    return current;
                }
            }
        }
    }

    // ------------------------------------------------------------------ ParseFile
    public class ParseFile
    {
        string name;
        string mimeType;
        byte[] bytes;
        bool saved;

        public ParseFile(string name, byte[] data) : this(name, data, null) { }
        public ParseFile(string name, byte[] data, string contentType)
        {
            this.name = name;
            this.bytes = data;
            this.mimeType = contentType;
        }
        internal ParseFile(string storedName, bool alreadySaved)
        {
            this.name = storedName;
            this.saved = alreadySaved;
        }

        public string Name { get { return name; } }
        public string MimeType { get { return mimeType; } }
        public bool IsDirty { get { return !saved; } }

        public Uri Url
        {
            get
            {
                if (!saved) return null;
                return new Uri(LocalFileServer.UrlFor(name));
            }
        }

        public Task SaveAsync()
        {
            try
            {
                if (saved) return T.Done();
                lock (LocalStore.Sync)
                {
                    string safe = new string((name ?? "file").Select(c => (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') ? c : '_').ToArray());
                    string stored = Guid.NewGuid().ToString("N").Substring(0, 12) + "-" + safe;
                    File.WriteAllBytes(Path.Combine(Path.Combine(LocalStore.Dir, "files"), stored), bytes ?? new byte[0]);
                    name = stored;
                    saved = true;
                    bytes = null;
                }
                return T.Done();
            }
            catch (Exception e) { return T.FailTask(e); }
        }
    }

    // Sirve LocalData\files solo por loopback, dentro del propio proceso del juego.
    // Es necesario porque ParseOctane descarga ParseFile.Url con System.Net.Http.HttpClient,
    // que no admite file://. No es un servicio externo ni se instala nada.
    internal static class LocalFileServer
    {
        static TcpListener listener;
        static int port;
        static readonly object gate = new object();

        public static string UrlFor(string storedName)
        {
            Start();
            return "http://127.0.0.1:" + port + "/files/" + Uri.EscapeDataString(storedName);
        }

        static void Start()
        {
            lock (gate)
            {
                if (listener != null) return;
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Thread t = new Thread(Loop);
                t.IsBackground = true;
                t.Name = "RiffRacerLocalFiles";
                t.Start();
            }
        }

        static void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(Handle, c);
            }
        }

        static void Handle(object state)
        {
            TcpClient c = (TcpClient)state;
            try
            {
                using (c)
                {
                    c.ReceiveTimeout = 5000;
                    NetworkStream ns = c.GetStream();
                    byte[] buf = new byte[8192];
                    int len = 0;
                    while (len < buf.Length)
                    {
                        int n = ns.Read(buf, len, buf.Length - len);
                        if (n <= 0) break;
                        len += n;
                        if (Encoding.ASCII.GetString(buf, 0, len).Contains("\r\n\r\n")) break;
                    }
                    string req = Encoding.ASCII.GetString(buf, 0, len);
                    string line = req.Split('\n')[0].Trim();
                    string[] parts = line.Split(' ');
                    byte[] body = null;
                    if (parts.Length >= 2 && (parts[0] == "GET" || parts[0] == "HEAD") && parts[1].StartsWith("/files/"))
                    {
                        string nm = Uri.UnescapeDataString(parts[1].Substring(7));
                        if (nm.IndexOfAny(new char[] { '/', '\\' }) < 0 && nm.IndexOf("..", StringComparison.Ordinal) < 0)
                        {
                            string path = Path.Combine(Path.Combine(LocalStore.Dir, "files"), nm);
                            if (File.Exists(path)) body = File.ReadAllBytes(path);
                        }
                    }
                    bool head = parts.Length >= 1 && parts[0] == "HEAD";
                    string hdr = body != null
                        ? "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    byte[] h = Encoding.ASCII.GetBytes(hdr);
                    ns.Write(h, 0, h.Length);
                    if (body != null && !head) ns.Write(body, 0, body.Length);
                    ns.Flush();
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ ParseQuery
    public class ParseQuery<T> where T : ParseObject
    {
        sealed class Cond { public string Key; public string Op; public object Val; }

        readonly string className;
        List<Cond> conds = new List<Cond>();
        string orderKey;
        bool orderDesc;
        int skip;
        int limit = -1;

        public ParseQuery(string className)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentException("className");
            this.className = className;
        }

        ParseQuery<T> Copy()
        {
            ParseQuery<T> q = new ParseQuery<T>(className);
            q.conds = new List<Cond>(conds);
            q.orderKey = orderKey; q.orderDesc = orderDesc; q.skip = skip; q.limit = limit;
            return q;
        }

        ParseQuery<T> With(string key, string op, object val)
        {
            ParseQuery<T> q = Copy();
            q.conds.Add(new Cond { Key = key, Op = op, Val = val });
            return q;
        }

        public ParseQuery<T> WhereEqualTo(string key, object value) { return With(key, "eq", value); }
        public ParseQuery<T> WhereNotEqualTo(string key, object value) { return With(key, "ne", value); }
        public ParseQuery<T> WhereGreaterThan(string key, object value) { return With(key, "gt", value); }
        public ParseQuery<T> WhereGreaterThanOrEqualTo(string key, object value) { return With(key, "gte", value); }
        public ParseQuery<T> WhereLessThan(string key, object value) { return With(key, "lt", value); }
        public ParseQuery<T> WhereLessThanOrEqualTo(string key, object value) { return With(key, "lte", value); }
        public ParseQuery<T> WhereExists(string key) { return With(key, "exists", null); }
        public ParseQuery<T> WhereDoesNotExist(string key) { return With(key, "nexists", null); }
        public ParseQuery<T> WhereStartsWith(string key, string prefix) { return With(key, "starts", prefix); }

        public ParseQuery<T> WhereContainedIn<TIn>(string key, IEnumerable<TIn> values)
        {
            List<object> l = new List<object>();
            if (values != null) foreach (TIn v in values) l.Add(v);
            return With(key, "in", l);
        }

        public ParseQuery<T> OrderBy(string key) { ParseQuery<T> q = Copy(); q.orderKey = key; q.orderDesc = false; return q; }
        public ParseQuery<T> OrderByDescending(string key) { ParseQuery<T> q = Copy(); q.orderKey = key; q.orderDesc = true; return q; }
        public ParseQuery<T> Skip(int count) { ParseQuery<T> q = Copy(); q.skip = count; return q; }
        public ParseQuery<T> Limit(int count) { ParseQuery<T> q = Copy(); q.limit = count; return q; }
        public ParseQuery<T> Include(string key) { return Copy(); }

        // ---- evaluacion
        static object Field(Rec r, string key)
        {
            if (key == "objectId") return r.Id;
            if (key == "createdAt") return r.Created;
            if (key == "updatedAt") return r.Updated;
            object v;
            return r.Data.TryGetValue(key, out v) ? v : null;
        }

        static bool IsNum(object o)
        {
            return o is long || o is int || o is double || o is float || o is short || o is byte || o is decimal;
        }

        static int? Cmp(object a, object b)
        {
            if (a == null || b == null) return null;
            if (IsNum(a) && IsNum(b)) return Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(b, CultureInfo.InvariantCulture));
            if (a is DateTime && b is DateTime) return ((DateTime)a).CompareTo((DateTime)b);
            if (a is string && b is string) return string.CompareOrdinal((string)a, (string)b);
            if (a is bool && b is bool) return ((bool)a).CompareTo((bool)b);
            return null;
        }

        static bool Eq(object v, object target)
        {
            if (v == null || target == null) return v == null && target == null;
            List<object> arr = v as List<object>;
            if (arr != null && !(target is List<object>)) return arr.Any(x => Eq(x, target));
            int? c = Cmp(v, target);
            if (c.HasValue) return c.Value == 0;
            ParseObject p1 = v as ParseObject, p2 = target as ParseObject;
            if (p1 != null && p2 != null) return p1.ObjectId == p2.ObjectId && p1.ClassName == p2.ClassName;
            return v.Equals(target);
        }

        static bool Match(Rec r, Cond c)
        {
            object v = Field(r, c.Key);
            switch (c.Op)
            {
                case "eq": return Eq(v, c.Val);
                case "ne": return !Eq(v, c.Val);
                case "gt": { int? x = Cmp(v, c.Val); return x.HasValue && x.Value > 0; }
                case "gte": { int? x = Cmp(v, c.Val); return x.HasValue && x.Value >= 0; }
                case "lt": { int? x = Cmp(v, c.Val); return x.HasValue && x.Value < 0; }
                case "lte": { int? x = Cmp(v, c.Val); return x.HasValue && x.Value <= 0; }
                case "in": return ((List<object>)c.Val).Any(x => Eq(v, x));
                case "exists": return v != null;
                case "nexists": return v == null;
                case "starts": return v is string && ((string)v).StartsWith((string)c.Val, StringComparison.Ordinal);
            }
            return false;
        }

        sealed class OrderCmp : IComparer<Rec>
        {
            readonly string key;
            public OrderCmp(string key) { this.key = key; }
            public int Compare(Rec a, Rec b)
            {
                object x = Field(a, key), y = Field(b, key);
                if (x == null && y == null) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                int? c = Cmp(x, y);
                return c.HasValue ? c.Value : 0;
            }
        }

        List<Rec> Matching()
        {
            lock (LocalStore.Sync)
            {
                if (className == "Profile_Steam")
                {
                    List<Rec> profs = LocalStore.Class("Profile_Steam");
                    if (profs.Count > 0)
                    {
                        Cond sc = conds.FirstOrDefault(c => c.Key == "SystemID" && c.Op == "eq");
                        if (sc != null && sc.Val != null)
                        {
                            profs[0].Data["SystemID"] = sc.Val.ToString();
                        }
                    }
                }
                IEnumerable<Rec> e = LocalStore.Class(className).Where(r => conds.All(c => Match(r, c))).ToList();
                if (orderKey != null) e = orderDesc ? e.OrderByDescending(r => r, new OrderCmp(orderKey)) : e.OrderBy(r => r, new OrderCmp(orderKey));
                return e.ToList();
            }
        }

        T Make(Rec r)
        {
            ParseObject o = typeof(T) == typeof(ParseObject) ? new ParseObject(className) : (ParseObject)Activator.CreateInstance(typeof(T), true);
            lock (LocalStore.Sync) { o.Load(r); }
            return (T)o;
        }

        public Task<IEnumerable<T>> FindAsync()
        {
            try
            {
                List<Rec> all = Matching();
                int lim = limit < 0 ? 100 : limit;
                List<T> res = all.Skip(Math.Max(0, skip)).Take(lim).Select(r => Make(r)).ToList();
                Log.W("FindAsync class=" + className + " results=" + res.Count);
                return T_.Ok<IEnumerable<T>>(res);
            }
            catch (Exception e) { Log.W("FindAsync EXCEPTION class=" + className + " " + e.Message); return T_.Fail<IEnumerable<T>>(e); }
        }

        public Task<T> FirstOrDefaultAsync()
        {
            try
            {
                Rec r = Matching().Skip(Math.Max(0, skip)).FirstOrDefault();
                return T_.Ok<T>(r == null ? null : Make(r));
            }
            catch (Exception e) { return T_.Fail<T>(e); }
        }

        public Task<T> FirstAsync()
        {
            try
            {
                Rec r = Matching().Skip(Math.Max(0, skip)).FirstOrDefault();
                if (r == null) throw new ParseException(ParseException.ErrorCode.ObjectNotFound, "No results matched the query.");
                return T_.Ok<T>(Make(r));
            }
            catch (Exception e) { return T_.Fail<T>(e); }
        }

        public Task<T> GetAsync(string objectId)
        {
            try
            {
                Rec r = Matching().FirstOrDefault(x => x.Id == objectId);
                if (r == null) throw new ParseException(ParseException.ErrorCode.ObjectNotFound, "Object with the given objectId not found.");
                return T_.Ok<T>(Make(r));
            }
            catch (Exception e) { return T_.Fail<T>(e); }
        }

        public Task<int> CountAsync()
        {
            try { int c = Matching().Count; Log.W("CountAsync class=" + className + " count=" + c); return T_.Ok<int>(c); }
            catch (Exception e) { Log.W("CountAsync EXCEPTION class=" + className + " " + e.Message); return T_.Fail<int>(e); }
        }
    }

    // Alias interno para evitar choque del nombre T (parametro generico) con la clase auxiliar.
    internal static class T_
    {
        public static Task<R> Ok<R>(R v) { return Task.FromResult(v); }
        public static Task<R> Fail<R>(Exception e) { return T.Fail<R>(e); }
    }
}
