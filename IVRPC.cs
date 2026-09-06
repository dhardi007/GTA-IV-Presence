using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace IVRPC
{
    class Config
    {
        public string ClientId = "";
        public string ProcessNames = "GTAIV";
        public int CheckIntervalSeconds = 5;
        public string Details = "Jugando GTA IV";
        public string State = "En Liberty City";
        public string LargeImageKey = "";
        public string LargeImageText = "Grand Theft Auto IV";
        public string SmallImageKey = "";
        public string SmallImageText = "";
    }

    class Program
    {
        static Config cfg = new Config();
        static ClientWebSocket ws;
        static byte[] rxBuf = new byte[131072];
        static long nonceSeq = 0;
        static volatile bool alive = false;
        static bool wasInGame = false;
        static string lastMsg = "";
        static int procId = Process.GetCurrentProcess().Id;

        static string JsonEsc(string s)
        {
            if (s == null || s.Length == 0) return "\"\"";
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') { sb.Append((char)92); sb.Append('"'); }
                else if (c == 92) { sb.Append((char)92); sb.Append((char)92); }
                else if (c == 10 || c == 13 || c == 9)
                {
                    sb.Append((char)92);
                    if (c == 10) sb.Append('n');
                    else if (c == 13) sb.Append('r');
                    else sb.Append('t');
                }
                else if (c < 32) { sb.Append((char)92); sb.Append('u'); sb.Append(((int)c).ToString("x4")); }
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        static bool ReadFrame(out uint opcode, out string payload)
        {
            opcode = 0; payload = null;
            try
            {
                MemoryStream ms = new MemoryStream();
                WebSocketReceiveResult res = null;
                while (true)
                {
                    ArraySegment<byte> seg = new ArraySegment<byte>(rxBuf);
                    res = ws.ReceiveAsync(seg, CancellationToken.None).GetAwaiter().GetResult();
                    if (res.MessageType == WebSocketMessageType.Close) return false;
                    ms.Write(rxBuf, 0, res.Count);
                    if (res.EndOfMessage) break;
                }
                if (res == null) return false;
                byte[] data = ms.ToArray();
                if (data.Length == 0) return false;
                payload = Encoding.UTF8.GetString(data);
                opcode = 1;
                return true;
            }
            catch { return false; }
        }

        static void WriteFrame(uint opcode, string payload)
        {
            byte[] p = Encoding.UTF8.GetBytes(payload);
            ws.SendAsync(new ArraySegment<byte>(p), WebSocketMessageType.Text, true,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        static void ClosePipe()
        {
            alive = false;
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                    ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).GetAwaiter().GetResult();
                if (ws != null) ws.Dispose();
            }
            catch { }
            ws = null;
        }

        static void ListenThread()
        {
            while (alive)
            {
                try
                {
                    uint op; string payload;
                    if (!ReadFrame(out op, out payload)) break;
                    if (op == 3 && payload != null) WriteFrame(4, payload);
                    else if (op == 2)
                    {
                        Console.WriteLine("[!] " + payload);
                        break;
                    }
                }
                catch { break; }
            }
            alive = false;
        }

        static string WhichClientRunning()
        {
            string[] names = { "Vesktop", "Discord", "discord", "DiscordPTB", "DiscordCanary" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (Process.GetProcessesByName(names[i]).Length > 0) return names[i];
                }
                catch { }
            }
            return null;
        }

        static string HintFor(string client)
        {
            if (client == null)
                return "Discord/Vesktop no esta en ejecucion. Abrelo.";
            if (client == "Vesktop")
                return "Vesktop abierto pero sin RPC: activa 'Enable Rich Presence via arRPC' en Ajustes de Vesktop y reinicialo.";
            return client + " abierto pero sin RPC: activa 'Compartir tu actividad detectada' (Ajustes -> Actividad).";
        }

        static string ConnectToDiscord()
        {
            try
            {
                string url = "ws://127.0.0.1:6463/?v=1&client_id=" +
                    Uri.EscapeDataString(cfg.ClientId) + "&encoding=json";
                ws = new ClientWebSocket();
                ws.ConnectAsync(new Uri(url), CancellationToken.None).GetAwaiter().GetResult();
                alive = true;
                Console.WriteLine("[OK] Conectado a Discord via arRPC WebSocket 6463.");
                return "OK";
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] No hay arRPC WebSocket: " + ex.GetType().Name + " - " + ex.Message);
            }
            ClosePipe();
            return WhichClientRunning();
        }

        static string Activity(long startSec, bool clear)
        {
            if (clear) return "null";
            StringBuilder a = new StringBuilder();
            a.Append('{');
            a.Append((char)34).Append("details").Append((char)34).Append(':').Append(JsonEsc(cfg.Details)).Append(',');
            a.Append((char)34).Append("state").Append((char)34).Append(':').Append(JsonEsc(cfg.State));
            if (startSec > 0)
            {
                a.Append(',').Append((char)34).Append("timestamps").Append((char)34).Append(':');
                a.Append('{');
                a.Append((char)34).Append("start").Append((char)34).Append(':').Append(startSec);
                a.Append('}');
            }
            if (cfg.LargeImageKey.Length > 0 || cfg.LargeImageText.Length > 0 ||
                cfg.SmallImageKey.Length > 0 || cfg.SmallImageText.Length > 0)
            {
                a.Append(',').Append((char)34).Append("assets").Append((char)34).Append(':').Append('{');
                bool first = true;
                if (cfg.LargeImageKey.Length > 0)
                {
                    if (!first) a.Append(','); first = false;
                    a.Append((char)34).Append("large_image").Append((char)34).Append(':').Append(JsonEsc(cfg.LargeImageKey));
                }
                if (cfg.LargeImageText.Length > 0)
                {
                    if (!first) a.Append(','); first = false;
                    a.Append((char)34).Append("large_text").Append((char)34).Append(':').Append(JsonEsc(cfg.LargeImageText));
                }
                if (cfg.SmallImageKey.Length > 0)
                {
                    if (!first) a.Append(','); first = false;
                    a.Append((char)34).Append("small_image").Append((char)34).Append(':').Append(JsonEsc(cfg.SmallImageKey));
                }
                if (cfg.SmallImageText.Length > 0)
                {
                    if (!first) a.Append(','); first = false;
                    a.Append((char)34).Append("small_text").Append((char)34).Append(':').Append(JsonEsc(cfg.SmallImageText));
                }
                a.Append('}');
            }
            a.Append('}');
            return a.ToString();
        }

        static void SendCommand(string cmd, string argsJson)
        {
            string msg = "{" +
                (char)34 + "cmd" + (char)34 + ":" + JsonEsc(cmd) + "," +
                (char)34 + "args" + (char)34 + ":" + argsJson + "," +
                (char)34 + "nonce" + (char)34 + ":" + JsonEsc("iv" + (nonceSeq++)) +
                "}";
            WriteFrame(1, msg);
        }

        static void SetPresence(long startSec)
        {
            string args = "{" + (char)34 + "pid" + (char)34 + ":" + procId + "," +
                          (char)34 + "activity" + (char)34 + ":" + Activity(startSec, false) + "}";
            SendCommand("SET_ACTIVITY", args);
        }

        static void ClearPresence()
        {
            string args = "{" + (char)34 + "pid" + (char)34 + ":" + procId + "," +
                          (char)34 + "activity" + (char)34 + ":null}";
            SendCommand("SET_ACTIVITY", args);
        }

        static bool GameRunning()
        {
            string[] names = cfg.ProcessNames.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    if (Process.GetProcessesByName(names[i].Trim()).Length > 0) return true;
                }
                catch { }
            }
            return false;
        }

        static void LoadConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (!File.Exists(configPath)) return;
            try
            {
                string json = File.ReadAllText(configPath);
                cfg.ClientId             = JsonGet(json, "ClientId", cfg.ClientId);
                cfg.ProcessNames         = JsonGet(json, "ProcessNames", cfg.ProcessNames);
                cfg.Details              = JsonGet(json, "Details", cfg.Details);
                cfg.State                = JsonGet(json, "State", cfg.State);
                cfg.LargeImageKey        = JsonGet(json, "LargeImageKey", cfg.LargeImageKey);
                cfg.LargeImageText       = JsonGet(json, "LargeImageText", cfg.LargeImageText);
                cfg.SmallImageKey        = JsonGet(json, "SmallImageKey", cfg.SmallImageKey);
                cfg.SmallImageText       = JsonGet(json, "SmallImageText", cfg.SmallImageText);
                int iv = 0;
                if (int.TryParse(JsonGet(json, "CheckIntervalSeconds", cfg.CheckIntervalSeconds.ToString()), NumberStyles.Integer, CultureInfo.InvariantCulture, out iv) && iv > 0)
                    cfg.CheckIntervalSeconds = iv;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] config.json no valido: " + ex.Message);
            }
        }

        static string JsonGet(string json, string key, string fallback)
        {
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i);
            if (i < 0) return fallback;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == 9 || json[i] == 13 || json[i] == 10)) i++;
            if (i >= json.Length) return fallback;
            if (json[i] == '"')
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < json.Length && json[i] != '"') { if (json[i] != 92) sb.Append(json[i]); i++; }
                return sb.ToString();
            }
            else
            {
                int j = i;
                while (j < json.Length && json[j] != ',' && json[j] != '}' && json[j] != 13 && json[j] != 10) j++;
                return json.Substring(i, j - i).Trim();
            }
        }

        static void Main(string[] args)
        {
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"), e.ExceptionObject.ToString());
                    }
                    catch { }
                };
            }
            catch { }
            try { Console.Title = "GTA IV Discord RPC"; } catch { }
            Console.WriteLine("==========================================");
            Console.WriteLine("  GTA IV Discord Rich Presence");
            Console.WriteLine("==========================================");

            LoadConfig();
            Console.WriteLine("App ID  : " + cfg.ClientId);
            Console.WriteLine("Procesos: " + cfg.ProcessNames);
            Console.WriteLine("");

            if (string.IsNullOrEmpty(cfg.ClientId))
            {
                Console.WriteLine("[ERROR] Falta 'ClientId' en config.json");
                Console.WriteLine("Pulsa Enter para salir.");
                Console.ReadLine();
                return;
            }

            while (true)
            {
                try
                {
                    if (!alive)
                    {
                        string r = ConnectToDiscord();
                        if (r != "OK")
                        {
                            string h = HintFor(r);
                            if (h != lastMsg) { Console.WriteLine("[!] " + h); lastMsg = h; }
                            Thread.Sleep(5000);
                            continue;
                        }
                        wasInGame = false;
                        Thread t = new Thread(ListenThread);
                        t.IsBackground = true;
                        t.Start();
                    }
                    else
                    {
                        bool inGame = GameRunning();
                        if (inGame && !wasInGame)
                        {
                            SetPresence(((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds());
                            wasInGame = true;
                            Console.WriteLine("[OK] " + DateTime.Now.ToString("HH:mm:ss") + " GTA IV detectado -> presencia activada.");
                        }
                        else if (!inGame && wasInGame)
                        {
                            ClearPresence();
                            wasInGame = false;
                            Console.WriteLine("[OK] " + DateTime.Now.ToString("HH:mm:ss") + " GTA IV cerrado -> presencia limpia.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] " + ex.Message);
                }
                Thread.Sleep(cfg.CheckIntervalSeconds * 1000);
            }
        }
    }
}