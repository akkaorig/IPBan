﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using System.Management;
using System.Linq;

namespace IPBan
{
    // cat /etc/*-release
    public static class IPBanOS
    {
        /// <summary>
        /// Unknown operating system
        /// </summary>
        public const string Unknown = "Unknown";

        /// <summary>
        /// Windows
        /// </summary>
        public const string Windows = "Windows";

        /// <summary>
        /// Linux
        /// </summary>
        public const string Linux = "Linux";

        /// <summary>
        /// Macintosh / OS 10+
        /// </summary>
        public const string Mac = "Mac";

        /// <summary>
        /// Operating system name (i.e. Windows, Linux or OSX)
        /// </summary>
        public static string Name { get; private set; }

        /// <summary>
        /// Operating system version
        /// </summary>
        public static string Version { get; private set; }

        /// <summary>
        /// Operating system friendly/code name
        /// </summary>
        public static string FriendlyName { get; private set; }

        /// <summary>
        /// Operating system description
        /// </summary>
        public static string Description { get; private set; }

        private static string ExtractRegex(string input, string regex, string defaultValue)
        {
            Match m = Regex.Match(input, regex, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (m.Success)
            {
                return m.Groups["value"].Value.Trim('[', ']', '"', '\'', '(', ')', ' ', '\r', '\n', '\t');
            }
            return defaultValue;
        }

        static IPBanOS()
        {
            try
            {
                Version = Environment.OSVersion.VersionString;
                Description = RuntimeInformation.OSDescription;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string tempFile = Path.GetTempFileName();
                    Process.Start("/bin/bash", "-c \"cat /etc/*release* > " + tempFile + "\"").WaitForExit();
                    string versionText = File.ReadAllText(tempFile);
                    File.Delete(tempFile);
                    Name = IPBanOS.Linux;
                    FriendlyName = ExtractRegex(versionText, "^(Id|Distrib_Id)=(?<value>.*?)$", string.Empty);
                    if (FriendlyName.Length != 0)
                    {
                        string codeName = ExtractRegex(versionText, "^(Name|Distrib_CodeName)=(?<value>.+)$", string.Empty);
                        if (codeName.Length != 0)
                        {
                            FriendlyName += " - " + codeName;
                        }
                        Version = ExtractRegex(versionText, "^Version_Id=(?<value>.+)$", Version);
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Name = IPBanOS.Windows;
                    string tempFile = Path.GetTempFileName();

                    // .net core WMI has a strange bug where WMI will not initialize on some systems
                    // since this is the only place where WMI is used, we can just work-around it
                    // with the wmic executable, which exists (as of 2018) on all supported Windows.
                    StartProcessAndWait("cmd", "/C wmic path Win32_OperatingSystem get Caption,Version /format:table > \"" + tempFile + "\"");
                    if (File.Exists(tempFile))
                    {
                        string[] lines = File.ReadAllLines(tempFile);
                        File.Delete(tempFile);
                        if (lines.Length > 1)
                        {
                            int versionIndex = lines[0].IndexOf("Version");
                            if (versionIndex >= 0)
                            {
                                FriendlyName = lines[1].Substring(0, versionIndex - 1).Trim();
                                Version = lines[1].Substring(versionIndex).Trim();
                            }
                        }
                    }
                    else
                    {
                        // fall-back to WMI, maybe future .NET core versions will fix the bug
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem"))
                        {
                            foreach (var result in searcher.Get())
                            {
                                FriendlyName = result["Caption"] as string;
                                Version = result["Version"] as string;
                                break;
                            }
                        }
                    }
                    }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // TODO: Implement better for MAC
                    Name = IPBanOS.Mac;
                    FriendlyName = "OSX";
                }
                else
                {
                    Name = IPBanOS.Unknown;
                    FriendlyName = "Unknown";
                }
            }
            catch (Exception ex)
            {
                IPBanLog.Error(ex);
            }
        }
        
        /// <summary>
        /// Get a string representing the operating system
        /// </summary>
        /// <returns>String</returns>
        public static string OSString()
        {
            return $"Name: {Name}, Version: {Version}, Friendly Name: {FriendlyName}, Description: {Description}";
        }

        /// <summary>
        /// Easy way to execute processes. Timeout to complete is 30 seconds.
        /// </summary>
        /// <param name="program">Program to run</param>
        /// <param name="args">Arguments</param>
        /// <param name="allowedExitCode">Allowed exit codes, if empty not checked, otherwise a mismatch will throw an exception.</param>
        public static void StartProcessAndWait(string program, string args, params int[] allowedExitCode)
        {
            IPBanLog.Write(LogLevel.Information, $"Executing process {program} {args}...");

            var p = new Process
            {
                StartInfo = new ProcessStartInfo(program, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas"
                }
            };
            p.Start();
            if (!p.WaitForExit(30000))
            {
                p.Kill();
            }
            if (allowedExitCode.Length != 0 && Array.IndexOf(allowedExitCode, p.ExitCode) < 0)
            {
                throw new ApplicationException($"Program {program} {args}: failed with exit code {p.ExitCode}");
            }
        }
    }

    /// <summary>
    /// Mark a class as requiring a specific operating system
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class RequiredOperatingSystemAttribute : Attribute
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="os">OS (IPBanOS.*)</param>
        public RequiredOperatingSystemAttribute(string os)
        {
            RequiredOS = os;
        }

        /// <summary>
        /// Whether the current OS is valid for this attribute
        /// </summary>
        public bool IsValid
        {
            get { return RequiredOS == IPBanOS.Name; }
        }

        /// <summary>
        /// The required operating system (IPBanOS.*)
        /// </summary>
        public string RequiredOS { get; private set; }
    }

    /// <summary>
    /// Apply a custom name to a class
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomNameAttribute : Attribute
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name">Custom name</param>
        public CustomNameAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Short name
        /// </summary>
        public string Name { get; set; }
    }
}
