using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NetworkTools
{
    public class AddrInfo
    {
        public string Ip;
        public int Prefix;

        public string Mask { get { return IpCalc.ToStr(IpCalc.MaskFromPrefix(Prefix)); } }
        public override string ToString() { return Ip + "/" + Prefix + "    (" + Mask + ")"; }
    }

    public class AdapterInfo
    {
        public string Id, Name, Description, Mac;
        public OperationalStatus Status;
        public NetworkInterfaceType Type;
        public int Index;
        public bool Dhcp;
        public bool FromRegistry;   // addresses read from saved config (adapter not connected)
        public List<AddrInfo> Addrs = new List<AddrInfo>();
        public List<string> Gateways = new List<string>();
        public List<string> Dns = new List<string>();

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case OperationalStatus.Up: return "已連線";
                    case OperationalStatus.Down: return "已中斷";
                    case OperationalStatus.Dormant: return "休眠";
                    case OperationalStatus.NotPresent: return "不存在";
                    default: return Status.ToString();
                }
            }
        }

        public string TypeText
        {
            get
            {
                switch (Type)
                {
                    case NetworkInterfaceType.Ethernet:
                    case NetworkInterfaceType.GigabitEthernet:
                    case NetworkInterfaceType.FastEthernetT:
                    case NetworkInterfaceType.FastEthernetFx:
                        return "乙太網路";
                    case NetworkInterfaceType.Wireless80211: return "Wi-Fi";
                    case NetworkInterfaceType.Ppp: return "PPP";
                    default: return "其他";
                }
            }
        }

        public string IpSummary
        {
            get
            {
                if (Dhcp && Addrs.Count == 0) return "DHCP";
                return (Dhcp ? "DHCP: " : "") + string.Join(", ", Addrs.Select(a => a.Ip + "/" + a.Prefix));
            }
        }

        public static List<AdapterInfo> GetAll()
        {
            var list = new List<AdapterInfo>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (!ni.Supports(NetworkInterfaceComponent.IPv4)) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    IPv4InterfaceProperties v4 = props.GetIPv4Properties();
                    if (v4 == null) continue;

                    var a = new AdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        Status = ni.OperationalStatus,
                        Type = ni.NetworkInterfaceType,
                        Index = v4.Index,
                        Dhcp = v4.IsDhcpEnabled
                    };
                    a.Mac = string.Join("-", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        int prefix = 0;
                        try { prefix = ua.PrefixLength; } catch { }
                        if (prefix <= 0 && ua.IPv4Mask != null)
                        {
                            uint m;
                            if (IpCalc.TryParseIPv4(ua.IPv4Mask.ToString(), out m)) IpCalc.TryMaskToPrefix(m, out prefix);
                        }
                        a.Addrs.Add(new AddrInfo { Ip = ua.Address.ToString(), Prefix = prefix });
                    }
                    foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                    {
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && g.Address.ToString() != "0.0.0.0")
                            a.Gateways.Add(g.Address.ToString());
                    }
                    foreach (IPAddress dns in props.DnsAddresses)
                    {
                        if (dns.AddressFamily == AddressFamily.InterNetwork) a.Dns.Add(dns.ToString());
                    }

                    // A disconnected adapter reports no addresses: show its saved config from the registry instead.
                    if (a.Addrs.Count == 0) FillFromRegistry(a);
                    list.Add(a);
                }
                catch { }
            }
            return list;
        }

        /// <summary>Manually configured DNS servers (empty = DNS comes from DHCP or none).</summary>
        public static List<string> StaticDns(string id)
        {
            var list = new List<string>();
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + id))
                {
                    string ns = k == null ? null : k.GetValue("NameServer") as string;
                    if (!string.IsNullOrEmpty(ns))
                        list.AddRange(ns.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            catch { }
            return list;
        }

        static void FillFromRegistry(AdapterInfo a)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + a.Id))
                {
                    if (k == null) return;
                    object dhcp = k.GetValue("EnableDHCP");
                    if (dhcp is int) a.Dhcp = (int)dhcp != 0;
                    if (a.Dhcp) return;

                    string[] ips = k.GetValue("IPAddress") as string[];
                    string[] masks = k.GetValue("SubnetMask") as string[];
                    if (ips != null)
                    {
                        for (int i = 0; i < ips.Length; i++)
                        {
                            if (ips[i] == "0.0.0.0" || ips[i].Length == 0) continue;
                            int prefix = 24;
                            uint m;
                            if (masks != null && i < masks.Length && IpCalc.TryParseIPv4(masks[i], out m))
                                IpCalc.TryMaskToPrefix(m, out prefix);
                            a.Addrs.Add(new AddrInfo { Ip = ips[i], Prefix = prefix });
                        }
                    }
                    string[] gws = k.GetValue("DefaultGateway") as string[];
                    if (gws != null && a.Gateways.Count == 0)
                        a.Gateways.AddRange(gws.Where(g => g.Length > 0 && g != "0.0.0.0"));
                    string ns = k.GetValue("NameServer") as string;
                    if (!string.IsNullOrEmpty(ns) && a.Dns.Count == 0)
                        a.Dns.AddRange(ns.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    if (a.Addrs.Count > 0) a.FromRegistry = true;
                }
            }
            catch { }
        }
    }

    public static class Netsh
    {
        [DllImport("kernel32.dll")]
        static extern uint GetOEMCP();

        public static string Run(string args, out int exitCode)
        {
            var psi = new ProcessStartInfo("netsh.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Encoding enc;
            try { enc = Encoding.GetEncoding((int)GetOEMCP()); } catch { enc = Encoding.Default; }
            psi.StandardOutputEncoding = enc;
            psi.StandardErrorEncoding = enc;
            using (Process p = Process.Start(psi))
            {
                var err = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                exitCode = p.ExitCode;
                lock (err) return (o + Environment.NewLine + err).Trim();
            }
        }
    }
}
