using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Parse;

class Test
{
    static int fails = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FALLA ") + what); if (!ok) fails++; }

    static void Main()
    {
        ParseClient.Configuration cfg = new ParseClient.Configuration();
        cfg.ApplicationId = "x"; cfg.Server = "http://riffapi.hobune.stream"; cfg.WindowsKey = "k";
        ParseClient.Initialize(cfg);

        // Configuration sembrada
        ParseObject conf = ParseObject.GetQuery("Configuration").FirstOrDefaultAsync().Result;
        Check(conf != null && conf.Get<int>("TrackConversionCost") == 0, "Configuration sembrada, TrackConversionCost=0");

        // Usuario
        ParseUser u = new ParseUser(); u.Username = "dev-123"; u.Password = "pw"; u.Email = "a@b.c";
        u.SignUpAsync().Wait();
        Check(ParseUser.CurrentUser != null && ParseUser.CurrentUser.Username == "dev-123", "SignUp + CurrentUser");
        ParseUser.LogOut();
        Check(ParseUser.CurrentUser == null, "LogOut");
        ParseUser l = ParseUser.LogInAsync("dev-123", "pw").Result;
        Check(l.ObjectId == u.ObjectId, "LogIn correcto");
        var bad = ParseUser.LogInAsync("dev-123", "mal");
        try { bad.Wait(); Check(false, "LogIn invalido debe fallar"); } catch (AggregateException) { Check(bad.IsFaulted, "LogIn invalido falla"); }
        var dup = new ParseUser(); dup.Username = "dev-123"; dup.Password = "x";
        var t2 = dup.SignUpAsync(); try { t2.Wait(); } catch (AggregateException) { }
        Check(t2.IsFaulted, "SignUp duplicado falla");

        // Objetos + consultas (pistas creadas por el usuario / listas)
        ParseObject song = new ParseObject("Songs");
        song["SongName"] = "Mi cancion"; song["Plays"] = 0; song["Score"] = 12.5; song.SaveAsync().Wait();
        song.Increment("Plays"); song.Increment("Plays"); song.SaveAsync().Wait();
        Check(song.Get<int>("Plays") == 2, "Increment x2 persistido");
        ParseObject pl = new ParseObject("Playlists"); pl["name"] = "Mis pistas"; pl["creator"] = "dev-123"; pl.Add("songs", "A"); pl.Add("songs", "B"); pl.SaveAsync().Wait();
        var found = ParseObject.GetQuery("Playlists").WhereEqualTo("creator", "dev-123").FindAsync().Result.ToList();
        Check(found.Count == 1 && found[0].Get<List<object>>("songs").Count == 2, "Playlist propia con lista de canciones");
        for (int i = 0; i < 5; i++) { ParseObject s = new ParseObject("SongPlays"); s["n"] = i; s["tag"] = "t" + (i % 2); s.SaveAsync().Wait(); }
        var q = ParseObject.GetQuery("SongPlays").WhereGreaterThanOrEqualTo("n", 1).WhereLessThanOrEqualTo("n", 3).OrderByDescending("n");
        var rs = q.FindAsync().Result.ToList();
        Check(rs.Count == 3 && rs[0].Get<int>("n") == 3, "Rango + orden descendente");
        Check(ParseObject.GetQuery("SongPlays").Skip(1).Limit(2).FindAsync().Result.Count() == 2, "Skip/Limit");
        Check(ParseObject.GetQuery("SongPlays").WhereContainedIn("tag", new[] { "t1" }).CountAsync().Result == 2, "WhereContainedIn + Count");
        Check(ParseObject.GetQuery("SongPlays").WhereEqualTo("objectId", rs[0].ObjectId).FindAsync().Result.Count() == 1, "Consulta por objectId");
        rs[0].DeleteAsync().Wait();
        Check(ParseObject.GetQuery("SongPlays").CountAsync().Result == 4, "DeleteAsync");

        // Archivos: ParseFile.Url descargado con HttpClient como hace ParseOctane
        byte[] payload = Encoding.UTF8.GetBytes("datos de pista \u00e1\u00e9\u00ed");
        ParseFile f = new ParseFile("track.dat", payload, "application/octet-stream");
        Check(f.Url == null, "Url nula antes de guardar");
        f.SaveAsync().Wait();
        ParseObject fd = new ParseObject("FileData"); fd["data"] = f; fd["name"] = "track.dat"; fd.SaveAsync().Wait();
        ParseObject fd2 = ParseObject.GetQuery("FileData").FirstOrDefaultAsync().Result;
        ParseFile f2 = fd2.Get<ParseFile>("data");
        byte[] got = new HttpClient().GetByteArrayAsync(f2.Url).Result;
        Check(got.SequenceEqual(payload), "Descarga por HttpClient desde ParseFile.Url (" + f2.Url + ")");

        // Push/Analytics/Installation
        ParsePush p = new ParsePush(); p.Channels = new List<string> { "c" }; p.Data = new Dictionary<string, object>();
        p.SendAsync().Wait(); ParseAnalytics.TrackAppOpenedAsync().Wait();
        Check(ParseInstallation.CurrentInstallation.ObjectId != null, "Installation + Push + Analytics");

        // Get de clave inexistente = KeyNotFoundException como el original
        try { conf.Get<int>("NoExiste"); Check(false, "KeyNotFound"); } catch (KeyNotFoundException) { Check(true, "Get de clave inexistente lanza KeyNotFoundException"); }

        Console.WriteLine(fails == 0 ? "TODO OK" : ("FALLOS: " + fails));
        Environment.Exit(fails);
    }
}
