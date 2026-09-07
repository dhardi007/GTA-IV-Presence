using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

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
        public int WantedOffset = 0xebd0d0;
        public bool WantedEnabled = true;
        public string WantedText = "Busqueda: {n} estrella{s}";
        public string WantedZeroText = "Sin busqueda";
        public int HealthOffset = 0x119d06c;
        public bool HealthEnabled = true;
        public string HealthText = "Salud: {h}%";
        public int MoneyOffset = 0xeb7760;
        public bool MoneyEnabled = true;
        public string MoneyText = "Dinero: ${m}";
        public int MissionOffset = 0x119af24;
        public bool MissionEnabled = true;
        public string MissionActiveText = "En mision";
        public string MissionFreeText = "Libre";
        public int VehicleOffset = 0xeb901c;
        public bool VehicleEnabled = true;
        public string VehicleInText = "En vehiculo";
        public string VehicleFootText = "A pie";
        public string JoinSeparator = " | ";
    }

    class Native
    {
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(IntPtr h, int cls, out IntPtr outBuf, int size, IntPtr retLen);
    }

    class GameData
    {
        static IntPtr hProc = IntPtr.Zero;
        static int gameBase = 0;

        public static bool Attach(int pid)
        {
            Detach();
            hProc = Native.OpenProcess(Native.PROCESS_VM_READ | Native.PROCESS_QUERY_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return false;
            IntPtr peb;
            if (Native.NtQueryInformationProcess(hProc, 26, out peb, IntPtr.Size, IntPtr.Zero) != 0) return false;
            byte[] b = ReadAnon(peb.ToInt64() + 8, 4);
            if (b == null || b.Length < 4) return false;
            gameBase = BitConverter.ToInt32(b, 0);
            return gameBase != 0;
        }

        public static void Detach()
        {
            if (hProc != IntPtr.Zero) { Native.CloseHandle(hProc); hProc = IntPtr.Zero; }
        }

        static byte[] ReadAnon(long addr, int size)
        {
            if (hProc == IntPtr.Zero) return null;
            byte[] buf = new byte[size];
            IntPtr rd;
            if (!Native.ReadProcessMemory(hProc, new IntPtr(addr), buf, size, out rd)) return null;
            if (rd.ToInt64() != size) return null;
            return buf;
        }

        public static int ReadInt(int relative)
        {
            if (gameBase == 0) return -1;
            long addr = gameBase + (long)relative;
            byte[] b = ReadAnon(addr, 4);
            if (b == null) return -1;
            return BitConverter.ToInt32(b, 0);
        }

        public static float ReadFloat(int relative)
        {
            if (gameBase == 0) return float.NaN;
            long addr = gameBase + (long)relative;
            byte[] b = ReadAnon(addr, 4);
            if (b == null) return float.NaN;
            return BitConverter.ToSingle(b, 0);
        }

        public static int Base()
        {
            return gameBase;
        }

        public static int Wanted(int offset)
        {
            return ReadInt(offset);
        }
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
        static long startSec = 0;
        static int lastWanted = -2;
        static float lastHealth = float.NaN;
        static float lastMoney = float.NaN;
        static int lastMission = -2;
        static int lastVehicle = -2;
        static bool areFledged = false;

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

        static void RefreshPresence()
        {
            int want = -1;
            if (areFledged && cfg.WantedEnabled) want = GameData.Wanted(cfg.WantedOffset);
            float health = float.NaN;
            if (areFledged && cfg.HealthEnabled) health = GameData.ReadFloat(cfg.HealthOffset);
            float money = float.NaN;
            if (areFledged && cfg.MoneyEnabled) money = GameData.ReadFloat(cfg.MoneyOffset);
            int mission = -1;
            if (areFledged && cfg.MissionEnabled) mission = GameData.ReadInt(cfg.MissionOffset);
            int vehicle = -1;
            if (areFledged && cfg.VehicleEnabled) vehicle = GameData.ReadInt(cfg.VehicleOffset);
            bool changed = (want != lastWanted);
            float lastH = lastHealth;
            bool healthChanged = !(float.IsNaN(health) && float.IsNaN(lastH)) &&
                                 (float.IsNaN(health) || float.IsNaN(lastH) || Math.Abs((double)(health - lastH)) > 0.3);
            float lastM = lastMoney;
            bool moneyChanged = !(float.IsNaN(money) && float.IsNaN(lastM)) &&
                                (float.IsNaN(money) || float.IsNaN(lastM) || Math.Abs((double)(money - lastM)) > 0.01);
            bool missionChanged = (mission != lastMission);
            bool vehicleChanged = (vehicle != lastVehicle);
            if (!changed && !healthChanged && !moneyChanged && !missionChanged && !vehicleChanged) return;
            lastWanted = want;
            lastHealth = health;
            lastMoney = money;
            lastMission = mission;
            lastVehicle = vehicle;
            StringBuilder parts = new StringBuilder();
            if (want > 0 && cfg.WantedText.Length > 0)
                parts.Append(cfg.WantedText.Replace("{n}", want.ToString()).Replace("{s}", want > 1 ? "s" : ""));
            else if (cfg.WantedZeroText.Length > 0)
                parts.Append(cfg.WantedZeroText);
            if (!float.IsNaN(health) && health >= 0 && health < 400)
            {
                int hp = (int)Math.Round(health);
                string line = cfg.HealthText.Replace("{h}", hp.ToString());
                if (parts.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    parts.Append(cfg.JoinSeparator);
                parts.Append(line);
            }
            if (!float.IsNaN(money) && money >= 0 && money < 1e9f)
            {
                int mc = (int)Math.Round(money);
                string line = cfg.MoneyText.Replace("{m}", mc.ToString());
                if (parts.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    parts.Append(cfg.JoinSeparator);
                parts.Append(line);
            }
            if (mission >= 0)
            {
                string line = (mission == 4 || mission > 2) ? cfg.MissionActiveText :
                              (cfg.MissionFreeText.Length > 0 ? cfg.MissionFreeText : "");
                if (parts.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    parts.Append(cfg.JoinSeparator);
                parts.Append(line);
            }
            if (vehicle >= 0)
            {
                string line = (vehicle == 7) ? cfg.VehicleInText :
                              (cfg.VehicleFootText.Length > 0 ? cfg.VehicleFootText : "");
                if (parts.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    parts.Append(cfg.JoinSeparator);
                parts.Append(line);
            }
            if (want < 0 && cfg.State.Length > 0 && parts.Length == 0)
            {
                parts.Append(cfg.State);
            }
            string state = parts.ToString();
            if (state.Length == 0) state = cfg.State;
            cfg.State = state;
            SetPresence(startSec);
            Console.WriteLine("[OK] " + DateTime.Now.ToString("HH:mm:ss") + " Wanted=" + want +
                              " Health=" + (float.IsNaN(health) ? "n/a" : health.ToString("0.0")) +
                              " Money=" + (float.IsNaN(money) ? "n/a" : money.ToString("0")) +
                              " Mission=" + mission + " Vehicle=" + vehicle + " -> " + state);
        }

        static int GamePid()
        {
            string[] names = cfg.ProcessNames.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string n in names)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(n.Trim());
                    if (ps.Length > 0) return ps[0].Id;
                }
                catch { }
            }
            return 0;
        }

        static bool GameRunning()
        {
            return GamePid() != 0;
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
                int wo = 0;
                if (int.TryParse(JsonGet(json, "WantedOffset", cfg.WantedOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out wo) && wo != 0)
                    cfg.WantedOffset = wo;
                string we = JsonGet(json, "WantedEnabled", cfg.WantedEnabled ? "1" : "0");
                cfg.WantedEnabled = we == "1" || we.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.WantedText     = JsonGet(json, "WantedText", cfg.WantedText);
                cfg.WantedZeroText = JsonGet(json, "WantedZeroText", cfg.WantedZeroText);
                int hoff = 0;
                if (int.TryParse(JsonGet(json, "HealthOffset", cfg.HealthOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hoff) && hoff != 0)
                    cfg.HealthOffset = hoff;
                string he = JsonGet(json, "HealthEnabled", cfg.HealthEnabled ? "1" : "0");
                cfg.HealthEnabled = he == "1" || he.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.HealthText     = JsonGet(json, "HealthText", cfg.HealthText);
                int moff = 0;
                if (int.TryParse(JsonGet(json, "MoneyOffset", cfg.MoneyOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out moff) && moff != 0)
                    cfg.MoneyOffset = moff;
                string me = JsonGet(json, "MoneyEnabled", cfg.MoneyEnabled ? "1" : "0");
                cfg.MoneyEnabled = me == "1" || me.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.MoneyText     = JsonGet(json, "MoneyText", cfg.MoneyText);
                int mioff = 0;
                if (int.TryParse(JsonGet(json, "MissionOffset", cfg.MissionOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mioff) && mioff != 0)
                    cfg.MissionOffset = mioff;
                string mien = JsonGet(json, "MissionEnabled", cfg.MissionEnabled ? "1" : "0");
                cfg.MissionEnabled = mien == "1" || mien.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.MissionActiveText = JsonGet(json, "MissionActiveText", cfg.MissionActiveText);
                cfg.MissionFreeText   = JsonGet(json, "MissionFreeText", cfg.MissionFreeText);
                int veh = 0;
                if (int.TryParse(JsonGet(json, "VehicleOffset", cfg.VehicleOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out veh) && veh != 0)
                    cfg.VehicleOffset = veh;
                string ven = JsonGet(json, "VehicleEnabled", cfg.VehicleEnabled ? "1" : "0");
                cfg.VehicleEnabled = ven == "1" || ven.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.VehicleInText   = JsonGet(json, "VehicleInText", cfg.VehicleInText);
                cfg.VehicleFootText = JsonGet(json, "VehicleFootText", cfg.VehicleFootText);
                cfg.JoinSeparator  = JsonGet(json, "JoinSeparator", cfg.JoinSeparator);
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
                            int pid = GamePid();
                            bool attached = pid != 0 && GameData.Attach(pid) &&
                                (cfg.WantedEnabled || cfg.HealthEnabled || cfg.MoneyEnabled || cfg.MissionEnabled || cfg.VehicleEnabled);
                            if (attached)
                            {
                                Console.WriteLine("[OK] " + DateTime.Now.ToString("HH:mm:ss") + " Memoria de GTAIV leida (pid " + pid + ", base " + GameData.Base().ToString("x") + ").");
                            }
                            areFledged = attached;
                            lastWanted = -2;
                            lastHealth = float.NaN;
                            lastMoney = float.NaN;
                            lastMission = -2;
                            lastVehicle = -2;
                            startSec = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                            wasInGame = true;
                            RefreshPresence();
                            Console.WriteLine("[OK] " + DateTime.Now.ToString("HH:mm:ss") + " GTA IV detectado -> presencia activada.");
                        }
                        else if (inGame && wasInGame)
                        {
                            RefreshPresence();
                        }
                        else if (!inGame && wasInGame)
                        {
                            ClearPresence();
                            GameData.Detach();
                            wasInGame = false;
                            lastWanted = -2;
                            lastHealth = float.NaN;
                            lastMoney = float.NaN;
                            lastMission = -2;
                            lastVehicle = -2;
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