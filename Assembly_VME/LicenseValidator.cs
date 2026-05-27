using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

public class LicenseValidator
{
    // ── UPDATE THIS URL with your new deployed URL ──────────────────────────
    private const string SheetApiUrl =
        "https://script.google.com/macros/s/AKfycbyt3FCq5cCDq8iUOi3jmyHHxBr1tjsu9Es3FOUigGENqh0bEoq5Fav1-0-98lKi7dgs/exec";

    private const int TrialDays = 60;

    // ─── MAC Detection ────────────────────────────────────────────────────────
    public static string GetMacAddress()
    {
        var mac = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                     && !n.Description.ToLower().Contains("virtual")
                     && !n.Description.ToLower().Contains("vmware")
                     && !n.Description.ToLower().Contains("vbox")
                     && !n.Description.ToLower().Contains("pseudo"))
            .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .Select(n => n.GetPhysicalAddress().ToString())
            .FirstOrDefault(m => m != null && m.Length == 12);

        if (mac != null)
            mac = string.Join("-", Enumerable.Range(0, 6)
                .Select(i => mac.Substring(i * 2, 2)));

        return mac ?? "UNKNOWN";
    }

    // ─── Internet Check ───────────────────────────────────────────────────────
    public static bool HasInternet(int timeoutMs = 8000)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create("http://www.google.com/");
            req.KeepAlive = false;
            req.Timeout = timeoutMs;
            using (var res = (HttpWebResponse)req.GetResponse())
                return true;
        }
        catch { return false; }
    }

    // ─── Main Entry Point ─────────────────────────────────────────────────────
    public static bool IsLicensed()
    {
        string mac = GetMacAddress();
        string user = Environment.UserName;
        string today = DateTime.Now.ToString("MM/dd/yyyy");

        try
        {
            if (!HasInternet())
                return OfflineCheck();

            // Step 1 — Check if MAC exists in sheet
            string checkUrl = $"{SheetApiUrl}?action=check&mac={Uri.EscapeDataString(mac)}";
            string raw;
            using (var client = new WebClient())
                raw = client.DownloadString(checkUrl);

            var json = JObject.Parse(raw);
            string statusVal = json["status"]?.Value<string>() ?? "";

            if (statusVal == "new")
            {
                // New user — register automatically
                string registerUrl =
                    $"{SheetApiUrl}" +
                    $"?action=register" +
                    $"&mac={Uri.EscapeDataString(mac)}" +
                    $"&username={Uri.EscapeDataString(user)}" +
                    $"&region=India" +
                    $"&installDate={Uri.EscapeDataString(today)}" +
                    $"&validDays={TrialDays}";

                using (var client = new WebClient())
                    client.DownloadString(registerUrl);

                LocalRegistry.WriteFirstUse(today, TrialDays);

                TaskDialog.Show("Welcome!",
                    $"Welcome {user}!\n\n" +
                    $"Your {TrialDays}-day trial has started.\n" +
                    $"MAC: {mac}");

                return true;
            }
            else if (statusVal == "found")
            {
                bool allowed = json["allowed"]?.Value<bool>() == true;
                int validity = json["validDays"]?.Value<int>() ?? TrialDays;
                string installDate = json["installDate"]?.Value<string>() ?? today;

                if (!allowed)
                {
                    TaskDialog.Show("License Blocked",
                        "Your license has been blocked.\n" +
                        "Please contact Thivagar@in9plus.com");
                    return false;
                }

                string[] formats = { "MM/dd/yyyy", "MM-dd-yyyy", "dd/MM/yyyy",
                                     "M/d/yyyy",   "yyyy-MM-dd" };
                DateTime install = DateTime.ParseExact(
                    installDate.Trim(), formats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None);

                int elapsed = (int)(DateTime.Now.Date - install).TotalDays;
                int remaining = validity - elapsed;

                if (remaining <= 0)
                {
                    TaskDialog.Show("License Expired",
                        "Your trial has expired.\n" +
                        "Please contact Thivagar@in9plus.com");
                    return false;
                }

                // Update last seen date
                string updateUrl =
                    $"{SheetApiUrl}" +
                    $"?action=updateLastSeen" +
                    $"&mac={Uri.EscapeDataString(mac)}" +
                    $"&lastSeen={Uri.EscapeDataString(today)}";
                using (var client = new WebClient())
                    client.DownloadString(updateUrl);

                LocalRegistry.WriteFirstUse(installDate, validity);

                // Show warning when less than 10 days remaining
                if (remaining <= 10)
                {
                    TaskDialog.Show("License Expiring Soon",
                        $"Your license expires in {remaining} day(s).\n" +
                        "Please contact Thivagar@in9plus.com to renew.");
                }

                return true;
            }

            // Unexpected response — allow with offline fallback
            return OfflineCheck();
        }
        catch (Exception ex)
        {
            // Any error — fall back to offline registry check
            return OfflineCheck();
        }
    }

    // ─── Offline Fallback ─────────────────────────────────────────────────────
    private static bool OfflineCheck()
    {
        if (LocalRegistry.IsBlacklisted())
        {
            TaskDialog.Show("License Blocked",
                "Your license has been blocked.\n" +
                "Please contact Thivagar@in9plus.com");
            return false;
        }

        string installDate = LocalRegistry.GetInstallDate();
        if (installDate == null)
        {
            // First run with no internet — give benefit of the doubt
            LocalRegistry.WriteFirstUse(DateTime.Now.ToString("MM/dd/yyyy"), TrialDays);
            return true;
        }

        string result = LocalRegistry.EvaluateDays();

        if (result == "Expired")
        {
            TaskDialog.Show("License Expired",
                "Your trial has expired.\n" +
                "Please contact Thivagar@in9plus.com");
            return false;
        }
        else if (result == "Error")
        {
            TaskDialog.Show("License Error",
                "License check failed (clock tampering detected).\n" +
                "Please contact Thivagar@in9plus.com");
            return false;
        }

        int remaining = int.Parse(result);
        if (remaining <= 10)
        {
            TaskDialog.Show("License Expiring Soon",
                $"Your license expires in {remaining} day(s).\n" +
                "Please contact Thivagar@in9plus.com to renew.");
        }

        return true;
    }
}

// ─── Local Registry ───────────────────────────────────────────────────────────
public static class LocalRegistry
{
    private static readonly string RegPath =
        @"SOFTWARE\WOW6432Node\{A93736D1-C28E-4392-A403-3FAB071D0F5A}";

    private static readonly string[] DateFormats =
        { "MM/dd/yyyy", "MM-dd-yyyy", "dd/MM/yyyy", "M/d/yyyy", "yyyy-MM-dd" };

    public static void WriteFirstUse(string installDate, int trialDays)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
        {
            if (key.GetValue("Install") == null)
                key.SetValue("Install", installDate);
            key.SetValue("Use", DateTime.Now.ToString("MM/dd/yyyy"));
            key.SetValue("FreePeriod", trialDays);
        }
    }

    public static string GetInstallDate()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
            return key.GetValue("Install") as string;
    }

    public static string EvaluateDays()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
        {
            string installStr = key.GetValue("Install") as string;
            string lastUseStr = key.GetValue("Use") as string;
            int trialDays = Convert.ToInt32(key.GetValue("FreePeriod") ?? 60);

            if (installStr == null || lastUseStr == null)
                return "Error";

            DateTime now = DateTime.Now.Date;
            DateTime install = DateTime.ParseExact(
                installStr.Trim(), DateFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None);
            DateTime lastUse = DateTime.ParseExact(
                lastUseStr.Trim(), DateFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None);

            int elapsed = (int)(now - install).TotalDays;
            int sinceLastUse = (int)(now - lastUse).TotalDays;

            key.SetValue("Use", now.ToString("MM/dd/yyyy"));

            if (sinceLastUse < 0 || elapsed < 0) return "Error";
            if (elapsed > trialDays) return "Expired";
            return Convert.ToString(trialDays - elapsed);
        }
    }

    public static bool IsBlacklisted()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
            return key.GetValue("Black") != null;
    }

    public static void Blacklist()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
            key.SetValue("Black", "True");
    }
}