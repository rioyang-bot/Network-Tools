using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NetworkTools
{
    // PuTTY serial (COM port) console connection on the Ping / PuTTY page.
    public partial class MainForm
    {
        Label lblPort, lblUser, lblCom, lblBaud;
        InputField fldCom, fldBaud;

        static readonly string[] BaudRates = { "9600", "115200", "19200", "38400", "57600", "4800", "2400", "230400" };

        void BuildSerialFields(FlowLayoutPanel row)
        {
            lblCom = FlowLabel("COM 埠");
            row.Controls.Add(lblCom);
            fldCom = new InputField(true, 10.5f) { Width = S(130), Height = S(36), Placeholder = "例：COM3" };
            fldCom.Box.Text = cfg.Get("SerialPort", "");
            fldCom.Choices = ComChoices;
            row.Controls.Add(fldCom);

            lblBaud = FlowLabel("鮑率");
            row.Controls.Add(lblBaud);
            fldBaud = new InputField(true, 10.5f) { Width = S(112), Height = S(36) };
            fldBaud.Box.Text = cfg.Get("SerialBaud", "9600");
            fldBaud.Choices = () => BaudRates.Select(b => new[] { b + " bps", b }).ToList();
            row.Controls.Add(fldBaud);

            var tip = new ToolTip { AutoPopDelay = 15000 };
            tip.SetToolTip(fldCom.Box, "按右側 ▾ 列出這台電腦的 COM 埠 (USB 轉 RS232 線插上後會出現)");
            tip.SetToolTip(fldBaud.Box, "Cisco / HPE / Aruba 等多數設備的 Console 為 9600；部分設備為 115200\n資料格式固定 8N1、無流量控制");
        }

        void ApplyProtoUi()
        {
            bool serial = segProto.SelectedText == "Serial";
            lblPort.Visible = fldPort.Visible = lblUser.Visible = fldUser.Visible = !serial;
            lblCom.Visible = fldCom.Visible = lblBaud.Visible = fldBaud.Visible = serial;
            if (serial && fldCom.Box.Text.Trim().Length == 0)
            {
                List<string[]> ports = ComChoices();
                if (ports.Count == 1) fldCom.Box.Text = ports[0][1];
            }
        }

        // {display, value} pairs, e.g. {"COM3   USB-SERIAL CH340", "COM3"}
        static List<string[]> ComChoices()
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (System.Management.ManagementBaseObject o in s.Get())
                    {
                        string name = Convert.ToString(o["Name"]);
                        Match m = Regex.Match(name ?? "", @"\((COM\d+)\)");
                        if (m.Success) found[m.Groups[1].Value] = name.Replace(m.Value, "").Trim();
                    }
                }
            }
            catch { }
            try
            {
                foreach (string p in System.IO.Ports.SerialPort.GetPortNames())
                    if (!found.ContainsKey(p)) found[p] = "";
            }
            catch { }
            return found.OrderBy(kv => ComNumber(kv.Key))
                        .Select(kv => new[] { kv.Key + (kv.Value.Length > 0 ? "    " + kv.Value : ""), kv.Key.ToUpperInvariant() })
                        .ToList();
        }

        static int ComNumber(string com)
        {
            int n;
            return int.TryParse(com.Substring(3), out n) ? n : 9999;
        }

        void LaunchSerial()
        {
            string com = fldCom.Box.Text.Trim().ToUpperInvariant();
            if (Regex.IsMatch(com, @"^\d+$")) com = "COM" + com;
            if (!Regex.IsMatch(com, @"^COM\d+$"))
            {
                Warn("請輸入或選擇 COM 埠，例如 COM3。\n(按右側 ▾ 可列出這台電腦的 COM 埠)");
                fldCom.Box.Focus();
                return;
            }
            int baud;
            string baudText = fldBaud.Box.Text.Trim().Replace("bps", "").Trim();
            if (!int.TryParse(baudText, out baud) || baud < 50 || baud > 4000000)
            {
                Warn("鮑率格式錯誤，例如 9600 或 115200。");
                fldBaud.Box.Focus();
                return;
            }
            List<string[]> ports = ComChoices();
            if (!ports.Any(c => c[1] == com) &&
                !Ask("這台電腦目前找不到 " + com + "。\n仍要用 PuTTY 開啟嗎？"))
                return;

            string exe = FindPutty();
            if (exe == null)
            {
                if (!Ask("找不到 putty.exe。\n是否手動選擇 PuTTY 的位置？")) return;
                exe = BrowsePutty();
                if (exe == null) return;
            }
            string args = "-serial " + com + " -sercfg " + baud + ",8,n,1,N";
            try
            {
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                fldCom.Box.Text = com;
                fldBaud.Box.Text = baud.ToString();
                AppendPing("啟動 PuTTY：putty " + args, TermInfo);
                banner.SetState("›", "已啟動 PuTTY  →  " + com, "Serial Console  ·  " + baud + " bps  ·  8N1  ·  無流量控制", Theme.Sky, false);
                SaveAll();
            }
            catch (Exception ex)
            {
                Warn("無法啟動 PuTTY：" + ex.Message);
            }
        }

        void SaveSerialSettings()
        {
            if (fldCom == null) return;
            cfg.Set("SerialPort", fldCom.Box.Text.Trim());
            cfg.Set("SerialBaud", fldBaud.Box.Text.Trim());
        }
    }
}
