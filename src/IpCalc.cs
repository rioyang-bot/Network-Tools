using System;

namespace NetworkTools
{
    /// <summary>IPv4 / CIDR helper functions.</summary>
    public static class IpCalc
    {
        public static bool TryParseIPv4(string s, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Trim().Split('.');
            if (parts.Length != 4) return false;
            foreach (string p in parts)
            {
                if (p.Length == 0 || p.Length > 3) return false;
                foreach (char c in p) if (c < '0' || c > '9') return false;
                int n = int.Parse(p);
                if (n > 255) return false;
                value = (value << 8) | (uint)n;
            }
            return true;
        }

        public static string ToStr(uint v)
        {
            return ((v >> 24) & 255) + "." + ((v >> 16) & 255) + "." + ((v >> 8) & 255) + "." + (v & 255);
        }

        public static uint MaskFromPrefix(int prefix)
        {
            if (prefix <= 0) return 0u;
            if (prefix >= 32) return 0xFFFFFFFFu;
            return 0xFFFFFFFFu << (32 - prefix);
        }

        public static bool TryMaskToPrefix(uint mask, out int prefix)
        {
            prefix = 0;
            uint m = mask;
            while ((m & 0x80000000u) != 0) { prefix++; m <<= 1; }
            return m == 0; // mask must be contiguous ones
        }

        /// <summary>
        /// Accepts "ip/24", "ip/255.255.255.0", "ip 255.255.255.0" or just "ip" (defaults to /24).
        /// </summary>
        public static bool TryParseCidr(string input, out uint ip, out int prefix, out string error)
        {
            ip = 0; prefix = 24; error = null;
            if (input == null || input.Trim().Length == 0) { error = "請輸入 IP，例如 192.168.100.100/24"; return false; }
            string[] parts = input.Trim().Split(new[] { '/', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2) { error = "格式錯誤，例如 192.168.100.100/24"; return false; }
            if (!TryParseIPv4(parts[0], out ip)) { error = "IP 位址格式錯誤"; return false; }
            if (parts.Length == 2)
            {
                string m = parts[1];
                if (m.IndexOf('.') >= 0)
                {
                    uint mask;
                    if (!TryParseIPv4(m, out mask) || !TryMaskToPrefix(mask, out prefix) || prefix == 0)
                    { error = "子網路遮罩格式錯誤"; return false; }
                }
                else
                {
                    int p;
                    if (!int.TryParse(m, out p) || p < 1 || p > 32) { error = "遮罩長度需為 1 ~ 32"; return false; }
                    prefix = p;
                }
            }
            return true;
        }

        public static bool HasExplicitMask(string input)
        {
            if (input == null) return false;
            return input.Trim().Split(new[] { '/', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length == 2;
        }

        public static uint Network(uint ip, int prefix) { return ip & MaskFromPrefix(prefix); }
        public static uint Broadcast(uint ip, int prefix) { return Network(ip, prefix) | ~MaskFromPrefix(prefix); }

        /// <summary>Default gateway = last usable host of the subnet (broadcast - 1).</summary>
        public static string SuggestGateway(uint ip, int prefix)
        {
            if (prefix > 30) return "";
            uint net = Network(ip, prefix);
            uint gw = Broadcast(ip, prefix) - 1;
            if (gw == ip) gw = net + 1;   // own IP is the last host -> use the first host instead
            if (gw == ip) return "";
            return ToStr(gw);
        }
    }
}
