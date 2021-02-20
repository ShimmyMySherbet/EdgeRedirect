using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Serilog;
using Serilog.Core;

namespace EdgeRedirect
{
    internal static class Program
    {
        public static Logger Logger;

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int system(string format);

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [STAThread]
        private static void Main()
        {
            Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateLogger();
            Logger.Information("Creating WMI Watcher...");
            string queryString = "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'msedge.exe'";
            ManagementEventWatcher watcher = new ManagementEventWatcher(new WqlEventQuery(queryString));
            watcher.EventArrived += new EventArrivedEventHandler(OnProcessStart);
            watcher.Start();
            Logger.Information("WMI Watcher active.");
            Thread.Sleep(-1);
        }

        public static void OnProcessStart(object sender, EventArrivedEventArgs e)
        {
            if (e.NewEvent.Properties["ProcessName"].Value.ToString().ToLower() == "msedge.exe")
            {
                int ProcID = int.Parse(e.NewEvent.Properties["ProcessId"].Value.ToString());
                string CommandLine = GetCommandLine(ProcID);
                if (!string.IsNullOrEmpty(CommandLine))
                {
                    string[] array = (from Match m in Regex.Matches(CommandLine, "[\\\"](.+?)[\\\"]|([^ ]+)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled)
                                      select m.Value.Trim('"').Trim()).ToArray();


                    if (array.Length > 2 && string.Equals(array[1], "--single-argument", StringComparison.InvariantCultureIgnoreCase))
                    {
                        Logger.Debug("EDGE PROCESS START. PROCESS ID: {pid}", ProcID);
                        Logger.Debug("Command Line: {com}", array);
                        string arg = array[2];
                        string slipUrl = null;
                        if (arg.StartsWith("microsoft-edge:"))
                        {
                            slipUrl = arg.Remove(0, 15); ;
                        }
                        Logger.Debug("Launched via windows start! sending kill...");
                        // kill process hierarchy
                        system($"Taskkill /f /t /PID {ProcID}");
                        Logger.Debug("Kill code sent!");

                        Dictionary<string, string> ProcessArgs = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

                        foreach (var a in arg.Split('&'))
                            if (a.Contains('='))
                            {
                                var b = a.Split('=');
                                ProcessArgs.Add(WebUtility.UrlDecode(b[0]), WebUtility.UrlDecode(b[1]));
                            }

                        string Parsed = WebUtility.UrlDecode(arg);
                        if (ProcessArgs.ContainsKey("url") || slipUrl != null)
                        {
                            string url = slipUrl != null ? slipUrl : ProcessArgs["url"];

                            Logger.Debug("Target URL: {url}", url);
                            string redirectURL = null;

                            if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri uri))
                            {
                                if (string.Equals(uri.Host, "www.bing.com", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    Dictionary<string, string> URLParameters = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                                    foreach (var qprt in url.Replace('?', '&').Split('&').Where(x => x.Contains('=')))
                                    {
                                        var qparts = qprt.Split('=');
                                        string key = WebUtility.UrlDecode(qparts[0]);
                                        string value = WebUtility.UrlDecode(qparts[1]);
                                        URLParameters.Add(key, value);
                                    }
                                    if (URLParameters.ContainsKey("q"))
                                    {
                                        string encodedSearch = WebUtility.UrlEncode(URLParameters["q"]);
                                        redirectURL = $"https://www.google.com/search?q={encodedSearch}";
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(redirectURL))
                            {
                                redirectURL = url;
                            }

                            if (Uri.TryCreate(redirectURL, UriKind.Absolute, out Uri redirectURI))
                            {
                                if (redirectURI.Scheme.Equals("http", StringComparison.InvariantCultureIgnoreCase) || redirectURI.Scheme.Equals("https", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    Logger.Information("Starting url: {url}", redirectURL);
                                    Process.Start(redirectURI.ToString());
                                }
                            }

                        }
                    }
                }
            }
        }

        private static string GetCommandLine(int ProcessID)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + ProcessID))
            using (ManagementObjectCollection objects = searcher.Get())
            {
                return objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
            }
        }
    }
}