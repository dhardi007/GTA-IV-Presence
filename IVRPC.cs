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
        public string FixedState = "In Liberty City";
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
        public int VehiclePtrOffset = 0x1200184;
        public int VehicleModelOffset = 0x1654;
        public int VehicleModelNameOffset = 0x0;
        public int VehicleNameLen = 24;
        public int VehiclePosOffset = 0x90;
        public string VehicleText = "En {model} · {speed} mph";
        public string JoinSeparator = " | ";
        public int PlayerPosOffset = 0xd736b0;
        public bool ZoneEnabled = true;
        public string ZoneText = "En {zone}";
        public int MissionTitleOffset = 0xd734e8;
        public int MissionTitleLen = 128;
        public bool CharacterEnabled = true;
        public string CharacterText = "{character}";
        public string CharacterDefault = "Niko Belic";
        public bool WeaponEnabled = true;
        public int WeaponSlotOffset = 0x768;
        public int WeaponAmmoOffset = 0x5e8;
        public int PlayerPedPtrOffset = 0x14c6998;
        public string PlayerPedPtrOffsets = "14c6998,14cfba8,14cfbbc,14cfbc8,14c69ac,14c69b8,14cac94,14c9a88,14bcaa8";
        public int DriverPedOffset = 0x50;
        public int PedHealthField = 0x20;
        public float HealthMatchTolerance = 4f;
        public string WeaponText = "Weapon: {weapon} · {ammo} ammo";
    }

    class ZoneInfo
    {
        public string Key;
        public string Name;
        public float X1, Y1, Z1, X2, Y2, Z2;
        public ZoneInfo(string key, string name, float x1, float y1, float z1, float x2, float y2, float z2)
        {
            Key = key; Name = name;
            X1 = x1; Y1 = y1; Z1 = z1; X2 = x2; Y2 = y2; Z2 = z2;
        }
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

        public static int ReadIntAbs(long addr)
        {
            byte[] b = ReadAnon(addr, 4);
            if (b == null) return -1;
            return BitConverter.ToInt32(b, 0);
        }

        public static float ReadFloatAbs(long addr)
        {
            byte[] b = ReadAnon(addr, 4);
            if (b == null) return float.NaN;
            return BitConverter.ToSingle(b, 0);
        }

        public static string ReadString(long addr, int max)
        {
            if (addr <= 0 || max <= 0) return "";
            byte[] b = ReadAnon(addr, max);
            if (b == null) return "";
            int len = 0;
            while (len < b.Length && b[len] != 0) len++;
            return Encoding.ASCII.GetString(b, 0, len);
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
        static string lastModel = "";
        static int lastSpeedMph = -2;
        static double lastPx = 0, lastPy = 0, lastPz = 0;
        static long lastPosMs = 0;
        static bool areFledged = false;
        static string lastZone = "";
        static string lastMissionTitle = "";
        static string lastWeapon = "";

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

        static string ModelDisplay(string raw)
        {
            if (raw == null) return "";
            if (!IsValidModelName(raw)) return "";
            string up = raw.Trim().ToUpperInvariant();
            if (up.Length == 0) return "";
            string full;
            if (ModelNames.TryGetValue(up, out full)) return full;
            return up.Substring(0, 1) + up.Substring(1).ToLowerInvariant();
        }

        static bool IsValidModelName(string raw)
        {
            if (raw == null || raw.Length < 2 || raw.Length > 24) return false;
            int alnum = 0;
            foreach (char c in raw)
            {
                if (c >= 'A' && c <= 'Z') alnum++;
                else if (c >= 'a' && c <= 'z') alnum++;
                else if (c >= '0' && c <= '9') alnum++;
                else if (c == '_') continue;
                else return false;
            }
            return alnum >= 2;
        }

        static readonly System.Collections.Generic.Dictionary<string, string> ModelNames =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "CAVCADE", "Cavalcade" },
                { "COGNOS", "Cognoscenti" },
                { "DILETT", "Dilletante" },
                { "FEROCI", "Feroci" },
                { "ESPERANT", "Esperant" },
                { "VEH_POLICE_OLD", "Police Cruiser" },
            };

        static readonly System.Collections.Generic.Dictionary<int, string> WeaponNames =
            new System.Collections.Generic.Dictionary<int, string>
            {
                { 0, "Punches" },
                { 15, "Knife" },
                { 16, "Pistol" },
                { 22, "AK47" },
                { 28, "Grenade" },
            };

                private static readonly ZoneInfo[] Zones = new ZoneInfo[] {
            new ZoneInfo("Zact1", "Acter", -2023.890000f, 374.040000f, -20.000000f, -1313.850000f, 641.471000f, 350.000000f),
            new ZoneInfo("Zact2", "Acter", -2023.890000f, 203.392000f, -20.000000f, -1383.780000f, 374.040000f, 350.000000f),
            new ZoneInfo("Zacti", "Acter Industrial Park", -2281.200000f, -839.742000f, -20.000000f, -933.339000f, -234.742000f, 350.000000f),
            new ZoneInfo("Zacti1", "Acter Industrial Park", -933.339000f, -479.583000f, -20.000000f, -872.306000f, -261.129000f, 350.000000f),
            new ZoneInfo("Zald2", "Alderney City", -1481.630000f, 882.454000f, -20.000000f, -745.720000f, 1147.540000f, 350.000000f),
            new ZoneInfo("Zald4", "Alderney City", -1380.250000f, 641.471000f, -20.000000f, -745.720000f, 882.454000f, 350.000000f),
            new ZoneInfo("Zald1", "Alderney City", -1659.550000f, 882.454000f, -20.000000f, -1481.630000f, 1029.800000f, 350.000000f),
            new ZoneInfo("Zald3", "Alderney City", -1648.230000f, 1029.800000f, -20.000000f, -1481.630000f, 1147.540000f, 350.000000f),
            new ZoneInfo("ZPENN1", "Alderney State Correctional Facility", -1145.720000f, -437.839000f, -20.000000f, -939.863000f, -319.704000f, 250.000000f),
            new ZoneInfo("ZPENN2", "Alderney State Correctional Facility", -1184.940000f, -500.289000f, -20.000000f, -1026.250000f, -437.883000f, 250.000000f),
            new ZoneInfo("ZPENN3", "Alderney State Correctional Facility", -1026.250000f, -460.589000f, -20.000000f, -933.339000f, -437.883000f, 250.000000f),
            new ZoneInfo("ZPENN5", "Alderney State Correctional Facility", -933.339000f, -460.489000f, -20.000000f, -896.439000f, -348.449000f, 250.000000f),
            new ZoneInfo("ZPENN4", "Alderney State Correctional Facility", -939.863000f, -437.839000f, -20.000000f, -933.339000f, -348.449000f, 250.000000f),
            new ZoneInfo("ZBRI2", "Algonquin Bridge", -31.314600f, 217.798000f, 30.000000f, 809.208000f, 307.377000f, 80.000000f),
            new ZoneInfo("ZBRI2", "Algonquin Bridge", 267.099000f, 218.067000f, -20.000000f, 345.646000f, 306.367000f, 30.000000f),
            new ZoneInfo("ZBRI2", "Algonquin Bridge", 660.374000f, 226.353000f, -20.000000f, 720.266000f, 321.351000f, 30.000000f),
            new ZoneInfo("ZBRI2", "Algonquin Bridge", 579.713000f, 214.313000f, -20.000000f, 660.374000f, 311.629000f, 30.000000f),
            new ZoneInfo("ZBRI2", "Algonquin Bridge", 809.208000f, 217.798000f, 30.000000f, 826.520000f, 307.377000f, 80.000000f),
            new ZoneInfo("ZBOAB3", "BOABO", 660.374000f, 101.967000f, -20.000000f, 720.266000f, 226.353000f, 30.000000f),
            new ZoneInfo("ZBOAB2", "BOABO", 720.266000f, 33.934100f, -20.000000f, 826.520000f, 321.351000f, 30.000000f),
            new ZoneInfo("ZBOAB1", "BOABO", 720.266000f, 321.351000f, -20.000000f, 1050.760000f, 539.984000f, 350.000000f),
            new ZoneInfo("ZBOAB4", "BOABO", 826.506000f, 101.246000f, -20.000000f, 851.424000f, 172.239000f, 28.700000f),
            new ZoneInfo("ZBOAB5", "BOABO", 851.424000f, 143.866000f, -20.000000f, 868.301000f, 172.239000f, 25.500000f),
            new ZoneInfo("ZBOAB6", "BOABO", 826.520000f, 172.239000f, -20.000000f, 875.301000f, 321.351000f, 20.000000f),
            new ZoneInfo("ZBEG1", "Beachgate", 1224.180000f, -713.653000f, -20.000000f, 1445.280000f, -580.431000f, 350.000000f),
            new ZoneInfo("ZBEG2", "Beachgate", 1224.180000f, -924.020000f, -20.000000f, 1496.330000f, -713.653000f, 350.000000f),
            new ZoneInfo("ZBECCT1", "Beechwood City", 1409.970000f, -12.752100f, -20.000000f, 1519.080000f, 273.548000f, 350.000000f),
            new ZoneInfo("ZBECCT2", "Beechwood City", 1519.080000f, -130.564000f, -20.000000f, 1627.590000f, 287.148000f, 350.000000f),
            new ZoneInfo("ZBECCT3", "Beechwood City", 1627.590000f, -82.852200f, -20.000000f, 1753.830000f, 287.148000f, 350.000000f),
            new ZoneInfo("ZBECCT5", "Beechwood City", 1519.080000f, 287.148000f, -20.000000f, 1873.700000f, 345.809000f, 350.000000f),
            new ZoneInfo("ZBECCT4", "Beechwood City", 1753.830000f, 137.321000f, -20.000000f, 1873.700000f, 287.148000f, 350.000000f),
            new ZoneInfo("Zberc", "Berchem", -2023.890000f, 641.471000f, -20.000000f, -1380.250000f, 882.454000f, 350.000000f),
            new ZoneInfo("Zberc1", "Berchem", -2023.890000f, 434.333000f, -20.000000f, -1427.830000f, 641.471000f, 350.000000f),
            new ZoneInfo("ZBOO1", "Booth Tunnel", -689.991000f, 304.602000f, -50.000000f, -473.805000f, 361.597000f, -20.000000f),
            new ZoneInfo("ZBOO2", "Booth Tunnel", -665.589000f, 361.597000f, -50.000000f, -575.964000f, 438.883000f, -20.000000f),
            new ZoneInfo("ZBOO3", "Booth Tunnel", -661.130000f, 438.883000f, -50.000000f, -604.330000f, 509.597000f, -20.000000f),
            new ZoneInfo("ZBOO4", "Booth Tunnel", -643.152000f, 509.597000f, -50.000000f, -604.330000f, 609.361000f, -20.000000f),
            new ZoneInfo("ZBOO5", "Booth Tunnel", -661.130000f, 509.597000f, -50.000000f, -643.152000f, 609.361000f, -20.000000f),
            new ZoneInfo("ZBOO6", "Booth Tunnel", -679.219000f, 609.361000f, -50.000000f, -643.152000f, 745.696000f, -20.000000f),
            new ZoneInfo("ZBOO7", "Booth Tunnel", -643.152000f, 609.361000f, -50.000000f, -604.330000f, 745.696000f, -20.000000f),
            new ZoneInfo("ZBOO8", "Booth Tunnel", -679.219000f, 745.696000f, -50.000000f, -615.633000f, 807.647000f, -20.000000f),
            new ZoneInfo("ZBOO9", "Booth Tunnel", -803.662000f, 754.700000f, -50.000000f, -745.720000f, 802.420000f, -20.000000f),
            new ZoneInfo("ZBOO10", "Booth Tunnel", -745.720000f, 746.353000f, -50.000000f, -679.219000f, 811.101000f, -20.000000f),
            new ZoneInfo("ZBOO11", "Booth Tunnel", -689.991000f, 304.602000f, -20.000000f, -473.805000f, 361.597000f, 3.000000f),
            new ZoneInfo("ZBOO12", "Booth Tunnel", -665.589000f, 361.597000f, -20.000000f, -575.964000f, 438.883000f, 3.400000f),
            new ZoneInfo("ZBOO13", "Booth Tunnel", -661.130000f, 438.883000f, -20.000000f, -604.330000f, 509.597000f, 3.499990f),
            new ZoneInfo("ZBOO14", "Booth Tunnel", -643.152000f, 509.597000f, -20.000000f, -604.330000f, 609.361000f, 3.100000f),
            new ZoneInfo("ZBOO15", "Booth Tunnel", -661.130000f, 509.597000f, -20.000000f, -643.152000f, 609.361000f, -6.600010f),
            new ZoneInfo("ZBOO16", "Booth Tunnel", -679.219000f, 609.361000f, -20.000000f, -643.152000f, 745.696000f, -6.600020f),
            new ZoneInfo("ZBOO17", "Booth Tunnel", -643.152000f, 609.361000f, -20.000000f, -604.330000f, 745.696000f, 3.400000f),
            new ZoneInfo("ZBOO18", "Booth Tunnel", -679.219000f, 745.696000f, -20.000000f, -615.633000f, 807.647000f, -4.800030f),
            new ZoneInfo("ZBOO19", "Booth Tunnel", -803.662000f, 754.700000f, -20.000000f, -745.720000f, 802.420000f, -4.300010f),
            new ZoneInfo("ZBOO20", "Booth Tunnel", -745.720000f, 746.353000f, -20.000000f, -679.219000f, 811.101000f, -6.600020f),
            new ZoneInfo("ZBOO21", "Booth Tunnel", -861.604000f, 754.700000f, -20.000000f, -803.662000f, 802.420000f, -2.900000f),
            new ZoneInfo("ZBOO22", "Booth Tunnel", -861.604000f, 754.700000f, -50.000000f, -803.662000f, 802.420000f, -20.000000f),
            new ZoneInfo("ZBOO23", "Booth Tunnel", -919.546000f, 754.700000f, -20.000000f, -861.604000f, 802.420000f, -0.100002f),
            new ZoneInfo("ZBOO24", "Booth Tunnel", -919.546000f, 754.700000f, -50.000000f, -861.604000f, 802.420000f, -20.000000f),
            new ZoneInfo("ZBOULE2", "Boulevard", 220.618000f, 1818.990000f, -20.000000f, 781.360000f, 2206.990000f, 350.000000f),
            new ZoneInfo("ZBOULE3", "Boulevard", 515.618000f, 1743.380000f, -20.000000f, 781.360000f, 1818.990000f, 350.000000f),
            new ZoneInfo("ZBOULE4", "Boulevard", 781.360000f, 1743.380000f, -20.000000f, 863.135000f, 2206.990000f, 350.000000f),
            new ZoneInfo("ZBOULE1", "Boulevard", 192.618000f, 1743.380000f, -20.000000f, 515.618000f, 1818.990000f, 350.000000f),
            new ZoneInfo("ZBRI1", "Broker Bridge", 401.667000f, -443.017000f, 30.000000f, 906.080000f, -368.603000f, 52.973800f),
            new ZoneInfo("ZBRI1", "Broker Bridge", 567.091000f, -441.118000f, -20.000000f, 650.385000f, -368.603000f, 30.000000f),
            new ZoneInfo("ZBRI1", "Broker Bridge", 401.667000f, -425.325000f, -20.000000f, 490.904000f, -390.211000f, 30.000000f),
            new ZoneInfo("ZBRI1", "Broker Bridge", 490.904000f, -441.118000f, -20.000000f, 567.091000f, -407.903000f, 30.000000f),
            new ZoneInfo("Zcgci2", "Castle Garden City", -659.208000f, -481.390000f, -20.000000f, -304.118000f, -346.624000f, 350.000000f),
            new ZoneInfo("Zcgci1", "Castle Garden City", -475.440000f, -715.671000f, -20.000000f, -304.118000f, -481.390000f, 350.000000f),
            new ZoneInfo("Zcgci4", "Castle Garden City", -691.208000f, -346.624000f, -20.000000f, -483.208000f, -139.945000f, 350.000000f),
            new ZoneInfo("Zcgci3", "Castle Garden City", -773.867000f, -285.868000f, -20.000000f, -691.208000f, -139.945000f, 250.000000f),
            new ZoneInfo("Zcgar3", "Castle Gardens", 123.786000f, -773.537000f, -20.000000f, 304.217000f, -690.606000f, 350.000000f),
            new ZoneInfo("Zcgar4", "Castle Gardens", -304.118000f, -912.537000f, -20.000000f, 304.217000f, -773.537000f, 350.000000f),
            new ZoneInfo("Zcgar2", "Castle Gardens", -60.915200f, -1023.730000f, -20.000000f, 304.217000f, -912.537000f, 350.000000f),
            new ZoneInfo("Zcgar1", "Castle Gardens", -304.118000f, -1065.730000f, -20.000000f, -60.915200f, -912.537000f, 350.000000f),
            new ZoneInfo("ZCERV1", "Cerveza Heights", 1050.760000f, 321.351000f, -20.000000f, 1355.350000f, 539.984000f, 350.000000f),
            new ZoneInfo("ZCERV2", "Cerveza Heights", 1355.350000f, 321.351000f, -20.000000f, 1519.080000f, 466.202000f, 350.000000f),
            new ZoneInfo("ZCERV4", "Cerveza Heights", 1519.080000f, 345.809000f, -20.000000f, 1558.800000f, 466.202000f, 350.000000f),
            new ZoneInfo("ZCERV3", "Cerveza Heights", 1409.970000f, 273.548000f, -20.000000f, 1519.080000f, 321.351000f, 350.000000f),
            new ZoneInfo("Zchisl1", "Charge Island", 387.346000f, 823.624000f, -20.000000f, 723.390000f, 1140.180000f, 350.000000f),
            new ZoneInfo("Zchisl2", "Charge Island", 438.349000f, 578.351000f, -20.000000f, 706.969000f, 823.624000f, 350.000000f),
            new ZoneInfo("ZCHASE2", "Chase Point", 701.523000f, 1188.170000f, -20.000000f, 863.135000f, 1516.680000f, 350.000000f),
            new ZoneInfo("ZCHASE1", "Chase Point", 863.135000f, 1198.390000f, -20.000000f, 913.380000f, 1338.760000f, 350.000000f),
            new ZoneInfo("Zchin", "Chinatown", -76.214300f, -505.423000f, -20.000000f, 212.278000f, -298.546000f, 350.000000f),
            new ZoneInfo("ZCity1", "City Hall", -254.508000f, -346.624000f, -20.000000f, -76.214300f, -321.624000f, 350.000000f),
            new ZoneInfo("ZCity2", "City Hall", -304.118000f, -602.863000f, -20.000000f, -76.214300f, -346.624000f, 350.000000f),
            new ZoneInfo("Zcois1", "Colony Island", 490.904000f, -407.903000f, -20.000000f, 567.091000f, -368.603000f, 30.000000f),
            new ZoneInfo("Zcois4", "Colony Island", 348.619000f, 34.595300f, -20.000000f, 548.856000f, 148.235000f, 350.000000f),
            new ZoneInfo("Zcois5", "Colony Island", 345.646000f, 148.235000f, -20.000000f, 579.713000f, 480.058000f, 30.000000f),
            new ZoneInfo("Zcois3", "Colony Island", 364.765000f, -36.926000f, -20.000000f, 566.461000f, 34.595300f, 350.000000f),
            new ZoneInfo("Zcois8", "Colony Island", 406.158000f, -133.745000f, -20.000000f, 570.314000f, -36.926000f, 350.000000f),
            new ZoneInfo("ZDOWNT", "Downtown", 954.063000f, -12.752200f, -20.000000f, 1119.970000f, 321.351000f, 350.000000f),
            new ZoneInfo("ZBRI6", "Dukes Bay Bridge", 1402.810000f, 1115.060000f, -20.000000f, 1426.680000f, 1338.760000f, 22.000000f),
            new ZoneInfo("ZBRI6", "Dukes Bay Bridge", 1402.810000f, 1338.760000f, -1.312100f, 1426.680000f, 1552.760000f, 22.187900f),
            new ZoneInfo("ZBRI3", "East Borough Bridge", 616.353000f, 599.436000f, 4.274860f, 706.969000f, 649.391000f, 40.431800f),
            new ZoneInfo("ZBRI3", "East Borough Bridge", 706.969000f, 599.436000f, 20.000000f, 795.605000f, 649.391000f, 30.000000f),
            new ZoneInfo("ZBRI4", "East Borough Bridge", 327.747000f, 988.736000f, -20.000000f, 387.346000f, 1033.940000f, 60.000000f),
            new ZoneInfo("ZBRI4", "East Borough Bridge", 387.346000f, 988.736000f, 10.000000f, 446.945000f, 1033.940000f, 60.000000f),
            new ZoneInfo("ZBRI5", "East Borough Bridge", 519.495000f, 1188.170000f, 10.000000f, 580.557000f, 1285.970000f, 45.000000f),
            new ZoneInfo("ZBRI5", "East Borough Bridge", 519.495000f, 1140.180000f, -20.000000f, 580.557000f, 1188.170000f, 45.000000f),
            new ZoneInfo("ZBRI3", "East Borough Bridge", 706.969000f, 599.436000f, -20.000000f, 795.605000f, 649.391000f, 80.000000f),
            new ZoneInfo("ZBRI3", "East Borough Bridge", 795.605000f, 599.436000f, 80.000000f, 974.815000f, 649.391000f, 30.000000f),
            new ZoneInfo("ZBRI4", "East Borough Bridge", 214.634000f, 988.736000f, -20.000000f, 327.747000f, 1033.940000f, 60.000000f),
            new ZoneInfo("Zehol1", "East Holland", -273.709000f, 1191.310000f, -20.000000f, 153.233000f, 1407.380000f, 350.000000f),
            new ZoneInfo("Zehol2", "East Holland", 153.233000f, 1191.310000f, -20.000000f, 224.420000f, 1298.100000f, 350.000000f),
            new ZoneInfo("ZEHOK9", "East Hook", 595.540000f, -5.146620f, -20.000000f, 720.266000f, 101.967000f, 350.000000f),
            new ZoneInfo("ZEHOK8", "East Hook", 720.266000f, -20.089800f, -20.000000f, 826.520000f, 33.934100f, 350.000000f),
            new ZoneInfo("ZEHOK6", "East Hook", 720.266000f, -49.683900f, -20.000000f, 837.984000f, -41.036600f, 350.000000f),
            new ZoneInfo("ZEHOK4", "East Hook", 720.266000f, -76.398000f, -20.000000f, 861.788000f, -68.986000f, 350.000000f),
            new ZoneInfo("ZEHOK3", "East Hook", 720.266000f, -82.420200f, -20.000000f, 882.172000f, -76.398000f, 350.000000f),
            new ZoneInfo("ZEHOK2", "East Hook", 720.266000f, -89.832200f, -20.000000f, 890.511000f, -82.420200f, 350.000000f),
            new ZoneInfo("ZEHOK7", "East Hook", 720.266000f, -41.036600f, -20.000000f, 833.377000f, -20.089800f, 350.000000f),
            new ZoneInfo("ZEHOK5", "East Hook", 720.266000f, -68.986000f, -20.000000f, 848.133000f, -49.683900f, 350.000000f),
            new ZoneInfo("ZEHOK1", "East Hook", 720.266000f, -232.360000f, -20.000000f, 954.063000f, -89.832200f, 350.000000f),
            new ZoneInfo("ZEHOK10", "East Hook", 650.385000f, -441.118000f, -20.000000f, 720.266000f, -5.146640f, 30.000000f),
            new ZoneInfo("ZESTCT2", "East Island City", 795.605000f, 539.984000f, -20.000000f, 1259.510000f, 656.075000f, 350.000000f),
            new ZoneInfo("ZESTCT9", "East Island City", 1288.540000f, 791.094000f, -20.000000f, 1355.350000f, 808.982000f, 350.000000f),
            new ZoneInfo("ZESTCT5", "East Island City", 1297.080000f, 808.982000f, -20.000000f, 1355.350000f, 837.092000f, 350.000000f),
            new ZoneInfo("ZESTCT6", "East Island City", 1308.910000f, 837.092000f, -20.000000f, 1355.350000f, 855.339000f, 350.000000f),
            new ZoneInfo("ZESTCT7", "East Island City", 1319.760000f, 855.339000f, -20.000000f, 1355.350000f, 879.011000f, 350.000000f),
            new ZoneInfo("ZESTCT8", "East Island City", 1332.090000f, 879.011000f, -20.000000f, 1355.350000f, 907.671000f, 350.000000f),
            new ZoneInfo("ZESTCT1", "East Island City", 1206.410000f, 656.075000f, -20.000000f, 1259.510000f, 776.226000f, 350.000000f),
            new ZoneInfo("ZESTCT3", "East Island City", 1259.510000f, 539.984000f, -20.000000f, 1355.350000f, 776.226000f, 350.000000f),
            new ZoneInfo("ZESTCT4", "East Island City", 1278.940000f, 776.226000f, -20.000000f, 1355.350000f, 791.094000f, 350.000000f),
            new ZoneInfo("Zeast", "Easton", -31.314600f, -139.945000f, -20.000000f, 108.875000f, 148.235000f, 350.000000f),
            new ZoneInfo("ZFRIS2", "Firefly Island", 630.349000f, -713.653000f, -20.000000f, 1445.280000f, -580.431000f, 350.000000f),
            new ZoneInfo("ZFRIS1", "Firefly Island", 630.349000f, -881.620000f, -20.000000f, 1224.180000f, -713.653000f, 350.000000f),
            new ZoneInfo("ZFIEPR2", "Firefly Projects", 1180.090000f, -472.138000f, -20.000000f, 1281.980000f, -364.609000f, 350.000000f),
            new ZoneInfo("ZFIEPR4", "Firefly Projects", 1180.090000f, -364.609000f, -20.000000f, 1445.280000f, -280.181000f, 350.000000f),
            new ZoneInfo("ZFIEPR1", "Firefly Projects", 1179.920000f, -580.431000f, -20.000000f, 1281.980000f, -472.138000f, 350.000000f),
            new ZoneInfo("ZFIEPR3", "Firefly Projects", 1281.980000f, -580.431000f, -20.000000f, 1445.280000f, -364.609000f, 350.000000f),
            new ZoneInfo("ZFIEPR5", "Firefly Projects", 1180.090000f, -280.181000f, -20.000000f, 1393.380000f, -225.707000f, 350.000000f),
            new ZoneInfo("ZFIEPR6", "Firefly Projects", 1445.280000f, -580.431000f, -20.000000f, 1563.780000f, -280.181000f, 350.000000f),
            new ZoneInfo("Zfisn", "Fishmarket North", 212.278000f, -303.904000f, -20.000000f, 394.167000f, -139.945000f, 350.000000f),
            new ZoneInfo("Zfiss1", "Fishmarket South", 212.278000f, -505.423000f, -20.000000f, 401.667000f, -303.904000f, 350.000000f),
            new ZoneInfo("Zfiss2", "Fishmarket South", 401.667000f, -505.423000f, -20.000000f, 490.904000f, -445.003000f, 350.000000f),
            new ZoneInfo("Zfiss3", "Fishmarket South", 218.680000f, -580.551000f, -20.000000f, 490.904000f, -505.423000f, 350.000000f),
            new ZoneInfo("Zfiss4", "Fishmarket South", 268.584000f, -690.606000f, -20.000000f, 490.904000f, -580.551000f, 350.000000f),
            new ZoneInfo("Zfiss5", "Fishmarket South", 304.217000f, -790.770000f, -20.000000f, 434.896000f, -690.606000f, 350.000000f),
            new ZoneInfo("ZFORT", "Fortside", 192.618000f, 1516.680000f, -20.000000f, 676.386000f, 1743.380000f, 350.000000f),
            new ZoneInfo("ZAIRPT1", "Francis International Airport", 1931.480000f, 795.065000f, -20.000000f, 2441.580000f, 1076.280000f, 350.000000f),
            new ZoneInfo("ZAIRPT2", "Francis International Airport", 1753.830000f, -209.055000f, -20.000000f, 2737.310000f, 137.321000f, 350.000000f),
            new ZoneInfo("ZAIRPT3", "Francis International Airport", 1873.700000f, 137.321000f, -20.000000f, 2879.300000f, 795.065000f, 350.000000f),
            new ZoneInfo("ZAIRU0", "Francis International Airport", 2369.270000f, 137.321000f, -20.000000f, 2647.660000f, 795.065000f, 130.000000f),
            new ZoneInfo("ZAIRU2", "Francis International Airport", 2101.530000f, 19.891300f, -20.000000f, 2435.530000f, 137.321000f, 130.000000f),
            new ZoneInfo("ZAIRU4", "Francis International Airport", 2117.520000f, 137.321000f, -20.000000f, 2138.110000f, 167.691000f, 130.000000f),
            new ZoneInfo("ZAIRU5", "Francis International Airport", 2232.380000f, 182.049000f, -20.000000f, 2243.970000f, 193.607000f, 130.000000f),
            new ZoneInfo("ZAIRU3", "Francis International Airport", 2138.110000f, 137.321000f, -20.000000f, 2369.270000f, 182.049000f, 130.000000f),
            new ZoneInfo("ZAIRU6", "Francis International Airport", 2328.070000f, 253.060000f, -20.000000f, 2369.270000f, 277.326000f, 130.000000f),
            new ZoneInfo("ZAIRU7", "Francis International Airport", 2328.070000f, 500.043000f, -20.000000f, 2369.270000f, 524.310000f, 130.000000f),
            new ZoneInfo("ZAIRU8", "Francis International Airport", 2179.550000f, 624.746000f, -20.000000f, 2369.270000f, 795.065000f, 130.000000f),
            new ZoneInfo("ZAIRU9", "Francis International Airport", 2328.070000f, 577.510000f, -20.000000f, 2369.270000f, 624.746000f, 130.000000f),
            new ZoneInfo("ZAIRU1", "Francis International Airport", 2179.550000f, 536.785000f, -20.000000f, 2187.580000f, 624.746000f, 9.000000f),
            new ZoneInfo("ZHap1", "Happiness Island", -833.596000f, -779.497000f, -20.000000f, -490.549000f, -648.254000f, 350.000000f),
            new ZoneInfo("ZHap2", "Happiness Island", -813.427000f, -910.740000f, -20.000000f, -436.682000f, -779.497000f, 350.000000f),
            new ZoneInfo("ZHap3", "Happiness Island", -783.242000f, -1086.350000f, -20.000000f, -377.497000f, -910.606000f, 350.000000f),
            new ZoneInfo("Zhat", "Hatton Gardens", -31.314600f, 369.954000f, -20.000000f, 267.099000f, 609.361000f, 350.000000f),
            new ZoneInfo("ZBRI8", "Hickey Bridge", -753.262000f, 1151.040000f, 8.000000f, -738.262000f, 1183.240000f, 33.000000f),
            new ZoneInfo("ZBRI8", "Hickey Bridge", -738.262000f, 1151.040000f, -20.000000f, -702.852000f, 1183.240000f, 33.000000f),
            new ZoneInfo("ZBRI8", "Hickey Bridge", -702.852000f, 1151.040000f, 8.000000f, -663.503000f, 1183.240000f, 33.000000f),
            new ZoneInfo("ZHOVEB3", "Hove Beach", 954.063000f, -472.138000f, -20.000000f, 1180.090000f, -280.181000f, 350.000000f),
            new ZoneInfo("ZHOVEB2", "Hove Beach", 720.266000f, -472.138000f, -20.000000f, 954.063000f, -232.360000f, 30.000000f),
            new ZoneInfo("ZHOVEB1", "Hove Beach", 720.266000f, -580.431000f, -20.000000f, 1179.920000f, -472.138000f, 350.000000f),
            new ZoneInfo("ZINDUS5", "Industrial", 1443.640000f, 1338.760000f, -20.000000f, 1483.640000f, 1511.740000f, 350.000000f),
            new ZoneInfo("ZINDUS3", "Industrial", 863.135000f, 1338.760000f, -20.000000f, 1443.640000f, 1661.550000f, 350.000000f),
            new ZoneInfo("ZINDUS4", "Industrial", 676.386000f, 1516.680000f, -20.000000f, 863.135000f, 1743.380000f, 350.000000f),
            new ZoneInfo("ZINDUS6", "Industrial", 863.135000f, 1661.550000f, -20.000000f, 1060.820000f, 1762.250000f, 350.000000f),
            new ZoneInfo("ZINDUS2", "Industrial", 1060.820000f, 1661.550000f, -20.000000f, 1099.920000f, 1719.240000f, 350.000000f),
            new ZoneInfo("ZINDUS1", "Industrial", 942.316000f, 1762.250000f, -20.000000f, 1025.440000f, 1848.460000f, 350.000000f),
            new ZoneInfo("Zlanc1", "Lancaster", -31.314600f, 906.696000f, -20.000000f, 214.634000f, 1075.610000f, 350.000000f),
            new ZoneInfo("Zlanc2", "Lancaster", -31.314600f, 1075.610000f, -20.000000f, 233.733000f, 1191.310000f, 350.000000f),
            new ZoneInfo("Zlance", "Lancet", -31.314600f, 148.235000f, -20.000000f, 267.099000f, 369.954000f, 30.000000f),
            new ZoneInfo("ZLEAP", "Leaper's Bridge", 343.176000f, -66.726300f, -20.000000f, 406.158000f, -36.926000f, 30.000000f),
            new ZoneInfo("Zleft1", "Leftwood", -1454.470000f, 1147.540000f, -20.000000f, -738.262000f, 1417.700000f, 350.000000f),
            new ZoneInfo("Zleft2", "Leftwood", -1601.430000f, 1147.540000f, -20.000000f, -1454.470000f, 1417.700000f, 350.000000f),
            new ZoneInfo("ZLBAY2", "Little Bay", 1443.640000f, 1511.740000f, -20.000000f, 1644.070000f, 1887.140000f, 350.000000f),
            new ZoneInfo("ZLBAY1", "Little Bay", 1443.640000f, 1887.140000f, -20.000000f, 1549.290000f, 1980.900000f, 350.000000f),
            new ZoneInfo("ZLBAY3", "Little Bay", 1483.640000f, 1467.220000f, -20.000000f, 1601.910000f, 1511.740000f, 350.000000f),
            new ZoneInfo("ZLBAY4", "Little Bay", 1386.120000f, 1661.550000f, -20.566600f, 1443.640000f, 1980.900000f, 349.433000f),
            new ZoneInfo("ZLBAY5", "Little Bay", 1264.120000f, 1661.550000f, -20.000000f, 1386.120000f, 1848.460000f, 350.000000f),
            new ZoneInfo("Zital", "Little Italy", -254.508000f, -321.624000f, -20.000000f, -76.214200f, -222.048000f, 350.000000f),
            new ZoneInfo("Zlowe1", "Lower Easton", -76.214200f, -298.546000f, -20.000000f, 212.278000f, -222.048000f, 350.000000f),
            new ZoneInfo("Zlowe2", "Lower Easton", -31.314600f, -222.048000f, -20.000000f, 212.278000f, -139.945000f, 350.000000f),
            new ZoneInfo("ZMHILLS", "Meadow Hills", 1558.800000f, 345.809000f, -20.000000f, 1659.900000f, 795.065000f, 350.000000f),
            new ZoneInfo("ZMPARK4", "Meadows Park", 1355.350000f, 795.065000f, -20.000000f, 1713.810000f, 1014.760000f, 350.000000f),
            new ZoneInfo("ZMPARK1", "Meadows Park", 1355.350000f, 466.202000f, -20.000000f, 1558.800000f, 795.065000f, 350.000000f),
            new ZoneInfo("ZMPARK2", "Meadows Park", 1713.810000f, 795.065000f, -20.000000f, 1931.480000f, 1170.580000f, 350.000000f),
            new ZoneInfo("ZMPARK3", "Meadows Park", 1452.010000f, 1014.760000f, -20.000000f, 1713.810000f, 1210.980000f, 350.000000f),
            new ZoneInfo("Zmidpa", "Middle Park", -381.711000f, 609.361000f, -20.000000f, -31.314600f, 1191.310000f, 350.000000f),
            new ZoneInfo("Zmide", "Middle Park East", -31.314600f, 609.361000f, -20.000000f, 211.533000f, 906.696000f, 350.000000f),
            new ZoneInfo("Zmdw1", "Middle Park West", -643.152000f, 609.361000f, -20.000000f, -381.711000f, 745.696000f, 350.000000f),
            new ZoneInfo("Zmdw2", "Middle Park West", -679.219000f, 745.696000f, -20.000000f, -381.711000f, 837.076000f, 350.000000f),
            new ZoneInfo("Zmdw3", "Middle Park West", -702.852000f, 837.076000f, -20.000000f, -381.711000f, 906.696000f, 350.000000f),
            new ZoneInfo("Znorm", "Normandy", -1313.850000f, 374.040000f, -20.000000f, -855.416000f, 641.471000f, 350.000000f),
            new ZoneInfo("Znhol1", "North Holland", -690.252000f, 1191.310000f, -20.000000f, -273.709000f, 1305.980000f, 350.000000f),
            new ZoneInfo("Znhol2", "North Holland", -634.125000f, 1305.980000f, -20.000000f, -273.709000f, 1407.380000f, 350.000000f),
            new ZoneInfo("ZNRDNS2", "Northern Gardens", 942.316000f, 1848.460000f, -20.000000f, 1264.120000f, 2193.110000f, 350.000000f),
            new ZoneInfo("ZNRDNS5", "Northern Gardens", 1025.440000f, 1762.250000f, -20.000000f, 1264.120000f, 1848.460000f, 350.000000f),
            new ZoneInfo("ZNRDNS6", "Northern Gardens", 1060.820000f, 1719.240000f, -20.000000f, 1264.120000f, 1762.250000f, 350.000000f),
            new ZoneInfo("ZNRDNS7", "Northern Gardens", 1099.920000f, 1661.550000f, -20.000000f, 1264.120000f, 1719.240000f, 350.000000f),
            new ZoneInfo("ZNRDNS3", "Northern Gardens", 1264.120000f, 1848.460000f, -20.000000f, 1386.120000f, 2193.110000f, 350.000000f),
            new ZoneInfo("ZNRDNS4", "Northern Gardens", 1386.120000f, 1980.900000f, -20.000000f, 1443.640000f, 2154.310000f, 350.000000f),
            new ZoneInfo("ZNRDNS1", "Northern Gardens", 863.135000f, 1762.250000f, -20.000000f, 942.316000f, 2206.990000f, 350.000000f),
            new ZoneInfo("Znort1", "Northwood", -606.125000f, 1407.380000f, -20.000000f, 58.918900f, 1610.150000f, 350.000000f),
            new ZoneInfo("Znort2", "Northwood", -510.167000f, 1610.150000f, -20.000000f, -165.606000f, 1870.540000f, 350.000000f),
            new ZoneInfo("Znort3", "Northwood", -165.606000f, 1610.150000f, -20.000000f, 9.726260f, 1754.750000f, 350.000000f),
            new ZoneInfo("Znort4", "Northwood", -165.606000f, 1754.750000f, -20.000000f, -45.793800f, 1810.740000f, 250.000000f),
            new ZoneInfo("Znort5", "Northwood", -578.528000f, 1610.150000f, -20.000000f, -510.167000f, 1817.150000f, 350.000000f),
            new ZoneInfo("ZBRI7", "Northwood Heights Bridge", 9.726260f, 1626.810000f, -20.000000f, 88.182700f, 1721.810000f, 60.000000f),
            new ZoneInfo("ZBRI7", "Northwood Heights Bridge", 88.182700f, 1670.140000f, -20.000000f, 166.639000f, 1765.140000f, 60.000000f),
            new ZoneInfo("ZBRI7", "Northwood Heights Bridge", 166.639000f, 1718.710000f, -20.000000f, 192.618000f, 1798.710000f, 60.000000f),
            new ZoneInfo("ZOUTLO", "Outlook", 954.063000f, -280.181000f, -20.000000f, 1139.430000f, -12.752200f, 350.000000f),
            new ZoneInfo("Zport", "Port Tudor", -1383.780000f, 203.392000f, -20.000000f, -897.531000f, 374.040000f, 350.000000f),
            new ZoneInfo("Zport1", "Port Tudor", -1294.570000f, -234.742000f, -20.000000f, -933.339000f, 203.392000f, 350.000000f),
            new ZoneInfo("Zpres", "Presidents City", 108.875000f, -139.945000f, -20.000000f, 343.176000f, 148.235000f, 350.000000f),
            new ZoneInfo("Zpurg1", "Purgatory", -643.152000f, 509.597000f, -20.000000f, -381.711000f, 609.361000f, 350.000000f),
            new ZoneInfo("Zpurg2", "Purgatory", -778.416000f, 438.883000f, -20.000000f, -381.711000f, 509.597000f, 350.000000f),
            new ZoneInfo("Zpurg3", "Purgatory", -751.416000f, 361.597000f, -20.000000f, -381.724000f, 438.883000f, 350.000000f),
            new ZoneInfo("ZRHIL3", "Rotterdam Hill", 826.520000f, -20.089800f, -20.000000f, 899.995000f, 33.934100f, 350.000000f),
            new ZoneInfo("ZRHIL7", "Rotterdam Hill", 861.788000f, -76.398000f, -20.000000f, 899.995000f, -68.986000f, 350.000000f),
            new ZoneInfo("ZRHIL8", "Rotterdam Hill", 882.172000f, -82.420200f, -20.000000f, 899.995000f, -76.398000f, 350.000000f),
            new ZoneInfo("ZRHIL9", "Rotterdam Hill", 890.511000f, -89.832200f, -20.000000f, 899.995000f, -82.420200f, 350.000000f),
            new ZoneInfo("ZRHIL5", "Rotterdam Hill", 837.984000f, -49.683900f, -20.000000f, 899.995000f, -41.036600f, 350.000000f),
            new ZoneInfo("ZRHIL4", "Rotterdam Hill", 833.377000f, -41.036600f, -20.000000f, 899.995000f, -20.089800f, 350.000000f),
            new ZoneInfo("ZRHIL6", "Rotterdam Hill", 848.133000f, -68.986000f, -20.000000f, 899.995000f, -49.683900f, 350.000000f),
            new ZoneInfo("ZRHIL2", "Rotterdam Hill", 826.520000f, 33.934100f, -20.000000f, 954.063000f, 321.351000f, 350.000000f),
            new ZoneInfo("ZRHIL1", "Rotterdam Hill", 899.995000f, -89.832200f, -20.000000f, 954.063000f, 33.934100f, 350.000000f),
            new ZoneInfo("ZRHIL10", "Rotterdam Hill", 869.072000f, 321.351000f, 350.000000f, 954.063000f, 364.191000f, 7.576900f),
            new ZoneInfo("ZRHIL11", "Rotterdam Hill", 910.813000f, 364.191000f, 350.000000f, 954.063000f, 418.250000f, 12.560200f),
            new ZoneInfo("ZRHIL12", "Rotterdam Hill", 865.390000f, 364.191000f, 350.000000f, 910.813000f, 377.487000f, 15.142400f),
            new ZoneInfo("ZRHIL13", "Rotterdam Hill", 869.537000f, 377.487000f, 350.000000f, 910.813000f, 386.178000f, 38.097700f),
            new ZoneInfo("ZRHIL15", "Rotterdam Hill", 877.244000f, 386.178000f, 350.000000f, 910.813000f, 397.612000f, 12.573200f),
            new ZoneInfo("ZRHIL16", "Rotterdam Hill", 886.591000f, 397.612000f, 350.000000f, 910.813000f, 406.339000f, 37.604300f),
            new ZoneInfo("ZRHIL14", "Rotterdam Hill", 898.111000f, 406.339000f, 350.000000f, 910.813000f, 412.662000f, 12.560200f),
            new ZoneInfo("ZSHTLER", "Schottler", 1119.970000f, -12.752200f, -20.000000f, 1409.970000f, 321.351000f, 350.000000f),
            new ZoneInfo("ZSOHAN1", "South Bohan", 266.523000f, 1341.680000f, -20.000000f, 701.523000f, 1516.680000f, 350.000000f),
            new ZoneInfo("ZSOHAN2", "South Bohan", 350.911000f, 1188.170000f, -20.000000f, 701.523000f, 1341.680000f, 350.000000f),
            new ZoneInfo("ZSLOPES", "South Slopes", 1139.430000f, -280.181000f, -20.000000f, 1519.080000f, -12.752100f, 350.000000f),
            new ZoneInfo("ZSTAR", "Star Junction", -381.711000f, 148.235000f, -20.000000f, -31.314600f, 609.361000f, 350.000000f),
            new ZoneInfo("ZSTEI3", "Steinway", 795.605000f, 776.226000f, -20.000000f, 1259.510000f, 1014.760000f, 350.000000f),
            new ZoneInfo("ZSTEI2", "Steinway", 1022.310000f, 1014.760000f, -20.000000f, 1361.460000f, 1185.080000f, 350.000000f),
            new ZoneInfo("ZSTEI11", "Steinway", 1361.460000f, 1014.760000f, -20.000000f, 1452.010000f, 1115.060000f, 350.000000f),
            new ZoneInfo("ZSTEI1", "Steinway", 795.605000f, 656.075000f, -20.000000f, 1206.410000f, 776.226000f, 350.000000f),
            new ZoneInfo("ZSTEI4", "Steinway", 1259.510000f, 907.671000f, -20.000000f, 1355.350000f, 1014.760000f, 350.000000f),
            new ZoneInfo("ZSTEI5", "Steinway", 1259.510000f, 879.011000f, -20.000000f, 1332.090000f, 907.671000f, 350.000000f),
            new ZoneInfo("ZSTEI6", "Steinway", 1259.510000f, 855.339000f, -20.000000f, 1319.760000f, 879.011000f, 350.000000f),
            new ZoneInfo("ZSTEI7", "Steinway", 1259.510000f, 837.092000f, -20.000000f, 1308.910000f, 855.339000f, 350.000000f),
            new ZoneInfo("ZSTEI8", "Steinway", 1259.510000f, 808.982000f, -20.000000f, 1297.080000f, 837.092000f, 350.000000f),
            new ZoneInfo("ZSTEI9", "Steinway", 1259.510000f, 791.094000f, -20.000000f, 1288.540000f, 808.982000f, 350.000000f),
            new ZoneInfo("ZSTEI10", "Steinway", 1259.510000f, 776.226000f, -20.000000f, 1278.940000f, 791.094000f, 350.000000f),
            new ZoneInfo("Zsuff1", "Suffolk", -483.208000f, -222.048000f, -20.000000f, -31.314600f, -139.945000f, 350.000000f),
            new ZoneInfo("Zsuff2", "Suffolk", -483.208000f, -346.624000f, -20.000000f, -254.508000f, -222.048000f, 350.000000f),
            new ZoneInfo("Zexc1", "The Exchange", -304.118000f, -773.537000f, -20.000000f, -76.214300f, -602.863000f, 350.000000f),
            new ZoneInfo("Zexc2", "The Exchange", -76.214300f, -580.551000f, -20.000000f, 218.680000f, -505.423000f, 350.000000f),
            new ZoneInfo("Zexc3", "The Exchange", -76.214300f, -690.606000f, -20.000000f, 268.584000f, -580.551000f, 350.000000f),
            new ZoneInfo("Zexc4", "The Exchange", -76.214300f, -773.537000f, -20.000000f, 123.786000f, -690.606000f, 350.000000f),
            new ZoneInfo("Zmeat", "The Meat Quarter", -883.699000f, -139.945000f, -20.000000f, -381.724000f, 148.235000f, 350.000000f),
            new ZoneInfo("Ztri", "The Triangle", -381.724000f, -139.945000f, -20.000000f, -31.314600f, 148.235000f, 350.000000f),
            new ZoneInfo("Ztudo2", "Tudor", -2281.200000f, -234.742000f, -20.000000f, -2023.890000f, 289.462000f, 350.000000f),
            new ZoneInfo("Ztudo1", "Tudor", -2023.890000f, -234.742000f, -20.000000f, -1294.570000f, 203.392000f, 350.000000f),
            new ZoneInfo("Zvarh", "Varsity Heights", -702.852000f, 906.696000f, -20.000000f, -381.711000f, 1191.310000f, 350.000000f),
            new ZoneInfo("Zwest2", "Westdyke", -1081.630000f, 1954.730000f, -20.000000f, -849.897000f, 2019.950000f, 350.000000f),
            new ZoneInfo("Zwest1", "Westdyke", -1563.420000f, 1417.700000f, -20.000000f, -711.262000f, 1954.730000f, 350.000000f),
            new ZoneInfo("Zwestm", "Westminster", -796.099000f, 148.235000f, -20.000000f, -381.724000f, 361.597000f, 346.900000f),
            new ZoneInfo("ZWILIS1", "Willis", 1659.900000f, 345.809000f, -20.000000f, 1873.700000f, 795.065000f, 350.000000f),
        };

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

            string model = "";
            int speedMph = -1;
            long nowMs = Environment.TickCount;
            int carPtr = GameData.ReadIntAbs(GameData.Base() + (long)cfg.VehiclePtrOffset);
            bool rot = (carPtr > 0x10000 && carPtr < 0x7E000000);
            if (rot)
            {
                int entry = GameData.ReadIntAbs(carPtr + (long)cfg.VehicleModelOffset);
                    if (entry > 0x10000 && entry < 0x7E000000)
                        model = ModelDisplay(GameData.ReadString(entry + (long)cfg.VehicleModelNameOffset, cfg.VehicleNameLen));
                    float px = GameData.ReadFloatAbs(carPtr + (long)cfg.VehiclePosOffset);
                    float py = GameData.ReadFloatAbs(carPtr + (long)cfg.VehiclePosOffset + 4);
                    float pz = GameData.ReadFloatAbs(carPtr + (long)cfg.VehiclePosOffset + 8);
                    if (!float.IsNaN(px) && !float.IsNaN(py) && !float.IsNaN(pz) && px != 0)
                    {
                        double dx = px - lastPx, dy = py - lastPy, dz = pz - lastPz;
                        long dtMs = nowMs - lastPosMs;
                        if (lastPosMs > 0 && dtMs > 300 && dtMs < 20000)
                        {
                            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            double mps = dist / (dtMs / 1000.0);
                            double mph = mps * 2.23694;
                            if (mph < 1) mph = 0;
                            speedMph = (int)Math.Round(mph);
                        }
                        lastPx = px; lastPy = py; lastPz = pz; lastPosMs = nowMs;
                    }
            }
            else
            {
                lastPosMs = 0;
            }

            bool changed = (want != lastWanted);
            float lastH = lastHealth;
            bool healthChanged = !(float.IsNaN(health) && float.IsNaN(lastH)) &&
                                 (float.IsNaN(health) || float.IsNaN(lastH) || Math.Abs((double)(health - lastH)) > 0.3);
            float lastM = lastMoney;
            bool moneyChanged = !(float.IsNaN(money) && float.IsNaN(lastM)) &&
                                (float.IsNaN(money) || float.IsNaN(lastM) || Math.Abs((double)(money - lastM)) > 0.01);
            bool missionChanged = (mission != lastMission);
            bool vehicleChanged = (vehicle != lastVehicle);
            bool modelChanged = (model != lastModel);
            bool speedChanged = (speedMph != lastSpeedMph);
            string zoneName = cfg.ZoneEnabled ? PlayerZone() : "";
            bool zoneChanged = (zoneName != lastZone);
            string missionTitle = MissionTitle();
            bool titleChanged = (missionTitle != lastMissionTitle);
            string weapon = CurrentWeapon();
            bool weaponChanged = (weapon != lastWeapon);
            if (!changed && !healthChanged && !moneyChanged && !missionChanged && !vehicleChanged && !modelChanged && !speedChanged && !zoneChanged && !titleChanged && !weaponChanged) return;
            lastWanted = want;
            lastHealth = health;
            lastMoney = money;
            lastMission = mission;
            lastVehicle = vehicle;
            lastModel = model;
            lastSpeedMph = speedMph;
            lastZone = zoneName;
            lastMissionTitle = missionTitle;
            lastWeapon = weapon;

            StringBuilder parts = new StringBuilder();
            string character = CurrentCharacter(missionTitle);
            if (mission >= 0 && cfg.MissionEnabled)
            {
                string named = MissionName(missionTitle);
                string line = (named.Length > 0 && mission > 0) ? named :
                              (mission == 4 || mission > 2) ? cfg.MissionActiveText :
                              (cfg.MissionFreeText.Length > 0 ? cfg.MissionFreeText : "");
                if (parts.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    parts.Append(cfg.JoinSeparator);
                parts.Append(line);
            }
            if (cfg.CharacterEnabled && character.Length > 0)
            {
                string cline = cfg.CharacterText.Replace("{character}", character);
                if (cline.Length > 0)
                {
                    if (parts.Length > 0 && cfg.JoinSeparator.Length > 0) parts.Append(cfg.JoinSeparator);
                    parts.Append(cline);
                }
            }
            if (weapon.Length > 0 && cfg.WeaponEnabled)
            {
                if (parts.Length > 0 && cfg.JoinSeparator.Length > 0) parts.Append(cfg.JoinSeparator);
                parts.Append(weapon);
            }
            string vline = "";
            if (rot && model.Length > 0)
                vline = cfg.VehicleText.Replace("{model}", model).Replace("{speed}", (speedMph < 0 ? "? " : speedMph.ToString()));
            else if (rot)
                vline = cfg.VehicleInText;
            else
                vline = (cfg.VehicleFootText.Length > 0 ? cfg.VehicleFootText : "");
            if (vline.Length > 0)
            {
                if (parts.Length > 0 && cfg.JoinSeparator.Length > 0) parts.Append(cfg.JoinSeparator);
                parts.Append(vline);
            }
            string zoneText = (zoneName.Length > 0 && cfg.ZoneText.Length > 0)
                ? cfg.ZoneText.Replace("{zone}", zoneName) : "";
            string situ = parts.ToString();
            if (zoneText.Length > 0)
            {
                if (situ.Length == 0) situ = zoneText;
                else situ = zoneText + (cfg.JoinSeparator.Length > 0 ? cfg.JoinSeparator : "") + situ;
            }
            StringBuilder stats = new StringBuilder();
            if (want > 0 && cfg.WantedText.Length > 0)
            {
                string wl = cfg.WantedText.Replace("{n}", want.ToString()).Replace("{s}", want > 1 ? "s" : "");
                if (stats.Length > 0 && wl.Length > 0 && cfg.JoinSeparator.Length > 0)
                    stats.Append(cfg.JoinSeparator);
                stats.Append(wl);
            }
            else if (cfg.WantedZeroText.Length > 0)
                stats.Append(cfg.WantedZeroText);
            if (!float.IsNaN(health) && health >= 0 && health < 400)
            {
                int hp = (int)Math.Round(health);
                string line = cfg.HealthText.Replace("{h}", hp.ToString());
                if (stats.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    stats.Append(cfg.JoinSeparator);
                stats.Append(line);
            }
            if (!float.IsNaN(money) && money >= 0 && money < 1e9f)
            {
                int mc = (int)Math.Round(money);
                string line = cfg.MoneyText.Replace("{m}", mc.ToString());
                if (stats.Length > 0 && line.Length > 0 && cfg.JoinSeparator.Length > 0)
                    stats.Append(cfg.JoinSeparator);
                stats.Append(line);
            }
            string st = stats.ToString();
            if (st.Length > 0)
            {
                if (situ.Length == 0) situ = st;
                else situ = situ + (cfg.JoinSeparator.Length > 0 ? cfg.JoinSeparator : "") + st;
            }
            if (situ.Length == 0) situ = cfg.Details;
            cfg.Details = situ;
            cfg.State = cfg.FixedState; 
            SetPresence(startSec);
            Console.WriteLine("[OK] " + DateTime.Now.ToString("HH:mm:ss") + " Wanted=" + want +
                              " Health=" + (float.IsNaN(health) ? "n/a" : health.ToString("0.0")) +
                              " Money=" + (float.IsNaN(money) ? "n/a" : money.ToString("0")) +
                              " Mission=" + mission + " Vehicle=" + vehicle + " Model=" + model +
                              " Speed=" + (speedMph < 0 ? "n/a" : speedMph.ToString()) + " mph" +
                              "  ->  Details=" + situ + " | State=" + cfg.State);
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

        static string PlayerZone()
        {
            if (!cfg.ZoneEnabled || GameData.Base() == 0) return "";
            float px = GameData.ReadFloat(cfg.PlayerPosOffset);
            float py = GameData.ReadFloat(cfg.PlayerPosOffset + 4);
            float pz = GameData.ReadFloat(cfg.PlayerPosOffset + 8);
            if (float.IsNaN(px) || px == 0) return "";
            for (int i = 0; i < Zones.Length; i++)
            {
                ZoneInfo z = Zones[i];
                if (px >= z.X1 && px <= z.X2 && py >= z.Y1 && py <= z.Y2 && pz >= z.Z1 && pz <= z.Z2)
                    return z.Name;
            }
            return "";
        }

        static string MissionTitle()
        {
            if (!cfg.CharacterEnabled || GameData.Base() == 0) return "";
            string s = GameData.ReadString(GameData.Base() + (long)cfg.MissionTitleOffset, cfg.MissionTitleLen);
            return s.Trim();
        }

        static string CurrentCharacter(string title)
        {
            if (GameData.Base() == 0) return cfg.CharacterDefault;
            int ep = GameData.ReadInt(0xd73240);
            switch (ep)
            {
                case 0: return "Niko Belic";
                case 1: return "Johnny Klebitz";
                case 2: return "Luis Fernando Lopez";
            }
            return cfg.CharacterDefault;
        }

        static int CheckPlayerPed(int ptrOff)
        {
            int p = GameData.ReadIntAbs(GameData.Base() + (long)ptrOff);
            if (p < 0x1E000000 || p > 0x2E000000) return 0;
            float hg = GameData.ReadFloat(cfg.HealthOffset);
            if (float.IsNaN(hg) || hg < 0 || hg > 400) return p;
            float ph = GameData.ReadFloatAbs(p + (long)cfg.PedHealthField);
            if (float.IsNaN(ph)) return 0;
            if (Math.Abs((double)(ph - hg)) <= (double)cfg.HealthMatchTolerance) return p;
            return 0;
        }

        static int PlayerPed()
        {
            if (GameData.Base() == 0) return 0;
            int carPtr = GameData.ReadIntAbs(GameData.Base() + (long)cfg.VehiclePtrOffset);
            if (carPtr > 0x10000 && carPtr < 0x7E000000)
            {
                int d = GameData.ReadIntAbs(carPtr + (long)cfg.DriverPedOffset);
                if (d > 0x10000 && d < 0x7E000000) return d;
            }
            if (cfg.PlayerPedPtrOffset > 0)
            {
                int p = CheckPlayerPed(cfg.PlayerPedPtrOffset);
                if (p != 0) return p;
            }
            if (cfg.PlayerPedPtrOffsets != null && cfg.PlayerPedPtrOffsets.Length > 0)
            {
                string[] parts = cfg.PlayerPedPtrOffsets.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string s in parts)
                {
                    int off = 0;
                    if (int.TryParse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out off) && off > 0)
                    {
                        int p = CheckPlayerPed(off);
                        if (p != 0) return p;
                    }
                }
            }
            return 0;
        }

        static string CurrentWeapon()
        {
            if (!cfg.WeaponEnabled || GameData.Base() == 0) return "";
            int ped = PlayerPed();
            if (ped == 0) return "";
            int w = GameData.ReadIntAbs(ped + (long)cfg.WeaponSlotOffset);
            if (w < 0) return "";
            string name;
            if (!WeaponNames.TryGetValue(w, out name)) name = "Weapon " + w;
            int ammo = GameData.ReadIntAbs(ped + (long)cfg.WeaponAmmoOffset);
            string line = cfg.WeaponText.Replace("{weapon}", name).Replace("{ammo}", ammo < 0 ? "?" : ammo.ToString());
            return line;
        }

        static string MissionName(string title)
        {
            int idx = title.IndexOf(" - ");
            if (idx >= 0) {
                string ep = title.Substring(0, idx).Trim();
                string nm = title.Substring(idx + 3).Trim();
                if (nm.Length == 0) return "";
                string epU = ep.ToUpperInvariant();
                if (epU == "TBOGT" || epU == "TLAD" || epU == "GTA IV" || epU == "GTAIV" ||
                    epU.Contains("LOST AND DAMNED") || epU.Contains("BALLAD"))
                    return nm;
                return title.Trim();
            }
            return title.Trim();
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
                cfg.FixedState           = JsonGet(json, "FixedState", cfg.FixedState);
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
                int vp = 0;
                if (int.TryParse(JsonGet(json, "VehiclePtrOffset", cfg.VehiclePtrOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vp) && vp != 0)
                    cfg.VehiclePtrOffset = vp;
                int vmo = 0;
                if (int.TryParse(JsonGet(json, "VehicleModelOffset", cfg.VehicleModelOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vmo) && vmo != 0)
                    cfg.VehicleModelOffset = vmo;
                int vmno = 0;
                if (int.TryParse(JsonGet(json, "VehicleModelNameOffset", cfg.VehicleModelNameOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vmno) && vmno != 0)
                    cfg.VehicleModelNameOffset = vmno;
                int vnl = 0;
                if (int.TryParse(JsonGet(json, "VehicleNameLen", cfg.VehicleNameLen.ToString()), NumberStyles.Integer, CultureInfo.InvariantCulture, out vnl) && vnl > 0 && vnl <= 64)
                    cfg.VehicleNameLen = vnl;
                int vpo = 0;
                if (int.TryParse(JsonGet(json, "VehiclePosOffset", cfg.VehiclePosOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vpo) && vpo != 0)
                    cfg.VehiclePosOffset = vpo;
                cfg.VehicleText    = JsonGet(json, "VehicleText", cfg.VehicleText);
                cfg.JoinSeparator  = JsonGet(json, "JoinSeparator", cfg.JoinSeparator);
                int ppo = 0;
                if (int.TryParse(JsonGet(json, "PlayerPosOffset", cfg.PlayerPosOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ppo) && ppo != 0)
                    cfg.PlayerPosOffset = ppo;
                string zen = JsonGet(json, "ZoneEnabled", cfg.ZoneEnabled ? "1" : "0");
                cfg.ZoneEnabled = zen == "1" || zen.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.ZoneText    = JsonGet(json, "ZoneText", cfg.ZoneText);
                int mto = 0;
                if (int.TryParse(JsonGet(json, "MissionTitleOffset", cfg.MissionTitleOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mto) && mto != 0)
                    cfg.MissionTitleOffset = mto;
                int mtn = 0;
                if (int.TryParse(JsonGet(json, "MissionTitleLen", cfg.MissionTitleLen.ToString()), NumberStyles.Integer, CultureInfo.InvariantCulture, out mtn) && mtn > 0 && mtn <= 256)
                    cfg.MissionTitleLen = mtn;
                string cen = JsonGet(json, "CharacterEnabled", cfg.CharacterEnabled ? "1" : "0");
                cfg.CharacterEnabled = cen == "1" || cen.Equals("true", StringComparison.OrdinalIgnoreCase);
                cfg.CharacterText    = JsonGet(json, "CharacterText", cfg.CharacterText);
                cfg.CharacterDefault = JsonGet(json, "CharacterDefault", cfg.CharacterDefault);
                string wen = JsonGet(json, "WeaponEnabled", cfg.WeaponEnabled ? "1" : "0");
                cfg.WeaponEnabled = wen == "1" || wen.Equals("true", StringComparison.OrdinalIgnoreCase);
                int wslot = 0;
                if (int.TryParse(JsonGet(json, "WeaponSlotOffset", cfg.WeaponSlotOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out wslot) && wslot != 0)
                    cfg.WeaponSlotOffset = wslot;
                int wammo = 0;
                if (int.TryParse(JsonGet(json, "WeaponAmmoOffset", cfg.WeaponAmmoOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out wammo) && wammo != 0)
                    cfg.WeaponAmmoOffset = wammo;
                int ppdo = 0;
                if (int.TryParse(JsonGet(json, "PlayerPedPtrOffset", cfg.PlayerPedPtrOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ppdo) && ppdo != 0)
                    cfg.PlayerPedPtrOffset = ppdo;
                cfg.PlayerPedPtrOffsets = JsonGet(json, "PlayerPedPtrOffsets", cfg.PlayerPedPtrOffsets);
                int dvo = 0;
                if (int.TryParse(JsonGet(json, "DriverPedOffset", cfg.DriverPedOffset.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dvo))
                    cfg.DriverPedOffset = dvo;
                int phf = 0;
                if (int.TryParse(JsonGet(json, "PedHealthField", cfg.PedHealthField.ToString("x")), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out phf) && phf != 0)
                    cfg.PedHealthField = phf;
                float hmt = 0;
                if (float.TryParse(JsonGet(json, "HealthMatchTolerance", cfg.HealthMatchTolerance.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out hmt) && hmt >= 0)
                    cfg.HealthMatchTolerance = hmt;
                cfg.WeaponText = JsonGet(json, "WeaponText", cfg.WeaponText);
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
                            lastModel = "";
                            lastSpeedMph = -2;
                            lastPosMs = 0;
                            lastZone = "";
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
                            lastModel = "";
                            lastSpeedMph = -2;
                            lastPosMs = 0;
                            lastZone = "";
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